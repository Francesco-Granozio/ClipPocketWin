using System;
using ClipPocketWin.Domain.Models;

namespace ClipPocketWin;

internal static class ClipboardTypeFilter
{
    public const string AllTag = "All";

    public static string Normalize(string? tag)
    {
        return string.IsNullOrWhiteSpace(tag)
            ? AllTag
            : tag.Trim();
    }

    public static bool IsSelected(string selectedTag, string candidateTag)
    {
        return string.Equals(Normalize(selectedTag), Normalize(candidateTag), StringComparison.Ordinal);
    }

    public static bool Matches(string selectedTag, ClipboardItem item)
    {
        string normalized = Normalize(selectedTag);
        return string.Equals(normalized, AllTag, StringComparison.Ordinal)
            || string.Equals(item.TypeFilterTag, normalized, StringComparison.Ordinal);
    }
}
