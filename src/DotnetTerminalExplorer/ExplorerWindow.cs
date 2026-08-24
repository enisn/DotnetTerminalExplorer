#pragma warning disable CS0618 // The preview/editor intentionally uses Terminal.Gui TextView.

using System.Drawing;
using DotnetTerminalExplorer.Core;
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

    private readonly IFileTreeService _fileTreeService;
    private readonly IDefaultFileLauncher _launcher;
    private readonly ITextPreviewService _previewService;
    private readonly IFileMutationService _mutationService;

    private FileSystemEntry? _loadedEntry;
    private string _savedContent = string.Empty;
    private FileSystemEntry? _renamingEntry;

    public ExplorerWindow(
        IFileTreeService fileTree,
        ITextPreviewService previewService,
        IDefaultFileLauncher launcher,
        Action requestStop,
        IFileMutationService? mutationService = null)
    {
        ArgumentNullException.ThrowIfNull(fileTree);
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(requestStop);

        _fileTreeService = fileTree;
        _previewService = previewService;
        _launcher = launcher;
        _mutationService = mutationService ?? new FileMutationService();

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
            Width = Dim.Percent(35),
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
        FileTree = new TreeView<FileSystemEntry>
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            MultiSelect = false,
            TreeBuilder = new FileSystemTreeBuilder(fileTree),
        };
        RenameInput = new TextField
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

        FileTree.SelectionChanged += (_, eventArgs) => ShowSelection(eventArgs.NewValue);
        Preview.ContentsChanged += (_, _) => UpdatePreviewTitle();

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

        FileTreePane.Add(FileTree, RenameInput);
        PreviewPane.Add(Preview);
        Add(FileTreePane, PreviewPane, StatusBar);

        FileTree.AddObject(fileTree.Root);
        FileTree.GoToFirst();
        FileTree.Expand(fileTree.Root);
    }

    internal FrameView FileTreePane { get; }

    internal FrameView PreviewPane { get; }

    internal TreeView<FileSystemEntry> FileTree { get; }

    internal TextView Preview { get; }

    internal TextField RenameInput { get; }

    internal StatusBar StatusBar { get; }

    internal Shortcut ReloadShortcut { get; private set; } = null!;

    internal Shortcut SaveShortcut { get; private set; } = null!;

    internal Shortcut RenameShortcut { get; private set; } = null!;

    internal Shortcut EditShortcut { get; private set; } = null!;

    internal Shortcut QuitShortcut { get; private set; } = null!;

    public bool IsDirty =>
        _loadedEntry is { IsDirectory: false }
        && _previewService is ITextFileService
        && Preview.Text != _savedContent;

    public FileSystemEntry? LoadedEntry => _loadedEntry;

    private StatusBar CreateStatusBar(Action requestStop)
    {
        ReloadShortcut = new Shortcut(Key.F5, "Reload", ReloadSelected)
        {
            BindKeyToApplication = true,
        };
        SaveShortcut = new Shortcut(Key.S.WithCtrl, "Save", SaveSelected)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        RenameShortcut = new Shortcut(Key.F2, "Rename", StartRename)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        EditShortcut = new Shortcut(Key.F8, "Edit Ext.", EditSelected)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        QuitShortcut = new Shortcut(Key.Esc, "Quit", requestStop)
        {
            BindKeyToApplication = true,
        };

        return new StatusBar([ReloadShortcut, SaveShortcut, RenameShortcut, EditShortcut, QuitShortcut]);
    }

    private void ShowSelection(FileSystemEntry? entry)
    {
        _loadedEntry = entry;

        if (entry is null || entry.IsDirectory)
        {
            _savedContent = string.Empty;
            Preview.ReadOnly = true;
            ShowPreview(TextPreview.ForDirectory().Text);
        }
        else
        {
            var preview = _previewService.Read(entry);
            _savedContent = preview.Kind == TextPreviewKind.Content ? preview.Text : string.Empty;
            Preview.ReadOnly = preview.Kind != TextPreviewKind.Content;
            ShowPreview(preview.Text);
        }

        UpdatePreviewTitle();
        UpdateShortcutStates();
    }

    private void ReloadSelected()
    {
        ShowSelection(FileTree.SelectedObject);
    }

    public void SaveSelected()
    {
        if (_loadedEntry is not { IsDirectory: false } selectedFile ||
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

    public void StartRename()
    {
        var target = FileTree.SelectedObject;
        if (target is null || target == _fileTreeService.Root)
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

    private void EditSelected()
    {
        if (FileTree.SelectedObject is not { IsDirectory: false } selectedFile)
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
        if (_loadedEntry is null || _loadedEntry.IsDirectory)
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
        var isFile = _loadedEntry is { IsDirectory: false };
        EditShortcut.Enabled = isFile;
        SaveShortcut.Enabled = isFile && _previewService is ITextFileService;
        RenameShortcut.Enabled = FileTree.SelectedObject is not null && FileTree.SelectedObject != _fileTreeService.Root;
    }

    private void ShowPreview(string text)
    {
        Preview.Text = text;
        Preview.MoveHome();
        Preview.ScrollTo(Point.Empty);
        Preview.SetNeedsDraw();
    }
}
