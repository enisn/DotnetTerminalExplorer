using System.Text;
using DotnetTerminalExplorer.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;
using TuiColor = Terminal.Gui.Drawing.Color;

namespace DotnetTerminalExplorer;

internal sealed class ImagePreviewView : View
{
    private static readonly TuiAttribute HeaderAttribute =
        new(ColorName16.Cyan, ColorName16.Black);

    private static readonly TuiAttribute DimHeaderAttribute =
        new(ColorName16.DarkGray, ColorName16.Black);

    private string? _filePath;
    private string? _headerInfo;
    private Image<Rgba32>? _cachedThumbnail;
    private int _cachedWidth;
    private int _cachedHeight;
    private int _decodeVersion;
    private string? _pendingDecodePath;
    private int _pendingDecodeWidth;
    private int _pendingDecodeHeight;

    public ImagePreviewView()
    {
        CanFocus = false;
        DrawingContent += (_, e) => DrawImageContent();
    }

    internal Action<Action>? UiInvoker { get; set; }

    public void SetImage(string? filePath, string? headerInfo)
    {
        if (_filePath == filePath && _headerInfo == headerInfo)
        {
            return;
        }

        _filePath = filePath;
        _headerInfo = headerInfo;
        _decodeVersion++;
        ClearCache();
        SetNeedsDraw();
    }

    public void Clear()
    {
        _filePath = null;
        _headerInfo = null;
        _decodeVersion++;
        ClearCache();
        SetNeedsDraw();
    }

    private bool DrawImageContent()
    {
        var viewport = Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return true;
        }

        // Draw header information
        if (!string.IsNullOrEmpty(_headerInfo))
        {
            SetAttribute(HeaderAttribute);
            Move(0, 0);
            var headerText = _headerInfo.Length > viewport.Width
                ? _headerInfo[..viewport.Width]
                : _headerInfo;
            AddStr(headerText);
        }

        if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
        {
            return true;
        }

        int availableWidth = viewport.Width;
        int availableHeight = Math.Max(0, viewport.Height - 2); // Leave 2 lines for header & spacing

        if (availableWidth <= 0 || availableHeight <= 0)
        {
            return true;
        }

        int targetPixelWidth = availableWidth;
        int targetPixelHeight = availableHeight * 2; // 2 vertical pixels per text cell

        EnsureThumbnail(targetPixelWidth, targetPixelHeight);

        if (_cachedThumbnail is null)
        {
            if (!string.IsNullOrEmpty(_filePath) && _pendingDecodePath == _filePath)
            {
                SetAttribute(DimHeaderAttribute);
                Move(0, 1);
                AddStr("Decoding image...");
            }

            return true;
        }

        int thumbWidth = _cachedThumbnail.Width;
        int thumbHeight = _cachedThumbnail.Height;
        int cellRows = (thumbHeight + 1) / 2;

        int offsetX = Math.Max(0, (availableWidth - thumbWidth) / 2);
        int offsetY = 2 + Math.Max(0, (availableHeight - cellRows) / 2);

        for (int y = 0; y < thumbHeight; y += 2)
        {
            int drawY = offsetY + (y / 2);
            if (drawY >= viewport.Height)
            {
                break;
            }

            for (int x = 0; x < thumbWidth; x++)
            {
                int drawX = offsetX + x;
                if (drawX >= viewport.Width)
                {
                    break;
                }

                var topPixel = _cachedThumbnail[x, y];
                var botPixel = (y + 1 < thumbHeight) ? _cachedThumbnail[x, y + 1] : topPixel;

                var fg = new TuiColor(topPixel.R, topPixel.G, topPixel.B);
                var bg = new TuiColor(botPixel.R, botPixel.G, botPixel.B);

                SetAttribute(new TuiAttribute(fg, bg));
                AddRune(drawX, drawY, (Rune)'▀');
            }
        }

        return true;
    }

    private void EnsureThumbnail(int targetPixelWidth, int targetPixelHeight)
    {
        if (_cachedThumbnail is not null && _cachedWidth == targetPixelWidth && _cachedHeight == targetPixelHeight)
        {
            return;
        }

        if (_pendingDecodePath == _filePath && _pendingDecodeWidth == targetPixelWidth && _pendingDecodeHeight == targetPixelHeight)
        {
            // A decode for this exact target is already in flight.
            return;
        }

        ClearCache();

        if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
        {
            return;
        }

        var version = ++_decodeVersion;
        var filePath = _filePath;
        _pendingDecodePath = filePath;
        _pendingDecodeWidth = targetPixelWidth;
        _pendingDecodeHeight = targetPixelHeight;

        if (UiInvoker is null)
        {
            // No UI loop is available (unit tests); decode synchronously.
            StoreThumbnail(version, filePath, DecodeThumbnail(filePath, targetPixelWidth, targetPixelHeight), targetPixelWidth, targetPixelHeight);
            return;
        }

        _ = DecodeThumbnailAsync(UiInvoker, filePath, targetPixelWidth, targetPixelHeight, version);
    }

    private async Task DecodeThumbnailAsync(Action<Action> invoker, string filePath, int targetPixelWidth, int targetPixelHeight, int version)
    {
        var thumbnail = await Task.Run(() => DecodeThumbnail(filePath, targetPixelWidth, targetPixelHeight));
        try
        {
            invoker(() => StoreThumbnail(version, filePath, thumbnail, targetPixelWidth, targetPixelHeight));
        }
        catch
        {
            // The application was shut down while decoding; dispose and ignore.
            thumbnail?.Dispose();
        }
    }

    private static Image<Rgba32>? DecodeThumbnail(string filePath, int targetPixelWidth, int targetPixelHeight)
    {
        try
        {
            using var original = Image.Load<Rgba32>(filePath);
            return original.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(targetPixelWidth, targetPixelHeight),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Bicubic,
            }));
        }
        catch
        {
            return null;
        }
    }

    private void StoreThumbnail(int version, string filePath, Image<Rgba32>? thumbnail, int targetPixelWidth, int targetPixelHeight)
    {
        if (version != _decodeVersion || _filePath != filePath)
        {
            // A newer selection or resize superseded this decode.
            thumbnail?.Dispose();
            return;
        }

        _pendingDecodePath = null;
        _pendingDecodeWidth = 0;
        _pendingDecodeHeight = 0;

        if (thumbnail is null)
        {
            return;
        }

        _cachedThumbnail = thumbnail;
        _cachedWidth = targetPixelWidth;
        _cachedHeight = targetPixelHeight;
        SetNeedsDraw();
    }

    private void ClearCache()
    {
        _cachedThumbnail?.Dispose();
        _cachedThumbnail = null;
        _cachedWidth = 0;
        _cachedHeight = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _decodeVersion++;
            ClearCache();
        }
        base.Dispose(disposing);
    }
}
