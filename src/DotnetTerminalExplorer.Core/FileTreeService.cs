using System.Security;

namespace DotnetTerminalExplorer.Core;

public sealed class FileTreeService : IFileTreeService
{
    public const int DefaultPageSize = 500;

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly Func<string, FileAttributes> _getAttributes;
    private readonly Func<string, DateTime?> _getLastWriteTimeUtc;
    private readonly Func<string, long?> _getFileSize;
    private readonly StringComparison _pathComparison;
    private readonly string _rootPathWithSeparator;

    /// <param name="pageSize">
    /// Number of entries returned per page by <see cref="GetChildrenPage"/>.
    /// Pass 0 to disable paging entirely.
    /// </param>
    public FileTreeService(string rootDirectory, int pageSize = DefaultPageSize)
        : this(
            rootDirectory,
            Directory.EnumerateFileSystemEntries,
            File.GetAttributes,
            pageSize,
            DefaultGetLastWriteTimeUtc,
            DefaultGetFileSize)
    {
    }

    internal FileTreeService(
        string rootDirectory,
        Func<string, IEnumerable<string>> enumerateEntries,
        Func<string, FileAttributes> getAttributes,
        int pageSize = DefaultPageSize,
        Func<string, DateTime?>? getLastWriteTimeUtc = null,
        Func<string, long?>? getFileSize = null)
    {
        ArgumentNullException.ThrowIfNull(enumerateEntries);
        ArgumentNullException.ThrowIfNull(getAttributes);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 0);

        PageSize = pageSize == 0 ? int.MaxValue : pageSize;
        RootPath = RootPathResolver.Resolve(rootDirectory);
        _enumerateEntries = enumerateEntries;
        _getAttributes = getAttributes;
        _getLastWriteTimeUtc = getLastWriteTimeUtc ?? DefaultGetLastWriteTimeUtc;
        _getFileSize = getFileSize ?? DefaultGetFileSize;
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

    public IReadOnlyList<FileSystemEntry> GetChildren(FileSystemEntry directory) =>
        GetChildren(directory, FileTreeSortOptions.Default);

    public IReadOnlyList<FileSystemEntry> GetChildren(
        FileSystemEntry directory,
        FileTreeSortOptions sortOptions,
        Func<FileSystemEntry, long?>? sizeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(sortOptions);

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

        return EnumerateAndSort(directory, sortOptions, sizeProvider);
    }

    public FileTreePage GetChildrenPage(FileSystemEntry directory, int skip) =>
        GetChildrenPage(directory, skip, FileTreeSortOptions.Default);

    public FileTreePage GetChildrenPage(
        FileSystemEntry directory,
        int skip,
        FileTreeSortOptions sortOptions,
        Func<FileSystemEntry, long?>? sizeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(sortOptions);
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
        IEnumerable<FileSystemEntry> entries = EnumerateAndSort(directory, sortOptions, sizeProvider)
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

    private FileSystemEntry[] EnumerateAndSort(
        FileSystemEntry directory,
        FileTreeSortOptions sortOptions,
        Func<FileSystemEntry, long?>? sizeProvider)
    {
        if (sortOptions == FileTreeSortOptions.Default)
        {
            return _enumerateEntries(directory.FullPath)
                .Where(IsWithinRoot)
                .Select(CreateEntry)
                .OrderByDescending(static entry => entry.IsDirectory)
                .ThenBy(static entry => entry.Name, NameComparer)
                .ThenBy(static entry => entry.FullPath, NameComparer)
                .ToArray();
        }

        var list = _enumerateEntries(directory.FullPath)
            .Where(IsWithinRoot)
            .Select(CreateEntry)
            .ToArray();

        if (list.Length <= 1)
        {
            return list;
        }

        if (sortOptions.Field == FileTreeSortField.Modified)
        {
            var modifiedCache = new Dictionary<string, DateTime?>(list.Length, StringComparer.FromComparison(_pathComparison));
            foreach (var entry in list)
            {
                modifiedCache[entry.FullPath] = _getLastWriteTimeUtc(entry.FullPath);
            }

            Array.Sort(list, new FileSystemEntryComparer(
                sortOptions,
                getLastModifiedUtc: entry => modifiedCache.GetValueOrDefault(entry.FullPath)));
            return list;
        }

        if (sortOptions.Field == FileTreeSortField.Size)
        {
            var sizeCache = new Dictionary<string, long?>(list.Length, StringComparer.FromComparison(_pathComparison));
            foreach (var entry in list)
            {
                // Directories NEVER trigger blocking recursive scans here; only
                // already-cached directory sizes from sizeProvider are consulted.
                sizeCache[entry.FullPath] = sizeProvider?.Invoke(entry)
                    ?? (entry.IsDirectory ? null : _getFileSize(entry.FullPath));
            }

            Array.Sort(list, new FileSystemEntryComparer(
                sortOptions,
                getSizeBytes: entry => sizeCache.GetValueOrDefault(entry.FullPath)));
            return list;
        }

        Array.Sort(list, new FileSystemEntryComparer(sortOptions));
        return list;
    }

    private static DateTime? DefaultGetLastWriteTimeUtc(string path)
    {
        try
        {
            var dt = Directory.Exists(path)
                ? Directory.GetLastWriteTimeUtc(path)
                : File.GetLastWriteTimeUtc(path);
            return dt.Year <= 1601 ? null : dt;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return null;
        }
    }

    private static long? DefaultGetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return null;
        }
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
