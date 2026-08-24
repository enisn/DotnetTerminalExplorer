#pragma warning disable CS0618

using Terminal.Gui.Views;

namespace DotnetTerminalExplorer.Tests;

public sealed class EditorFindBarTests
{
    [Fact]
    public void UpdateMatches_FindsMatchesCaseInsensitiveByDefault()
    {
        var textView = new TextView
        {
            Text = "Hello World\nhello Universe\nanother HELLO line"
        };

        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "hello";

        Assert.Equal(3, findBar.Matches.Count);
        Assert.Equal(0, findBar.CurrentMatchIndex);
        Assert.Equal("1 of 3", findBar.MatchCountLabel.Text);
        Assert.Equal((0, 0, 5), findBar.Matches[0]);
        Assert.Equal((1, 0, 5), findBar.Matches[1]);
        Assert.Equal((2, 8, 5), findBar.Matches[2]);
    }

    [Fact]
    public void NextMatch_And_PreviousMatch_CycleThroughMatches()
    {
        var textView = new TextView
        {
            Text = "one two three\none two four\none two five"
        };

        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "two";

        Assert.Equal(3, findBar.Matches.Count);
        Assert.Equal(0, findBar.CurrentMatchIndex);
        Assert.Equal("1 of 3", findBar.MatchCountLabel.Text);

        findBar.NextMatch();
        Assert.Equal(1, findBar.CurrentMatchIndex);
        Assert.Equal("2 of 3", findBar.MatchCountLabel.Text);

        findBar.NextMatch();
        Assert.Equal(2, findBar.CurrentMatchIndex);
        Assert.Equal("3 of 3", findBar.MatchCountLabel.Text);

        // Wrap around to first
        findBar.NextMatch();
        Assert.Equal(0, findBar.CurrentMatchIndex);
        Assert.Equal("1 of 3", findBar.MatchCountLabel.Text);

        // Wrap backwards to last
        findBar.PreviousMatch();
        Assert.Equal(2, findBar.CurrentMatchIndex);
        Assert.Equal("3 of 3", findBar.MatchCountLabel.Text);
    }

    [Fact]
    public void UpdateMatches_WhenNoMatches_ShowsNoMatches()
    {
        var textView = new TextView
        {
            Text = "sample content"
        };

        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "nonexistent";

        Assert.Empty(findBar.Matches);
        Assert.Equal(-1, findBar.CurrentMatchIndex);
        Assert.Equal("No matches", findBar.MatchCountLabel.Text);
    }

    [Fact]
    public void UpdateMatches_SelectsAndVisuallyHighlightsActiveMatchInTextView()
    {
        var textView = new TextView { Text = "Line 1\nLine 2 hello world\nLine 3" };
        var findBar = new EditorFindBar(textView);

        findBar.QueryInput.Text = "hello";

        Assert.True(textView.IsSelecting);
        Assert.Equal("hello", textView.SelectedText);

        findBar.Close();
        Assert.False(textView.IsSelecting);
    }

    [Fact]
    public void Reset_ClearsInputAndClosesFindBar()
    {
        var textView = new TextView { Text = "some content" };
        var findBar = new EditorFindBar(textView);
        findBar.Open();
        findBar.QueryInput.Text = "content";

        Assert.True(findBar.Visible);
        Assert.NotEmpty(findBar.QueryInput.Text);

        findBar.Reset();
        Assert.False(findBar.Visible);
        Assert.Empty(findBar.QueryInput.Text);
        Assert.False(textView.IsSelecting);
    }
}
