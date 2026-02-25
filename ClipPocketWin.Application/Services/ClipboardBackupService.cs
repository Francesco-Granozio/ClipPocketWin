using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using System.Text.Json;

namespace ClipPocketWin.Application.Services;

public sealed class ClipboardBackupService : IClipboardBackupService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IClipboardStateService _clipboardStateService;

    public ClipboardBackupService(IClipboardStateService clipboardStateService)
    {
        _clipboardStateService = clipboardStateService;
    }

    public Task<Result<byte[]>> ExportBackupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ClipboardItem[] history = _clipboardStateService.ClipboardItems
                .Where(item => item.Type != ClipboardItemType.Image || item.BinaryContent?.Length <= DomainLimits.MaxPersistedImageBytes)
                .Take(DomainLimits.MaxHistoryItemsHardLimit)
                .ToArray();

            PinnedClipboardItem[] pinned = _clipboardStateService.PinnedItems
                .Take(DomainLimits.MaxPinnedItems)
                .ToArray();

            ClipboardBackupPayload payload = new()
            {
                Version = 1,
                History = history,
                Pinned = pinned
            };

            return Task.FromResult(Result<byte[]>.Success(JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Task.FromResult(Result<byte[]>.Failure(new Error(ErrorCode.BackupExportFailed, "Failed to export clipboard backup.", exception)));
        }
    }

    public async Task<Result> ImportBackupAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        if (payload is null)
        {
            return Result.Failure(new Error(ErrorCode.BackupPayloadInvalid, "Backup payload cannot be null."));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            ClipboardBackupPayload? backup = JsonSerializer.Deserialize<ClipboardBackupPayload>(payload, SerializerOptions);
            if (backup is null)
            {
                return Result.Failure(new Error(ErrorCode.BackupPayloadInvalid, "Backup payload could not be deserialized."));
            }

            if (backup.Version != 1)
            {
                return Result.Failure(new Error(ErrorCode.BackupVersionUnsupported, $"Backup version {backup.Version} is not supported."));
            }

            Result clearResult = await _clipboardStateService.ClearClipboardHistoryAsync(cancellationToken);
            if (clearResult.IsFailure)
            {
                return Result.Failure(new Error(ErrorCode.BackupImportFailed, "Failed to clear history before backup import.", clearResult.Error?.Exception));
            }

            foreach (ClipboardItem item in backup.History.Take(DomainLimits.MaxHistoryItemsHardLimit))
            {
                Result addResult = await _clipboardStateService.AddClipboardItemAsync(item, cancellationToken);
                if (addResult.IsFailure)
                {
                    return Result.Failure(new Error(ErrorCode.BackupImportFailed, "Failed while importing history items from backup.", addResult.Error?.Exception));
                }
            }

            foreach (PinnedClipboardItem pinnedItem in _clipboardStateService.PinnedItems.ToArray())
            {
                Result unpinResult = await _clipboardStateService.TogglePinAsync(pinnedItem.OriginalItem, cancellationToken);
                if (unpinResult.IsFailure)
                {
                    return Result.Failure(new Error(ErrorCode.BackupImportFailed, "Failed while resetting pinned items before backup import.", unpinResult.Error?.Exception));
                }
            }

            foreach (PinnedClipboardItem pinnedItem in backup.Pinned.Take(DomainLimits.MaxPinnedItems))
            {
                Result pinResult = await _clipboardStateService.TogglePinAsync(pinnedItem.OriginalItem, cancellationToken);
                if (pinResult.IsFailure)
                {
                    return Result.Failure(new Error(ErrorCode.BackupImportFailed, "Failed while importing pinned items from backup.", pinResult.Error?.Exception));
                }
            }

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            return Result.Failure(new Error(ErrorCode.BackupPayloadInvalid, "Backup payload JSON is invalid.", exception));
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(ErrorCode.BackupImportFailed, "Unexpected failure during backup import.", exception));
        }
    }
}
