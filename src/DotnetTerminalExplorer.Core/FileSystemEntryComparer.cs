namespace DotnetTerminalExplorer.Core;

public sealed class FileSystemEntryComparer : IComparer<FileSystemEntry>
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly FileTreeSortOptions _sortOptions;
    private readonly Func<FileSystemEntry, DateTime?>? _getLastModifiedUtc;
    private readonly Func<FileSystemEntry, long?>? _getSizeBytes;

    public FileSystemEntryComparer(
        FileTreeSortOptions sortOptions,
        Func<FileSystemEntry, DateTime?>? getLastModifiedUtc = null,
        Func<FileSystemEntry, long?>? getSizeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(sortOptions);
        _sortOptions = sortOptions;
        _getLastModifiedUtc = getLastModifiedUtc;
        _getSizeBytes = getSizeBytes;
    }

    public int Compare(FileSystemEntry? x, FileSystemEntry? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        // LoadMore sentinels always stay at the bottom.
        var xIsLoadMore = x.Kind == FileSystemEntryKind.LoadMore;
        var yIsLoadMore = y.Kind == FileSystemEntryKind.LoadMore;
        if (xIsLoadMore != yIsLoadMore)
        {
            return xIsLoadMore ? 1 : -1;
        }

        // Directories always appear before files.
        if (x.IsDirectory != y.IsDirectory)
        {
            return x.IsDirectory ? -1 : 1;
        }

        var descending = _sortOptions.Direction == FileTreeSortDirection.Descending;
        var primary = _sortOptions.Field switch
        {
            FileTreeSortField.Modified => CompareNullable(_getLastModifiedUtc?.Invoke(x), _getLastModifiedUtc?.Invoke(y), descending),
            FileTreeSortField.Size => CompareNullable(_getSizeBytes?.Invoke(x), _getSizeBytes?.Invoke(y), descending),
            _ => CompareNames(x, y, descending),
        };

        if (primary != 0)
        {
            return primary;
        }

        // Deterministic tie-breaker: always Name ascending, then FullPath ascending.
        var nameComparison = NameComparer.Compare(x.Name, y.Name);
        return nameComparison != 0
            ? nameComparison
            : NameComparer.Compare(x.FullPath, y.FullPath);
    }

    private static int CompareNames(FileSystemEntry x, FileSystemEntry y, bool descending)
    {
        var comparison = NameComparer.Compare(x.Name, y.Name);
        return descending ? -comparison : comparison;
    }

    private static int CompareNullable<T>(T? x, T? y, bool descending)
        where T : struct, IComparable<T>
    {
        // Unknown (null) values always sort after known values regardless of direction
        // so still-calculating directories do not jump ahead of measured entries.
        if (x.HasValue != y.HasValue)
        {
            return x.HasValue ? -1 : 1;
        }

        if (!x.HasValue)
        {
            return 0;
        }

        var comparison = x.Value.CompareTo(y!.Value);
        return descending ? -comparison : comparison;
    }
}
