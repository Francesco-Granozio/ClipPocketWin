using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ClipPocketWin.Infrastructure.Clipboard;

public sealed partial class WindowsClipboardMonitor : IClipboardMonitor, IDisposable
{
    private const uint ClipboardFormatUnicodeText = 13;
    private const uint ClipboardFormatDib = 8;
    private const uint ClipboardFormatDibV5 = 17;
    private const uint ClipboardFormatHDrop = 15;
    private const uint ProcessQueryLimitedInformation = 0x1000;
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
    private bool _captureRichTextEnabled;
    private bool _isRunning;

    public WindowsClipboardMonitor(ILogger<WindowsClipboardMonitor> logger)
    {
        _logger = logger;
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

                bool captureRichTextEnabled;
                lock (_syncRoot)
                {
                    captureRichTextEnabled = _captureRichTextEnabled;
                }

                Result<IReadOnlyList<ClipboardItem>> captureResult = TryReadClipboardItems(captureRichTextEnabled);
                if (captureResult.IsFailure)
                {
                    _logger.LogWarning(captureResult.Error?.Exception, "Clipboard capture failed with code {ErrorCode}: {Message}", captureResult.Error?.Code, captureResult.Error?.Message);
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
                        _logger.LogWarning(callbackResult.Error?.Exception, "Clipboard item processing failed with code {ErrorCode}: {Message}", callbackResult.Error?.Code, callbackResult.Error?.Message);
                    }
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

    private static Result<IReadOnlyList<ClipboardItem>> TryReadClipboardItems(bool captureRichTextEnabled)
    {
        if (!OpenClipboardWithRetry())
        {
            return Result<IReadOnlyList<ClipboardItem>>.Failure(new Error(ErrorCode.ClipboardMonitorReadFailed, "Clipboard is currently unavailable for reading."));
        }

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            (string? sourceApplicationIdentifier, string? sourceApplicationExecutablePath) = TryGetForegroundProcessInfo();

            if (TryReadFilePaths(out IReadOnlyList<string>? filePaths))
            {
                List<ClipboardItem> fileItems = new(filePaths!.Count);
                foreach (string filePath in filePaths)
                {
                    fileItems.Add(new ClipboardItem
                    {
                        Type = ClipboardItemType.File,
                        Timestamp = now,
                        SourceApplicationIdentifier = sourceApplicationIdentifier,
                        SourceApplicationExecutablePath = sourceApplicationExecutablePath,
                        FilePath = filePath,
                        TextContent = filePath
                    });
                }

                return Result<IReadOnlyList<ClipboardItem>>.Success(fileItems);
            }

            /*
            if (TryReadRichTextContent(captureRichTextEnabled, out RichTextContent? richTextContent))
            {
                return Result<IReadOnlyList<ClipboardItem>>.Success([
                    new ClipboardItem
                {
                    Type = ClipboardItemType.RichText,
                    Timestamp = now,
                    SourceApplicationIdentifier = sourceApplicationIdentifier,
                    SourceApplicationExecutablePath = sourceApplicationExecutablePath,
                    RichTextContent = richTextContent,
                    TextContent = richTextContent!.PlainText
                }
                ]);
            }
            */

            if (TryReadImagePayload(out byte[]? imagePayload))
            {
                return Result<IReadOnlyList<ClipboardItem>>.Success([
                    new ClipboardItem
                {
                    Type = ClipboardItemType.Image,
                    Timestamp = now,
                    SourceApplicationIdentifier = sourceApplicationIdentifier,
                    SourceApplicationExecutablePath = sourceApplicationExecutablePath,
                    BinaryContent = imagePayload
                }
                ]);
            }

            if (!TryReadUnicodeText(out string? textContent) || string.IsNullOrWhiteSpace(textContent))
            {
                return Result<IReadOnlyList<ClipboardItem>>.Success([]);
            }

            ClipboardItemType clipboardItemType = ClipboardItemClassifier.ClassifyText(textContent);
            return Result<IReadOnlyList<ClipboardItem>>.Success([
                new ClipboardItem
            {
                Type = clipboardItemType,
                Timestamp = now,
                SourceApplicationIdentifier = sourceApplicationIdentifier,
                SourceApplicationExecutablePath = sourceApplicationExecutablePath,
                TextContent = textContent
            }
            ]);
        }
        catch (Exception exception)
        {
            return Result<IReadOnlyList<ClipboardItem>>.Failure(new Error(ErrorCode.ClipboardMonitorReadFailed, "Unexpected failure while reading clipboard payload.", exception));
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

    private static bool TryReadRichTextContent(bool captureRichTextEnabled, out RichTextContent? richTextContent)
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

        if ((rtfData is null && htmlData is null) || string.IsNullOrWhiteSpace(plainText))
        {
            return false;
        }

        bool isMixed = HasMixedContent(rtfData, htmlData);
        bool isFormatted = captureRichTextEnabled && HasSignificantFormatting(rtfData, htmlData);
        if (!isMixed && !isFormatted)
        {
            return false;
        }

        richTextContent = new RichTextContent(rtfData, htmlData, plainText);
        return true;
    }

    private static bool HasMixedContent(byte[]? rtfData, byte[]? htmlData)
    {
        if (rtfData is not null)
        {
            string rtf = Encoding.ASCII.GetString(rtfData);
            if (rtf.Contains("\\pict", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (htmlData is not null)
        {
            string html = Encoding.UTF8.GetString(htmlData);
            if (html.Contains("<img", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSignificantFormatting(byte[]? rtfData, byte[]? htmlData)
    {
        if (rtfData is not null)
        {
            string rtf = Encoding.ASCII.GetString(rtfData);
            if (RtfSignificantFormattingRegex().IsMatch(rtf))
            {
                return true;
            }
        }

        if (htmlData is not null)
        {
            string html = Encoding.UTF8.GetString(htmlData);
            string[] formattingTags = ["<b>", "<i>", "<strong>", "<em>", "<h1", "<h2", "<h3", "<table", "<ul", "<ol", "<span style", "<font", "<mark", "<img"];
            foreach (string tag in formattingTags)
            {
                if (html.Contains(tag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    [GeneratedRegex(@"\\(b(?!0\b)|i(?!0\b)|ul(?!none\b|0\b)|strike(?!0\b)|field|pict)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RtfSignificantFormattingRegex();

    private static bool TryReadFilePaths(out IReadOnlyList<string>? filePaths)
    {
        filePaths = null;
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

        List<string> paths = new((int)fileCount);
        for (uint fileIndex = 0; fileIndex < fileCount; fileIndex++)
        {
            uint length = DragQueryFile(handle, fileIndex, null, 0);
            if (length == 0)
            {
                continue;
            }

            StringBuilder pathBuilder = new((int)length + 1);
            _ = DragQueryFile(handle, fileIndex, pathBuilder, (uint)pathBuilder.Capacity);
            string path = pathBuilder.ToString();

            try
            {
                if (!string.IsNullOrWhiteSpace(path) && !System.IO.Directory.Exists(path))
                {
                    paths.Add(path);
                }
            }
            catch
            {
                // Ignore paths that throw exceptions (e.g. virtual folders like the Recycle Bin)
            }
        }

        if (paths.Count == 0)
        {
            return false;
        }

        filePaths = paths;
        return true;
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

    private static (string? ProcessIdentifier, string? ExecutablePath) TryGetForegroundProcessInfo()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return (null, null);
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out uint processId);
        if (processId == 0)
        {
            return (null, null);
        }

        string? processPath = TryResolveProcessExecutablePath(processId);
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return (null, null);
        }

        string processName = Path.GetFileNameWithoutExtension(processPath);
        string? processIdentifier = string.IsNullOrWhiteSpace(processName) ? null : processName;
        return (processIdentifier, processPath);
    }

    private static string? TryResolveProcessExecutablePath(uint processId)
    {
        IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            StringBuilder buffer = new(1024);
            uint length = (uint)buffer.Capacity;
            if (!QueryFullProcessImageName(processHandle, 0, buffer, ref length))
            {
                return null;
            }

            string candidate = buffer.ToString();
            return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
        }
        finally
        {
            _ = CloseHandle(processHandle);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr processHandle, uint flags, StringBuilder executablePath, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr objectHandle);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(IntPtr hMem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);
}
