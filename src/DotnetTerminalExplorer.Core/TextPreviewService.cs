namespace DotnetTerminalExplorer.Core;

public sealed class TextPreviewService : ITextPreviewService
{
    private readonly Func<string, string> _readAllText;
    private readonly long _maxPreviewBytes;

    public TextPreviewService()
        : this(File.ReadAllText)
    {
    }

    internal TextPreviewService(
        Func<string, string> readAllText,
        long maxPreviewBytes = FilePreviewHelper.DefaultMaxPreviewBytes)
    {
        ArgumentNullException.ThrowIfNull(readAllText);
        _readAllText = readAllText;
        _maxPreviewBytes = maxPreviewBytes;
    }

    public TextPreview Read(FileSystemEntry entry, bool forceLoad = false) =>
        FilePreviewHelper.ReadPreview(entry, _readAllText, forceLoad, _maxPreviewBytes);
}
