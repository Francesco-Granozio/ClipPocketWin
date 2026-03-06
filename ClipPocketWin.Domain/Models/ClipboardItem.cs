namespace ClipPocketWin.Domain.Models;

public sealed record ClipboardItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ClipboardItemType Type { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string? SourceApplicationIdentifier { get; init; }

    public string? SourceApplicationExecutablePath { get; init; }

    public string? TextContent { get; init; }

    public byte[]? BinaryContent { get; init; }

    public string? FilePath { get; init; }

    public RichTextContent? RichTextContent { get; init; }

    public bool CanEditText => Type is ClipboardItemType.Text
        or ClipboardItemType.Code
        or ClipboardItemType.Url
        or ClipboardItemType.Email
        or ClipboardItemType.Phone
        or ClipboardItemType.Json
        or ClipboardItemType.Color
        or ClipboardItemType.RichText;

    public string DisplayString => Type switch
    {
        ClipboardItemType.Text or ClipboardItemType.Code or ClipboardItemType.Url or ClipboardItemType.Email or ClipboardItemType.Phone or ClipboardItemType.Json or ClipboardItemType.Color
            => TrimForDisplay(TextContent),
        ClipboardItemType.Image => "Image",
        ClipboardItemType.File => string.IsNullOrWhiteSpace(FilePath) ? "File" : Path.GetFileName(FilePath),
        ClipboardItemType.RichText => RichTextContent?.DisplayString ?? "Rich Text",
        _ => "Clipboard Item"
    };

    public bool IsEquivalentContent(ClipboardItem other)
    {
        if (other.Type != Type)
        {
            return false;
        }

        return Type switch
        {
            ClipboardItemType.Text or ClipboardItemType.Code or ClipboardItemType.Url or ClipboardItemType.Email or ClipboardItemType.Phone or ClipboardItemType.Json or ClipboardItemType.Color
                => string.Equals(TextContent, other.TextContent, StringComparison.Ordinal),
            ClipboardItemType.Image => BinaryEquals(BinaryContent, other.BinaryContent),
            ClipboardItemType.File => string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase),
            ClipboardItemType.RichText => string.Equals(RichTextContent?.PlainText, other.RichTextContent?.PlainText, StringComparison.Ordinal),
            _ => false
        };
    }

    public string? ResolveTextPayload()
    {
        return Type switch
        {
            ClipboardItemType.Text or ClipboardItemType.Code or ClipboardItemType.Url or ClipboardItemType.Email or ClipboardItemType.Phone or ClipboardItemType.Json or ClipboardItemType.Color
                => TextContent,
            ClipboardItemType.RichText
                => RichTextContent?.PlainText ?? TextContent,
            ClipboardItemType.File
                => FilePath ?? TextContent,
            _
                => null
        };
    }

    public string? ResolveEditableTextPayload()
    {
        if (!CanEditText)
        {
            return null;
        }

        return Type == ClipboardItemType.RichText
            ? RichTextContent?.PlainText ?? TextContent ?? string.Empty
            : TextContent ?? string.Empty;
    }

    private static bool BinaryEquals(byte[]? left, byte[]? right)
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

    private static string TrimForDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Invalid Text";
        }

        const int maxLength = 100;
        return value.Length > maxLength ? value[..maxLength] : value;
    }
}
