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
    private const int ShowMarginPixels = 10;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;

    private readonly ILogger<WindowPanelService> _logger;
    private readonly object _syncRoot = new();

    private nint _windowHandle;
    private nint _lastExternalForegroundWindowHandle;

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

    public nint LastExternalForegroundWindowHandle
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastExternalForegroundWindowHandle;
            }
        }
    }

    public void AttachWindowHandle(nint windowHandle)
    {
        lock (_syncRoot)
        {
            _windowHandle = windowHandle;
            _lastExternalForegroundWindowHandle = nint.Zero;
        }
    }

    public Task<Result> ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyVisibility(visible: true, showPlacementMode: ShowPlacementMode.ActiveMonitorBottomCenter));
    }

    public Task<Result> ShowAtPointerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyVisibility(visible: true, showPlacementMode: ShowPlacementMode.PointerCentered));
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

    private Result ApplyVisibility(bool visible, ShowPlacementMode showPlacementMode = ShowPlacementMode.ActiveMonitorBottomCenter)
    {
        lock (_syncRoot)
        {
            if (_windowHandle == nint.Zero)
            {
                return Result.Failure(new Error(ErrorCode.PanelOperationFailed, "Main window handle is not attached to panel service."));
            }

            if (visible)
            {
                CaptureLastExternalForegroundWindowHandle();

                bool positioned = showPlacementMode switch
                {
                    ShowPlacementMode.PointerCentered => MoveWindowNearPointerWithinMonitor(),
                    _ => MoveWindowToActiveMonitorBounds()
                };

                if (!positioned)
                {
                    _logger.LogDebug("Panel positioning before show failed. PlacementMode={PlacementMode}", showPlacementMode);
                }
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

    private void CaptureLastExternalForegroundWindowHandle()
    {
        nint foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == nint.Zero || foregroundWindow == _windowHandle)
        {
            return;
        }

        _lastExternalForegroundWindowHandle = foregroundWindow;
    }

    private bool MoveWindowToActiveMonitorBounds()
    {
        if (!GetCursorPos(out Point cursorPosition))
        {
            return false;
        }

        nint monitor = MonitorFromPoint(cursorPosition, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return false;
        }

        MonitorInfo monitorInfo = new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        if (!GetWindowRect(_windowHandle, out Rect windowRect))
        {
            return false;
        }

        int windowWidth = Math.Max(1, windowRect.Right - windowRect.Left);
        int windowHeight = Math.Max(1, windowRect.Bottom - windowRect.Top);

        Rect workArea = monitorInfo.Work;
        int targetX = workArea.Left + ((workArea.Right - workArea.Left - windowWidth) / 2);
        int targetY = workArea.Bottom - windowHeight - ShowMarginPixels;

        targetX = Math.Clamp(targetX, workArea.Left, Math.Max(workArea.Left, workArea.Right - windowWidth));
        targetY = Math.Clamp(targetY, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - windowHeight));

        return SetWindowPos(
            _windowHandle,
            nint.Zero,
            targetX,
            targetY,
            0,
            0,
            SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate);
    }

    private bool MoveWindowNearPointerWithinMonitor()
    {
        if (!GetCursorPos(out Point cursorPosition))
        {
            return false;
        }

        nint monitor = MonitorFromPoint(cursorPosition, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return false;
        }

        MonitorInfo monitorInfo = new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        if (!GetWindowRect(_windowHandle, out Rect windowRect))
        {
            return false;
        }

        int windowWidth = Math.Max(1, windowRect.Right - windowRect.Left);
        int windowHeight = Math.Max(1, windowRect.Bottom - windowRect.Top);

        Rect workArea = monitorInfo.Work;
        int targetX = cursorPosition.X - (windowWidth / 2);
        int targetY = cursorPosition.Y - (windowHeight / 2);

        targetX = Math.Clamp(targetX, workArea.Left, Math.Max(workArea.Left, workArea.Right - windowWidth));
        targetY = Math.Clamp(targetY, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - windowHeight));

        return SetWindowPos(
            _windowHandle,
            nint.Zero,
            targetX,
            targetY,
            0,
            0,
            SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate);
    }

    private enum ShowPlacementMode
    {
        ActiveMonitorBottomCenter,
        PointerCentered
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
