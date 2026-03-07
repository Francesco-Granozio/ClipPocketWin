using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Services;

internal sealed class ClipboardSelectionService
{
    private readonly IAutoPasteService _autoPasteService;

    public ClipboardSelectionService(IAutoPasteService autoPasteService)
    {
        _autoPasteService = autoPasteService;
    }

    public async Task<Result> SelectClipboardItemAsync(ClipboardItem item, bool autoPasteEnabled, CancellationToken cancellationToken)
    {
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

    public Task<Result> CopyClipboardItemAsync(ClipboardItem item, CancellationToken cancellationToken)
    {
        return _autoPasteService.SetClipboardContentAsync(item, cancellationToken);
    }

    public async Task<Result> PasteClipboardItemAsync(ClipboardItem item, CancellationToken cancellationToken)
    {
        Result copyResult = await CopyClipboardItemAsync(item, cancellationToken);
        if (copyResult.IsFailure)
        {
            return copyResult;
        }

        return await _autoPasteService.PasteToPreviousWindowAsync(cancellationToken);
    }
}
