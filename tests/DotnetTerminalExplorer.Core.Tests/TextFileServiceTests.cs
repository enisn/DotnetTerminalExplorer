using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Core.Tests;

public sealed class TextFileServiceTests
{
    [Fact]
    public void ReadAndSave_WritesAndReadsFileContent()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.CreateFile("sample.txt", "Initial content");
        var entry = File(path);
        var service = new TextFileService();

        var saveResult = service.Save(entry, "Updated content 🎉");
        Assert.True(saveResult.Success);
        Assert.Null(saveResult.ErrorMessage);

        var readResult = service.Read(entry);
        Assert.Equal(TextPreviewKind.Content, readResult.Kind);
        Assert.Equal("Updated content 🎉", readResult.Text);
    }

    [Fact]
    public void Save_ReturnsFailureForDirectories()
    {
        using var directory = new TemporaryDirectory();
        var entry = new FileSystemEntry(
            directory.Path,
            "directory",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);
        var service = new TextFileService();

        var result = service.Save(entry, "some text");

        Assert.False(result.Success);
        Assert.Contains("Cannot save content to a directory", result.ErrorMessage);
    }

    [Fact]
    public void Save_ReturnsFailureWhenWriteThrows()
    {
        var service = new TextFileService(
            _ => "content",
            (_, _) => throw new UnauthorizedAccessException("Read-only file system."));
        var entry = File("/scope/file.txt");

        var result = service.Save(entry, "new text");

        Assert.False(result.Success);
        Assert.Contains("Unable to save", result.ErrorMessage);
        Assert.Contains("Read-only file system", result.ErrorMessage);
    }

    private static FileSystemEntry File(string path) =>
        new(
            path,
            Path.GetFileName(path),
            FileSystemEntryKind.File,
            IsReparsePoint: false);
}
