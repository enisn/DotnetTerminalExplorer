using System.Security;
using DotnetTerminalExplorer.Core;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class FileSystemTreeBuilder(IFileTreeService fileTree)
    : TreeBuilder<FileSystemEntry>(supportsCanExpand: true)
{
    internal const string LoadMorePathSuffix = "|load-more";
    internal const int PrefetchRemainingThreshold = 5;

    private readonly object _gate = new();
    private readonly Dictionary<string, DirectoryState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileSystemEntry> _loadMoreByParent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileSystemEntry> _parentByChild = new(StringComparer.Ordinal);
    private HashSet<string>? _filterVisiblePaths;
    private Dictionary<string, FileSystemEntry>? _filterFiles;
    private FileTreeSortOptions _sortOptions = FileTreeSortOptions.Default;
    private Func<FileSystemEntry, long?>? _sizeProvider;

    public event Action<FileSystemEntry, IReadOnlyList<FileSystemEntry>>? DirectoryPageLoaded;

    private sealed class DirectoryState(FileSystemEntry directory)
    {
        public FileSystemEntry Directory { get; } = directory;

        public List<FileSystemEntry> Loaded { get; } = [];

        public bool HasMore { get; set; }
    }

    public FileTreeSortOptions SortOptions
    {
        get
        {
            lock (_gate)
            {
                return _sortOptions;
            }
        }
    }

    public Func<FileSystemEntry, long?>? SizeProvider
    {
        get
        {
            lock (_gate)
            {
                return _sizeProvider;
            }
        }
        set
        {
            lock (_gate)
            {
                _sizeProvider = value;
            }
        }
    }

    public bool IsFilterActive
    {
        get
        {
            lock (_gate)
            {
                return _filterVisiblePaths is not null;
            }
        }
    }

    public void SetSortOptions(FileTreeSortOptions sortOptions)
    {
        ArgumentNullException.ThrowIfNull(sortOptions);

        lock (_gate)
        {
            _sortOptions = sortOptions;
            _states.Clear();
            _loadMoreByParent.Clear();
            _parentByChild.Clear();
        }
    }

    public void InvalidateAll()
    {
        lock (_gate)
        {
            _states.Clear();
            _loadMoreByParent.Clear();
            _parentByChild.Clear();
        }
    }

    public IReadOnlyList<FileSystemEntry> GetLoadedEntriesSnapshot()
    {
        lock (_gate)
        {
            if (_filterVisiblePaths is { } visiblePaths && _filterFiles is { } filterFiles)
            {
                var list = new List<FileSystemEntry>(visiblePaths.Count);
                foreach (var path in visiblePaths)
                {
                    if (filterFiles.TryGetValue(path, out var file))
                    {
                        list.Add(file);
                    }
                    else
                    {
                        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                        list.Add(new FileSystemEntry(path, string.IsNullOrEmpty(name) ? path : name, FileSystemEntryKind.Directory, IsReparsePoint: false));
                    }
                }

                return list;
            }

            var result = new List<FileSystemEntry>();
            foreach (var state in _states.Values)
            {
                result.AddRange(state.Loaded);
            }

            return result;
        }
    }

    public IReadOnlyList<FileSystemEntry> GetLoadedChildren(string directoryFullPath)
    {
        ArgumentNullException.ThrowIfNull(directoryFullPath);

        lock (_gate)
        {
            return _states.TryGetValue(directoryFullPath, out var state)
                ? [.. state.Loaded]
                : [];
        }
    }

    public bool ResortLoadedDirectories(
        IEnumerable<string> changedPaths,
        out List<FileSystemEntry> affectedParents)
    {
        ArgumentNullException.ThrowIfNull(changedPaths);
        affectedParents = [];

        lock (_gate)
        {
            if (_sortOptions.Field != FileTreeSortField.Size || _filterVisiblePaths is not null)
            {
                return false;
            }

            var seenParents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in changedPaths)
            {
                if (_parentByChild.TryGetValue(path, out var parent) &&
                    seenParents.Add(parent.FullPath) &&
                    _states.TryGetValue(parent.FullPath, out var state) &&
                    state.Loaded.Count > 1)
                {
                    var comparer = new FileSystemEntryComparer(_sortOptions, getSizeBytes: _sizeProvider);
                    state.Loaded.Sort(comparer);
                    affectedParents.Add(state.Directory);
                }
            }

            return affectedParents.Count > 0;
        }
    }

    public void ApplyFilter(IEnumerable<FileSystemEntry> matchedFiles)
    {
        ArgumentNullException.ThrowIfNull(matchedFiles);

        var comparison = PathComparison;
        var rootPath = fileTree.Root.FullPath;
        var visible = new HashSet<string>(StringComparer.FromComparison(comparison));
        var files = new Dictionary<string, FileSystemEntry>(StringComparer.FromComparison(comparison));

        foreach (var file in matchedFiles)
        {
            if (file.Kind != FileSystemEntryKind.File)
            {
                continue;
            }

            files[file.FullPath] = file;
            visible.Add(file.FullPath);

            var directory = Path.GetDirectoryName(file.FullPath);
            while (!string.IsNullOrEmpty(directory) && !visible.Contains(directory))
            {
                if (string.Equals(directory, rootPath, comparison))
                {
                    break;
                }

                visible.Add(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }

        lock (_gate)
        {
            _filterVisiblePaths = visible;
            _filterFiles = files;
        }
    }

    public void ClearFilter()
    {
        lock (_gate)
        {
            _filterVisiblePaths = null;
            _filterFiles = null;
        }
    }

    public override bool CanExpand(FileSystemEntry toExpand) =>
        toExpand.Kind != FileSystemEntryKind.LoadMore && fileTree.CanExpand(toExpand);

    public override IEnumerable<FileSystemEntry> GetChildren(FileSystemEntry forObject)
    {
        if (forObject.Kind == FileSystemEntryKind.LoadMore)
        {
            return [];
        }

        IReadOnlyList<FileSystemEntry>? newlyLoaded = null;
        FileSystemEntry[] result;

        lock (_gate)
        {
            if (_filterVisiblePaths is { } visiblePaths)
            {
                return GetFilteredChildren(forObject, visiblePaths);
            }

            var state = EnsureState(forObject, out newlyLoaded);
            result = state.HasMore
                ? [.. state.Loaded, GetOrCreateLoadMoreNode(forObject)]
                : [.. state.Loaded];
        }

        if (newlyLoaded is { Count: > 0 })
        {
            DirectoryPageLoaded?.Invoke(forObject, newlyLoaded);
        }

        return result;
    }

    private List<FileSystemEntry> GetFilteredChildren(
        FileSystemEntry directory,
        HashSet<string> visiblePaths)
    {
        var prefix = directory.FullPath.EndsWith(Path.DirectorySeparatorChar)
            ? directory.FullPath
            : directory.FullPath + Path.DirectorySeparatorChar;
        var children = new List<FileSystemEntry>();

        foreach (var path in visiblePaths)
        {
            if (!path.StartsWith(prefix, PathComparison))
            {
                continue;
            }

            var name = path[prefix.Length..];
            if (name.Length == 0 ||
                name.Contains(Path.DirectorySeparatorChar) ||
                name.Contains(Path.AltDirectorySeparatorChar))
            {
                continue;
            }

            children.Add(_filterFiles!.TryGetValue(path, out var file)
                ? file
                : new FileSystemEntry(path, name, FileSystemEntryKind.Directory, IsReparsePoint: false));
        }

        if (_sortOptions == FileTreeSortOptions.Default)
        {
            children.Sort(static (left, right) => left.IsDirectory == right.IsDirectory
                ? string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
                : (left.IsDirectory ? -1 : 1));
            return children;
        }

        children.Sort(new FileSystemEntryComparer(
            _sortOptions,
            getLastModifiedUtc: _sortOptions.Field == FileTreeSortField.Modified ? TryGetLastWriteTimeUtc : null,
            getSizeBytes: _sortOptions.Field == FileTreeSortField.Size ? _sizeProvider : null));
        return children;
    }

    private static DateTime? TryGetLastWriteTimeUtc(FileSystemEntry entry)
    {
        try
        {
            var dt = entry.IsDirectory
                ? Directory.GetLastWriteTimeUtc(entry.FullPath)
                : File.GetLastWriteTimeUtc(entry.FullPath);
            return dt.Year <= 1601 ? null : dt;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return null;
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public bool TryGetLoadMoreParent(FileSystemEntry loadMore, out FileSystemEntry? parent)
    {
        ArgumentNullException.ThrowIfNull(loadMore);
        parent = null;

        if (loadMore.Kind != FileSystemEntryKind.LoadMore)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_loadMoreByParent.TryGetValue(loadMore.FullPath, out var directory))
            {
                return false;
            }

            parent = directory;
            return true;
        }
    }

    public bool TryAdvance(FileSystemEntry loadMore, out FileSystemEntry? parent)
    {
        ArgumentNullException.ThrowIfNull(loadMore);
        parent = null;

        if (loadMore.Kind != FileSystemEntryKind.LoadMore)
        {
            return false;
        }

        FileSystemEntry? directory;
        lock (_gate)
        {
            if (!_loadMoreByParent.TryGetValue(loadMore.FullPath, out directory))
            {
                return false;
            }
        }

        parent = directory;
        return Advance(directory);
    }

    public bool Advance(FileSystemEntry directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        IReadOnlyList<FileSystemEntry>? newlyLoaded;
        lock (_gate)
        {
            if (!_states.TryGetValue(directory.FullPath, out var state) || !state.HasMore)
            {
                return false;
            }

            newlyLoaded = LoadPage(directory, state);
        }

        if (newlyLoaded is { Count: > 0 })
        {
            DirectoryPageLoaded?.Invoke(directory, newlyLoaded);
        }

        return true;
    }

    public bool TryGetPrefetchParent(FileSystemEntry entry, out FileSystemEntry? parent)
    {
        ArgumentNullException.ThrowIfNull(entry);
        parent = null;

        if (entry.Kind == FileSystemEntryKind.LoadMore)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_parentByChild.TryGetValue(entry.FullPath, out var directory) ||
                !_states.TryGetValue(directory.FullPath, out var state) ||
                !state.HasMore)
            {
                return false;
            }

            var index = state.Loaded.IndexOf(entry);
            if (index < 0 || state.Loaded.Count - 1 - index > PrefetchRemainingThreshold)
            {
                return false;
            }

            parent = directory;
            return true;
        }
    }

    public void Invalidate(string directoryFullPath)
    {
        ArgumentNullException.ThrowIfNull(directoryFullPath);

        lock (_gate)
        {
            _states.Remove(directoryFullPath);
            _loadMoreByParent.Remove(directoryFullPath + LoadMorePathSuffix);

            var staleChildKeys = _parentByChild
                .Where(pair => pair.Value.FullPath == directoryFullPath)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in staleChildKeys)
            {
                _parentByChild.Remove(key);
            }
        }
    }

    internal FileSystemEntry? GetLoadMoreNode(FileSystemEntry parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        lock (_gate)
        {
            return _states.TryGetValue(parent.FullPath, out var state) && state.HasMore
                ? GetOrCreateLoadMoreNode(parent)
                : null;
        }
    }

    private FileSystemEntry GetOrCreateLoadMoreNode(FileSystemEntry parent)
    {
        if (_loadMoreByParent.TryGetValue(parent.FullPath, out var existing))
        {
            return existing;
        }

        var node = new FileSystemEntry(
            parent.FullPath + LoadMorePathSuffix,
            "── Load more…",
            FileSystemEntryKind.LoadMore,
            IsReparsePoint: false);
        _loadMoreByParent[node.FullPath] = parent;
        return node;
    }

    private DirectoryState EnsureState(FileSystemEntry directory, out IReadOnlyList<FileSystemEntry>? newlyLoaded)
    {
        if (_states.TryGetValue(directory.FullPath, out var state))
        {
            newlyLoaded = null;
            return state;
        }

        state = new DirectoryState(directory);
        _states[directory.FullPath] = state;
        newlyLoaded = LoadPage(directory, state);
        return state;
    }

    private IReadOnlyList<FileSystemEntry>? LoadPage(FileSystemEntry directory, DirectoryState state)
    {
        try
        {
            var page = fileTree.GetChildrenPage(directory, state.Loaded.Count, _sortOptions, _sizeProvider);
            state.Loaded.AddRange(page.Entries);
            state.HasMore = page.HasMore;

            if (!state.HasMore && state.Loaded.Count > 1 &&
                _sortOptions != FileTreeSortOptions.Default &&
                (_sortOptions.Field == FileTreeSortField.Name || _sortOptions.Field == FileTreeSortField.Size))
            {
                // Ensures consistent ordering even when an IFileTreeService implementation
                // uses the default interface fallback without custom sorting.
                state.Loaded.Sort(new FileSystemEntryComparer(_sortOptions, getSizeBytes: _sizeProvider));
            }

            foreach (var child in page.Entries)
            {
                _parentByChild[child.FullPath] = directory;
            }

            return page.Entries;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            state.HasMore = false;
            return null;
        }
    }
}
