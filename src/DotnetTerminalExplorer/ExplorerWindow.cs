#pragma warning disable CS0618 // The preview/editor intentionally uses Terminal.Gui TextView.

using System.Drawing;
using DotnetTerminalExplorer.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace DotnetTerminalExplorer;

internal sealed class ExplorerWindow : Window
{
    private static readonly TuiAttribute PreviewContentAttribute =
        new(ColorName16.White, ColorName16.Black);

    private static readonly TuiAttribute PreviewSelectionAttribute =
        new(ColorName16.Black, ColorName16.White);

    private static readonly Scheme PreviewColorScheme = new()
    {
        Normal = PreviewContentAttribute,
        HotNormal = PreviewContentAttribute,
        Focus = PreviewContentAttribute,
        HotFocus = PreviewContentAttribute,
        Active = PreviewSelectionAttribute,
        HotActive = PreviewSelectionAttribute,
        Highlight = PreviewSelectionAttribute,
        Editable = PreviewContentAttribute,
        ReadOnly = PreviewContentAttribute,
        Disabled = PreviewContentAttribute,
    };

    public const int DefaultMinLeftPaneWidth = 24;
    public const int DefaultMaxLeftPaneWidth = 48;
    public const int MinLeftPaneWidth = 18;
    public const int MinRightPaneWidth = 20;

    private readonly IFileTreeService _fileTreeService;
    private readonly IDefaultFileLauncher _launcher;
    private readonly ITextPreviewService _previewService;
    private readonly IFileMutationService _mutationService;
    private readonly ISearchService _searchService;
    private readonly IEntrySizeService _sizeService;
    private readonly bool _ownsSizeService;
    private readonly Func<FileSystemEntry, bool> _confirmDelete;
    private readonly Func<bool> _confirmExit;
    private readonly Action _showHelp;
    private readonly Action _requestStop;
    private readonly IApplication? _application;

    private int? _customLeftPaneWidth;
    private FileSystemEntry? _loadedEntry;
    private string _savedContent = string.Empty;
    private FileSystemEntry? _renamingEntry;
    private FileSystemEntry? _creatingInDirectory;
    private int _previewLoadVersion;
    private TextPreviewKind? _previewKind;
    private bool _advancingLoadMore;
    private bool _focusEditorOnLoaded;
    private FileTreeSortOptions _sortOptions = FileTreeSortOptions.Default;
    private bool _showSizes;
    private bool _suppressSelectionReload;
    private bool _disposed;
    private readonly HashSet<string> _forcedPreviewPaths = new(StringComparer.Ordinal);

