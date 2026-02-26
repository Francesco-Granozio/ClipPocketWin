using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IClipboardStateService
{
    event EventHandler? StateChanged;

    IReadOnlyList<ClipboardItem> ClipboardItems { get; }

    IReadOnlyList<PinnedClipboardItem> PinnedItems { get; }

    IReadOnlyList<Snippet> Snippets { get; }

    ClipPocketSettings Settings { get; }

    Task<Result> InitializeAsync(CancellationToken cancellationToken = default);

    Task<Result> StartRuntimeAsync(CancellationToken cancellationToken = default);

    Task<Result> StopRuntimeAsync(CancellationToken cancellationToken = default);

    Task<Result> AddClipboardItemAsync(ClipboardItem item, CancellationToken cancellationToken = default);

    Task<Result> DeleteClipboardItemAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> SelectClipboardItemAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> CopyClipboardItemAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> PasteClipboardItemAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> ClearClipboardHistoryAsync(CancellationToken cancellationToken = default);

    Task<Result> TogglePinAsync(ClipboardItem item, CancellationToken cancellationToken = default);

    Task<Result> SaveSettingsAsync(ClipPocketSettings settings, CancellationToken cancellationToken = default);
}
