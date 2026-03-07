namespace ClipPocketWin.Domain.Models;

public static class ClipboardItemFactory
{
    public static TextClipboardItem CreateText(
        ClipboardItemType type,
        DateTimeOffset timestamp,
        string? textContent,
        string? sourceApplicationIdentifier = null,
        string? sourceApplicationExecutablePath = null,
        RichTextContent? richTextContent = null,
        Guid? id = null)
    {
        return new TextClipboardItem
        {
            Id = id ?? Guid.NewGuid(),
            Type = type,
            Timestamp = timestamp,
            SourceApplicationIdentifier = sourceApplicationIdentifier,
            SourceApplicationExecutablePath = sourceApplicationExecutablePath,
            TextContent = textContent,
            RichTextContent = richTextContent
        };
    }

    public static ImageClipboardItem CreateImage(
        DateTimeOffset timestamp,
        byte[] binaryContent,
        string? sourceApplicationIdentifier = null,
        string? sourceApplicationExecutablePath = null,
        Guid? id = null)
    {
        return new ImageClipboardItem
        {
            Id = id ?? Guid.NewGuid(),
            Timestamp = timestamp,
            SourceApplicationIdentifier = sourceApplicationIdentifier,
            SourceApplicationExecutablePath = sourceApplicationExecutablePath,
            BinaryContent = binaryContent
        };
    }

    public static FileClipboardItem CreateFile(
        DateTimeOffset timestamp,
        string filePath,
        string? sourceApplicationIdentifier = null,
        string? sourceApplicationExecutablePath = null,
        Guid? id = null)
    {
        return new FileClipboardItem
        {
            Id = id ?? Guid.NewGuid(),
            Timestamp = timestamp,
            SourceApplicationIdentifier = sourceApplicationIdentifier,
            SourceApplicationExecutablePath = sourceApplicationExecutablePath,
            FilePath = filePath,
            TextContent = filePath
        };
    }
}
