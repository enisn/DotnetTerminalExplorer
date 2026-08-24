namespace DotnetTerminalExplorer.Core;

public sealed record SearchOptions
{
    public const int DefaultMaxResults = 1000;
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public string Query { get; init; } = string.Empty;

    public SearchMode Mode { get; init; } = SearchMode.Content;

    public bool IsCaseSensitive { get; init; }

    public bool IsRegex { get; init; }

    public bool MatchWholeWord { get; init; }

    public bool RespectGitIgnore { get; init; } = true;

    public string? FilePattern { get; init; }

    public int MaxResults { get; init; } = DefaultMaxResults;

    public long MaxFileSizeBytes { get; init; } = DefaultMaxFileSizeBytes;
}
