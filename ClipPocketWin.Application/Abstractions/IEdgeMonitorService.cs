using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IEdgeMonitorService
{
    event EventHandler? EdgeEntered;

    event EventHandler? EdgeExited;

    Task<Result> StartAsync(double showDelaySeconds, double hideDelaySeconds, CancellationToken cancellationToken = default);

    Task<Result> UpdateDelaysAsync(double showDelaySeconds, double hideDelaySeconds, CancellationToken cancellationToken = default);

    Task<Result> StopAsync(CancellationToken cancellationToken = default);
}
