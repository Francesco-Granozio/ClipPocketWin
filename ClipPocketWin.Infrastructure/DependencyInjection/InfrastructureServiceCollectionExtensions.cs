using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Infrastructure.Clipboard;
using ClipPocketWin.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ClipPocketWin.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddClipPocketInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClipboardMonitor, WindowsClipboardMonitor>();
        services.AddSingleton<IClipboardHistoryRepository, FileClipboardHistoryRepository>();
        services.AddSingleton<IPinnedClipboardRepository, FilePinnedClipboardRepository>();
        services.AddSingleton<ISettingsRepository, FileSettingsRepository>();
        return services;
    }
}
