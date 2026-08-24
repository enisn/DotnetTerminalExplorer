#pragma warning disable CS0618 // The editor intentionally uses Terminal.Gui TextView.

using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class EditorFindBar : View
{
    private readonly TextView _textView;
    private readonly List<(int Line, int Column, int Length)> _matches = [];
    private int _currentMatchIndex = -1;
    private bool _isCaseSensitive;

    public TextField QueryInput { get; }
    public Label MatchCountLabel { get; }
    public Action? OnClose { get; set; }

    public EditorFindBar(TextView textView)
    {
        ArgumentNullException.ThrowIfNull(textView);
        _textView = textView;

        CanFocus = true;
        Height = 1;
        Width = Dim.Fill();

        var findLabel = new Label
        {
            Text = "Find: ",
            X = 0,
            Y = 0,
            Width = 6,
            CanFocus = false,
        };

        QueryInput = new TextField
        {
            X = Pos.Right(findLabel),
            Y = 0,
            Width = 30,
            CanFocus = true,
        };

        MatchCountLabel = new Label
        {
            Text = string.Empty,
            X = Pos.Right(QueryInput) + 1,
            Y = 0,
            Width = Dim.Fill(),
            CanFocus = false,
        };

        Add(findLabel, QueryInput, MatchCountLabel);

        QueryInput.TextChanged += (_, _) => UpdateMatches();

        QueryInput.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.Enter || keyEvent == Key.F3 || keyEvent == Key.CursorDown)
            {
                NextMatch();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Enter.WithShift || keyEvent == Key.F3.WithShift || keyEvent == Key.CursorUp)
            {
                PreviousMatch();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Esc)
            {
                Close();
                keyEvent.Handled = true;
            }
            else if (keyEvent == new Key('c').WithAlt || keyEvent == new Key('C').WithAlt)
            {
                _isCaseSensitive = !_isCaseSensitive;
                UpdateMatches();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.F.WithCtrl)
            {
                QueryInput.SelectAll();
                keyEvent.Handled = true;
            }
        };
    }

    public IReadOnlyList<(int Line, int Column, int Length)> Matches => _matches;

    public int CurrentMatchIndex => _currentMatchIndex;

    public bool IsCaseSensitive => _isCaseSensitive;

    public void Open()
    {
        Visible = true;
        CanFocus = true;
        QueryInput.CanFocus = true;
        QueryInput.SetFocus();
        UpdateMatches();
    }

    public void Close(bool restoreFocus = true)
    {
        var wasVisible = Visible;
        Visible = false;
        _matches.Clear();
        _currentMatchIndex = -1;
        MatchCountLabel.Text = string.Empty;
        _textView.IsSelecting = false;
        _textView.SetNeedsDraw();

        if (wasVisible && restoreFocus)
        {
            OnClose?.Invoke();
        }
    }

    public void Reset()
    {
        Close(restoreFocus: false);
        QueryInput.Text = string.Empty;
    }

    public void NextMatch()
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _currentMatchIndex = (_currentMatchIndex + 1) % _matches.Count;
        HighlightCurrentMatch();
    }

    public void PreviousMatch()
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _currentMatchIndex = (_currentMatchIndex - 1 + _matches.Count) % _matches.Count;
        HighlightCurrentMatch();
    }

    public void UpdateMatches()
    {
        _matches.Clear();
        _currentMatchIndex = -1;

        var query = QueryInput.Text;
        if (string.IsNullOrEmpty(query))
        {
            MatchCountLabel.Text = string.Empty;
            _textView.IsSelecting = false;
            _textView.SetNeedsDraw();
            return;
        }

        var text = _textView.Text;
        if (string.IsNullOrEmpty(text))
        {
            MatchCountLabel.Text = "No matches";
            _textView.IsSelecting = false;
            _textView.SetNeedsDraw();
            return;
        }

        var comparison = _isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Split text by lines to compute (Line, Column)
        var lines = text.Split('\n');
        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var line = lines[lineIdx].TrimEnd('\r');
            int colIdx = 0;

            while (colIdx < line.Length)
            {
                int matchCol = line.IndexOf(query, colIdx, comparison);
                if (matchCol < 0)
                {
                    break;
                }

                _matches.Add((lineIdx, matchCol, query.Length));
                colIdx = matchCol + Math.Max(1, query.Length);
            }
        }

        if (_matches.Count == 0)
        {
            MatchCountLabel.Text = _isCaseSensitive ? "No matches [Aa]" : "No matches";
            _textView.IsSelecting = false;
            _textView.SetNeedsDraw();
        }
        else
        {
            _currentMatchIndex = 0;
            HighlightCurrentMatch();
        }
    }

    private void HighlightCurrentMatch()
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _matches.Count)
        {
            _textView.IsSelecting = false;
            _textView.SetNeedsDraw();
            return;
        }

        var match = _matches[_currentMatchIndex];
        var caseIndicator = _isCaseSensitive ? " [Aa]" : string.Empty;
        MatchCountLabel.Text = $"{_currentMatchIndex + 1} of {_matches.Count}{caseIndicator}";

        try
        {
            _textView.SelectionStartRow = match.Line;
            _textView.SelectionStartColumn = match.Column;
            _textView.InsertionPoint = new Point(match.Column + match.Length, match.Line);
            _textView.IsSelecting = true;
            _textView.ScrollTo(new Point(0, Math.Max(0, match.Line - 5)));
            _textView.SetNeedsDraw();
        }
        catch
        {
            // Ignore bounds if document changed
        }
    }
}
