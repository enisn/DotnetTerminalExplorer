namespace DotnetTerminalExplorer;

internal sealed record CommandPaletteItem(
    string Id,
    string Category,
    string Title,
    string ShortcutHint,
    bool IsEnabled,
    Action Execute,
    string? StateBadge = null,
    string? DisabledReason = null)
{
    public string FullLabel => string.IsNullOrEmpty(StateBadge)
        ? $"{Category}: {Title}"
        : $"{Category}: {Title} {StateBadge}";

    public bool MatchesFilter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            var matched =
                Category.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                Title.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                ShortcutHint.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                (StateBadge is not null && StateBadge.Contains(token, StringComparison.OrdinalIgnoreCase));

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    public string FormatRow(int rowWidth)
    {
        var left = FullLabel;
        var right = IsEnabled
            ? ShortcutHint
            : (string.IsNullOrEmpty(ShortcutHint) ? "(unavailable)" : $"{ShortcutHint} (n/a)");

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        var contentWidth = Math.Max(40, rowWidth);
        var minGap = 2;
        var maxLeft = Math.Max(12, contentWidth - right.Length - minGap);
        if (left.Length > maxLeft)
        {
            left = left[..(maxLeft - 1)] + "…";
        }

        var padding = Math.Max(minGap, contentWidth - left.Length - right.Length);
        return left + new string(' ', padding) + right;
    }
}
