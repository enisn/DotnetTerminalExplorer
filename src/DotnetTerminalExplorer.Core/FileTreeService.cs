namespace DotnetTerminalExplorer.Core;

public sealed class FileTreeService : IFileTreeService
{
    public const int DefaultPageSize = 500;

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly Func<string, FileAttributes> _getAttributes;
    private readonly StringComparison _pathComparison;
    private readonly string _rootPathWithSeparator;

    /// <param name="pageSize">
    /// Number of entries returned per page by <see cref="GetChildrenPage"/>.
    /// Pass 0 to disable paging entirely.
    /// </param>
    public FileTreeService(string rootDirectory, int pageSize = DefaultPageSize)
        : this(rootDirectory, Directory.EnumerateFileSystemEntries, File.GetAttributes, pageSize)
    {
    }

    internal FileTreeService(
        string rootDirectory,
        Func<string, IEnumerable<string>> enumerateEntries,
        Func<string, FileAttributes> getAttributes,
        int pageSize = DefaultPageSize)
    {
        ArgumentNullException.ThrowIfNull(enumerateEntries);
        ArgumentNullException.ThrowIfNull(getAttributes);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 0);

        PageSize = pageSize == 0 ? int.MaxValue : pageSize;
        RootPath = RootPathResolver.Resolve(rootDirectory);
        _enumerateEntries = enumerateEntries;
        _getAttributes = getAttributes;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        _rootPathWithSeparator = Path.EndsInDirectorySeparator(RootPath)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;

        Root = CreateEntry(RootPath);
    }

    public string RootPath { get; }

    public FileSystemEntry Root { get; }

    public int PageSize { get; }

    public bool CanExpand(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.IsDirectory
            && IsWithinRoot(entry.FullPath)
            && (!entry.IsReparsePoint || IsRoot(entry.FullPath));
    }

    public IReadOnlyList<FileSystemEntry> GetChildren(FileSystemEntry directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!directory.IsDirectory)
        {
            return [];
        }

        if (!IsWithinRoot(directory.FullPath))
        {
            throw new InvalidOperationException(
                $"The path '{directory.FullPath}' is outside the explorer root '{RootPath}'.");
        }

        if (directory.IsReparsePoint && !IsRoot(directory.FullPath))
        {
            return [];
        }

        return _enumerateEntries(directory.FullPath)
            .Where(IsWithinRoot)
            .Select(CreateEntry)
            .OrderByDescending(static entry => entry.IsDirectory)
            .ThenBy(static entry => entry.Name, NameComparer)
            .ThenBy(static entry => entry.FullPath, NameComparer)
            .ToArray();
    }

    public FileTreePage GetChildrenPage(FileSystemEntry directory, int skip)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(skip, 0);

        if (!directory.IsDirectory)
        {
            return new FileTreePage([], HasMore: false);
        }

        if (!IsWithinRoot(directory.FullPath))
        {
            throw new InvalidOperationException(
                $"The path '{directory.FullPath}' is outside the explorer root '{RootPath}'.");
        }

        if (directory.IsReparsePoint && !IsRoot(directory.FullPath))
        {
            return new FileTreePage([], HasMore: false);
        }

        // Skip/take must run on the fully sorted sequence: filesystem enumeration
        // order is arbitrary, so paging before sorting would duplicate or drop
        // entries across page boundaries whenever the two orders disagree.
        IEnumerable<FileSystemEntry> entries = _enumerateEntries(directory.FullPath)
            .Where(IsWithinRoot)
            .Select(CreateEntry)
            .OrderByDescending(static entry => entry.IsDirectory)
            .ThenBy(static entry => entry.Name, NameComparer)
            .ThenBy(static entry => entry.FullPath, NameComparer)
            .Skip(skip);

        if (PageSize < int.MaxValue)
        {
            // Fetch one extra entry to detect whether another page follows.
            entries = entries.Take(PageSize + 1);
        }

        var page = entries.ToArray();

        return page.Length > PageSize
            ? new FileTreePage(page[..PageSize], HasMore: true)
            : new FileTreePage(page, HasMore: false);
    }

    private FileSystemEntry CreateEntry(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var attributes = _getAttributes(fullPath);
        var kind = attributes.HasFlag(FileAttributes.Directory)
            ? FileSystemEntryKind.Directory
            : FileSystemEntryKind.File;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));

        if (string.IsNullOrEmpty(name))
        {
            name = fullPath;
        }

        return new FileSystemEntry(
            fullPath,
            name,
            kind,
            attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    private bool IsRoot(string path) =>
        string.Equals(Path.GetFullPath(path), RootPath, _pathComparison);

    private bool IsWithinRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);

        return string.Equals(fullPath, RootPath, _pathComparison)
            || fullPath.StartsWith(_rootPathWithSeparator, _pathComparison);
    }
}
