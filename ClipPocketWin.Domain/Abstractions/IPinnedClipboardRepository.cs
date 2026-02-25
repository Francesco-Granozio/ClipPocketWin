using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Domain.Abstractions;

public interface IPinnedClipboardRepository
{
    Task<Result<IReadOnlyList<PinnedClipboardItem>>> LoadAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(IReadOnlyList<PinnedClipboardItem> items, CancellationToken cancellationToken = default);
}
