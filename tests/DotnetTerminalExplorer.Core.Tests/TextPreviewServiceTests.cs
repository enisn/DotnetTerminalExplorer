using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Core.Tests;

public sealed class TextPreviewServiceTests
{
    [Fact]
    public void Read_ReturnsUnicodeFileContent()
    {
        using var directory = new TemporaryDirectory();
        const string content = "Hello, dünya! 🌍 こんにちは";
        var path = directory.CreateFile("unicode.txt", content);
        var service = new TextPreviewService();

        var result = service.Read(File(path));

        Assert.Equal(TextPreviewKind.Content, result.Kind);
        Assert.Equal(content, result.Text);
    }

    [Fact]
    public void Read_ReturnsPlaceholderForDirectories()
    {
        using var directory = new TemporaryDirectory();
        var service = new TextPreviewService();
        var entry = new FileSystemEntry(
            directory.Path,
            "directory",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);

        var result = service.Read(entry);

        Assert.Equal(TextPreviewKind.Directory, result.Kind);
        Assert.Contains("Select a file", result.Text);
    }

    [Fact]
    public void Read_ReturnsDisplayableErrorForMissingFiles()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "missing.txt");
        var service = new TextPreviewService();

        var result = service.Read(File(path));

        Assert.Equal(TextPreviewKind.Error, result.Kind);
        Assert.Contains("missing.txt", result.Text);
    }

    [Fact]
    public void Read_ReturnsDisplayableErrorWhenReadingFails()
    {
        var service = new TextPreviewService(
            _ => throw new UnauthorizedAccessException("Access denied for test."));

        var result = service.Read(File("protected.txt"));

        Assert.Equal(TextPreviewKind.Error, result.Kind);
        Assert.Contains("protected.txt", result.Text);
        Assert.Contains("Access denied", result.Text);
    }

    private static FileSystemEntry File(string path) =>
        new(
            path,
            Path.GetFileName(path),
            FileSystemEntryKind.File,
            IsReparsePoint: false);
}
