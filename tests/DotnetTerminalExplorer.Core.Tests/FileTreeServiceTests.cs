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

    [Fact]
    public void GetChildren_SortsByNameDescending_KeepingDirectoriesFirst()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("alpha.txt");
        directory.CreateFile("beta.txt");
        directory.CreateDirectory("echo");
        directory.CreateDirectory("zulu");
        var service = new FileTreeService(directory.Path);

        var children = service.GetChildren(
            service.Root,
            new FileTreeSortOptions(FileTreeSortField.Name, FileTreeSortDirection.Descending));

        Assert.Equal(["zulu", "echo", "beta.txt", "alpha.txt"], children.Select(e => e.Name));
    }

    [Fact]
    public void GetChildren_SortsByModifiedDate_KeepingDirectoriesFirst()
    {
        using var directory = new TemporaryDirectory();
        var oldFile = directory.CreateFile("old.txt");
        var newFile = directory.CreateFile("new.txt");
        var oldDir = directory.CreateDirectory("old-dir");
        var newDir = directory.CreateDirectory("new-dir");

        var timestamps = new Dictionary<string, DateTime?>
        {
            [oldDir] = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            [newDir] = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            [oldFile] = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            [newFile] = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var service = new FileTreeService(
            directory.Path,
            Directory.EnumerateFileSystemEntries,
            File.GetAttributes,
            getLastWriteTimeUtc: p => timestamps.GetValueOrDefault(p));

        var newestFirst = service.GetChildren(
            service.Root,
            new FileTreeSortOptions(FileTreeSortField.Modified, FileTreeSortDirection.Descending));
        Assert.Equal(["new-dir", "old-dir", "new.txt", "old.txt"], newestFirst.Select(e => e.Name));

        var oldestFirst = service.GetChildren(
            service.Root,
            new FileTreeSortOptions(FileTreeSortField.Modified, FileTreeSortDirection.Ascending));
        Assert.Equal(["old-dir", "new-dir", "old.txt", "new.txt"], oldestFirst.Select(e => e.Name));
    }

    [Fact]
    public void GetChildren_SortsBySize_UsesProviderForDirsAndPlacesUnknownAfterKnown()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("small.txt", new string('a', 10));
        directory.CreateFile("large.txt", new string('b', 1000));
        var bigDir = directory.CreateDirectory("big-dir");
        var smallDir = directory.CreateDirectory("small-dir");
        directory.CreateDirectory("unknown-dir");

        var dirSizes = new Dictionary<string, long>
        {
            [bigDir] = 9000,
            [smallDir] = 100,
        };

        var service = new FileTreeService(directory.Path);

        var largestFirst = service.GetChildren(
            service.Root,
            new FileTreeSortOptions(FileTreeSortField.Size, FileTreeSortDirection.Descending),
            sizeProvider: e => dirSizes.TryGetValue(e.FullPath, out var bytes) ? bytes : null);

        Assert.Equal(
            ["big-dir", "small-dir", "unknown-dir", "large.txt", "small.txt"],
            largestFirst.Select(e => e.Name));

        var smallestFirst = service.GetChildren(
            service.Root,
            new FileTreeSortOptions(FileTreeSortField.Size, FileTreeSortDirection.Ascending),
            sizeProvider: e => dirSizes.TryGetValue(e.FullPath, out var bytes) ? bytes : null);

        Assert.Equal(
            ["small-dir", "big-dir", "unknown-dir", "small.txt", "large.txt"],
            smallestFirst.Select(e => e.Name));
    }

    [Fact]
    public void DefaultSort_DoesNotQueryLastWriteTimeOrFileSize()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt", "abc");
        directory.CreateDirectory("sub");
        var modifiedCalls = 0;
        var sizeCalls = 0;

        var service = new FileTreeService(
            directory.Path,
            Directory.EnumerateFileSystemEntries,
            File.GetAttributes,
            getLastWriteTimeUtc: _ => { modifiedCalls++; return DateTime.UtcNow; },
            getFileSize: _ => { sizeCalls++; return 1; });

        _ = service.GetChildrenPage(service.Root, skip: 0, FileTreeSortOptions.Default);

        Assert.Equal(0, modifiedCalls);
        Assert.Equal(0, sizeCalls);
    }
}
