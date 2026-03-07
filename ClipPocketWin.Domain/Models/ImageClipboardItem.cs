namespace ClipPocketWin.Domain.Models;

public sealed record ImageClipboardItem : ClipboardItem
{
    public override ClipboardItemType Type
    {
        get => ClipboardItemType.Image;
        init
        {
            if (value != ClipboardItemType.Image)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Clipboard type must be Image.");
            }
        }
    }

    public override byte[]? BinaryContent { get; init; }

    public override string DisplayString => "Image";

    public override bool IsImage => true;

    public override string PreviewText => NormalizePreviewText("Image content", DisplayString);

    public override bool IsEquivalentContent(ClipboardItem other)
    {
        return other is ImageClipboardItem otherImage
            && BinaryEquals(BinaryContent, otherImage.BinaryContent);
    }

    public override bool CanPersist(int maxPersistedImageBytes)
    {
        if (BinaryContent is null)
        {
            return false;
        }

        return BinaryContent.Length <= maxPersistedImageBytes;
    }
}
