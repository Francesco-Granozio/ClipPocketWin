using ClipPocketWin.Domain.Models;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClipPocketWin.Infrastructure.Clipboard;

internal static partial class ClipboardItemClassifier
{
    public static ClipboardItemType ClassifyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ClipboardItemType.Text;
        }

        string trimmed = text.Trim();
        if (IsEmail(trimmed))
        {
            return ClipboardItemType.Email;
        }

        if (LooksLikeMailTo(trimmed))
        {
            return ClipboardItemType.Email;
        }

        if (IsUrl(trimmed))
        {
            return ClipboardItemType.Url;
        }

        if (IsPhone(trimmed))
        {
            return ClipboardItemType.Phone;
        }

        if (IsJson(trimmed))
        {
            return ClipboardItemType.Json;
        }

        if (IsColor(trimmed))
        {
            return ClipboardItemType.Color;
        }

        if (LooksLikeCode(trimmed))
        {
            return ClipboardItemType.Code;
        }

        return ClipboardItemType.Text;
    }

    private static bool IsUrl(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return !string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMailTo(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJson(string input)
    {
        if (input.Length < 2)
        {
            return false;
        }

        char first = input[0];
        char last = input[^1];
        bool candidate = (first == '{' && last == '}') || (first == '[' && last == ']');
        if (!candidate)
        {
            return false;
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(input);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        bool[] indicators =
        [
            input.Contains("func ", StringComparison.Ordinal) || input.Contains("function ", StringComparison.Ordinal),
            input.Contains("class ", StringComparison.Ordinal) || input.Contains("struct ", StringComparison.Ordinal),
            input.Contains("import ", StringComparison.Ordinal) || input.Contains("package ", StringComparison.Ordinal),
            input.Contains("const ", StringComparison.Ordinal) || input.Contains("let ", StringComparison.Ordinal) || input.Contains("var ", StringComparison.Ordinal),
            input.Contains("def ", StringComparison.Ordinal) || input.Contains("=>", StringComparison.Ordinal),
            input.Split('\n').Length > 3 && (input.Contains('{') || input.Contains(':')),
            input.Contains("public ", StringComparison.Ordinal) || input.Contains("private ", StringComparison.Ordinal),
            ControlFlowRegex().IsMatch(input)
        ];

        int indicatorCount = indicators.Count(indicator => indicator);
        return indicatorCount >= 2;
    }

    [GeneratedRegex(@"^[\+]?[(]?[0-9]{1,4}[)]?[-\s\./0-9]{6,}$", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^#([0-9a-fA-F]{3}){1,2}$", RegexOptions.Compiled)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"^rgb\(", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RgbColorRegex();

    [GeneratedRegex(@"^hsl\(", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HslColorRegex();

    [GeneratedRegex(@"^rgba\(", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RgbaColorRegex();

    [GeneratedRegex(@"^hsla\(", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HslaColorRegex();

    [GeneratedRegex(@"^\s*(if|for|while)\s*\(", RegexOptions.Compiled)]
    private static partial Regex ControlFlowRegex();

    private static bool IsPhone(string input)
    {
        return PhoneRegex().IsMatch(input);
    }

    private static bool IsEmail(string input)
    {
        return EmailRegex().IsMatch(input);
    }

    private static bool IsColor(string input)
    {
        return HexColorRegex().IsMatch(input)
            || RgbColorRegex().IsMatch(input)
            || HslColorRegex().IsMatch(input)
            || RgbaColorRegex().IsMatch(input)
            || HslaColorRegex().IsMatch(input);
    }
}
