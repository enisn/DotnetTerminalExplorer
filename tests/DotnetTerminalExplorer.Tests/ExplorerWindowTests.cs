#pragma warning disable CS0618 // The production preview intentionally uses Terminal.Gui TextView.

using DotnetTerminalExplorer.Core;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace DotnetTerminalExplorer.Tests;

public sealed class ExplorerWindowTests
{
    [Fact]
    public void Constructor_ComposesClampedColumnsAndExpandsOnlyRoot()
    {
        var tree = new FakeFileTreeService();

        using var window = CreateWindow(tree);

        Assert.IsType<DimFunc>(window.FileTreePane.Width);
        Assert.IsType<DimFill>(window.PreviewPane.Width);
        Assert.Equal(window.CalculatedLeftPaneWidth, window.GetCalculatedLeftPaneWidth());
        Assert.Equal(2, window.SubViews.Count(view => view != window.StatusBar));
        Assert.Equal([tree.Root.FullPath], tree.EnumeratedDirectories);
        Assert.True(window.FileTree.HasFocus);
    }

    [Fact]
    public void SelectingTooLargeFile_ShowsPlaceholderAndEnablesLoadShortcut()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.PreviewByPath[tree.File.FullPath] = TextPreview.ForTooLarge("File is too large to load automatically.");
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.File;

