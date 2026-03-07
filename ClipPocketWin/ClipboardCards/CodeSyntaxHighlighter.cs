using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System.Text.RegularExpressions;

namespace ClipPocketWin;

internal static class CodeSyntaxHighlighter
{
    private static readonly Regex TokenRegex = new(
        @"(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')|\b(\d+(?:\.\d+)?)\b|\b(class|struct|enum|interface|public|private|protected|internal|static|readonly|const|void|int|long|string|bool|var|let|if|else|switch|case|for|foreach|while|do|return|new|try|catch|finally|async|await|import|package|function|def|true|false|null|using|namespace)\b|(//.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static Paragraph BuildParagraph(string? code)
    {
        string text = string.IsNullOrWhiteSpace(code) ? " " : code;
        const int max = 340;
        if (text.Length > max)
        {
            text = text[..max];
        }

        Paragraph paragraph = new();
        int current = 0;

        foreach (Match match in TokenRegex.Matches(text))
        {
            if (match.Index > current)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = text[current..match.Index],
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 228, 236, 247))
                });
            }

            Run run = new()
            {
                Text = match.Value,
                Foreground = ResolveBrush(match)
            };
            paragraph.Inlines.Add(run);
            current = match.Index + match.Length;
        }

        if (current < text.Length)
        {
            paragraph.Inlines.Add(new Run
            {
                Text = text[current..],
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 228, 236, 247))
            });
        }

        return paragraph;
    }

    private static Brush ResolveBrush(Match match)
    {
        if (match.Groups[1].Success)
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 176, 107));
        }

        if (match.Groups[2].Success)
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 104, 194, 255));
        }

        if (match.Groups[3].Success)
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 193, 142, 255));
        }

        if (match.Groups[4].Success)
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 116, 149, 163));
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 228, 236, 247));
    }
}
