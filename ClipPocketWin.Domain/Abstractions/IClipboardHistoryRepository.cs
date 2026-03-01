using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Domain.Abstractions;

public interface IClipboardHistoryRepository
{
    Task<Result<IReadOnlyList<ClipboardItem>>> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(IReadOnlyList<ClipboardItem> items, CancellationToken cancellationToken = default);

    Task<Result> ClearAsync(CancellationToken cancellationToken = default);
}
