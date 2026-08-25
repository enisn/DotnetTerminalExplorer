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

    private sealed class StreamingSearchService : ISearchService
    {
        public async IAsyncEnumerable<SearchResult> SearchAsync(
            string rootPath,
            SearchOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken);
            yield return CreateResult("first.cs");
            await Task.Delay(400, cancellationToken);
            yield return CreateResult("second.cs");
        }

        private static SearchResult CreateResult(string fileName)
        {
            var entry = new FileSystemEntry($"/test/path/{fileName}", fileName, FileSystemEntryKind.File, false);
            return new SearchResult(entry, 0, 0, $"/test/path/{fileName}", 5);
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
        Assert.NotNull(dialog.ShowInTreeButton);
    }

    [Fact]
    public void ShowInTreeButton_DisabledInitially()
    {
        var dialog = new SearchDialog(new FakeSearchService(), "/test/path");

        Assert.False(dialog.ShowInTreeButton.Enabled);
    }

    [Fact]
    public async Task ShowInTreeButton_EnabledOnlyAfterSearchCompletes()
    {
        var streamingSearch = new StreamingSearchService();
        var dialog = new SearchDialog(streamingSearch, "/test/path");
        dialog.QueryInput.Text = "needle";

        await Task.Delay(450);

        Assert.Contains("Searching... found 1 matches", dialog.StatusLabel.Text);
        Assert.False(dialog.ShowInTreeButton.Enabled);

        await Task.Delay(700);

        Assert.Contains("Found 2 matches in", dialog.StatusLabel.Text);
        Assert.True(dialog.ShowInTreeButton.Enabled);
    }

    [Fact]
    public async Task ShowInTreeButton_DisabledAgainWhenNewSearchStarts()
    {
        var fakeSearch = new FakeSearchService();
        fakeSearch.ResultsToReturn.Add(CreateFileMatch());
        var dialog = new SearchDialog(fakeSearch, "/test/path");
        dialog.QueryInput.Text = "MyMethod";

        await Task.Delay(500);
        Assert.True(dialog.ShowInTreeButton.Enabled);

        dialog.QueryInput.Text = "AnotherQuery";

        Assert.False(dialog.ShowInTreeButton.Enabled);
    }

    [Fact]
    public async Task ShowInTreeButton_NotEnabledWhenNoResultsFound()
    {
        var fakeSearch = new FakeSearchService();
        var dialog = new SearchDialog(fakeSearch, "/test/path");
        dialog.QueryInput.Text = "nothing";

        await Task.Delay(500);

        Assert.Contains("No matches found", dialog.StatusLabel.Text);
        Assert.False(dialog.ShowInTreeButton.Enabled);
    }

    [Fact]
    public async Task RequestShowInTree_PublishesDistinctResultSnapshot()
    {
        var fakeSearch = new FakeSearchService();
        fakeSearch.ResultsToReturn.Add(CreateFileMatch());
        fakeSearch.ResultsToReturn.Add(CreateFileMatch());
        IReadOnlyList<SearchResult>? received = null;
        var dialog = new SearchDialog(fakeSearch, "/test/path");
        dialog.ShowInTreeRequested += results => received = results;
        dialog.QueryInput.Text = "MyMethod";

        await Task.Delay(500);
        dialog.RequestShowInTree();

        Assert.NotNull(received);
        Assert.Equal(2, received!.Count);
    }

    [Fact]
    public void RequestShowInTree_WithoutResults_DoesNotPublish()
    {
        var dialog = new SearchDialog(new FakeSearchService(), "/test/path");
        var published = false;
        dialog.ShowInTreeRequested += _ => published = true;

        dialog.RequestShowInTree();

        Assert.False(published);
    }

    [Fact]
    public async Task SearchDialog_TriggerSearch_ShowsLoadingBeforeResultsArrive()
    {
        var fakeSearch = new FakeSearchService();
        fakeSearch.ResultsToReturn.Add(CreateFileMatch());

        var dialog = new SearchDialog(fakeSearch, "/test/path");
        dialog.QueryInput.Text = "MyMethod";

        // Debounce window has not elapsed yet, results must not be shown.
        await Task.Delay(100);

        Assert.Equal("Loading...", dialog.StatusLabel.Text);
    }

    [Fact]
    public async Task SearchDialog_TriggerSearch_PopulatesResults()
    {
        var fakeSearch = new FakeSearchService();
        fakeSearch.ResultsToReturn.Add(CreateFileMatch());

        var dialog = new SearchDialog(fakeSearch, "/test/path");
        dialog.QueryInput.Text = "MyMethod";

        // Wait past debounce for async search task to complete.
        await Task.Delay(500);

        Assert.Contains("Found 1 matches", dialog.StatusLabel.Text);
    }

    [Fact]
    public async Task SearchDialog_TriggerSearch_DisplaysResultsAsTheyArrive()
    {
        var streamingSearch = new StreamingSearchService();
        var dialog = new SearchDialog(streamingSearch, "/test/path");
        dialog.QueryInput.Text = "needle";

        // First result arrives ~300ms in; flush timer publishes it before the second one.
        await Task.Delay(450);

        Assert.Contains("Searching... found 1 matches", dialog.StatusLabel.Text);

        await Task.Delay(600);

        Assert.Contains("Found 2 matches in", dialog.StatusLabel.Text);
        Assert.Equal(2, dialog.ResultsListView.Source?.Count ?? -1);
    }

    private static SearchResult CreateFileMatch()
    {
        var entry = new FileSystemEntry("/test/path/test.cs", "test.cs", FileSystemEntryKind.File, false);
        return new SearchResult(entry, 10, 5, "public void MyMethod()", 8);
    }
}
