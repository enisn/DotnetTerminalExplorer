#pragma warning disable CS0618 // The production preview intentionally uses Terminal.Gui TextView.

using DotnetTerminalExplorer.Core;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer.Tests;

public sealed class CommandMenuAndViewTests
{
    [Fact]
    public void MenuShortcut_IsCtrlPAndBoundToApplication()
    {
        using var window = CreateWindow(new FakeTreeService());

        Assert.Equal(Key.P.WithCtrl, window.MenuShortcut.Key);
        Assert.Equal("Menu", window.MenuShortcut.Title);
        Assert.True(window.MenuShortcut.BindKeyToApplication);
    }

    [Fact]
    public void FileTree_AngleBrackets_ResizeLeftPaneWithoutAltModifier()
    {
        using var window = CreateWindow(new FakeTreeService());
        window.Viewport = new System.Drawing.Rectangle(0, 0, 100, 30);
        window.FileTree.SetFocus();

        var initial = window.GetCalculatedLeftPaneWidth();

        // Plain '>' and shifted '>' (as sent by Kitty/iTerm2 keyboard protocols) both expand.
        var expanded = window.FileTree.NewKeyDownEvent(new Key('>'));
        Assert.True(expanded);
        Assert.Equal(initial + 4, window.GetCalculatedLeftPaneWidth());

        var expandedWithShift = window.FileTree.NewKeyDownEvent(new Key((KeyCode)'>' | KeyCode.ShiftMask));
        Assert.True(expandedWithShift);
        Assert.Equal(initial + 8, window.GetCalculatedLeftPaneWidth());

        var shrunk = window.FileTree.NewKeyDownEvent(new Key('<'));
        Assert.True(shrunk);
        Assert.Equal(initial + 4, window.GetCalculatedLeftPaneWidth());

        // ':' and ',' are also intercepted before TreeView's KeystrokeNavigator.
        Assert.True(window.FileTree.NewKeyDownEvent(new Key(':')));
        Assert.True(window.FileTree.NewKeyDownEvent(new Key(',')));
    }

    [Fact]
    public void Preview_CtrlP_IsInterceptedBeforeTextViewBindings()
    {
        var tree = new FakeTreeService();
        using var window = CreateWindow(tree);
        window.FileTree.SelectedObject = tree.SmallFile;
        window.Preview.SetFocus();

        Assert.True(window.Preview.NewKeyDownEvent(Key.P.WithCtrl));
    }

    [Fact]
    public void CommandPaletteDialog_FiltersAndExecutesEnabledCommand()
    {
        var executed = new List<string>();
        CommandPaletteItem[] commands =
        [
            new("file.delete", "File", "Delete Selected", "Del / Backspace", true, () => executed.Add("delete")),
            new("layout.expand", "Layout", "Expand Left Pane", "Alt+Right / >", true, () => executed.Add("expand")),
            new("sort.size-desc", "Sort", "Size (Largest First)", "", true, () => executed.Add("size")),
        ];

        using var dialog = new CommandPaletteDialog(commands, "Selected: big.log");

        dialog.FilterInput.Text = "del";
        dialog.ApplyFilter(dialog.FilterInput.Text);

        var single = Assert.Single(dialog.FilteredCommands);
        Assert.Equal("file.delete", single.Id);

        Assert.True(dialog.TryAcceptSelected());
        Assert.Equal("file.delete", dialog.SelectedCommand?.Id);

        dialog.SelectedCommand!.Execute();
        Assert.Equal(["delete"], executed);
    }

    [Fact]
    public void CommandPaletteDialog_RefusesDisabledCommandAndDisplaysReason()
    {
        CommandPaletteItem[] commands =
        [
            new(
                "file.delete",
                "File",
                "Delete Selected",
                "Del / Backspace",
                false,
                () => { },
                DisabledReason: "Select a file or folder (not the root directory)."),
        ];

        using var dialog = new CommandPaletteDialog(commands);

        Assert.False(dialog.TryAcceptSelected());
        Assert.Null(dialog.SelectedCommand);
        Assert.Contains("root directory", dialog.StatusLabel.Text);
    }

