namespace ClipPocketWin.Domain.Models;

public sealed record PinnedClipboardItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ClipboardItem OriginalItem { get; init; } = default!;

    public DateTimeOffset PinnedDate { get; init; } = DateTimeOffset.UtcNow;

    public string? CustomTitle { get; init; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(CustomTitle) ? OriginalItem.DisplayString : CustomTitle;
}
