using ClipPocketWin.Domain.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Services;

internal sealed class ClipboardStatePersistenceCoordinator
{
    private readonly IClipboardHistoryRepository _historyRepository;
    private readonly IPinnedClipboardRepository _pinnedRepository;
    private readonly ISettingsRepository _settingsRepository;

    public ClipboardStatePersistenceCoordinator(
        IClipboardHistoryRepository historyRepository,
        IPinnedClipboardRepository pinnedRepository,
        ISettingsRepository settingsRepository)
    {
        _historyRepository = historyRepository;
        _pinnedRepository = pinnedRepository;
        _settingsRepository = settingsRepository;
    }

    public async Task<Result<ClipboardStateInitializationData>> LoadAsync(CancellationToken cancellationToken)
    {
        Result<ClipPocketSettings> settingsResult = await _settingsRepository.LoadAsync(cancellationToken);
        if (settingsResult.IsFailure)
        {
            return Result<ClipboardStateInitializationData>.Failure(new Error(
                ErrorCode.StateInitializationFailed,
                "Failed to initialize state settings.",
                settingsResult.Error?.Exception));
        }

        ClipPocketSettings settings = settingsResult.Value!;

        Result<IReadOnlyList<ClipboardItem>> historyResult = settings.RememberHistory
            ? await _historyRepository.LoadAsync(cancellationToken)
            : Result<IReadOnlyList<ClipboardItem>>.Success([]);

        if (historyResult.IsFailure)
        {
            return Result<ClipboardStateInitializationData>.Failure(new Error(
                ErrorCode.StateInitializationFailed,
                "Failed to initialize clipboard history state.",
                historyResult.Error?.Exception));
        }

        Result<IReadOnlyList<PinnedClipboardItem>> pinnedResult = await _pinnedRepository.LoadAsync(cancellationToken);
        if (pinnedResult.IsFailure)
        {
            return Result<ClipboardStateInitializationData>.Failure(new Error(
                ErrorCode.StateInitializationFailed,
                "Failed to initialize pinned state.",
                pinnedResult.Error?.Exception));
        }

        ClipboardStateInitializationData state = new(
            settings,
            historyResult.Value!,
            pinnedResult.Value!);

        return Result<ClipboardStateInitializationData>.Success(state);
    }

    public async Task<Result> SaveHistoryAsync(IReadOnlyList<ClipboardItem> items, CancellationToken cancellationToken)
    {
        Result saveResult = await _historyRepository.SaveAsync(items, cancellationToken);
        if (saveResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.StatePersistenceFailed,
                "Failed to persist clipboard history.",
                saveResult.Error?.Exception));
        }

        return Result.Success();
    }

    public async Task<Result> SavePinnedAsync(IReadOnlyList<PinnedClipboardItem> items, CancellationToken cancellationToken)
    {
        Result saveResult = await _pinnedRepository.SaveAsync(items, cancellationToken);
        if (saveResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.StatePersistenceFailed,
                "Failed to persist pinned clipboard items.",
                saveResult.Error?.Exception));
        }

        return Result.Success();
    }

    public async Task<Result> SaveSettingsAsync(ClipPocketSettings settings, CancellationToken cancellationToken)
    {
        Result saveResult = await _settingsRepository.SaveAsync(settings, cancellationToken);
        if (saveResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.StatePersistenceFailed,
                "Failed to persist settings.",
                saveResult.Error?.Exception));
        }

        return Result.Success();
    }

    public async Task<Result> ClearHistoryAsync(CancellationToken cancellationToken)
    {
        Result clearResult = await _historyRepository.ClearAsync(cancellationToken);
        if (clearResult.IsFailure)
        {
            return Result.Failure(new Error(
                ErrorCode.StatePersistenceFailed,
                "Failed to clear clipboard history.",
                clearResult.Error?.Exception));
        }

        return Result.Success();
    }
}
