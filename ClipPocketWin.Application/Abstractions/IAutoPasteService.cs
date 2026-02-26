using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IAutoPasteService
{
    Task<Result> SetClipboardContentAsync(ClipboardItem item, CancellationToken cancellationToken = default);

    Task<Result> PasteToPreviousWindowAsync(CancellationToken cancellationToken = default);
}
