using CliFx;
using CliFx.Infrastructure;

namespace DotnetTerminalExplorer;

internal static class ExplorerCommandLine
{
    public static CommandLineApplication Create(
        Action<string> runExplorer,
        IConsole? console = null)
    {
        ArgumentNullException.ThrowIfNull(runExplorer);

        var builder = new CommandLineApplicationBuilder()
            .SetTitle(ProductInfo.Name)
            .SetExecutableName(ProductInfo.ExecutableName)
            .SetDescription(ProductInfo.Description)
            .SetVersion(ProductInfo.Version)
            .AddCommand(ExploreCommand.Descriptor)
            .UseTypeInstantiator(type => type switch
            {
                Type commandType when commandType == typeof(ExploreCommand) =>
                    new ExploreCommand(runExplorer),
                _ => throw new InvalidOperationException(
                    $"No command factory is registered for '{type.FullName}'."),
            });

        if (console is not null)
        {
            builder.UseConsole(console);
        }

        return builder.Build();
    }
}
