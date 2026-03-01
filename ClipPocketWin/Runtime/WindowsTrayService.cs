using ClipPocketWin.Application.Abstractions;
using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ClipPocketWin.Runtime;

public sealed class WindowsTrayService : ITrayService, IDisposable
{
    private const uint TrayIconId = 1;

    private const uint WmUser = 0x0400;
    private const uint WmNull = 0x0000;
    private const uint WmCommand = 0x0111;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmReturnCommand = 0x0100;
    private const uint TpmRightButton = 0x0002;

    private const uint CommandShow = 1001;
    private const uint CommandHide = 1002;
    private const uint CommandToggle = 1003;
    private const uint CommandExit = 1004;

    private static readonly IntPtr IdiApplication = new(0x7F00);

    private readonly ILogger<WindowsTrayService> _logger;
    private readonly object _syncRoot = new();
    private readonly string _windowClassName = $"ClipPocketWin.Tray.{Guid.NewGuid():N}";

    private Thread? _messageThread;
    private TaskCompletionSource<Result>? _startupSource;
    private WndProcDelegate? _windowProcedure;

    private IntPtr _moduleHandle;
    private IntPtr _windowHandle;
    private IntPtr _menuHandle;
    private IntPtr _iconHandle;

    private ushort _classAtom;
    private uint _trayMessageId;
    private bool _trayIconAdded;
    private bool _started;

