using System.Collections.ObjectModel;
using System.Diagnostics;
using DotnetTerminalExplorer.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class SearchDialog : Dialog
{
    private readonly ISearchService _searchService;
    private readonly string _rootPath;
    private readonly IApplication? _application;
    private readonly ObservableCollection<string> _displayList = [];
    private readonly List<SearchResult> _results = [];
    private CancellationTokenSource? _searchCts;
    private bool _isContentMode = true;
    private bool _isCaseSensitive;
    private bool _isRegex;
    private bool _respectGitIgnore = true;

    public TextField QueryInput { get; }
    public CheckBox ContentModeCheck { get; }
    public CheckBox CaseSensitiveCheck { get; }
    public CheckBox RegexCheck { get; }
    public CheckBox GitIgnoreCheck { get; }
    public Label StatusLabel { get; }
    public ListView ResultsListView { get; }

    public SearchResult? SelectedResult { get; private set; }
    public event Action<SearchResult>? ResultChosen;

    public SearchDialog(
        ISearchService searchService,
        string rootPath,
        IApplication? application = null)
    {
        ArgumentNullException.ThrowIfNull(searchService);
        ArgumentNullException.ThrowIfNull(rootPath);

        _searchService = searchService;
        _rootPath = rootPath;
        _application = application;

        Title = "Workspace Search";
        Width = 84;
        Height = 24;

        var queryLabel = new Label
        {
            Text = "Search:",
            X = 1,
            Y = 0,
            Width = 8,
        };

        QueryInput = new TextField
        {
            X = Pos.Right(queryLabel) + 1,
            Y = 0,
            Width = Dim.Fill(1),
        };

        ContentModeCheck = new CheckBox
        {
            Text = "Content",
            Value = CheckState.Checked,
            X = 1,
            Y = 1,
        };

        CaseSensitiveCheck = new CheckBox
        {
            Text = "Match Case",
            Value = CheckState.UnChecked,
            X = Pos.Right(ContentModeCheck) + 2,
            Y = 1,
        };

        RegexCheck = new CheckBox
        {
            Text = "Regex",
            Value = CheckState.UnChecked,
            X = Pos.Right(CaseSensitiveCheck) + 2,
            Y = 1,
        };

        GitIgnoreCheck = new CheckBox
        {
            Text = "GitIgnore",
            Value = CheckState.Checked,
            X = Pos.Right(RegexCheck) + 2,
            Y = 1,
        };

        StatusLabel = new Label
        {
            Text = "Type a search query and press Enter",
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
        };

        ResultsListView = new ListView
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
        };
        ResultsListView.SetSource(_displayList);

        var closeButton = new Button
        {
            Text = "Cancel",
            X = Pos.AnchorEnd(12),
            Y = Pos.AnchorEnd(1),
        };

        closeButton.Accepting += (s, e) =>
        {
            RequestStop();
            e.Handled = true;
        };

        Add(
            queryLabel,
            QueryInput,
            ContentModeCheck,
            CaseSensitiveCheck,
            RegexCheck,
            GitIgnoreCheck,
            StatusLabel,
            ResultsListView,
            closeButton);

        ContentModeCheck.ValueChanged += (_, args) =>
        {
            _isContentMode = ContentModeCheck.Value == CheckState.Checked;
            TriggerSearch();
        };

        CaseSensitiveCheck.ValueChanged += (_, args) =>
        {
            _isCaseSensitive = CaseSensitiveCheck.Value == CheckState.Checked;
            TriggerSearch();
        };

        RegexCheck.ValueChanged += (_, args) =>
        {
            _isRegex = RegexCheck.Value == CheckState.Checked;
            TriggerSearch();
        };

        GitIgnoreCheck.ValueChanged += (_, args) =>
        {
            _respectGitIgnore = GitIgnoreCheck.Value == CheckState.Checked;
            TriggerSearch();
        };

        QueryInput.TextChanged += (_, _) => TriggerSearch();

        QueryInput.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.CursorDown && _results.Count > 0)
            {
                ResultsListView.SetFocus();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Enter)
            {
                ChooseSelected();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Esc)
            {
                RequestStop();
                keyEvent.Handled = true;
            }
        };

        ResultsListView.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.Enter)
            {
                ChooseSelected();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Esc)
            {
                RequestStop();
                keyEvent.Handled = true;
            }
        };

        ResultsListView.Accepting += (_, eventArgs) =>
        {
            ChooseSelected();
            eventArgs.Handled = true;
        };
    }

    private void ChooseSelected()
    {
        if (ResultsListView.SelectedItem is int idx && idx >= 0 && idx < _results.Count)
        {
            SelectedResult = _results[idx];
            ResultChosen?.Invoke(SelectedResult);
            RequestStop();
        }
    }

    public void TriggerSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        var query = QueryInput.Text.Trim();
        _results.Clear();
        _displayList.Clear();

        if (string.IsNullOrEmpty(query))
        {
            StatusLabel.Text = "Type a search query...";
            return;
        }

        var options = new SearchOptions
        {
            Query = query,
            Mode = _isContentMode ? SearchMode.Content : SearchMode.FileName,
            IsCaseSensitive = _isCaseSensitive,
            IsRegex = _isRegex,
            RespectGitIgnore = _respectGitIgnore,
        };

        StatusLabel.Text = "Searching...";
        var stopwatch = Stopwatch.StartNew();

        _ = Task.Run(async () =>
        {
            try
            {
                int count = 0;
                await foreach (var result in _searchService.SearchAsync(_rootPath, options, token))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var displayLine = FormatDisplayItem(result);
                    void AddAction()
                    {
                        _results.Add(result);
                        _displayList.Add(displayLine);
                        count++;
                        StatusLabel.Text = $"Searching... found {count} matches ({stopwatch.ElapsedMilliseconds} ms)";
                    }

                    if (_application is not null)
                    {
                        _application.Invoke(AddAction);
                    }
                    else
                    {
                        AddAction();
                    }
                }

                void CompleteAction()
                {
                    stopwatch.Stop();
                    StatusLabel.Text = _results.Count == 0
                        ? $"No matches found ({stopwatch.ElapsedMilliseconds} ms)"
                        : $"Found {_results.Count} matches in {stopwatch.ElapsedMilliseconds} ms";
                }

                if (_application is not null)
                {
                    _application.Invoke(CompleteAction);
                }
                else
                {
                    CompleteAction();
                }
            }
            catch (OperationCanceledException)
            {
                // Search was cancelled by a newer query
            }
            catch (Exception ex)
            {
                void ErrorAction()
                {
                    StatusLabel.Text = $"Error: {ex.Message}";
                }

                if (_application is not null)
                {
                    _application.Invoke(ErrorAction);
                }
                else
                {
                    ErrorAction();
                }
            }
        }, token);
    }

    private static string FormatDisplayItem(SearchResult result)
    {
        if (result.LineNumber > 0)
        {
            var snippet = result.LineText.Trim();
            if (snippet.Length > 50)
            {
                snippet = snippet[..47] + "...";
            }
            return $"{result.Entry.Name}:{result.LineNumber}  {snippet}";
        }

        return $"{result.Entry.Name}  ({result.Entry.FullPath})";
    }
}
