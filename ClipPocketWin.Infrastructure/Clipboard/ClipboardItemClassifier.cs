using ClipPocketWin.Domain.Models;
using System;
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

        if (LooksLikeCode(text))
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

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeFtp, StringComparison.OrdinalIgnoreCase);
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

        int score = 0;
        if (input.Contains('\n'))
        {
            score++;
        }

        if (input.Contains('{') && input.Contains('}'))
        {
            score++;
        }

        if (input.Contains(";") || input.Contains("=>") || input.Contains("::"))
        {
            score++;
        }

        if (CodeKeywordRegex().IsMatch(input))
        {
            score++;
        }

        return score >= 2;
    }

    [GeneratedRegex(@"^[\+]?[(]?[0-9]{1,4}[)]?[-\s\./0-9]{6,}$", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^(#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|rgb\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)|rgba\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*(0|0?\.\d+|1)\s*\))$", RegexOptions.Compiled)]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"\b(class|public|private|protected|internal|function|func|let|var|const|return|using|namespace|if|else|switch|case|for|foreach|while|async|await)\b", RegexOptions.Compiled)]
    private static partial Regex CodeKeywordRegex();

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
        return ColorRegex().IsMatch(input);
    }
}
