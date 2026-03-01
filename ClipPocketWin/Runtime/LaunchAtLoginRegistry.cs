using ClipPocketWin.Shared.ResultPattern;
using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace ClipPocketWin.Runtime;

internal static class LaunchAtLoginRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "ClipPocketWin";

    public static Result SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return Result.Failure(new Error(ErrorCode.StoragePathUnavailable, "Unable to open Run registry key for launch at login."));
            }

            if (!enabled)
            {
                key.DeleteValue(EntryName, throwOnMissingValue: false);
                return Result.Success();
            }

            string executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return Result.Failure(new Error(ErrorCode.InvalidOperation, "Unable to resolve current executable path for launch at login."));
            }

            key.SetValue(EntryName, $"\"{executablePath}\"");
            return Result.Success();
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(ErrorCode.InvalidOperation, "Failed to update launch at login registration.", exception));
        }
    }

}
