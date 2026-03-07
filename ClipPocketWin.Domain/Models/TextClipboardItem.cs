namespace ClipPocketWin.Domain.Models;

public sealed record TextClipboardItem : ClipboardItem
{
    private ClipboardItemType _type = ClipboardItemType.Text;

    public override ClipboardItemType Type
    {
        get => _type;
        init => _type = EnsureTextType(value);
    }

    public override string? TextContent { get; init; }

    public override RichTextContent? RichTextContent { get; init; }

    public override bool CanEditText => true;

    public override bool IsColor => Type == ClipboardItemType.Color;

    public override bool IsCode => Type == ClipboardItemType.Code;

    public override bool IsRichText => Type == ClipboardItemType.RichText;

    public override string DisplayString => Type == ClipboardItemType.RichText
        ? RichTextContent?.DisplayString ?? "Rich Text"
        : TrimForDisplay(TextContent);

    public override string PreviewText
    {
        get
        {
            string? preview = Type == ClipboardItemType.RichText
                ? RichTextContent?.PlainText
                : TextContent;

            return NormalizePreviewText(preview, DisplayString);
        }
    }

    public override bool IsEquivalentContent(ClipboardItem other)
    {
        if (other is not TextClipboardItem otherText || Type != otherText.Type)
        {
            return false;
        }

        return Type == ClipboardItemType.RichText
            ? string.Equals(RichTextContent?.PlainText, otherText.RichTextContent?.PlainText, StringComparison.Ordinal)
            : string.Equals(TextContent, otherText.TextContent, StringComparison.Ordinal);
    }

    public override string? ResolveTextPayload()
    {
        return Type == ClipboardItemType.RichText
            ? RichTextContent?.PlainText ?? TextContent
            : TextContent;
    }

    public override string? ResolveEditableTextPayload()
    {
        return Type == ClipboardItemType.RichText
            ? RichTextContent?.PlainText ?? TextContent ?? string.Empty
            : TextContent ?? string.Empty;
    }

    private static ClipboardItemType EnsureTextType(ClipboardItemType type)
    {
        return type is ClipboardItemType.Text
            or ClipboardItemType.Code
            or ClipboardItemType.Url
            or ClipboardItemType.Email
            or ClipboardItemType.Phone
            or ClipboardItemType.Json
            or ClipboardItemType.Color
            or ClipboardItemType.RichText
            ? type
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Clipboard type is not text-based.");
    }
}
