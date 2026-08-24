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
    private FileSystemEntry? _creatingInDirectory;

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
        };

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
        PreviewPane.Add(Preview, ImagePreview);
        Add(FileTreePane, PreviewPane, StatusBar);

        FileTree.AddObject(fileTree.Root);
        FileTree.GoToFirst();
        FileTree.Expand(fileTree.Root);
    }

    internal FrameView FileTreePane { get; }

    internal FrameView PreviewPane { get; }

    internal TreeView<FileSystemEntry> FileTree { get; }

    internal TextView Preview { get; }

    internal ImagePreviewView ImagePreview { get; }

    internal TextField RenameInput { get; }

    internal TextField CreateInput { get; }

    internal StatusBar StatusBar { get; }

    internal Shortcut ReloadShortcut { get; private set; } = null!;

    internal Shortcut SaveShortcut { get; private set; } = null!;

    internal Shortcut NewFileShortcut { get; private set; } = null!;

    internal Shortcut RenameShortcut { get; private set; } = null!;

    internal Shortcut DeleteShortcut { get; private set; } = null!;

    internal Shortcut EditShortcut { get; private set; } = null!;

    internal Shortcut QuitShortcut { get; private set; } = null!;

    public bool IsDirty =>
        _loadedEntry is { IsDirectory: false }
        && _previewService is ITextFileService
        && !Preview.ReadOnly
        && Preview.Visible
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
        QuitShortcut = new Shortcut(Key.Esc, "Quit", requestStop)
        {
            BindKeyToApplication = true,
        };

        return new StatusBar([
            ReloadShortcut,
            SaveShortcut,
            NewFileShortcut,
            RenameShortcut,
            DeleteShortcut,
            EditShortcut,
            QuitShortcut
        ]);
    }

    private void ShowSelection(FileSystemEntry? entry)
    {
        _loadedEntry = entry;

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
            var preview = _previewService.Read(entry);
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

    public void StartCreate()
    {
        CancelRename();

        var target = FileTree.SelectedObject ?? _fileTreeService.Root;
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

    public void DeleteSelected()
    {
        var target = FileTree.SelectedObject;
        if (target is null || target == _fileTreeService.Root)
        {
            return;
        }

        var deleteResult = _mutationService.Delete(target);
        if (deleteResult.Success)
        {
            if (_loadedEntry is not null &&
                (_loadedEntry == target || _loadedEntry.FullPath.StartsWith(target.FullPath, StringComparison.Ordinal)))
            {
                _loadedEntry = null;
                _savedContent = string.Empty;
                ImagePreview.Visible = false;
                ImagePreview.Clear();
                Preview.Visible = true;
                Preview.ReadOnly = true;
                ShowPreview(TextPreview.ForDirectory().Text);
            }

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
        var isEditableText = isFile && _previewService is ITextFileService && !Preview.ReadOnly && Preview.Visible;
        var hasNonRootSelection = FileTree.SelectedObject is not null && FileTree.SelectedObject != _fileTreeService.Root;
        EditShortcut.Enabled = isFile;
        SaveShortcut.Enabled = isEditableText;
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
