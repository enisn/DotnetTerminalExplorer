namespace DotnetTerminalExplorer.Core;

public sealed class FileMutationService : IFileMutationService
{
    private readonly Action<string, string> _moveFile;
    private readonly Action<string, string> _moveDirectory;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly Action<string> _createFile;
    private readonly Action<string> _deleteFile;
    private readonly Action<string> _deleteDirectory;

    public FileMutationService()
        : this(
            File.Move,
            Directory.Move,
            File.Exists,
            Directory.Exists,
            path => File.WriteAllText(path, string.Empty),
            File.Delete,
            path => Directory.Delete(path, recursive: true))
    {
    }

    internal FileMutationService(
        Action<string, string> moveFile,
        Action<string, string> moveDirectory,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        Action<string>? createFile = null,
        Action<string>? deleteFile = null,
        Action<string>? deleteDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(moveFile);
        ArgumentNullException.ThrowIfNull(moveDirectory);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(directoryExists);

        _moveFile = moveFile;
        _moveDirectory = moveDirectory;
        _fileExists = fileExists;
        _directoryExists = directoryExists;
        _createFile = createFile ?? (path => File.WriteAllText(path, string.Empty));
        _deleteFile = deleteFile ?? File.Delete;
        _deleteDirectory = deleteDirectory ?? (path => Directory.Delete(path, recursive: true));
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
            trimmedName.Contains('/') ||
            trimmedName.Contains('\\') ||
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

    public FileCreateResult CreateFile(FileSystemEntry parentDirectory, string fileName)
    {
        ArgumentNullException.ThrowIfNull(parentDirectory);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileCreateResult.Failed("Name cannot be empty or whitespace.");
        }

        var trimmedName = fileName.Trim();

        var invalidChars = Path.GetInvalidFileNameChars();
        if (trimmedName.IndexOfAny(invalidChars) >= 0 ||
            trimmedName.Contains('/') ||
            trimmedName.Contains('\\') ||
            trimmedName.Contains(Path.DirectorySeparatorChar) ||
            trimmedName.Contains(Path.AltDirectorySeparatorChar))
        {
            return FileCreateResult.Failed($"'{trimmedName}' contains invalid characters.");
        }

        var targetDir = parentDirectory.IsDirectory
            ? parentDirectory.FullPath
            : Path.GetDirectoryName(parentDirectory.FullPath);

        if (string.IsNullOrEmpty(targetDir) || !_directoryExists(targetDir))
        {
            return FileCreateResult.Failed("Target directory does not exist.");
        }

        var newFullPath = Path.Combine(targetDir, trimmedName);

        if (_fileExists(newFullPath) || _directoryExists(newFullPath))
        {
            return FileCreateResult.Failed($"An item with name '{trimmedName}' already exists.");
        }

        try
        {
            _createFile(newFullPath);

            var newEntry = new FileSystemEntry(
                newFullPath,
                trimmedName,
                FileSystemEntryKind.File,
                IsReparsePoint: false);

            return FileCreateResult.Successful(newEntry);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return FileCreateResult.Failed($"Unable to create '{trimmedName}': {exception.Message}");
        }
    }

    public FileDeleteResult Delete(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            if (entry.IsDirectory)
            {
                _deleteDirectory(entry.FullPath);
            }
            else
            {
                _deleteFile(entry.FullPath);
            }

            return FileDeleteResult.Successful();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return FileDeleteResult.Failed($"Unable to delete '{entry.Name}': {exception.Message}");
        }
    }
}
