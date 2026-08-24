using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Core.Tests;

public sealed class TextFileServiceTests
{
    [Fact]
    public void ReadAndSave_WritesAndReadsFileContent()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.CreateFile("sample.txt", "Initial content");
        var entry = Entry(path);
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
        var entry = Entry("/scope/file.txt");

        var result = service.Save(entry, "new text");

        Assert.False(result.Success);
        Assert.Contains("Unable to save", result.ErrorMessage);
        Assert.Contains("Read-only file system", result.ErrorMessage);
    }

    [Fact]
    public void Save_ReturnsFailureForBinaryAndImageFiles()
    {
        using var directory = new TemporaryDirectory();
        var binPath = Path.Combine(directory.Path, "app.bin");
        System.IO.File.WriteAllBytes(binPath, [0x00, 0x01]);
        var imgPath = Path.Combine(directory.Path, "pic.png");
        System.IO.File.WriteAllBytes(imgPath, [0x89, 0x50, 0x4E, 0x47]);

        var service = new TextFileService();

        var binResult = service.Save(Entry(binPath), "new text");
        Assert.False(binResult.Success);
        Assert.Contains("Cannot save text to a binary or image file", binResult.ErrorMessage);

        var imgResult = service.Save(Entry(imgPath), "new text");
        Assert.False(imgResult.Success);
        Assert.Contains("Cannot save text to a binary or image file", imgResult.ErrorMessage);
    }

    private static FileSystemEntry Entry(string path) =>
        new(
            path,
            Path.GetFileName(path),
            FileSystemEntryKind.File,
            IsReparsePoint: false);
}
