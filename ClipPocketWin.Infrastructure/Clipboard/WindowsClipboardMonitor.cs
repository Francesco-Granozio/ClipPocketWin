using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ClipPocketWin.Infrastructure.Clipboard;

public sealed class WindowsClipboardMonitor : IClipboardMonitor, IDisposable
{
    private const uint ClipboardFormatUnicodeText = 13;
    private const uint ClipboardFormatDib = 8;
    private const uint ClipboardFormatDibV5 = 17;
    private const uint ClipboardFormatHDrop = 15;
    private const uint DragQueryFileCount = 0xFFFFFFFF;
    private static readonly uint HtmlClipboardFormat = RegisterClipboardFormat("HTML Format");
    private static readonly uint RtfClipboardFormat = RegisterClipboardFormat("Rich Text Format");

    private readonly ILogger<WindowsClipboardMonitor> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMilliseconds(250);
    private readonly object _syncRoot = new();

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private Func<ClipboardItem, Task<Result>>? _captureCallback;
    private uint _lastClipboardSequence;
    private bool _isRunning;

    public WindowsClipboardMonitor(ILogger<WindowsClipboardMonitor> logger)
    {
        _logger = logger;
    }

    public Task<Result> StartAsync(Func<ClipboardItem, Task<Result>> onClipboardItemCapturedAsync, CancellationToken cancellationToken = default)
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
                _lastClipboardSequence = GetClipboardSequenceNumber();
                _isRunning = true;
                _monitorTask = Task.Run(() => MonitorLoopAsync(_monitorCts.Token), CancellationToken.None);
            }

            _logger.LogInformation("Windows clipboard monitor started with polling interval {IntervalMs}ms.", _pollingInterval.TotalMilliseconds);
            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.ClipboardMonitorStartFailed, "Failed to start Windows clipboard monitor.", exception)));
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

            _logger.LogInformation("Windows clipboard monitor stopped.");
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
            return Result.Failure(new Error(ErrorCode.InvalidOperation, "Failed to stop Windows clipboard monitor.", exception));
        }
        finally
        {
            monitorCts?.Dispose();
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
            _logger.LogWarning(exception, "Failed to stop clipboard monitor during dispose.");
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(_pollingInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                uint currentSequence = GetClipboardSequenceNumber();
                if (currentSequence == _lastClipboardSequence)
                {
                    continue;
                }

                _lastClipboardSequence = currentSequence;

                Result<ClipboardItem?> captureResult = TryReadClipboardItem();
                if (captureResult.IsFailure)
                {
                    _logger.LogWarning(captureResult.Error?.Exception, "Clipboard capture failed with code {ErrorCode}: {Message}", captureResult.Error?.Code, captureResult.Error?.Message);
                    continue;
                }

                ClipboardItem? clipboardItem = captureResult.Value;
                if (clipboardItem is null)
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

                Result callbackResult = await callback(clipboardItem);
                if (callbackResult.IsFailure)
                {
                    _logger.LogWarning(callbackResult.Error?.Exception, "Clipboard item processing failed with code {ErrorCode}: {Message}", callbackResult.Error?.Code, callbackResult.Error?.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Clipboard monitor loop terminated unexpectedly.");
        }
    }

    private static Result<ClipboardItem?> TryReadClipboardItem()
    {
        if (!OpenClipboardWithRetry())
        {
            return Result<ClipboardItem?>.Failure(new Error(ErrorCode.ClipboardMonitorReadFailed, "Clipboard is currently unavailable for reading."));
        }

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (TryReadFilePath(out string? filePath))
            {
                return Result<ClipboardItem?>.Success(new ClipboardItem
                {
                    Type = ClipboardItemType.File,
                    Timestamp = now,
                    FilePath = filePath,
                    TextContent = filePath
                });
            }

            if (TryReadImagePayload(out byte[]? imagePayload))
            {
                return Result<ClipboardItem?>.Success(new ClipboardItem
                {
                    Type = ClipboardItemType.Image,
                    Timestamp = now,
                    BinaryContent = imagePayload
                });
            }

            if (TryReadRichTextContent(out RichTextContent? richTextContent))
            {
                return Result<ClipboardItem?>.Success(new ClipboardItem
                {
                    Type = ClipboardItemType.RichText,
                    Timestamp = now,
                    RichTextContent = richTextContent,
                    TextContent = richTextContent!.PlainText
                });
            }

            if (!TryReadUnicodeText(out string? textContent) || string.IsNullOrWhiteSpace(textContent))
            {
                return Result<ClipboardItem?>.Success(null);
            }

            ClipboardItemType clipboardItemType = ClipboardItemClassifier.ClassifyText(textContent);
            return Result<ClipboardItem?>.Success(new ClipboardItem
            {
                Type = clipboardItemType,
                Timestamp = now,
                TextContent = textContent
            });
        }
        catch (Exception exception)
        {
            return Result<ClipboardItem?>.Failure(new Error(ErrorCode.ClipboardMonitorReadFailed, "Unexpected failure while reading clipboard payload.", exception));
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private static bool TryReadUnicodeText(out string? text)
    {
        text = null;
        if (!IsClipboardFormatAvailable(ClipboardFormatUnicodeText))
        {
            return false;
        }

        IntPtr handle = GetClipboardData(ClipboardFormatUnicodeText);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            text = Marshal.PtrToStringUni(pointer);
            return text is not null;
        }
        finally
        {
            _ = GlobalUnlock(handle);
        }
    }

    private static bool TryReadImagePayload(out byte[]? imagePayload)
    {
        if (TryReadBytesFromFormat(ClipboardFormatDibV5, out imagePayload))
        {
            return true;
        }

        return TryReadBytesFromFormat(ClipboardFormatDib, out imagePayload);
    }

    private static bool TryReadRichTextContent(out RichTextContent? richTextContent)
    {
        richTextContent = null;

        byte[]? rtfData = null;
        byte[]? htmlData = null;
        string plainText = string.Empty;

        if (RtfClipboardFormat != 0)
        {
            _ = TryReadBytesFromFormat(RtfClipboardFormat, out rtfData);
        }

        if (HtmlClipboardFormat != 0)
        {
            _ = TryReadBytesFromFormat(HtmlClipboardFormat, out htmlData);
        }

        if (TryReadUnicodeText(out string? text) && !string.IsNullOrWhiteSpace(text))
        {
            plainText = text;
        }

        if (rtfData is null && htmlData is null)
        {
            return false;
        }

        richTextContent = new RichTextContent(rtfData, htmlData, plainText);
        return true;
    }

    private static bool TryReadFilePath(out string? filePath)
    {
        filePath = null;
        if (!IsClipboardFormatAvailable(ClipboardFormatHDrop))
        {
            return false;
        }

        IntPtr handle = GetClipboardData(ClipboardFormatHDrop);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        uint fileCount = DragQueryFile(handle, DragQueryFileCount, null, 0);
        if (fileCount == 0)
        {
            return false;
        }

        uint length = DragQueryFile(handle, 0, null, 0);
        if (length == 0)
        {
            return false;
        }

        StringBuilder pathBuilder = new((int)length + 1);
        _ = DragQueryFile(handle, 0, pathBuilder, (uint)pathBuilder.Capacity);
        filePath = pathBuilder.ToString();
        return !string.IsNullOrWhiteSpace(filePath);
    }

    private static bool TryReadBytesFromFormat(uint clipboardFormat, out byte[]? data)
    {
        data = null;
        if (!IsClipboardFormatAvailable(clipboardFormat))
        {
            return false;
        }

        IntPtr handle = GetClipboardData(clipboardFormat);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        nuint size = GlobalSize(handle);
        if (size == 0)
        {
            return false;
        }

        IntPtr pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            data = new byte[(int)size];
            Marshal.Copy(pointer, data, 0, data.Length);
            return true;
        }
        finally
        {
            _ = GlobalUnlock(handle);
        }
    }

    private static bool OpenClipboardWithRetry()
    {
        const int retryCount = 5;
        const int delayMs = 12;

        for (int attempt = 0; attempt < retryCount; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(delayMs);
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(IntPtr hMem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);
}
