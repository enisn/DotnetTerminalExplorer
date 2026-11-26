using System.Buffers.Binary;
using System.IO.Compression;
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
        WriteSolidPng(imgPath, width: 16, height: 32, r: 200, g: 100, b: 50, a: 255);

        var service = new TextPreviewService();
        var result = service.Read(Entry(imgPath));

        Assert.Equal(TextPreviewKind.Image, result.Kind);
        Assert.Contains("16x32", result.Text);
        Assert.Contains("PNG", result.Text);
    }

    [Fact]
    public void ImageDecoder_DecodeThumbnail_DecodesAndScalesPreservingAspectRatioAndColor()
    {
        using var directory = new TemporaryDirectory();
        var imgPath = Path.Combine(directory.Path, "banner.png");
        WriteSolidPng(imgPath, width: 40, height: 20, r: 64, g: 128, b: 192, a: 255);

        var thumb = ImageDecoder.DecodeThumbnail(imgPath, maxWidth: 10, maxHeight: 10);

        Assert.NotNull(thumb);
        Assert.Equal(10, thumb!.Width);
        Assert.Equal(5, thumb.Height);

        var pixel = thumb.GetPixel(5, 2);
        Assert.Equal((64, 128, 192, 255), pixel);
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

    private static void WriteSolidPng(string path, int width, int height, byte r, byte g, byte b, byte a)
    {
        using var output = File.Create(path);
        // PNG signature
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR chunk
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        WritePngChunk(output, "IHDR"u8, ihdr);

        // Raw scanlines (filter byte 0 + RGBA pixels per row)
        var raw = new byte[height * (1 + (width * 4))];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (1 + (width * 4));
            raw[rowStart] = 0;
            for (var x = 0; x < width; x++)
            {
                var px = rowStart + 1 + (x * 4);
                raw[px] = r;
                raw[px + 1] = g;
                raw[px + 2] = b;
                raw[px + 3] = a;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        WritePngChunk(output, "IDAT"u8, compressed.ToArray());
        WritePngChunk(output, "IEND"u8, []);
    }

    private static void WritePngChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, data.Length);
        output.Write(lengthBytes);
        output.Write(type);
        output.Write(data);

        var crc = ComputeCrc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in type)
        {
            crc = UpdateCrc(crc, b);
        }

        foreach (var b in data)
        {
            crc = UpdateCrc(crc, b);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return crc;
    }
}
