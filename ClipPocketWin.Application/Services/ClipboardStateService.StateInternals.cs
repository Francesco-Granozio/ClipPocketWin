using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Services;

public sealed partial class ClipboardStateService
{
    private async Task<Result> PersistHistoryAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ClipboardItem> snapshot;
        ClipPocketSettings settings;

        lock (_syncRoot)
        {
            settings = _settings;
            int maxItems = Math.Min(_clipboardItems.Count, DomainLimits.MaxHistoryItemsHardLimit);
            List<ClipboardItem> filtered = new(maxItems);
            for (int i = 0; i < _clipboardItems.Count && filtered.Count < DomainLimits.MaxHistoryItemsHardLimit; i++)
            {
                ClipboardItem item = _clipboardItems[i];
                if (item.Type == ClipboardItemType.Image && item.BinaryContent?.Length > DomainLimits.MaxPersistedImageBytes)
                {
                    continue;
                }

                filtered.Add(item);
            }

            snapshot = filtered;
        }

        if (!settings.RememberHistory)
        {
            return Result.Success();
        }

        Result saveResult = await _historyRepository.SaveAsync(snapshot, cancellationToken);
        if (saveResult.IsFailure)
        {
            return Result.Failure(new Error(ErrorCode.StatePersistenceFailed, "Failed to persist clipboard history.", saveResult.Error?.Exception));
        }

        return Result.Success();
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsExcludedBySourceApplication(ClipboardItem item, ClipPocketSettings settings)
    {
        if (settings.ExcludedAppIds.Count == 0 || string.IsNullOrWhiteSpace(item.SourceApplicationIdentifier))
        {
            return false;
        }

        string sourceId = item.SourceApplicationIdentifier.Trim();
        string sourceIdWithoutExtension = sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? sourceId[..^4]
            : sourceId;

        foreach (string excluded in settings.ExcludedAppIds)
        {
            if (string.IsNullOrWhiteSpace(excluded))
            {
                continue;
            }

            string candidate = excluded.Trim();
            if (string.Equals(candidate, sourceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, sourceIdWithoutExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private Task<Result> HandleClipboardItemCapturedAsync(ClipboardItem item)
    {
        return AddClipboardItemAsync(item);
    }

    private ClipboardItem? ResolveClipboardItem(Guid id)
    {
        lock (_syncRoot)
        {
            for (int i = 0; i < _clipboardItems.Count; i++)
            {
                ClipboardItem item = _clipboardItems[i];
                if (item.Id == id)
                {
                    return item;
                }
            }

            for (int i = 0; i < _pinnedItems.Count; i++)
            {
                ClipboardItem item = _pinnedItems[i].OriginalItem;
                if (item.Id == id)
                {
                    return item;
                }
            }

            return null;
        }
    }

    private void RefreshSnapshotsUnsafe()
    {
        _clipboardItemsSnapshot = [.. _clipboardItems];
        _pinnedItemsSnapshot = [.. _pinnedItems];
    }
}
