#pragma warning disable CS0618 // The editor intentionally uses Terminal.Gui TextView.

using System.Drawing;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class EditorFindBar : View
{
    private const int LabelColumnWidth = 9;
    private const int ReplaceButtonWidth = 11;

    private readonly TextView _textView;
    private readonly Label _replaceLabel;
    private readonly List<(int Line, int Column, int Length)> _matches = [];
    private int _currentMatchIndex = -1;
    private bool _isCaseSensitive;
    private bool _isReplaceMode;

    public TextField QueryInput { get; }

    public TextField ReplaceInput { get; }

    public Label MatchCountLabel { get; }

    public Button ReplaceToggleButton { get; }

    public Action? OnClose { get; set; }

    public EditorFindBar(TextView textView)
    {
        ArgumentNullException.ThrowIfNull(textView);
        _textView = textView;

        SetScheme(TuiSchemes.InputScheme);

        CanFocus = true;
        Height = 1;
        Width = Dim.Fill();
        Y = Pos.AnchorEnd(1);

        var findLabel = new Label
        {
            Text = "Find: ",
            X = 0,
            Y = 0,
            Width = LabelColumnWidth,
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
            Width = Dim.Fill(ReplaceButtonWidth + 2),
            CanFocus = false,
        };

        ReplaceToggleButton = new Button
        {
            Text = "Replace",
            X = Pos.AnchorEnd(ReplaceButtonWidth),
            Y = 0,
            Width = ReplaceButtonWidth,
            Height = 1,
            CanFocus = true,
        };

        _replaceLabel = new Label
        {
            Text = "Replace: ",
            X = 0,
            Y = 1,
            Width = LabelColumnWidth,
            CanFocus = false,
            Visible = false,
        };

        ReplaceInput = new TextField
        {
            X = Pos.Right(_replaceLabel),
            Y = 1,
            Width = 30,
            CanFocus = true,
            Visible = false,
        };

        Add(findLabel, QueryInput, MatchCountLabel, ReplaceToggleButton, _replaceLabel, ReplaceInput);

        ReplaceToggleButton.Accepting += (sender, args) =>
        {
            ToggleReplaceMode();
            args.Handled = true;
        };

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
                ToggleCaseSensitivity();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.F.WithCtrl)
            {
                QueryInput.SelectAll();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Tab && _isReplaceMode)
            {
                ReplaceInput.SetFocus();
                keyEvent.Handled = true;
            }
            else if (keyEvent == new Key('r').WithAlt || keyEvent == new Key('R').WithAlt)
            {
                ToggleReplaceMode();
                keyEvent.Handled = true;
            }
            else if (IsReplaceShortcut(keyEvent))
            {
                ToggleReplaceMode();
                keyEvent.Handled = true;
            }
        };

        ReplaceInput.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.Enter.WithCtrl)
            {
                ReplaceAll();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Enter)
            {
                ReplaceCurrent();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Esc)
            {
                Close();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Tab || keyEvent == Key.Tab.WithShift)
            {
                QueryInput.SetFocus();
                keyEvent.Handled = true;
            }
            else if (keyEvent == new Key('c').WithAlt || keyEvent == new Key('C').WithAlt)
            {
                ToggleCaseSensitivity();
                keyEvent.Handled = true;
            }
            else if (keyEvent == new Key('r').WithAlt || keyEvent == new Key('R').WithAlt)
            {
                ToggleReplaceMode();
                keyEvent.Handled = true;
            }
            else if (IsReplaceShortcut(keyEvent))
            {
                ToggleReplaceMode();
                keyEvent.Handled = true;
            }
        };
    }

    public IReadOnlyList<(int Line, int Column, int Length)> Matches => _matches;

    public int CurrentMatchIndex => _currentMatchIndex;

    public bool IsCaseSensitive => _isCaseSensitive;

    public bool IsReplaceMode => _isReplaceMode;

    public void Open() => Open(replaceMode: false);

    public void Open(bool replaceMode)
    {
        if (replaceMode != _isReplaceMode)
        {
            _isReplaceMode = replaceMode;
            UpdateLayoutForMode();
        }

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
        ReplaceInput.Text = string.Empty;
        _isReplaceMode = false;
        UpdateLayoutForMode();
    }

    public void ToggleReplaceMode()
    {
        _isReplaceMode = !_isReplaceMode;
        UpdateLayoutForMode();

        if (Visible)
        {
            if (_isReplaceMode)
            {
                ReplaceInput.SetFocus();
            }
            else
            {
                QueryInput.SetFocus();
            }
        }
    }

    public void ToggleCaseSensitivity()
    {
        _isCaseSensitive = !_isCaseSensitive;
        UpdateMatches();
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

    public void ReplaceCurrent()
    {
        if (_textView.ReadOnly)
        {
            MatchCountLabel.Text = "Read-only";
            return;
        }

        if (_currentMatchIndex < 0 || _currentMatchIndex >= _matches.Count)
        {
            return;
        }

        var match = _matches[_currentMatchIndex];
        var lines = _textView.Text.Split('\n');
        if (match.Line >= lines.Length)
        {
            UpdateMatches();
            return;
        }

        var replacedLine = SpliceMatch(lines[match.Line], match.Column, match.Length, ReplaceInput.Text);
        if (replacedLine is null)
        {
            UpdateMatches();
            return;
        }

        lines[match.Line] = replacedLine;
        _textView.Text = string.Join("\n", lines);
        UpdateMatches();
        HighlightMatchAtOrAfter(match.Line, match.Column);
    }

    public void ReplaceAll()
    {
        if (_textView.ReadOnly)
        {
            MatchCountLabel.Text = "Read-only";
            return;
        }

        var query = QueryInput.Text;
        if (string.IsNullOrEmpty(query) || _matches.Count == 0)
        {
            return;
        }

        var replacement = ReplaceInput.Text;
        var comparison = _isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var lines = _textView.Text.Split('\n');
        var replacedCount = 0;

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var newLine = ReplaceOccurrences(lines[lineIdx], query, replacement, comparison, out var count);
            if (count > 0)
            {
                replacedCount += count;
                lines[lineIdx] = newLine;
            }
        }

        _textView.Text = string.Join("\n", lines);
        UpdateMatches();

        var caseIndicator = _isCaseSensitive ? " [Aa]" : string.Empty;
        MatchCountLabel.Text = replacedCount == 1
            ? $"Replaced 1 occurrence{caseIndicator}"
            : $"Replaced {replacedCount} occurrences{caseIndicator}";
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

    private void UpdateLayoutForMode()
    {
        var rows = _isReplaceMode ? 2 : 1;
        Height = rows;
        Y = Pos.AnchorEnd(rows);
        _replaceLabel.Visible = _isReplaceMode;
        ReplaceInput.Visible = _isReplaceMode;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void HighlightMatchAtOrAfter(int line, int column)
    {
        for (int i = 0; i < _matches.Count; i++)
        {
            var candidate = _matches[i];
            if (candidate.Line > line || (candidate.Line == line && candidate.Column >= column))
            {
                _currentMatchIndex = i;
                HighlightCurrentMatch();
                return;
            }
        }

        if (_matches.Count > 0)
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

    internal static bool IsReplaceShortcut(Key key) =>
        key == Key.H.WithCtrl || key == Key.Backspace.WithCtrl;

    private static string? SpliceMatch(string rawLine, int column, int length, string replacement)
    {
        var hasCarriageReturn = rawLine.EndsWith('\r');
        var line = hasCarriageReturn ? rawLine[..^1] : rawLine;

        if (column < 0 || column + length > line.Length)
        {
            return null;
        }

        var newLine = line[..column] + replacement + line[(column + length)..];
        return hasCarriageReturn ? newLine + "\r" : newLine;
    }

    private static string ReplaceOccurrences(
        string rawLine,
        string query,
        string replacement,
        StringComparison comparison,
        out int replacedCount)
    {
        replacedCount = 0;
        var hasCarriageReturn = rawLine.EndsWith('\r');
        var line = hasCarriageReturn ? rawLine[..^1] : rawLine;

        var result = new StringBuilder();
        var searchIndex = 0;
        while (searchIndex < line.Length)
        {
            var matchIndex = line.IndexOf(query, searchIndex, comparison);
            if (matchIndex < 0)
            {
                result.Append(line[searchIndex..]);
                break;
            }

            result.Append(line[searchIndex..matchIndex]);
            result.Append(replacement);
            replacedCount++;
            searchIndex = matchIndex + query.Length;
        }

        var newLine = result.ToString();
        return hasCarriageReturn ? newLine + "\r" : newLine;
    }
}
