using DotnetTerminalExplorer.Core;

namespace DotnetTerminalExplorer.Core.Tests;

public sealed class FileMutationServiceTests
{
    [Fact]
    public void Rename_File_RenamesOnDiskAndReturnsUpdatedEntry()
    {
        using var directory = new TemporaryDirectory();
        var originalPath = directory.CreateFile("old.txt", "hello");
        var entry = new FileSystemEntry(
            originalPath,
            "old.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.Rename(entry, "new.txt");

        Assert.True(result.Success);
        Assert.NotNull(result.NewEntry);
        Assert.Equal("new.txt", result.NewEntry.Name);
        Assert.False(File.Exists(originalPath));
        Assert.True(File.Exists(result.NewEntry.FullPath));
        Assert.Equal("hello", File.ReadAllText(result.NewEntry.FullPath));
    }

    [Fact]
    public void Rename_Directory_RenamesOnDiskAndReturnsUpdatedEntry()
    {
        using var directory = new TemporaryDirectory();
        var subDir = directory.CreateDirectory("old-folder");
        File.WriteAllText(Path.Combine(subDir, "inner.txt"), "inner content");

        var entry = new FileSystemEntry(
            subDir,
            "old-folder",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.Rename(entry, "new-folder");

        Assert.True(result.Success);
        Assert.NotNull(result.NewEntry);
        Assert.Equal("new-folder", result.NewEntry.Name);
        Assert.False(Directory.Exists(subDir));
        Assert.True(Directory.Exists(result.NewEntry.FullPath));
        Assert.True(File.Exists(Path.Combine(result.NewEntry.FullPath, "inner.txt")));
    }

    [Fact]
    public void Rename_SameName_ReturnsSuccessWithoutMoving()
    {
        var entry = new FileSystemEntry(
            "/scope/file.txt",
            "file.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false);
        var moveCalled = false;
        var service = new FileMutationService(
            (_, _) => moveCalled = true,
            (_, _) => moveCalled = true,
            _ => false,
            _ => false);

        var result = service.Rename(entry, "file.txt");

        Assert.True(result.Success);
        Assert.Equal(entry, result.NewEntry);
        Assert.False(moveCalled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid/name")]
    [InlineData("invalid\\name")]
    public void Rename_InvalidName_ReturnsFailure(string invalidName)
    {
        var entry = new FileSystemEntry(
            "/scope/file.txt",
            "file.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.Rename(entry, invalidName);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Rename_AlreadyExistingTarget_ReturnsFailure()
    {
        using var directory = new TemporaryDirectory();
        var pathA = directory.CreateFile("a.txt", "a");
        directory.CreateFile("b.txt", "b");

        var entry = new FileSystemEntry(
            pathA,
            "a.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.Rename(entry, "b.txt");

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
        Assert.True(File.Exists(pathA));
    }

    [Fact]
    public void CreateFile_InDirectory_CreatesFileAndReturnsNewEntry()
    {
        using var directory = new TemporaryDirectory();
        var dirEntry = new FileSystemEntry(
            directory.Path,
            "root",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.CreateFile(dirEntry, "created.txt");

        Assert.True(result.Success);
        Assert.NotNull(result.NewEntry);
        Assert.Equal("created.txt", result.NewEntry.Name);
        Assert.True(File.Exists(result.NewEntry.FullPath));
        Assert.Equal(string.Empty, File.ReadAllText(result.NewEntry.FullPath));
    }

    [Fact]
    public void CreateFile_UsingFileEntry_CreatesFileInParentDirectory()
    {
        using var directory = new TemporaryDirectory();
        var existingFile = directory.CreateFile("existing.txt", "content");
        var fileEntry = new FileSystemEntry(
            existingFile,
            "existing.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.CreateFile(fileEntry, "sibling.txt");

        Assert.True(result.Success);
        Assert.NotNull(result.NewEntry);
        Assert.Equal("sibling.txt", result.NewEntry.Name);
        Assert.True(File.Exists(result.NewEntry.FullPath));
        Assert.Equal(directory.Path, Path.GetDirectoryName(result.NewEntry.FullPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid/name")]
    [InlineData("invalid\\name")]
    public void CreateFile_InvalidName_ReturnsFailure(string invalidName)
    {
        using var directory = new TemporaryDirectory();
        var dirEntry = new FileSystemEntry(
            directory.Path,
            "root",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.CreateFile(dirEntry, invalidName);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void CreateFile_AlreadyExistingTarget_ReturnsFailure()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("existing.txt", "content");
        var dirEntry = new FileSystemEntry(
            directory.Path,
            "root",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.CreateFile(dirEntry, "existing.txt");

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public void Delete_File_DeletesFromDisk()
    {
        using var directory = new TemporaryDirectory();
        var filePath = directory.CreateFile("to-delete.txt", "goodbye");
        var entry = new FileSystemEntry(
            filePath,
            "to-delete.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.Delete(entry);

        Assert.True(result.Success);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void Delete_Directory_DeletesRecursivelyFromDisk()
    {
        using var directory = new TemporaryDirectory();
        var subDir = directory.CreateDirectory("to-delete-folder");
        File.WriteAllText(Path.Combine(subDir, "inner.txt"), "inner");
        var entry = new FileSystemEntry(
            subDir,
            "to-delete-folder",
            FileSystemEntryKind.Directory,
            IsReparsePoint: false);
        var service = new FileMutationService();

        var result = service.Delete(entry);

        Assert.True(result.Success);
        Assert.False(Directory.Exists(subDir));
    }

    [Fact]
    public void Delete_ThrowsException_ReturnsFailure()
    {
        var entry = new FileSystemEntry(
            "/nonexistent/file.txt",
            "file.txt",
            FileSystemEntryKind.File,
            IsReparsePoint: false);
        var service = new FileMutationService(
            (_, _) => { },
            (_, _) => { },
            _ => false,
            _ => false,
            _ => { },
            _ => throw new IOException("Disk error"),
            _ => { });

        var result = service.Delete(entry);

        Assert.False(result.Success);
        Assert.Contains("Disk error", result.ErrorMessage);
    }
}
