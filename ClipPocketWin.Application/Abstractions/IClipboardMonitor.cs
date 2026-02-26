using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IClipboardMonitor
{
    Task<Result> StartAsync(
        Func<ClipboardItem, Task<Result>> onClipboardItemCapturedAsync,
        bool captureRichTextEnabled,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateCaptureRichTextAsync(bool captureRichTextEnabled, CancellationToken cancellationToken = default);

    Task<Result> StopAsync(CancellationToken cancellationToken = default);
}
