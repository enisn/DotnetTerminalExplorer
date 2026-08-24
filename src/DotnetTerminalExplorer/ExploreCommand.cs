using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer;

[Command(Description = "Explore a directory in the terminal.")]
internal sealed partial class ExploreCommand(Action<string> runExplorer) : ICommand
{
    private const int UsageErrorExitCode = 2;

    [CommandParameter(
        0,
        Name = "directory",
        Description = "Directory to explore. Defaults to the current working directory.")]
    public string? Directory { get; set; }

    public ValueTask ExecuteAsync(IConsole console)
    {
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

        runExplorer(rootDirectory);
        return ValueTask.CompletedTask;
    }
}
