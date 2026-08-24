using DotnetTerminalExplorer.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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

        var result = service.Read(Entry(path));

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

        var result = service.Read(Entry(path));

        Assert.Equal(TextPreviewKind.Error, result.Kind);
        Assert.Contains("missing.txt", result.Text);
    }

    [Fact]
    public void Read_ReturnsDisplayableErrorWhenReadingFails()
    {
        var service = new TextPreviewService(
            _ => throw new UnauthorizedAccessException("Access denied for test."));

        var result = service.Read(Entry("protected.txt"));

        Assert.Equal(TextPreviewKind.Error, result.Kind);
        Assert.Contains("protected.txt", result.Text);
        Assert.Contains("Access denied", result.Text);
    }

    [Fact]
    public void Read_ReturnsBinaryInfoForBinaryFiles()
    {
        using var directory = new TemporaryDirectory();
        var binPath = Path.Combine(directory.Path, "program.bin");
        System.IO.File.WriteAllBytes(binPath, [0x00, 0x01, 0x02, 0x03]);
        var service = new TextPreviewService();

        var result = service.Read(Entry(binPath));

        Assert.Equal(TextPreviewKind.Binary, result.Kind);
        Assert.Contains("[Binary File]", result.Text);
        Assert.Contains("program.bin", result.Text);
        Assert.Contains("F8", result.Text);
        Assert.Contains("Ctrl+L", result.Text);
    }

    [Fact]
    public void Read_LoadsBinaryFileWhenForced()
    {
        using var directory = new TemporaryDirectory();
        var binPath = Path.Combine(directory.Path, "program.bin");
        System.IO.File.WriteAllBytes(binPath, [0x00, 0x01, 0x02, 0x03]);
        var service = new TextPreviewService(_ => "raw binary text content");

        var result = service.Read(Entry(binPath), forceLoad: true);

        Assert.Equal(TextPreviewKind.Content, result.Kind);
        Assert.Equal("raw binary text content", result.Text);
    }

    [Fact]
    public void Read_ReturnsImageInfoForImageFiles()
    {
        using var directory = new TemporaryDirectory();
        var imgPath = Path.Combine(directory.Path, "test.png");
        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(16, 32))
        {
            img.SaveAsPng(imgPath);
        }

        var service = new TextPreviewService();
        var result = service.Read(Entry(imgPath));

        Assert.Equal(TextPreviewKind.Image, result.Kind);
        Assert.Contains("16x32", result.Text);
        Assert.Contains("PNG", result.Text);
    }

    [Fact]
    public void Read_SkipsLargeFilesWithTooLargePreview()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.CreateFile("large.txt", new string('x', 100));
        var readCalls = 0;
        var service = new TextPreviewService(
            _ =>
            {
                readCalls++;
                return "content";
            },
            maxPreviewBytes: 10);

        var result = service.Read(Entry(path));

        Assert.Equal(TextPreviewKind.TooLarge, result.Kind);
        Assert.Contains("too large", result.Text);
        Assert.Contains("large.txt", result.Text);
        Assert.Contains("Ctrl+L", result.Text);
        Assert.Equal(0, readCalls);
    }

    [Fact]
    public void Read_LoadsLargeFileWhenForced()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.CreateFile("large.txt", new string('x', 100));
        var service = new TextPreviewService(_ => "forced content", maxPreviewBytes: 10);

        var result = service.Read(Entry(path), forceLoad: true);

        Assert.Equal(TextPreviewKind.Content, result.Kind);
        Assert.Equal("forced content", result.Text);
    }

    [Fact]
    public void Read_LoadsFilesAtTheSizeLimit()
    {
        using var directory = new TemporaryDirectory();
        const string content = "0123456789";
        var path = directory.CreateFile("boundary.txt", content);
        var service = new TextPreviewService(File.ReadAllText, maxPreviewBytes: content.Length);

        var result = service.Read(Entry(path));

        Assert.Equal(TextPreviewKind.Content, result.Kind);
        Assert.Equal(content, result.Text);
    }

    private static FileSystemEntry Entry(string path) =>
        new(
            path,
            Path.GetFileName(path),
            FileSystemEntryKind.File,
            IsReparsePoint: false);
}
