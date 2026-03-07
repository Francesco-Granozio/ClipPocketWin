using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;

namespace ClipPocketWin.Application.Services;

public sealed class ClipboardStateService : IClipboardStateService
{
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly ClipboardStateStore _stateStore;
    private readonly ClipboardStatePersistenceCoordinator _persistenceCoordinator;
    private readonly ClipboardSelectionService _selectionService;
    private readonly ILogger<ClipboardStateService> _logger;

    public ClipboardStateService(
        IClipboardMonitor clipboardMonitor,
        IClipboardHistoryRepository historyRepository,
        IPinnedClipboardRepository pinnedRepository,
        ISettingsRepository settingsRepository,
        IAutoPasteService autoPasteService,
        ILogger<ClipboardStateService> logger)
    {
        _clipboardMonitor = clipboardMonitor;
        _stateStore = new ClipboardStateStore();
        _persistenceCoordinator = new ClipboardStatePersistenceCoordinator(historyRepository, pinnedRepository, settingsRepository);
        _selectionService = new ClipboardSelectionService(autoPasteService);
        _logger = logger;
    }

    public event EventHandler? StateChanged;

    public IReadOnlyList<ClipboardItem> ClipboardItems => _stateStore.ClipboardItems;

    public IReadOnlyList<PinnedClipboardItem> PinnedItems => _stateStore.PinnedItems;

    public ClipPocketSettings Settings => _stateStore.Settings;

    public async Task<Result> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Result<ClipboardStateInitializationData> loadResult = await _persistenceCoordinator.LoadAsync(cancellationToken);
            if (loadResult.IsFailure)
            {
                return Result.Failure(loadResult.Error!);
            }

            ClipboardStateInitializationData state = loadResult.Value;
            _stateStore.Initialize(state.Settings, state.HistoryItems, state.PinnedItems);

#if DEBUG
            _logger.LogInformation(
                "Initialized state with {HistoryCount} history items and {PinnedCount} pinned items",
                _stateStore.ClipboardItems.Count,
                _stateStore.PinnedItems.Count);
#endif
            OnStateChanged();
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(
                ErrorCode.StateInitializationFailed,
                "Unexpected failure while initializing state.",
                exception));
        }
    }

    public async Task<Result> AddClipboardItemAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        if (item is null)
        {
            return Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null."));
        }

        bool changed = _stateStore.TryAddClipboardItem(item);
        if (!changed)
        {
            return Result.Success();
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
        if (_stateStore.RuntimeStarted)
        {
            return Result.Success();
        }

        bool captureRichTextEnabled = _stateStore.CaptureRichTextEnabled;
        Result startResult = await _clipboardMonitor.StartAsync(HandleClipboardItemCapturedAsync, captureRichTextEnabled, cancellationToken);
        if (startResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.ClipboardMonitorStartFailed,
                "Failed to start clipboard runtime monitor.",
                startResult.Error?.Exception));
        }

        _stateStore.MarkRuntimeStarted();

#if DEBUG
        _logger.LogInformation("Clipboard runtime monitor started.");
#endif
        return Result.Success();
    }

    public async Task<Result> StopRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (!_stateStore.RuntimeStarted)
        {
            return Result.Success();
        }

        Result stopResult = await _clipboardMonitor.StopAsync(cancellationToken);
        if (stopResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.InvalidOperation,
                "Failed to stop clipboard runtime monitor.",
                stopResult.Error?.Exception));
        }

        _stateStore.MarkRuntimeStopped();

#if DEBUG
        _logger.LogInformation("Clipboard runtime monitor stopped.");
#endif
        return Result.Success();
    }

    public async Task<Result> DeleteClipboardItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        bool changed = _stateStore.RemoveClipboardItem(id);
        if (!changed)
        {
            return Result.Success();
        }

        Result historyPersistResult = await PersistHistoryAsync(cancellationToken);
        if (historyPersistResult.IsFailure)
        {
            return historyPersistResult;
        }

        Result pinnedPersistResult = await _persistenceCoordinator.SavePinnedAsync(_stateStore.PinnedItems, cancellationToken);
        if (pinnedPersistResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.StatePersistenceFailed,
                "Failed to persist pinned items after deleting clipboard item.",
                pinnedPersistResult.Error?.Exception));
        }

        OnStateChanged();
        return Result.Success();
    }

    public async Task<Result> SelectClipboardItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ClipboardItem? item = _stateStore.ResolveClipboardItem(id);
        if (item is null)
        {
            return Result.Failure(new Error(
                ErrorCode.ClipboardHistoryItemNotFound,
                $"Clipboard item with id '{id}' was not found."));
        }

        bool autoPasteEnabled = _stateStore.Settings.AutoPasteEnabled;
        return await _selectionService.SelectClipboardItemAsync(item, autoPasteEnabled, cancellationToken);
    }

    public async Task<Result> CopyClipboardItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ClipboardItem? item = _stateStore.ResolveClipboardItem(id);
        if (item is null)
        {
            return Result.Failure(new Error(
                ErrorCode.ClipboardHistoryItemNotFound,
                $"Clipboard item with id '{id}' was not found."));
        }

        return await _selectionService.CopyClipboardItemAsync(item, cancellationToken);
    }

    public async Task<Result> PasteClipboardItemAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ClipboardItem? item = _stateStore.ResolveClipboardItem(id);
        if (item is null)
        {
            return Result.Failure(new Error(
                ErrorCode.ClipboardHistoryItemNotFound,
                $"Clipboard item with id '{id}' was not found."));
        }

        return await _selectionService.PasteClipboardItemAsync(item, cancellationToken);
    }

    public async Task<Result> ClearClipboardHistoryAsync(CancellationToken cancellationToken = default)
    {
        _stateStore.ClearClipboardHistory();

        Result clearResult = await _persistenceCoordinator.ClearHistoryAsync(cancellationToken);
        if (clearResult.IsFailure)
        {
            return clearResult;
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

        _stateStore.TogglePin(item);

        Result saveResult = await _persistenceCoordinator.SavePinnedAsync(_stateStore.PinnedItems, cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult;
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

        ClipboardStateSettingsUpdate update = _stateStore.ApplySettings(settings);

        Result saveSettingsResult = await _persistenceCoordinator.SaveSettingsAsync(settings, cancellationToken);
        if (saveSettingsResult.IsFailure)
        {
            return saveSettingsResult;
        }

        if (update.HistoryLimitEnforced)
        {
            Result persistResult = await PersistHistoryAsync(cancellationToken);
            if (persistResult.IsFailure)
            {
                return persistResult;
            }
        }

        if (update.CaptureRichTextChanged)
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
        ClipPocketSettings settings = _stateStore.Settings;
        if (!settings.RememberHistory)
        {
            return Result.Success();
        }

        IReadOnlyList<ClipboardItem> snapshot = _stateStore.BuildPersistableHistorySnapshot();
        return await _persistenceCoordinator.SaveHistoryAsync(snapshot, cancellationToken);
    }

    private Task<Result> HandleClipboardItemCapturedAsync(ClipboardItem item)
    {
        return AddClipboardItemAsync(item);
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
