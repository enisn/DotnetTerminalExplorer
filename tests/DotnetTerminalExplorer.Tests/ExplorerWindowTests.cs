#pragma warning disable CS0618 // The production preview intentionally uses Terminal.Gui TextView.

using DotnetTerminalExplorer.Core;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace DotnetTerminalExplorer.Tests;

public sealed class ExplorerWindowTests
{
    [Fact]
    public void Constructor_ComposesThirtyFiveSixtyFiveColumnsAndExpandsOnlyRoot()
    {
        var tree = new FakeFileTreeService();

        using var window = CreateWindow(tree);

        var leftWidth = Assert.IsType<DimPercent>(window.FileTreePane.Width);
        Assert.Equal(35, leftWidth.Percentage);
        Assert.IsType<DimFill>(window.PreviewPane.Width);
        Assert.Equal(2, window.SubViews.Count(view => view != window.StatusBar));
        Assert.Equal([tree.Root.FullPath], tree.EnumeratedDirectories);
    }

    [Fact]
    public void SelectingFile_UpdatesPreviewAndEditEnablement()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "selected content";
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.File;

        Assert.Equal("selected content", window.Preview.Text);
        Assert.False(window.Preview.ReadOnly);
        Assert.True(window.EditShortcut.Enabled);
        Assert.True(window.SaveShortcut.Enabled);
        Assert.True(window.NewFileShortcut.Enabled);
        Assert.True(window.RenameShortcut.Enabled);
        Assert.True(window.DeleteShortcut.Enabled);

        window.FileTree.SelectedObject = tree.Root;

