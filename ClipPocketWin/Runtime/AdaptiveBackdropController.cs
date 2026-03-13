using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinRT;
using WinRT.Interop;

namespace ClipPocketWin.Runtime;

public sealed class AdaptiveBackdropController : IDisposable
{
    private const int SamplingGridRows = 5;
    private const int SamplingGridCols = 5;
    private const int ChunkSubSamplesPerAxis = 3;
    private const double ChunkTrimFraction = 0.05;
    private const double ChunkUpperPercentile = 0.78;
    private const double ChunkUpperPercentileWeight = 0.35;
    private const int OuterRingSampleSegments = 8;
    private const int OuterRingSampleInsetPixels = 2;
    private const int VkLButton = 0x01;
    private const short KeyPressedMask = unchecked((short)0x8000);
    private const uint InvalidColorRef = 0xFFFFFFFF;
    private const int DwmCompositionDelayMs = 60;

    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly AdaptiveBackdropOptions _options;
    private readonly ILogger? _logger;

    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _configurationSource;
    private DispatcherQueueTimer? _delayedReadabilityTimer;
    private DispatcherQueueTimer? _continuousReadabilityTimer;
    private double _smoothedBackdropLuminance;
    private bool _hasBackdropSample;
    private bool _isBackdropSampling;
    private bool _wasVisibleForSampling;
    private BackdropSampleSource _lastLoggedSampleSource = BackdropSampleSource.None;
    private DateTimeOffset _lastBackdropDiagnosticLogUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastBackdropFallbackWarningUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    private enum BackdropSampleSource
    {
        None,
        UnderWindow,
        OuterRing,
        Fallback
    }

    private readonly record struct BackdropSamplingDiagnostics(
        BackdropSampleSource Source,
        int ValidSamples,
        int CandidateWindowCount,
        int TouchedWindowCount);



    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public AdaptiveBackdropController(Window window, AppWindow appWindow, AdaptiveBackdropOptions options, ILogger? logger = null)
    {
        _window = window;
        _appWindow = appWindow;
        _options = options;
        _logger = logger;
        _smoothedBackdropLuminance = options.LuminanceFallbackValue;
    }

    public bool Initialize()
    {
        ThrowIfDisposed();

        if (!DesktopAcrylicController.IsSupported())
        {
            return false;
        }

        _configurationSource = new SystemBackdropConfiguration
        {
            Theme = SystemBackdropTheme.Dark,
            IsInputActive = true
        };

        _acrylicController = new DesktopAcrylicController
        {
            TintColor = _options.BaseTintColor,
            TintOpacity = _options.MinTintOpacity,
            LuminosityOpacity = _options.MinLuminosityOpacity,
            FallbackColor = Windows.UI.Color.FromArgb(
                _options.FallbackMinAlpha,
                _options.BaseTintColor.R,
                _options.BaseTintColor.G,
                _options.BaseTintColor.B)
        };

        _acrylicController.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        return true;
    }

    public void HandleAppWindowChanged(AppWindowChangedEventArgs args)
    {
        if (_acrylicController == null)
        {
            return;
        }

        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        if (!IsWindowVisibleForSampling())
        {
            return;
        }

        _wasVisibleForSampling = true;
        _ = RefreshBackdropProtectionAsync();
        TriggerDelayedReadabilityCheck(_options.PostMoveReadabilityDelaySeconds);
        EnsureContinuousReadabilityCheck();
    }

    public void HandleWindowActivated(WindowActivatedEventArgs args)
    {
        if (_configurationSource != null)
        {
            _configurationSource.IsInputActive = true;
        }

        bool isVisible = IsWindowVisibleForSampling();
        if (args.WindowActivationState == WindowActivationState.Deactivated || !isVisible)
        {
            StopContinuousReadabilityCheck();
            _wasVisibleForSampling = false;
            return;
        }

        EnsureContinuousReadabilityCheck();

        bool isJustShown = !_wasVisibleForSampling;
        _wasVisibleForSampling = true;
        if (!isJustShown)
        {
            return;
        }

        _hasBackdropSample = false;
        _ = RefreshBackdropProtectionAsync();
        TriggerDelayedReadabilityCheck(_options.PostShowReadabilityDelaySeconds);
    }

