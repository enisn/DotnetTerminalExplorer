namespace DotnetTerminalExplorer.Core;

public sealed record FileDeleteResult(
    bool Success,
    string? ErrorMessage = null)
{
    public static FileDeleteResult Successful() => new(true);

    public static FileDeleteResult Failed(string errorMessage) => new(false, errorMessage);
}
