using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Domain.Models;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClipPocketWin.Runtime;

public sealed class WindowsAutoPasteService : IAutoPasteService
{
    private const uint CfUnicodeText = 13;
    private const uint CfDib = 8;
    private const uint GlobalMoveable = 0x0002;
    private const uint GlobalZeroInit = 0x0040;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const int FocusRetryCount = 4;
    private const int FocusRetryDelayMs = 35;
    private const int PasteDispatchDelayMs = 45;

    private readonly IWindowPanelService _windowPanelService;
    private readonly ILogger<WindowsAutoPasteService> _logger;

    public WindowsAutoPasteService(
        IWindowPanelService windowPanelService,
        ILogger<WindowsAutoPasteService> logger)
    {
        _windowPanelService = windowPanelService;
        _logger = logger;
    }

    public Task<Result> SetClipboardContentAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (item is null)
        {
            return Task.FromResult(Result.Failure(new Error(ErrorCode.ClipboardItemInvalid, "Clipboard item cannot be null.")));
        }

        if (item.Type == ClipboardItemType.Image)
        {
            if (item.BinaryContent is null || item.BinaryContent.Length == 0)
            {
                return Task.FromResult(Result.Failure(new Error(
                    ErrorCode.ClipboardItemUnsupportedType,
                    "Image clipboard item does not contain binary payload for auto-paste.")));
            }

            Result imageWriteResult = WriteBinaryPayloadToClipboard(CfDib, item.BinaryContent);
            return Task.FromResult(imageWriteResult);
        }

        string? textPayload = ResolveTextPayload(item);
        if (string.IsNullOrWhiteSpace(textPayload))
        {
            return Task.FromResult(Result.Failure(new Error(
                ErrorCode.ClipboardItemUnsupportedType,
                $"Clipboard item type '{item.Type}' does not expose a textual payload for auto-paste.")));
        }

        Result writeResult = WriteUnicodeTextToClipboard(textPayload);
        if (writeResult.IsFailure)
        {
            return Task.FromResult(writeResult);
        }

