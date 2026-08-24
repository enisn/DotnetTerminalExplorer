namespace DotnetTerminalExplorer.Core;

public sealed record FileSystemEntry(
    string FullPath,
    string Name,
    FileSystemEntryKind Kind,
    bool IsReparsePoint)
{
    public bool IsDirectory => Kind == FileSystemEntryKind.Directory;

    public override string ToString() => Name;
}
