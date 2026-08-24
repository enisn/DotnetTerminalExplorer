using SixLabors.ImageSharp;

namespace DotnetTerminalExplorer.Core;

internal static class FilePreviewHelper
{
    public static TextPreview ReadPreview(FileSystemEntry entry, Func<string, string> readAllText)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsDirectory)
        {
            return TextPreview.ForDirectory();
        }

        if (FileTypeClassifier.IsImageExtension(entry.FullPath))
        {
            try
            {
                if (File.Exists(entry.FullPath))
                {
                    var fileInfo = new FileInfo(entry.FullPath);
                    try
                    {
                        var imageInfo = Image.Identify(entry.FullPath);
                        if (imageInfo is not null)
                        {
                            var formatName = imageInfo.Metadata.DecodedImageFormat?.Name ?? Path.GetExtension(entry.FullPath).TrimStart('.').ToUpperInvariant();
                            var header = $"Format: {formatName} | Dimensions: {imageInfo.Width}x{imageInfo.Height} | Size: {FileTypeClassifier.FormatFileSize(fileInfo.Length)}";
                            return TextPreview.ForImage(header);
                        }
                    }
                    catch
                    {
                        // Fallback to basic file info if image header couldn't be parsed
                    }

                    return TextPreview.ForImage($"Image: {entry.Name} | Size: {FileTypeClassifier.FormatFileSize(fileInfo.Length)}");
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                return TextPreview.FromError($"Unable to preview '{entry.Name}': {exception.Message}");
            }
        }

        if (FileTypeClassifier.IsBinaryFile(entry.FullPath))
        {
            try
            {
                if (File.Exists(entry.FullPath))
                {
                    var fileInfo = new FileInfo(entry.FullPath);
                    var ext = fileInfo.Extension.Length > 1 ? fileInfo.Extension[1..].ToUpperInvariant() : "Binary";
                    var summary = $"""
                        [Binary File]

                        Name:          {entry.Name}
                        Size:          {FileTypeClassifier.FormatFileSize(fileInfo.Length)}
                        Type:          {ext} File
                        Last Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}

                        Press F8 to open with external application.
                        """;
                    return TextPreview.ForBinary(summary);
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                return TextPreview.FromError($"Unable to preview '{entry.Name}': {exception.Message}");
            }
        }

        try
        {
            return TextPreview.FromContent(readAllText(entry.FullPath));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return TextPreview.FromError(
                $"Unable to preview '{entry.Name}': {exception.Message}");
        }
    }
}
