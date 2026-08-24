namespace DotnetTerminalExplorer.Core;

public static class RootPathResolver
{
    public static string Resolve(string? directory, string? currentDirectory = null)
    {
        var baseDirectory = Path.GetFullPath(currentDirectory ?? Directory.GetCurrentDirectory());
        var fullPath = directory is null
            ? baseDirectory
            : Path.GetFullPath(directory, baseDirectory);

        fullPath = Path.TrimEndingDirectorySeparator(fullPath);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The path '{fullPath}' does not exist or is not a directory.");
        }

        return fullPath;
    }
}
