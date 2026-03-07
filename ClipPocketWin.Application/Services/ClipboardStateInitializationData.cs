using ClipPocketWin.Domain.Models;

namespace ClipPocketWin.Application.Services;

internal readonly record struct ClipboardStateInitializationData(
    ClipPocketSettings Settings,
    IReadOnlyList<ClipboardItem> HistoryItems,
    IReadOnlyList<PinnedClipboardItem> PinnedItems);
