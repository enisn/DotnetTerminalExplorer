using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Tests;

public sealed class SearchDialogTests
{
    private sealed class FakeSearchService : ISearchService
    {
        public List<SearchResult> ResultsToReturn { get; } = [];

        public async IAsyncEnumerable<SearchResult> SearchAsync(
            string rootPath,
            SearchOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (var result in ResultsToReturn)
            {
                yield return result;
            }
        }
    }

    [Fact]
    public void SearchDialog_InitializesControlsProperly()
    {
        var fakeSearch = new FakeSearchService();
        var dialog = new SearchDialog(fakeSearch, "/test/path");

        Assert.Equal("Workspace Search", dialog.Title);
        Assert.NotNull(dialog.QueryInput);
        Assert.NotNull(dialog.ContentModeCheck);
        Assert.NotNull(dialog.CaseSensitiveCheck);
        Assert.NotNull(dialog.RegexCheck);
        Assert.NotNull(dialog.GitIgnoreCheck);
        Assert.NotNull(dialog.ResultsListView);
    }

    [Fact]
    public async Task SearchDialog_TriggerSearch_PopulatesResults()
    {
        var fakeSearch = new FakeSearchService();
        var entry = new FileSystemEntry("/test/path/test.cs", "test.cs", FileSystemEntryKind.File, false);
        fakeSearch.ResultsToReturn.Add(new SearchResult(entry, 10, 5, "public void MyMethod()", 8));

        var dialog = new SearchDialog(fakeSearch, "/test/path");
        dialog.QueryInput.Text = "MyMethod";

        // Wait brief moment for async search task to complete
        await Task.Delay(100);

        Assert.Contains("Found 1 matches", dialog.StatusLabel.Text);
    }
}
