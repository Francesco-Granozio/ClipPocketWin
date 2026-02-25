using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Composition;
using WinRT.Interop;
using WinRT;
using Microsoft.UI;
using System;

namespace ClipPocketWin
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow m_AppWindow;
        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _configurationSource;

        public MainWindow()
        {
            this.InitializeComponent();

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
            TrySetAcrylicBackdrop();

            this.Activated += Window_Activated;
            this.Closed += Window_Closed;
        }

        private bool TrySetAcrylicBackdrop()
        {
            if (!DesktopAcrylicController.IsSupported())
                return false;

            _configurationSource = new SystemBackdropConfiguration();
            _configurationSource.Theme = SystemBackdropTheme.Dark;
            _configurationSource.IsInputActive = true;

            _acrylicController = new DesktopAcrylicController();

            // ── KEY SETTINGS FOR REAL GLASS TRANSPARENCY ──
            // TintColor: the color overlay on the blurred background
            // TintOpacity: 0 = no tint (fully transparent), 1 = solid tint
            // LuminosityOpacity: 0 = maximum see-through, 1 = opaque luminosity layer
            _acrylicController.TintColor = Windows.UI.Color.FromArgb(255, 15, 20, 50);
            _acrylicController.TintOpacity = 0.15f;          // very subtle dark tint
            _acrylicController.LuminosityOpacity = 0.0f;     // max transparency
            _acrylicController.FallbackColor = Windows.UI.Color.FromArgb(200, 15, 20, 50);

            // Attach to the window
            _acrylicController.AddSystemBackdropTarget(
                this.As<ICompositionSupportsSystemBackdrop>());
            _acrylicController.SetSystemBackdropConfiguration(_configurationSource);

            return true;
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            // Always keep acrylic active, even when window loses focus
            if (_configurationSource != null)
            {
                _configurationSource.IsInputActive = true;
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            if (_acrylicController != null)
            {
                _acrylicController.Dispose();
                _acrylicController = null;
            }
            _configurationSource = null;
        }
    }
}