using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipPocketWin.Infrastructure.Clipboard;

public sealed partial class WindowsClipboardMonitor
{
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

            if (TryReadRichTextContent(captureRichTextEnabled, out RichTextContent? richTextContent))
            {
                return Result<IReadOnlyList<ClipboardItem>>.Success([
                    new ClipboardItem
                    {
                        Type = ClipboardItemType.Text,
                        Timestamp = now,
                        SourceApplicationIdentifier = sourceApplicationIdentifier,
                        SourceApplicationExecutablePath = sourceApplicationExecutablePath,
                        RichTextContent = richTextContent,
                        TextContent = richTextContent!.PlainText
                    }
                ]);
            }


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
}
