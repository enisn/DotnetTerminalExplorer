#pragma warning disable CS0618 // The preview/editor intentionally uses Terminal.Gui TextView.

using System.Drawing;
using DotnetTerminalExplorer.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
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
    private readonly Func<FileSystemEntry, bool> _confirmDelete;
    private readonly Action _showHelp;
    private readonly IApplication? _application;

    private int? _customLeftPaneWidth;
    private FileSystemEntry? _loadedEntry;
    private string _savedContent = string.Empty;
    private FileSystemEntry? _renamingEntry;
    private FileSystemEntry? _creatingInDirectory;
    private int _previewLoadVersion;
    private TextPreviewKind? _previewKind;
    private bool _advancingLoadMore;
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
        ISearchService? searchService = null)
    {
        ArgumentNullException.ThrowIfNull(fileTree);
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(requestStop);

        _fileTreeService = fileTree;
        _previewService = previewService;
        _launcher = launcher;
        _mutationService = mutationService ?? new FileMutationService();
        _searchService = searchService ?? new FastSearchEngine();
        _confirmDelete = confirmDelete ?? ConfirmDeleteViaMessageBox;
        _showHelp = showHelp ?? ShowHelpViaDialog;
        _application = application;

        Title = ProductInfo.Name;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        StatusBar = CreateStatusBar(requestStop);
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
        TreeBuilder = new FileSystemTreeBuilder(fileTree);
        FileTree = new TreeView<FileSystemEntry>
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            MultiSelect = false,
            TreeBuilder = TreeBuilder,
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
            Y = Pos.AnchorEnd(1),
            Visible = false,
        };
        FindBar.OnClose = () => Preview.SetFocus();

        FileTree.SelectionChanged += (_, eventArgs) => ShowSelection(eventArgs.NewValue);
        Preview.ContentsChanged += (_, _) => UpdatePreviewTitle();

        KeyDown += (sender, keyEvent) =>
        {
            if (keyEvent == Key.CursorLeft.WithAlt || keyEvent == new Key('[').WithAlt)
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

    internal Shortcut SearchShortcut { get; private set; } = null!;

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

    public void ShowHelp()
    {
        _showHelp();
    }

    private StatusBar CreateStatusBar(Action requestStop)
    {
        HelpShortcut = new Shortcut(Key.F1, "Help", ShowHelp)
        {
            BindKeyToApplication = true,
        };
        SearchShortcut = new Shortcut(Key.F.WithCtrl, "Search", TriggerContextAwareSearch)
        {
            BindKeyToApplication = true,
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
        QuitShortcut = new Shortcut(Key.Esc, "Quit", requestStop)
        {
            BindKeyToApplication = true,
        };

        return new StatusBar([
            HelpShortcut,
            SearchShortcut,
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
            ShowPreview(TextPreview.ForDirectory().Text);
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
            FileTree.RebuildTree();
            FileTree.SelectedObject = newEntry;
            ShowSelection(newEntry);
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

        var renameResult = _mutationService.Rename(_renamingEntry, RenameInput.Text);
        if (renameResult.Success && renameResult.NewEntry is not null)
        {
            var updatedEntry = renameResult.NewEntry;
            if (_loadedEntry == _renamingEntry)
            {
                _loadedEntry = updatedEntry;
            }

            RenameInput.Visible = false;
            _renamingEntry = null;

            InvalidateParentCache(updatedEntry.FullPath);
            FileTree.RebuildTree();
            FileTree.SelectedObject = updatedEntry;
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
            FileTree.RebuildTree();
            FileTree.SelectedObject = _fileTreeService.Root;
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
            "Navigation & Layout:\n" +
            "  Tab               Switch focus between Files tree and Preview pane\n" +
            "  Alt+Left / Alt+[  Shrink the left Files panel\n" +
            "  Alt+Right / Alt+] Expand the left Files panel\n" +
            "  Up / Down / Enter Navigate directories and select files\n" +
            "  Right / Ctrl+Right Expand the selected directory (one level)\n\n" +
            "Search:\n" +
            "  Ctrl+F / F3       Find in active file (if editor focused) or Workspace Search (if tree focused)\n" +
            "  Ctrl+Shift+F      Workspace Search across all files (Ripgrep speed)\n" +
            "  F3 / Enter        Next match in find bar\n" +
            "  Shift+F3 / S-Ent  Previous match in find bar\n\n" +
            "File Operations:\n" +
            "  Ctrl+S            Save modifications to the active file\n" +
            "  Ctrl+N            Create a new file in the selected directory\n" +
            "  F2                Rename the selected file or directory inline\n" +
            "  Del               Delete the selected file or directory\n" +
            "  F5                Reload the selected file from disk\n" +
            "  Ctrl+L            Load a large file (> 2 MB) on demand\n" +
            "  F8                Open selected file with default OS application\n\n" +
            "General:\n" +
            "  F1                Show this help dialog\n" +
            "  Esc               Quit application (or cancel inline input / dialog)\n";

        var dialog = new Dialog
        {
            Title = "Keyboard Shortcuts & Help",
            Width = 84,
            Height = 24,
        };

        var label = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            TextAlignment = Alignment.Start,
            Text = helpText,
        };

        var closeButton = new Button
        {
            Text = "Close",
            IsDefault = true,
        };

        closeButton.Accepting += (s, e) =>
        {
            dialog.RequestStop();
            e.Handled = true;
        };

        dialog.AddButton(closeButton);
        dialog.Add(label);

        _application.Run(dialog);
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

    public void OpenWorkspaceSearch()
    {
        if (_application is null)
        {
            return;
        }

        var dialog = new SearchDialog(_searchService, _fileTreeService.Root.FullPath, _application);
        dialog.ResultChosen += NavigateToSearchResult;

        _application.Run(dialog);
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
        var isTooLargePreview = isFile && _previewKind == TextPreviewKind.TooLarge;
        var hasNonRootSelection = FileTree.SelectedObject is { } selection
            && selection != _fileTreeService.Root
            && selection.Kind != FileSystemEntryKind.LoadMore;
        EditShortcut.Enabled = isFile;
        SaveShortcut.Enabled = isEditableText;
        LoadShortcut.Enabled = isTooLargePreview;
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
