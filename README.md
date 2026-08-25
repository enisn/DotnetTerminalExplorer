# Dotnet Terminal Explorer

Dotnet Terminal Explorer (`dte`) is a deliberately lightweight, scoped file explorer for the terminal. It opens a filesystem tree on the left and an interactive file preview and editor on the right with support for text files and TrueColor image rendering.

![Dotnet Terminal Explorer showing the filesystem tree and text preview](docs/images/dotnet-terminal-explorer.png)

![Dotnet Terminal Explorer rendering an image preview in TrueColor](docs/images/dotnet-terminal-explorer-image-preview.png)

`dte` explores only the directory passed to it, similar to opening a folder with `code`. If the directory is omitted, it uses the current working directory.

## Requirements

- **Standalone Native Binary (Recommended):** **None** — zero dependencies or .NET runtime installation required (pre-compiled with Native AOT).
- **.NET Global Tool (`dotnet tool`):** Requires [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) or SDK.
- **Building from Source:** Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Installation

### Option 1: Standalone Native Binary (Zero .NET runtime required)

#### Linux & macOS
```bash
curl -fsSL https://raw.githubusercontent.com/enisn/DotnetTerminalExplorer/main/install.sh | bash
```

#### Windows (PowerShell)
```powershell
irm https://raw.githubusercontent.com/enisn/DotnetTerminalExplorer/main/install.ps1 | iex
```

