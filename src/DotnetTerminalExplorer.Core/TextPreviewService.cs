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

    public TextPreview Read(FileSystemEntry entry) =>
        FilePreviewHelper.ReadPreview(entry, _readAllText);
}
