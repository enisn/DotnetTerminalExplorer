namespace DotnetTerminalExplorer.Core;

public sealed record FileRenameResult(
    bool Success,
    FileSystemEntry? NewEntry = null,
    string? ErrorMessage = null)
{
    public static FileRenameResult Successful(FileSystemEntry newEntry) =>
        new(true, newEntry ?? throw new ArgumentNullException(nameof(newEntry)));

    public static FileRenameResult Failed(string errorMessage) =>
        new(false, null, errorMessage ?? throw new ArgumentNullException(nameof(errorMessage)));
}
