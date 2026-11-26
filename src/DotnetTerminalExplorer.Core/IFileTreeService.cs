namespace DotnetTerminalExplorer.Core;

public interface IFileTreeService
{
    FileSystemEntry Root { get; }

    bool CanExpand(FileSystemEntry entry);

    IReadOnlyList<FileSystemEntry> GetChildren(FileSystemEntry directory);

    IReadOnlyList<FileSystemEntry> GetChildren(
        FileSystemEntry directory,
        FileTreeSortOptions sortOptions,
        Func<FileSystemEntry, long?>? sizeProvider = null) =>
        GetChildren(directory);

    FileTreePage GetChildrenPage(FileSystemEntry directory, int skip);

    FileTreePage GetChildrenPage(
        FileSystemEntry directory,
        int skip,
        FileTreeSortOptions sortOptions,
        Func<FileSystemEntry, long?>? sizeProvider = null) =>
        GetChildrenPage(directory, skip);
}
