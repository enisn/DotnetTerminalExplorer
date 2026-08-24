namespace DotnetTerminalExplorer.Core;

public sealed class TextFileService : ITextFileService
{
    private readonly Func<string, string> _readAllText;
    private readonly Action<string, string> _writeAllText;
    private readonly long _maxPreviewBytes;

    public TextFileService()
        : this(File.ReadAllText, File.WriteAllText)
    {
    }

    internal TextFileService(
        Func<string, string> readAllText,
        Action<string, string> writeAllText,
        long maxPreviewBytes = FilePreviewHelper.DefaultMaxPreviewBytes)
    {
        ArgumentNullException.ThrowIfNull(readAllText);
        ArgumentNullException.ThrowIfNull(writeAllText);

        _readAllText = readAllText;
        _writeAllText = writeAllText;
        _maxPreviewBytes = maxPreviewBytes;
    }

    public TextPreview Read(FileSystemEntry entry, bool forceLoad = false) =>
        FilePreviewHelper.ReadPreview(entry, _readAllText, forceLoad, _maxPreviewBytes);

    public FileSaveResult Save(FileSystemEntry entry, string content)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(content);

        if (entry.IsDirectory)
        {
            return FileSaveResult.Failed("Cannot save content to a directory.");
        }

        if (FileTypeClassifier.IsImageExtension(entry.FullPath) || FileTypeClassifier.IsBinaryFile(entry.FullPath))
        {
            return FileSaveResult.Failed("Cannot save text to a binary or image file.");
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
