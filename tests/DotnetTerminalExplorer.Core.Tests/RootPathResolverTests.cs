using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Core.Tests;

public sealed class RootPathResolverTests
{
    [Fact]
    public void Resolve_UsesCurrentDirectoryWhenPathIsOmitted()
    {
        using var directory = new TemporaryDirectory();

        var result = RootPathResolver.Resolve(null, directory.Path);

        Assert.Equal(Path.GetFullPath(directory.Path), result);
    }

    [Fact]
    public void Resolve_NormalizesRelativePathsAndTrailingSeparators()
    {
        using var directory = new TemporaryDirectory();
        var child = directory.CreateDirectory("child");

        var result = RootPathResolver.Resolve($"child{Path.DirectorySeparatorChar}", directory.Path);

        Assert.Equal(Path.GetFullPath(child), result);
    }

    [Fact]
    public void Resolve_RejectsMissingDirectories()
    {
        using var directory = new TemporaryDirectory();

        var exception = Assert.Throws<DirectoryNotFoundException>(
            () => RootPathResolver.Resolve("missing", directory.Path));

        Assert.Contains("does not exist or is not a directory", exception.Message);
    }

    [Fact]
    public void Resolve_RejectsFiles()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.CreateFile("file.txt");

        Assert.Throws<DirectoryNotFoundException>(() => RootPathResolver.Resolve(file));
    }
}
