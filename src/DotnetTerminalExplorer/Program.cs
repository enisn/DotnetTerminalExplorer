namespace DotnetTerminalExplorer;

internal static class Program
{
    public static int Main(string[] args)
    {
        var application = ExplorerCommandLine.Create(TerminalExplorerRunner.Run);

        return application.RunAsync(args).GetAwaiter().GetResult();
    }
}
