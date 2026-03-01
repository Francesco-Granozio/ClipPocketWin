using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ClipPocketWin.Runtime;

public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private readonly ILogger<WindowsGlobalHotkeyService> _logger;
    private readonly object _syncRoot = new();

    private Thread? _messageThread;
    private TaskCompletionSource<Result>? _startupSource;
    private uint _messageThreadId;
    private KeyboardShortcut _shortcut = KeyboardShortcut.Default;
    private bool _started;

    private static readonly int[] EmptyRegisteredIds = [];

    public WindowsGlobalHotkeyService(ILogger<WindowsGlobalHotkeyService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? HotkeyPressed;

    public async Task<Result> StartAsync(KeyboardShortcut shortcut, CancellationToken cancellationToken = default)
    {
        bool updateRunningService = false;
        Task<Result>? startupTask = null;

        lock (_syncRoot)
        {
            if (_started)
            {
                updateRunningService = true;
                startupTask = Task.FromResult(Result.Success());
            }
            else
            {
                if (_messageThread is not null)
                {
                    return Result.Failure(new Error(ErrorCode.InvalidOperation, "Hotkey service is already starting."));
                }

                _shortcut = shortcut;
                _startupSource = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
                _messageThread = new Thread(MessageThreadMain)
                {
                    IsBackground = true,
                    Name = "ClipPocketWin.GlobalHotkeyService"
                };
                _messageThread.Start();
                startupTask = _startupSource.Task;
            }
        }

        if (updateRunningService)
        {
            return await UpdateShortcutAsync(shortcut, cancellationToken);
        }

        if (startupTask is null)
        {
            return Result.Failure(new Error(ErrorCode.InvalidOperation, "Global hotkey startup task was not initialized."));
        }

        Result startupResult;
        try
        {
            startupResult = await startupTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _ = await StopAsync();
            return Result.Failure(new Error(ErrorCode.Canceled, "Global hotkey startup was canceled."));
        }
        finally
        {
            lock (_syncRoot)
            {
                _startupSource = null;
            }
        }

        if (startupResult.IsFailure)
        {
            return startupResult;
        }

        lock (_syncRoot)
        {
            _started = true;
        }

#if DEBUG
        _logger.LogInformation("Global hotkey service started for {DisplayShortcut}", shortcut.DisplayString);
#endif
        return Result.Success();
    }

    public async Task<Result> UpdateShortcutAsync(KeyboardShortcut shortcut, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool isStarted;
        lock (_syncRoot)
        {
            isStarted = _started;
            if (!isStarted)
            {
                _shortcut = shortcut;
            }
        }

        if (!isStarted)
        {
#if DEBUG
            _logger.LogInformation("Global hotkey updated to {DisplayShortcut}", shortcut.DisplayString);
#endif
            return Result.Success();
        }

        Result stopResult = await StopAsync(cancellationToken);
        if (stopResult.IsFailure)
        {
            return stopResult;
        }

        Result startResult = await StartAsync(shortcut, cancellationToken);
        if (startResult.IsFailure)
        {
            return startResult;
        }

#if DEBUG
        _logger.LogInformation("Global hotkey updated to {DisplayShortcut}", shortcut.DisplayString);
#endif
        return Result.Success();
    }

    public Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        Thread? messageThread;
        uint messageThreadId;

        lock (_syncRoot)
        {
            if (!_started && _messageThread is null)
            {
                return Task.FromResult(Result.Success());
            }

            messageThread = _messageThread;
            messageThreadId = _messageThreadId;
            _started = false;
        }

        try
        {
            if (messageThreadId != 0)
            {
                _ = PostThreadMessage(messageThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            }

            if (messageThread is not null && messageThread.IsAlive)
            {
                if (!messageThread.Join(TimeSpan.FromSeconds(3)))
                {
                    return Task.FromResult(Result.Failure(new Error(
                        ErrorCode.InvalidOperation,
                        "Failed to stop global hotkey service within timeout.")));
                }
            }

#if DEBUG
            _logger.LogInformation("Global hotkey service stopped.");
#endif
            return Task.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.InvalidOperation, "Failed to stop global hotkey service.", exception)));
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
#if DEBUG
            _logger.LogWarning(exception, "Failed to dispose global hotkey service.");
