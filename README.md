# Dotnet Terminal Explorer

Dotnet Terminal Explorer (`dte`) is a lightweight terminal file explorer for .NET.

This repository currently contains the initial clean project architecture. The application behavior will be added incrementally.

## Projects

- `src/DotnetTerminalExplorer.Core` — UI-independent explorer logic.
- `src/DotnetTerminalExplorer` — the packable Terminal.Gui application.
- `tests/DotnetTerminalExplorer.Core.Tests` — Core unit tests.
- `tests/DotnetTerminalExplorer.Tests` — CLI and TUI tests.

## Build

```shell
dotnet restore
dotnet build --no-restore
```

