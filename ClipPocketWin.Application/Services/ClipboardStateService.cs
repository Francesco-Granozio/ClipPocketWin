using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain;
using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;

namespace ClipPocketWin.Application.Services;

public sealed class ClipboardStateService : IClipboardStateService
{
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly IClipboardHistoryRepository _historyRepository;
    private readonly IPinnedClipboardRepository _pinnedRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IAutoPasteService _autoPasteService;
    private readonly ILogger<ClipboardStateService> _logger;
    private readonly object _syncRoot = new();

    private List<ClipboardItem> _clipboardItems = [];
    private List<PinnedClipboardItem> _pinnedItems = [];
    private ClipPocketSettings _settings = new();
    private bool _runtimeStarted;

    public ClipboardStateService(
        IClipboardMonitor clipboardMonitor,
        IClipboardHistoryRepository historyRepository,
        IPinnedClipboardRepository pinnedRepository,
        ISettingsRepository settingsRepository,
        IAutoPasteService autoPasteService,
        ILogger<ClipboardStateService> logger)
    {
        _clipboardMonitor = clipboardMonitor;
        _historyRepository = historyRepository;
        _pinnedRepository = pinnedRepository;
        _settingsRepository = settingsRepository;
        _autoPasteService = autoPasteService;
        _logger = logger;
    }

    public event EventHandler? StateChanged;

    public IReadOnlyList<ClipboardItem> ClipboardItems
    {
        get
        {
            lock (_syncRoot)
            {
                return _clipboardItems.ToArray();
            }
        }
    }

    public IReadOnlyList<PinnedClipboardItem> PinnedItems
    {
        get
        {
            lock (_syncRoot)
            {
                return _pinnedItems.ToArray();
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

    public async Task<Result> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Result<ClipPocketSettings> settingsResult = await _settingsRepository.LoadAsync(cancellationToken);
            if (settingsResult.IsFailure)
            {
                return Result.Failure(new Error(ErrorCode.StateInitializationFailed, "Failed to initialize state settings.", settingsResult.Error?.Exception));
            }

            ClipPocketSettings settings = settingsResult.Value!;

            Result<IReadOnlyList<ClipboardItem>> historyResult = settings.RememberHistory
                ? await _historyRepository.LoadAsync(cancellationToken)
                : Result<IReadOnlyList<ClipboardItem>>.Success([]);

            if (historyResult.IsFailure)
            {
                return Result.Failure(new Error(ErrorCode.StateInitializationFailed, "Failed to initialize clipboard history state.", historyResult.Error?.Exception));
            }

            Result<IReadOnlyList<PinnedClipboardItem>> pinnedResult = await _pinnedRepository.LoadAsync(cancellationToken);
            if (pinnedResult.IsFailure)
            {
                return Result.Failure(new Error(ErrorCode.StateInitializationFailed, "Failed to initialize pinned state.", pinnedResult.Error?.Exception));
            }

            lock (_syncRoot)
            {
                _settings = settings;
                int targetLimit = _settings.EffectiveHistoryLimit;
                _clipboardItems = historyResult.Value!.ToList();
                if (_clipboardItems.Count > targetLimit)
                {
                    _clipboardItems = _clipboardItems.Take(targetLimit).ToList();
                }
                _pinnedItems = pinnedResult.Value!.ToList();
            }

            _logger.LogInformation("Initialized state with {HistoryCount} history items and {PinnedCount} pinned items", _clipboardItems.Count, _pinnedItems.Count);
            OnStateChanged();
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(ErrorCode.StateInitializationFailed, "Unexpected failure while initializing state.", exception));
        }
    }

