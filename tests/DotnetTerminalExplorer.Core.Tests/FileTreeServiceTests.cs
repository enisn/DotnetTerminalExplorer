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
    public void GetChildrenPage_ReturnsSortedFirstPageAndReportsMore()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateDirectory("zulu");
        directory.CreateFile("beta.txt");
        directory.CreateFile("alpha.txt");
        var service = new FileTreeService(directory.Path, pageSize: 2);

        var first = service.GetChildrenPage(service.Root, skip: 0);

        Assert.True(first.HasMore);
        Assert.Equal(["zulu", "alpha.txt"], first.Entries.Select(entry => entry.Name));

        var second = service.GetChildrenPage(service.Root, skip: first.Entries.Count);

        Assert.False(second.HasMore);
        Assert.Equal(["beta.txt"], second.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void GetChildrenPage_PagesStayConsistentRegardlessOfEnumerationOrder()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateDirectory("zulu");
        directory.CreateDirectory("alpha");
        directory.CreateFile("beta.txt");
        directory.CreateFile("gamma.txt");
        directory.CreateFile("delta.txt");

        // Force the worst case: enumeration order is the exact reverse of the
        // sorted order, so paging before sorting would duplicate/miss entries.
        var reversed = Directory
            .EnumerateFileSystemEntries(directory.Path)
            .OrderDescending(StringComparer.Ordinal)
            .ToArray();
        var service = new FileTreeService(
            directory.Path,
            _ => reversed,
            File.GetAttributes,
            pageSize: 2);

        var pages = new List<string>();
        FileTreePage page;
        var skip = 0;
        do
        {
            page = service.GetChildrenPage(service.Root, skip);
            pages.AddRange(page.Entries.Select(entry => entry.Name));
            skip += page.Entries.Count;
        }
        while (page.HasMore);

        Assert.Equal(["alpha", "zulu", "beta.txt", "delta.txt", "gamma.txt"], pages);
    }

    [Fact]
    public void GetChildrenPage_ReturnsFullListingWhenItFitsInOnePage()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("solo.txt");
        var service = new FileTreeService(directory.Path, pageSize: 500);

        var page = service.GetChildrenPage(service.Root, skip: 0);

        Assert.False(page.HasMore);
        Assert.Equal(["solo.txt"], page.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public void GetChildrenPage_SkipBeyondEnd_ReturnsEmptyPage()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("solo.txt");
        var service = new FileTreeService(directory.Path, pageSize: 500);

        var page = service.GetChildrenPage(service.Root, skip: 10);

        Assert.False(page.HasMore);
        Assert.Empty(page.Entries);
    }

    [Fact]
    public void GetChildrenPage_ReturnsEmptyPageForFiles()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.CreateFile("plain.txt");
        var service = new FileTreeService(directory.Path);
        var fileEntry = new FileSystemEntry(path, "plain.txt", FileSystemEntryKind.File, IsReparsePoint: false);

        var page = service.GetChildrenPage(fileEntry, skip: 0);

        Assert.False(page.HasMore);
        Assert.Empty(page.Entries);
    }

    [Fact]
    public void GetChildrenPage_RejectsDirectoriesOutsideTheRoot()
    {
        using var scope = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var service = new FileTreeService(scope.Path);
        var outsideEntry = new FileSystemEntry(
            outside.Path,
            "outside",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);

        Assert.Throws<InvalidOperationException>(() => service.GetChildrenPage(outsideEntry, skip: 0));
    }

    [Fact]
    public void Constructor_RejectsNegativePageSize()
    {
        using var directory = new TemporaryDirectory();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FileTreeService(directory.Path, pageSize: -1));
    }

    [Fact]
    public void PageSizeZero_DisablesPagingEntirely()
    {
        using var directory = new TemporaryDirectory();
        for (var i = 0; i < 3; i++)
        {
            directory.CreateFile($"file{i}.txt");
        }
        var service = new FileTreeService(directory.Path, pageSize: 0);

        var page = service.GetChildrenPage(service.Root, skip: 0);

        Assert.False(page.HasMore);
        Assert.Equal(3, page.Entries.Count);
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
