namespace DotnetTerminalExplorer.Core;

public enum EntrySizeState
{
    Pending,
    Ready,
    SkippedLink,
}

public readonly record struct EntrySizeInfo(
    long Bytes,
    EntrySizeState State,
    bool IsPartial = false)
{
    public static EntrySizeInfo Pending { get; } = new(0, EntrySizeState.Pending);

    public static EntrySizeInfo SkippedLink { get; } = new(0, EntrySizeState.SkippedLink);

    public static EntrySizeInfo Ready(long bytes, bool isPartial = false) =>
        new(bytes, EntrySizeState.Ready, isPartial);
}
