using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT;
using WinRT.Interop;

namespace ClipPocketWin
{
    public sealed partial class MainWindow : Window
    {
        private const float MinTintOpacity = 0.18f;
        private const float MaxTintOpacity = 0.79f;
        private const float MinLuminosityOpacity = 0.00f;
        private const float MaxLuminosityOpacity = 0.52f;
        private const float MaxAdditionalContrastTint = 0.22f;
        private const double LuminanceProtectionThreshold = 0.62;
        private const double LuminanceFallbackValue = 0.74;
        private const double LuminanceSmoothing = 0.62;
        private const int SamplingPadding = 8;
        private const int ReadabilityUpdateIntervalMs = 260;
        private static readonly Windows.UI.Color BaseTintColor = Windows.UI.Color.FromArgb(255, 15, 20, 50);
        private static readonly Windows.UI.Color StrongProtectionTintColor = Windows.UI.Color.FromArgb(255, 1, 4, 12);

        private AppWindow m_AppWindow;
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;
        private DispatcherQueueTimer? _readabilityTimer;
        private double _smoothedBackdropLuminance = LuminanceFallbackValue;
        private bool _hasBackdropSample;
        private bool _isBackdropSampling;

        public MainWindow()
        {
            InitializeComponent();

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            m_AppWindow = AppWindow.GetFromWindowId(wndId);

            // Extend content into title bar for seamless glass effect
            m_AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            m_AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            m_AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            m_AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
            m_AppWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Transparent;

            var presenter = m_AppWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
                presenter.IsAlwaysOnTop = false;
            }

