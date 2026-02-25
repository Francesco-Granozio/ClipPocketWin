using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Infrastructure.Persistence;
using ClipPocketWin.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ClipPocketWin.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddClipPocketInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClipboardEncryptionService, AesGcmClipboardEncryptionService>();
        services.AddSingleton<IClipboardHistoryRepository, FileClipboardHistoryRepository>();
        services.AddSingleton<IPinnedClipboardRepository, FilePinnedClipboardRepository>();
        services.AddSingleton<ISnippetRepository, FileSnippetRepository>();
        services.AddSingleton<ISettingsRepository, FileSettingsRepository>();
        return services;
    }
}
