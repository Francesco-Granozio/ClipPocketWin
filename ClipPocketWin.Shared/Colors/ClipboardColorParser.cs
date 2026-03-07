using System.Globalization;

namespace ClipPocketWin.Shared.Colors;

public static class ClipboardColorParser
{
    public static bool TryParse(string? value, out ClipboardColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();
        if (text.EndsWith(';'))
        {
            text = text[..^1].TrimEnd();
        }

        return TryParseHex(text, out color)
            || TryParseRgb(text, out color)
            || TryParseHsl(text, out color);
    }

    private static bool TryParseHex(string text, out ClipboardColor color)
    {
        color = default;
        if (!text.StartsWith('#'))
        {
            return false;
        }

        string hex = text[1..];
        if (hex.Length == 3)
        {
            if (!TryParseHexByte(new string(hex[0], 2), out byte r)
                || !TryParseHexByte(new string(hex[1], 2), out byte g)
                || !TryParseHexByte(new string(hex[2], 2), out byte b))
            {
                return false;
            }

            color = new ClipboardColor(255, r, g, b);
            return true;
        }

        if (hex.Length == 4)
        {
            if (!TryParseHexByte(new string(hex[0], 2), out byte r)
                || !TryParseHexByte(new string(hex[1], 2), out byte g)
                || !TryParseHexByte(new string(hex[2], 2), out byte b)
                || !TryParseHexByte(new string(hex[3], 2), out byte a))
            {
                return false;
            }

            color = new ClipboardColor(a, r, g, b);
            return true;
        }

        if (hex.Length == 6)
        {
            if (!TryParseHexByte(hex[0..2], out byte r)
                || !TryParseHexByte(hex[2..4], out byte g)
                || !TryParseHexByte(hex[4..6], out byte b))
            {
                return false;
            }

            color = new ClipboardColor(255, r, g, b);
            return true;
        }

        if (hex.Length == 8)
        {
            if (!TryParseHexByte(hex[0..2], out byte a)
                || !TryParseHexByte(hex[2..4], out byte r)
                || !TryParseHexByte(hex[4..6], out byte g)
                || !TryParseHexByte(hex[6..8], out byte b))
            {
                return false;
            }

            color = new ClipboardColor(a, r, g, b);
            return true;
        }

        return false;
    }

    private static bool TryParseRgb(string text, out ClipboardColor color)
    {
        color = default;
        if (!TryGetFunctionArguments(text, "rgb", out string[] rgbArguments)
            && !TryGetFunctionArguments(text, "rgba", out rgbArguments))
        {
            return false;
        }

        if (rgbArguments.Length is not (3 or 4))
        {
            return false;
        }

        if (!TryParseRgbComponent(rgbArguments[0], out byte r)
            || !TryParseRgbComponent(rgbArguments[1], out byte g)
            || !TryParseRgbComponent(rgbArguments[2], out byte b))
        {
            return false;
        }

        byte a = 255;
        if (rgbArguments.Length == 4 && !TryParseAlpha(rgbArguments[3], out a))
        {
            return false;
        }

        color = new ClipboardColor(a, r, g, b);
        return true;
    }

    private static bool TryParseHsl(string text, out ClipboardColor color)
    {
        color = default;
        if (!TryGetFunctionArguments(text, "hsl", out string[] hslArguments)
            && !TryGetFunctionArguments(text, "hsla", out hslArguments))
        {
            return false;
        }

        if (hslArguments.Length is not (3 or 4))
        {
            return false;
        }

        if (!TryParseHue(hslArguments[0], out double hue)
            || !TryParsePercentage(hslArguments[1], out double saturation)
            || !TryParsePercentage(hslArguments[2], out double lightness))
        {
            return false;
        }

        byte a = 255;
        if (hslArguments.Length == 4 && !TryParseAlpha(hslArguments[3], out a))
        {
            return false;
        }

        (byte r, byte g, byte b) = HslToRgb(hue, saturation, lightness);
        color = new ClipboardColor(a, r, g, b);
        return true;
    }

    private static (byte R, byte G, byte B) HslToRgb(double hueDegrees, double saturation, double lightness)
    {
        double hue = ((hueDegrees % 360d) + 360d) % 360d;
        double c = (1d - Math.Abs((2d * lightness) - 1d)) * saturation;
        double x = c * (1d - Math.Abs(((hue / 60d) % 2d) - 1d));
        double m = lightness - (c / 2d);

        (double rPrime, double gPrime, double bPrime) = hue switch
        {
            >= 0 and < 60 => (c, x, 0d),
            >= 60 and < 120 => (x, c, 0d),
            >= 120 and < 180 => (0d, c, x),
            >= 180 and < 240 => (0d, x, c),
            >= 240 and < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        byte r = ToByte((rPrime + m) * 255d);
        byte g = ToByte((gPrime + m) * 255d);
        byte b = ToByte((bPrime + m) * 255d);
        return (r, g, b);
    }

    private static bool TryParseRgbComponent(string component, out byte value)
    {
        value = 0;
        string normalized = component.Trim();
        if (normalized.EndsWith('%'))
        {
            if (!double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
                || percent < 0d
                || percent > 100d)
            {
                return false;
            }

            value = ToByte((percent / 100d) * 255d);
            return true;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || number < 0d
            || number > 255d)
        {
            return false;
        }

        value = ToByte(number);
        return true;
    }

    private static bool TryParseAlpha(string component, out byte alpha)
    {
        alpha = 255;
        string normalized = component.Trim();
        if (normalized.EndsWith('%'))
        {
            if (!double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
                || percent < 0d
                || percent > 100d)
            {
                return false;
            }

            alpha = ToByte((percent / 100d) * 255d);
            return true;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || number < 0d
            || number > 1d)
        {
            return false;
        }

        alpha = ToByte(number * 255d);
        return true;
    }

    private static bool TryParseHue(string value, out double hue)
    {
        hue = 0d;
        string normalized = value.Trim();
        if (normalized.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^3].TrimEnd();
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out hue);
    }

    private static bool TryParsePercentage(string value, out double percentage)
    {
        percentage = 0d;
        string normalized = value.Trim();
        if (!normalized.EndsWith('%'))
        {
            return false;
        }

        if (!double.TryParse(normalized[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
            || percent < 0d
            || percent > 100d)
        {
            return false;
        }

        percentage = percent / 100d;
        return true;
    }

    private static bool TryGetFunctionArguments(string text, string functionName, out string[] arguments)
    {
        arguments = [];
        string prefix = functionName + "(";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
        {
            return false;
        }

        string inner = text[prefix.Length..^1].Trim();
        if (inner.Length == 0)
        {
            return false;
        }

        if (inner.Contains('/'))
        {
            string normalized = inner.Replace("/", ",", StringComparison.Ordinal);
            arguments = normalized.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return arguments.Length > 0;
        }

        arguments = inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return arguments.Length > 0;
    }

    private static bool TryParseHexByte(string value, out byte result)
    {
        return byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }
}
