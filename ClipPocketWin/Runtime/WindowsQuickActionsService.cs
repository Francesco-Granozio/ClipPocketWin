using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ClipPocketWin.Runtime;

public sealed class WindowsQuickActionsService : IQuickActionsService
{
    private readonly IAutoPasteService _autoPasteService;
    private readonly ILogger<WindowsQuickActionsService> _logger;

    public WindowsQuickActionsService(
        IAutoPasteService autoPasteService,
        ILogger<WindowsQuickActionsService> logger)
    {
        _autoPasteService = autoPasteService;
        _logger = logger;
    }

    public async Task<Result> SaveToFileAsync(ClipboardItem item, nint ownerWindowHandle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null)
        {
            return Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null."));
        }

        if (ownerWindowHandle == nint.Zero)
        {
            return Result.Failure(new Error(ErrorCode.InvalidOperation, "Window handle is required to open the save picker."));
        }

        try
        {
            FileSavePicker picker = new();
            (string fileTypeName, string extension) = ResolveSaveType(item);
            picker.FileTypeChoices.Add(fileTypeName, [extension]);
            picker.SuggestedFileName = BuildSuggestedName(item, extension);
            InitializeWithWindow.Initialize(picker, ownerWindowHandle);

            Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return Result.Success();
            }

            return await WriteItemToPathAsync(item, file.Path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
#if DEBUG
            _logger.LogWarning(exception, "Quick action Save to file failed.");
#endif
            return Result.Failure(new Error(ErrorCode.StorageWriteFailed, "Failed to save clipboard item to file.", exception));
        }
    }

    public Task<Result> CopyAsBase64Async(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null.")));
        }

        string? textPayload = ResolveTextPayload(item);
        string encoded;
        if (!string.IsNullOrWhiteSpace(textPayload))
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(textPayload);
            encoded = Convert.ToBase64String(utf8);
        }
        else if (item.BinaryContent is { Length: > 0 } binary)
        {
            encoded = Convert.ToBase64String(binary);
        }
        else
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.ClipboardItemUnsupportedType, "Current clipboard item cannot be converted to Base64.")));
        }

        ClipboardItem output = new()
        {
            Type = ClipboardItemType.Text,
            Timestamp = DateTimeOffset.UtcNow,
            TextContent = encoded
        };

        return _autoPasteService.SetClipboardContentAsync(output, cancellationToken);
    }

    public Task<Result> UrlEncodeAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        return TransformUrlAsync(item, encode: true, cancellationToken);
    }

    public Task<Result> UrlDecodeAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        return TransformUrlAsync(item, encode: false, cancellationToken);
    }

    public Task<Result> EditTextAsync(ClipboardItem sourceItem, string editedText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceItem is null)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null.")));
        }

        if (!IsTextEditableType(sourceItem.Type))
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.ClipboardItemUnsupportedType, "Edit quick action supports only text clipboard items.")));
        }

        string nextText = editedText ?? string.Empty;
        ClipboardItemType outputType = sourceItem.Type == ClipboardItemType.RichText
            ? ClipboardItemType.Text
            : sourceItem.Type;

        ClipboardItem output = new()
        {
            Type = outputType,
            Timestamp = DateTimeOffset.UtcNow,
            TextContent = nextText
        };

        return _autoPasteService.SetClipboardContentAsync(output, cancellationToken);
    }

    private async Task<Result> TransformUrlAsync(ClipboardItem item, bool encode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null)
        {
            return Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null."));
        }

        string? text = ResolveTextPayload(item);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Failure(new Error(ErrorCode.ClipboardItemUnsupportedType, "URL actions require a text clipboard item."));
        }

        string transformed;
        try
        {
            transformed = encode ? Uri.EscapeDataString(text) : Uri.UnescapeDataString(text);
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(ErrorCode.DataFormatInvalid, "Failed to process URL encoding/decoding.", exception));
        }

        ClipboardItem output = new()
        {
            Type = ClipboardItemType.Text,
            Timestamp = DateTimeOffset.UtcNow,
            TextContent = transformed
        };

        return await _autoPasteService.SetClipboardContentAsync(output, cancellationToken);
    }

    private static async Task<Result> WriteItemToPathAsync(ClipboardItem item, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (item.Type)
        {
            case ClipboardItemType.Text:
            case ClipboardItemType.Code:
            case ClipboardItemType.Url:
            case ClipboardItemType.Email:
            case ClipboardItemType.Phone:
            case ClipboardItemType.Json:
            case ClipboardItemType.Color:
                {
                    string text = item.TextContent ?? string.Empty;
                    await File.WriteAllTextAsync(path, text, Encoding.UTF8, cancellationToken);
                    return Result.Success();
                }
            case ClipboardItemType.RichText:
                {
                    if (item.RichTextContent?.RtfData is { Length: > 0 } rtfData)
                    {
                        await File.WriteAllBytesAsync(path, rtfData, cancellationToken);
                        return Result.Success();
                    }

                    string text = item.RichTextContent?.PlainText ?? item.TextContent ?? string.Empty;
                    await File.WriteAllTextAsync(path, text, Encoding.UTF8, cancellationToken);
                    return Result.Success();
                }
            case ClipboardItemType.Image:
                {
                    if (item.BinaryContent is null || item.BinaryContent.Length == 0)
                    {
                        return Result.Failure(new Error(ErrorCode.ClipboardItemUnsupportedType, "Image clipboard item has no binary payload."));
                    }

                    if (TryBuildBitmapFromDib(item.BinaryContent, out byte[]? bmpBytes) && bmpBytes is not null)
                    {
                        await File.WriteAllBytesAsync(path, bmpBytes, cancellationToken);
                    }
                    else
                    {
                        await File.WriteAllBytesAsync(path, item.BinaryContent, cancellationToken);
                    }

                    return Result.Success();
                }
            case ClipboardItemType.File:
                {
                    if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
                    {
                        return Result.Failure(new Error(ErrorCode.NotFound, "Source file was not found for Save to file."));
                    }

                    File.Copy(item.FilePath, path, overwrite: true);
                    return Result.Success();
                }
            default:
                return Result.Failure(new Error(ErrorCode.ClipboardItemUnsupportedType, "Clipboard item type is not supported for Save to file."));
        }
    }

    private static (string TypeName, string Extension) ResolveSaveType(ClipboardItem item)
    {
        return item.Type switch
        {
            ClipboardItemType.Image => ("Bitmap image", ".bmp"),
            ClipboardItemType.RichText => ("Rich text", ".rtf"),
            ClipboardItemType.File => ("All files", Path.GetExtension(item.FilePath) is { Length: > 1 } ext ? ext : ".bin"),
            _ => ("Text file", ".txt")
        };
    }

    private static string BuildSuggestedName(ClipboardItem item, string extension)
    {
        string timestamp = item.Timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string baseName = item.Type == ClipboardItemType.File && !string.IsNullOrWhiteSpace(item.FilePath)
            ? Path.GetFileNameWithoutExtension(item.FilePath)
            : $"clipboard-{timestamp}";

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"clipboard-{timestamp}";
        }

        return baseName + extension;
    }

    private static string? ResolveTextPayload(ClipboardItem item)
    {
        return item.Type switch
        {
            ClipboardItemType.Text or ClipboardItemType.Code or ClipboardItemType.Url or ClipboardItemType.Email or ClipboardItemType.Phone or ClipboardItemType.Json or ClipboardItemType.Color
                => item.TextContent,
            ClipboardItemType.RichText
                => item.RichTextContent?.PlainText ?? item.TextContent,
            ClipboardItemType.File
                => item.FilePath ?? item.TextContent,
            _
                => null
        };
    }

    private static bool IsTextEditableType(ClipboardItemType type)
    {
        return type is ClipboardItemType.Text
            or ClipboardItemType.Code
            or ClipboardItemType.Url
            or ClipboardItemType.Email
            or ClipboardItemType.Phone
            or ClipboardItemType.Json
            or ClipboardItemType.Color
            or ClipboardItemType.RichText;
    }

    private static bool TryBuildBitmapFromDib(byte[] dibPayload, out byte[]? bmpBytes)
    {
        bmpBytes = null;
        if (dibPayload.Length < 40)
        {
            return false;
        }

        int headerSize = BitConverter.ToInt32(dibPayload, 0);
        if (headerSize < 40 || headerSize > dibPayload.Length)
        {
            return false;
        }

        short bitsPerPixel = BitConverter.ToInt16(dibPayload, 14);
        int compression = BitConverter.ToInt32(dibPayload, 16);
        int colorsUsed = BitConverter.ToInt32(dibPayload, 32);

        int colorTableEntries = colorsUsed;
        if (colorTableEntries == 0 && bitsPerPixel is > 0 and <= 8)
        {
            colorTableEntries = 1 << bitsPerPixel;
        }

        int maskBytes = (compression is 3 or 6) ? 12 : 0;
        int colorTableBytes = colorTableEntries * 4;
        int pixelDataOffset = 14 + headerSize + maskBytes + colorTableBytes;
        if (pixelDataOffset > int.MaxValue - dibPayload.Length)
        {
            return false;
        }

        int fileSize = 14 + dibPayload.Length;
        byte[] fileHeader = new byte[14];
        fileHeader[0] = (byte)'B';
        fileHeader[1] = (byte)'M';
        Array.Copy(BitConverter.GetBytes(fileSize), 0, fileHeader, 2, 4);
        Array.Copy(BitConverter.GetBytes(pixelDataOffset), 0, fileHeader, 10, 4);

        bmpBytes = new byte[fileSize];
        Buffer.BlockCopy(fileHeader, 0, bmpBytes, 0, fileHeader.Length);
        Buffer.BlockCopy(dibPayload, 0, bmpBytes, fileHeader.Length, dibPayload.Length);
        return true;
    }
}
