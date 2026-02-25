using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface ITrayService
{
    event EventHandler? ToggleRequested;

    event EventHandler? ShowRequested;

    event EventHandler? HideRequested;

    event EventHandler? ExitRequested;

    Task<Result> StartAsync(CancellationToken cancellationToken = default);

    Task<Result> StopAsync(CancellationToken cancellationToken = default);
}
