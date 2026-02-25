using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ClipPocketWin.Runtime;

public sealed class WindowPanelService : IWindowPanelService
{
    private const int ShowCommand = 5;
    private const int HideCommand = 0;

    private readonly ILogger<WindowPanelService> _logger;
    private readonly object _syncRoot = new();

    private nint _windowHandle;

    public WindowPanelService(ILogger<WindowPanelService> logger)
    {
        _logger = logger;
    }

    public bool IsVisible
    {
        get
        {
            lock (_syncRoot)
            {
                if (_windowHandle == nint.Zero)
                {
                    return false;
                }

                return IsWindowVisible(_windowHandle);
            }
        }
    }

    public void AttachWindowHandle(nint windowHandle)
    {
        lock (_syncRoot)
        {
            _windowHandle = windowHandle;
        }
    }

    public Task<Result> ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyVisibility(visible: true));
    }

    public Task<Result> HideAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyVisibility(visible: false));
    }

    public Task<Result> ToggleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return IsVisible ? HideAsync(cancellationToken) : ShowAsync(cancellationToken);
    }

    public bool IsPointerOverPanel()
    {
        lock (_syncRoot)
        {
            if (_windowHandle == nint.Zero || !IsWindowVisible(_windowHandle))
            {
                return false;
            }

            if (!GetCursorPos(out Point cursorPosition))
            {
                return false;
            }

            if (!GetWindowRect(_windowHandle, out Rect windowRect))
            {
                return false;
            }

            const int margin = 10;
            return cursorPosition.X >= windowRect.Left - margin
                && cursorPosition.X <= windowRect.Right + margin
                && cursorPosition.Y >= windowRect.Top - margin
                && cursorPosition.Y <= windowRect.Bottom + margin;
        }
    }

    private Result ApplyVisibility(bool visible)
    {
        lock (_syncRoot)
        {
            if (_windowHandle == nint.Zero)
            {
                return Result.Failure(new Error(ErrorCode.PanelOperationFailed, "Main window handle is not attached to panel service."));
            }

            bool commandResult = ShowWindow(_windowHandle, visible ? ShowCommand : HideCommand);
            if (visible)
            {
                _ = SetForegroundWindow(_windowHandle);
            }

            _logger.LogDebug("Panel visibility changed to {Visible}. NativeResult={NativeResult}", visible, commandResult);
            return Result.Success();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