    [Fact]
    public void CommandPaletteDialog_ArrowKeysNavigateListDirectlyFromFilterInput()
    {
        CommandPaletteItem[] commands =
        [
            new("a", "File", "First", "", true, () => { }),
            new("b", "File", "Second", "", true, () => { }),
            new("c", "File", "Third", "", true, () => { }),
        ];

        using var dialog = new CommandPaletteDialog(commands);
        Assert.Equal(0, dialog.CommandsListView.SelectedItem);

        dialog.FilterInput.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(1, dialog.CommandsListView.SelectedItem);

        dialog.FilterInput.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(2, dialog.CommandsListView.SelectedItem);

        dialog.FilterInput.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal(1, dialog.CommandsListView.SelectedItem);
    }

    [Fact]
    public void BuildCommandPaletteItems_ReflectsSelectionAndExecutesDeleteAndResizeWithoutModifiers()
    {
        var tree = new FakeTreeService();
        var mutation = new FakeMutationService();
        using var window = CreateWindow(tree, mutationService: mutation);
        window.Viewport = new System.Drawing.Rectangle(0, 0, 100, 30);

        // On Root: Delete and Rename are unavailable.
        window.FileTree.SelectedObject = tree.Root;
        var rootItems = window.BuildCommandPaletteItems();
        Assert.False(rootItems.Single(i => i.Id == "file.delete").IsEnabled);
        Assert.False(rootItems.Single(i => i.Id == "file.rename").IsEnabled);

        // Select a file: Delete becomes enabled and executes cleanly without pressing Del.
        window.FileTree.SelectedObject = tree.SmallFile;
        var fileItems = window.BuildCommandPaletteItems();
        var deleteCmd = fileItems.Single(i => i.Id == "file.delete");
        Assert.True(deleteCmd.IsEnabled);
        deleteCmd.Execute();
        Assert.Equal([tree.SmallFile.FullPath], mutation.DeletedPaths);

        // Expand/Shrink commands adjust left pane without Alt keys.
        var initialWidth = window.GetCalculatedLeftPaneWidth();
        fileItems.Single(i => i.Id == "layout.expand").Execute();
        Assert.Equal(initialWidth + 8, window.GetCalculatedLeftPaneWidth());

        fileItems.Single(i => i.Id == "layout.shrink").Execute();
        Assert.Equal(initialWidth, window.GetCalculatedLeftPaneWidth());
    }

    [Fact]
    public void ResizePaneDialog_HandlesArrowsPlusMinusAndPresetsWithoutAltKey()
    {
        using var window = CreateWindow(new FakeTreeService());
        window.Viewport = new System.Drawing.Rectangle(0, 0, 100, 30);

        using var dialog = new ResizePaneDialog(
            window.GetCalculatedLeftPaneWidth,
            () => 100,
            () => !window.CustomLeftPaneWidth.HasValue,
            window.ShrinkLeftPane,
            window.ExpandLeftPane,
            window.SetLeftPaneWidth,
            window.ResetLeftPaneWidth);

        Assert.Equal(35, window.GetCalculatedLeftPaneWidth());

        Assert.True(dialog.NewKeyDownEvent(Key.CursorRight));
        Assert.Equal(39, window.GetCalculatedLeftPaneWidth());

        Assert.True(dialog.NewKeyDownEvent(new Key('+')));
        Assert.Equal(43, window.GetCalculatedLeftPaneWidth());

        Assert.True(dialog.NewKeyDownEvent(Key.CursorLeft));
        Assert.Equal(39, window.GetCalculatedLeftPaneWidth());

        Assert.True(dialog.NewKeyDownEvent(new Key('3'))); // Wide 50%
        Assert.Equal(50, window.GetCalculatedLeftPaneWidth());

        Assert.True(dialog.NewKeyDownEvent(new Key('4'))); // Extra Wide 70%
        Assert.Equal(70, window.GetCalculatedLeftPaneWidth());

        Assert.True(dialog.NewKeyDownEvent(new Key('0'))); // Reset Auto
        Assert.Null(window.CustomLeftPaneWidth);
        Assert.Equal(35, window.GetCalculatedLeftPaneWidth());
    }

