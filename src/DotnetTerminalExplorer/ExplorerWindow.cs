#pragma warning disable CS0618 // The initial design intentionally uses Terminal.Gui TextView for read-only previews.

using System.Drawing;
using DotnetTerminalExplorer.Core;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class ExplorerWindow : Window
{
    private readonly IDefaultFileLauncher _launcher;
    private readonly ITextPreviewService _previewService;

    public ExplorerWindow(
        IFileTreeService fileTree,
        ITextPreviewService previewService,
        IDefaultFileLauncher launcher,
        Action requestStop)
    {
        ArgumentNullException.ThrowIfNull(fileTree);
        ArgumentNullException.ThrowIfNull(previewService);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(requestStop);

        _previewService = previewService;
        _launcher = launcher;

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

        FileTree.SelectionChanged += (_, eventArgs) => ShowSelection(eventArgs.NewValue);

        FileTreePane.Add(FileTree);
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

    internal StatusBar StatusBar { get; }

    internal Shortcut ReloadShortcut { get; private set; } = null!;

    internal Shortcut EditShortcut { get; private set; } = null!;

    internal Shortcut QuitShortcut { get; private set; } = null!;

    private StatusBar CreateStatusBar(Action requestStop)
    {
        ReloadShortcut = new Shortcut(Key.F5, "Reload", ReloadSelected)
        {
            BindKeyToApplication = true,
        };
        EditShortcut = new Shortcut(Key.F8, "Edit", EditSelected)
        {
            BindKeyToApplication = true,
            Enabled = false,
        };
        QuitShortcut = new Shortcut(Key.Esc, "Quit", requestStop)
        {
            BindKeyToApplication = true,
        };

        return new StatusBar([ReloadShortcut, EditShortcut, QuitShortcut]);
    }

    private void ShowSelection(FileSystemEntry? entry)
    {
        EditShortcut.Enabled = entry is { IsDirectory: false };

        var preview = entry is null
            ? TextPreview.ForDirectory()
            : _previewService.Read(entry);

        ShowPreview(preview.Text);
    }

    private void ReloadSelected()
    {
        ShowSelection(FileTree.SelectedObject);
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

    private void ShowPreview(string text)
    {
        Preview.Text = text;
        Preview.MoveHome();
        Preview.ScrollTo(Point.Empty);
        Preview.SetNeedsDraw();
    }
}
