namespace DotnetTerminalExplorer.Core;

public interface ITextPreviewService
{
    TextPreview Read(FileSystemEntry entry, bool forceLoad = false);
}
