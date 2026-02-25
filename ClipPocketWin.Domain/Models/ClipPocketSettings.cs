namespace ClipPocketWin.Domain.Models;

public sealed record ClipPocketSettings
{
    public bool LaunchAtLogin { get; init; }

    public KeyboardShortcut KeyboardShortcut { get; init; } = KeyboardShortcut.Default;

    public bool RememberHistory { get; init; } = true;

    public bool ShowRecent { get; init; } = true;

    public bool ShowPinned { get; init; } = true;

    public bool AutoPasteEnabled { get; init; }

    public int MaxHistoryItems { get; init; } = 100;

    public bool EnableHistoryLimit { get; init; }

    public bool AutoShowOnEdge { get; init; }

    public double AutoShowDelay { get; init; } = 0.3;

    public double AutoHideDelay { get; init; } = 0.5;

    public bool CaptureRichText { get; init; } = true;

    public bool SnippetsEnabled { get; init; } = true;

    public string DensityMode { get; init; } = "comfortable";

    public double FontSizeScale { get; init; } = 1.0;

    public string ThemeOverride { get; init; } = "dark";

    public bool EncryptHistory { get; init; }

    public bool IncognitoMode { get; init; }

    public HashSet<string> ExcludedAppIds { get; init; } = [];

    public int EffectiveHistoryLimit => EnableHistoryLimit ? Math.Min(Math.Max(MaxHistoryItems, 10), DomainLimits.MaxHistoryItemsHardLimit) : DomainLimits.MaxHistoryItemsHardLimit;
}
