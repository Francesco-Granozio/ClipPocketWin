using System;
using System.Collections.Generic;
using System.Linq;
using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Models;

namespace ClipPocketWin.Application.Services;

internal sealed class ClipboardStateStore
{
    private readonly object _syncRoot = new();
    private List<ClipboardItem> _clipboardItems = [];
    private List<PinnedClipboardItem> _pinnedItems = [];
    private ClipboardItem[] _clipboardItemsSnapshot = [];
    private PinnedClipboardItem[] _pinnedItemsSnapshot = [];
    private ClipPocketSettings _settings = new();
    private bool _runtimeStarted;

    public IReadOnlyList<ClipboardItem> ClipboardItems
    {
        get
        {
            lock (_syncRoot)
            {
                return _clipboardItemsSnapshot;
            }
        }
    }

    public IReadOnlyList<PinnedClipboardItem> PinnedItems
    {
        get
        {
            lock (_syncRoot)
            {
                return _pinnedItemsSnapshot;
            }
        }
    }

    public ClipPocketSettings Settings
    {
        get
        {
            lock (_syncRoot)
            {
                return _settings;
            }
        }
    }

    public bool RuntimeStarted
    {
        get
        {
            lock (_syncRoot)
            {
                return _runtimeStarted;
            }
        }
    }

    public bool CaptureRichTextEnabled
    {
        get
        {
            lock (_syncRoot)
            {
                return _settings.CaptureRichText;
            }
        }
    }

    public void Initialize(
        ClipPocketSettings settings,
        IReadOnlyList<ClipboardItem> historyItems,
        IReadOnlyList<PinnedClipboardItem> pinnedItems)
    {
        lock (_syncRoot)
        {
            _settings = settings;
            _clipboardItems = historyItems.ToList();

            int targetLimit = _settings.EffectiveHistoryLimit;
            if (_clipboardItems.Count > targetLimit)
            {
                _clipboardItems.RemoveRange(targetLimit, _clipboardItems.Count - targetLimit);
            }

            _pinnedItems = pinnedItems.ToList();
            RefreshSnapshotsUnsafe();
        }
    }

    public bool TryAddClipboardItem(ClipboardItem item)
    {
        lock (_syncRoot)
        {
            if (_settings.IncognitoMode)
            {
                return false;
            }

            if (IsExcludedBySourceApplication(item, _settings))
            {
                return false;
            }

            if (_clipboardItems.Any(existing => existing.IsEquivalentContent(item)))
            {
                return false;
            }

            _clipboardItems.Insert(0, item);

            int targetLimit = _settings.EffectiveHistoryLimit;
            if (_clipboardItems.Count > targetLimit)
            {
                _clipboardItems.RemoveRange(targetLimit, _clipboardItems.Count - targetLimit);
            }

            RefreshSnapshotsUnsafe();
            return true;
        }
    }

    public bool RemoveClipboardItem(Guid id)
    {
        lock (_syncRoot)
        {
            bool historyChanged = _clipboardItems.RemoveAll(x => x.Id == id) > 0;
            bool pinnedChanged = _pinnedItems.RemoveAll(x => x.OriginalItem.Id == id) > 0;
            bool changed = historyChanged || pinnedChanged;

            if (changed)
            {
                RefreshSnapshotsUnsafe();
            }

            return changed;
        }
    }

    public void ClearClipboardHistory()
    {
        lock (_syncRoot)
        {
            _clipboardItems.Clear();
            RefreshSnapshotsUnsafe();
        }
    }

    public void TogglePin(ClipboardItem item)
    {
        lock (_syncRoot)
        {
            int index = _pinnedItems.FindIndex(x => x.OriginalItem.IsEquivalentContent(item));
            if (index >= 0)
            {
                _pinnedItems.RemoveAt(index);
            }
            else
            {
                _pinnedItems.Insert(0, new PinnedClipboardItem { OriginalItem = item });
                if (_pinnedItems.Count > DomainLimits.MaxPinnedItems)
                {
                    _pinnedItems.RemoveRange(DomainLimits.MaxPinnedItems, _pinnedItems.Count - DomainLimits.MaxPinnedItems);
                }
            }

            RefreshSnapshotsUnsafe();
        }
    }

    public ClipboardStateSettingsUpdate ApplySettings(ClipPocketSettings settings)
    {
        lock (_syncRoot)
        {
            bool captureRichTextChanged = _settings.CaptureRichText != settings.CaptureRichText;
            _settings = settings;

            bool historyLimitEnforced = false;
            int targetLimit = _settings.EffectiveHistoryLimit;
            if (_clipboardItems.Count > targetLimit)
            {
                _clipboardItems.RemoveRange(targetLimit, _clipboardItems.Count - targetLimit);
                historyLimitEnforced = true;
            }

            RefreshSnapshotsUnsafe();
            return new ClipboardStateSettingsUpdate(captureRichTextChanged, historyLimitEnforced);
        }
    }

    public ClipboardItem? ResolveClipboardItem(Guid id)
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

    public void MarkRuntimeStarted()
    {
        lock (_syncRoot)
        {
            _runtimeStarted = true;
        }
    }

    public void MarkRuntimeStopped()
    {
        lock (_syncRoot)
        {
            _runtimeStarted = false;
        }
    }

    public IReadOnlyList<ClipboardItem> BuildPersistableHistorySnapshot()
    {
        lock (_syncRoot)
        {
            List<ClipboardItem> filtered = new(_clipboardItems.Count);
            for (int i = 0; i < _clipboardItems.Count; i++)
            {
                ClipboardItem item = _clipboardItems[i];
                if (!item.CanPersist(DomainLimits.MaxPersistedImageBytes))
                {
                    continue;
                }

                filtered.Add(item);
            }

            return filtered;
        }
    }

    private void RefreshSnapshotsUnsafe()
    {
        _clipboardItemsSnapshot = [.. _clipboardItems];
        _pinnedItemsSnapshot = [.. _pinnedItems];
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
}
