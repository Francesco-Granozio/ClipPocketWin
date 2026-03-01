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

public sealed record AdaptiveBackdropOptions
{
    public string DiagnosticName { get; init; } = "Window";
    public Windows.UI.Color BaseTintColor { get; init; } = Windows.UI.Color.FromArgb(255, 15, 20, 50);
    public Windows.UI.Color StrongProtectionTintColor { get; init; } = Windows.UI.Color.FromArgb(255, 1, 4, 12);
    public float MinTintOpacity { get; init; } = 0.12f;
    public float MaxTintOpacity { get; init; } = 0.90f;
    public float MinLuminosityOpacity { get; init; } = 0.00f;
    public float MaxLuminosityOpacity { get; init; } = 0.66f;
    public float MaxAdditionalContrastTint { get; init; } = 0.30f;
    public byte FallbackMinAlpha { get; init; } = 190;
    public byte FallbackMaxAlpha { get; init; } = 255;
    public double LuminanceProtectionThreshold { get; init; } = 0.89;
    public double LuminanceFallbackValue { get; init; } = 0.74;
    public double ProtectionCurveGamma { get; init; } = 1.95;
    public double LuminanceRiseSmoothing { get; init; } = 0.72;
    public double ReliableLuminanceDecaySmoothing { get; init; } = 0.20;
    public double UnreliableLuminanceDecaySmoothing { get; init; } = 0.05;
    public int MinimumReliableUnderWindowSamples { get; init; } = 45;
    public double PostMoveReadabilityDelaySeconds { get; init; } = 0.45;
    public double PostShowReadabilityDelaySeconds { get; init; } = 0.90;
    public double ContinuousReadabilityIntervalSeconds { get; init; } = 0.22;
    public double BackdropDiagnosticLogIntervalSeconds { get; init; } = 1.5;
    public double BackdropFallbackWarningIntervalSeconds { get; init; } = 6.0;
}

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
    private const uint GwHwndNext = 2;
    private const uint InvalidColorRef = 0xFFFFFFFF;

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

    private readonly struct WindowSamplingCandidate
    {
        public WindowSamplingCandidate(nint handle, NativeRect bounds)
        {
            Handle = handle;
            Bounds = bounds;
        }

        public nint Handle { get; }

        public NativeRect Bounds { get; }
    }

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

        _ = RefreshBackdropProtectionAsync();
        TriggerDelayedReadabilityCheck(_options.PostShowReadabilityDelaySeconds);
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
                (measuredLuminance, diagnostics) = await System.Threading.Tasks.Task.Run(() =>
                {
                    if (TryMeasureBackdropLuminance(windowHandle, left, top, width, height, out double sampledLuminance, out BackdropSamplingDiagnostics sampledDiagnostics))
                    {
                        return (sampledLuminance, sampledDiagnostics);
                    }

                    return (_options.LuminanceFallbackValue, new BackdropSamplingDiagnostics(BackdropSampleSource.Fallback, 0, 0, 0));
                });
            }
            finally
            {
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

        if (TryMeasureUnderWindowLuminance(windowHandle, left, top, width, height, out double underWindowLuminance, out int underWindowSamples, out int candidateWindowCount, out int touchedWindowCount))
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
        nint windowHandle,
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
        candidateWindowCount = 0;
        touchedWindowCount = 0;

        if (!TryBuildUnderWindowCandidates(windowHandle, out List<WindowSamplingCandidate> candidates))
        {
            return false;
        }

        candidateWindowCount = candidates.Count;
        if (candidateWindowCount == 0)
        {
            return false;
        }

        Span<double> chunkLuminances = stackalloc double[SamplingGridRows * SamplingGridCols];
        int validChunkCount = 0;

        Dictionary<nint, IntPtr> windowDcs = new(candidates.Count);
        HashSet<nint> touchedWindows = [];

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

                            if (!TrySampleLuminanceFromUnderlyingWindow(candidates, windowDcs, sampleX, sampleY, out double sampleLuminance, out nint sampledWindowHandle))
                            {
                                continue;
                            }

                            chunkTotal += sampleLuminance;
                            chunkSampleCount++;
                            validSampleCount++;
                            _ = touchedWindows.Add(sampledWindowHandle);
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
            foreach (KeyValuePair<nint, IntPtr> pair in windowDcs)
            {
                if (pair.Value != IntPtr.Zero)
                {
                    _ = ReleaseDC(pair.Key, pair.Value);
                }
            }
        }

        if (validChunkCount == 0)
        {
            return false;
        }

        luminance = ComputeRobustChunkLuminance(chunkLuminances[..validChunkCount]);
        touchedWindowCount = touchedWindows.Count;
        return true;
    }

    private static bool TryBuildUnderWindowCandidates(nint windowHandle, out List<WindowSamplingCandidate> candidates)
    {
        candidates = [];

        if (!IsWindow(windowHandle))
        {
            return false;
        }

        nint current = GetWindow(windowHandle, GwHwndNext);
        int safetyCounter = 0;

        while (current != nint.Zero && safetyCounter < 1024)
        {
            safetyCounter++;

            if (current != windowHandle
                && IsWindow(current)
                && IsWindowVisible(current)
                && !IsIconic(current)
                && GetWindowRect(current, out NativeRect bounds)
                && bounds.Right > bounds.Left
                && bounds.Bottom > bounds.Top)
            {
                candidates.Add(new WindowSamplingCandidate(current, bounds));
            }

            current = GetWindow(current, GwHwndNext);
        }

        return candidates.Count > 0;
    }

    private static bool TrySampleLuminanceFromUnderlyingWindow(
        List<WindowSamplingCandidate> candidates,
        Dictionary<nint, IntPtr> windowDcs,
        int screenX,
        int screenY,
        out double luminance,
        out nint sampledWindowHandle)
    {
        luminance = 0d;
        sampledWindowHandle = nint.Zero;

        for (int i = 0; i < candidates.Count; i++)
        {
            WindowSamplingCandidate candidate = candidates[i];
            if (!IsPointInsideRect(candidate.Bounds, screenX, screenY))
            {
                continue;
            }

            if (!windowDcs.TryGetValue(candidate.Handle, out nint targetDc))
            {
                targetDc = GetWindowDC(candidate.Handle);
                windowDcs[candidate.Handle] = targetDc;
            }

            if (targetDc == nint.Zero)
            {
                continue;
            }

            int localX = screenX - candidate.Bounds.Left;
            int localY = screenY - candidate.Bounds.Top;
            uint colorRef = GetPixel(targetDc, localX, localY);
            if (colorRef == InvalidColorRef)
            {
                continue;
            }

            sampledWindowHandle = candidate.Handle;
            luminance = ColorRefToLuminance(colorRef);
            return true;
        }

        return false;
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

    private static bool IsPointInsideRect(NativeRect rect, int x, int y)
    {
        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
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
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDc);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int x, int y);
}
