using StbImageSharp;

namespace DotnetTerminalExplorer.Core;

public readonly record struct ImageDimensions(int Width, int Height, string FormatName);

public sealed class RgbaImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public byte[] Pixels { get; } = pixels;

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        var offset = ((y * Width) + x) * 4;
        return (Pixels[offset], Pixels[offset + 1], Pixels[offset + 2], Pixels[offset + 3]);
    }
}

public static class ImageDecoder
{
    public static ImageDimensions? Identify(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        using var stream = File.OpenRead(filePath);
        var formatName = DetectFormatName(stream, filePath);
        stream.Position = 0;

        var info = ImageInfo.FromStream(stream);
        if (info is not { Width: > 0, Height: > 0 })
        {
            return null;
        }

        return new ImageDimensions(info.Value.Width, info.Value.Height, formatName);
    }

    public static RgbaImage? DecodeThumbnail(string filePath, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (maxWidth <= 0 || maxHeight <= 0)
        {
            return null;
        }

        using var stream = File.OpenRead(filePath);
        var decoded = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0 || decoded.Data is null)
        {
            return null;
        }

        var (targetWidth, targetHeight) = ComputeFittedSize(decoded.Width, decoded.Height, maxWidth, maxHeight);
        if (targetWidth == decoded.Width && targetHeight == decoded.Height)
        {
            return new RgbaImage(decoded.Width, decoded.Height, decoded.Data);
        }

        return ResizeAreaAverage(decoded.Data, decoded.Width, decoded.Height, targetWidth, targetHeight);
    }

    internal static (int Width, int Height) ComputeFittedSize(
        int sourceWidth,
        int sourceHeight,
        int maxWidth,
        int maxHeight)
    {
        if (sourceWidth <= maxWidth && sourceHeight <= maxHeight)
        {
            return (sourceWidth, sourceHeight);
        }

        var scaleX = (double)maxWidth / sourceWidth;
        var scaleY = (double)maxHeight / sourceHeight;
        var scale = Math.Min(scaleX, scaleY);

        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return (Math.Min(width, maxWidth), Math.Min(height, maxHeight));
    }

    internal static RgbaImage ResizeAreaAverage(
        byte[] src,
        int srcWidth,
        int srcHeight,
        int dstWidth,
        int dstHeight)
    {
        var dst = new byte[dstWidth * dstHeight * 4];
        var scaleX = (double)srcWidth / dstWidth;
        var scaleY = (double)srcHeight / dstHeight;

        for (var y = 0; y < dstHeight; y++)
        {
            var srcY0 = y * scaleY;
            var srcY1 = (y + 1) * scaleY;
            var yStart = Math.Clamp((int)Math.Floor(srcY0), 0, srcHeight - 1);
            var yEnd = Math.Clamp((int)Math.Ceiling(srcY1), yStart + 1, srcHeight);

            for (var x = 0; x < dstWidth; x++)
            {
                var srcX0 = x * scaleX;
                var srcX1 = (x + 1) * scaleX;
                var xStart = Math.Clamp((int)Math.Floor(srcX0), 0, srcWidth - 1);
                var xEnd = Math.Clamp((int)Math.Ceiling(srcX1), xStart + 1, srcWidth);

                double sumR = 0;
                double sumG = 0;
                double sumB = 0;
                double sumA = 0;
                double totalWeight = 0;

                for (var sy = yStart; sy < yEnd; sy++)
                {
                    var wy = Math.Min(sy + 1.0, srcY1) - Math.Max(sy, srcY0);
                    if (wy <= 0)
                    {
                        continue;
                    }

                    var rowOffset = sy * srcWidth * 4;
                    for (var sx = xStart; sx < xEnd; sx++)
                    {
                        var wx = Math.Min(sx + 1.0, srcX1) - Math.Max(sx, srcX0);
                        if (wx <= 0)
                        {
                            continue;
                        }

                        var weight = wx * wy;
                        var idx = rowOffset + (sx * 4);
                        sumR += src[idx] * weight;
                        sumG += src[idx + 1] * weight;
                        sumB += src[idx + 2] * weight;
                        sumA += src[idx + 3] * weight;
                        totalWeight += weight;
                    }
                }

                var dstIdx = ((y * dstWidth) + x) * 4;
                if (totalWeight > 0)
                {
                    dst[dstIdx] = (byte)Math.Clamp((int)Math.Round(sumR / totalWeight), 0, 255);
                    dst[dstIdx + 1] = (byte)Math.Clamp((int)Math.Round(sumG / totalWeight), 0, 255);
                    dst[dstIdx + 2] = (byte)Math.Clamp((int)Math.Round(sumB / totalWeight), 0, 255);
                    dst[dstIdx + 3] = (byte)Math.Clamp((int)Math.Round(sumA / totalWeight), 0, 255);
                }
            }
        }

        return new RgbaImage(dstWidth, dstHeight, dst);
    }

    private static string DetectFormatName(Stream stream, string filePath)
    {
        Span<byte> header = stackalloc byte[12];
        var read = stream.Read(header);

        if (read >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return "PNG";
        }

        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "JPEG";
        }

        if (read >= 6 &&
            header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
        {
            return "GIF";
        }

        if (read >= 2 && header[0] == 0x42 && header[1] == 0x4D)
        {
            return "BMP";
        }

        if (read >= 4 &&
            header[0] == 0x38 && header[1] == 0x42 && header[2] == 0x50 && header[3] == 0x53)
        {
            return "PSD";
        }

        var ext = Path.GetExtension(filePath).TrimStart('.');
        return string.IsNullOrEmpty(ext) ? "IMAGE" : ext.ToUpperInvariant();
    }
}
