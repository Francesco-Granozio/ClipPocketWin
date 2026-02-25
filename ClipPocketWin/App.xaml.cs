using global::ClipPocketWin.Application.Abstractions;
using global::ClipPocketWin.Application.DependencyInjection;
using global::ClipPocketWin.DependencyInjection;
using global::ClipPocketWin.Infrastructure.DependencyInjection;
using global::ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using WinRT.Interop;

namespace ClipPocketWin
{
    public partial class App : Microsoft.UI.Xaml.Application
    {
        public IServiceProvider Services { get; }

        private Microsoft.UI.Xaml.Window? _window;
        private IAppRuntimeService? _appRuntimeService;

        public App()
        {
            InitializeComponent();
            Services = BuildServiceProvider();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();

            IWindowPanelService panelService = Services.GetRequiredService<IWindowPanelService>();
            nint windowHandle = WindowNative.GetWindowHandle(_window);
            panelService.AttachWindowHandle(windowHandle);

            Result startupResult = await InitializeRuntimeAsync();
            ILogger<App>? logger = Services.GetService<ILogger<App>>();
            if (startupResult.IsFailure)
            {
                logger?.LogError(
                    startupResult.Error?.Exception,
                    "Application runtime initialization failed with code {ErrorCode}: {Message}",
                    startupResult.Error?.Code,
                    startupResult.Error?.Message);
            }

            _window.Closed += Window_Closed;
            _window.Activate();
        }

        private async Task<Result> InitializeRuntimeAsync()
        {
            try
            {
                IClipboardStateService clipboardStateService = Services.GetRequiredService<IClipboardStateService>();

                Result initializeResult = await clipboardStateService.InitializeAsync();
                if (initializeResult.IsFailure)
                {
                    return initializeResult;
                }

                _appRuntimeService = Services.GetRequiredService<IAppRuntimeService>();
                _appRuntimeService.ExitRequested += AppRuntimeService_ExitRequested;
                Result runtimeResult = await _appRuntimeService.StartAsync();
                if (runtimeResult.IsFailure)
                {
                    return runtimeResult;
                }


                return Result.Success();
            }
            catch (Exception exception)
            {
                return Result.Failure(new Error(
                    ErrorCode.DependencyResolutionFailed,
                    "Failed to resolve runtime startup services.",
                    exception));
            }
        }

        private async void Window_Closed(object sender, WindowEventArgs args)
        {
            if (_appRuntimeService is null)
            {
                return;
            }

            Result stopResult = await _appRuntimeService.StopAsync();
            if (stopResult.IsFailure)
            {
                ILogger<App>? logger = Services.GetService<ILogger<App>>();
                logger?.LogWarning(stopResult.Error?.Exception, "Failed to stop runtime cleanly with code {ErrorCode}: {Message}", stopResult.Error?.Code, stopResult.Error?.Message);
                return;
            }

        }

        private async void AppRuntimeService_ExitRequested(object? sender, EventArgs e)
        {
            if (_appRuntimeService is not null)
            {
                _appRuntimeService.ExitRequested -= AppRuntimeService_ExitRequested;
                _ = await _appRuntimeService.StopAsync();
            }

            _window?.Close();
        }

        private static ServiceProvider BuildServiceProvider()
        {
            ServiceCollection services = new();
            services.AddLogging(builder => builder.AddConsole());
            services.AddClipPocketInfrastructure();
            services.AddClipPocketApplication();
            services.AddClipPocketPresentationRuntime();
            return services.BuildServiceProvider();
        }
    }
}
