namespace ClipPocketWin.Domain.Models;

public sealed record FileClipboardItem : ClipboardItem
{
    public override ClipboardItemType Type
    {
        get => ClipboardItemType.File;
        init
        {
            if (value != ClipboardItemType.File)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Clipboard type must be File.");
            }
        }
    }

    public override string? FilePath { get; init; }

    public override string? TextContent { get; init; }

    public override string DisplayString => string.IsNullOrWhiteSpace(FilePath)
        ? "File"
        : Path.GetFileName(FilePath);

    public override bool IsFile => true;

    public override string PreviewText => NormalizePreviewText(FilePath, DisplayString);

    public override bool IsEquivalentContent(ClipboardItem other)
    {
        return other is FileClipboardItem otherFile
            && string.Equals(FilePath, otherFile.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    public override string? ResolveTextPayload()
    {
        return FilePath ?? TextContent;
    }
}