        Assert.True(window.Preview.Visible);
        Assert.True(window.Preview.ReadOnly);
        Assert.Contains("too large", window.Preview.Text);
        Assert.True(window.LoadShortcut.Enabled);
        Assert.False(window.SaveShortcut.Enabled);
        Assert.True(window.EditShortcut.Enabled);
        Assert.False(window.IsDirty);
    }

    [Fact]
    public void LoadShortcut_ForceLoadsTooLargeFileAndEnablesEditing()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.PreviewByPath[tree.File.FullPath] = TextPreview.ForTooLarge("File is too large to load automatically.");
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = tree.File;

        preview.PreviewByPath[tree.File.FullPath] = TextPreview.FromContent("forced content");
        window.LoadShortcut.Action!.Invoke();

        Assert.Equal("forced content", window.Preview.Text);
        Assert.False(window.Preview.ReadOnly);
        Assert.False(window.LoadShortcut.Enabled);
        Assert.True(window.SaveShortcut.Enabled);
        Assert.Equal([tree.File.FullPath], preview.ForcedReadPaths);
        Assert.False(window.IsDirty);
    }

    [Fact]
    public void ReloadShortcut_ReloadsForcedFilesWithoutAskingAgain()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.PreviewByPath[tree.File.FullPath] = TextPreview.ForTooLarge("File is too large to load automatically.");
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = tree.File;

        preview.PreviewByPath[tree.File.FullPath] = TextPreview.FromContent("forced content");
        window.LoadShortcut.Action!.Invoke();
        preview.PreviewByPath[tree.File.FullPath] = TextPreview.FromContent("reloaded content");
        window.ReloadShortcut.Action!.Invoke();

        Assert.Equal("reloaded content", window.Preview.Text);
        Assert.Equal(2, preview.ForcedReadPaths.Count(path => path == tree.File.FullPath));
    }

    [Fact]
    public void LoadShortcut_DoesNothingForDirectoriesAndUnselectedEntries()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.ChildDirectory;
        window.LoadShortcut.Action!.Invoke();

        Assert.Empty(preview.ForcedReadPaths);
        Assert.Contains("Select a file", window.Preview.Text);
    }

    [Fact]
    public void SelectingFile_ShowsLoadingPlaceholderBeforeContentArrives()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        using var window = CreateWindow(tree, preview);
        string? textDuringRead = null;
        var readOnlyDuringRead = true;
        preview.OnRead = () => (textDuringRead, readOnlyDuringRead) = (window.Preview.Text, window.Preview.ReadOnly);

        window.FileTree.SelectedObject = tree.File;

        Assert.Contains("Loading", textDuringRead);
        Assert.True(readOnlyDuringRead);
        Assert.Equal("preview", window.Preview.Text);
        Assert.False(window.Preview.ReadOnly);
        Assert.False(window.LoadShortcut.Enabled);
    }

    [Fact]
    public void CtrlRight_IsReboundFromRecursiveExpandToSingleLevel()
    {
        using var window = CreateWindow(new FakeFileTreeService());
        var defaults = TreeView<FileSystemEntry>.DefaultKeyBindings;

        Assert.True(defaults.TryGetValue(Command.ExpandAll, out var expandAll));
        Assert.DoesNotContain(Key.CursorRight.WithCtrl, expandAll.All ?? []);

        Assert.True(defaults.TryGetValue(Command.Expand, out var expand));
        Assert.Contains(Key.CursorRight.WithCtrl, expand.All ?? []);
    }

    [Fact]
    public void GetChildren_LargeDirectoriesArePagedWithLoadMoreSentinel()
    {
        var tree = new FakeFileTreeService { PageSize = 1 };
        var builder = new FileSystemTreeBuilder(tree);

        var children = builder.GetChildren(tree.Root).ToArray();

        Assert.Equal(2, children.Length);
        Assert.Equal(tree.ChildDirectory, children[0]);
        Assert.Equal(FileSystemEntryKind.LoadMore, children[1].Kind);
        Assert.False(builder.CanExpand(children[1]));
    }

    [Fact]
    public void TryAdvance_LoadsNextPageAndKeepsSentinelUntilExhausted()
    {
        var tree = new FakeFileTreeService { PageSize = 1 };
        tree.AdditionalChildren.Add(new FileSystemEntry(
            "/scope/extra.txt",
            "extra.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false));
        var builder = new FileSystemTreeBuilder(tree);

        _ = builder.GetChildren(tree.Root).ToArray();

        var sentinel = builder.GetLoadMoreNode(tree.Root);
        Assert.NotNull(sentinel);

        Assert.True(builder.TryAdvance(sentinel!, out var parent));
        Assert.Equal(tree.Root, parent);

        var children = builder.GetChildren(tree.Root).ToArray();
        Assert.Equal(3, children.Length);
        Assert.Equal(FileSystemEntryKind.LoadMore, children[^1].Kind);

        Assert.True(builder.TryAdvance(sentinel!, out _));
        Assert.False(builder.TryAdvance(sentinel!, out _));

        children = builder.GetChildren(tree.Root).ToArray();
        Assert.Equal(
            [tree.ChildDirectory, tree.File, tree.AdditionalChildren[0]],
            children);
    }

    [Fact]
    public void GetChildren_ServesCachedPagesWithoutReenumerating()
    {
        var tree = new FakeFileTreeService { PageSize = 1 };
        var builder = new FileSystemTreeBuilder(tree);

        _ = builder.GetChildren(tree.Root).ToArray();
        var readsAfterFirstLoad = tree.EnumeratedDirectories.Count;

        _ = builder.GetChildren(tree.Root).ToArray();

        Assert.Equal(readsAfterFirstLoad, tree.EnumeratedDirectories.Count);

        builder.Invalidate(tree.Root.FullPath);
        _ = builder.GetChildren(tree.Root).ToArray();

        Assert.Equal(readsAfterFirstLoad + 1, tree.EnumeratedDirectories.Count);
    }

    [Fact]
    public void SelectingLoadMoreSentinel_AutoLoadsNextPage()
    {
        var tree = new FakeFileTreeService { PageSize = 1 };
        tree.AdditionalChildren.Add(new FileSystemEntry(
            "/scope/extra.txt",
            "extra.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false));
        using var window = CreateWindow(tree);

        var sentinel = window.TreeBuilder.GetLoadMoreNode(tree.Root);
        Assert.NotNull(sentinel);

        window.FileTree.SelectedObject = sentinel;

        Assert.Contains(tree.File, window.TreeBuilder.GetChildren(tree.Root));
        Assert.Contains("Loading more entries", window.Preview.Text);
        Assert.True(window.Preview.ReadOnly);
        Assert.False(window.EditShortcut.Enabled);
        Assert.False(window.DeleteShortcut.Enabled);
        Assert.False(window.RenameShortcut.Enabled);
        Assert.False(window.SaveShortcut.Enabled);
        Assert.False(window.IsDirty);
    }

    [Fact]
    public void LoadMoreSentinel_DisappearsWhenDirectoryIsExhausted()
    {
        var tree = new FakeFileTreeService { PageSize = 1 };
        using var window = CreateWindow(tree);

        var sentinel = window.TreeBuilder.GetLoadMoreNode(tree.Root);
        Assert.NotNull(sentinel);

        window.FileTree.SelectedObject = sentinel;

        Assert.Null(window.TreeBuilder.GetLoadMoreNode(tree.Root));
        Assert.DoesNotContain(sentinel!, window.TreeBuilder.GetChildren(tree.Root));
    }

    [Fact]
    public void TryGetPrefetchParent_ReturnsTrueOnlyWithinThreshold()
    {
        var tree = new FakeFileTreeService { PageSize = 10 };
        for (var i = 0; i < 12; i++)
        {
            tree.AdditionalChildren.Add(new FileSystemEntry(
                $"/scope/extra{i:00}.txt",
                $"extra{i:00}.txt",
                FileSystemEntryKind.File,
                IsReparsePoint: false));
        }
        var builder = new FileSystemTreeBuilder(tree);
        _ = builder.GetChildren(tree.Root).ToArray();

        Assert.False(builder.TryGetPrefetchParent(tree.ChildDirectory, out _));
        Assert.False(builder.TryGetPrefetchParent(tree.AdditionalChildren[1], out _));
        Assert.True(builder.TryGetPrefetchParent(tree.AdditionalChildren[2], out var nearEndParent));
        Assert.Equal(tree.Root, nearEndParent);
    }

    [Fact]
    public void TryGetPrefetchParent_ReturnsFalseWhenNoMorePages()
    {
        var tree = new FakeFileTreeService { PageSize = 10 };
        var builder = new FileSystemTreeBuilder(tree);
        _ = builder.GetChildren(tree.Root).ToArray();

        Assert.False(builder.TryGetPrefetchParent(tree.File, out _));
        Assert.False(builder.TryGetPrefetchParent(tree.ChildDirectory, out _));
    }

    [Fact]
    public void NavigatingNearEndOfPage_PrefetchesNextPageAutomatically()
    {
        var tree = new FakeFileTreeService { PageSize = 2 };
        var extra = new FileSystemEntry(
            "/scope/extra.txt",
            "extra.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false);
        tree.AdditionalChildren.Add(extra);
        using var window = CreateWindow(tree);

        Assert.DoesNotContain(extra, window.TreeBuilder.GetChildren(tree.Root));

        window.FileTree.SelectedObject = tree.File;

        Assert.Contains(extra, window.TreeBuilder.GetChildren(tree.Root));
        Assert.Equal(tree.File, window.FileTree.SelectedObject);
        Assert.NotEqual(FileSystemEntryKind.LoadMore, window.FileTree.SelectedObject?.Kind);
    }

    [Fact]
    public void NavigatingEarlyInPage_DoesNotPrefetch()
    {
        var tree = new FakeFileTreeService { PageSize = 10 };
        for (var i = 0; i < 4; i++)
        {
            tree.AdditionalChildren.Add(new FileSystemEntry(
                $"/scope/extra{i:00}.txt",
                $"extra{i:00}.txt",
                FileSystemEntryKind.File,
                IsReparsePoint: false));
        }
        using var window = CreateWindow(tree);
        var readsBefore = tree.EnumeratedDirectories.Count;

        window.FileTree.SelectedObject = tree.ChildDirectory;

        Assert.Equal(readsBefore, tree.EnumeratedDirectories.Count);
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
    public void DeleteShortcut_AsksForConfirmationBeforeDeleting()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        var confirmedEntries = new List<FileSystemEntry>();
        using var window = CreateWindow(
            tree,
            mutationService: mutation,
            confirmDelete: entry =>
            {
                confirmedEntries.Add(entry);
                return true;
            });

        window.FileTree.SelectedObject = tree.File;
        window.DeleteSelected();

        Assert.Equal([tree.File], confirmedEntries);
        Assert.Equal([tree.File.FullPath], mutation.DeletedEntries.Select(e => e.FullPath));
    }

    [Fact]
    public void DeleteShortcut_DoesNotDeleteWhenConfirmationIsDeclined()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        using var window = CreateWindow(
            tree,
            mutationService: mutation,
            confirmDelete: _ => false);

        window.FileTree.SelectedObject = tree.File;

        window.DeleteSelected();

        Assert.Empty(mutation.DeletedEntries);
        Assert.Equal(tree.File, window.LoadedEntry);
        Assert.Equal(tree.File, window.FileTree.SelectedObject);
    }

    [Fact]
    public void DeleteShortcut_DoesNotAskForConfirmationWhenRootIsSelected()
    {
        var tree = new FakeFileTreeService();
        var mutation = new FakeMutationService();
        var confirmationCount = 0;
        using var window = CreateWindow(
            tree,
            mutationService: mutation,
            confirmDelete: _ =>
            {
                confirmationCount++;
                return true;
            });

        window.FileTree.SelectedObject = tree.Root;
        window.DeleteSelected();

        Assert.Equal(0, confirmationCount);
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
    public void StatusBar_DefinesHelpReloadSaveNewRenameDeleteEditAndQuitShortcuts()
    {
        var quitInvocations = 0;
        using var window = CreateWindow(
            new FakeFileTreeService(),
            requestStop: () => quitInvocations++);

        Assert.Equal(Key.F1, window.HelpShortcut.Key);
        Assert.Equal("Help", window.HelpShortcut.Title);
        Assert.True(window.HelpShortcut.BindKeyToApplication);

        Assert.Equal(Key.F5, window.ReloadShortcut.Key);
        Assert.Equal("Reload", window.ReloadShortcut.Title);
        Assert.True(window.ReloadShortcut.BindKeyToApplication);

        Assert.Equal(Key.S.WithCtrl, window.SaveShortcut.Key);
        Assert.Equal("Save", window.SaveShortcut.Title);

        Assert.Equal(Key.L.WithCtrl, window.LoadShortcut.Key);
        Assert.Equal("Load", window.LoadShortcut.Title);

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

    [Fact]
    public void HelpShortcut_InvokesShowHelpDelegate()
    {
        var helpInvocations = 0;
        using var window = CreateWindow(
            new FakeFileTreeService(),
            showHelp: () => helpInvocations++);

        window.HelpShortcut.Action!.Invoke();

        Assert.Equal(1, helpInvocations);
    }

    [Theory]
    [InlineData(60, 24)]   // Min clamp (default min is 24)
    [InlineData(80, 28)]   // 80 * 0.35 = 28
    [InlineData(100, 35)]  // 100 * 0.35 = 35
    [InlineData(200, 48)]  // Max clamp (default max is 48 on ultrawide)
    [InlineData(300, 48)]  // Max clamp on 300-col screen
    public void GetCalculatedLeftPaneWidth_ClampsOnWideAndNarrowScreens(int terminalWidth, int expectedWidth)
    {
        using var window = CreateWindow(new FakeFileTreeService());
        window.Viewport = new System.Drawing.Rectangle(0, 0, terminalWidth, 24);

        var calculated = window.GetCalculatedLeftPaneWidth();

        Assert.Equal(expectedWidth, calculated);
    }

    [Fact]
    public void ShrinkAndExpandLeftPane_AdjustsWidthWithinBounds()
    {
        using var window = CreateWindow(new FakeFileTreeService());
        window.Viewport = new System.Drawing.Rectangle(0, 0, 80, 24);

        // Initial default: 28
        Assert.Equal(28, window.GetCalculatedLeftPaneWidth());
        Assert.Null(window.CustomLeftPaneWidth);

        // Expand by 4 -> 32
        window.ExpandLeftPane(4);
        Assert.Equal(32, window.CustomLeftPaneWidth);
        Assert.Equal(32, window.GetCalculatedLeftPaneWidth());

        // Shrink by 8 -> 24
        window.ShrinkLeftPane(8);
        Assert.Equal(24, window.CustomLeftPaneWidth);
        Assert.Equal(24, window.GetCalculatedLeftPaneWidth());

        // Shrink below MinLeftPaneWidth (18) -> clamped to 18
        window.ShrinkLeftPane(20);
        Assert.Equal(ExplorerWindow.MinLeftPaneWidth, window.CustomLeftPaneWidth);
        Assert.Equal(ExplorerWindow.MinLeftPaneWidth, window.GetCalculatedLeftPaneWidth());

        // Expand above max (80 - 20 = 60) -> clamped to 60
        window.ExpandLeftPane(100);
        Assert.Equal(60, window.CustomLeftPaneWidth);
        Assert.Equal(60, window.GetCalculatedLeftPaneWidth());

        // Reset to default
        window.ResetLeftPaneWidth();
        Assert.Null(window.CustomLeftPaneWidth);
        Assert.Equal(28, window.GetCalculatedLeftPaneWidth());
    }

    [Fact]
    public void SelectingImageFile_ShowsImageViewAndDisablesSave()
    {
        var tree = new FakeFileTreeService();
        var imageEntry = new FileSystemEntry("/scope/photo.png", "photo.png", FileSystemEntryKind.File, IsReparsePoint: false);
        var preview = new FakeFileService();
        preview.PreviewByPath[imageEntry.FullPath] = TextPreview.ForImage("Format: PNG | 800x600");
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = imageEntry;

        Assert.True(window.ImagePreview.Visible);
        Assert.False(window.Preview.Visible);
        Assert.False(window.SaveShortcut.Enabled);
        Assert.True(window.EditShortcut.Enabled);
        Assert.False(window.IsDirty);
    }

    [Fact]
    public void SelectingBinaryFile_ShowsBinaryInfoAndDisablesSave()
    {
        var tree = new FakeFileTreeService();
        var binEntry = new FileSystemEntry("/scope/app.dll", "app.dll", FileSystemEntryKind.File, IsReparsePoint: false);
        var preview = new FakeFileService();
        preview.PreviewByPath[binEntry.FullPath] = TextPreview.ForBinary("[Binary File]");
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = binEntry;

        Assert.False(window.ImagePreview.Visible);
        Assert.True(window.Preview.Visible);
        Assert.True(window.Preview.ReadOnly);
        Assert.True(window.LoadShortcut.Enabled);
        Assert.False(window.SaveShortcut.Enabled);
        Assert.True(window.EditShortcut.Enabled);
        Assert.Equal("[Binary File]", window.Preview.Text);
        Assert.False(window.IsDirty);
    }

    [Fact]
    public void LoadShortcut_ForceLoadsBinaryFileAndEnablesEditing()
    {
        var tree = new FakeFileTreeService();
        var binEntry = new FileSystemEntry("/scope/app.dll", "app.dll", FileSystemEntryKind.File, IsReparsePoint: false);
        var preview = new FakeFileService();
        preview.PreviewByPath[binEntry.FullPath] = TextPreview.ForBinary("[Binary File]");
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = binEntry;

        preview.PreviewByPath[binEntry.FullPath] = TextPreview.FromContent("binary as text");
        window.LoadShortcut.Action!.Invoke();

        Assert.Equal("binary as text", window.Preview.Text);
        Assert.False(window.Preview.ReadOnly);
        Assert.False(window.LoadShortcut.Enabled);
        Assert.True(window.SaveShortcut.Enabled);
        Assert.Equal([binEntry.FullPath], preview.ForcedReadPaths);
        Assert.False(window.IsDirty);
    }

    [Fact]
    public void ReloadShortcut_ReloadsForcedBinaryFilesWithoutAskingAgain()
    {
        var tree = new FakeFileTreeService();
        var binEntry = new FileSystemEntry("/scope/app.dll", "app.dll", FileSystemEntryKind.File, IsReparsePoint: false);
        var preview = new FakeFileService();
        preview.PreviewByPath[binEntry.FullPath] = TextPreview.ForBinary("[Binary File]");
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = binEntry;

        preview.PreviewByPath[binEntry.FullPath] = TextPreview.FromContent("binary as text");
        window.LoadShortcut.Action!.Invoke();
        preview.PreviewByPath[binEntry.FullPath] = TextPreview.FromContent("reloaded binary text");
        window.ReloadShortcut.Action!.Invoke();

        Assert.Equal("reloaded binary text", window.Preview.Text);
        Assert.Equal(2, preview.ForcedReadPaths.Count(path => path == binEntry.FullPath));
    }

    [Fact]
    public void EditShortcut_LaunchesImageFileWithExternalProgram()
    {
        var tree = new FakeFileTreeService();
        var imageEntry = new FileSystemEntry("/scope/logo.png", "logo.png", FileSystemEntryKind.File, IsReparsePoint: false);
        var preview = new FakeFileService();
        var launcher = new FakeLauncher();
        preview.PreviewByPath[imageEntry.FullPath] = TextPreview.ForImage("Format: PNG");
        using var window = CreateWindow(tree, preview, launcher);

        window.FileTree.SelectedObject = imageEntry;
        window.EditShortcut.Action!.Invoke();

        Assert.Equal(["/scope/logo.png"], launcher.LaunchedPaths);
    }

    [Fact]
    public void TriggerContextAwareSearch_WhenEditorFocusedAndFileLoaded_OpensFindBar()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "Sample file content";
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = tree.File;

        // Simulate focus on Preview
        window.Preview.SetFocus();

        Assert.False(window.FindBar.Visible);
        window.TriggerContextAwareSearch();

        Assert.True(window.FindBar.Visible);
    }

    [Fact]
    public void TriggerContextAwareSearch_WhenFileTreeFocused_KeepsFindBarClosed()
    {
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(tree);
        window.FileTree.SetFocus();

        window.TriggerContextAwareSearch();

        Assert.False(window.FindBar.Visible);
    }

    [Fact]
    public void TriggerContextAwareReplace_WhenEditorFocusedAndFileLoaded_OpensFindBarInReplaceMode()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "Sample file content";
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = tree.File;

        window.Preview.SetFocus();

        Assert.False(window.FindBar.Visible);
        window.TriggerContextAwareReplace();

        Assert.True(window.FindBar.Visible);
        Assert.True(window.FindBar.IsReplaceMode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Preview_CtrlH_OpensFindBarInReplaceMode_AndDoesNotDeleteWord(bool terminalEncodesCtrlHAsBackspace)
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "hello world";
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = tree.File;

        window.Preview.SetFocus();

        // Terminals that encode Ctrl+H as 0x08 deliver it as Ctrl+Backspace, which
        // TextView binds to word-delete; the interception must pre-empt that binding.
        var key = terminalEncodesCtrlHAsBackspace ? Key.Backspace.WithCtrl : Key.H.WithCtrl;
        var handled = window.Preview.NewKeyDownEvent(key);

        Assert.True(handled);
        Assert.True(window.FindBar.Visible);
        Assert.True(window.FindBar.IsReplaceMode);
        Assert.Equal("hello world", window.Preview.Text);
    }

    [Fact]
    public void ReplaceAll_InEditor_UpdatesTextAndMarksFileDirty()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "Sample file content";
        using var window = CreateWindow(tree, preview);
        window.FileTree.SelectedObject = tree.File;

        window.FindBar.Open(replaceMode: true);
        window.FindBar.QueryInput.Text = "Sample";
        window.FindBar.ReplaceInput.Text = "Updated";
        window.FindBar.ReplaceAll();

        Assert.Equal("Updated file content", window.Preview.Text);
        Assert.True(window.IsDirty);
        Assert.EndsWith(" *", window.PreviewPane.Title);
    }

    [Fact]
    public void ChangingTreeSelection_DoesNotStealFocusToPreview()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "Sample file content";
        using var window = CreateWindow(tree, preview);

        window.FileTree.SetFocus();
        Assert.True(window.FileTree.HasFocus);

        // Change tree selection as when navigating with arrow keys
        window.FileTree.SelectedObject = tree.File;

        Assert.True(window.FileTree.HasFocus);
        Assert.False(window.Preview.HasFocus);
    }

    [Fact]
    public void NavigateToSearchResult_SelectsFileAndLoadsPreview()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "Line 1\nLine 2 target\nLine 3";
        using var window = CreateWindow(tree, preview);

        var result = new SearchResult(tree.File, 2, 8, "Line 2 target", 6);
        window.NavigateToSearchResult(result);

        Assert.Equal(tree.File, window.LoadedEntry);
        Assert.Equal(tree.File, window.FileTree.SelectedObject);
        Assert.Equal("Line 1\nLine 2 target\nLine 3", window.Preview.Text);
    }

    [Fact]
    public void ExpandAndSelectPath_SelectsNestedFileAndExpandsParents()
    {
        var tree = new FakeFileTreeService();
        var nestedDir = new FileSystemEntry("/scope/sub", "sub", FileSystemEntryKind.Directory, false);
        var nestedFile = new FileSystemEntry("/scope/sub/nested.txt", "nested.txt", FileSystemEntryKind.File, false);
        tree.AdditionalChildren.Add(nestedDir);
        tree.ChildrenByDirectory[nestedDir.FullPath] = [nestedFile];

        using var window = CreateWindow(tree);

        var success = window.ExpandAndSelectPath(nestedFile.FullPath);
        Assert.True(success);
        Assert.Equal(nestedFile, window.FileTree.SelectedObject);
    }

    [Fact]
    public void EnterOnTree_WhenTextFileSelected_FocusesPreview()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.ContentByPath[tree.File.FullPath] = "sample text";
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.File;
        window.FileTree.SetFocus();
        Assert.True(window.FileTree.HasFocus);

        window.FileTree.NewKeyDownEvent(Key.Enter);

        Assert.True(window.Preview.HasFocus);
    }

    [Fact]
    public void EnterOnTree_WhenDirectorySelected_DoesNotFocusPreview()
    {
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(tree);

        window.FileTree.SelectedObject = tree.ChildDirectory;
        window.FileTree.SetFocus();

        window.FileTree.NewKeyDownEvent(Key.Enter);

        Assert.True(window.FileTree.HasFocus);
        Assert.False(window.Preview.HasFocus);
    }

    [Fact]
    public void EnterOnTree_WhenImageSelected_DoesNotFocusPreview()
    {
        var tree = new FakeFileTreeService();
        var imageEntry = new FileSystemEntry("/scope/photo.png", "photo.png", FileSystemEntryKind.File, IsReparsePoint: false);
        var preview = new FakeFileService();
        preview.PreviewByPath[imageEntry.FullPath] = TextPreview.ForImage("Format: PNG");
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = imageEntry;
        window.FileTree.SetFocus();

        window.FileTree.NewKeyDownEvent(Key.Enter);

        Assert.True(window.FileTree.HasFocus);
        Assert.False(window.Preview.HasFocus);
    }

    [Fact]
    public void EnterOnTree_WhenBinarySelected_DoesNotFocusPreview()
    {
        var tree = new FakeFileTreeService();
        var binEntry = new FileSystemEntry("/scope/app.dll", "app.dll", FileSystemEntryKind.File, IsReparsePoint: false);
        var preview = new FakeFileService();
        preview.PreviewByPath[binEntry.FullPath] = TextPreview.ForBinary("[Binary File]");
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = binEntry;
        window.FileTree.SetFocus();

        window.FileTree.NewKeyDownEvent(Key.Enter);

        Assert.True(window.FileTree.HasFocus);
        Assert.False(window.Preview.HasFocus);
    }

    [Fact]
    public void EnterOnTree_WhenTooLargeFileSelected_DoesNotFocusPreview()
    {
        var tree = new FakeFileTreeService();
        var preview = new FakeFileService();
        preview.PreviewByPath[tree.File.FullPath] = TextPreview.ForTooLarge("File is too large.");
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.File;
        window.FileTree.SetFocus();

        window.FileTree.NewKeyDownEvent(Key.Enter);

        Assert.True(window.FileTree.HasFocus);
        Assert.False(window.Preview.HasFocus);
    }

    [Fact]
    public void EscOnPreview_ReturnsFocusToFileTreeAndDoesNotQuit()
    {
        var quitInvocations = 0;
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(tree, requestStop: () => quitInvocations++);

        window.FileTree.SelectedObject = tree.File;
        window.Preview.SetFocus();
        Assert.True(window.Preview.HasFocus);

        window.Preview.NewKeyDownEvent(Key.Esc);

        Assert.True(window.FileTree.HasFocus);
        Assert.Equal(0, quitInvocations);
    }

    [Fact]
    public void EscOnTree_WhenConfirmationAccepted_RequestsStop()
    {
        var quitInvocations = 0;
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(
            tree,
            requestStop: () => quitInvocations++,
            confirmExit: () => true);

        window.FileTree.SetFocus();
        window.FileTree.NewKeyDownEvent(Key.Esc);

        Assert.Equal(1, quitInvocations);
    }

    [Fact]
    public void EscOnTree_WhenConfirmationDeclined_DoesNotRequestStopAndKeepsTreeFocused()
    {
        var quitInvocations = 0;
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(
            tree,
            requestStop: () => quitInvocations++,
            confirmExit: () => false);

        window.FileTree.SetFocus();
        window.FileTree.NewKeyDownEvent(Key.Esc);

        Assert.Equal(0, quitInvocations);
        Assert.True(window.FileTree.HasFocus);
    }

    [Fact]
    public void RequestExit_WhenConfirmed_InvokesRequestStop()
    {
        var quitInvocations = 0;
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(
            tree,
            requestStop: () => quitInvocations++,
            confirmExit: () => true);

        window.RequestExit();

        Assert.Equal(1, quitInvocations);
    }

    [Fact]
    public void RequestExit_WhenDeclined_KeepsFileTreeFocusedAndDoesNotStop()
    {
        var quitInvocations = 0;
        var tree = new FakeFileTreeService();
        using var window = CreateWindow(
            tree,
            requestStop: () => quitInvocations++,
            confirmExit: () => false);

        window.Preview.SetFocus();
        window.RequestExit();

        Assert.Equal(0, quitInvocations);
        Assert.True(window.FileTree.HasFocus);
    }

    private static ExplorerWindow CreateWindow(
        FakeFileTreeService tree,
        FakeFileService? preview = null,
        FakeLauncher? launcher = null,
        Action? requestStop = null,
        FakeMutationService? mutationService = null,
        Func<FileSystemEntry, bool>? confirmDelete = null,
        Action? showHelp = null,
        Func<bool>? confirmExit = null) =>
        new(
            tree,
            preview ?? new FakeFileService(),
            launcher ?? new FakeLauncher(),
            requestStop ?? (() => { }),
            mutationService ?? new FakeMutationService(),
            confirmDelete,
            showHelp,
            confirmExit: confirmExit);

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

        public List<FileSystemEntry> AdditionalChildren { get; } = [];

        public Dictionary<string, List<FileSystemEntry>> ChildrenByDirectory { get; } = [];

        public List<string> EnumeratedDirectories { get; } = [];

        public int PageSize { get; set; } = int.MaxValue;

        public bool CanExpand(FileSystemEntry entry) => entry.IsDirectory;

        public IReadOnlyList<FileSystemEntry> GetChildren(FileSystemEntry directory)
        {
            EnumeratedDirectories.Add(directory.FullPath);

            if (ChildrenByDirectory.TryGetValue(directory.FullPath, out var customChildren))
            {
                return customChildren;
            }

            return directory == Root
                ? [ChildDirectory, File, .. AdditionalChildren]
                : [];
        }

        public FileTreePage GetChildrenPage(FileSystemEntry directory, int skip)
        {
            var all = GetChildren(directory);
            var page = all.Skip(skip).ToList();

            return page.Count > PageSize
                ? new FileTreePage([.. page.Take(PageSize)], HasMore: true)
                : new FileTreePage(page, HasMore: false);
        }
    }

    private sealed class FakeFileService : ITextFileService
    {
        public Dictionary<string, string> ContentByPath { get; } = [];

        public Dictionary<string, TextPreview> PreviewByPath { get; } = [];

        public Dictionary<string, string> SavedContentByPath { get; } = [];

        public List<string> ReadPaths { get; } = [];

        public List<string> ForcedReadPaths { get; } = [];

        public Action? OnRead { get; set; }

        public string? SaveError { get; init; }

        public TextPreview Read(FileSystemEntry entry, bool forceLoad = false)
        {
            OnRead?.Invoke();
            ReadPaths.Add(entry.FullPath);
            if (forceLoad)
            {
                ForcedReadPaths.Add(entry.FullPath);
            }

            if (PreviewByPath.TryGetValue(entry.FullPath, out var customPreview))
            {
                return customPreview;
            }

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