    [Fact]
    public void ViewSettingsDialog_AppliesSortOptionsAndShowSizes()
    {
        using var dialog = new ViewSettingsDialog(
            FileTreeSortOptions.Default,
            currentShowSizes: false);

        // Select Size -> defaults to Descending and auto-checks ShowSizes.
        dialog.SortFieldSelector.Value = 2;
        Assert.Equal(1, dialog.SortDirectionSelector.Value);
        Assert.Equal(CheckState.Checked, dialog.ShowSizesCheck.Value);

        dialog.ApplyAndClose();

        Assert.True(dialog.Applied);
        Assert.Equal(new FileTreeSortOptions(FileTreeSortField.Size, FileTreeSortDirection.Descending), dialog.SelectedSortOptions);
        Assert.True(dialog.SelectedShowSizes);
    }

    [Fact]
    public void ShowSizes_IsOffByDefaultAndRequestsZeroSizesAtStartup()
    {
        var tree = new FakeTreeService();
        var sizeService = new FakeEntrySizeService();
        using var window = CreateWindow(tree, sizeService: sizeService);

        Assert.False(window.ShowSizes);
        Assert.Equal("Files", window.FileTreePane.Title);
        Assert.Empty(sizeService.RequestedPaths);
        Assert.Equal("small.txt", window.FormatTreeEntry(tree.SmallFile));
    }

    [Fact]
    public void ShowSizes_WhenEnabled_ShowsPlaceholdersImmediatelyAndUpdatesLazily()
    {
        var tree = new FakeTreeService();
        var sizeService = new FakeEntrySizeService();
        using var window = CreateWindow(tree, sizeService: sizeService);

        window.SetShowSizes(true);

        Assert.True(window.ShowSizes);
        // Before background calculation finishes, entries display placeholder immediately.
        Assert.Contains("...", window.FormatTreeEntry(tree.BigDir));
        Assert.Contains(tree.BigDir.FullPath, sizeService.RequestedPaths);
        Assert.Contains(tree.SmallFile.FullPath, sizeService.RequestedPaths);

        // Simulate lazy background result arriving.
        sizeService.Complete(tree.BigDir.FullPath, 2L * 1024 * 1024);

        Assert.Contains("2.0 MB", window.FormatTreeEntry(tree.BigDir));
        Assert.Equal("Files (sizes)", window.FileTreePane.Title);

        // Turning OFF cancels pending work and restores plain names.
        window.SetShowSizes(false);
        Assert.True(sizeService.CancelPendingCallCount > 0);
        Assert.Equal("Files", window.FileTreePane.Title);
        Assert.Equal("big-dir", window.FormatTreeEntry(tree.BigDir));
    }

