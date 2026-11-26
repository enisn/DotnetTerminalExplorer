using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class CommandPaletteDialog : Dialog
{
    private readonly IReadOnlyList<CommandPaletteItem> _allCommands;
    private readonly List<CommandPaletteItem> _filteredCommands = [];
    private readonly ObservableCollection<string> _displayItems = [];
    private readonly string _defaultStatusText;
    private readonly int _rowWidth;

    public TextField FilterInput { get; }

    public ListView CommandsListView { get; }

    public Label StatusLabel { get; }

    public Button RunButton { get; }

    public CommandPaletteItem? SelectedCommand { get; private set; }

    public IReadOnlyList<CommandPaletteItem> FilteredCommands => _filteredCommands;

    public CommandPaletteDialog(
        IReadOnlyList<CommandPaletteItem> commands,
        string? contextDescription = null,
        int ownerWidth = 80,
        int ownerHeight = 24)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _allCommands = commands;
        _defaultStatusText = string.IsNullOrWhiteSpace(contextDescription)
            ? "Type to filter, Up/Down to select, Enter to run, Esc to close."
            : $"{contextDescription} | Up/Down + Enter to run, Esc to close.";

        var dialogWidth = ownerWidth > 0 ? Math.Clamp(ownerWidth - 4, 52, 78) : 76;
        var dialogHeight = ownerHeight > 0 ? Math.Clamp(ownerHeight - 2, 14, 24) : 22;
        _rowWidth = Math.Max(44, dialogWidth - 6);

        Title = "Command Menu (Ctrl+P / :)";
        Width = dialogWidth;
        Height = dialogHeight;
        SetScheme(TuiSchemes.InputScheme);

        var filterLabel = new Label
        {
            Text = "Filter:",
            X = 1,
            Y = 0,
            Width = 7,
        };

        FilterInput = new TextField
        {
            X = Pos.Right(filterLabel) + 1,
            Y = 0,
            Width = Dim.Fill(1),
        };

        CommandsListView = new ListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
        };
        CommandsListView.SetSource(_displayItems);

        StatusLabel = new Label
        {
            Text = _defaultStatusText,
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(1),
        };

        RunButton = new Button
        {
            Text = "Run",
            X = 1,
            Y = Pos.AnchorEnd(1),
            IsDefault = true,
        };
        RunButton.Accepting += (_, e) =>
        {
            TryAcceptSelected();
            e.Handled = true;
        };

        var closeButton = new Button
        {
            Text = "Close",
            X = Pos.AnchorEnd(10),
            Y = Pos.AnchorEnd(1),
        };
        closeButton.Accepting += (_, e) =>
        {
            SelectedCommand = null;
            RequestStop();
            e.Handled = true;
        };

        Add(filterLabel, FilterInput, CommandsListView, StatusLabel, RunButton, closeButton);

        FilterInput.TextChanged += (_, _) => ApplyFilter(FilterInput.Text);

        FilterInput.KeyDown += (_, keyEvent) =>
        {
            if (keyEvent == Key.CursorDown)
            {
                MoveSelection(1);
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.CursorUp)
            {
                MoveSelection(-1);
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.PageDown)
            {
                CommandsListView.MovePageDown();
                UpdateStatusForCurrentSelection();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.PageUp)
            {
                CommandsListView.MovePageUp();
                UpdateStatusForCurrentSelection();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Enter)
            {
                TryAcceptSelected();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Esc)
            {
                SelectedCommand = null;
                RequestStop();
                keyEvent.Handled = true;
            }
        };

        CommandsListView.KeyDown += (_, keyEvent) =>
        {
            if (keyEvent == Key.Enter)
            {
                TryAcceptSelected();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Esc)
            {
                SelectedCommand = null;
                RequestStop();
                keyEvent.Handled = true;
            }
        };

        CommandsListView.Accepting += (_, e) =>
        {
            TryAcceptSelected();
            e.Handled = true;
        };

        CommandsListView.ValueChanged += (_, _) => UpdateStatusForCurrentSelection();

        ApplyFilter(string.Empty);
        Initialized += (_, _) => FilterInput.SetFocus();
    }

    public void ApplyFilter(string query)
    {
        _filteredCommands.Clear();
        _displayItems.Clear();

        foreach (var item in _allCommands)
        {
            if (item.MatchesFilter(query))
            {
                _filteredCommands.Add(item);
                _displayItems.Add(item.FormatRow(_rowWidth));
            }
        }

        if (_filteredCommands.Count > 0)
        {
            // Prefer focusing the first enabled command when filtering.
            var firstEnabledIndex = _filteredCommands.FindIndex(static c => c.IsEnabled);
            CommandsListView.SelectedItem = firstEnabledIndex >= 0 ? firstEnabledIndex : 0;
            CommandsListView.EnsureSelectedItemVisible();
        }
        else
        {
            CommandsListView.SelectedItem = null;
        }

        UpdateStatusForCurrentSelection();
    }

    public bool TryAcceptSelected()
    {
        if (CommandsListView.SelectedItem is not int index ||
            index < 0 ||
            index >= _filteredCommands.Count)
        {
            StatusLabel.Text = "No matching command.";
            return false;
        }

        var chosen = _filteredCommands[index];
        if (!chosen.IsEnabled)
        {
            StatusLabel.Text = chosen.DisabledReason ?? $"'{chosen.Title}' is not available right now.";
            return false;
        }

        SelectedCommand = chosen;
        RequestStop();
        return true;
    }

    private void MoveSelection(int delta)
    {
        if (_filteredCommands.Count == 0)
        {
            return;
        }

        var current = CommandsListView.SelectedItem ?? 0;
        var next = Math.Clamp(current + delta, 0, _filteredCommands.Count - 1);
        CommandsListView.SelectedItem = next;
        CommandsListView.EnsureSelectedItemVisible();
        UpdateStatusForCurrentSelection();
    }

    private void UpdateStatusForCurrentSelection()
    {
        if (_filteredCommands.Count == 0)
        {
            StatusLabel.Text = "No commands match the filter.";
            return;
        }

        if (CommandsListView.SelectedItem is int index &&
            index >= 0 &&
            index < _filteredCommands.Count)
        {
            var current = _filteredCommands[index];
            if (!current.IsEnabled && !string.IsNullOrEmpty(current.DisabledReason))
            {
                StatusLabel.Text = $"Unavailable: {current.DisabledReason}";
                return;
            }
        }

        StatusLabel.Text = _defaultStatusText;
    }
}