    public ExplorerWindow(
        IFileTreeService fileTree,
        ITextPreviewService previewService,
        IDefaultFileLauncher launcher,
        Action requestStop,
        IFileMutationService? mutationService = null,
        Func<FileSystemEntry, bool>? confirmDelete = null,
        Action? showHelp = null,
        IApplication? application = null,
        ISearchService? searchService = null,
        Func<bool>? confirmExit = null,
        IEntrySizeService? sizeService = null)
    {
        ArgumentNullException.ThrowIfNull(fileTree);
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(requestStop);

        _fileTreeService = fileTree;
        _previewService = previewService;
        _launcher = launcher;
        _requestStop = requestStop;
        _mutationService = mutationService ?? new FileMutationService();
        _searchService = searchService ?? new FastSearchEngine();
        _ownsSizeService = sizeService is null;
        _sizeService = sizeService ?? new EntrySizeService();
        _confirmDelete = confirmDelete ?? ConfirmDeleteViaMessageBox;
        _confirmExit = confirmExit ?? ConfirmExitViaMessageBox;
        _showHelp = showHelp ?? ShowHelpViaDialog;
        _application = application;

        Title = ProductInfo.Name;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        StatusBar = CreateStatusBar();
        FileTreePane = new FrameView
        {
            Title = "Files",
            X = 0,
            Y = 0,
            Width = Dim.Func(_ => GetCalculatedLeftPaneWidth()),
            Height = Dim.Fill(StatusBar),
        };
        PreviewPane = new FrameView
        {
            Title = "Preview",
            X = Pos.Right(FileTreePane),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(StatusBar),
        };
        TreeBuilder = new FileSystemTreeBuilder(fileTree)
        {
            SizeProvider = GetEntrySizeForSorting,
        };
        TreeBuilder.DirectoryPageLoaded += OnDirectoryPageLoaded;
        _sizeService.SizesUpdated += OnSizesUpdated;
        FileTree = new ExplorerTreeView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            MultiSelect = false,
            TreeBuilder = TreeBuilder,
            AspectGetter = FormatTreeEntry,
            InterceptKeyDown = HandleTreePreKeyDown,
        };
        RebindRecursiveExpandToSingleLevel();
        RenameInput = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Visible = false,
        };
        CreateInput = new TextField
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Visible = false,
        };
        Preview = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = true,
            Text = TextPreview.ForDirectory().Text,
        };
        Preview.SetScheme(PreviewColorScheme);
        RenameInput.SetScheme(TuiSchemes.InputScheme);
        CreateInput.SetScheme(TuiSchemes.InputScheme);

        ImagePreview = new ImagePreviewView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
            UiInvoker = _application is null ? null : _application.Invoke,
        };

        FindBar = new EditorFindBar(Preview)
        {
            X = 0,
            Visible = false,
        };
        FindBar.OnClose = () => Preview.SetFocus();

        FileTree.SelectionChanged += (_, eventArgs) =>
        {
            if (_suppressSelectionReload)
            {
                return;
            }

            ShowSelection(eventArgs.NewValue);
        };
        Preview.ContentsChanged += (_, _) => UpdatePreviewTitle();

        // Ctrl+H must open the replace bar even though TextView binds it (and terminals
        // that encode Ctrl+H as 0x08 deliver it as Ctrl+Backspace) to word-delete.
        // Ctrl+P opens the universal Command Menu from anywhere, including inside the editor.
        // A KeyDown subscriber runs before the built-in key bindings, so intercept here.
        Preview.KeyDown += (sender, keyEvent) =>
        {
            if (EditorFindBar.IsReplaceShortcut(keyEvent))
            {
                TriggerContextAwareReplace();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.P.WithCtrl)
            {
                OpenCommandPalette();
                keyEvent.Handled = true;
            }
        };

        KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.P.WithCtrl)
            {
                OpenCommandPalette();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.CursorLeft.WithAlt || keyEvent == new Key('[').WithAlt)
            {
                ShrinkLeftPane();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.CursorRight.WithAlt || keyEvent == new Key(']').WithAlt)
            {
                ExpandLeftPane();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.F.WithCtrl || keyEvent == Key.F3)
            {
                if (FindBar.Visible)
                {
                    FindBar.NextMatch();
                }
                else
                {
                    TriggerContextAwareSearch();
                }
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.F3.WithShift)
            {
                if (FindBar.Visible)
                {
                    FindBar.PreviousMatch();
                }
                else
                {
                    TriggerContextAwareSearch();
                }
                keyEvent.Handled = true;
            }
            else if (EditorFindBar.IsReplaceShortcut(keyEvent))
            {
                TriggerContextAwareReplace();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.F.WithCtrl.WithShift)
            {
                OpenWorkspaceSearch();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.F5)
            {
                ReloadSelected();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.F2)
            {
                StartRename();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Delete)
            {
                DeleteSelected();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.F8)
            {
                EditSelected();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.L.WithCtrl)
            {
                LoadSelected();
                keyEvent.Handled = true;
            }
        };

        RenameInput.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.Enter)
            {
                CommitRename();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Esc)
            {
                CancelRename();
                keyEvent.Handled = true;
            }
        };

        CreateInput.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.Enter)
            {
                CommitCreate();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Esc)
            {
                CancelCreate();
                keyEvent.Handled = true;
            }
        };

        FileTree.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.Backspace)
            {
                DeleteSelected();
                keyEvent.Handled = true;
            }
            else if (keyEvent == Key.Enter)
            {
                var selected = FileTree.SelectedObject;
                if (selected is { Kind: FileSystemEntryKind.File })
                {
                    if (_previewKind == TextPreviewKind.Content && Preview.Visible && !Preview.ReadOnly)
                    {
                        Preview.SetFocus();
                        keyEvent.Handled = true;
                    }
                    else if (_previewKind is null && _loadedEntry == selected)
                    {
                        _focusEditorOnLoaded = true;
                        keyEvent.Handled = true;
                    }
                }
            }
            else if (keyEvent == Key.Esc)
            {
                if (IsTreeFiltered)
                {
                    ClearTreeFilter();
                }
                else
                {
                    RequestExit();
                }
                keyEvent.Handled = true;
            }
        };

        Preview.KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.Esc)
            {
                FileTree.SetFocus();
                keyEvent.Handled = true;
            }
        };

        FileTreePane.Add(FileTree, RenameInput, CreateInput);
        PreviewPane.Add(Preview, ImagePreview, FindBar);
        Add(FileTreePane, PreviewPane, StatusBar);

        FileTree.AddObject(fileTree.Root);
        FileTree.GoToFirst();
        FileTree.Expand(fileTree.Root);
        FileTree.SetFocus();

        Initialized += (_, _) => FileTree.SetFocus();
    }

    internal FileSystemTreeBuilder TreeBuilder { get; }

    internal FrameView FileTreePane { get; }

    internal FrameView PreviewPane { get; }

    internal TreeView<FileSystemEntry> FileTree { get; }

    internal TextView Preview { get; }

    internal ImagePreviewView ImagePreview { get; }

    internal EditorFindBar FindBar { get; }

    internal TextField RenameInput { get; }

    internal TextField CreateInput { get; }

    internal StatusBar StatusBar { get; }

    internal Shortcut HelpShortcut { get; private set; } = null!;

    internal Shortcut MenuShortcut { get; private set; } = null!;

    internal Shortcut SearchShortcut { get; private set; } = null!;

    internal Shortcut ClearFilterShortcut { get; private set; } = null!;

    internal Shortcut ReloadShortcut { get; private set; } = null!;

    internal Shortcut SaveShortcut { get; private set; } = null!;

    internal Shortcut NewFileShortcut { get; private set; } = null!;

    internal Shortcut RenameShortcut { get; private set; } = null!;

    internal Shortcut DeleteShortcut { get; private set; } = null!;

    internal Shortcut EditShortcut { get; private set; } = null!;

    internal Shortcut LoadShortcut { get; private set; } = null!;

    internal Shortcut QuitShortcut { get; private set; } = null!;

    public int? CustomLeftPaneWidth => _customLeftPaneWidth;

    public int CalculatedLeftPaneWidth => GetCalculatedLeftPaneWidth();

    public FileTreeSortOptions SortOptions => _sortOptions;

    public bool ShowSizes => _showSizes;

    public bool IsDirty =>
        _loadedEntry is { Kind: FileSystemEntryKind.File }
        && _previewService is ITextFileService
        && !Preview.ReadOnly
        && Preview.Visible
        && Preview.Text != _savedContent;

    public FileSystemEntry? LoadedEntry => _loadedEntry;

    public int GetCalculatedLeftPaneWidth()
    {
        var totalWidth = Viewport.Width > 0 ? Viewport.Width : 80;
        var minW = Math.Min(MinLeftPaneWidth, Math.Max(10, totalWidth / 2));
        var maxW = Math.Max(minW, totalWidth - MinRightPaneWidth);

        if (_customLeftPaneWidth.HasValue)
        {
            return Math.Clamp(_customLeftPaneWidth.Value, minW, maxW);
        }

        var defaultWidth = (int)Math.Round(totalWidth * 0.35);
        return Math.Clamp(defaultWidth, DefaultMinLeftPaneWidth, Math.Min(DefaultMaxLeftPaneWidth, maxW));
    }

    public void ShrinkLeftPane(int amount = 4)
    {
        var current = GetCalculatedLeftPaneWidth();
        var totalWidth = Viewport.Width > 0 ? Viewport.Width : 80;
        var minW = Math.Min(MinLeftPaneWidth, Math.Max(10, totalWidth / 2));
        _customLeftPaneWidth = Math.Max(minW, current - amount);
        FileTreePane.SetNeedsLayout();
        PreviewPane.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    public void ExpandLeftPane(int amount = 4)
    {
        var current = GetCalculatedLeftPaneWidth();
        var totalWidth = Viewport.Width > 0 ? Viewport.Width : 80;
        var minW = Math.Min(MinLeftPaneWidth, Math.Max(10, totalWidth / 2));
        var maxW = Math.Max(minW, totalWidth - MinRightPaneWidth);
        _customLeftPaneWidth = Math.Min(maxW, current + amount);
        FileTreePane.SetNeedsLayout();
        PreviewPane.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    public void ResetLeftPaneWidth()
    {
        _customLeftPaneWidth = null;
        FileTreePane.SetNeedsLayout();
        PreviewPane.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    public void SetLeftPaneWidth(int width)
    {
        var totalWidth = Viewport.Width > 0 ? Viewport.Width : 80;
        var minW = Math.Min(MinLeftPaneWidth, Math.Max(10, totalWidth / 2));
        var maxW = Math.Max(minW, totalWidth - MinRightPaneWidth);
        _customLeftPaneWidth = Math.Clamp(width, minW, maxW);
        FileTreePane.SetNeedsLayout();
        PreviewPane.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    public void TogglePaneFocus()
    {
        if (FileTree.HasFocus)
        {
            if (Preview.Visible)
            {
                Preview.SetFocus();
            }
            else if (ImagePreview.Visible)
            {
                ImagePreview.SetFocus();
            }
        }
        else
        {
            FileTree.SetFocus();
        }
    }

    public void SetSortOptions(FileTreeSortOptions sortOptions, bool enableSizesWhenSortingBySize = true)
    {
        ArgumentNullException.ThrowIfNull(sortOptions);

        if (sortOptions.Field == FileTreeSortField.Size && enableSizesWhenSortingBySize && !_showSizes)
        {
            _showSizes = true;
        }

        _sortOptions = sortOptions;
        var previousSelection = FileTree.SelectedObject;

        TreeBuilder.SetSortOptions(sortOptions);
        _suppressSelectionReload = true;
        try
        {
            FileTree.RebuildTree();
            FileTree.Expand(_fileTreeService.Root);
            if (previousSelection is not null)
            {
                FileTree.SelectedObject = previousSelection;
            }
        }
        finally
        {
            _suppressSelectionReload = false;
        }

        UpdateFileTreeTitle();
        FileTree.SetNeedsDraw();

        if (_showSizes)
        {
            RequestSizesForLoadedEntries();
        }
    }

    public void SetShowSizes(bool enabled)
    {
        if (_showSizes == enabled)
        {
            return;
        }

        _showSizes = enabled;

        if (enabled)
        {
            // Render the list immediately first; sizes calculate lazily in background.
            UpdateFileTreeTitle();
            if (_loadedEntry is { IsDirectory: true } selectedDir)
            {
                ShowPreview(FormatDirectoryPreview(selectedDir));
            }
            FileTree.SetNeedsDraw();
            RequestSizesForLoadedEntries();
        }
        else
        {
            _sizeService.CancelPending();
            UpdateFileTreeTitle();
            if (_loadedEntry is { IsDirectory: true })
            {
                ShowPreview(TextPreview.ForDirectory().Text);
            }
            FileTree.SetNeedsDraw();
        }
    }

    public void RecalculateSizes()
    {
        _sizeService.Clear();
        _showSizes = true;
        UpdateFileTreeTitle();
        if (_loadedEntry is { IsDirectory: true } selectedDir)
        {
            ShowPreview(FormatDirectoryPreview(selectedDir));
        }
        FileTree.SetNeedsDraw();
        RequestSizesForLoadedEntries();
    }

    public void OpenCommandPalette()
    {
        if (_application is null)
        {
            return;
        }

        var selected = FileTree.SelectedObject;
        var context = selected is null
            ? null
            : selected == _fileTreeService.Root
                ? $"Selected: {selected.Name} (root)"
                : $"Selected: {selected.Name}";

        var items = BuildCommandPaletteItems();
        using var dialog = new CommandPaletteDialog(
            items,
            context,
            Viewport.Width,
            Viewport.Height);

        _application.Run(dialog);

        var chosen = dialog.SelectedCommand;
        if (chosen is not null)
        {
            chosen.Execute();
        }
    }

    public void OpenViewSettings()
    {
        if (_application is null)
        {
            return;
        }

        using var dialog = new ViewSettingsDialog(
            _sortOptions,
            _showSizes,
            adjustPaneWidth: delta =>
            {
                if (delta < 0)
                {
                    ShrinkLeftPane(-delta);
                }
                else
                {
                    ExpandLeftPane(delta);
                }
            },
            resetPaneWidth: ResetLeftPaneWidth,
            describePaneWidth: DescribePaneWidth,
            ownerWidth: Viewport.Width,
            ownerHeight: Viewport.Height);

        _application.Run(dialog);

        if (dialog.Applied)
        {
            var sortChanged = dialog.SelectedSortOptions != _sortOptions;
            if (sortChanged)
            {
                SetSortOptions(dialog.SelectedSortOptions, enableSizesWhenSortingBySize: false);
            }

            SetShowSizes(dialog.SelectedShowSizes);
        }
    }

    public void OpenResizePaneDialog()
    {
        if (_application is null)
        {
            return;
        }

        using var dialog = new ResizePaneDialog(
            getCurrentWidth: GetCalculatedLeftPaneWidth,
            getTotalWidth: () => Viewport.Width > 0 ? Viewport.Width : 80,
            isAutoWidth: () => !_customLeftPaneWidth.HasValue,
            shrink: ShrinkLeftPane,
            expand: ExpandLeftPane,
            setWidth: SetLeftPaneWidth,
            resetWidth: ResetLeftPaneWidth);

        _application.Run(dialog);
    }

    internal IReadOnlyList<CommandPaletteItem> BuildCommandPaletteItems()
    {
        var selected = FileTree.SelectedObject;
        var hasNonRootSelection = selected is not null
            && selected != _fileTreeService.Root
            && selected.Kind != FileSystemEntryKind.LoadMore;
        var isFile = _loadedEntry is { Kind: FileSystemEntryKind.File };
        var isEditableText = isFile && _previewService is ITextFileService && !Preview.ReadOnly && Preview.Visible;
        var isLoadablePreview = isFile && (_previewKind == TextPreviewKind.TooLarge || _previewKind == TextPreviewKind.Binary);
        var canFindInFile = isFile && Preview.Visible;

        string? ActiveBadge(FileTreeSortField field, FileTreeSortDirection dir) =>
            _sortOptions.Field == field && _sortOptions.Direction == dir ? "[Active]" : null;

        return
        [
            // File operations
            new CommandPaletteItem(
                "file.delete",
                "File",
                "Delete Selected",
                "Del / Backspace",
                hasNonRootSelection,
                DeleteSelected,
                DisabledReason: "Select a file or folder (not the root directory)."),
            new CommandPaletteItem(
                "file.rename",
                "File",
                "Rename Selected",
                "F2",
                hasNonRootSelection,
                StartRename,
                DisabledReason: "Select a file or folder (not the root directory)."),
            new CommandPaletteItem(
                "file.new",
                "File",
                "New File",
                "Ctrl+N",
                true,
                StartCreate),
            new CommandPaletteItem(
                "file.save",
                "File",
                "Save File",
                "Ctrl+S",
                isEditableText,
                SaveSelected,
                DisabledReason: "Select an editable text file first."),
            new CommandPaletteItem(
                "file.reload",
                "File",
                "Reload Selected",
                "F5",
                true,
                ReloadSelected),
            new CommandPaletteItem(
                "file.load",
                "File",
                "Load Large / Binary as Text",
                "Ctrl+L",
                isLoadablePreview,
                LoadSelected,
                DisabledReason: "Available when a large (>2 MB) or binary file is selected."),
            new CommandPaletteItem(
                "file.open-external",
                "File",
                "Open with External App",
                "F8",
                isFile,
                EditSelected,
                DisabledReason: "Select a file first."),

            // View & Folder Sizes (opt-in, lazy)
            new CommandPaletteItem(
                "view.toggle-sizes",
                "View",
                "Show Folder & File Sizes (Lazy)",
                "",
                true,
                () => SetShowSizes(!_showSizes),
                StateBadge: _showSizes ? "[ON]" : "[OFF]"),
            new CommandPaletteItem(
                "view.recalc-sizes",
                "View",
                "Recalculate Folder Sizes",
                "",
                _showSizes,
                RecalculateSizes,
                DisabledReason: "Enable 'Show Folder & File Sizes' first."),

            // Sorting
            new CommandPaletteItem(
                "sort.name-asc",
                "Sort",
                "Name (A → Z)",
                "",
                true,
                () => SetSortOptions(new FileTreeSortOptions(FileTreeSortField.Name, FileTreeSortDirection.Ascending)),
                StateBadge: ActiveBadge(FileTreeSortField.Name, FileTreeSortDirection.Ascending)),
            new CommandPaletteItem(
                "sort.name-desc",
                "Sort",
                "Name (Z → A)",
                "",
                true,
                () => SetSortOptions(new FileTreeSortOptions(FileTreeSortField.Name, FileTreeSortDirection.Descending)),
                StateBadge: ActiveBadge(FileTreeSortField.Name, FileTreeSortDirection.Descending)),
            new CommandPaletteItem(
                "sort.modified-desc",
                "Sort",
                "Modified Date (Newest First)",
                "",
                true,
                () => SetSortOptions(new FileTreeSortOptions(FileTreeSortField.Modified, FileTreeSortDirection.Descending)),
                StateBadge: ActiveBadge(FileTreeSortField.Modified, FileTreeSortDirection.Descending)),
            new CommandPaletteItem(
                "sort.modified-asc",
                "Sort",
                "Modified Date (Oldest First)",
                "",
                true,
                () => SetSortOptions(new FileTreeSortOptions(FileTreeSortField.Modified, FileTreeSortDirection.Ascending)),
                StateBadge: ActiveBadge(FileTreeSortField.Modified, FileTreeSortDirection.Ascending)),
            new CommandPaletteItem(
                "sort.size-desc",
                "Sort",
                "Size (Largest First)",
                "",
                true,
                () => SetSortOptions(new FileTreeSortOptions(FileTreeSortField.Size, FileTreeSortDirection.Descending)),
                StateBadge: ActiveBadge(FileTreeSortField.Size, FileTreeSortDirection.Descending)),
            new CommandPaletteItem(
                "sort.size-asc",
                "Sort",
                "Size (Smallest First)",
                "",
                true,
                () => SetSortOptions(new FileTreeSortOptions(FileTreeSortField.Size, FileTreeSortDirection.Ascending)),
                StateBadge: ActiveBadge(FileTreeSortField.Size, FileTreeSortDirection.Ascending)),

            // Search & Filter
            new CommandPaletteItem(
                "search.workspace",
                "Search",
                "Workspace Search",
                "Ctrl+Shift+F",
                true,
                OpenWorkspaceSearch),
            new CommandPaletteItem(
                "search.find",
                "Search",
                "Find in Active File",
                "Ctrl+F",
                canFindInFile,
                () => FindBar.Open(),
                DisabledReason: "Select a previewable text file first."),
            new CommandPaletteItem(
                "search.replace",
                "Search",
                "Find & Replace in Active File",
                "Ctrl+H",
                canFindInFile,
                () => FindBar.Open(replaceMode: true),
                DisabledReason: "Select a previewable text file first."),
            new CommandPaletteItem(
                "search.clear-filter",
                "Search",
                "Clear Tree Filter",
                "Alt+X / Esc",
                IsTreeFiltered,
                ClearTreeFilter,
                DisabledReason: "No search filter is currently active."),

            // Layout & Navigation
            new CommandPaletteItem(
                "layout.resize",
                "Layout",
                "Resize Left Pane…",
                "< / >",
                true,
                OpenResizePaneDialog),
            new CommandPaletteItem(
                "layout.expand",
                "Layout",
                "Expand Left Pane (+8)",
                "Alt+Right / >",
                true,
                () => ExpandLeftPane(8)),
            new CommandPaletteItem(
                "layout.shrink",
                "Layout",
                "Shrink Left Pane (-8)",
                "Alt+Left / <",
                true,
                () => ShrinkLeftPane(8)),
            new CommandPaletteItem(
                "layout.reset",
                "Layout",
                "Reset Left Pane Width",
                "",
                true,
                ResetLeftPaneWidth),
            new CommandPaletteItem(
                "nav.switch-pane",
                "Navigation",
                "Switch Pane Focus",
                "Tab",
                true,
                TogglePaneFocus),

            // General
            new CommandPaletteItem(
                "general.settings",
                "General",
                "View & Sort Settings…",
                ",",
                true,
                OpenViewSettings),
            new CommandPaletteItem(
                "general.help",
                "General",
                "Keyboard Shortcuts & Help",
                "F1",
                true,
                ShowHelp),
            new CommandPaletteItem(
                "general.quit",
                "General",
                "Quit",
                "Esc",
                true,
                RequestExit),
        ];
    }

    private string DescribePaneWidth()
    {
        var current = GetCalculatedLeftPaneWidth();
        var total = Viewport.Width > 0 ? Viewport.Width : 80;
        var mode = _customLeftPaneWidth.HasValue ? "Custom" : "Auto";
        return $"{current}/{total} cols ({mode})";
    }

    internal string FormatTreeEntry(FileSystemEntry entry)
    {
        if (!_showSizes || entry.Kind == FileSystemEntryKind.LoadMore)
        {
            return entry.Name;
        }

        if (_sizeService.TryGetSize(entry.FullPath, out var info))
        {
            return info.State switch
            {
                EntrySizeState.SkippedLink => $"[    link] {entry.Name}",
                EntrySizeState.Ready => FormatReadySizeEntry(entry.Name, info),
                _ => $"[     ...] {entry.Name}",
            };
        }

        if (entry == _fileTreeService.Root && TryComputeRootSumFromLoadedChildren(out var rootInfo))
        {
            return FormatReadySizeEntry(entry.Name, rootInfo);
        }

        return entry == _fileTreeService.Root
            ? entry.Name
            : $"[     ...] {entry.Name}";
    }

    private static string FormatReadySizeEntry(string name, EntrySizeInfo info)
    {
        var compact = FileTypeClassifier.FormatCompactSize(info.Bytes);
        var badge = info.IsPartial ? "~" + compact : compact;
        return $"[{badge,8}] {name}";
    }

    private bool TryComputeRootSumFromLoadedChildren(out EntrySizeInfo result)
    {
        result = default;
        if (TreeBuilder.GetLoadMoreNode(_fileTreeService.Root) is not null)
        {
            return false;
        }

        var children = TreeBuilder.GetLoadedChildren(_fileTreeService.Root.FullPath);
        if (children.Count == 0)
        {
            result = EntrySizeInfo.Ready(0);
            return true;
        }

        long total = 0;
        var isPartial = false;
        foreach (var child in children)
        {
            if (!_sizeService.TryGetSize(child.FullPath, out var childInfo))
            {
                return false;
            }

            if (childInfo.State == EntrySizeState.Pending)
            {
                return false;
            }

            if (childInfo.State == EntrySizeState.Ready)
            {
                total += childInfo.Bytes;
                isPartial |= childInfo.IsPartial;
            }
        }

        result = EntrySizeInfo.Ready(total, isPartial);
        return true;
    }

    private string FormatDirectoryPreview(FileSystemEntry directory)
    {
        if (!_showSizes)
        {
            return TextPreview.ForDirectory().Text;
        }

        string sizeLine;
        if (_sizeService.TryGetSize(directory.FullPath, out var info))
        {
            sizeLine = info.State switch
            {
                EntrySizeState.SkippedLink => "Skipped (symbolic link)",
                EntrySizeState.Ready => info.IsPartial
                    ? $"{FileTypeClassifier.FormatFileSize(info.Bytes)} (partial — some entries inaccessible)"
                    : FileTypeClassifier.FormatFileSize(info.Bytes),
                _ => "Calculating in background...",
            };
        }
        else if (directory == _fileTreeService.Root && TryComputeRootSumFromLoadedChildren(out var rootInfo))
        {
            sizeLine = rootInfo.IsPartial
                ? $"{FileTypeClassifier.FormatFileSize(rootInfo.Bytes)} (partial — some entries inaccessible)"
                : FileTypeClassifier.FormatFileSize(rootInfo.Bytes);
        }
        else
        {
            sizeLine = "Calculating in background...";
        }

        return $"""
            {TextPreview.ForDirectory().Text}

            Directory: {directory.Name}
            Path:      {directory.FullPath}
            Size:      {sizeLine}
            """;
    }

    private long? GetEntrySizeForSorting(FileSystemEntry entry) =>
        _sizeService.TryGetKnownBytes(entry.FullPath);

    private void OnDirectoryPageLoaded(FileSystemEntry directory, IReadOnlyList<FileSystemEntry> loadedEntries)
    {
        if (!_showSizes || _disposed || loadedEntries.Count == 0)
        {
            return;
        }

        _sizeService.RequestSizes(loadedEntries);
        UpdateFileTreeTitle();
    }

    private void RequestSizesForLoadedEntries()
    {
        var loadedEntries = TreeBuilder.GetLoadedEntriesSnapshot();
        if (loadedEntries.Count > 0)
        {
            _sizeService.RequestSizes(loadedEntries);
            UpdateFileTreeTitle();
        }
    }

    private void OnSizesUpdated(IReadOnlyCollection<string> changedPaths)
    {
        if (_disposed || !_showSizes)
        {
            return;
        }

        void ApplyUpdate()
        {
            if (_disposed || !_showSizes)
            {
                return;
            }

            if (_sortOptions.Field == FileTreeSortField.Size &&
                TreeBuilder.ResortLoadedDirectories(changedPaths, out var affectedParents))
            {
                var previousSelection = FileTree.SelectedObject;
                _suppressSelectionReload = true;
                try
                {
                    foreach (var parent in affectedParents)
                    {
                        FileTree.RefreshObject(parent, startAtTop: false);
                    }

                    if (previousSelection is not null)
                    {
                        FileTree.SelectedObject = previousSelection;
                    }
                }
                finally
                {
                    _suppressSelectionReload = false;
                }
            }

            if (_loadedEntry is { IsDirectory: true } selectedDir)
            {
                ShowPreview(FormatDirectoryPreview(selectedDir));
            }

            UpdateFileTreeTitle();
            FileTree.SetNeedsDraw();
        }

        if (_application is null)
        {
            ApplyUpdate();
            return;
        }

        try
        {
            _application.Invoke(ApplyUpdate);
        }
        catch
        {
            // The application was shut down while updating; nothing to update.
        }
    }

    private void UpdateFileTreeTitle()
    {
        List<string>? tags = null;

        if (IsTreeFiltered)
        {
            tags ??= [];
            tags.Add("filtered");
        }

        if (_sortOptions != FileTreeSortOptions.Default)
        {
            tags ??= [];
            tags.Add(_sortOptions.FormatShortLabel());
        }

        if (_showSizes)
        {
            tags ??= [];
            tags.Add(_sizeService.IsCalculating ? "sizes…" : "sizes");
        }

        FileTreePane.Title = tags is null
            ? "Files"
            : $"Files ({string.Join(", ", tags)})";
    }

    private bool HandleTreePreKeyDown(Key keyEvent)
    {
        if (IsPlainRune(keyEvent, ':'))
        {
            OpenCommandPalette();
            return true;
        }

        if (IsPlainRune(keyEvent, ','))
        {
            OpenViewSettings();
            return true;
        }

        if (IsPlainRune(keyEvent, '<'))
        {
            ShrinkLeftPane();
            return true;
        }

        if (IsPlainRune(keyEvent, '>'))
        {
            ExpandLeftPane();
            return true;
        }

        return false;
    }

    private static bool IsPlainRune(Key keyEvent, char ch) =>
        !keyEvent.IsCtrl
        && !keyEvent.IsAlt
        && (keyEvent.AsRune.Value == ch
            || keyEvent.NoShift.AsRune.Value == ch
            || (uint)(keyEvent.KeyCode & KeyCode.CharMask) == ch);

    private sealed class ExplorerTreeView : TreeView<FileSystemEntry>
    {
        public Func<Key, bool>? InterceptKeyDown { get; set; }

        protected override bool OnKeyDown(Key key)
        {
            if (InterceptKeyDown?.Invoke(key) == true)
            {
                return true;
            }

            return base.OnKeyDown(key);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            TreeBuilder.DirectoryPageLoaded -= OnDirectoryPageLoaded;
            _sizeService.SizesUpdated -= OnSizesUpdated;
            _sizeService.CancelPending();
            if (_ownsSizeService)
            {
                _sizeService.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public void ShowHelp()
    {
        _showHelp();
    }

    private StatusBar CreateStatusBar()
    {
        HelpShortcut = new Shortcut(Key.F1, "Help", ShowHelp)
        {
            BindKeyToApplication = true,
        };
        MenuShortcut = new Shortcut(Key.P.WithCtrl, "Menu", OpenCommandPalette)
        {
            BindKeyToApplication = true,
        };
        SearchShortcut = new Shortcut(Key.F.WithCtrl, "Search", TriggerContextAwareSearch)
        {
            BindKeyToApplication = true,
        };
        ClearFilterShortcut = new Shortcut(Key.X.WithAlt, "Clear Filter", ClearTreeFilter)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        ReloadShortcut = new Shortcut(Key.F5, "Reload", ReloadSelected)
        {
            BindKeyToApplication = true,
        };
        SaveShortcut = new Shortcut(Key.S.WithCtrl, "Save", SaveSelected)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        NewFileShortcut = new Shortcut(Key.N.WithCtrl, "New", StartCreate)
        {
            BindKeyToApplication = true,
        };
        RenameShortcut = new Shortcut(Key.F2, "Rename", StartRename)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        DeleteShortcut = new Shortcut(Key.Delete, "Delete", DeleteSelected)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        EditShortcut = new Shortcut(Key.F8, "Edit Ext.", EditSelected)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        LoadShortcut = new Shortcut(Key.L.WithCtrl, "Load", LoadSelected)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        QuitShortcut = new Shortcut(Key.Esc, "Quit", RequestExit)
        {
            BindKeyToApplication = true,
        };

        return new StatusBar([
            HelpShortcut,
            MenuShortcut,
            SearchShortcut,
            ClearFilterShortcut,
            SaveShortcut,
            NewFileShortcut,
            QuitShortcut
        ]);
    }

    private void RebindRecursiveExpandToSingleLevel()
    {
        // Recursive expand-all over an arbitrary filesystem subtree is
        // unbounded; expand one level instead so huge trees stay responsive.
        var ctrlRight = Key.CursorRight.WithCtrl;
        var defaults = TreeView<FileSystemEntry>.DefaultKeyBindings;

        if (defaults.TryGetValue(Command.ExpandAll, out var expandAll))
        {
            defaults[Command.ExpandAll] = CopyBindingWithout(expandAll, ctrlRight);
        }

        if (defaults.TryGetValue(Command.Expand, out var expand))
        {
            defaults[Command.Expand] = CopyBindingWith(expand, ctrlRight);
        }
    }

    private static PlatformKeyBinding CopyBindingWithout(PlatformKeyBinding binding, Key key) =>
        new()
        {
            All = [.. (binding.All ?? []).Where(existing => existing != key)],
            Windows = [.. (binding.Windows ?? []).Where(existing => existing != key)],
            Linux = [.. (binding.Linux ?? []).Where(existing => existing != key)],
            Macos = [.. (binding.Macos ?? []).Where(existing => existing != key)],
        };

    private static PlatformKeyBinding CopyBindingWith(PlatformKeyBinding binding, Key key) =>
        new()
        {
            All = AddKeyIfMissing(binding.All ?? [], key),
            Windows = AddKeyIfMissing(binding.Windows ?? [], key),
            Linux = AddKeyIfMissing(binding.Linux ?? [], key),
            Macos = AddKeyIfMissing(binding.Macos ?? [], key),
        };

    private static Key[] AddKeyIfMissing(Key[] keys, Key key) =>
        keys.Contains(key) ? keys : [.. keys, key];

    private void ShowSelection(FileSystemEntry? entry) =>
        ShowSelection(
            entry,
            forceLoad: entry is not null && _forcedPreviewPaths.Contains(entry.FullPath));

    private void ShowSelection(FileSystemEntry? entry, bool forceLoad)
    {
        _loadedEntry = entry;
        _previewLoadVersion++;
        _previewKind = null;
        _focusEditorOnLoaded = false;
        FindBar.Reset();

        if (entry is { Kind: FileSystemEntryKind.LoadMore })
        {
            _savedContent = string.Empty;
            ImagePreview.Visible = false;
            ImagePreview.Clear();
            Preview.Visible = true;
            Preview.ReadOnly = true;
            ShowPreview("Loading more entries…");
            UpdatePreviewTitle();
            UpdateShortcutStates();
            LoadMoreEntries(entry);
            return;
        }

        if (entry is null || entry.IsDirectory)
        {
            _savedContent = string.Empty;
            ImagePreview.Visible = false;
            ImagePreview.Clear();
            Preview.Visible = true;
            Preview.ReadOnly = true;
            ShowPreview(entry is not null ? FormatDirectoryPreview(entry) : TextPreview.ForDirectory().Text);
        }
        else
        {
            _savedContent = string.Empty;
            ImagePreview.Visible = false;
            ImagePreview.Clear();
            Preview.Visible = true;
            Preview.ReadOnly = true;
            ShowPreview($"Loading '{entry.Name}'...");
            LoadPreview(entry, forceLoad, _previewLoadVersion);
        }

        UpdatePreviewTitle();
        UpdateShortcutStates();
        PrefetchNextPageIfNearEnd(entry);
    }

    private void PrefetchNextPageIfNearEnd(FileSystemEntry? entry)
    {
        if (entry is null || _advancingLoadMore ||
            !TreeBuilder.TryGetPrefetchParent(entry, out var directory) ||
            directory is null)
        {
            return;
        }

        _advancingLoadMore = true;
        try
        {
            if (TreeBuilder.Advance(directory))
            {
                FileTree.RefreshObject(directory, startAtTop: false);
            }
        }
        finally
        {
            _advancingLoadMore = false;
        }
    }

    private void LoadMoreEntries(FileSystemEntry loadMore)
    {
        if (_advancingLoadMore)
        {
            return;
        }

        _advancingLoadMore = true;
        try
        {
            if (TreeBuilder.TryAdvance(loadMore, out var parent) && parent is not null)
            {
                FileTree.RefreshObject(parent, startAtTop: false);
            }
        }
        finally
        {
            _advancingLoadMore = false;
        }
    }

    private void LoadPreview(FileSystemEntry entry, bool forceLoad, int version)
    {
        if (_application is null)
        {
            // No application instance is available (unit tests); load synchronously.
            ApplyPreview(entry, version, ReadPreviewSafely(entry, forceLoad));
            return;
        }

        _ = LoadPreviewAsync(_application, entry, forceLoad, version);
    }

    private async Task LoadPreviewAsync(IApplication application, FileSystemEntry entry, bool forceLoad, int version)
    {
        var preview = await Task.Run(() => ReadPreviewSafely(entry, forceLoad));
        try
        {
            application.Invoke(() => ApplyPreview(entry, version, preview));
        }
        catch
        {
            // The application was shut down while loading; nothing to update.
        }
    }

    private TextPreview ReadPreviewSafely(FileSystemEntry entry, bool forceLoad)
    {
        try
        {
            return _previewService.Read(entry, forceLoad);
        }
        catch (Exception exception)
        {
            return TextPreview.FromError($"Unable to preview '{entry.Name}': {exception.Message}");
        }
    }

    private void ApplyPreview(FileSystemEntry entry, int version, TextPreview preview)
    {
        if (version != _previewLoadVersion || _loadedEntry != entry)
        {
            return;
        }

        _previewKind = preview.Kind;

        if (preview.Kind == TextPreviewKind.Image)
        {
            _savedContent = string.Empty;
            Preview.Visible = false;
            ImagePreview.Visible = true;
            ImagePreview.SetImage(entry.FullPath, preview.Text);
        }
        else
        {
            ImagePreview.Visible = false;
            ImagePreview.Clear();
            Preview.Visible = true;
            _savedContent = preview.Kind == TextPreviewKind.Content ? preview.Text : string.Empty;
            Preview.ReadOnly = preview.Kind != TextPreviewKind.Content;
            ShowPreview(preview.Text);
        }

        if (_focusEditorOnLoaded)
        {
            _focusEditorOnLoaded = false;
            if (preview.Kind == TextPreviewKind.Content && Preview.Visible && !Preview.ReadOnly)
            {
                Preview.SetFocus();
            }
        }

        UpdatePreviewTitle();
        UpdateShortcutStates();
    }

    private void LoadSelected()
    {
        if (FileTree.SelectedObject is { Kind: FileSystemEntryKind.File } selectedFile)
        {
            _forcedPreviewPaths.Add(selectedFile.FullPath);
            ShowSelection(selectedFile, forceLoad: true);
        }
    }

    private void ReloadSelected()
    {
        ShowSelection(FileTree.SelectedObject);
    }

    public void SaveSelected()
    {
        if (_loadedEntry is not { Kind: FileSystemEntryKind.File } selectedFile ||
            _previewService is not ITextFileService fileService)
        {
            return;
        }

        var saveResult = fileService.Save(selectedFile, Preview.Text);
        if (saveResult.Success)
        {
            _savedContent = Preview.Text;
            _sizeService.InvalidatePathAndAncestors(selectedFile.FullPath, _fileTreeService.Root.FullPath);
            if (_showSizes)
            {
                RequestSizesForLoadedEntries();
            }
            UpdatePreviewTitle();
        }
        else
        {
            ShowPreview(saveResult.ErrorMessage ?? $"Unable to save '{selectedFile.Name}'.");
        }
    }

    public void StartCreate()
    {
        CancelRename();

        var target = FileTree.SelectedObject ?? _fileTreeService.Root;

        if (target.Kind == FileSystemEntryKind.LoadMore)
        {
            if (TreeBuilder.TryGetLoadMoreParent(target, out var loadMoreParent) &&
                loadMoreParent is not null)
            {
                target = loadMoreParent;
            }
            else
            {
                return;
            }
        }

        if (target.IsDirectory)
        {
            _creatingInDirectory = target;
        }
        else
        {
            var parentPath = Path.GetDirectoryName(target.FullPath);
            _creatingInDirectory = string.IsNullOrEmpty(parentPath)
                ? _fileTreeService.Root
                : new FileSystemEntry(parentPath, Path.GetFileName(parentPath), FileSystemEntryKind.Directory, IsReparsePoint: false);
        }

        CreateInput.Text = string.Empty;
        CreateInput.Visible = true;
        CreateInput.SetFocus();
    }

    public void CommitCreate()
    {
        if (_creatingInDirectory is null)
        {
            CancelCreate();
            return;
        }

        var targetDir = _creatingInDirectory;
        var createResult = _mutationService.CreateFile(targetDir, CreateInput.Text);
        if (createResult.Success && createResult.NewEntry is not null)
        {
            var newEntry = createResult.NewEntry;
            CreateInput.Visible = false;
            _creatingInDirectory = null;

            TreeBuilder.Invalidate(targetDir.FullPath);
            _sizeService.InvalidatePathAndAncestors(newEntry.FullPath, _fileTreeService.Root.FullPath);
            FileTree.RebuildTree();
            FileTree.SelectedObject = newEntry;
            ShowSelection(newEntry);
            if (_showSizes)
            {
                RequestSizesForLoadedEntries();
            }
            FileTree.SetFocus();
        }
        else
        {
            CreateInput.Visible = false;
            _creatingInDirectory = null;
            ShowPreview(createResult.ErrorMessage ?? "Create file failed.");
            FileTree.SetFocus();
        }
    }

    public void CancelCreate()
    {
        _creatingInDirectory = null;
        CreateInput.Visible = false;
        FileTree.SetFocus();
    }

    public void StartRename()
    {
        CancelCreate();

        var target = FileTree.SelectedObject;
        if (target is null || target == _fileTreeService.Root ||
            target.Kind == FileSystemEntryKind.LoadMore)
        {
            return;
        }

        _renamingEntry = target;
        RenameInput.Text = target.Name;
        RenameInput.Visible = true;
        RenameInput.SetFocus();
    }

    public void CommitRename()
    {
        if (_renamingEntry is null)
        {
            CancelRename();
            return;
        }

        var originalEntry = _renamingEntry;
        var renameResult = _mutationService.Rename(originalEntry, RenameInput.Text);
        if (renameResult.Success && renameResult.NewEntry is not null)
        {
            var updatedEntry = renameResult.NewEntry;
            if (_loadedEntry == originalEntry)
            {
                _loadedEntry = updatedEntry;
            }

            RenameInput.Visible = false;
            _renamingEntry = null;

            InvalidateParentCache(updatedEntry.FullPath);
            _sizeService.InvalidatePathAndAncestors(originalEntry.FullPath, _fileTreeService.Root.FullPath);
            _sizeService.InvalidatePathAndAncestors(updatedEntry.FullPath, _fileTreeService.Root.FullPath);
            FileTree.RebuildTree();
            FileTree.SelectedObject = updatedEntry;
            if (_showSizes)
            {
                RequestSizesForLoadedEntries();
            }
            UpdatePreviewTitle();
            UpdateShortcutStates();
            FileTree.SetFocus();
        }
        else
        {
            RenameInput.Visible = false;
            _renamingEntry = null;
            ShowPreview(renameResult.ErrorMessage ?? "Rename failed.");
            FileTree.SetFocus();
        }
    }

    public void CancelRename()
    {
        _renamingEntry = null;
        RenameInput.Visible = false;
        FileTree.SetFocus();
    }

    public void DeleteSelected()
    {
        var target = FileTree.SelectedObject;
        if (target is null || target == _fileTreeService.Root ||
            target.Kind == FileSystemEntryKind.LoadMore)
        {
            return;
        }

        if (!_confirmDelete(target))
        {
            FileTree.SetFocus();
            return;
        }

        var deleteResult = _mutationService.Delete(target);
        if (deleteResult.Success)
        {
            if (_loadedEntry is not null &&
                (_loadedEntry == target || _loadedEntry.FullPath.StartsWith(target.FullPath, StringComparison.Ordinal)))
            {
                _previewLoadVersion++;
                _previewKind = null;
                _loadedEntry = null;
                _savedContent = string.Empty;
                ImagePreview.Visible = false;
                ImagePreview.Clear();
                Preview.Visible = true;
                Preview.ReadOnly = true;
                ShowPreview(TextPreview.ForDirectory().Text);
            }

            InvalidateParentCache(target.FullPath);
            TreeBuilder.Invalidate(target.FullPath);
            _sizeService.InvalidatePathAndAncestors(target.FullPath, _fileTreeService.Root.FullPath);
            FileTree.RebuildTree();
            FileTree.SelectedObject = _fileTreeService.Root;
            if (_showSizes)
            {
                RequestSizesForLoadedEntries();
            }
            UpdatePreviewTitle();
            UpdateShortcutStates();
            FileTree.SetFocus();
        }
        else
        {
            ShowPreview(deleteResult.ErrorMessage ?? $"Unable to delete '{target.Name}'.");
            FileTree.SetFocus();
        }
    }

    private void InvalidateParentCache(string entryFullPath)
    {
        var parentPath = Path.GetDirectoryName(entryFullPath);
        if (!string.IsNullOrEmpty(parentPath))
        {
            TreeBuilder.Invalidate(parentPath);
        }
    }

    public void RequestExit()
    {
        if (_confirmExit())
        {
            _requestStop();
        }
        else
        {
            FileTree.SetFocus();
        }
    }

    private bool ConfirmExitViaMessageBox()
    {
        if (_application is null)
        {
            // No application instance is available (unit tests); proceed without a modal.
            return true;
        }

        var choice = MessageBox.Query(
            _application,
            "Quit",
            "Are you sure you want to quit?",
            "Quit",
            "Cancel");
        return choice == 0;
    }

    private bool ConfirmDeleteViaMessageBox(FileSystemEntry entry)
    {
        if (_application is null)
        {
            // No application instance is available (unit tests); proceed without a modal.
            return true;
        }

        var message = entry.IsDirectory
            ? $"Delete directory '{entry.Name}' and all of its contents?"
            : $"Delete '{entry.Name}'?";
        var choice = MessageBox.Query(
            _application,
            "Delete",
            message,
            "Delete",
            "Cancel");
        return choice == 0;
    }

    private void ShowHelpViaDialog()
    {
        if (_application is null)
        {
            // No application instance is available (unit tests); proceed without a modal.
            return;
        }

        const string helpText =
            "Dotnet Terminal Explorer (dte)\n\n" +
            "Menus & Settings (works over SSH / macOS / web consoles without Alt or Del):\n" +
            "  Ctrl+P / :        Open the searchable Command Menu (every action & shortcut)\n" +
            "  ,                 Open View & Sort Settings (Sort By, Order, Lazy Sizes, Width)\n" +
            "  F1                Show this help dialog\n\n" +
            "Navigation & Layout:\n" +
            "  Tab               Switch focus between Files tree and Preview pane\n" +
            "  < / >             Shrink / expand the left Files panel (in tree, no Alt needed)\n" +
            "  Alt+Left / Alt+[  Shrink the left Files panel\n" +
            "  Alt+Right / Alt+] Expand the left Files panel\n" +
            "  Up / Down         Navigate directories and select files\n" +
            "  Enter             Expand directory / Focus editor for text files\n" +
            "  Right / Ctrl+Right Expand the selected directory (one level)\n\n" +
            "Sorting & Folder Sizes:\n" +
            "  Command Menu / ,  Sort by Name, Modified Date, or Size (Asc / Desc)\n" +
            "  Show Sizes        Opt-in lazy background folder & file size calculation\n" +
            "                    (list always appears first; never blocks navigation)\n\n" +
            "Search & Replace:\n" +
            "  Ctrl+F / F3       Find in active file (if editor focused) or Workspace Search (if tree focused)\n" +
            "  Ctrl+Shift+F      Workspace Search across all files (Ripgrep speed)\n" +
            "  Show in Tree      Apply completed search results as a filter on the file tree\n" +
            "  Alt+X / Esc       Clear an applied tree filter (Esc quits only when no filter is applied)\n" +
            "  F3 / Enter        Next match in find bar\n" +
            "  Shift+F3 / S-Ent  Previous match in find bar\n" +
            "  Ctrl+H            Find & replace in active file (if editor focused)\n" +
            "  Alt+R / Replace   Show/hide replace field (Tab switches inputs)\n" +
            "  Enter (replace)   Replace current match and advance\n" +
            "  Ctrl+Enter        Replace all matches\n" +
            "  Alt+C             Toggle case sensitivity in find bar\n\n" +
            "File Operations:\n" +
            "  Ctrl+S            Save modifications to the active file\n" +
            "  Ctrl+N            Create a new file in the selected directory\n" +
            "  F2                Rename the selected file or directory inline\n" +
            "  Del / Backspace   Delete the selected file or directory (Backspace in tree only)\n" +
            "  F5                Reload the selected file from disk\n" +
            "  Ctrl+L            Load a large (> 2 MB) or binary file as text on demand\n" +
            "  F8                Open selected file with default OS application\n\n" +
            "General:\n" +
            "  Esc               Return to file tree (from editor) / Quit application with confirmation\n";

        var dialogWidth = Viewport.Width > 0 ? Math.Clamp(Viewport.Width - 4, 58, 86) : 84;
        var dialogHeight = Viewport.Height > 0 ? Math.Clamp(Viewport.Height - 2, 16, 36) : 34;

        var openMenuAfterClose = false;
        using var dialog = new Dialog
        {
            Title = "Keyboard Shortcuts & Help",
            Width = dialogWidth,
            Height = dialogHeight,
        };

        var helpView = new TextView
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = true,
            Text = helpText,
        };
        helpView.SetScheme(PreviewColorScheme);

        var openMenuButton = new Button
        {
            Text = "Open Menu (Ctrl+P)",
        };
        openMenuButton.Accepting += (_, e) =>
        {
            openMenuAfterClose = true;
            dialog.RequestStop();
            e.Handled = true;
        };

        var closeButton = new Button
        {
            Text = "Close",
            IsDefault = true,
        };

        closeButton.Accepting += (_, e) =>
        {
            dialog.RequestStop();
            e.Handled = true;
        };

        dialog.AddButton(openMenuButton);
        dialog.AddButton(closeButton);
        dialog.Add(helpView);

        _application.Run(dialog);

        if (openMenuAfterClose)
        {
            OpenCommandPalette();
        }
    }

    public void TriggerContextAwareSearch()
    {
        var isEditorFocused = Preview.HasFocus || FindBar.HasFocus || FindBar.QueryInput.HasFocus;
        if (isEditorFocused && _loadedEntry is { Kind: FileSystemEntryKind.File } && Preview.Visible)
        {
            FindBar.Open();
        }
        else
        {
            OpenWorkspaceSearch();
        }
    }

    public void TriggerContextAwareReplace()
    {
        var isEditorFocused = Preview.HasFocus || FindBar.HasFocus
            || FindBar.QueryInput.HasFocus || FindBar.ReplaceInput.HasFocus;
        if (isEditorFocused && _loadedEntry is { Kind: FileSystemEntryKind.File } && Preview.Visible)
        {
            FindBar.Open(replaceMode: true);
        }
        else
        {
            OpenWorkspaceSearch();
        }
    }

    public void OpenWorkspaceSearch()
    {
        if (_application is null)
        {
            return;
        }

        var dialog = new SearchDialog(_searchService, _fileTreeService.Root.FullPath, _application);
        dialog.ResultChosen += NavigateToSearchResult;
        dialog.ShowInTreeRequested += ShowSearchResultsInTree;

        _application.Run(dialog);
    }

    public bool IsTreeFiltered { get; private set; }

    public void ShowSearchResultsInTree(IReadOnlyList<SearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var matchedFiles = results
            .Select(static result => result.Entry)
            .Where(static entry => entry.Kind == FileSystemEntryKind.File)
            .ToArray();

        if (matchedFiles.Length == 0)
        {
            return;
        }

        TreeBuilder.ApplyFilter(matchedFiles);
        IsTreeFiltered = true;
        UpdateFileTreeTitle();
        ClearFilterShortcut.Enabled = true;
        FileTree.RebuildTree();
        FileTree.Expand(_fileTreeService.Root);
        FileTree.GoToFirst();
        if (_showSizes)
        {
            RequestSizesForLoadedEntries();
        }
        FileTree.SetFocus();
    }

    public void ClearTreeFilter()
    {
        if (!IsTreeFiltered)
        {
            return;
        }

        TreeBuilder.ClearFilter();
        IsTreeFiltered = false;
        UpdateFileTreeTitle();
        ClearFilterShortcut.Enabled = false;
        FileTree.RebuildTree();
        FileTree.Expand(_fileTreeService.Root);
        FileTree.GoToFirst();
        if (_showSizes)
        {
            RequestSizesForLoadedEntries();
        }
        FileTree.SetFocus();
    }

    public void NavigateToSearchResult(SearchResult result)
    {
        ExpandAndSelectPath(result.Entry.FullPath);
        ShowSelection(result.Entry);
        if (result.LineNumber > 0)
        {
            ScrollToLine(result.LineNumber, result.ColumnNumber);
        }
        FileTree.SetFocus();
    }

    public bool ExpandAndSelectPath(string targetFullPath)
    {
        var normalizedTarget = Path.GetFullPath(targetFullPath);
        var root = _fileTreeService.Root;
        var normalizedRoot = Path.GetFullPath(root.FullPath);

        if (!normalizedTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(normalizedTarget, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            FileTree.SelectedObject = root;
            return true;
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        var currentAccumulatedPath = normalizedRoot;

        for (int i = 0; i < segments.Length; i++)
        {
            currentAccumulatedPath = Path.Combine(currentAccumulatedPath, segments[i]);
            var isLast = i == segments.Length - 1;

            FileTree.Expand(current);

            var children = TreeBuilder.GetChildren(current).ToList();
            var next = children.FirstOrDefault(c => string.Equals(c.FullPath, currentAccumulatedPath, StringComparison.OrdinalIgnoreCase));

            while (next is null && TreeBuilder.Advance(current))
            {
                children = TreeBuilder.GetChildren(current).ToList();
                next = children.FirstOrDefault(c => string.Equals(c.FullPath, currentAccumulatedPath, StringComparison.OrdinalIgnoreCase));
            }

            if (next is null)
            {
                return false;
            }

            if (isLast)
            {
                FileTree.RefreshObject(current, startAtTop: false);
                FileTree.SelectedObject = next;
                return true;
            }
            else
            {
                current = next;
            }
        }

        return false;
    }

    private void ScrollToLine(int lineNumber, int columnNumber)
    {
        try
        {
            var row = Math.Max(0, lineNumber - 1);
            Preview.ScrollTo(new Point(0, Math.Max(0, row - 5)));
            Preview.SetNeedsDraw();
        }
        catch
        {
        }
    }

    private void EditSelected()
    {
        if (FileTree.SelectedObject is not { Kind: FileSystemEntryKind.File } selectedFile)
        {
            return;
        }

        try
        {
            _launcher.Launch(selectedFile.FullPath);
        }
        catch (Exception exception)
        {
            ShowPreview($"Unable to open '{selectedFile.Name}': {exception.Message}");
        }
    }

    private void UpdatePreviewTitle()
    {
        if (_loadedEntry is null || _loadedEntry.IsDirectory ||
            _loadedEntry.Kind == FileSystemEntryKind.LoadMore)
        {
            PreviewPane.Title = "Preview";
            return;
        }

        PreviewPane.Title = IsDirty
            ? $"Preview — {_loadedEntry.Name} *"
            : $"Preview — {_loadedEntry.Name}";
    }

    private void UpdateShortcutStates()
    {
        var isFile = _loadedEntry is { Kind: FileSystemEntryKind.File };
        var isEditableText = isFile && _previewService is ITextFileService && !Preview.ReadOnly && Preview.Visible;
        var isLoadablePreview = isFile && (_previewKind == TextPreviewKind.TooLarge || _previewKind == TextPreviewKind.Binary);
        var hasNonRootSelection = FileTree.SelectedObject is { } selection
            && selection != _fileTreeService.Root
            && selection.Kind != FileSystemEntryKind.LoadMore;
        EditShortcut.Enabled = isFile;
        SaveShortcut.Enabled = isEditableText;
        LoadShortcut.Enabled = isLoadablePreview;
        RenameShortcut.Enabled = hasNonRootSelection;
        DeleteShortcut.Enabled = hasNonRootSelection;
        NewFileShortcut.Enabled = true;
    }

    private void ShowPreview(string text)
    {
        Preview.Text = text;
        Preview.MoveHome();
        Preview.ScrollTo(Point.Empty);
        Preview.SetNeedsDraw();
    }
}
