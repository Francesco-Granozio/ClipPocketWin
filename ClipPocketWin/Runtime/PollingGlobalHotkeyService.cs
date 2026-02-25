using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ClipPocketWin.Runtime;

public sealed class PollingGlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private readonly ILogger<PollingGlobalHotkeyService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMilliseconds(45);
    private readonly object _syncRoot = new();

    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private KeyboardShortcut _shortcut = KeyboardShortcut.Default;
    private bool _isStarted;
    private bool _wasPressed;

    public PollingGlobalHotkeyService(ILogger<PollingGlobalHotkeyService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? HotkeyPressed;

    public Task<Result> StartAsync(KeyboardShortcut shortcut, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_isStarted)
            {
                _shortcut = shortcut;
                return Task.FromResult(Result.Success());
            }

            _shortcut = shortcut;
            _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollingTask = Task.Run(() => PollingLoopAsync(_pollingCts.Token), CancellationToken.None);
            _isStarted = true;
            _wasPressed = false;
        }

        _logger.LogInformation("Global hotkey polling started for {DisplayShortcut}", shortcut.DisplayString);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> UpdateShortcutAsync(KeyboardShortcut shortcut, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _shortcut = shortcut;
            _wasPressed = false;
        }

        _logger.LogInformation("Global hotkey updated to {DisplayShortcut}", shortcut.DisplayString);
        return Task.FromResult(Result.Success());
    }

    public async Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        Task? pollingTask;
        CancellationTokenSource? pollingCts;

        lock (_syncRoot)
        {
            if (!_isStarted)
            {
                return Result.Success();
            }

            _isStarted = false;
            pollingTask = _pollingTask;
            pollingCts = _pollingCts;
            _pollingTask = null;
            _pollingCts = null;
            _wasPressed = false;
        }

        try
        {
            pollingCts?.Cancel();
            if (pollingTask is not null)
            {
                await pollingTask.WaitAsync(cancellationToken);
            }

            _logger.LogInformation("Global hotkey polling stopped.");
            return Result.Success();
        }
        catch (OperationCanceledException) when (pollingCts?.IsCancellationRequested == true)
        {
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(ErrorCode.InvalidOperation, "Failed to stop global hotkey polling.", exception));
        }
        finally
        {
            pollingCts?.Dispose();
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to dispose global hotkey service.");
        }
    }

    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(_pollingInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                KeyboardShortcut shortcut;
                lock (_syncRoot)
                {
                    shortcut = _shortcut;
                }

                bool isPressed = IsShortcutPressed(shortcut);
                if (isPressed && !_wasPressed)
                {
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }

                _wasPressed = isPressed;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Global hotkey polling loop crashed.");
        }
    }

    private static bool IsShortcutPressed(KeyboardShortcut shortcut)
    {
        if (!AreModifiersPressed(shortcut.Modifiers))
        {
            return false;
        }

        return IsKeyPressed((int)shortcut.KeyCode);
    }

    private static bool AreModifiersPressed(ShortcutModifiers modifiers)
    {
        if (modifiers.HasFlag(ShortcutModifiers.Control) && !IsKeyPressed(VkControl))
        {
            return false;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Alt) && !IsKeyPressed(VkMenu))
        {
            return false;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Shift) && !IsKeyPressed(VkShift))
        {
            return false;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Windows) && !IsWindowsKeyPressed())
        {
            return false;
        }

        return true;
    }

    private static bool IsWindowsKeyPressed()
    {
        return IsKeyPressed(VkLWin) || IsKeyPressed(VkRWin);
    }

    private static bool IsKeyPressed(int vk)
    {
        short state = GetAsyncKeyState(vk);
        return (state & 0x8000) != 0;
    }

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
