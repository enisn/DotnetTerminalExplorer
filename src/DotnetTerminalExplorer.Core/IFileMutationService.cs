namespace DotnetTerminalExplorer.Core;

public interface IFileMutationService
{
    FileRenameResult Rename(FileSystemEntry entry, string newName);
}