    public async Task<Result> AddClipboardItemAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            return Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null."));
        }

        lock (_syncRoot)
        {
            if (_settings.IncognitoMode)
            {
                return Result.Success();
            }

            if (IsExcludedBySourceApplication(item, _settings))
            {
                return Result.Success();
            }

            if (_clipboardItems.Any(existing => existing.IsEquivalentContent(item)))
            {
                return Result.Success();
            }

            _clipboardItems.Insert(0, item);

            int targetLimit = _settings.EffectiveHistoryLimit;
            if (_clipboardItems.Count > targetLimit)
            {
                _clipboardItems = _clipboardItems.Take(targetLimit).ToList();
            }
        }

        Result persistResult = await PersistHistoryAsync(cancellationToken);
        if (persistResult.IsFailure)
        {
            return persistResult;
        }

        OnStateChanged();
        return Result.Success();
    }

    public async Task<Result> StartRuntimeAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_runtimeStarted)
            {
                return Result.Success();
            }
        }

        bool captureRichTextEnabled;
        lock (_syncRoot)
        {
            captureRichTextEnabled = _settings.CaptureRichText;
        }

        Result startResult = await _clipboardMonitor.StartAsync(HandleClipboardItemCapturedAsync, captureRichTextEnabled, cancellationToken);
        if (startResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.ClipboardMonitorStartFailed,
                "Failed to start clipboard runtime monitor.",
                startResult.Error?.Exception));
        }

        lock (_syncRoot)
        {
            _runtimeStarted = true;
        }

        _logger.LogInformation("Clipboard runtime monitor started.");
        return Result.Success();
    }

    public async Task<Result> StopRuntimeAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (!_runtimeStarted)
            {
                return Result.Success();
            }
        }

        Result stopResult = await _clipboardMonitor.StopAsync(cancellationToken);
        if (stopResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.InvalidOperation,
                "Failed to stop clipboard runtime monitor.",
                stopResult.Error?.Exception));
        }

        lock (_syncRoot)
        {
            _runtimeStarted = false;
        }

        _logger.LogInformation("Clipboard runtime monitor stopped.");
        return Result.Success();
    }

    public async Task<Result> DeleteClipboardItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        bool changed;
        lock (_syncRoot)
        {
            changed = _clipboardItems.RemoveAll(x => x.Id == id) > 0;
            _pinnedItems = _pinnedItems.Where(x => x.OriginalItem.Id != id).ToList();
        }

        if (!changed)
        {
            return Result.Success();
        }

        Result historyPersistResult = await PersistHistoryAsync(cancellationToken);
        if (historyPersistResult.IsFailure)
        {
            return historyPersistResult;
        }

        Result pinnedPersistResult = await _pinnedRepository.SaveAsync(PinnedItems, cancellationToken);
        if (pinnedPersistResult.IsFailure)
        {
            return Result.Failure(new Error(ErrorCode.StatePersistenceFailed, "Failed to persist pinned items after deleting clipboard item.", pinnedPersistResult.Error?.Exception));
        }

        OnStateChanged();
        return Result.Success();
    }

    public async Task<Result> SelectClipboardItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ClipboardItem? item = ResolveClipboardItem(id);
        if (item is null)
        {
            return Result.Failure(new Error(ErrorCode.ClipboardHistoryItemNotFound, $"Clipboard item with id '{id}' was not found."));
        }

        bool autoPasteEnabled;
        lock (_syncRoot)
        {
            autoPasteEnabled = _settings.AutoPasteEnabled;
        }

        Result setClipboardResult = await _autoPasteService.SetClipboardContentAsync(item, cancellationToken);
        if (setClipboardResult.IsFailure)
        {
            return setClipboardResult;
        }

        if (!autoPasteEnabled)
        {
            return Result.Success();
        }

        Result pasteResult = await _autoPasteService.PasteToPreviousWindowAsync(cancellationToken);
        if (pasteResult.IsFailure)
        {
            return pasteResult;
        }

        return Result.Success();
    }

    public async Task<Result> CopyClipboardItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ClipboardItem? item = ResolveClipboardItem(id);
        if (item is null)
        {
            return Result.Failure(new Error(ErrorCode.ClipboardHistoryItemNotFound, $"Clipboard item with id '{id}' was not found."));
        }

        return await _autoPasteService.SetClipboardContentAsync(item, cancellationToken);
    }

    public async Task<Result> PasteClipboardItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Result copyResult = await CopyClipboardItemAsync(id, cancellationToken);
        if (copyResult.IsFailure)
        {
            return copyResult;
        }

        return await _autoPasteService.PasteToPreviousWindowAsync(cancellationToken);
    }

    public async Task<Result> ClearClipboardHistoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _clipboardItems.Clear();
        }

        Result clearResult = await _historyRepository.ClearAsync(cancellationToken);
        if (clearResult.IsFailure)
        {
            return Result.Failure(new Error(ErrorCode.StatePersistenceFailed, "Failed to clear clipboard history.", clearResult.Error?.Exception));
        }

        OnStateChanged();
        return Result.Success();
    }

    public async Task<Result> TogglePinAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            return Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null when toggling pin."));
        }

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
                    _pinnedItems = _pinnedItems.Take(DomainLimits.MaxPinnedItems).ToList();
                }
            }
        }

        Result saveResult = await _pinnedRepository.SaveAsync(PinnedItems, cancellationToken);
        if (saveResult.IsFailure)
        {
            return Result.Failure(new Error(ErrorCode.StatePersistenceFailed, "Failed to persist pinned clipboard items.", saveResult.Error?.Exception));
        }

        OnStateChanged();
        return Result.Success();
    }

    public async Task<Result> SaveSettingsAsync(ClipPocketSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings is null)
        {
            return Result.Failure(new Error(ErrorCode.SettingsInvalid, "Settings cannot be null."));
        }

        bool captureRichTextChanged;
        bool limitEnforced = false;

        lock (_syncRoot)
        {
            captureRichTextChanged = _settings.CaptureRichText != settings.CaptureRichText;
            _settings = settings;

            int targetLimit = _settings.EffectiveHistoryLimit;
            if (_clipboardItems.Count > targetLimit)
            {
                _clipboardItems = _clipboardItems.Take(targetLimit).ToList();
                limitEnforced = true;
            }
        }

        Result saveSettingsResult = await _settingsRepository.SaveAsync(settings, cancellationToken);
        if (saveSettingsResult.IsFailure)
        {
            return Result.Failure(new Error(ErrorCode.StatePersistenceFailed, "Failed to persist settings.", saveSettingsResult.Error?.Exception));
        }

        if (limitEnforced)
        {
            Result persistResult = await PersistHistoryAsync(cancellationToken);
            if (persistResult.IsFailure)
            {
                return persistResult;
            }
        }

        if (captureRichTextChanged)
        {
            Result richTextModeResult = await _clipboardMonitor.UpdateCaptureRichTextAsync(settings.CaptureRichText, cancellationToken);
            if (richTextModeResult.IsFailure)
            {
                return Result.Failure(new Error(
                    ErrorCode.InvalidOperation,
                    "Failed to update clipboard capture rich text mode.",
                    richTextModeResult.Error?.Exception));
            }
        }

        OnStateChanged();
        return Result.Success();
    }

    private async Task<Result> PersistHistoryAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ClipboardItem> snapshot;
        ClipPocketSettings settings;

        lock (_syncRoot)
        {
            settings = _settings;
            snapshot = _clipboardItems
                .Where(item => item.Type != ClipboardItemType.Image || item.BinaryContent?.Length <= DomainLimits.MaxPersistedImageBytes)
                .Take(DomainLimits.MaxHistoryItemsHardLimit)
                .ToList();
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
            return _clipboardItems.FirstOrDefault(x => x.Id == id)
                ?? _pinnedItems.Select(x => x.OriginalItem).FirstOrDefault(x => x.Id == id);
        }
    }
}
