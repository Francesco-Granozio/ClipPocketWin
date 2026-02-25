namespace ClipPocketWin.Domain.Models;

public sealed record ClipboardBackupPayload
{
    public int Version { get; init; } = 1;

    public IReadOnlyList<ClipboardItem> History { get; init; } = [];

    public IReadOnlyList<PinnedClipboardItem> Pinned { get; init; } = [];
}
