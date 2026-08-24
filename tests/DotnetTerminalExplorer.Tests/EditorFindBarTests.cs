#pragma warning disable CS0618

using Terminal.Gui.Input;
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

    [Fact]
    public void Open_WithReplaceMode_ShowsReplaceRow()
    {
        var textView = new TextView { Text = "sample content" };
        var findBar = new EditorFindBar(textView);

        findBar.Open(replaceMode: true);

        Assert.True(findBar.Visible);
        Assert.True(findBar.IsReplaceMode);
        Assert.True(findBar.ReplaceInput.Visible);
    }

    [Fact]
    public void ToggleReplaceMode_ExpandsAndCollapsesReplaceRow()
    {
        var textView = new TextView { Text = "sample content" };
        var findBar = new EditorFindBar(textView);
        findBar.Open();

        Assert.False(findBar.IsReplaceMode);
        Assert.False(findBar.ReplaceInput.Visible);

        findBar.ToggleReplaceMode();
        Assert.True(findBar.IsReplaceMode);
        Assert.True(findBar.ReplaceInput.Visible);

        findBar.ToggleReplaceMode();
        Assert.False(findBar.IsReplaceMode);
        Assert.False(findBar.ReplaceInput.Visible);
    }

    [Fact]
    public void ReplaceCurrent_ReplacesHighlightedMatchAndAdvancesToNext()
    {
        var textView = new TextView { Text = "foo bar foo" };
        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "foo";
        findBar.ReplaceInput.Text = "baz";

        findBar.ReplaceCurrent();

        Assert.Equal("baz bar foo", textView.Text);
        Assert.Single(findBar.Matches);
        Assert.Equal((0, 8, 3), findBar.Matches[0]);
        Assert.Equal(0, findBar.CurrentMatchIndex);
        Assert.Equal("1 of 1", findBar.MatchCountLabel.Text);

        findBar.ReplaceCurrent();

        Assert.Equal("baz bar baz", textView.Text);
        Assert.Empty(findBar.Matches);
    }

    [Fact]
    public void ReplaceAll_ReplacesEveryOccurrenceAcrossLines_CaseInsensitiveByDefault()
    {
        var textView = new TextView { Text = "cat dog\ncat\ncat cat" };
        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "CAT";
        findBar.ReplaceInput.Text = "dog";

        findBar.ReplaceAll();

        Assert.Equal("dog dog\ndog\ndog dog", textView.Text);
        Assert.Equal("Replaced 4 occurrences", findBar.MatchCountLabel.Text);
    }

    [Fact]
    public void ReplaceAll_RespectsCaseSensitiveToggle()
    {
        var textView = new TextView { Text = "Hello hello HELLO" };
        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "hello";
        findBar.ReplaceInput.Text = "hey";
        findBar.ToggleCaseSensitivity();

        findBar.ReplaceAll();

        Assert.Equal("Hello hey HELLO", textView.Text);
        Assert.Equal("Replaced 1 occurrence [Aa]", findBar.MatchCountLabel.Text);
    }

    [Fact]
    public void ReplaceAll_WithEmptyReplacement_DeletesMatches()
    {
        var textView = new TextView { Text = "ab cd ab" };
        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "ab";
        findBar.ReplaceInput.Text = string.Empty;

        findBar.ReplaceAll();

        Assert.Equal(" cd ", textView.Text);
    }

    [Fact]
    public void ReplaceAll_WhenNoMatches_ChangesNothing()
    {
        var textView = new TextView { Text = "one two" };
        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "zzz";
        findBar.ReplaceInput.Text = "y";

        findBar.ReplaceAll();

        Assert.Equal("one two", textView.Text);
        Assert.Equal("No matches", findBar.MatchCountLabel.Text);
    }

    [Fact]
    public void Replace_WhenReadOnly_ShowsHintAndKeepsText()
    {
        var textView = new TextView { Text = "alpha beta", ReadOnly = true };
        var findBar = new EditorFindBar(textView);
        findBar.QueryInput.Text = "alpha";
        findBar.ReplaceInput.Text = "gamma";

        findBar.ReplaceCurrent();
        Assert.Equal("alpha beta", textView.Text);
        Assert.Equal("Read-only", findBar.MatchCountLabel.Text);

        findBar.MatchCountLabel.Text = string.Empty;
        findBar.ReplaceAll();
        Assert.Equal("alpha beta", textView.Text);
        Assert.Equal("Read-only", findBar.MatchCountLabel.Text);
    }

    [Fact]
    public void Reset_ClearsReplaceInputAndExitsReplaceMode()
    {
        var textView = new TextView { Text = "sample content" };
        var findBar = new EditorFindBar(textView);
        findBar.Open(replaceMode: true);
        findBar.ReplaceInput.Text = "x";

        findBar.Reset();

        Assert.False(findBar.Visible);
        Assert.False(findBar.IsReplaceMode);
        Assert.False(findBar.ReplaceInput.Visible);
        Assert.Empty(findBar.QueryInput.Text);
        Assert.Empty(findBar.ReplaceInput.Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QueryInput_CtrlHTogglesReplaceMode_InsteadOfDeletingWord(bool terminalEncodesCtrlHAsBackspace)
    {
        var textView = new TextView { Text = "sample content" };
        var findBar = new EditorFindBar(textView);
        findBar.Open();
        findBar.QueryInput.Text = "hello world";

        var key = terminalEncodesCtrlHAsBackspace ? Key.Backspace.WithCtrl : Key.H.WithCtrl;
        var handled = findBar.QueryInput.NewKeyDownEvent(key);

        Assert.True(handled);
        Assert.True(findBar.IsReplaceMode);
        Assert.True(findBar.ReplaceInput.Visible);
        Assert.Equal("hello world", findBar.QueryInput.Text);
    }

    [Fact]
    public void ReplaceToggleButton_Activating_TogglesReplaceMode()
    {
        var textView = new TextView { Text = "sample content" };
        var findBar = new EditorFindBar(textView);
        findBar.Open();

        Assert.False(findBar.IsReplaceMode);

        findBar.ReplaceToggleButton.NewKeyDownEvent(Key.Space);
        Assert.True(findBar.IsReplaceMode);

        findBar.ReplaceToggleButton.NewKeyDownEvent(Key.Space);
        Assert.False(findBar.IsReplaceMode);
    }
}
