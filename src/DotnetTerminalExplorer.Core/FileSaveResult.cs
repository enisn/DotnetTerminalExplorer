namespace DotnetTerminalExplorer.Core;

public sealed record FileSaveResult(bool Success, string? ErrorMessage = null)
{
    public static FileSaveResult Successful() => new(true);

    public static FileSaveResult Failed(string errorMessage) =>
        new(false, errorMessage ?? throw new ArgumentNullException(nameof(errorMessage)));
}
