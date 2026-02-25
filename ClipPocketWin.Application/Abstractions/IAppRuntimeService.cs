using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IAppRuntimeService
{
    event EventHandler? ExitRequested;

    Task<Result> StartAsync(CancellationToken cancellationToken = default);

    Task<Result> StopAsync(CancellationToken cancellationToken = default);
}
