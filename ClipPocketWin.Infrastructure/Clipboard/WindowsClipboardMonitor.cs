using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;

namespace ClipPocketWin.Infrastructure.Clipboard;

public sealed class WindowsClipboardMonitor : IClipboardMonitor, IDisposable
{
    private readonly ILogger<WindowsClipboardMonitor> _logger;
    private readonly WindowsClipboardPayloadReader _payloadReader;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMilliseconds(250);
    private readonly object _syncRoot = new();

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private Func<ClipboardItem, Task<Result>>? _captureCallback;
    private uint _lastClipboardSequence;
    private bool _captureRichTextEnabled;
    private bool _isRunning;

    public WindowsClipboardMonitor(ILogger<WindowsClipboardMonitor> logger)
    {
        _logger = logger;
        _payloadReader = new WindowsClipboardPayloadReader();
    }

    public Task<Result> StartAsync(
        Func<ClipboardItem, Task<Result>> onClipboardItemCapturedAsync,
        bool captureRichTextEnabled,
        CancellationToken cancellationToken = default)
    {
        if (onClipboardItemCapturedAsync is null)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.ValidationError, "Clipboard monitor callback cannot be null.")));
        }

        try
        {
            lock (_syncRoot)
            {
                if (_isRunning)
                {
                    return Task.FromResult(Result.Success());
                }

                _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _captureCallback = onClipboardItemCapturedAsync;
                _captureRichTextEnabled = captureRichTextEnabled;
                _lastClipboardSequence = WindowsClipboardNativeApi.GetClipboardSequenceNumber();
                _isRunning = true;
                _monitorTask = Task.Run(() => MonitorLoopAsync(_monitorCts.Token), CancellationToken.None);
            }

#if DEBUG
            _logger.LogInformation("Windows clipboard monitor started with polling interval {IntervalMs}ms.", _pollingInterval.TotalMilliseconds);
#endif
            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(Result.Failure(new Error(
                ErrorCode.ClipboardMonitorStartFailed,
                "Failed to start Windows clipboard monitor.",
                exception)));
        }
    }

    public async Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        Task? monitorTask;
        CancellationTokenSource? monitorCts;

        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return Result.Success();
            }

            _isRunning = false;
            monitorTask = _monitorTask;
            monitorCts = _monitorCts;
            _monitorTask = null;
            _monitorCts = null;
            _captureCallback = null;
        }

        try
        {
            monitorCts?.Cancel();
            if (monitorTask is not null)
            {
                await monitorTask.WaitAsync(cancellationToken);
            }

#if DEBUG
            _logger.LogInformation("Windows clipboard monitor stopped.");
#endif
            return Result.Success();
        }
        catch (OperationCanceledException) when (monitorCts?.IsCancellationRequested == true)
        {
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(
                ErrorCode.InvalidOperation,
                "Failed to stop Windows clipboard monitor.",
                exception));
        }
        finally
        {
            monitorCts?.Dispose();
        }
    }

    public Task<Result> UpdateCaptureRichTextAsync(bool captureRichTextEnabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            _captureRichTextEnabled = captureRichTextEnabled;
        }

        return Task.FromResult(Result.Success());
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
            _logger.LogWarning(exception, "Failed to stop clipboard monitor during dispose.");
#endif
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(_pollingInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                uint currentSequence = WindowsClipboardNativeApi.GetClipboardSequenceNumber();
                if (currentSequence == _lastClipboardSequence)
                {
                    continue;
                }

                _lastClipboardSequence = currentSequence;

                bool captureRichTextEnabled;
                lock (_syncRoot)
                {
                    captureRichTextEnabled = _captureRichTextEnabled;
                }

                Result<IReadOnlyList<ClipboardItem>> captureResult = _payloadReader.TryReadClipboardItems(captureRichTextEnabled);
                if (captureResult.IsFailure)
                {
#if DEBUG
                    _logger.LogWarning(
                        captureResult.Error?.Exception,
                        "Clipboard capture failed with code {ErrorCode}: {Message}",
                        captureResult.Error?.Code,
                        captureResult.Error?.Message);
#endif
                    continue;
                }

                IReadOnlyList<ClipboardItem> clipboardItems = captureResult.Value!;
                if (clipboardItems.Count == 0)
                {
                    continue;
                }

                Func<ClipboardItem, Task<Result>>? callback;
                lock (_syncRoot)
                {
                    callback = _captureCallback;
                }

                if (callback is null)
                {
                    continue;
                }

                foreach (ClipboardItem clipboardItem in clipboardItems)
                {
                    Result callbackResult = await callback(clipboardItem);
                    if (callbackResult.IsFailure)
                    {
#if DEBUG
                        _logger.LogWarning(
                            callbackResult.Error?.Exception,
                            "Clipboard item processing failed with code {ErrorCode}: {Message}",
                            callbackResult.Error?.Code,
                            callbackResult.Error?.Message);
#endif
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
            _logger.LogError(exception, "Clipboard monitor loop terminated unexpectedly.");
#endif
        }
    }
}