        Assert.True(window.Preview.ReadOnly);
        Assert.False(window.EditShortcut.Enabled);
        Assert.False(window.SaveShortcut.Enabled);
        Assert.True(window.NewFileShortcut.Enabled);
        Assert.False(window.RenameShortcut.Enabled);
        Assert.False(window.DeleteShortcut.Enabled);
    }

    [Fact]
    public void EditingPreview_UpdatesDirtyStateAndTitle()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "initial";
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.File;
        Assert.False(window.IsDirty);
        Assert.Equal("Preview — file.txt", window.PreviewPane.Title);

        window.Preview.Text = "modified content";
        Assert.True(window.IsDirty);
        Assert.Equal("Preview — file.txt *", window.PreviewPane.Title);
    }

    [Fact]
    public void SaveShortcut_SavesContentAndResetsDirtyState()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "initial";
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.File;
        window.Preview.Text = "new content";
        Assert.True(window.IsDirty);

        window.SaveShortcut.Action!.Invoke();

        Assert.False(window.IsDirty);
        Assert.Equal("new content", preview.SavedContentByPath[tree.File.FullPath]);
        Assert.Equal("Preview — file.txt", window.PreviewPane.Title);
    }

    [Fact]
    public void SaveShortcut_DisplaysErrorWhenSaveFails()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService
        {
            SaveError = "Disk full",
        };
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.File;
        window.Preview.Text = "unsaved";

        window.SaveShortcut.Action!.Invoke();

        Assert.Contains("Disk full", window.Preview.Text);
    }

    [Fact]
    public void StartCreate_ShowsInputWithEmptyText()
    {
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(tree);

        window.FileTree.SelectedObject = tree.ChildDirectory;
        window.NewFileShortcut.Action!.Invoke();

        Assert.True(window.CreateInput.Visible);
        Assert.Equal(string.Empty, window.CreateInput.Text);
    }

    [Fact]
    public void CommitCreate_CallsMutationServiceAndUpdatesTree()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        using var window = CreateWindow(tree, mutationService: mutation);

        window.FileTree.SelectedObject = tree.ChildDirectory;
        window.StartCreate();
        window.CreateInput.Text = "newfile.txt";

        window.CommitCreate();

        Assert.False(window.CreateInput.Visible);
        Assert.Equal(tree.ChildDirectory.FullPath, mutation.CreatedEntries[0].parent.FullPath);
        Assert.Equal("newfile.txt", mutation.CreatedEntries[0].fileName);
        Assert.Equal("newfile.txt", window.FileTree.SelectedObject?.Name);
    }

    [Fact]
    public void CommitCreate_DisplaysErrorWhenCreateFails()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService
        {
            CreateError = "File already exists",
        };
        using var window = CreateWindow(tree, mutationService: mutation);

        window.FileTree.SelectedObject = tree.ChildDirectory;
        window.StartCreate();
        window.CreateInput.Text = "newfile.txt";

        window.CommitCreate();

        Assert.False(window.CreateInput.Visible);
        Assert.Contains("File already exists", window.Preview.Text);
    }

    [Fact]
    public void CancelCreate_HidesInputWithoutCreating()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        using var window = CreateWindow(tree, mutationService: mutation);

        window.FileTree.SelectedObject = tree.ChildDirectory;
        window.StartCreate();
        window.CreateInput.Text = "cancelled.txt";

        window.CancelCreate();

        Assert.False(window.CreateInput.Visible);
        Assert.Empty(mutation.CreatedEntries);
    }

    [Fact]
    public void StartRename_ShowsInputWithCurrentName()
    {
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(tree);

        window.FileTree.SelectedObject = tree.File;
        window.RenameShortcut.Action!.Invoke();

        Assert.True(window.RenameInput.Visible);
        Assert.Equal("file.txt", window.RenameInput.Text);
    }

    [Fact]
    public void CommitRename_CallsMutationServiceAndUpdatesTree()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        using var window = CreateWindow(tree, mutationService: mutation);

        window.FileTree.SelectedObject = tree.File;
        window.StartRename();
        window.RenameInput.Text = "renamed.txt";

        window.CommitRename();

        Assert.False(window.RenameInput.Visible);
        Assert.Equal(tree.File.FullPath, mutation.RenamedEntries[0].entry.FullPath);
        Assert.Equal("renamed.txt", mutation.RenamedEntries[0].newName);
        Assert.Equal("renamed.txt", window.FileTree.SelectedObject?.Name);
    }

    [Fact]
    public void CancelRename_HidesInputWithoutModifying()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        using var window = CreateWindow(tree, mutationService: mutation);

        window.FileTree.SelectedObject = tree.File;
        window.StartRename();
        window.RenameInput.Text = "canceled.txt";

        window.CancelRename();

        Assert.False(window.RenameInput.Visible);
        Assert.Empty(mutation.RenamedEntries);
    }

    [Fact]
    public void DeleteShortcut_DeletesSelectedFileAndResetsLoadedSelection()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        using var window = CreateWindow(tree, mutationService: mutation);

        window.FileTree.SelectedObject = tree.File;
        Assert.Equal(tree.File, window.LoadedEntry);

        window.DeleteShortcut.Action!.Invoke();

        Assert.Equal([tree.File.FullPath], mutation.DeletedEntries.Select(e => e.FullPath));
        Assert.Equal(tree.Root, window.LoadedEntry);
        Assert.Equal(tree.Root, window.FileTree.SelectedObject);
    }

    [Fact]
    public void DeleteShortcut_DoesNothingWhenRootIsSelected()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        using var window = CreateWindow(tree, mutationService: mutation);

        window.FileTree.SelectedObject = tree.Root;
        window.DeleteSelected();

        Assert.Empty(mutation.DeletedEntries);
    }

    [Fact]
    public void DeleteShortcut_DisplaysErrorWhenDeleteFails()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService
        {
            DeleteError = "Access denied",
        };
        using var window = CreateWindow(tree, mutationService: mutation);

        window.FileTree.SelectedObject = tree.File;
        window.DeleteSelected();

        Assert.Contains("Access denied", window.Preview.Text);
    }

    [Fact]
    public void Preview_UsesHighContrastAnsi16ColorsForEveryTextState()
    {
        using var window = CreateWindow(new FakeFileTreeService());
        var expectedContent = new TuiAttribute(ColorName16.White, ColorName16.Black);
        var expectedSelection = new TuiAttribute(ColorName16.Black, ColorName16.White);

        Assert.Equal(expectedContent, window.Preview.GetAttributeForRole(VisualRole.Normal));
        Assert.Equal(expectedContent, window.Preview.GetAttributeForRole(VisualRole.Editable));
        Assert.Equal(expectedContent, window.Preview.GetAttributeForRole(VisualRole.ReadOnly));
        Assert.Equal(expectedContent, window.Preview.GetAttributeForRole(VisualRole.Disabled));
        Assert.Equal(expectedSelection, window.Preview.GetAttributeForRole(VisualRole.Active));
        Assert.Equal(expectedSelection, window.Preview.GetAttributeForRole(VisualRole.Highlight));

        VisualRole[] previewTextRoles =
        [
            VisualRole.Normal,
            VisualRole.HotNormal,
            VisualRole.Focus,
            VisualRole.HotFocus,
            VisualRole.Active,
            VisualRole.HotActive,
            VisualRole.Highlight,
            VisualRole.Editable,
            VisualRole.ReadOnly,
            VisualRole.Disabled,
        ];

        foreach (var role in previewTextRoles)
        {
            var attribute = window.Preview.GetAttributeForRole(role);

            Assert.NotEqual(
                attribute.Foreground.GetClosestNamedColor16(),
                attribute.Background.GetClosestNamedColor16());
        }
    }

    [Fact]
    public void ReloadShortcut_RereadsOnlySelectedFile()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "before";
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = tree.File;
        preview.ContentByPath[tree.File.FullPath] = "after";

        window.ReloadShortcut.Action!.Invoke();

        Assert.Equal("after", window.Preview.Text);
        Assert.Equal(2, preview.ReadPaths.Count(path => path == tree.File.FullPath));
        Assert.Equal([tree.Root.FullPath], tree.EnumeratedDirectories);
    }

    [Fact]
    public void EditShortcut_LaunchesSelectedFileWithoutTouchingDirectories()
    {
        var tree = new FakeFileTreeService();
        var launcher = new FakeLauncher();
        using var window = CreateWindow(tree, launcher: launcher);

        window.EditShortcut.Action!.Invoke();
        Assert.Empty(launcher.LaunchedPaths);

        window.FileTree.SelectedObject = tree.File;
        window.EditShortcut.Action.Invoke();

        Assert.Equal([tree.File.FullPath], launcher.LaunchedPaths);
    }

    [Fact]
    public void EditShortcut_DisplaysLaunchFailuresInPreview()
    {
        var tree = new FakeFileTreeService();
        var launcher = new FakeLauncher
        {
            Exception = new InvalidOperationException("No default application."),
        };
        using var window = CreateWindow(tree, launcher: launcher);
        window.FileTree.SelectedObject = tree.File;

        window.EditShortcut.Action!.Invoke();

        Assert.Contains("Unable to open", window.Preview.Text);
        Assert.Contains("No default application", window.Preview.Text);
    }

    [Fact]
    public void StatusBar_DefinesReloadSaveNewRenameDeleteEditAndQuitShortcuts()
    {
        var quitInvocations = 0;
        using var window = CreateWindow(
            new FakeFileTreeService(),
            requestStop: () => quitInvocations++);

        Assert.Equal(Key.F5, window.ReloadShortcut.Key);
        Assert.Equal("Reload", window.ReloadShortcut.Title);
        Assert.True(window.ReloadShortcut.BindKeyToApplication);

        Assert.Equal(Key.S.WithCtrl, window.SaveShortcut.Key);
        Assert.Equal("Save", window.SaveShortcut.Title);

        Assert.Equal(Key.N.WithCtrl, window.NewFileShortcut.Key);
        Assert.Equal("New", window.NewFileShortcut.Title);

        Assert.Equal(Key.F2, window.RenameShortcut.Key);
        Assert.Equal("Rename", window.RenameShortcut.Title);

        Assert.Equal(Key.Delete, window.DeleteShortcut.Key);
        Assert.Equal("Delete", window.DeleteShortcut.Title);

        Assert.Equal(Key.F8, window.EditShortcut.Key);
        Assert.Equal("Edit Ext.", window.EditShortcut.Title);

        Assert.Equal(Key.Esc, window.QuitShortcut.Key);
        Assert.Equal("Quit", window.QuitShortcut.Title);

        window.QuitShortcut.Action!.Invoke();

        Assert.Equal(1, quitInvocations);
    }

    private static ExplorerWindow CreateWindow(
        FakeFileTreeService tree,
        FakeFileService? preview = null,
        FakeLauncher? launcher = null,
        Action? requestStop = null,
        FakeMutationService? mutationService = null) =>
        new(
            tree,
            preview ?? new FakeFileService(),
            launcher ?? new FakeLauncher(),
            requestStop ?? (() => { }),
            mutationService ?? new FakeMutationService());

    private sealed class FakeFileTreeService : IFileTreeService
    {
        public FakeFileTreeService()
        {
            Root = new FileSystemEntry(
                "/scope",
                "scope",
                FileSystemEntryKind.Directory,
                IsReparsePoint: false);
            ChildDirectory = new FileSystemEntry(
                "/scope/child",
                "child",
                FileSystemEntryKind.Directory,
                IsReparsePoint: false);
            File = new FileSystemEntry(
                "/scope/file.txt",
                "file.txt",
                FileSystemEntryKind.File,
                IsReparsePoint: false);
        }

        public FileSystemEntry Root { get; }

        public FileSystemEntry ChildDirectory { get; }

        public FileSystemEntry File { get; }

        public List<string> EnumeratedDirectories { get; } = [];

        public bool CanExpand(FileSystemEntry entry) => entry.IsDirectory;

        public IReadOnlyList<FileSystemEntry> GetChildren(FileSystemEntry directory)
        {
            EnumeratedDirectories.Add(directory.FullPath);

            return directory == Root
                ? [ChildDirectory, File]
                : [];
        }
    }

    private sealed class FakeFileService : ITextFileService
    {
        public Dictionary<string, string> ContentByPath { get; } = [];

        public Dictionary<string, string> SavedContentByPath { get; } = [];

        public List<string> ReadPaths { get; } = [];

        public string? SaveError { get; init; }

        public TextPreview Read(FileSystemEntry entry)
        {
            ReadPaths.Add(entry.FullPath);

            return entry.IsDirectory
                ? TextPreview.ForDirectory()
                : TextPreview.FromContent(ContentByPath.GetValueOrDefault(entry.FullPath, "preview"));
        }

        public FileSaveResult Save(FileSystemEntry entry, string content)
        {
            if (SaveError is not null)
            {
                return FileSaveResult.Failed(SaveError);
            }

            SavedContentByPath[entry.FullPath] = content;
            return FileSaveResult.Successful();
        }
    }

    private sealed class FakeMutationService : IFileMutationService
    {
        public List<(FileSystemEntry entry, string newName)> RenamedEntries { get; } = [];

        public List<(FileSystemEntry parent, string fileName)> CreatedEntries { get; } = [];

        public List<FileSystemEntry> DeletedEntries { get; } = [];

        public string? RenameError { get; init; }

        public string? CreateError { get; init; }

        public string? DeleteError { get; init; }

        public FileRenameResult Rename(FileSystemEntry entry, string newName)
        {
            if (RenameError is not null)
            {
                return FileRenameResult.Failed(RenameError);
            }

            RenamedEntries.Add((entry, newName));
            var newPath = Path.Combine(Path.GetDirectoryName(entry.FullPath) ?? "", newName);
            var updated = new FileSystemEntry(newPath, newName, entry.Kind, entry.IsReparsePoint);
            return FileRenameResult.Successful(updated);
        }

        public FileCreateResult CreateFile(FileSystemEntry parentDirectory, string fileName)
        {
            if (CreateError is not null)
            {
                return FileCreateResult.Failed(CreateError);
            }

            CreatedEntries.Add((parentDirectory, fileName));
            var newPath = Path.Combine(parentDirectory.FullPath, fileName);
            var created = new FileSystemEntry(newPath, fileName, FileSystemEntryKind.File, IsReparsePoint: false);
            return FileCreateResult.Successful(created);
        }

        public FileDeleteResult Delete(FileSystemEntry entry)
        {
            if (DeleteError is not null)
            {
                return FileDeleteResult.Failed(DeleteError);
            }

            DeletedEntries.Add(entry);
            return FileDeleteResult.Successful();
        }
    }

    private sealed class FakeLauncher : IDefaultFileLauncher
    {
        public Exception? Exception { get; init; }

        public List<string> LaunchedPaths { get; } = [];

        public void Launch(string filePath)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            LaunchedPaths.Add(filePath);
        }
    }
}