    public WindowsTrayService(ILogger<WindowsTrayService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? ToggleRequested;

    public event EventHandler? ShowRequested;

    public event EventHandler? HideRequested;

    public event EventHandler? ExitRequested;

    public async Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<Result> startupTask;
        lock (_syncRoot)
        {
            if (_started)
            {
                return Result.Success();
            }

            if (_messageThread is not null)
            {
                return Result.Failure(new Error(ErrorCode.InvalidOperation, "Tray service is already starting."));
            }

            _startupSource = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
            _messageThread = new Thread(MessageThreadMain)
            {
                IsBackground = true,
                Name = "ClipPocketWin.TrayService"
            };
            _messageThread.Start();
            startupTask = _startupSource.Task;
        }

        Result startupResult;
        try
        {
            startupResult = await startupTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _ = await StopAsync();
            return Result.Failure(new Error(ErrorCode.Canceled, "Tray service startup was canceled."));
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
        _logger.LogInformation("Windows tray service started.");
#endif
        return Result.Success();
    }

    public Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Thread? messageThread;
        IntPtr windowHandle;

        lock (_syncRoot)
        {
            if (!_started && _messageThread is null)
            {
                return Task.FromResult(Result.Success());
            }

            messageThread = _messageThread;
            windowHandle = _windowHandle;
            _started = false;
        }

        if (windowHandle != IntPtr.Zero)
        {
            _ = PostMessage(windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        if (messageThread is not null && messageThread.IsAlive)
        {
            if (!messageThread.Join(TimeSpan.FromSeconds(3)))
            {
                return Task.FromResult(Result.Failure(new Error(
                    ErrorCode.InvalidOperation,
                    "Failed to stop tray service within timeout.")));
            }
        }

#if DEBUG
        _logger.LogInformation("Windows tray service stopped.");
#endif
        return Task.FromResult(Result.Success());
    }

    public void Dispose()
    {
        try
        {
            _ = StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
#if DEBUG
            _logger.LogWarning(exception, "Failed to dispose tray service cleanly.");
#endif
        }
    }

    private void MessageThreadMain()
    {
        Result initializationResult = InitializeNativeTray();
        CompleteStartup(initializationResult);
        if (initializationResult.IsFailure)
        {
            CleanupNativeTray();
            return;
        }

        try
        {
            int getMessageResult;
            while ((getMessageResult = GetMessage(out Message message, IntPtr.Zero, 0, 0)) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }

            if (getMessageResult == -1)
            {
#if DEBUG
                _logger.LogError("Tray message loop failed with Win32 error {ErrorCode}.", Marshal.GetLastWin32Error());
#endif
            }
        }
        catch (Exception exception)
        {
#if DEBUG
            _logger.LogError(exception, "Tray message loop terminated unexpectedly.");
#endif
        }
        finally
        {
            CleanupNativeTray();
        }
    }

    private Result InitializeNativeTray()
    {
        IntPtr moduleHandle = GetModuleHandle(null);
        if (moduleHandle == IntPtr.Zero)
        {
            return Result.Failure(new Error(
                ErrorCode.TrayStartFailed,
                "Failed to resolve module handle for tray service.",
                CreateWin32Exception("GetModuleHandle")));
        }

        WndProcDelegate windowProcedure = WindowProc;
        lock (_syncRoot)
        {
            _windowProcedure = windowProcedure;
        }

        WindowClassEx windowClass = new()
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            WindowProcedure = windowProcedure,
            InstanceHandle = moduleHandle,
            ClassName = _windowClassName
        };

        ushort classAtom = RegisterClassEx(ref windowClass);
        if (classAtom == 0)
        {
            lock (_syncRoot)
            {
                _windowProcedure = null;
            }

            return Result.Failure(new Error(
                ErrorCode.TrayStartFailed,
                "Failed to register tray message window class.",
                CreateWin32Exception("RegisterClassEx")));
        }

        IntPtr windowHandle = CreateWindowEx(
            0,
            _windowClassName,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            moduleHandle,
            IntPtr.Zero);

        if (windowHandle == IntPtr.Zero)
        {
            _ = UnregisterClass(_windowClassName, moduleHandle);
            lock (_syncRoot)
            {
                _windowProcedure = null;
            }

            return Result.Failure(new Error(
                ErrorCode.TrayStartFailed,
                "Failed to create tray message window.",
                CreateWin32Exception("CreateWindowEx")));
        }

        IntPtr menuHandle = CreatePopupMenu();
        if (menuHandle == IntPtr.Zero)
        {
            CleanupFailedInitialization(windowHandle, IntPtr.Zero, classAtom, moduleHandle, IntPtr.Zero, 0, false);
            return Result.Failure(new Error(
                ErrorCode.TrayStartFailed,
                "Failed to create tray context menu.",
                CreateWin32Exception("CreatePopupMenu")));
        }

        bool menuBuilt = BuildContextMenu(menuHandle);
        if (!menuBuilt)
        {
            CleanupFailedInitialization(windowHandle, menuHandle, classAtom, moduleHandle, IntPtr.Zero, 0, false);
            return Result.Failure(new Error(
                ErrorCode.TrayStartFailed,
                "Failed to populate tray context menu.",
                CreateWin32Exception("AppendMenu")));
        }

        IntPtr iconHandle = LoadIcon(IntPtr.Zero, IdiApplication);
        if (iconHandle == IntPtr.Zero)
        {
            CleanupFailedInitialization(windowHandle, menuHandle, classAtom, moduleHandle, IntPtr.Zero, 0, false);
            return Result.Failure(new Error(
                ErrorCode.TrayStartFailed,
                "Failed to load tray icon handle.",
                CreateWin32Exception("LoadIcon")));
        }

        uint trayMessageId = WmUser + 204;
        NotifyIconData iconData = CreateNotifyIconData(windowHandle, iconHandle, trayMessageId);
        if (!Shell_NotifyIcon(NimAdd, ref iconData))
        {
            CleanupFailedInitialization(windowHandle, menuHandle, classAtom, moduleHandle, iconHandle, trayMessageId, false);
            return Result.Failure(new Error(
                ErrorCode.TrayStartFailed,
                "Failed to add tray icon to notification area.",
                CreateWin32Exception("Shell_NotifyIcon(NIM_ADD)")));
        }

        iconData.Version = NotifyIconVersion4;
        _ = Shell_NotifyIcon(NimSetVersion, ref iconData);

        lock (_syncRoot)
        {
            _moduleHandle = moduleHandle;
            _classAtom = classAtom;
            _windowHandle = windowHandle;
            _menuHandle = menuHandle;
            _iconHandle = iconHandle;
            _trayMessageId = trayMessageId;
            _trayIconAdded = true;
        }

        return Result.Success();
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

    private void CleanupNativeTray()
    {
        IntPtr windowHandle;
        IntPtr menuHandle;
        IntPtr iconHandle;
        IntPtr moduleHandle;
        ushort classAtom;
        uint trayMessageId;
        bool trayIconAdded;

        lock (_syncRoot)
        {
            windowHandle = _windowHandle;
            menuHandle = _menuHandle;
            iconHandle = _iconHandle;
            moduleHandle = _moduleHandle;
            classAtom = _classAtom;
            trayMessageId = _trayMessageId;
            trayIconAdded = _trayIconAdded;

            _windowHandle = IntPtr.Zero;
            _menuHandle = IntPtr.Zero;
            _iconHandle = IntPtr.Zero;
            _moduleHandle = IntPtr.Zero;
            _classAtom = 0;
            _trayMessageId = 0;
            _trayIconAdded = false;
            _windowProcedure = null;
            _messageThread = null;
            _started = false;
        }

        CleanupFailedInitialization(windowHandle, menuHandle, classAtom, moduleHandle, iconHandle, trayMessageId, trayIconAdded);
    }

    private void CleanupFailedInitialization(
        IntPtr windowHandle,
        IntPtr menuHandle,
        ushort classAtom,
        IntPtr moduleHandle,
        IntPtr iconHandle,
        uint trayMessageId,
        bool trayIconAdded)
    {
        if (trayIconAdded && windowHandle != IntPtr.Zero)
        {
            NotifyIconData iconData = CreateNotifyIconData(windowHandle, iconHandle, trayMessageId);
            _ = Shell_NotifyIcon(NimDelete, ref iconData);
        }

        if (menuHandle != IntPtr.Zero)
        {
            _ = DestroyMenu(menuHandle);
        }

        if (windowHandle != IntPtr.Zero)
        {
            _ = DestroyWindow(windowHandle);
        }

        if (classAtom != 0)
        {
            _ = UnregisterClass(_windowClassName, moduleHandle);
        }
    }

    private bool BuildContextMenu(IntPtr menuHandle)
    {
        return AppendMenu(menuHandle, MfString, CommandShow, "Show")
            && AppendMenu(menuHandle, MfString, CommandHide, "Hide")
            && AppendMenu(menuHandle, MfString, CommandToggle, "Toggle")
            && AppendMenu(menuHandle, MfSeparator, 0, string.Empty)
            && AppendMenu(menuHandle, MfString, CommandExit, "Exit");
    }

    private NotifyIconData CreateNotifyIconData(IntPtr windowHandle, IntPtr iconHandle, uint trayMessageId)
    {
        return new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = windowHandle,
            IconId = TrayIconId,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = trayMessageId,
            IconHandle = iconHandle,
            Tip = "ClipPocketWin",
            Info = string.Empty,
            InfoTitle = string.Empty
        };
    }

    private IntPtr WindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _trayMessageId)
        {
            HandleTrayMouseMessage((uint)lParam.ToInt64());
            return IntPtr.Zero;
        }

