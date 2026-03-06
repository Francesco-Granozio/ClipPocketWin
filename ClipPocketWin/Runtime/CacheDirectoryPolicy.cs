using System;
using System.Collections.Generic;
using System.IO;

namespace ClipPocketWin.Runtime;

internal static class CacheDirectoryPolicy
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(12);
    private static readonly Dictionary<string, DateTimeOffset> LastCleanupUtcByDirectory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SyncRoot = new();

    internal static void EnsureDirectoryAndApplyPolicy(string directoryPath, int maxFileCount, long maxTotalBytes)
    {
        Directory.CreateDirectory(directoryPath);
        if (!TryBeginCleanup(directoryPath))
        {
            return;
        }

        try
        {
            CleanupDirectory(directoryPath, maxFileCount, maxTotalBytes);
        }
        catch
        {
        }
    }

    internal static void TouchFile(string filePath)
    {
        try
        {
            File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
        }
        catch
        {
        }
    }

    private static bool TryBeginCleanup(string directoryPath)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (SyncRoot)
        {
            if (LastCleanupUtcByDirectory.TryGetValue(directoryPath, out DateTimeOffset lastRun)
                && (now - lastRun) < CleanupInterval)
            {
                return false;
            }

            LastCleanupUtcByDirectory[directoryPath] = now;
            return true;
        }
    }

    private static void CleanupDirectory(string directoryPath, int maxFileCount, long maxTotalBytes)
    {
        DirectoryInfo directory = new(directoryPath);
        FileInfo[] files = directory.GetFiles();
        if (files.Length == 0)
        {
            return;
        }

        Array.Sort(files, static (left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));

        int keptFiles = 0;
        long keptBytes = 0;
        foreach (FileInfo file in files)
        {
            long fileLength = Math.Max(0L, file.Length);
            bool keepWithinCount = keptFiles < maxFileCount;
            bool keepWithinSize = (keptBytes + fileLength) <= maxTotalBytes || keptFiles == 0;
            if (keepWithinCount && keepWithinSize)
            {
                keptFiles++;
                keptBytes += fileLength;
                continue;
            }

            try
            {
                file.Delete();
            }
            catch
            {
            }
        }
    }
}
