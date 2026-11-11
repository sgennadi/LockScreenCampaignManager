using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Presentation-only summary of untrusted GitHub release Markdown.
/// Never changes the release metadata, hashes, signed assets or source text.
/// The results textbox is plain text (not a Markdown renderer).
/// </summary>
internal static class ReleaseNotesSummary
{
    private static readonly Regex ListMarker = new(
        @"^(?:[0-9]{1,2}[.)]|[-*+])\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownLink = new(
        @"\[([^\]\r\n]{1,160})\]\([^\)\r\n]+\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HtmlTag = new(
        @"<[^>\r\n]{1,160}>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Spaces = new(
        @"\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string Format(string? markdown, int maxItems = 5,
        int maxCharsPerItem = 180)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        maxItems = Math.Clamp(maxItems, 1, 8);
        maxCharsPerItem = Math.Clamp(maxCharsPerItem, 50, 250);

        var explicitItems = new List<string>();
        var legacyParagraphs = new List<string>();

        foreach (string raw in markdown.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') ||
                line.StartsWith(new string((char)96, 3), StringComparison.Ordinal) ||
                line.StartsWith("---", StringComparison.Ordinal) ||
                line.StartsWith('|') || line.StartsWith('>') ||
                line.StartsWith('['))
                continue;

            bool isListItem = ListMarker.IsMatch(line);
            if (isListItem) line = ListMarker.Replace(line, string.Empty, 1);

            // The full GitHub release (Open releases) retains all caveats.
            // This view intentionally contains only the short change list.
            if (line.StartsWith("**Open gates:**", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Open gates:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("**Pending validation:**", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Pending validation:", StringComparison.OrdinalIgnoreCase))
                continue;

            string text = FlattenMarkdown(line);
            if (text.Length == 0) continue;
            if (isListItem) explicitItems.Add(text);
            else legacyParagraphs.Add(text);

            // Keep input bounded; GitHub release bodies are untrusted.
            if (explicitItems.Count + legacyParagraphs.Count >= 80) break;
        }

        // Prefer an authored numbered/bulleted change list; older releases
        // used one bold-labeled paragraph per change, so support both.
        var items = explicitItems.Count > 0 ? explicitItems : legacyParagraphs;
        return string.Join(Environment.NewLine,
            items.Take(maxItems).Select((item, index) =>
                (index + 1).ToString() + ". " + Shorten(item, maxCharsPerItem)));
    }

    private static string FlattenMarkdown(string text)
    {
        text = MarkdownLink.Replace(text, "$1");
        text = HtmlTag.Replace(text, string.Empty);
        text = text.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("~~", string.Empty, StringComparison.Ordinal)
            .Replace(((char)96).ToString(), string.Empty, StringComparison.Ordinal);
        return Spaces.Replace(text, " ").Trim();
    }

    private static string Shorten(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        int boundary = text.LastIndexOf(' ', maxChars - 1);
        if (boundary < maxChars / 2) boundary = maxChars - 1;
        return text[..boundary].TrimEnd(' ', '.', ',', ';') + "…";
    }
}
