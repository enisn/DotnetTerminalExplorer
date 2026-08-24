using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer;

[Command(Description = "Explore a directory in the terminal.")]
internal sealed partial class ExploreCommand(Action<string, int> runExplorer) : ICommand
{
    private const int UsageErrorExitCode = 2;

    [CommandParameter(
        0,
        Name = "directory",
        Description = "Directory to explore. Defaults to the current working directory.")]
    public string? Directory { get; set; }

    [CommandOption(
        "page-size",
        Description = "Number of entries loaded per page when expanding large directories. Pass 0 to disable paging.")]
    public int PageSize { get; set; } = FileTreeService.DefaultPageSize;

    public ValueTask ExecuteAsync(IConsole console)
    {
        if (PageSize < 0)
        {
            throw new CommandException(
                "--page-size must be 0 or greater (0 disables paging).",
                UsageErrorExitCode);
        }

        string rootDirectory;

        try
        {
            rootDirectory = RootPathResolver.Resolve(Directory);
        }
        catch (Exception exception) when (exception is ArgumentException
            or DirectoryNotFoundException
            or NotSupportedException)
        {
            throw new CommandException(
                exception.Message,
                UsageErrorExitCode,
                innerException: exception);
        }

        runExplorer(rootDirectory, PageSize);
        return ValueTask.CompletedTask;
    }
}
