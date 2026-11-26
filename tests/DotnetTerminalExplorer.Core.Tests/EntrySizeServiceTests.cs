using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Core.Tests;

public sealed class EntrySizeServiceTests
{
    [Fact]
    public async Task RequestSizes_CalculatesFileAndRecursiveDirectorySizesAsynchronously()
    {
        using var dir = new TemporaryDirectory();
        var fileA = dir.CreateFile("a.txt", new string('x', 120));
        dir.CreateFile("sub/nested1.bin", new string('y', 300));
        dir.CreateFile("sub/deep/nested2.bin", new string('z', 500));
        var subDirPath = Path.Combine(dir.Path, "sub");
        var deepDirPath = Path.Combine(subDirPath, "deep");

        using var service = new EntrySizeService(maxConcurrency: 2);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SizesUpdated += _ =>
        {
            if (service.TryGetSize(fileA, out var fi) && fi.State == EntrySizeState.Ready &&
                service.TryGetSize(subDirPath, out var di) && di.State == EntrySizeState.Ready)
            {
                done.TrySetResult();
            }
        };

        FileSystemEntry[] entries =
        [
            new FileSystemEntry(fileA, "a.txt", FileSystemEntryKind.File, IsReparsePoint: false),
            new FileSystemEntry(subDirPath, "sub", FileSystemEntryKind.Directory, IsReparsePoint: false),
        ];

        service.RequestSizes(entries);

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(service.TryGetSize(fileA, out var fileInfo));
        Assert.Equal(EntrySizeState.Ready, fileInfo.State);
        Assert.Equal(120, fileInfo.Bytes);

        Assert.True(service.TryGetSize(subDirPath, out var subInfo));
        Assert.Equal(EntrySizeState.Ready, subInfo.State);
        Assert.Equal(800, subInfo.Bytes);
        Assert.False(subInfo.IsPartial);

        // Bottom-up walk also memoizes visited subdirectories for instant expansion later.
        Assert.True(service.TryGetSize(deepDirPath, out var deepInfo));
        Assert.Equal(EntrySizeState.Ready, deepInfo.State);
        Assert.Equal(500, deepInfo.Bytes);
    }

    [Fact]
    public void RequestSizes_SkipsReparsePointDirectoriesImmediately()
    {
        using var scope = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        outside.CreateFile("huge.bin", new string('a', 4096));
        var linkPath = Path.Combine(scope.Path, "linked");

        try
        {
            Directory.CreateSymbolicLink(linkPath, outside.Path);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
        {
            return;
        }

        using var service = new EntrySizeService(maxConcurrency: 1);
        var linkEntry = new FileSystemEntry(linkPath, "linked", FileSystemEntryKind.Directory, IsReparsePoint: true);

        service.RequestSizes([linkEntry]);

        Assert.True(service.TryGetSize(linkPath, out var info));
        Assert.Equal(EntrySizeState.SkippedLink, info.State);
        Assert.Null(service.TryGetKnownBytes(linkPath));
    }

    [Fact]
    public void CalculateDirectorySize_SkipsNestedSymlinkDirectory()
    {
        using var scope = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        scope.CreateFile("sub/inside.txt", new string('a', 50));
        outside.CreateFile("outside.txt", new string('b', 5000));
        var subPath = Path.Combine(scope.Path, "sub");
        var linkPath = Path.Combine(subPath, "link-out");

        try
        {
            Directory.CreateSymbolicLink(linkPath, outside.Path);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or PlatformNotSupportedException)
        {
            return;
        }

        using var service = new EntrySizeService(maxConcurrency: 1);
        var result = service.CalculateDirectorySize(subPath, CancellationToken.None);

        Assert.Equal(EntrySizeState.Ready, result.State);
        Assert.Equal(50, result.Bytes);
        Assert.True(service.TryGetSize(linkPath, out var linkInfo));
        Assert.Equal(EntrySizeState.SkippedLink, linkInfo.State);
    }

    [Fact]
    public void InvalidatePathAndAncestors_ClearsTargetDescendantsAndAncestorsUpToRoot()
    {
        using var dir = new TemporaryDirectory();
        var fileA = dir.CreateFile("sub/deep/a.txt", "hello");
        var siblingDir = dir.CreateDirectory("other");
        dir.CreateFile("other/b.txt", "world");

        var subPath = Path.Combine(dir.Path, "sub");
        var deepPath = Path.Combine(subPath, "deep");

        using var service = new EntrySizeService(maxConcurrency: 1);
        _ = service.CalculateDirectorySize(dir.Path, CancellationToken.None);

        Assert.True(service.TryGetSize(dir.Path, out _));
        Assert.True(service.TryGetSize(subPath, out _));
        Assert.True(service.TryGetSize(deepPath, out _));
        Assert.True(service.TryGetSize(siblingDir, out _));

        service.InvalidatePathAndAncestors(fileA, dir.Path);

        Assert.False(service.TryGetSize(deepPath, out _));
        Assert.False(service.TryGetSize(subPath, out _));
        Assert.False(service.TryGetSize(dir.Path, out _));
        // Unrelated sibling stays cached.
        Assert.True(service.TryGetSize(siblingDir, out _));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(150 * 1024, "150 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(2L * 1024 * 1024 * 1024, "2.0 GB")]
    public void FormatCompactSize_FormatsCompactHumanReadableStrings(long bytes, string expected)
    {
        var actual = FileTypeClassifier.FormatCompactSize(bytes);
        Assert.Equal(expected, actual);
        Assert.True(actual.Length <= 7, $"Expected <= 7 chars, got '{actual}' ({actual.Length})");
    }
}
