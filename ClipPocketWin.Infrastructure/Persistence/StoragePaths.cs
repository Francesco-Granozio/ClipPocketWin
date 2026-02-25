namespace ClipPocketWin.Infrastructure.Persistence;

internal static class StoragePaths
{
    public static string AppDirectory
    {
        get
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDirectory = Path.Combine(localAppData, "ClipPocketWin");
            Directory.CreateDirectory(appDirectory);
            return appDirectory;
        }
    }

    public static string ClipboardHistoryJson => Path.Combine(AppDirectory, "clipboardHistory.json");

    public static string ClipboardHistoryEncrypted => Path.Combine(AppDirectory, "clipboardHistory.encrypted");

    public static string PinnedItemsJson => Path.Combine(AppDirectory, "pinnedItems.json");

    public static string SnippetsJson => Path.Combine(AppDirectory, "snippets.json");

    public static string SettingsJson => Path.Combine(AppDirectory, "settings.json");

    public static string EncryptionKeyFile => Path.Combine(AppDirectory, ".clippocket_encryption_key");
}
