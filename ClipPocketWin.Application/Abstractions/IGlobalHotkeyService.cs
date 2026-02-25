using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;

namespace ClipPocketWin.Application.Abstractions;

public interface IGlobalHotkeyService
{
    event EventHandler? HotkeyPressed;

    Task<Result> StartAsync(KeyboardShortcut shortcut, CancellationToken cancellationToken = default);

    Task<Result> UpdateShortcutAsync(KeyboardShortcut shortcut, CancellationToken cancellationToken = default);

    Task<Result> StopAsync(CancellationToken cancellationToken = default);
}
