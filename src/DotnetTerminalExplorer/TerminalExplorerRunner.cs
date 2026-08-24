using DotnetTerminalExplorer.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;

namespace DotnetTerminalExplorer;

internal static class TerminalExplorerRunner
{
    public static void Run(string rootDirectory, int pageSize = FileTreeService.DefaultPageSize)
    {
        var fileTree = new FileTreeService(rootDirectory, pageSize);
        var fileService = new TextFileService();
        var launcher = new DefaultFileLauncher();
        var mutationService = new FileMutationService();

        Driver.SizeDetection = SizeDetectionMode.Polling;

        using var application = Application.Create().Init();

        // In Polling mode Terminal.Gui initializes the output buffer to 0x0 and
        // SizeMonitorImpl swallows the initial size, so push it explicitly via ioctl.
        if (application.Driver?.GetOutput().GetSize() is { } size && size.Width > 0 && size.Height > 0)
        {
            application.Driver.SetScreenSize(size.Width, size.Height);
        }
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
