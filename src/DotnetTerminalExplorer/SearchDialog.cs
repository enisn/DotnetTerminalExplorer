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
    private const int SearchDebounceMilliseconds = 250;
    private const int FlushIntervalMilliseconds = 100;
    private const int FlushBatchSize = 200;
    private const int MaxInFlightUiBatches = 3;

    private readonly ISearchService _searchService;
    private readonly string _rootPath;
    private readonly IApplication? _application;
    private readonly ObservableCollection<string> _displayList = [];
    private readonly List<SearchResult> _results = [];
    private readonly object _pendingLock = new();
    private readonly List<(int Generation, SearchResult Result, string DisplayLine)> _pendingResults = [];
    private CancellationTokenSource? _searchCts;
    private System.Threading.Timer? _flushTimer;
    private int _searchGeneration;
    private int _inFlightUiBatches;
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
    public Button ShowInTreeButton { get; }

    public SearchResult? SelectedResult { get; private set; }
    public event Action<SearchResult>? ResultChosen;
    public event Action<IReadOnlyList<SearchResult>>? ShowInTreeRequested;

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
        SetScheme(TuiSchemes.InputScheme);

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

        ShowInTreeButton = new Button
        {
            Text = "Show in Tree",
            X = 1,
            Y = Pos.AnchorEnd(1),
            Enabled = false,
        };

        ShowInTreeButton.Accepting += (s, e) =>
        {
            RequestShowInTree();
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
            ShowInTreeButton,
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

    public void RequestShowInTree()
    {
        if (_results.Count == 0)
        {
            return;
        }

        ShowInTreeRequested?.Invoke(_results.ToArray());
        RequestStop();
    }

    public void TriggerSearch()
    {
        var generation = Interlocked.Increment(ref _searchGeneration);

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        StopFlushTimer();
        lock (_pendingLock)
        {
            _pendingResults.Clear();
        }

        _results.Clear();
        _displayList.Clear();
        ShowInTreeButton.Enabled = false;

        var query = QueryInput.Text.Trim();

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

        StatusLabel.Text = "Loading...";
        var stopwatch = Stopwatch.StartNew();

        _ = RunSearchAsync(generation, token, options, stopwatch);
    }

    private async Task RunSearchAsync(
        int generation,
        CancellationToken token,
        SearchOptions options,
        Stopwatch stopwatch)
    {
        try
        {
            await Task.Delay(SearchDebounceMilliseconds, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (generation != Volatile.Read(ref _searchGeneration))
        {
            return;
        }

        StartFlushTimer(generation, stopwatch);

        try
        {
            await foreach (var result in _searchService.SearchAsync(_rootPath, options, token).ConfigureAwait(false))
            {
                var displayLine = FormatDisplayItem(result);
                var flushNow = false;

                lock (_pendingLock)
                {
                    if (generation == Volatile.Read(ref _searchGeneration))
                    {
                        _pendingResults.Add((generation, result, displayLine));
                        flushNow = _pendingResults.Count >= FlushBatchSize;
                    }
                }

                if (flushNow)
                {
                    FlushPendingResults(generation, stopwatch);
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }
            }

            StopFlushTimer();
            FlushPendingResults(generation, stopwatch, bypassBackpressure: true);
            ShowCompletionStatus(generation, stopwatch);
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled by a newer query or dialog disposal.
        }
        catch (Exception ex)
        {
            void ErrorAction()
            {
                if (generation != Volatile.Read(ref _searchGeneration))
                {
                    return;
                }

                StatusLabel.Text = $"Error: {ex.Message}";
            }

            MarshalToUi(ErrorAction);
        }
    }

    private void FlushPendingResults(int generation, Stopwatch stopwatch, bool bypassBackpressure = false)
    {
        // Backpressure: if the UI thread is still draining previous batches, leave
        // results pending and let the flush timer retry shortly.
        if (!bypassBackpressure &&
            _application is not null &&
            Volatile.Read(ref _inFlightUiBatches) >= MaxInFlightUiBatches)
        {
            return;
        }

        List<(SearchResult Result, string DisplayLine)> batch;

        lock (_pendingLock)
        {
            if (_pendingResults.Count == 0)
            {
                return;
            }

            batch = _pendingResults
                .Where(p => p.Generation == generation)
                .Select(p => (p.Result, p.DisplayLine))
                .ToList();

            _pendingResults.RemoveAll(p => p.Generation == generation);

            if (batch.Count == 0)
            {
                return;
            }
        }

        void ApplyBatch()
        {
            try
            {
                if (generation != Volatile.Read(ref _searchGeneration))
                {
                    return;
                }

                // Suspending events avoids ListWrapper rescanning the whole list
                // (marks + MaxItemLength) on every single Add, which is O(n^2).
                ResultsListView.SuspendCollectionChangedEvent();
                try
                {
                    foreach (var (result, displayLine) in batch)
                    {
                        _results.Add(result);
                        _displayList.Add(displayLine);
                    }
                }
                finally
                {
                    ResultsListView.ResumeSuspendCollectionChangedEvent();
                }

                ResultsListView.SetContentSize(
                    new System.Drawing.Size(
                        ResultsListView.MaxItemLength,
                        Math.Max(_displayList.Count, ResultsListView.Viewport.Height)));

                StatusLabel.Text = $"Searching... found {_results.Count} matches ({stopwatch.ElapsedMilliseconds} ms)";
            }
            finally
            {
                if (_application is not null)
                {
                    Interlocked.Decrement(ref _inFlightUiBatches);
                }
            }
        }

        if (_application is not null)
        {
            Interlocked.Increment(ref _inFlightUiBatches);
            _application.Invoke(ApplyBatch);
        }
        else
        {
            ApplyBatch();
        }
    }

    private void ShowCompletionStatus(int generation, Stopwatch stopwatch)
    {
        void CompleteAction()
        {
            if (generation != Volatile.Read(ref _searchGeneration))
            {
                return;
            }

            stopwatch.Stop();
            StatusLabel.Text = _results.Count == 0
                ? $"No matches found ({stopwatch.ElapsedMilliseconds} ms)"
                : $"Found {_results.Count} matches in {stopwatch.ElapsedMilliseconds} ms";
            ShowInTreeButton.Enabled = _results.Count > 0;
        }

        MarshalToUi(CompleteAction);
    }

    private void MarshalToUi(Action action)
    {
        if (_application is not null)
        {
            _application.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private void StartFlushTimer(int generation, Stopwatch stopwatch)
    {
        StopFlushTimer();
        _flushTimer = new System.Threading.Timer(
            _ => FlushPendingResults(generation, stopwatch),
            null,
            FlushIntervalMilliseconds,
            FlushIntervalMilliseconds);
    }

    private void StopFlushTimer()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref _searchGeneration);
            StopFlushTimer();
            _searchCts?.Cancel();
        }

        base.Dispose(disposing);
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