    [Fact]
    public void SortBySize_EnablesShowSizesAndResortsLazilyWhenDirSizesArrive_PreservingSelection()
    {
        var tree = new FakeTreeService();
        var preview = new FakeTextFileService();
        var sizeService = new FakeEntrySizeService();
        sizeService.SetSize(tree.SmallFile.FullPath, 100);
        sizeService.SetSize(tree.LargeFile.FullPath, 50_000);
        using var window = CreateWindow(tree, preview, sizeService: sizeService);

        window.FileTree.SelectedObject = tree.SmallFile;
        var readsBeforeSort = preview.ReadCount;

        window.SetSortOptions(new FileTreeSortOptions(FileTreeSortField.Size, FileTreeSortDirection.Descending));

        Assert.True(window.ShowSizes);
        Assert.Equal(FileTreeSortField.Size, window.SortOptions.Field);
        Assert.Equal(tree.SmallFile, window.FileTree.SelectedObject);
        Assert.Equal(readsBeforeSort, preview.ReadCount); // Selection preserved without re-reading preview!

        // Files already sort largest-first immediately.
        var initialChildren = window.TreeBuilder.GetChildren(tree.Root).ToArray();
        Assert.Equal(tree.LargeFile, initialChildren[2]);
        Assert.Equal(tree.SmallFile, initialChildren[3]);

        // Now directory sizes arrive asynchronously: big-dir (10 MB) > small-dir (1 KB).
        sizeService.Complete(tree.SmallDir.FullPath, 1024);
        sizeService.Complete(tree.BigDir.FullPath, 10L * 1024 * 1024);

        var resortedChildren = window.TreeBuilder.GetChildren(tree.Root).ToArray();
        Assert.Equal(
            [tree.BigDir, tree.SmallDir, tree.LargeFile, tree.SmallFile],
            resortedChildren);
        Assert.Equal(tree.SmallFile, window.FileTree.SelectedObject);
        Assert.Equal(readsBeforeSort, preview.ReadCount);
    }

    [Fact]
    public void ExpandingSubdirectory_WhileShowSizesEnabled_LazilyRequestsNewChildren()
    {
        var tree = new FakeTreeService();
        var nested = new FileSystemEntry("/scope/big-dir/huge.log", "huge.log", FileSystemEntryKind.File, false);
        tree.ChildrenByDirectory[tree.BigDir.FullPath] = [nested];
        var sizeService = new FakeEntrySizeService();
        using var window = CreateWindow(tree, sizeService: sizeService);

        window.SetShowSizes(true);
        Assert.DoesNotContain(nested.FullPath, sizeService.RequestedPaths);

        // Expanding big-dir loads its children immediately and triggers lazy size request.
        _ = window.TreeBuilder.GetChildren(tree.BigDir).ToArray();
        Assert.Contains(nested.FullPath, sizeService.RequestedPaths);
    }

    [Fact]
    public void DeletingEntry_InvalidatesCachedSizeAndAncestors()
    {
        var tree = new FakeTreeService();
        var mutation = new FakeMutationService();
        var sizeService = new FakeEntrySizeService();
        sizeService.SetSize(tree.SmallFile.FullPath, 512);
        using var window = CreateWindow(tree, mutationService: mutation, sizeService: sizeService);

        window.FileTree.SelectedObject = tree.SmallFile;
        window.DeleteSelected();

        Assert.Contains(tree.SmallFile.FullPath, sizeService.InvalidatedPaths);
    }

    private static ExplorerWindow CreateWindow(
        FakeTreeService tree,
        FakeTextFileService? preview = null,
        FakeMutationService? mutationService = null,
        FakeEntrySizeService? sizeService = null) =>
        new(
            tree,
            preview ?? new FakeTextFileService(),
            new NoOpLauncher(),
            () => { },
            mutationService ?? new FakeMutationService(),
            confirmDelete: _ => true,
            confirmExit: () => true,
            sizeService: sizeService ?? new FakeEntrySizeService());

    private sealed class FakeTreeService : IFileTreeService
    {
        public FakeTreeService()
        {
            Root = new FileSystemEntry("/scope", "scope", FileSystemEntryKind.Directory, false);
            SmallDir = new FileSystemEntry("/scope/alpha-small-dir", "alpha-small-dir", FileSystemEntryKind.Directory, false);
            BigDir = new FileSystemEntry("/scope/big-dir", "big-dir", FileSystemEntryKind.Directory, false);
            SmallFile = new FileSystemEntry("/scope/small.txt", "small.txt", FileSystemEntryKind.File, false);
            LargeFile = new FileSystemEntry("/scope/large.bin", "large.bin", FileSystemEntryKind.File, false);
        }

