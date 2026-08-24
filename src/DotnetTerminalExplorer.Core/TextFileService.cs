namespace DotnetTerminalExplorer.Core;

public sealed class TextFileService : ITextFileService
{
    private readonly Func<string, string> _readAllText;
    private readonly Action<string, string> _writeAllText;

    public TextFileService()
        : this(File.ReadAllText, File.WriteAllText)
    {
    }

    internal TextFileService(
        Func<string, string> readAllText,
        Action<string, string> writeAllText)
    {
        ArgumentNullException.ThrowIfNull(readAllText);
        ArgumentNullException.ThrowIfNull(writeAllText);

        _readAllText = readAllText;
        _writeAllText = writeAllText;
    }

    public TextPreview Read(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsDirectory)
        {
            return TextPreview.ForDirectory();
        }

        try
        {
            return TextPreview.FromContent(_readAllText(entry.FullPath));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return TextPreview.FromError(
                $"Unable to preview '{entry.Name}': {exception.Message}");
        }
    }

    public FileSaveResult Save(FileSystemEntry entry, string content)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(content);

        if (entry.IsDirectory)
        {
            return FileSaveResult.Failed("Cannot save content to a directory.");
        }

        try
        {
            _writeAllText(entry.FullPath, content);
            return FileSaveResult.Successful();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return FileSaveResult.Failed(
                $"Unable to save '{entry.Name}': {exception.Message}");
        }
    }
}
