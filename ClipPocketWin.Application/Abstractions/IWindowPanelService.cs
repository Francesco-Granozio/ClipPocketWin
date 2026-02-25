using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IWindowPanelService
{
    bool IsVisible { get; }

    void AttachWindowHandle(nint windowHandle);

    Task<Result> ShowAsync(CancellationToken cancellationToken = default);

    Task<Result> HideAsync(CancellationToken cancellationToken = default);

    Task<Result> ToggleAsync(CancellationToken cancellationToken = default);

    bool IsPointerOverPanel();
}
