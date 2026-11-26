using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Security;

namespace DotnetTerminalExplorer.Core;

public sealed class EntrySizeService : IEntrySizeService
{
    private const int NotificationBatchDelayMs = 120;
    private const int CancellationCheckInterval = 256;

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly ConcurrentDictionary<string, EntrySizeInfo> _cache = new(PathComparer);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(PathComparer);
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly object _lifecycleLock = new();
    private readonly object _notifyLock = new();
    private readonly HashSet<string> _pendingNotifications = new(PathComparer);

    private CancellationTokenSource _cts = new();
    private Timer? _notifyTimer;
    private int _activeTasks;
    private bool _disposed;

    public EntrySizeService(int? maxConcurrency = null)
    {
        var parallelism = maxConcurrency ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);
        _concurrencyGate = new SemaphoreSlim(parallelism, parallelism);
    }

    public bool IsCalculating => Volatile.Read(ref _activeTasks) > 0;

    public event Action<IReadOnlyCollection<string>>? SizesUpdated;

    public bool TryGetSize(string fullPath, out EntrySizeInfo info)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        return _cache.TryGetValue(fullPath, out info);
    }

    public long? TryGetKnownBytes(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        return _cache.TryGetValue(fullPath, out var info) && info.State == EntrySizeState.Ready
            ? info.Bytes
            : null;
    }

    public void RequestSizes(IEnumerable<FileSystemEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        CancellationToken token;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            token = _cts.Token;
        }

        List<FileSystemEntry>? filesToMeasure = null;

        foreach (var entry in entries)
        {
            if (entry.Kind == FileSystemEntryKind.LoadMore)
            {
                continue;
            }

            if (_cache.TryGetValue(entry.FullPath, out var existing) &&
                existing.State != EntrySizeState.Pending)
            {
                continue;
            }

            if (entry.IsDirectory && entry.IsReparsePoint)
            {
                _cache[entry.FullPath] = EntrySizeInfo.SkippedLink;
                QueueNotification(entry.FullPath);
                continue;
            }

            if (!_inFlight.TryAdd(entry.FullPath, 0))
            {
                continue;
            }

            _cache.TryAdd(entry.FullPath, EntrySizeInfo.Pending);

            if (entry.IsDirectory)
            {
                ScheduleDirectoryCalculation(entry.FullPath, token);
            }
            else
            {
                filesToMeasure ??= [];
                filesToMeasure.Add(entry);
            }
        }

        if (filesToMeasure is { Count: > 0 })
        {
            ScheduleFileBatchCalculation(filesToMeasure, token);
        }
    }

    public void CancelPending()
    {
        CancellationTokenSource oldCts;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            oldCts = _cts;
            _cts = new CancellationTokenSource();
        }

        try
        {
            oldCts.Cancel();
        }
        finally
        {
            oldCts.Dispose();
        }

        _inFlight.Clear();
    }

    public void InvalidatePathAndAncestors(string fullPath, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(rootPath);

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));

        // Remove any cached descendants if a directory was mutated/deleted.
        var prefix = current + Path.DirectorySeparatorChar;
        foreach (var key in _cache.Keys)
        {
            if (string.Equals(key, current, PathComparison) ||
                key.StartsWith(prefix, PathComparison))
            {
                _cache.TryRemove(key, out _);
                _inFlight.TryRemove(key, out _);
            }
        }

        // Invalidate ancestors up to and including root.
        while (!string.IsNullOrEmpty(current))
        {
            _cache.TryRemove(current, out _);
            _inFlight.TryRemove(current, out _);

            if (string.Equals(current, normalizedRoot, PathComparison))
            {
                break;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, PathComparison))
            {
                break;
            }

            current = Path.TrimEndingDirectorySeparator(parent);
        }
    }

    public void Clear()
    {
        CancelPending();
        _cache.Clear();
        lock (_notifyLock)
        {
            _pendingNotifications.Clear();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource oldCts;
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            oldCts = _cts;
        }

        try
        {
            oldCts.Cancel();
        }
        finally
        {
            oldCts.Dispose();
        }

        lock (_notifyLock)
        {
            _notifyTimer?.Dispose();
            _notifyTimer = null;
            _pendingNotifications.Clear();
        }

        _concurrencyGate.Dispose();
    }

    private void ScheduleFileBatchCalculation(List<FileSystemEntry> files, CancellationToken token)
    {
        Interlocked.Increment(ref _activeTasks);

        _ = Task.Run(() =>
        {
            var completedPaths = new List<string>(files.Count);
            try
            {
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var fileInfo = new FileInfo(file.FullPath);
                        if (fileInfo.Exists)
                        {
                            _cache[file.FullPath] = EntrySizeInfo.Ready(fileInfo.Length);
                        }
                        else
                        {
                            _cache.TryRemove(file.FullPath, out _);
                        }
                    }
                    catch (Exception exception) when (exception is IOException
                        or UnauthorizedAccessException
                        or SecurityException)
                    {
                        _cache[file.FullPath] = EntrySizeInfo.Ready(0, isPartial: true);
                    }
                    finally
                    {
                        _inFlight.TryRemove(file.FullPath, out _);
                        completedPaths.Add(file.FullPath);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                foreach (var file in files)
                {
                    _inFlight.TryRemove(file.FullPath, out _);
                }
            }
            finally
            {
                var remaining = Interlocked.Decrement(ref _activeTasks);
                if (completedPaths.Count > 0)
                {
                    QueueNotifications(completedPaths, flushImmediately: remaining == 0);
                }
            }
        }, CancellationToken.None);
    }

    private void ScheduleDirectoryCalculation(string directoryPath, CancellationToken token)
    {
        Interlocked.Increment(ref _activeTasks);

        _ = Task.Run(async () =>
        {
            var acquired = false;
            var completed = false;
            try
            {
                await _concurrencyGate.WaitAsync(token).ConfigureAwait(false);
                acquired = true;

                var result = CalculateDirectorySize(directoryPath, token);
                _cache[directoryPath] = result;
                completed = true;
            }
            catch (OperationCanceledException)
            {
                // Cancelled by toggle-off, Clear, or Dispose.
            }
            catch (ObjectDisposedException)
            {
                // Disposed while waiting on semaphore.
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or SecurityException)
            {
                _cache[directoryPath] = EntrySizeInfo.Ready(0, isPartial: true);
                completed = true;
            }
            finally
            {
                if (acquired)
                {
                    try
                    {
                        _concurrencyGate.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                _inFlight.TryRemove(directoryPath, out _);
                var remaining = Interlocked.Decrement(ref _activeTasks);
                if (completed)
                {
                    QueueNotification(directoryPath, flushImmediately: remaining == 0);
                }
            }
        }, CancellationToken.None);
    }

    internal EntrySizeInfo CalculateDirectorySize(string rootDirectoryPath, CancellationToken cancellationToken)
    {
        var stack = new Stack<DirectoryFrame>();
        var rootFrame = new DirectoryFrame(rootDirectoryPath, parent: null);
        stack.Push(rootFrame);

        var entryCounter = 0;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = stack.Peek();

            if (!frame.Expanded)
            {
                frame.Expanded = true;
                try
                {
                    var enumerable = new FileSystemEnumerable<(string Path, bool IsDir, bool IsReparsePoint, long Length)>(
                        frame.Path,
                        static (ref System.IO.Enumeration.FileSystemEntry entry) => (
                            entry.ToSpecifiedFullPath(),
                            entry.IsDirectory,
                            (entry.Attributes & FileAttributes.ReparsePoint) != 0,
                            entry.IsDirectory ? 0L : entry.Length),
                        new EnumerationOptions
                        {
                            IgnoreInaccessible = false,
                            AttributesToSkip = FileAttributes.None,
                            RecurseSubdirectories = false,
                        });

                    foreach (var (childPath, isDir, isReparsePoint, length) in enumerable)
                    {
                        if ((++entryCounter & (CancellationCheckInterval - 1)) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        if (isDir)
                        {
                            if (isReparsePoint)
                            {
                                _cache[childPath] = EntrySizeInfo.SkippedLink;
                                continue;
                            }

                            if (_cache.TryGetValue(childPath, out var cached) &&
                                cached.State == EntrySizeState.Ready)
                            {
                                frame.TotalBytes += cached.Bytes;
                                frame.IsPartial |= cached.IsPartial;
                                continue;
                            }

                            stack.Push(new DirectoryFrame(childPath, parent: frame));
                        }
                        else
                        {
                            frame.TotalBytes += length;
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or SecurityException)
                {
                    frame.IsPartial = true;
                }
            }
            else
            {
                stack.Pop();
                var result = EntrySizeInfo.Ready(frame.TotalBytes, frame.IsPartial);
                _cache[frame.Path] = result;

                if (frame.Parent is not null)
                {
                    frame.Parent.TotalBytes += frame.TotalBytes;
                    frame.Parent.IsPartial |= frame.IsPartial;
                }
            }
        }

        return EntrySizeInfo.Ready(rootFrame.TotalBytes, rootFrame.IsPartial);
    }

    private void QueueNotification(string path, bool flushImmediately = false)
    {
        QueueNotifications([path], flushImmediately);
    }

    private void QueueNotifications(IEnumerable<string> paths, bool flushImmediately = false)
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        string[]? immediateBatch = null;

        lock (_notifyLock)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var path in paths)
            {
                _pendingNotifications.Add(path);
            }

            if (flushImmediately)
            {
                _notifyTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                if (_pendingNotifications.Count > 0)
                {
                    immediateBatch = [.. _pendingNotifications];
                    _pendingNotifications.Clear();
                }
            }
            else if (_notifyTimer is null)
            {
                _notifyTimer = new Timer(
                    FlushNotificationsCallback,
                    null,
                    NotificationBatchDelayMs,
                    Timeout.Infinite);
            }
            else
            {
                _notifyTimer.Change(NotificationBatchDelayMs, Timeout.Infinite);
            }
        }

        if (immediateBatch is not null)
        {
            SizesUpdated?.Invoke(immediateBatch);
        }
    }

    private void FlushNotificationsCallback(object? state)
    {
        string[]? batch = null;

        lock (_notifyLock)
        {
            if (_disposed || _pendingNotifications.Count == 0)
            {
                return;
            }

            batch = [.. _pendingNotifications];
            _pendingNotifications.Clear();
        }

        SizesUpdated?.Invoke(batch);
    }

    private sealed class DirectoryFrame(string path, DirectoryFrame? parent)
    {
        public string Path { get; } = path;

        public DirectoryFrame? Parent { get; } = parent;

        public long TotalBytes { get; set; }

        public bool IsPartial { get; set; }

        public bool Expanded { get; set; }
    }
}
