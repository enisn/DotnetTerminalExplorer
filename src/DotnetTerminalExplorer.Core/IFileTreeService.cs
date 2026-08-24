namespace DotnetTerminalExplorer.Core;

public interface IFileTreeService
{
    FileSystemEntry Root { get; }

    bool CanExpand(FileSystemEntry entry);

    IReadOnlyList<FileSystemEntry> GetChildren(FileSystemEntry directory);

    FileTreePage GetChildrenPage(FileSystemEntry directory, int skip);
}