    public void ForceImmediateRefresh()
    {
        if (_acrylicController == null)
        {
            return;
        }

        _hasBackdropSample = false;
        _wasVisibleForSampling = true;
        _ = RefreshBackdropProtectionAsync();
        TriggerDelayedReadabilityCheck(_options.PostShowReadabilityDelaySeconds);
        EnsureContinuousReadabilityCheck();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _delayedReadabilityTimer?.Stop();
        _delayedReadabilityTimer = null;

        if (_continuousReadabilityTimer != null)
        {
            _continuousReadabilityTimer.Tick -= ContinuousReadabilityTimer_Tick;
            _continuousReadabilityTimer.Stop();
            _continuousReadabilityTimer = null;
        }

        // Ensure WDA is always restored to WDA_NONE to prevent ghost windows
        nint hWnd = WindowNative.GetWindowHandle(_window);
        if (hWnd != nint.Zero)
        {
            _ = SetWindowDisplayAffinity(hWnd, 0);
        }

        _acrylicController?.Dispose();
        _acrylicController = null;
        _configurationSource = null;
    }

    private void TriggerDelayedReadabilityCheck(double delaySeconds)
    {
        if (!IsWindowVisibleForSampling())
        {
            _delayedReadabilityTimer?.Stop();
            return;
        }

        if (_delayedReadabilityTimer == null)
        {
            _delayedReadabilityTimer = _window.DispatcherQueue.CreateTimer();
            _delayedReadabilityTimer.Tick += (s, e) =>
            {
                _delayedReadabilityTimer.Stop();
                if (IsWindowVisibleForSampling())
                {
                    _ = RefreshBackdropProtectionAsync();
                }
            };
        }

        _delayedReadabilityTimer.Stop();
        _delayedReadabilityTimer.Interval = TimeSpan.FromSeconds(delaySeconds);
        _delayedReadabilityTimer.Start();
    }

    private void EnsureContinuousReadabilityCheck()
    {
        if (!IsWindowVisibleForSampling())
        {
            _continuousReadabilityTimer?.Stop();
            return;
        }

        if (_continuousReadabilityTimer == null)
        {
            _continuousReadabilityTimer = _window.DispatcherQueue.CreateTimer();
            _continuousReadabilityTimer.Interval = TimeSpan.FromSeconds(_options.ContinuousReadabilityIntervalSeconds);
            _continuousReadabilityTimer.IsRepeating = true;
            _continuousReadabilityTimer.Tick += ContinuousReadabilityTimer_Tick;
        }

        if (!_continuousReadabilityTimer.IsRunning)
        {
            _continuousReadabilityTimer.Start();
        }
    }

    private void StopContinuousReadabilityCheck()
    {
        _continuousReadabilityTimer?.Stop();
    }

