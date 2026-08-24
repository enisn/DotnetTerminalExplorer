namespace DotnetTerminalExplorer.Core;

public sealed record FileCreateResult(
    bool Success,
    FileSystemEntry? NewEntry = null,
    string? ErrorMessage = null)
{
    public static FileCreateResult Successful(FileSystemEntry newEntry) => new(true, newEntry);

    public static FileCreateResult Failed(string errorMessage) => new(false, null, errorMessage);
}
