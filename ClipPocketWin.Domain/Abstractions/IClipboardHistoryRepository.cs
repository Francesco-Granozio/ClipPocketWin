using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Domain.Abstractions;

public interface IClipboardHistoryRepository
{
    Task<Result<IReadOnlyList<ClipboardItem>>> LoadAsync(bool encrypted, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(IReadOnlyList<ClipboardItem> items, bool encrypted, CancellationToken cancellationToken = default);

    Task<Result> ClearAsync(CancellationToken cancellationToken = default);
}
