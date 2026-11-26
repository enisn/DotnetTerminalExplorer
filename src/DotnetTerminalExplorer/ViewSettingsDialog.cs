using DotnetTerminalExplorer.Core;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class ViewSettingsDialog : Dialog
{
    private readonly Action<int>? _adjustPaneWidth;
    private readonly Action? _resetPaneWidth;
    private readonly Func<string>? _describePaneWidth;
    private bool _suppressFieldDefaultUpdate;

    public OptionSelector SortFieldSelector { get; }

    public OptionSelector SortDirectionSelector { get; }

    public CheckBox ShowSizesCheck { get; }

    public Label PaneWidthLabel { get; }

    public bool Applied { get; private set; }

    public FileTreeSortOptions SelectedSortOptions { get; private set; }

    public bool SelectedShowSizes { get; private set; }

    public ViewSettingsDialog(
        FileTreeSortOptions currentSort,
        bool currentShowSizes,
        Action<int>? adjustPaneWidth = null,
        Action? resetPaneWidth = null,
        Func<string>? describePaneWidth = null,
        int ownerWidth = 80,
        int ownerHeight = 24)
    {
        ArgumentNullException.ThrowIfNull(currentSort);

        _adjustPaneWidth = adjustPaneWidth;
        _resetPaneWidth = resetPaneWidth;
        _describePaneWidth = describePaneWidth;
        SelectedSortOptions = currentSort;
        SelectedShowSizes = currentShowSizes;

        var dialogWidth = ownerWidth > 0 ? Math.Clamp(ownerWidth - 6, 54, 68) : 64;
        var dialogHeight = ownerHeight > 0 ? Math.Clamp(ownerHeight - 4, 17, 20) : 19;

        Title = "View & Sort Settings";
        Width = dialogWidth;
        Height = dialogHeight;
        SetScheme(TuiSchemes.InputScheme);

        var sortFieldLabel = new Label
        {
            Text = "Sort By:",
            X = 1,
            Y = 0,
        };

        SortFieldSelector = new OptionSelector
        {
            X = 1,
            Y = 1,
            Orientation = Orientation.Horizontal,
            Labels = ["Name", "Modified Date", "Size"],
            Value = FieldToIndex(currentSort.Field),
        };

        var sortDirLabel = new Label
        {
            Text = "Order:",
            X = 1,
            Y = 3,
        };

        SortDirectionSelector = new OptionSelector
        {
            X = 1,
            Y = 4,
            Orientation = Orientation.Horizontal,
            Labels = ["Ascending (A→Z / Oldest / Smallest)", "Descending (Z→A / Newest / Largest)"],
            Value = currentSort.Direction == FileTreeSortDirection.Ascending ? 0 : 1,
        };

        SortFieldSelector.ValueChanged += (_, _) =>
        {
            if (_suppressFieldDefaultUpdate)
            {
                return;
            }

            var field = IndexToField(SortFieldSelector.Value ?? 0);
            var defaultDirection = FileTreeSortOptions.GetDefaultDirection(field);
            SortDirectionSelector.Value = defaultDirection == FileTreeSortDirection.Ascending ? 0 : 1;

            if (field == FileTreeSortField.Size && ShowSizesCheck is not null)
            {
                ShowSizesCheck.Value = CheckState.Checked;
            }
        };

        ShowSizesCheck = new CheckBox
        {
            Text = "Show folder & file sizes (lazy background)",
            Value = currentShowSizes ? CheckState.Checked : CheckState.UnChecked,
            X = 1,
            Y = 6,
        };

        var lazyHintLabel = new Label
        {
            Text = "List always loads first; sizes calculate lazily.",
            X = 3,
            Y = 7,
            Width = Dim.Fill(1),
        };

        PaneWidthLabel = new Label
        {
            Text = FormatPaneWidthText(),
            X = 1,
            Y = 9,
            Width = Dim.Fill(1),
        };

        var shrinkButton = new Button
        {
            Text = "Shrink -4",
            X = 1,
            Y = 10,
            Enabled = _adjustPaneWidth is not null,
        };
        shrinkButton.Accepting += (_, e) =>
        {
            _adjustPaneWidth?.Invoke(-4);
            RefreshPaneWidthLabel();
            e.Handled = true;
        };

        var expandButton = new Button
        {
            Text = "Expand +4",
            X = Pos.Right(shrinkButton) + 1,
            Y = 10,
            Enabled = _adjustPaneWidth is not null,
        };
        expandButton.Accepting += (_, e) =>
        {
            _adjustPaneWidth?.Invoke(4);
            RefreshPaneWidthLabel();
            e.Handled = true;
        };

        var resetWidthButton = new Button
        {
            Text = "Auto Width",
            X = Pos.Right(expandButton) + 1,
            Y = 10,
            Enabled = _resetPaneWidth is not null,
        };
        resetWidthButton.Accepting += (_, e) =>
        {
            _resetPaneWidth?.Invoke();
            RefreshPaneWidthLabel();
            e.Handled = true;
        };

        var applyButton = new Button
        {
            Text = "Apply",
            X = 1,
            Y = Pos.AnchorEnd(1),
            IsDefault = true,
        };
        applyButton.Accepting += (_, e) =>
        {
            ApplyAndClose();
            e.Handled = true;
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            X = Pos.AnchorEnd(11),
            Y = Pos.AnchorEnd(1),
        };
        cancelButton.Accepting += (_, e) =>
        {
            Applied = false;
            RequestStop();
            e.Handled = true;
        };

        Add(
            sortFieldLabel,
            SortFieldSelector,
            sortDirLabel,
            SortDirectionSelector,
            ShowSizesCheck,
            lazyHintLabel,
            PaneWidthLabel,
            shrinkButton,
            expandButton,
            resetWidthButton,
            applyButton,
            cancelButton);

        KeyDown += (_, keyEvent) =>
        {
            if (keyEvent == Key.Esc)
            {
                Applied = false;
                RequestStop();
                keyEvent.Handled = true;
            }
        };

        _suppressFieldDefaultUpdate = false;
    }

    public void ApplyAndClose()
    {
        var field = IndexToField(SortFieldSelector.Value ?? 0);
        var direction = (SortDirectionSelector.Value ?? 0) == 0
            ? FileTreeSortDirection.Ascending
            : FileTreeSortDirection.Descending;

        SelectedSortOptions = new FileTreeSortOptions(field, direction);
        SelectedShowSizes = ShowSizesCheck.Value == CheckState.Checked;
        Applied = true;
        RequestStop();
    }

    private void RefreshPaneWidthLabel()
    {
        PaneWidthLabel.Text = FormatPaneWidthText();
    }

    private string FormatPaneWidthText() =>
        _describePaneWidth is null
            ? "Left Panel Width:"
            : $"Left Panel Width: {_describePaneWidth()}";

    private static int FieldToIndex(FileTreeSortField field) =>
        field switch
        {
            FileTreeSortField.Name => 0,
            FileTreeSortField.Modified => 1,
            FileTreeSortField.Size => 2,
            _ => 0,
        };

    private static FileTreeSortField IndexToField(int index) =>
        index switch
        {
            1 => FileTreeSortField.Modified,
            2 => FileTreeSortField.Size,
            _ => FileTreeSortField.Name,
        };
}
