using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.Imaging;
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

        string? textPayload = item.ResolveTextPayload();
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

        ClipboardItem output = ClipboardItemFactory.CreateText(
            ClipboardItemType.Text,
            DateTimeOffset.UtcNow,
            encoded);

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

        if (!sourceItem.CanEditText)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.ClipboardItemUnsupportedType, "Edit quick action supports only text clipboard items.")));
        }

        string nextText = editedText ?? string.Empty;
        ClipboardItemType outputType = sourceItem.IsRichText
            ? ClipboardItemType.Text
            : sourceItem.Type;

        ClipboardItem output = ClipboardItemFactory.CreateText(
            outputType,
            DateTimeOffset.UtcNow,
            nextText);

        return _autoPasteService.SetClipboardContentAsync(output, cancellationToken);
    }

    private async Task<Result> TransformUrlAsync(ClipboardItem item, bool encode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item is null)
        {
            return Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null."));
        }

        string? text = item.ResolveTextPayload();
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

        ClipboardItem output = ClipboardItemFactory.CreateText(
            ClipboardItemType.Text,
            DateTimeOffset.UtcNow,
            transformed);

        return await _autoPasteService.SetClipboardContentAsync(output, cancellationToken);
    }

    private static async Task<Result> WriteItemToPathAsync(ClipboardItem item, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (item.IsRichText && item.RichTextContent?.RtfData is { Length: > 0 } rtfData)
        {
            await File.WriteAllBytesAsync(path, rtfData, cancellationToken);
            return Result.Success();
        }

        if (item.IsImage)
        {
            if (item.BinaryContent is null || item.BinaryContent.Length == 0)
            {
                return Result.Failure(new Error(ErrorCode.ClipboardItemUnsupportedType, "Image clipboard item has no binary payload."));
            }

            if (DibBitmapConverter.TryBuildBitmapFromDib(item.BinaryContent, out byte[]? bmpBytes) && bmpBytes is not null)
            {
                await File.WriteAllBytesAsync(path, bmpBytes, cancellationToken);
            }
            else
            {
                await File.WriteAllBytesAsync(path, item.BinaryContent, cancellationToken);
            }

            return Result.Success();
        }

        if (item.IsFile)
        {
            if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
            {
                return Result.Failure(new Error(ErrorCode.NotFound, "Source file was not found for Save to file."));
            }

            File.Copy(item.FilePath, path, overwrite: true);
            return Result.Success();
        }

        string? textPayload = item.ResolveTextPayload();
        if (!string.IsNullOrWhiteSpace(textPayload))
        {
            await File.WriteAllTextAsync(path, textPayload, Encoding.UTF8, cancellationToken);
            return Result.Success();
        }

        return Result.Failure(new Error(ErrorCode.ClipboardItemUnsupportedType, "Clipboard item type is not supported for Save to file."));
    }

    private static (string TypeName, string Extension) ResolveSaveType(ClipboardItem item)
    {
        if (item.IsImage)
        {
            return ("Bitmap image", ".bmp");
        }

        if (item.IsRichText)
        {
            return ("Rich text", ".rtf");
        }

        if (item.IsFile)
        {
            string extension = Path.GetExtension(item.FilePath) ?? string.Empty;
            return ("All files", extension is { Length: > 1 } ext ? ext : ".bin");
        }

        return ("Text file", ".txt");
    }

    private static string BuildSuggestedName(ClipboardItem item, string extension)
    {
        string timestamp = item.Timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string baseName = item.IsFile && !string.IsNullOrWhiteSpace(item.FilePath)
            ? Path.GetFileNameWithoutExtension(item.FilePath)
            : $"clipboard-{timestamp}";

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"clipboard-{timestamp}";
        }

        return baseName + extension;
    }

}
