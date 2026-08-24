using DotnetTerminalExplorer.Core;
using Terminal.Gui.App;

namespace DotnetTerminalExplorer;

internal static class TerminalExplorerRunner
{
    public static void Run(string rootDirectory, int pageSize = FileTreeService.DefaultPageSize)
    {
        var fileTree = new FileTreeService(rootDirectory, pageSize);
        var fileService = new TextFileService();
        var launcher = new DefaultFileLauncher();
        var mutationService = new FileMutationService();

        using var application = Application.Create().Init();
        using var window = new ExplorerWindow(
            fileTree,
            fileService,
            launcher,
            application.RequestStop,
            mutationService,
            application: application);

        application.Run(window);
    }
}
