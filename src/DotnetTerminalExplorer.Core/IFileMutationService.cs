namespace DotnetTerminalExplorer.Core;

public interface IFileMutationService
{
    FileRenameResult Rename(FileSystemEntry entry, string newName);

    FileCreateResult CreateFile(FileSystemEntry parentDirectory, string fileName);

    FileDeleteResult Delete(FileSystemEntry entry);
}

