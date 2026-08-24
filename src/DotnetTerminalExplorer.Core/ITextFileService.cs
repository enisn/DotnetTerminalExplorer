namespace DotnetTerminalExplorer.Core;

public interface ITextFileService : ITextPreviewService
{
    FileSaveResult Save(FileSystemEntry entry, string content);
}
