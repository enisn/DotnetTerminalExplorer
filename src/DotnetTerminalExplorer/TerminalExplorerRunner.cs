using DotnetTerminalExplorer.Core;
using Terminal.Gui.App;

namespace DotnetTerminalExplorer;

internal static class TerminalExplorerRunner
{
    public static void Run(string rootDirectory)
    {
        var fileTree = new FileTreeService(rootDirectory);
        var preview = new TextPreviewService();
        var launcher = new DefaultFileLauncher();

        using var application = Application.Create().Init();
        using var window = new ExplorerWindow(
            fileTree,
            preview,
            launcher,
            application.RequestStop);

        application.Run(window);
    }
}