#### Manual Download
You can also download prebuilt standalone binaries directly from the [GitHub Releases](https://github.com/enisn/DotnetTerminalExplorer/releases) page (`linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`, `win-arm64`).

---

### Option 2: As a .NET Global Tool (requires .NET 10 Runtime)

```shell
dotnet tool install --global DotnetTerminalExplorer
```

---

## Uninstallation

### Standalone Native Binary

#### Linux & macOS
```bash
curl -fsSL https://raw.githubusercontent.com/enisn/DotnetTerminalExplorer/main/uninstall.sh | bash
```
*(Or simply remove `~/.local/bin/dte`)*

#### Windows (PowerShell)
```powershell
irm https://raw.githubusercontent.com/enisn/DotnetTerminalExplorer/main/uninstall.ps1 | iex
```
*(Or remove the `$HOME\.dte` folder)*

### .NET Global Tool
```shell
dotnet tool uninstall --global DotnetTerminalExplorer
```

---

## Usage

Run it for the current directory or a specific scoped folder:

```shell
dte
dte ./src
dte /path/to/project
dte --page-size 200 ./src    # 200 entries per page instead of 500
dte --page-size 0 ./src      # disable paging entirely
```

Use `dte --help` or `dte --version` without initializing Terminal.Gui or filesystem services.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `F1` | Show the Keyboard Shortcuts & Help dialog |
| `Enter` | Expand directory / Focus editor for text files (when tree focused) |
| `Tab` | Switch focus between Files tree and Preview editor |
| `Ctrl+F` / `F3` | Find in active file (if editor focused) or Workspace Search (if tree focused) |
| `Ctrl+H` | Find & Replace in active file (if editor focused) |
| `Ctrl+Shift+F` | Workspace Search across all files (Ripgrep speed) |
| `F3` / `Enter` | Next match in find bar |
| `Shift+F3` / `Shift+Enter` | Previous match in find bar |
| `Alt+Left` / `Alt+[` | Shrink the left Files panel width |
| `Alt+Right` / `Alt+]` | Expand the left Files panel width |
| `F5` | Reload the selected file |
| `Ctrl+S` | Save changes to the active file |
| `Ctrl+L` | Load a large or binary file as text on demand |
| `Ctrl+N` | Create a new file in the selected directory |
| `Right` / `Ctrl+Right` | Expand the selected directory (one level) |
| `F2` | Rename the selected file or directory inline |
| `Del` | Delete the selected file or directory (asks for confirmation) |
| `F8` | Open the selected file with the operating system's default application |
| `Alt+X` | Clear an applied search filter from the file tree |
| `Esc` | Return to file tree (if editor focused) / Quit with confirmation (if tree focused) |

`Ctrl+F` / `F3` is context-aware: pressing it while focused on the editor opens an in-editor find bar with instant match navigation (`Enter`/`F3` for next, `Shift+Enter`/`Shift+F3` for previous, `Alt+C` for case sensitivity); pressing it while focused on the file tree (or pressing `Ctrl+Shift+F` anytime) opens a ripgrep-grade workspace search modal streaming matches asynchronously across the repository with GitIgnore filtering, regex support, and file/content mode toggles.

When a workspace search finishes, the `Show in Tree` button becomes enabled and applies the found files as a filter on the file tree: only matched files and their parent directories are shown (the pane title changes to `Files (filtered)`), so matches can be previewed sequentially with the arrow keys. Clear the filter with `Alt+X` or by pressing `Esc` while the tree is focused; `Esc` only quits once no filter is applied.

While the in-editor find bar is open, press `Ctrl+H` or `Alt+R` (or click the `Replace` button on the right of the find bar) to reveal the replace field (`Tab` / `Shift+Tab` switches between the Find and Replace inputs). `Enter` in the replace field replaces the current match and advances to the next one, `Ctrl+Enter` replaces all occurrences at once, and `Alt+C` toggles case sensitivity for both find and replace. Replace actions show a read-only hint for non-editable previews; save the file with `Ctrl+S` afterwards.

Note: some terminals encode `Ctrl+H` as `0x08` (historic backspace), which reaches the app as `Ctrl+Backspace`. Both encodings are intercepted before the editor's word-delete binding, so `Ctrl+H` opens the replace bar in either case.

`F8` and `Ctrl+S` are disabled for directories. Files can be edited directly in the right-hand editor pane and saved with `Ctrl+S`. Pressing `Enter` on a selected text file in the file tree automatically focuses the editor, and pressing `Esc` inside the editor immediately returns focus to the file tree. Pressing `Esc` while on the file tree prompts a confirmation dialog before quitting to prevent accidental exits. Press `Ctrl+N` to create a new file in the current directory or `F2` to trigger an inline rename bar in the tree pane (`Enter` commits, `Esc` cancels). Press `Del` to delete the selected file or directory after confirming in a modal dialog. Press `F1` at any time to open the full help dialog.

On ultra-wide terminals, the left file panel is automatically clamped (to 24–48 columns by default) to keep the preview pane spacious. You can manually adjust the width at any time with `Alt+Left` / `Alt+Right` (or `Alt+[` / `Alt+]`).

File previews load asynchronously so navigating the tree never blocks. Binary files and files larger than 2 MB are not read automatically; the preview shows file metadata instead and `Ctrl+L` loads them as text on demand.

Very large directories are paged (500 entries per page by default, configurable via `--page-size`): the next page loads automatically as the selection approaches the end of the loaded items, and a `── Load more…` row is available for explicit jumps to the bottom. `Ctrl+Right` expands one level at a time (recursive expand-all is disabled so huge trees never freeze the UI).

## Initial behavior

- Directories are listed before files, and hidden entries are included.
- Only the root's immediate children are enumerated at startup. Descendants are read when their directory is expanded.
- Directory symlinks and reparse points inside the selected scope are shown but are not expanded.
- Text files support inline viewing and editing with dirty state tracking and `Ctrl+S` saving.
- Image files (`.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, `.bmp`, `.ico`, etc.) are rendered inline as TrueColor half-block (`▀`) thumbnails with format, dimension, and size metadata.
- Non-image binary files display structured metadata summaries instead of unprintable characters.
- Press `F8` on any selected file to open it with the operating system's default viewer or editor.

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
pwsh ./install-dev.ps1
```

The development install script removes an existing global development installation first so rebuilding the same version is deterministic.

## Publishing

CI restores, builds, tests, packs, installs the tool into an isolated directory, and exercises `dte --help` and `dte --version`. NuGet.org publishing is a separate manual GitHub Actions workflow that requires the `NUGET_API_KEY` repository secret.

## License

MIT