        if (message == WmCommand)
        {
            DispatchCommand((uint)((ulong)wParam.ToInt64() & 0xFFFF));
            return IntPtr.Zero;
        }

        if (message == WmClose)
        {
            _ = DestroyWindow(windowHandle);
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private void HandleTrayMouseMessage(uint mouseMessage)
    {
        switch (mouseMessage)
        {
            case WmLButtonUp:
                DispatchCommand(CommandToggle);
                break;
            case WmRButtonUp:
            case WmContextMenu:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        IntPtr windowHandle;
        IntPtr menuHandle;

        lock (_syncRoot)
        {
            windowHandle = _windowHandle;
            menuHandle = _menuHandle;
        }

        if (windowHandle == IntPtr.Zero || menuHandle == IntPtr.Zero)
        {
            return;
        }

        if (!GetCursorPos(out Point point))
        {
            return;
        }

        _ = SetForegroundWindow(windowHandle);
        uint selectedCommand = TrackPopupMenu(
            menuHandle,
            TpmReturnCommand | TpmRightButton,
            point.X,
            point.Y,
            0,
            windowHandle,
            IntPtr.Zero);

        if (selectedCommand != 0)
        {
            DispatchCommand(selectedCommand);
        }

        _ = PostMessage(windowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);
    }

    private void DispatchCommand(uint command)
    {
        try
        {
            switch (command)
            {
                case CommandShow:
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CommandHide:
                    HideRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CommandToggle:
                    ToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CommandExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception exception)
        {
#if DEBUG
            _logger.LogWarning(exception, "Tray command {CommandId} execution failed.", command);
#endif
        }
    }

    private static Exception CreateWin32Exception(string operation)
    {
        int errorCode = Marshal.GetLastWin32Error();
        return new InvalidOperationException($"{operation} failed with Win32 error {errorCode}.");
    }

    private delegate IntPtr WndProcDelegate(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public WndProcDelegate WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr InstanceHandle;
        public IntPtr IconHandle;
        public IntPtr CursorHandle;
        public IntPtr BackgroundBrushHandle;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;
        public IntPtr SmallIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr WindowHandle;
        public uint MessageId;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint IconId;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIconHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int extendedStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parentHandle,
        IntPtr menuHandle,
        IntPtr instanceHandle,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instanceHandle);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr windowHandle, uint minimumFilter, uint maximumFilter);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instanceHandle, IntPtr iconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr menuHandle, uint flags, uint newItemId, string? newItemText);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr menuHandle);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(
        IntPtr menuHandle,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr windowHandle,
        IntPtr rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);
}
