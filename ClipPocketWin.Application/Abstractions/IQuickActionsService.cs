using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IQuickActionsService
{
    Task<Result> SaveToFileAsync(ClipboardItem item, nint ownerWindowHandle, CancellationToken cancellationToken = default);

    Task<Result> CopyAsBase64Async(ClipboardItem item, CancellationToken cancellationToken = default);

    Task<Result> UrlEncodeAsync(ClipboardItem item, CancellationToken cancellationToken = default);

    Task<Result> UrlDecodeAsync(ClipboardItem item, CancellationToken cancellationToken = default);

    Task<Result> EditTextAsync(ClipboardItem sourceItem, string editedText, CancellationToken cancellationToken = default);
}
