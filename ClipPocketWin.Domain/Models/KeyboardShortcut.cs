namespace ClipPocketWin.Domain.Models;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Control = 1 << 0,
    Alt = 1 << 1,
    Shift = 1 << 2,
    Windows = 1 << 3
}

public readonly record struct KeyboardShortcut(uint KeyCode, ShortcutModifiers Modifiers, string DisplayString)
{
    private const uint VkOem1 = 0xBA;

    public static KeyboardShortcut Default => new(VkOem1, ShortcutModifiers.Control, "Ctrl+ò");
}
