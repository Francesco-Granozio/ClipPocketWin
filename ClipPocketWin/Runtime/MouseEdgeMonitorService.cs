using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ClipPocketWin.Runtime;

public sealed class MouseEdgeMonitorService : IEdgeMonitorService, IDisposable
{
    private const int DefaultPollIntervalMs = 50;
    private const int EdgeThresholdPixels = 10;

    private readonly ILogger<MouseEdgeMonitorService> _logger;
    private readonly object _syncRoot = new();

    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private TimeSpan _showDelay = TimeSpan.FromMilliseconds(300);
    private TimeSpan _hideDelay = TimeSpan.FromMilliseconds(500);
    private DateTimeOffset? _edgeEnteredAt;
    private DateTimeOffset? _edgeExitedAt;
    private bool _isEdgeActive;
    private bool _started;

    public MouseEdgeMonitorService(ILogger<MouseEdgeMonitorService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? EdgeEntered;

    public event EventHandler? EdgeExited;

    public Task<Result> StartAsync(double showDelaySeconds, double hideDelaySeconds, CancellationToken cancellationToken = default)
    {
        if (showDelaySeconds < 0 || hideDelaySeconds < 0)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.SettingsRangeInvalid, "Edge monitor delay values must be non-negative.")));
        }

        lock (_syncRoot)
        {
            _showDelay = TimeSpan.FromSeconds(showDelaySeconds);
            _hideDelay = TimeSpan.FromSeconds(hideDelaySeconds);

            if (_started)
            {
                return Task.FromResult(Result.Success());
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitorTask = Task.Run(() => MonitorAsync(_cts.Token), CancellationToken.None);
            _started = true;
            _edgeEnteredAt = null;
            _edgeExitedAt = null;
            _isEdgeActive = false;
        }

#if DEBUG
        _logger.LogInformation("Mouse edge monitor started with show/hide delays {ShowDelay}s/{HideDelay}s.", showDelaySeconds, hideDelaySeconds);
#endif
        return Task.FromResult(Result.Success());
    }

    public Task<Result> UpdateDelaysAsync(double showDelaySeconds, double hideDelaySeconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (showDelaySeconds < 0 || hideDelaySeconds < 0)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.SettingsRangeInvalid, "Edge monitor delay values must be non-negative.")));
        }

        lock (_syncRoot)
        {
            _showDelay = TimeSpan.FromSeconds(showDelaySeconds);
            _hideDelay = TimeSpan.FromSeconds(hideDelaySeconds);
        }

#if DEBUG
        _logger.LogInformation("Mouse edge monitor delays updated to {ShowDelay}s/{HideDelay}s.", showDelaySeconds, hideDelaySeconds);
#endif
        return Task.FromResult(Result.Success());
    }

    public async Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        Task? monitorTask;
        CancellationTokenSource? cts;

        lock (_syncRoot)
        {
            if (!_started)
            {
                return Result.Success();
            }

            _started = false;
            monitorTask = _monitorTask;
            cts = _cts;
            _monitorTask = null;
            _cts = null;
            _edgeEnteredAt = null;
            _edgeExitedAt = null;
            _isEdgeActive = false;
        }

        try
        {
            cts?.Cancel();
            if (monitorTask is not null)
            {
                await monitorTask.WaitAsync(cancellationToken);
            }

#if DEBUG
            _logger.LogInformation("Mouse edge monitor stopped.");
#endif
            return Result.Success();
        }
        catch (OperationCanceledException) when (cts?.IsCancellationRequested == true)
        {
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(ErrorCode.InvalidOperation, "Failed to stop edge monitor.", exception));
        }
        finally
        {
            cts?.Dispose();
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
#if DEBUG
            _logger.LogWarning(exception, "Failed to dispose edge monitor service.");
#endif
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(DefaultPollIntervalMs));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                bool atBottomEdge = IsCursorAtBottomEdge();
                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (atBottomEdge)
                {
                    _edgeExitedAt = null;
                    _edgeEnteredAt ??= now;

                    if (!_isEdgeActive && now - _edgeEnteredAt.Value >= _showDelay)
                    {
                        _isEdgeActive = true;
                        EdgeEntered?.Invoke(this, EventArgs.Empty);
                    }
                }
                else
                {
                    _edgeEnteredAt = null;
                    _edgeExitedAt ??= now;

                    if (_isEdgeActive && now - _edgeExitedAt.Value >= _hideDelay)
                    {
                        _isEdgeActive = false;
                        EdgeExited?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
#if DEBUG
            _logger.LogError(exception, "Mouse edge monitor loop crashed.");
#endif
        }
    }

    private static bool IsCursorAtBottomEdge()
    {
        if (!GetCursorPos(out Point cursor))
        {
            return false;
        }

        nint monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return false;
        }

        MonitorInfo monitorInfo = new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        int distanceFromBottom = monitorInfo.Monitor.Bottom - cursor.Y;
        return distanceFromBottom <= EdgeThresholdPixels;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }
}
