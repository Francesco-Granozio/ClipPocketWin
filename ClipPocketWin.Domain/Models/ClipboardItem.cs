namespace ClipPocketWin.Domain.Models;

public abstract record ClipboardItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public abstract ClipboardItemType Type { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string? SourceApplicationIdentifier { get; init; }

    public string? SourceApplicationExecutablePath { get; init; }

    public virtual string? TextContent { get; init; }

    public virtual byte[]? BinaryContent { get; init; }

    public virtual string? FilePath { get; init; }

    public virtual RichTextContent? RichTextContent { get; init; }

    public virtual bool CanEditText => false;

    public abstract string DisplayString { get; }

    public virtual string TypeFilterTag => Type.ToString();

    public virtual string StylePaletteKey => TypeFilterTag;

    public virtual bool IsImage => false;

    public virtual bool IsFile => false;

    public virtual bool IsColor => false;

    public virtual bool IsCode => false;

    public virtual bool IsRichText => false;

    public virtual string TypeLabel => Type switch
    {
        ClipboardItemType.Url => "URL",
        ClipboardItemType.Json => "JSON",
        ClipboardItemType.RichText => "Rich Text",
        _ => Type.ToString()
    };

    public virtual string Glyph => Type switch
    {
        ClipboardItemType.Text => "\uE8A4",
        ClipboardItemType.Code => "\uE943",
        ClipboardItemType.Url => "\uE71B",
        ClipboardItemType.Email => "\uE715",
        ClipboardItemType.Phone => "\uE717",
        ClipboardItemType.Json => "\uE9D5",
        ClipboardItemType.Color => "\uE790",
        ClipboardItemType.Image => "\uEB9F",
        ClipboardItemType.File => "\uE7C3",
        ClipboardItemType.RichText => "\uE8D2",
        _ => "\uE8A5"
    };

    public virtual string PreviewText => NormalizePreviewText(ResolveTextPayload(), DisplayString);

    public abstract bool IsEquivalentContent(ClipboardItem other);

    public virtual string? ResolveTextPayload() => null;

    public virtual string? ResolveEditableTextPayload() => null;

    public virtual bool CanPersist(int maxPersistedImageBytes) => true;

    protected static bool BinaryEquals(byte[]? left, byte[]? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return false;
        }

        return left.AsSpan().SequenceEqual(right);
    }

    protected static string TrimForDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Invalid Text";
        }

        const int maxLength = 100;
        return value.Length > maxLength ? value[..maxLength] : value;
    }

    protected static string NormalizePreviewText(string? preview, string fallback)
    {
        string normalized = string.IsNullOrWhiteSpace(preview) ? fallback : preview;
        normalized = normalized.Replace("\r", " ", StringComparison.Ordinal)
                             .Replace("\n", " ", StringComparison.Ordinal)
                             .Trim();

        const int maxPreviewLength = 180;
        return normalized.Length > maxPreviewLength
            ? normalized[..maxPreviewLength]
            : normalized;
    }
}
