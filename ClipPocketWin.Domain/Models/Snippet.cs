using System.Text.RegularExpressions;

namespace ClipPocketWin.Domain.Models;

public sealed record Snippet
{
    private static readonly Regex PlaceholderRegex = new("\\{([^}]+)\\}", RegexOptions.Compiled);

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public DateTimeOffset CreatedDate { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedDate { get; init; }

    public IReadOnlyList<string> GetPlaceholders()
    {
        MatchCollection matches = PlaceholderRegex.Matches(Content);
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> placeholders = [];

        foreach (Match match in matches)
        {
            if (match.Groups.Count < 2)
            {
                continue;
            }

            string name = match.Groups[1].Value;
            if (seen.Add(name))
            {
                placeholders.Add(name);
            }
        }

        return placeholders;
    }

    public string Resolve(IReadOnlyDictionary<string, string> values)
    {
        string output = Content;
        foreach ((string key, string value) in values)
        {
            output = output.Replace($"{{{key}}}", value, StringComparison.Ordinal);
        }

        return output;
    }
}
