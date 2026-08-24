namespace DotnetTerminalExplorer.Core.Tests;

public sealed class FastSearchEngineTests
{
    [Fact]
    public async Task SearchAsync_FindsContentMatchesInFiles()
    {
        using var tempDir = new TemporaryDirectory();
        tempDir.CreateFile("file1.txt", "hello world\nthis is a test\nanother line");
        tempDir.CreateFile("sub/file2.txt", "foo\nhello universe\nbar");
        tempDir.CreateFile("sub/file3.txt", "nothing here");

        var engine = new FastSearchEngine();
        var results = new List<SearchResult>();

        await foreach (var result in engine.SearchAsync(tempDir.Path, new SearchOptions
        {
            Query = "hello",
            Mode = SearchMode.Content,
        }))
        {
            results.Add(result);
        }

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Entry.Name == "file1.txt" && r.LineNumber == 1 && r.LineText.Contains("hello world"));
        Assert.Contains(results, r => r.Entry.Name == "file2.txt" && r.LineNumber == 2 && r.LineText.Contains("hello universe"));
    }

    [Fact]
    public async Task SearchAsync_CaseSensitiveAndInsensitive()
    {
        using var tempDir = new TemporaryDirectory();
        tempDir.CreateFile("test.txt", "HELLO world\nhello world\nHeLLo world");

        var engine = new FastSearchEngine();

        // Case insensitive (default)
        var insensitiveResults = new List<SearchResult>();
        await foreach (var r in engine.SearchAsync(tempDir.Path, new SearchOptions
        {
            Query = "hello",
            IsCaseSensitive = false,
        }))
        {
            insensitiveResults.Add(r);
        }
        Assert.Equal(3, insensitiveResults.Count);

        // Case sensitive
        var sensitiveResults = new List<SearchResult>();
        await foreach (var r in engine.SearchAsync(tempDir.Path, new SearchOptions
        {
            Query = "hello",
            IsCaseSensitive = true,
        }))
        {
            sensitiveResults.Add(r);
        }
        Assert.Single(sensitiveResults);
        Assert.Equal(2, sensitiveResults[0].LineNumber);
    }

    [Fact]
    public async Task SearchAsync_RegexMatching()
    {
        using var tempDir = new TemporaryDirectory();
        tempDir.CreateFile("test.txt", "var count = 123;\nvar name = 'dte';\nvar id = 456;");

        var engine = new FastSearchEngine();
        var results = new List<SearchResult>();

        await foreach (var r in engine.SearchAsync(tempDir.Path, new SearchOptions
        {
            Query = @"\d+",
            IsRegex = true,
        }))
        {
            results.Add(r);
        }

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.LineNumber == 1 && r.LineText.Contains("123"));
        Assert.Contains(results, r => r.LineNumber == 3 && r.LineText.Contains("456"));
    }

    [Fact]
    public async Task SearchAsync_SkipsBinaryFiles()
    {
        using var tempDir = new TemporaryDirectory();
        tempDir.CreateFile("text.txt", "secret keyword in text file");

        // Create a binary file containing null bytes and the keyword
        var binaryPath = Path.Combine(tempDir.Path, "data.bin");
        var binaryBytes = new byte[1024];
        Array.Fill<byte>(binaryBytes, 0);
        var keywordBytes = System.Text.Encoding.UTF8.GetBytes("secret keyword in binary");
        keywordBytes.CopyTo(binaryBytes, 10);
        File.WriteAllBytes(binaryPath, binaryBytes);

        var engine = new FastSearchEngine();
        var results = new List<SearchResult>();

        await foreach (var r in engine.SearchAsync(tempDir.Path, new SearchOptions
        {
            Query = "secret keyword",
        }))
        {
            results.Add(r);
        }

        Assert.Single(results);
        Assert.Equal("text.txt", results[0].Entry.Name);
    }

    [Fact]
    public async Task SearchAsync_RespectsGitIgnore()
    {
        using var tempDir = new TemporaryDirectory();
        tempDir.CreateFile(".gitignore", "ignored/\n*.log");
        tempDir.CreateFile("valid.txt", "target keyword");
        tempDir.CreateFile("ignored/test.txt", "target keyword");
        tempDir.CreateFile("error.log", "target keyword");

        var engine = new FastSearchEngine();
        var results = new List<SearchResult>();

        await foreach (var r in engine.SearchAsync(tempDir.Path, new SearchOptions
        {
            Query = "target keyword",
            RespectGitIgnore = true,
        }))
        {
            results.Add(r);
        }

        Assert.Single(results);
        Assert.Equal("valid.txt", results[0].Entry.Name);
    }

    [Fact]
    public async Task SearchAsync_FileNameMode()
    {
        using var tempDir = new TemporaryDirectory();
        tempDir.CreateFile("ExplorerWindow.cs", "class ExplorerWindow");
        tempDir.CreateFile("ExplorerCommandLine.cs", "class ExplorerCommandLine");
        tempDir.CreateFile("FileTreeService.cs", "class FileTreeService");

        var engine = new FastSearchEngine();
        var results = new List<SearchResult>();

        await foreach (var r in engine.SearchAsync(tempDir.Path, new SearchOptions
        {
            Query = "Explorer",
            Mode = SearchMode.FileName,
        }))
        {
            results.Add(r);
        }

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Entry.Name == "ExplorerWindow.cs");
        Assert.Contains(results, r => r.Entry.Name == "ExplorerCommandLine.cs");
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        using var tempDir = new TemporaryDirectory();
        for (int i = 0; i < 20; i++)
        {
            tempDir.CreateFile($"file{i}.txt", "common text");
        }

        var engine = new FastSearchEngine();
        var results = new List<SearchResult>();

        await foreach (var r in engine.SearchAsync(tempDir.Path, new SearchOptions
        {
            Query = "common text",
            MaxResults = 5,
        }))
        {
            results.Add(r);
        }

        Assert.Equal(5, results.Count);
    }
}
