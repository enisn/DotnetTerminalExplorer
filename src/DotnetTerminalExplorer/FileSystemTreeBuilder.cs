using System.Security;
using DotnetTerminalExplorer.Core;
using Terminal.Gui.Views;

namespace DotnetTerminalExplorer;

internal sealed class FileSystemTreeBuilder(IFileTreeService fileTree)
    : TreeBuilder<FileSystemEntry>(supportsCanExpand: true)
{
    public override bool CanExpand(FileSystemEntry toExpand) =>
        fileTree.CanExpand(toExpand);

    public override IEnumerable<FileSystemEntry> GetChildren(FileSystemEntry forObject)
    {
        try
        {
            return fileTree.GetChildren(forObject);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return [];
        }
    }
}
