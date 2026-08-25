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

    private sealed class DirectoryState
    {
        public List<FileSystemEntry> Loaded { get; } = [];

        public bool HasMore { get; set; }
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

        lock (_gate)
        {
            if (_filterVisiblePaths is { } visiblePaths)
            {
                return GetFilteredChildren(forObject, visiblePaths);
            }

            var state = EnsureState(forObject);
            return state.HasMore
                ? [.. state.Loaded, GetOrCreateLoadMoreNode(forObject)]
                : [.. state.Loaded];
        }
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

        children.Sort(static (left, right) => left.IsDirectory == right.IsDirectory
            ? string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
            : (left.IsDirectory ? -1 : 1));
        return children;
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

        lock (_gate)
        {
            if (!_loadMoreByParent.TryGetValue(loadMore.FullPath, out var directory))
            {
                return false;
            }

            parent = directory;
            return Advance(directory);
        }
    }

    public bool Advance(FileSystemEntry directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        lock (_gate)
        {
            if (!_states.TryGetValue(directory.FullPath, out var state) || !state.HasMore)
            {
                return false;
            }

            LoadPage(directory, state);
            return true;
        }
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

    private DirectoryState EnsureState(FileSystemEntry directory)
    {
        if (_states.TryGetValue(directory.FullPath, out var state))
        {
            return state;
        }

        state = new DirectoryState();
        _states[directory.FullPath] = state;
        LoadPage(directory, state);
        return state;
    }

    private void LoadPage(FileSystemEntry directory, DirectoryState state)
    {
        try
        {
            var page = fileTree.GetChildrenPage(directory, state.Loaded.Count);
            state.Loaded.AddRange(page.Entries);
            state.HasMore = page.HasMore;

            foreach (var child in page.Entries)
            {
                _parentByChild[child.FullPath] = directory;
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            state.HasMore = false;
        }
    }
}