        public FileSystemEntry Root { get; }
        public FileSystemEntry SmallDir { get; }
        public FileSystemEntry BigDir { get; }
        public FileSystemEntry SmallFile { get; }
        public FileSystemEntry LargeFile { get; }

        public Dictionary<string, List<FileSystemEntry>> ChildrenByDirectory { get; } = [];

        public bool CanExpand(FileSystemEntry entry) => entry.IsDirectory;

        public IReadOnlyList<FileSystemEntry> GetChildren(FileSystemEntry directory)
        {
            if (ChildrenByDirectory.TryGetValue(directory.FullPath, out var custom))
            {
                return custom;
            }

            return directory == Root
                ? [SmallDir, BigDir, LargeFile, SmallFile]
                : [];
        }

        public FileTreePage GetChildrenPage(FileSystemEntry directory, int skip) =>
            new(GetChildren(directory).Skip(skip).ToArray(), HasMore: false);
    }

    private sealed class FakeTextFileService : ITextFileService
    {
        public int ReadCount { get; private set; }

        public TextPreview Read(FileSystemEntry entry, bool forceLoad = false)
        {
            ReadCount++;
            return entry.IsDirectory ? TextPreview.ForDirectory() : TextPreview.FromContent("hello");
        }

        public FileSaveResult Save(FileSystemEntry entry, string content) => FileSaveResult.Successful();
    }

    private sealed class FakeMutationService : IFileMutationService
    {
        public List<string> DeletedPaths { get; } = [];

        public FileCreateResult CreateFile(FileSystemEntry parentDirectory, string fileName) =>
            FileCreateResult.Successful(new FileSystemEntry(Path.Combine(parentDirectory.FullPath, fileName), fileName, FileSystemEntryKind.File, false));

        public FileRenameResult Rename(FileSystemEntry entry, string newName) =>
            FileRenameResult.Successful(entry with { Name = newName });

        public FileDeleteResult Delete(FileSystemEntry entry)
        {
            DeletedPaths.Add(entry.FullPath);
            return FileDeleteResult.Successful();
        }
    }

    private sealed class NoOpLauncher : IDefaultFileLauncher
    {
        public void Launch(string filePath) { }
    }

    private sealed class FakeEntrySizeService : IEntrySizeService
    {
        private readonly Dictionary<string, EntrySizeInfo> _sizes = new(StringComparer.Ordinal);

        public bool IsCalculating { get; set; }

        public List<string> RequestedPaths { get; } = [];

        public List<string> InvalidatedPaths { get; } = [];

        public int CancelPendingCallCount { get; private set; }

        public event Action<IReadOnlyCollection<string>>? SizesUpdated;

        public void SetSize(string path, long bytes, bool isPartial = false) =>
            _sizes[path] = EntrySizeInfo.Ready(bytes, isPartial);

        public void Complete(string path, long bytes, bool isPartial = false)
        {
            _sizes[path] = EntrySizeInfo.Ready(bytes, isPartial);
            SizesUpdated?.Invoke([path]);
        }

        public bool TryGetSize(string fullPath, out EntrySizeInfo info) =>
            _sizes.TryGetValue(fullPath, out info);

        public long? TryGetKnownBytes(string fullPath) =>
            _sizes.TryGetValue(fullPath, out var info) && info.State == EntrySizeState.Ready
                ? info.Bytes
                : null;

        public void RequestSizes(IEnumerable<FileSystemEntry> entries)
        {
            foreach (var entry in entries)
            {
                RequestedPaths.Add(entry.FullPath);
                _sizes.TryAdd(entry.FullPath, EntrySizeInfo.Pending);
            }
        }

        public void CancelPending() => CancelPendingCallCount++;

        public void InvalidatePathAndAncestors(string fullPath, string rootPath)
        {
            InvalidatedPaths.Add(fullPath);
            _sizes.Remove(fullPath);
        }

        public void Clear() => _sizes.Clear();

        public void Dispose() { }
    }
}
