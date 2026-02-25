using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClipPocketWin.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddClipPocketApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClipboardStateService, ClipboardStateService>();
        services.AddSingleton<IAppRuntimeService, AppRuntimeService>();
        return services;
    }
}
