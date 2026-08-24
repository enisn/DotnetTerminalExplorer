using System.Text.RegularExpressions;

namespace DotnetTerminalExplorer.Core;

public sealed class GitIgnoreFilter
{
    private static readonly string[] DefaultIgnoredDirectories =
    [
        ".git",
        ".vs",
        ".idea",
        "node_modules",
        "bin",
        "obj",
        ".agents",
        ".codex"
    ];

    private readonly List<IgnoreRule> _rules = [];
    private readonly string _rootPath;

    public GitIgnoreFilter(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
        LoadRootGitIgnore();
    }

    private void LoadRootGitIgnore()
    {
        var gitIgnorePath = Path.Combine(_rootPath, ".gitignore");
        if (File.Exists(gitIgnorePath))
        {
            try
            {
                var lines = File.ReadAllLines(gitIgnorePath);
                AddRules(_rootPath, lines);
            }
            catch
            {
                // Fall back gracefully if cannot read .gitignore
            }
        }
    }

    public void AddRules(string directoryPath, IEnumerable<string> lines)
    {
        var normalizedDir = Path.GetFullPath(directoryPath);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var isNegated = line.StartsWith('!');
            if (isNegated)
            {
                line = line[1..].Trim();
            }

            var isDirectoryOnly = line.EndsWith('/');
            if (isDirectoryOnly)
            {
                line = line[..^1];
            }

            _rules.Add(new IgnoreRule(normalizedDir, line, isNegated, isDirectoryOnly));
        }
    }

    public bool IsIgnored(string fullPath, bool isDirectory)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        var fileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedPath));

        // Always skip common root metadata / cache directories
        if (isDirectory)
        {
            foreach (var defaultDir in DefaultIgnoredDirectories)
            {
                if (string.Equals(fileName, defaultDir, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        var relativePath = Path.GetRelativePath(_rootPath, normalizedPath).Replace('\\', '/');
        if (relativePath.StartsWith("./", StringComparison.Ordinal))
        {
            relativePath = relativePath[2..];
        }

        bool? isIgnored = null;

        for (int i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            if (rule.IsDirectoryOnly && !isDirectory)
            {
                continue;
            }

            if (rule.Matches(relativePath, fileName, isDirectory))
            {
                isIgnored = !rule.IsNegated;
            }
        }

        return isIgnored ?? false;
    }

    private sealed class IgnoreRule
    {
        private readonly string _baseDir;
        private readonly string _pattern;
        private readonly Regex _regex;

        public bool IsNegated { get; }
        public bool IsDirectoryOnly { get; }

        public IgnoreRule(string baseDir, string pattern, bool isNegated, bool isDirectoryOnly)
        {
            _baseDir = baseDir;
            _pattern = pattern;
            IsNegated = isNegated;
            IsDirectoryOnly = isDirectoryOnly;

            var regexPattern = GlobToRegex(pattern);
            _regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        public bool Matches(string relativePath, string fileName, bool isDirectory)
        {
            // Match whole path or just filename if pattern has no slash
            if (!_pattern.Contains('/'))
            {
                if (_regex.IsMatch(fileName))
                {
                    return true;
                }
            }

            return _regex.IsMatch(relativePath);
        }

        private static string GlobToRegex(string glob)
        {
            var escaped = Regex.Escape(glob)
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".");

            return $"^{escaped}(/.*)?$";
        }
    }
}
