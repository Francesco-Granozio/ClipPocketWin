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
            ShortcutScanState? previousState = null;
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                KeyboardShortcut shortcut;
                lock (_syncRoot)
                {
                    shortcut = _shortcut;
                }

                ShortcutScanState state = ReadShortcutState(shortcut);
                if (previousState is null || !previousState.Value.Equals(state))
                {
                    string missingModifiers = GetMissingModifiersDescription(shortcut, state);
                    _logger.LogInformation(
                        "Hotkey state {Shortcut} (vk=0x{VirtualKey:X2}): ctrl={Ctrl}, alt={Alt}, shift={Shift}, winL={LeftWin}, winR={RightWin}, target={Target}, oem1={Oem1}, oem3={Oem3}, modifiersMatch={ModifiersMatch}, match={Match}, missing={Missing}",
                        shortcut.DisplayString,
                        state.TargetKeyCode,
                        state.ControlPressed,
                        state.AltPressed,
                        state.ShiftPressed,
                        state.LeftWindowsPressed,
                        state.RightWindowsPressed,
                        state.TargetPressed,
                        state.Oem1Pressed,
                        state.Oem3Pressed,
                        state.ModifiersMatch,
                        state.IsMatch,
                        missingModifiers);

                    previousState = state;
                }

                bool isPressed = state.IsMatch;
                if (isPressed && !_wasPressed)
                {
                    _logger.LogInformation("Hotkey matched -> firing event for {Shortcut}", shortcut.DisplayString);
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

    private static ShortcutScanState ReadShortcutState(KeyboardShortcut shortcut)
    {
        bool controlPressed = IsKeyPressed(VkControl);
        bool altPressed = IsKeyPressed(VkMenu);
        bool shiftPressed = IsKeyPressed(VkShift);
        bool leftWindowsPressed = IsKeyPressed(VkLWin);
        bool rightWindowsPressed = IsKeyPressed(VkRWin);
        bool oem1Pressed = IsKeyPressed(VkOem1);
        bool oem3Pressed = IsKeyPressed(VkOem3);
        bool targetPressed = ReadTargetKeyPressed(shortcut, oem1Pressed, oem3Pressed, out uint targetKeyCode);

        bool modifiersMatch = AreModifiersPressed(
            shortcut.Modifiers,
            controlPressed,
            altPressed,
            shiftPressed,
            leftWindowsPressed,
            rightWindowsPressed);

        bool isMatch = modifiersMatch && targetPressed;
        return new ShortcutScanState(
            controlPressed,
            altPressed,
            shiftPressed,
            leftWindowsPressed,
            rightWindowsPressed,
            targetPressed,
            oem1Pressed,
            oem3Pressed,
            modifiersMatch,
            isMatch,
            targetKeyCode);
    }

    private static bool ReadTargetKeyPressed(KeyboardShortcut shortcut, bool oem1Pressed, bool oem3Pressed, out uint targetKeyCode)
    {
        targetKeyCode = shortcut.KeyCode;

        if (shortcut.KeyCode == (uint)VkOem1)
        {
            if (oem1Pressed)
            {
                targetKeyCode = (uint)VkOem1;
                return true;
            }

            if (oem3Pressed)
            {
                targetKeyCode = (uint)VkOem3;
                return true;
            }

            return false;
        }

        if (shortcut.KeyCode == (uint)VkOem3)
        {
            if (oem3Pressed)
            {
                targetKeyCode = (uint)VkOem3;
                return true;
            }

            if (oem1Pressed)
            {
                targetKeyCode = (uint)VkOem1;
                return true;
            }

            return false;
        }

        return IsKeyPressed((int)shortcut.KeyCode);
    }

    private static bool AreModifiersPressed(
        ShortcutModifiers modifiers,
        bool controlPressed,
        bool altPressed,
        bool shiftPressed,
        bool leftWindowsPressed,
        bool rightWindowsPressed)
    {
        if (modifiers.HasFlag(ShortcutModifiers.Control) && !controlPressed)
        {
            return false;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Alt) && !altPressed)
        {
            return false;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Shift) && !shiftPressed)
        {
            return false;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Windows) && !(leftWindowsPressed || rightWindowsPressed))
        {
            return false;
        }

        return true;
    }

    private static string GetMissingModifiersDescription(KeyboardShortcut shortcut, ShortcutScanState state)
    {
        string missing = string.Empty;

        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Control) && !state.ControlPressed)
        {
            missing = AppendMissingModifier(missing, "Ctrl");
        }

        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt) && !state.AltPressed)
        {
            missing = AppendMissingModifier(missing, "Alt");
        }

        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift) && !state.ShiftPressed)
        {
            missing = AppendMissingModifier(missing, "Shift");
        }

        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Windows) && !(state.LeftWindowsPressed || state.RightWindowsPressed))
        {
            missing = AppendMissingModifier(missing, "Win");
        }

        return string.IsNullOrEmpty(missing) ? "none" : missing;
    }

    private static string AppendMissingModifier(string current, string modifier)
    {
        return string.IsNullOrEmpty(current)
            ? modifier
            : current + "," + modifier;
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
    private const int VkOem1 = 0xBA;
    private const int VkOem3 = 0xC0;

    private readonly record struct ShortcutScanState(
        bool ControlPressed,
        bool AltPressed,
        bool ShiftPressed,
        bool LeftWindowsPressed,
        bool RightWindowsPressed,
        bool TargetPressed,
        bool Oem1Pressed,
        bool Oem3Pressed,
        bool ModifiersMatch,
        bool IsMatch,
        uint TargetKeyCode);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
