using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Core.Tests;

public sealed class FileTypeClassifierTests
{
    [Theory]
    [InlineData(".png", true)]
    [InlineData(".jpg", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".webp", true)]
    [InlineData(".gif", true)]
    [InlineData(".bmp", true)]
    [InlineData(".ico", true)]
    [InlineData("photo.PNG", true)]
    [InlineData("/path/to/image.jpg", true)]
    [InlineData(".txt", false)]
    [InlineData("file.cs", false)]
    [InlineData("program.exe", false)]
    public void IsImageExtension_IdentifiesImageExtensions(string path, bool expected)
    {
        Assert.Equal(expected, FileTypeClassifier.IsImageExtension(path));
    }

    [Theory]
    [InlineData(".dll", true)]
    [InlineData(".exe", true)]
    [InlineData(".so", true)]
    [InlineData(".dylib", true)]
    [InlineData(".bin", true)]
    [InlineData(".zip", true)]
    [InlineData("archive.tar.gz", true)]
    [InlineData(".txt", false)]
    [InlineData(".cs", false)]
    [InlineData(".md", false)]
    public void IsKnownBinaryExtension_IdentifiesKnownBinaryExtensions(string path, bool expected)
    {
        Assert.Equal(expected, FileTypeClassifier.IsKnownBinaryExtension(path));
    }

    [Fact]
    public void IsBinaryBuffer_DetectsNullBytes()
    {
        byte[] textBuffer = "Hello World"u8.ToArray();
        Assert.False(FileTypeClassifier.IsBinaryBuffer(textBuffer));

        byte[] binaryBuffer = [0x48, 0x65, 0x00, 0x6C, 0x6F];
        Assert.True(FileTypeClassifier.IsBinaryBuffer(binaryBuffer));
    }

    [Fact]
    public void IsBinaryFile_ReturnsTrueForBinaryFileAndFalseForText()
    {
        using var directory = new TemporaryDirectory();
        var textPath = directory.CreateFile("doc.txt", "This is plain text content.");
        var binPath = Path.Combine(directory.Path, "raw.dat");
        File.WriteAllBytes(binPath, [0x00, 0x01, 0x02, 0x03, 0xFF]);

        Assert.False(FileTypeClassifier.IsBinaryFile(textPath));
        Assert.True(FileTypeClassifier.IsBinaryFile(binPath));
    }

    [Theory]
    [InlineData(100, "100 B")]
    [InlineData(1024, "1 KB (1,024 bytes)")]
    [InlineData(1536, "1.5 KB (1,536 bytes)")]
    [InlineData(1048576, "1 MB (1,048,576 bytes)")]
    public void FormatFileSize_FormatsUnitsCorrectly(long bytes, string expected)
    {
        Assert.Equal(expected, FileTypeClassifier.FormatFileSize(bytes));
    }
}
