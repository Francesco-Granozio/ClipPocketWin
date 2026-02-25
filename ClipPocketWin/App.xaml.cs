using global::ClipPocketWin.Application.Abstractions;
using global::ClipPocketWin.Application.DependencyInjection;
using global::ClipPocketWin.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using System;

namespace ClipPocketWin
{
    public partial class App : Microsoft.UI.Xaml.Application
    {
        public IServiceProvider Services { get; }

        private Microsoft.UI.Xaml.Window? _window;

        public App()
        {
            InitializeComponent();
            Services = BuildServiceProvider();
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            ILogger<App> logger = Services.GetRequiredService<ILogger<App>>();
            IClipboardStateService clipboardStateService = Services.GetRequiredService<IClipboardStateService>();
            var initializeResult = await clipboardStateService.InitializeAsync();
            if (initializeResult.IsFailure)
            {
                logger.LogError(initializeResult.Error?.Exception, "Clipboard state initialization failed with code {ErrorCode}: {Message}", initializeResult.Error?.Code, initializeResult.Error?.Message);
            }

            _window = new MainWindow();
            _window.Activate();
        }

        private static ServiceProvider BuildServiceProvider()
        {
            ServiceCollection services = new();
            services.AddLogging(builder => builder.AddConsole());
            services.AddClipPocketInfrastructure();
            services.AddClipPocketApplication();
            return services.BuildServiceProvider();
        }
    }
}
