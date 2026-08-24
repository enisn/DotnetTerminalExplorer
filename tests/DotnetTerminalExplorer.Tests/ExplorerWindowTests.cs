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
        var preview = new FakePreviewService();
        preview.ContentByPath[tree.File.FullPath] = "selected content";
        using var window = CreateWindow(tree, preview);

        window.FileTree.SelectedObject = tree.File;

        Assert.Equal("selected content", window.Preview.Text);
        Assert.True(window.EditShortcut.Enabled);

        window.FileTree.SelectedObject = tree.Root;

        Assert.False(window.EditShortcut.Enabled);
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
        var preview = new FakePreviewService();
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
    public void StatusBar_DefinesReloadEditAndQuitShortcuts()
    {
        var quitInvocations = 0;
        using var window = CreateWindow(
            new FakeFileTreeService(),
            requestStop: () => quitInvocations++);

        Assert.Equal(Key.F5, window.ReloadShortcut.Key);
        Assert.Equal("Reload", window.ReloadShortcut.Title);
        Assert.True(window.ReloadShortcut.BindKeyToApplication);
        Assert.Equal(Key.F8, window.EditShortcut.Key);
        Assert.Equal("Edit", window.EditShortcut.Title);
        Assert.Equal(Key.Esc, window.QuitShortcut.Key);
        Assert.Equal("Quit", window.QuitShortcut.Title);

        window.QuitShortcut.Action!.Invoke();

        Assert.Equal(1, quitInvocations);
    }

    private static ExplorerWindow CreateWindow(
        FakeFileTreeService tree,
        FakePreviewService? preview = null,
        FakeLauncher? launcher = null,
        Action? requestStop = null) =>
        new(
            tree,
            preview ?? new FakePreviewService(),
            launcher ?? new FakeLauncher(),
            requestStop ?? (() => { }));

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

    private sealed class FakePreviewService : ITextPreviewService
    {
        public Dictionary<string, string> ContentByPath { get; } = [];

        public List<string> ReadPaths { get; } = [];

        public TextPreview Read(FileSystemEntry entry)
        {
            ReadPaths.Add(entry.FullPath);

            return entry.IsDirectory
                ? TextPreview.ForDirectory()
                : TextPreview.FromContent(ContentByPath.GetValueOrDefault(entry.FullPath, "preview"));
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
