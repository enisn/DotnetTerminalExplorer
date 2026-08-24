namespace DotnetTerminalExplorer.Core;

public sealed class TextPreviewService : ITextPreviewService
{
    private readonly Func<string, string> _readAllText;

    public TextPreviewService()
        : this(File.ReadAllText)
    {
    }

    internal TextPreviewService(Func<string, string> readAllText)
    {
        ArgumentNullException.ThrowIfNull(readAllText);
        _readAllText = readAllText;
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
}
