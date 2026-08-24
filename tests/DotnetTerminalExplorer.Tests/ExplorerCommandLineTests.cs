using CliFx.Infrastructure;

namespace DotnetTerminalExplorer.Tests;

public sealed class ExplorerCommandLineTests
{
    [Fact]
    public async Task NoArgument_UsesCurrentDirectory()
    {
        using var directory = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        var previousDirectory = Directory.GetCurrentDirectory();
        string? exploredDirectory = null;

        try
        {
            Directory.SetCurrentDirectory(directory.Path);
            var application = ExplorerCommandLine.Create(
                (path, _) => exploredDirectory = path,
                console);

            var exitCode = await application.RunAsync([]);

            Assert.Equal(0, exitCode);
            Assert.Equal(directory.Path, exploredDirectory);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    [Fact]
    public async Task PositionalDirectory_IsPassedToExplorerRunner()
    {
        using var directory = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        string? exploredDirectory = null;
        var application = ExplorerCommandLine.Create(
            (path, _) => exploredDirectory = path,
            console);

        var exitCode = await application.RunAsync([directory.Path]);

        Assert.Equal(0, exitCode);
        Assert.Equal(directory.Path, exploredDirectory);
    }

    [Fact]
    public async Task PageSizeOption_IsForwardedToExplorerRunner()
    {
        using var directory = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        string? exploredDirectory = null;
        var exploredPageSize = 0;
        var application = ExplorerCommandLine.Create(
            (path, pageSize) => (exploredDirectory, exploredPageSize) = (path, pageSize),
            console);

        var exitCode = await application.RunAsync([directory.Path, "--page-size", "200"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(directory.Path, exploredDirectory);
        Assert.Equal(200, exploredPageSize);
    }

    [Fact]
    public async Task PageSizeZero_IsForwardedAndDisablesPaging()
    {
        using var directory = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        var exploredPageSize = -1;
        var application = ExplorerCommandLine.Create(
            (_, pageSize) => exploredPageSize = pageSize,
            console);

        var exitCode = await application.RunAsync([directory.Path, "--page-size", "0"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, exploredPageSize);
    }

    [Fact]
    public async Task PageSizeDefaultsToFiveHundred()
    {
        using var directory = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        var exploredPageSize = 0;
        var application = ExplorerCommandLine.Create(
            (_, pageSize) => exploredPageSize = pageSize,
            console);

        var exitCode = await application.RunAsync([directory.Path]);

        Assert.Equal(0, exitCode);
        Assert.Equal(500, exploredPageSize);
    }

    [Theory]
    [InlineData("--page-size", "-1")]
    [InlineData("--page-size", "not-a-number")]
    public async Task InvalidPageSize_ReturnsError(params string[] option)
    {
        using var console = new FakeInMemoryConsole();
        var application = ExplorerCommandLine.Create((_, _) => { }, console);

        var exitCode = await application.RunAsync([.. option]);

        Assert.True(exitCode is 1 or 2);
        Assert.NotEmpty(console.ReadErrorString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    public async Task MetadataOptions_DoNotInvokeExplorerRunner(string option)
    {
        using var console = new FakeInMemoryConsole();
        var runnerInvocations = 0;
        var application = ExplorerCommandLine.Create(
            (_, _) => runnerInvocations++,
            console);

        var exitCode = await application.RunAsync([option]);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, runnerInvocations);
    }

    [Fact]
    public async Task InvalidDirectory_ReturnsUsageExitCode()
    {
        using var console = new FakeInMemoryConsole();
        var application = ExplorerCommandLine.Create((_, _) => { }, console);
        var missing = Path.Combine(Path.GetTempPath(), $"dte-missing-{Guid.NewGuid():N}");

        var exitCode = await application.RunAsync([missing]);

        Assert.Equal(2, exitCode);
        Assert.Contains("does not exist or is not a directory", console.ReadErrorString());
    }

    [Fact]
    public async Task FilePath_ReturnsUsageExitCode()
    {
        using var directory = new TemporaryDirectory();
        using var console = new FakeInMemoryConsole();
        var file = directory.CreateFile("file.txt", "content");
        var application = ExplorerCommandLine.Create((_, _) => { }, console);

        var exitCode = await application.RunAsync([file]);

        Assert.Equal(2, exitCode);
        Assert.Contains("does not exist or is not a directory", console.ReadErrorString());
    }

    [Theory]
    [InlineData("one", "two")]
    [InlineData("--unknown")]
    public async Task InvalidSyntax_ReturnsCliFxError(params string[] arguments)
    {
        using var console = new FakeInMemoryConsole();
        var application = ExplorerCommandLine.Create((_, _) => { }, console);

        var exitCode = await application.RunAsync(arguments);

        Assert.Equal(1, exitCode);
        Assert.NotEmpty(console.ReadErrorString());
    }
}
