using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace DotnetTerminalExplorer.Core;

public sealed class FastSearchEngine : ISearchService
{
    private const int BufferSize = 64 * 1024; // 64 KB chunk

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        string rootPath,
        SearchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Query))
        {
            yield break;
        }

        var resolvedRoot = RootPathResolver.Resolve(rootPath);
        var resultsChannel = Channel.CreateBounded<SearchResult>(new BoundedChannelOptions(options.MaxResults)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var searchTask = Task.Run(async () =>
        {
            try
            {
                if (options.Mode == SearchMode.FileName)
                {
                    await SearchFileNamesAsync(resolvedRoot, options, resultsChannel.Writer, cts.Token);
                }
                else
                {
                    await SearchContentAsync(resolvedRoot, options, resultsChannel.Writer, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Clean cancellation
            }
            finally
            {
                resultsChannel.Writer.TryComplete();
            }
        }, cts.Token);

        int count = 0;
        while (await resultsChannel.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
        {
            while (resultsChannel.Reader.TryRead(out var result))
            {
                yield return result;
                count++;
                if (count >= options.MaxResults)
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                    yield break;
                }
            }
        }

        await searchTask.ConfigureAwait(false);
    }

    private static async Task SearchFileNamesAsync(
        string rootPath,
        SearchOptions options,
        ChannelWriter<SearchResult> writer,
        CancellationToken cancellationToken)
    {
        var filter = options.RespectGitIgnore ? new GitIgnoreFilter(rootPath) : null;
        var comparison = options.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Regex? regex = null;

        if (options.IsRegex)
        {
            var regexOptions = RegexOptions.Compiled | (options.IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            regex = new Regex(options.Query, regexOptions);
        }

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDir = stack.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(currentDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entryPath in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entryPath);
                var isDir = Directory.Exists(entryPath);

                if (filter is not null && filter.IsIgnored(entryPath, isDir))
                {
                    continue;
                }

                if (isDir)
                {
                    stack.Push(entryPath);
                }

                bool isMatch;
                if (regex is not null)
                {
                    isMatch = regex.IsMatch(name);
                }
                else
                {
                    isMatch = name.Contains(options.Query, comparison);
                }

                if (isMatch)
                {
                    var entry = new FileSystemEntry(
                        entryPath,
                        name,
                        isDir ? FileSystemEntryKind.Directory : FileSystemEntryKind.File,
                        IsReparsePoint: false);

                    var result = new SearchResult(
                        entry,
                        LineNumber: 0,
                        ColumnNumber: 0,
                        LineText: entryPath,
                        MatchLength: options.Query.Length);

                    if (!await writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false) ||
                        !writer.TryWrite(result))
                    {
                        return;
                    }
                }
            }
        }
    }

    private static async Task SearchContentAsync(
        string rootPath,
        SearchOptions options,
        ChannelWriter<SearchResult> writer,
        CancellationToken cancellationToken)
    {
        var filter = options.RespectGitIgnore ? new GitIgnoreFilter(rootPath) : null;
        var fileChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
        });

        // Background crawler task
        var crawlerTask = Task.Run(async () =>
        {
            try
            {
                await CrawlFilesAsync(rootPath, options, filter, fileChannel.Writer, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                fileChannel.Writer.TryComplete();
            }
        }, cancellationToken);

        // Worker tasks
        int workerCount = Math.Max(2, Environment.ProcessorCount);
        var workers = new Task[workerCount];

        for (int i = 0; i < workerCount; i++)
        {
            workers[i] = Task.Run(async () =>
            {
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    while (await fileChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (fileChannel.Reader.TryRead(out var filePath))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await SearchSingleFileContentAsync(filePath, options, writer, buffer, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }, cancellationToken);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        await crawlerTask.ConfigureAwait(false);
    }

    private static async Task CrawlFilesAsync(
        string rootPath,
        SearchOptions options,
        GitIgnoreFilter? filter,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDir = stack.Pop();

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(currentDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entryPath in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDir = Directory.Exists(entryPath);

                if (filter is not null && filter.IsIgnored(entryPath, isDir))
                {
                    continue;
                }

                if (isDir)
                {
                    stack.Push(entryPath);
                }
                else
                {
                    // Check file extension pattern if provided
                    if (!string.IsNullOrEmpty(options.FilePattern) && !MatchesPattern(entryPath, options.FilePattern))
                    {
                        continue;
                    }

                    // Skip known binary extensions early
                    if (FileTypeClassifier.IsKnownBinaryExtension(entryPath))
                    {
                        continue;
                    }

                    while (!writer.TryWrite(entryPath))
                    {
                        if (!await writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
                        {
                            return;
                        }
                    }
                }
            }

            await Task.Yield();
        }
    }

    private static async Task SearchSingleFileContentAsync(
        string filePath,
        SearchOptions options,
        ChannelWriter<SearchResult> writer,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length > options.MaxFileSizeBytes || fileInfo.Length == 0)
            {
                return;
            }

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan | FileOptions.Asynchronous);

            // Fast-fail binary check on first chunk
            int firstRead = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, 8192)), cancellationToken).ConfigureAwait(false);
            if (firstRead == 0 || FileTypeClassifier.IsBinaryBuffer(buffer.AsSpan(0, firstRead)))
            {
                return;
            }

            stream.Seek(0, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var comparison = options.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            Regex? regex = null;
            if (options.IsRegex)
            {
                var regexOptions = RegexOptions.Compiled | (options.IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                regex = new Regex(options.Query, regexOptions);
            }

            var entry = new FileSystemEntry(
                filePath,
                fileInfo.Name,
                FileSystemEntryKind.File,
                IsReparsePoint: false);

            int lineNumber = 0;
            string? line;

            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                if (regex is not null)
                {
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        var result = new SearchResult(
                            entry,
                            lineNumber,
                            match.Index + 1,
                            line.TrimEnd(),
                            match.Length);

                        if (!await writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false) ||
                            !writer.TryWrite(result))
                        {
                            return;
                        }
                    }
                }
                else
                {
                    int index = line.IndexOf(options.Query, comparison);
                    if (index >= 0)
                    {
                        if (options.MatchWholeWord && !IsWholeWordMatch(line, index, options.Query.Length))
                        {
                            continue;
                        }

                        var result = new SearchResult(
                            entry,
                            lineNumber,
                            index + 1,
                            line.TrimEnd(),
                            options.Query.Length);

                        if (!await writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false) ||
                            !writer.TryWrite(result))
                        {
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Skip unreadable files
        }
    }

    private static bool MatchesPattern(string path, string pattern)
    {
        var fileName = Path.GetFileName(path);
        var patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pat in patterns)
        {
            var regex = "^" + Regex.Escape(pat).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            if (Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsWholeWordMatch(string line, int index, int length)
    {
        if (index > 0 && char.IsLetterOrDigit(line[index - 1]))
        {
            return false;
        }

        int afterIndex = index + length;
        if (afterIndex < line.Length && char.IsLetterOrDigit(line[afterIndex]))
        {
            return false;
        }

        return true;
    }
}
