using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Core.Tests;

public sealed class FileTreeServiceTests
{
    [Fact]
    public void GetChildren_SortsDirectoriesBeforeFilesByName()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("beta.txt");
        directory.CreateDirectory("zulu");
        directory.CreateFile("alpha.txt");
        directory.CreateDirectory("echo");
        var service = new FileTreeService(directory.Path);

        var children = service.GetChildren(service.Root);

        Assert.Collection(
            children,
            entry => Assert.Equal("echo", entry.Name),
            entry => Assert.Equal("zulu", entry.Name),
            entry => Assert.Equal("alpha.txt", entry.Name),
            entry => Assert.Equal("beta.txt", entry.Name));
    }

    [Fact]
    public void GetChildren_IncludesHiddenEntries()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile(".hidden", "visible to dte");
        var service = new FileTreeService(directory.Path);

        var children = service.GetChildren(service.Root);

        Assert.Contains(children, entry => entry.Name == ".hidden");
    }

    [Fact]
    public void ConstructionAndRootAccess_DoNotEnumerateDirectories()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateDirectory("child/grandchild");
        var enumeratedPaths = new List<string>();
        var service = new FileTreeService(
            directory.Path,
            path =>
            {
                enumeratedPaths.Add(path);
                return Directory.EnumerateFileSystemEntries(path);
            },
            File.GetAttributes);

        _ = service.Root;

        Assert.Empty(enumeratedPaths);

        var children = service.GetChildren(service.Root);

        Assert.Single(enumeratedPaths);
        Assert.Equal(directory.Path, enumeratedPaths[0]);
        Assert.DoesNotContain(
            enumeratedPaths,
            path => path.EndsWith("grandchild", StringComparison.Ordinal));
        Assert.Single(children);
    }

    [Fact]
    public void ReparsePointDirectories_CannotBeExpanded()
    {
        using var scope = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        outside.CreateFile("outside.txt");
        var link = Path.Combine(scope.Path, "outside-link");

        try
        {
            Directory.CreateSymbolicLink(link, outside.Path);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
        {
            return;
        }

        var service = new FileTreeService(scope.Path);
        var linkEntry = Assert.Single(service.GetChildren(service.Root));

        Assert.True(linkEntry.IsDirectory);
        Assert.True(linkEntry.IsReparsePoint);
        Assert.False(service.CanExpand(linkEntry));
        Assert.Empty(service.GetChildren(linkEntry));
    }

    [Fact]
    public void GetChildren_RejectsDirectoriesOutsideTheRoot()
    {
        using var scope = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var service = new FileTreeService(scope.Path);
        var outsideEntry = new FileSystemEntry(
            outside.Path,
            "outside",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);

        Assert.Throws<InvalidOperationException>(() => service.GetChildren(outsideEntry));
    }
}
