using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Infrastructure.Errors;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ClipPocketWin.Infrastructure.Persistence;

public sealed class FileClipboardHistoryRepository : IClipboardHistoryRepository
{
    private readonly ILogger<FileClipboardHistoryRepository> _logger;

    public FileClipboardHistoryRepository(ILogger<FileClipboardHistoryRepository> logger)
    {
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ClipboardItem>>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(StoragePaths.ClipboardHistoryJson))
            {
                return Result<IReadOnlyList<ClipboardItem>>.Success([]);
            }

            byte[] data = await File.ReadAllBytesAsync(StoragePaths.ClipboardHistoryJson, cancellationToken);
            if (data.Length == 0)
            {
                return Result<IReadOnlyList<ClipboardItem>>.Success([]);
            }

            List<ClipboardItem> items = JsonSerializer.Deserialize<List<ClipboardItem>>(data, JsonSerialization.Options) ?? [];
            ClipboardItem[] filtered = items
                .Where(item => item.Type != ClipboardItemType.Image || item.BinaryContent?.Length <= DomainLimits.MaxPersistedImageBytes)
                .Take(DomainLimits.MaxHistoryItemsHardLimit)
                .ToArray();

            return Result<IReadOnlyList<ClipboardItem>>.Success(filtered);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to load clipboard history from storage.";
            ErrorCode fallback = exception is JsonException ? ErrorCode.DeserializationFailed : ErrorCode.StorageReadFailed;
            return Result<IReadOnlyList<ClipboardItem>>.Failure(InfrastructureErrorFactory.FromException(exception, context, fallback));
        }
    }

    public async Task<Result> SaveAsync(IReadOnlyList<ClipboardItem> items, CancellationToken cancellationToken = default)
    {
        if (items is null)
        {
            return Result.Failure(new Error(ErrorCode.ValidationError, "Cannot save null clipboard history."));
        }

        try
        {
            string destinationPath = StoragePaths.ClipboardHistoryJson;

            ClipboardItem[] filteredItems = items
                .Where(item => item.Type != ClipboardItemType.Image || item.BinaryContent?.Length <= DomainLimits.MaxPersistedImageBytes)
                .Take(DomainLimits.MaxHistoryItemsHardLimit)
                .ToArray();

            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(filteredItems, JsonSerialization.Options);

            await File.WriteAllBytesAsync(destinationPath, serialized, cancellationToken);

            _logger.LogInformation("Saved {Count} clipboard items to {Path}", filteredItems.Length, destinationPath);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to save clipboard history to storage.";
            return Result.Failure(InfrastructureErrorFactory.FromException(exception, context, ErrorCode.StorageWriteFailed));
        }
    }

    public Task<Result> ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(StoragePaths.ClipboardHistoryJson))
            {
                File.Delete(StoragePaths.ClipboardHistoryJson);
            }

            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            string context = "Failed to clear clipboard history files.";
            return Task.FromResult(Result.Failure(InfrastructureErrorFactory.FromException(exception, context, ErrorCode.StorageDeleteFailed)));
        }
    }
}
