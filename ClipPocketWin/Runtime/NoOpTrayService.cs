using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClipPocketWin.Runtime;

public sealed class NoOpTrayService : ITrayService
{
    private readonly ILogger<NoOpTrayService> _logger;

    public NoOpTrayService(ILogger<NoOpTrayService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? ToggleRequested
    {
        add { }
        remove { }
    }

    public event EventHandler? ShowRequested
    {
        add { }
        remove { }
    }

    public event EventHandler? HideRequested
    {
        add { }
        remove { }
    }

    public event EventHandler? ExitRequested
    {
        add { }
        remove { }
    }

    public Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogWarning("Tray service is not yet implemented on this runtime path.");
        return Task.FromResult(Result.Failure(new Error(ErrorCode.TrayStartFailed, "Tray service is not yet implemented for this build target.")));
    }

    public Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success());
    }
}