    private void ContinuousReadabilityTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!IsWindowVisibleForSampling())
        {
            sender.Stop();
            return;
        }

        _ = RefreshBackdropProtectionAsync();
    }

    private bool IsWindowVisibleForSampling()
    {
        if (_acrylicController == null)
        {
            return false;
        }

        nint hWnd = WindowNative.GetWindowHandle(_window);
        if (hWnd == nint.Zero)
        {
            return false;
        }

        return IsWindowVisible(hWnd);
    }

    private async System.Threading.Tasks.Task RefreshBackdropProtectionAsync()
    {
        if (_acrylicController == null || _isBackdropSampling || !IsWindowVisibleForSampling())
        {
            return;
        }

        nint windowHandle = WindowNative.GetWindowHandle(_window);
        if (windowHandle == nint.Zero)
        {
            return;
        }

        int left = _appWindow.Position.X;
        int top = _appWindow.Position.Y;
        int width = _appWindow.Size.Width;
        int height = _appWindow.Size.Height;

        double measuredLuminance;
        BackdropSamplingDiagnostics diagnostics;
        if (width <= 0 || height <= 0)
        {
            measuredLuminance = _options.LuminanceFallbackValue;
            diagnostics = new BackdropSamplingDiagnostics(BackdropSampleSource.Fallback, 0, 0, 0);
        }
        else
        {
            _isBackdropSampling = true;
            try
            {
                // UI THREAD: Safely apply window capture exclusion before background capture
                _ = SetWindowDisplayAffinity(windowHandle, 0x11); // WDA_EXCLUDEFROMCAPTURE

                // UI THREAD: Give DWM time to recompose asynchronously to not block UI
                await System.Threading.Tasks.Task.Delay(DwmCompositionDelayMs);

                // Check again in case window was closed or hidden during the delay
                if (IsWindowVisibleForSampling())
                {
                    // BACKGROUND THREAD: perform pixel sampling with GDI
                    (measuredLuminance, diagnostics) = await System.Threading.Tasks.Task.Run(() =>
                    {
                        if (TryMeasureBackdropLuminance(windowHandle, left, top, width, height, out double sampledLuminance, out BackdropSamplingDiagnostics sampledDiagnostics))
                        {
                            return (sampledLuminance, sampledDiagnostics);
                        }

                        return (_options.LuminanceFallbackValue, new BackdropSamplingDiagnostics(BackdropSampleSource.Fallback, 0, 0, 0));
                    });
                }
                else
                {
                    measuredLuminance = _options.LuminanceFallbackValue;
                    diagnostics = new BackdropSamplingDiagnostics(BackdropSampleSource.Fallback, 0, 0, 0);
                }
            }
            finally
            {
                // UI THREAD: Restore normal window capture properties
                _ = SetWindowDisplayAffinity(windowHandle, 0); // WDA_NONE
                _isBackdropSampling = false;
            }
        }

        if (_acrylicController == null)
        {
            return;
        }

        if (!_hasBackdropSample)
        {
            _smoothedBackdropLuminance = measuredLuminance;
            _hasBackdropSample = true;
        }
        else
        {
            double stabilizedMeasuredLuminance = StabilizeMeasuredLuminance(measuredLuminance, _smoothedBackdropLuminance, diagnostics);
            double smoothing = ResolveLuminanceSmoothing(_smoothedBackdropLuminance, stabilizedMeasuredLuminance, diagnostics);
            _smoothedBackdropLuminance = Lerp(_smoothedBackdropLuminance, stabilizedMeasuredLuminance, smoothing);
            measuredLuminance = stabilizedMeasuredLuminance;
        }

        ApplyBackdropReadability(_smoothedBackdropLuminance);
        LogBackdropSamplingDiagnostics(diagnostics, measuredLuminance, _smoothedBackdropLuminance);
    }

    private double StabilizeMeasuredLuminance(double measuredLuminance, double currentSmoothedLuminance, BackdropSamplingDiagnostics diagnostics)
    {
        if (measuredLuminance >= currentSmoothedLuminance)
        {
            return measuredLuminance;
        }

        bool reliableUnderWindowSample = IsReliableUnderWindowSample(diagnostics);
        bool isMouseButtonDown = (GetAsyncKeyState(VkLButton) & KeyPressedMask) != 0;
        if (!reliableUnderWindowSample)
        {
            return currentSmoothedLuminance;
        }

        if (isMouseButtonDown)
        {
            const double maxDropWhileDragging = 0.05;
            return Math.Max(measuredLuminance, currentSmoothedLuminance - maxDropWhileDragging);
        }

        return measuredLuminance;
    }

    private double ResolveLuminanceSmoothing(double currentSmoothedLuminance, double targetLuminance, BackdropSamplingDiagnostics diagnostics)
    {
        if (targetLuminance >= currentSmoothedLuminance)
        {
            return _options.LuminanceRiseSmoothing;
        }

        return IsReliableUnderWindowSample(diagnostics)
            ? _options.ReliableLuminanceDecaySmoothing
            : _options.UnreliableLuminanceDecaySmoothing;
    }

    private bool IsReliableUnderWindowSample(BackdropSamplingDiagnostics diagnostics)
    {
        return diagnostics.Source == BackdropSampleSource.UnderWindow
            && diagnostics.ValidSamples >= _options.MinimumReliableUnderWindowSamples
            && diagnostics.TouchedWindowCount > 0;
    }

    private void LogBackdropSamplingDiagnostics(BackdropSamplingDiagnostics diagnostics, double measuredLuminance, double smoothedLuminance)
    {
#if DEBUG
        if (_logger == null)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool sourceChanged = diagnostics.Source != _lastLoggedSampleSource;
        bool intervalElapsed = (now - _lastBackdropDiagnosticLogUtc).TotalSeconds >= _options.BackdropDiagnosticLogIntervalSeconds;
        if (sourceChanged || intervalElapsed)
        {
            _logger.LogDebug(
                "{DiagnosticName} backdrop source={Source}, effectiveMeasured={MeasuredLuminance:F3}, smoothed={SmoothedLuminance:F3}, validSamples={ValidSamples}, candidateWindows={CandidateWindows}, touchedWindows={TouchedWindows}, reliableUnderWindow={ReliableUnderWindow}",
                _options.DiagnosticName,
                diagnostics.Source,
                measuredLuminance,
                smoothedLuminance,
                diagnostics.ValidSamples,
                diagnostics.CandidateWindowCount,
                diagnostics.TouchedWindowCount,
                IsReliableUnderWindowSample(diagnostics));

            _lastBackdropDiagnosticLogUtc = now;
            _lastLoggedSampleSource = diagnostics.Source;
        }

        if (diagnostics.Source == BackdropSampleSource.Fallback
            && (now - _lastBackdropFallbackWarningUtc).TotalSeconds >= _options.BackdropFallbackWarningIntervalSeconds)
        {
            _logger.LogWarning(
                "{DiagnosticName} backdrop under-window sampling unavailable. Using fallback luminance. ValidSamples={ValidSamples}, CandidateWindows={CandidateWindows}",
                _options.DiagnosticName,
                diagnostics.ValidSamples,
                diagnostics.CandidateWindowCount);
            _lastBackdropFallbackWarningUtc = now;
        }
#endif
    }

    private void ApplyBackdropReadability(double luminance)
    {
        if (_acrylicController == null)
        {
            return;
        }

        double threshold = Math.Clamp(_options.LuminanceProtectionThreshold, 0d, 0.98d);
        double normalizedProtection = (luminance - threshold) / (1d - threshold);
        normalizedProtection = Math.Clamp(normalizedProtection, 0d, 1d);
        normalizedProtection = Math.Pow(normalizedProtection, _options.ProtectionCurveGamma);
        normalizedProtection = SmoothStep(normalizedProtection);

        float protection = (float)normalizedProtection;
        float contrastBoost = protection * _options.MaxAdditionalContrastTint;

        _acrylicController.TintColor = LerpColor(_options.BaseTintColor, _options.StrongProtectionTintColor, protection);
        _acrylicController.TintOpacity = Math.Clamp(Lerp(_options.MinTintOpacity, _options.MaxTintOpacity, protection) + contrastBoost, 0f, 1f);
        _acrylicController.LuminosityOpacity = Lerp(_options.MinLuminosityOpacity, _options.MaxLuminosityOpacity, protection);
        _acrylicController.FallbackColor = Windows.UI.Color.FromArgb(
            (byte)Lerp(_options.FallbackMinAlpha, _options.FallbackMaxAlpha, protection),
            _options.BaseTintColor.R,
            _options.BaseTintColor.G,
            _options.BaseTintColor.B);
    }

    private static bool TryMeasureBackdropLuminance(nint windowHandle, int left, int top, int width, int height, out double luminance, out BackdropSamplingDiagnostics diagnostics)
    {
        luminance = 0d;
        diagnostics = new BackdropSamplingDiagnostics(BackdropSampleSource.Fallback, 0, 0, 0);

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (TryMeasureUnderWindowLuminance(left, top, width, height, out double underWindowLuminance, out int underWindowSamples, out int candidateWindowCount, out int touchedWindowCount))
        {
            luminance = underWindowLuminance;
            diagnostics = new BackdropSamplingDiagnostics(BackdropSampleSource.UnderWindow, underWindowSamples, candidateWindowCount, touchedWindowCount);
            return true;
        }

        if (TryMeasureOuterRingLuminance(left, top, width, height, out double outerRingLuminance, out int outerRingSamples))
        {
            luminance = outerRingLuminance;
            diagnostics = new BackdropSamplingDiagnostics(BackdropSampleSource.OuterRing, outerRingSamples, 0, 0);
            return true;
        }

        return false;
    }

    private static bool TryMeasureUnderWindowLuminance(
        int left,
        int top,
        int width,
        int height,
        out double luminance,
        out int validSampleCount,
        out int candidateWindowCount,
        out int touchedWindowCount)
    {
        luminance = 0d;
        validSampleCount = 0;
        candidateWindowCount = 1;
        touchedWindowCount = 1;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        nint desktopDc = GetDC(nint.Zero);
        if (desktopDc == nint.Zero)
        {
            return false;
        }

        Span<double> chunkLuminances = stackalloc double[SamplingGridRows * SamplingGridCols];
        int validChunkCount = 0;

        try
        {
            for (int r = 0; r < SamplingGridRows; r++)
            {
                for (int c = 0; c < SamplingGridCols; c++)
                {
                    double chunkTotal = 0d;
                    int chunkSampleCount = 0;

                    for (int sy = 0; sy < ChunkSubSamplesPerAxis; sy++)
                    {
                        for (int sx = 0; sx < ChunkSubSamplesPerAxis; sx++)
                        {
                            double normalizedX = (c + ((sx + 0.5d) / ChunkSubSamplesPerAxis)) / SamplingGridCols;
                            double normalizedY = (r + ((sy + 0.5d) / ChunkSubSamplesPerAxis)) / SamplingGridRows;

                            int sampleX = left + (int)(width * normalizedX);
                            int sampleY = top + (int)(height * normalizedY);

                            uint colorRef = GetPixel(desktopDc, sampleX, sampleY);
                            if (colorRef == InvalidColorRef)
                            {
                                continue;
                            }

                            chunkTotal += ColorRefToLuminance(colorRef);
                            chunkSampleCount++;
                            validSampleCount++;
                        }
                    }

                    if (chunkSampleCount == 0)
                    {
                        continue;
                    }

                    chunkLuminances[validChunkCount++] = chunkTotal / chunkSampleCount;
                }
            }
        }
        finally
        {
            _ = ReleaseDC(nint.Zero, desktopDc);
        }

        if (validChunkCount == 0)
        {
            return false;
        }

        luminance = ComputeRobustChunkLuminance(chunkLuminances[..validChunkCount]);
        return true;
    }

    private static bool TryMeasureOuterRingLuminance(int left, int top, int width, int height, out double luminance, out int validSampleCount)
    {
        luminance = 0d;
        validSampleCount = 0;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        nint desktopDc = GetDC(nint.Zero);
        if (desktopDc == nint.Zero)
        {
            return false;
        }

        try
        {
            Span<double> ringSamples = stackalloc double[OuterRingSampleSegments * 4];

            for (int i = 0; i < OuterRingSampleSegments; i++)
            {
                double normalized = (i + 0.5d) / OuterRingSampleSegments;
                int horizontalX = left + (int)(width * normalized);
                int verticalY = top + (int)(height * normalized);

                AddDesktopSample(desktopDc, horizontalX, top - OuterRingSampleInsetPixels, ringSamples, ref validSampleCount);
                AddDesktopSample(desktopDc, horizontalX, top + height + OuterRingSampleInsetPixels, ringSamples, ref validSampleCount);
                AddDesktopSample(desktopDc, left - OuterRingSampleInsetPixels, verticalY, ringSamples, ref validSampleCount);
                AddDesktopSample(desktopDc, left + width + OuterRingSampleInsetPixels, verticalY, ringSamples, ref validSampleCount);
            }

            if (validSampleCount == 0)
            {
                return false;
            }

            luminance = ComputeRobustChunkLuminance(ringSamples[..validSampleCount]);
            return true;
        }
        finally
        {
            _ = ReleaseDC(nint.Zero, desktopDc);
        }
    }

    private static void AddDesktopSample(nint desktopDc, int x, int y, Span<double> ringSamples, ref int sampleCount)
    {
        if ((uint)sampleCount >= (uint)ringSamples.Length)
        {
            return;
        }

        uint colorRef = GetPixel(desktopDc, x, y);
        if (colorRef == InvalidColorRef)
        {
            return;
        }

        ringSamples[sampleCount] = ColorRefToLuminance(colorRef);
        sampleCount++;
    }



    private static double ComputeRobustChunkLuminance(ReadOnlySpan<double> chunkLuminances)
    {
        double[] sorted = chunkLuminances.ToArray();
        Array.Sort(sorted);

        int trimCount = (int)Math.Floor(sorted.Length * ChunkTrimFraction);
        if (trimCount * 2 >= sorted.Length)
        {
            trimCount = 0;
        }

        double trimmedTotal = 0d;
        int trimmedCount = 0;
        for (int i = trimCount; i < sorted.Length - trimCount; i++)
        {
            trimmedTotal += sorted[i];
            trimmedCount++;
        }

        double trimmedMean = trimmedCount > 0
            ? trimmedTotal / trimmedCount
            : sorted[sorted.Length / 2];

        int upperPercentileIndex = (int)Math.Round((sorted.Length - 1) * ChunkUpperPercentile);
        upperPercentileIndex = Math.Clamp(upperPercentileIndex, 0, sorted.Length - 1);
        double upperPercentile = sorted[upperPercentileIndex];

        return Math.Clamp(Lerp(trimmedMean, upperPercentile, ChunkUpperPercentileWeight), 0d, 1d);
    }

    private static double ColorRefToLuminance(uint colorRef)
    {
        byte r = (byte)(colorRef & 0x000000FF);
        byte g = (byte)((colorRef & 0x0000FF00) >> 8);
        byte b = (byte)((colorRef & 0x00FF0000) >> 16);

        return ((0.2126d * r) + (0.7152d * g) + (0.0722d * b)) / 255d;
    }

    private static double SmoothStep(double value)
    {
        return value * value * (3d - (2d * value));
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }

    private static double Lerp(double start, double end, double amount)
    {
        return start + ((end - start) * amount);
    }

    private static Windows.UI.Color LerpColor(Windows.UI.Color start, Windows.UI.Color end, float amount)
    {
        return Windows.UI.Color.FromArgb(
            (byte)Lerp(start.A, end.A, amount),
            (byte)Lerp(start.R, end.R, amount),
            (byte)Lerp(start.G, end.G, amount),
            (byte)Lerp(start.B, end.B, amount));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AdaptiveBackdropController));
        }
    }

    [DllImport("user32.dll")]
    private static extern uint SetWindowDisplayAffinity(nint hwnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDc);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int x, int y);
}
