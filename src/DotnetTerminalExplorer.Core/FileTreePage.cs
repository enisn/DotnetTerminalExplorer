namespace DotnetTerminalExplorer.Core;

public sealed record FileTreePage(IReadOnlyList<FileSystemEntry> Entries, bool HasMore);
