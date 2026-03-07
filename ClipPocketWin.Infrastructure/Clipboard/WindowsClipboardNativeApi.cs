using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipPocketWin.Infrastructure.Clipboard;

internal static class WindowsClipboardNativeApi
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterClipboardFormat(string formatName);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalLock(IntPtr memoryHandle);

    [DllImport("kernel32.dll")]
    internal static extern bool GlobalUnlock(IntPtr memoryHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool QueryFullProcessImageName(IntPtr processHandle, uint flags, StringBuilder executablePath, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr objectHandle);

    [DllImport("kernel32.dll")]
    internal static extern nuint GlobalSize(IntPtr memoryHandle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint DragQueryFile(IntPtr hDrop, uint fileIndex, StringBuilder? filePathBuilder, uint filePathCapacity);
}
