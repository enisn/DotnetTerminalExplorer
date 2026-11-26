namespace DotnetTerminalExplorer.Core;

public interface IEntrySizeService : IDisposable
{
    bool IsCalculating { get; }

    event Action<IReadOnlyCollection<string>>? SizesUpdated;

    bool TryGetSize(string fullPath, out EntrySizeInfo info);

    long? TryGetKnownBytes(string fullPath);

    void RequestSizes(IEnumerable<FileSystemEntry> entries);

    void CancelPending();

    void InvalidatePathAndAncestors(string fullPath, string rootPath);

    void Clear();
}
