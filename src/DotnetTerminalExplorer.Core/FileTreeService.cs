namespace DotnetTerminalExplorer.Core;

public sealed class FileTreeService : IFileTreeService
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly Func<string, FileAttributes> _getAttributes;
    private readonly StringComparison _pathComparison;
    private readonly string _rootPathWithSeparator;

    public FileTreeService(string rootDirectory)
        : this(rootDirectory, Directory.EnumerateFileSystemEntries, File.GetAttributes)
    {
    }

    internal FileTreeService(
        string rootDirectory,
        Func<string, IEnumerable<string>> enumerateEntries,
        Func<string, FileAttributes> getAttributes)
    {
        ArgumentNullException.ThrowIfNull(enumerateEntries);
        ArgumentNullException.ThrowIfNull(getAttributes);

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