        return Task.FromResult(Result.Success());
    }

    public async Task<Result> PasteToPreviousWindowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        nint targetWindow = _windowPanelService.LastExternalForegroundWindowHandle;
        if (targetWindow == nint.Zero)
        {
            return Result.Failure(new Error(ErrorCode.PanelOperationFailed, "No previously focused external window is available for auto-paste."));
        }

        if (!IsWindow(targetWindow))
        {
            return Result.Failure(new Error(ErrorCode.PanelOperationFailed, "The previously focused external window is no longer valid for auto-paste."));
        }

        _logger.LogInformation("Auto-paste target window captured: {TargetWindow}.", targetWindow);

        bool targetActivated = await TryActivateTargetWindowAsync(targetWindow, cancellationToken);
        if (!targetActivated)
        {
            _logger.LogWarning("Target window {TargetWindow} did not become foreground before Ctrl+V. Continuing with paste fallback.", targetWindow);
        }
        else
        {
            _logger.LogInformation("Target window {TargetWindow} is foreground; sending Ctrl+V.", targetWindow);
        }

        await Task.Delay(PasteDispatchDelayMs, cancellationToken);
        Result sendResult = SendCtrlVShortcut();
        if (sendResult.IsFailure)
        {
            return sendResult;
        }

        _logger.LogInformation("Ctrl+V dispatch completed for target window {TargetWindow}.", targetWindow);
        return Result.Success();
    }

    private async Task<bool> TryActivateTargetWindowAsync(nint targetWindow, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= FocusRetryCount; attempt++)
        {
            nint foregroundBefore = GetForegroundWindow();
            bool setForegroundResult = SetForegroundWindow(targetWindow);
            _logger.LogInformation(
                "Auto-paste focus attempt {Attempt}/{MaxAttempts}: target={TargetWindow}, foregroundBefore={ForegroundBefore}, setForegroundResult={SetForegroundResult}",
                attempt,
                FocusRetryCount,
                targetWindow,
                foregroundBefore,
                setForegroundResult);

            await Task.Delay(FocusRetryDelayMs, cancellationToken);

            nint foregroundAfter = GetForegroundWindow();
            bool matched = foregroundAfter == targetWindow;
            _logger.LogInformation(
                "Auto-paste focus check {Attempt}/{MaxAttempts}: target={TargetWindow}, foregroundAfter={ForegroundAfter}, matched={Matched}",
                attempt,
                FocusRetryCount,
                targetWindow,
                foregroundAfter,
                matched);

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveTextPayload(ClipboardItem item)
    {
        return item.Type switch
        {
            ClipboardItemType.Text or ClipboardItemType.Code or ClipboardItemType.Url or ClipboardItemType.Email or ClipboardItemType.Phone or ClipboardItemType.Json or ClipboardItemType.Color
                => item.TextContent,
            ClipboardItemType.File
                => item.FilePath ?? item.TextContent,
            ClipboardItemType.RichText
                => item.RichTextContent?.PlainText ?? item.TextContent,
            _
                => item.TextContent
        };
    }

    private static Result WriteUnicodeTextToClipboard(string text)
    {
        byte[] payload = Encoding.Unicode.GetBytes(text + '\0');
        return WriteBinaryPayloadToClipboard(CfUnicodeText, payload);
    }

    private static Result WriteBinaryPayloadToClipboard(uint format, byte[] payload)
    {
        if (!OpenClipboardWithRetry())
        {
            return Result.Failure(new Error(
                ErrorCode.ClipboardMonitorReadFailed,
                "Unable to open the clipboard for auto-paste write operation."));
        }

        IntPtr allocation = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard())
            {
                return Result.Failure(CreateWin32Error(ErrorCode.InvalidOperation, "Failed to clear clipboard before auto-paste write."));
            }

            allocation = GlobalAlloc(GlobalMoveable | GlobalZeroInit, (nuint)payload.Length);
            if (allocation == IntPtr.Zero)
            {
                return Result.Failure(CreateWin32Error(ErrorCode.StorageWriteFailed, "Failed to allocate memory for clipboard payload."));
            }

            IntPtr targetPointer = GlobalLock(allocation);
            if (targetPointer == IntPtr.Zero)
            {
                return Result.Failure(CreateWin32Error(ErrorCode.StorageWriteFailed, "Failed to lock allocated clipboard memory."));
            }

            try
            {
                Marshal.Copy(payload, 0, targetPointer, payload.Length);
            }
            finally
            {
                _ = GlobalUnlock(allocation);
            }

            IntPtr clipboardHandle = SetClipboardData(format, allocation);
            if (clipboardHandle == IntPtr.Zero)
            {
                return Result.Failure(CreateWin32Error(ErrorCode.StorageWriteFailed, "Failed to set clipboard payload for auto-paste."));
            }

            allocation = IntPtr.Zero;
            return Result.Success();
        }
        finally
        {
            if (allocation != IntPtr.Zero)
            {
                _ = GlobalFree(allocation);
            }

            _ = CloseClipboard();
        }
    }

    private static Result SendCtrlVShortcut()
    {
        Input[] inputs =
        [
            CreateKeyboardInput(VkControl, keyUp: false),
            CreateKeyboardInput(VkV, keyUp: false),
            CreateKeyboardInput(VkV, keyUp: true),
            CreateKeyboardInput(VkControl, keyUp: true)
        ];

        uint sentCount = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sentCount != inputs.Length)
        {
            return Result.Failure(CreateWin32Error(ErrorCode.InvalidOperation, "Failed to send Ctrl+V input sequence."));
        }

        return Result.Success();
    }

    private static Input CreateKeyboardInput(ushort keyCode, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = keyCode,
                    Flags = keyUp ? KeyEventFKeyUp : 0
                }
            }
        };
    }

    private static bool OpenClipboardWithRetry()
    {
        const int retryCount = 5;
        const int delayMs = 15;

        for (int attempt = 0; attempt < retryCount; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(delayMs);
        }

        return false;
    }

    private static Error CreateWin32Error(ErrorCode code, string message)
    {
        int errorCode = Marshal.GetLastWin32Error();
        return new Error(code, $"{message} Win32Error={errorCode}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memoryHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}