            // Size the window
            var displayArea = DisplayArea.GetFromWindowId(wndId, DisplayAreaFallback.Primary);
            int windowWidth = 1100;
            int windowHeight = 420;
            int x = (displayArea.WorkArea.Width - windowWidth) / 2;
            int y = (displayArea.WorkArea.Height - windowHeight) / 2;
            m_AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, windowWidth, windowHeight));

            // Apply true glass blur with maximum transparency
            if (TrySetAcrylicBackdrop())
            {
                StartReadabilityMonitoring();
            }

            Activated += Window_Activated;
            Closed += Window_Closed;
        }

        private bool TrySetAcrylicBackdrop()
        {
            if (!DesktopAcrylicController.IsSupported())
            {
                return false;
            }

            _configurationSource = new SystemBackdropConfiguration();
            _configurationSource.Theme = SystemBackdropTheme.Dark;
            _configurationSource.IsInputActive = true;

            _acrylicController = new DesktopAcrylicController();

            // ── KEY SETTINGS FOR REAL GLASS TRANSPARENCY ──
            // TintColor: the color overlay on the blurred background
            // TintOpacity: 0 = no tint (fully transparent), 1 = solid tint
            // LuminosityOpacity: 0 = maximum see-through, 1 = opaque luminosity layer
            _acrylicController.TintColor = BaseTintColor;
            _acrylicController.TintOpacity = MinTintOpacity;
            _acrylicController.LuminosityOpacity = MinLuminosityOpacity;
            _acrylicController.FallbackColor = Windows.UI.Color.FromArgb(200, BaseTintColor.R, BaseTintColor.G, BaseTintColor.B);

            // Attach to the window
            _acrylicController.AddSystemBackdropTarget(
                this.As<ICompositionSupportsSystemBackdrop>());
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

            _ = RefreshBackdropProtectionAsync();

            return true;
        }

        private void StartReadabilityMonitoring()
        {
            _readabilityTimer ??= DispatcherQueue.CreateTimer();
            _readabilityTimer.Interval = TimeSpan.FromMilliseconds(ReadabilityUpdateIntervalMs);
            _readabilityTimer.IsRepeating = true;
            _readabilityTimer.Tick += ReadabilityTimer_Tick;
            _readabilityTimer.Start();
        }

        private void StopReadabilityMonitoring()
        {
            if (_readabilityTimer == null)
            {
                return;
            }

            _readabilityTimer.Tick -= ReadabilityTimer_Tick;
            _readabilityTimer.Stop();
            _readabilityTimer = null;
        }

        private void ReadabilityTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            _ = RefreshBackdropProtectionAsync();
        }

        private async Task RefreshBackdropProtectionAsync()
        {
            if (_acrylicController == null || _isBackdropSampling)
            {
                return;
            }

            int left = m_AppWindow.Position.X;
            int top = m_AppWindow.Position.Y;
            int width = m_AppWindow.Size.Width;
            int height = m_AppWindow.Size.Height;

            double measuredLuminance;
            if (width <= 0 || height <= 0)
            {
                measuredLuminance = LuminanceFallbackValue;
            }
            else
            {
                _isBackdropSampling = true;
                try
                {
                    measuredLuminance = await Task.Run(() =>
                    {
                        if (TryMeasureBackdropLuminance(left, top, width, height, out double sampledLuminance))
                        {
                            return sampledLuminance;
                        }

                        return LuminanceFallbackValue;
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
                _smoothedBackdropLuminance = Lerp(_smoothedBackdropLuminance, measuredLuminance, LuminanceSmoothing);
            }

            ApplyBackdropReadability(_smoothedBackdropLuminance);
        }

        private void ApplyBackdropReadability(double luminance)
        {
            if (_acrylicController == null)
            {
                return;
            }

            double normalizedProtection = (luminance - LuminanceProtectionThreshold) / (1d - LuminanceProtectionThreshold);
            normalizedProtection = Math.Clamp(normalizedProtection, 0d, 1d);
            normalizedProtection = SmoothStep(normalizedProtection);

            float protection = (float)normalizedProtection;
            float contrastBoost = protection * MaxAdditionalContrastTint;

            _acrylicController.TintColor = LerpColor(BaseTintColor, StrongProtectionTintColor, protection);
            _acrylicController.TintOpacity = Math.Clamp(Lerp(MinTintOpacity, MaxTintOpacity, protection) + contrastBoost, 0f, 1f);
            _acrylicController.LuminosityOpacity = Lerp(MinLuminosityOpacity, MaxLuminosityOpacity, protection);
            _acrylicController.FallbackColor = Windows.UI.Color.FromArgb(
                (byte)Lerp(190f, 255f, protection),
                BaseTintColor.R,
                BaseTintColor.G,
                BaseTintColor.B);
        }

        private static bool TryMeasureBackdropLuminance(int left, int top, int width, int height, out double luminance)
        {
            luminance = 0d;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            int centerX = left + (width / 2);
            int centerY = top + (height / 2);

            Span<(int X, int Y)> samples = stackalloc (int X, int Y)[8];
            samples[0] = (left - SamplingPadding, top - SamplingPadding);
            samples[1] = (centerX, top - SamplingPadding);
            samples[2] = (left + width + SamplingPadding, top - SamplingPadding);
            samples[3] = (left - SamplingPadding, centerY);
            samples[4] = (left + width + SamplingPadding, centerY);
            samples[5] = (left - SamplingPadding, top + height + SamplingPadding);
            samples[6] = (centerX, top + height + SamplingPadding);
            samples[7] = (left + width + SamplingPadding, top + height + SamplingPadding);

            IntPtr desktopDc = GetDC(IntPtr.Zero);
            if (desktopDc == IntPtr.Zero)
            {
                return false;
            }

            double total = 0d;
            int validCount = 0;

            try
            {
                foreach ((int sampleX, int sampleY) in samples)
                {
                    uint colorRef = GetPixel(desktopDc, sampleX, sampleY);
                    if (colorRef == 0xFFFFFFFF)
                    {
                        continue;
                    }

                    byte r = (byte)(colorRef & 0x000000FF);
                    byte g = (byte)((colorRef & 0x0000FF00) >> 8);
                    byte b = (byte)((colorRef & 0x00FF0000) >> 16);

                    total += (0.2126d * r + 0.7152d * g + 0.0722d * b) / 255d;
                    validCount++;
                }
            }
            finally
            {
                _ = ReleaseDC(IntPtr.Zero, desktopDc);
            }

            if (validCount == 0)
            {
                return false;
            }

            luminance = total / validCount;
            return true;
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

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            // Always keep acrylic active, even when window loses focus
            if (_configurationSource != null)
            {
                _configurationSource.IsInputActive = true;
            }

            _ = RefreshBackdropProtectionAsync();
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            StopReadabilityMonitoring();

            if (_acrylicController != null)
            {
                _acrylicController.Dispose();
                _acrylicController = null;
            }
            _configurationSource = null;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);
    }
}