#endif
        }
    }

    private void MessageThreadMain()
    {
        int[] registeredIds = EmptyRegisteredIds;

        try
        {
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);

            lock (_syncRoot)
            {
                _messageThreadId = GetCurrentThreadId();
            }

            KeyboardShortcut shortcut;
            lock (_syncRoot)
            {
                shortcut = _shortcut;
            }

            Result registerResult = RegisterShortcut(shortcut, out registeredIds);
            CompleteStartup(registerResult);
            if (registerResult.IsFailure)
            {
                return;
            }

            int getMessageResult;
            while ((getMessageResult = GetMessage(out Message message, IntPtr.Zero, 0, 0)) > 0)
            {
                if (message.MessageId != WmHotKey)
                {
                    continue;
                }

                int hotkeyId = unchecked((int)message.WParam.ToInt64());
                if (!Array.Exists(registeredIds, id => id == hotkeyId))
                {
                    continue;
                }

#if DEBUG
                _logger.LogInformation("WM_HOTKEY received (id={HotkeyId}) for {DisplayShortcut}", hotkeyId, shortcut.DisplayString);
#endif
                try
                {
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception exception)
                {
#if DEBUG
                    _logger.LogWarning(exception, "Hotkey callback execution failed.");
#endif
                }
            }

            if (getMessageResult == -1)
            {
#if DEBUG
                _logger.LogError("Global hotkey message loop failed with Win32 error {ErrorCode}.", Marshal.GetLastWin32Error());
#endif
            }
        }
        catch (Exception exception)
        {
            CompleteStartup(Result.Failure(new Error(
                ErrorCode.HotkeyRegistrationFailed,
                "Global hotkey message loop terminated unexpectedly.",
                exception)));
#if DEBUG
            _logger.LogError(exception, "Global hotkey message loop crashed.");
#endif
        }
        finally
        {
            UnregisterIds(registeredIds);
            lock (_syncRoot)
            {
                _messageThread = null;
                _messageThreadId = 0;
                _started = false;
            }
        }
    }

    private Result RegisterShortcut(KeyboardShortcut shortcut, out int[] registeredIds)
    {
        List<int> ids = [];
        uint modifiers = ToNativeModifiers(shortcut.Modifiers) | ModNoRepeat;
        List<uint> virtualKeys = ResolveVirtualKeys(shortcut.KeyCode);

        int nextId = HotkeyIdBase;
        foreach (uint virtualKey in virtualKeys)
        {
            int id = nextId++;
            bool isRegistered = RegisterHotKey(IntPtr.Zero, id, modifiers, virtualKey);
            if (isRegistered)
            {
                ids.Add(id);
#if DEBUG
                _logger.LogInformation(
                    "Global hotkey registered: {DisplayShortcut} (id={Id}, modifiers=0x{Modifiers:X4}, vk=0x{VirtualKey:X2})",
                    shortcut.DisplayString,
                    id,
                    modifiers,
                    virtualKey);
#endif
                continue;
            }

            int errorCode = Marshal.GetLastWin32Error();
#if DEBUG
            _logger.LogWarning(
                "Global hotkey register failed for {DisplayShortcut} (id={Id}, modifiers=0x{Modifiers:X4}, vk=0x{VirtualKey:X2}, Win32Error={Win32Error})",
                shortcut.DisplayString,
                id,
                modifiers,
                virtualKey,
                errorCode);
#endif
        }

        if (ids.Count == 0)
        {
            registeredIds = EmptyRegisteredIds;
            return Result.Failure(new Error(
                ErrorCode.HotkeyRegistrationFailed,
                $"Failed to register global hotkey {shortcut.DisplayString}. The shortcut may already be in use by another application."));
        }

        registeredIds = [.. ids];
        return Result.Success();
    }

    private static List<uint> ResolveVirtualKeys(uint keyCode)
    {
        if (keyCode == VkOem1)
        {
            return [VkOem1, VkOem3];
        }

        if (keyCode == VkOem3)
        {
            return [VkOem3, VkOem1];
        }

        return [keyCode];
    }

    private static uint ToNativeModifiers(ShortcutModifiers modifiers)
    {
        uint native = 0;

        if (modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            native |= ModAlt;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Control))
        {
            native |= ModControl;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            native |= ModShift;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Windows))
        {
            native |= ModWin;
        }

        return native;
    }

    private void UnregisterIds(int[] registeredIds)
    {
        foreach (int id in registeredIds)
        {
            if (!UnregisterHotKey(IntPtr.Zero, id))
            {
#if DEBUG
                _logger.LogDebug("Failed to unregister global hotkey id={HotkeyId}. Win32Error={Win32Error}", id, Marshal.GetLastWin32Error());
#endif
            }
        }
    }

    private void CompleteStartup(Result result)
    {
        TaskCompletionSource<Result>? startupSource;
        lock (_syncRoot)
        {
            startupSource = _startupSource;
        }

        startupSource?.TrySetResult(result);
    }

    private const int HotkeyIdBase = 0x4C50;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private const uint WmHotKey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private const uint VkOem1 = 0xBA;
    private const uint VkOem3 = 0xC0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr WindowHandle;
        public uint MessageId;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Message message, IntPtr windowHandle, uint minimumFilter, uint maximumFilter);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, IntPtr windowHandle, uint minimumFilter, uint maximumFilter, uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
