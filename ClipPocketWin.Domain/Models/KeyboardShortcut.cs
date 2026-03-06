namespace ClipPocketWin.Domain.Models;

public readonly record struct KeyboardShortcut(uint KeyCode, ShortcutModifiers Modifiers, string DisplayString)
{
    private const uint VkOem1 = 0xBA;

    public static KeyboardShortcut Default => new(VkOem1, ShortcutModifiers.Control, "Ctrl+ò");
}
