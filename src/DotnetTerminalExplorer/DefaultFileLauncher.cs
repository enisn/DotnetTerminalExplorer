using System.Diagnostics;

namespace DotnetTerminalExplorer;

internal sealed class DefaultFileLauncher : IDefaultFileLauncher
{
    public void Launch(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _ = Process.Start(new ProcessStartInfo(filePath)
        {
            UseShellExecute = true,
        }) ?? throw new InvalidOperationException(
            $"The operating system did not start an application for '{filePath}'.");
    }
}
