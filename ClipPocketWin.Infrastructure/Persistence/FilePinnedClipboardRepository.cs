using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Infrastructure.Errors;
using ClipPocketWin.Shared.ResultPattern;
using System.Text.Json;

namespace ClipPocketWin.Infrastructure.Persistence;

public sealed class FilePinnedClipboardRepository : IPinnedClipboardRepository
{
    public async Task<Result<IReadOnlyList<PinnedClipboardItem>>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(StoragePaths.PinnedItemsJson))
            {
                return Result<IReadOnlyList<PinnedClipboardItem>>.Success([]);
            }

            byte[] data = await File.ReadAllBytesAsync(StoragePaths.PinnedItemsJson, cancellationToken);
            if (data.Length == 0)
            {
                return Result<IReadOnlyList<PinnedClipboardItem>>.Success([]);
            }

            List<PinnedClipboardItem> items = JsonSerializer.Deserialize<List<PinnedClipboardItem>>(data, JsonSerialization.Options) ?? [];
            return Result<IReadOnlyList<PinnedClipboardItem>>.Success(items.Take(DomainLimits.MaxPinnedItems).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to load pinned clipboard items.";
            ErrorCode fallback = exception is JsonException ? ErrorCode.DeserializationFailed : ErrorCode.StorageReadFailed;
            return Result<IReadOnlyList<PinnedClipboardItem>>.Failure(InfrastructureErrorFactory.FromException(exception, context, fallback));
        }
    }

    public async Task<Result> SaveAsync(IReadOnlyList<PinnedClipboardItem> items, CancellationToken cancellationToken = default)
    {
        if (items is null)
        {
            return Result.Failure(new Error(ErrorCode.ValidationError, "Cannot save null pinned items collection."));
        }

        try
        {
            PinnedClipboardItem[] filtered = items.Take(DomainLimits.MaxPinnedItems).ToArray();
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(filtered, JsonSerialization.Options);
            await File.WriteAllBytesAsync(StoragePaths.PinnedItemsJson, data, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string context = "Failed to save pinned clipboard items.";
            return Result.Failure(InfrastructureErrorFactory.FromException(exception, context, ErrorCode.StorageWriteFailed));
        }
    }
}
