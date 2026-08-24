using System.Reflection;

namespace DotnetTerminalExplorer;

internal static class ProductInfo
{
    public const string Name = "Dotnet Terminal Explorer";
    public const string ExecutableName = "dte";
    public const string Description =
        "A lightweight, scoped file explorer for the terminal.";

    public static readonly string Version =
        typeof(ProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0]
        ?? typeof(ProductInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
