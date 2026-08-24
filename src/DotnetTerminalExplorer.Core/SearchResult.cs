namespace DotnetTerminalExplorer.Core;

public sealed record SearchResult(
    FileSystemEntry Entry,
    int LineNumber,
    int ColumnNumber,
    string LineText,
    int MatchLength)
{
    public string FormattedLocation => LineNumber > 0
        ? $"{Entry.Name}:{LineNumber}:{ColumnNumber}"
        : Entry.Name;

    public string RelativeOrFileName => Entry.Name;
}
