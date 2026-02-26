using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace ClipPocketWin.DependencyInjection;

public static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddClipPocketPresentationRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IWindowPanelService, WindowPanelService>();
        services.AddSingleton<IAutoPasteService, WindowsAutoPasteService>();
        services.AddSingleton<IGlobalHotkeyService, PollingGlobalHotkeyService>();
        services.AddSingleton<ITrayService, WindowsTrayService>();
        services.AddSingleton<IEdgeMonitorService, MouseEdgeMonitorService>();
        services.AddSingleton<IQuickActionsService, WindowsQuickActionsService>();
        return services;
    }
}
