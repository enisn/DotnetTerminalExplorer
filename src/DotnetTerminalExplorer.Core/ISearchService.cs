namespace DotnetTerminalExplorer.Core;

public interface ISearchService
{
    IAsyncEnumerable<SearchResult> SearchAsync(
        string rootPath,
        SearchOptions options,
        CancellationToken cancellationToken = default);
}
