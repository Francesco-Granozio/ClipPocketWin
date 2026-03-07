using System;
using System.Collections.Generic;
using ClipPocketWin.Domain.Models;

namespace ClipPocketWin;

internal static class ClipboardCardStylePalette
{
    private static readonly ClipboardCardStyleColors DefaultColors = new(
        Windows.UI.Color.FromArgb(255, 71, 85, 105),
        Windows.UI.Color.FromArgb(255, 226, 232, 240));

    private static readonly Dictionary<string, ClipboardCardStyleColors> ColorsByKey = new(StringComparer.Ordinal)
    {
        [nameof(ClipboardItemType.Code)] = new(
            Windows.UI.Color.FromArgb(255, 122, 92, 255),
            Windows.UI.Color.FromArgb(255, 219, 205, 255)),
        [nameof(ClipboardItemType.Url)] = new(
            Windows.UI.Color.FromArgb(255, 59, 130, 246),
            Windows.UI.Color.FromArgb(255, 191, 219, 254)),
        [nameof(ClipboardItemType.Email)] = new(
            Windows.UI.Color.FromArgb(255, 6, 182, 212),
            Windows.UI.Color.FromArgb(255, 165, 243, 252)),
        [nameof(ClipboardItemType.Phone)] = new(
            Windows.UI.Color.FromArgb(255, 34, 197, 94),
            Windows.UI.Color.FromArgb(255, 187, 247, 208)),
        [nameof(ClipboardItemType.Json)] = new(
            Windows.UI.Color.FromArgb(255, 22, 163, 74),
            Windows.UI.Color.FromArgb(255, 187, 247, 208)),
        [nameof(ClipboardItemType.Color)] = new(
            Windows.UI.Color.FromArgb(255, 168, 85, 247),
            Windows.UI.Color.FromArgb(255, 233, 213, 255)),
        [nameof(ClipboardItemType.Image)] = new(
            Windows.UI.Color.FromArgb(255, 14, 165, 233),
            Windows.UI.Color.FromArgb(255, 186, 230, 253)),
        [nameof(ClipboardItemType.File)] = new(
            Windows.UI.Color.FromArgb(255, 245, 158, 11),
            Windows.UI.Color.FromArgb(255, 254, 243, 199)),
        [nameof(ClipboardItemType.RichText)] = new(
            Windows.UI.Color.FromArgb(255, 139, 92, 246),
            Windows.UI.Color.FromArgb(255, 221, 214, 254)),
        [nameof(ClipboardItemType.Text)] = DefaultColors
    };

    public static ClipboardCardStyleColors Resolve(ClipboardItem item)
    {
        return ColorsByKey.TryGetValue(item.StylePaletteKey, out ClipboardCardStyleColors colors)
            ? colors
            : DefaultColors;
    }
}

internal readonly record struct ClipboardCardStyleColors(Windows.UI.Color AccentColor, Windows.UI.Color IconColor);
