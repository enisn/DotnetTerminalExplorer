namespace DotnetTerminalExplorer.Core;

public sealed class FileMutationService : IFileMutationService
{
    private readonly Action<string, string> _moveFile;
    private readonly Action<string, string> _moveDirectory;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;

    public FileMutationService()
        : this(File.Move, Directory.Move, File.Exists, Directory.Exists)
    {
    }

    internal FileMutationService(
        Action<string, string> moveFile,
        Action<string, string> moveDirectory,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(moveFile);
        ArgumentNullException.ThrowIfNull(moveDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(directoryExists);

        _moveFile = moveFile;
        _moveDirectory = moveDirectory;
        _fileExists = fileExists;
        _directoryExists = directoryExists;
    }

    public FileRenameResult Rename(FileSystemEntry entry, string newName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(newName))
        {
            return FileRenameResult.Failed("Name cannot be empty or whitespace.");
        }

        var trimmedName = newName.Trim();

        if (string.Equals(entry.Name, trimmedName, StringComparison.Ordinal))
        {
            return FileRenameResult.Successful(entry);
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        if (trimmedName.IndexOfAny(invalidChars) >= 0 ||
            trimmedName.Contains(Path.DirectorySeparatorChar) ||
            trimmedName.Contains(Path.AltDirectorySeparatorChar))
        {
            return FileRenameResult.Failed($"'{trimmedName}' contains invalid characters.");
        }

        var parentDirectory = Path.GetDirectoryName(entry.FullPath);
        if (string.IsNullOrEmpty(parentDirectory))
        {
            return FileRenameResult.Failed("Cannot rename root entry.");
        }

        var newFullPath = Path.Combine(parentDirectory, trimmedName);

        if (_fileExists(newFullPath) || _directoryExists(newFullPath))
        {
            return FileRenameResult.Failed($"An item with name '{trimmedName}' already exists.");
        }

        try
        {
            if (entry.IsDirectory)
            {
                _moveDirectory(entry.FullPath, newFullPath);
            }
            else
            {
                _moveFile(entry.FullPath, newFullPath);
            }

            var updatedEntry = new FileSystemEntry(
                newFullPath,
                trimmedName,
                entry.Kind,
                entry.IsReparsePoint);

            return FileRenameResult.Successful(updatedEntry);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return FileRenameResult.Failed($"Unable to rename '{entry.Name}': {exception.Message}");
        }
    }
}
