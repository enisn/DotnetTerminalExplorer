namespace DotnetTerminalExplorer.Core;

public static class FileTypeClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".webp",
        ".ico",
        ".tiff",
        ".tif",
        ".tga",
        ".pbm",
        ".jfif",
    };

    private static readonly HashSet<string> KnownBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll",
        ".exe",
        ".so",
        ".dylib",
        ".bin",
        ".zip",
        ".tar",
        ".gz",
        ".7z",
        ".rar",
        ".pdf",
        ".iso",
        ".mp3",
        ".mp4",
        ".wav",
        ".avi",
        ".mov",
        ".mkv",
        ".class",
        ".pyc",
        ".o",
        ".obj",
        ".wasm",
        ".pdb",
        ".nupkg",
        ".snupkg",
    };

    public static bool IsImageExtension(string pathOrExtension)
    {
        var ext = GetExtension(pathOrExtension);
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    public static bool IsKnownBinaryExtension(string pathOrExtension)
    {
        var ext = GetExtension(pathOrExtension);
        return !string.IsNullOrEmpty(ext) && KnownBinaryExtensions.Contains(ext);
    }

    public static bool IsBinaryBuffer(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return false;
        }

        int controlCharCount = 0;
        int checkLength = Math.Min(buffer.Length, 8192);

        for (int i = 0; i < checkLength; i++)
        {
            byte b = buffer[i];

            // A null byte is an immediate binary indicator
            if (b == 0)
            {
                return true;
            }

            // Check non-printable control characters excluding common whitespace (\t=9, \n=10, \r=13, \b=8, \f=12)
            if (b < 32 && b != 9 && b != 10 && b != 13 && b != 8 && b != 12)
            {
                controlCharCount++;
            }
        }

        // If more than 20% control chars, treat as binary
        return (double)controlCharCount / checkLength > 0.20;
    }

    public static bool IsBinaryFile(string filePath, int maxBytesToRead = 8192)
    {
        if (IsKnownBinaryExtension(filePath))
        {
            return true;
        }

        if (IsImageExtension(filePath))
        {
            return false;
        }

        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            byte[] buffer = new byte[maxBytesToRead];
            int read = stream.Read(buffer, 0, buffer.Length);
            return IsBinaryBuffer(buffer.AsSpan(0, read));
        }
        catch
        {
            return false;
        }
    }

    public static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} B"
            : $"{size:0.##} {units[unitIndex]} ({bytes:N0} bytes)";
    }

    private static string GetExtension(string pathOrExtension)
    {
        return pathOrExtension.StartsWith('.') && !pathOrExtension.Contains(Path.DirectorySeparatorChar) && !pathOrExtension.Contains(Path.AltDirectorySeparatorChar)
            ? pathOrExtension
            : Path.GetExtension(pathOrExtension);
    }
}
