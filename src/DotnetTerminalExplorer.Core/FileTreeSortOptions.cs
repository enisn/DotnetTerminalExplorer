namespace DotnetTerminalExplorer.Core;

public enum FileTreeSortField
{
    Name,
    Modified,
    Size,
}

public enum FileTreeSortDirection
{
    Ascending,
    Descending,
}

public sealed record FileTreeSortOptions(
    FileTreeSortField Field = FileTreeSortField.Name,
    FileTreeSortDirection Direction = FileTreeSortDirection.Ascending)
{
    public static readonly FileTreeSortOptions Default = new();

    public static FileTreeSortDirection GetDefaultDirection(FileTreeSortField field) =>
        field switch
        {
            FileTreeSortField.Name => FileTreeSortDirection.Ascending,
            FileTreeSortField.Modified => FileTreeSortDirection.Descending,
            FileTreeSortField.Size => FileTreeSortDirection.Descending,
            _ => FileTreeSortDirection.Ascending,
        };

    public string FormatShortLabel()
    {
        var fieldLabel = Field switch
        {
            FileTreeSortField.Name => "Name",
            FileTreeSortField.Modified => "Date",
            FileTreeSortField.Size => "Size",
            _ => "Name",
        };
        var arrow = Direction == FileTreeSortDirection.Ascending ? "↑" : "↓";
        return $"{fieldLabel} {arrow}";
    }
}
