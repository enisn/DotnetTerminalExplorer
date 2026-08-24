# Dotnet Terminal Explorer

Dotnet Terminal Explorer (`dte`) is a deliberately lightweight, scoped file explorer for the terminal. It opens a filesystem tree on the left and a read-only text preview on the right.

![Dotnet Terminal Explorer showing the filesystem tree and text preview](docs/images/dotnet-terminal-explorer.png)

`dte` explores only the directory passed to it, similar to opening a folder with `code`. If the directory is omitted, it uses the current working directory.

## Requirements

- .NET 10 SDK to build the repository.
- .NET 10 runtime to run the framework-dependent tool.

## Install

```shell
dotnet tool install --global DotnetTerminalExplorer
```

Run it for the current directory or a specific scoped folder:

```shell
dte
dte ./src
dte /path/to/project
```

Use `dte --help` or `dte --version` without initializing Terminal.Gui or filesystem services.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `F5` | Reload the selected file preview |
| `F8` | Open the selected file with the operating system's default application |
| `Esc` | Quit |

`F8` is disabled for directories. There is no internal editor; after editing externally, press `F5` to reload the preview.

## Initial behavior

- Directories are listed before files, and hidden entries are included.
- Only the root's immediate children are enumerated at startup. Descendants are read when their directory is expanded.
- Directory symlinks and reparse points inside the selected scope are shown but are not expanded.
- File previews use synchronous `File.ReadAllText` and display read errors in the preview pane.
- There is no file mutation, search, file watcher, binary detection, preview-size limit, or external configuration loading.

## Lightweight startup

The startup path intentionally avoids Generic Host and dependency injection containers. Only one source-generated CliFx command descriptor is registered, command activation uses an explicit type switch, and the command constructor stores only a runner delegate. Path validation completes before filesystem services and Terminal.Gui are created.

## Projects

- `src/DotnetTerminalExplorer.Core` — UI-independent explorer logic.
- `src/DotnetTerminalExplorer` — the packable Terminal.Gui application.
- `tests/DotnetTerminalExplorer.Core.Tests` — Core unit tests.
- `tests/DotnetTerminalExplorer.Tests` — CLI and TUI tests.

## Build

```shell
dotnet restore
dotnet build DotnetTerminalExplorer.sln --no-restore -c Release
dotnet test DotnetTerminalExplorer.sln --no-build --no-restore -c Release
```

## Pack and install for development

Create the tool and symbol packages under `nupkg`:

```powershell
pwsh ./pack.ps1
```

Repack and install the local package globally:

```powershell
pwsh ./install.ps1
```

The install script removes an existing global development installation first so rebuilding the same version is deterministic.

## Publishing

CI restores, builds, tests, packs, installs the tool into an isolated directory, and exercises `dte --help` and `dte --version`. NuGet.org publishing is a separate manual GitHub Actions workflow that requires the `NUGET_API_KEY` repository secret.

## License

MIT
