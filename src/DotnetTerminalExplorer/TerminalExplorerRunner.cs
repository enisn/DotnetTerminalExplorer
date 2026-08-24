using DotnetTerminalExplorer.Core;
using Terminal.Gui.App;

namespace DotnetTerminalExplorer;

internal static class TerminalExplorerRunner
{
    public static void Run(string rootDirectory)
    {
        var fileTree = new FileTreeService(rootDirectory);
        var fileService = new TextFileService();
        var launcher = new DefaultFileLauncher();
        var mutationService = new FileMutationService();

        using var application = Application.Create().Init();
        using var window = new ExplorerWindow(
            fileTree,
            fileService,
            launcher,
            application.RequestStop,
            mutationService);

        application.Run(window);
    }
}
