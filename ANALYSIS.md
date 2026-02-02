# RequestTracker — Workspace Analysis

**Last updated:** 2026-02-02

---

## Overview

**RequestTracker** is a .NET 9 desktop application built with **Avalonia UI 11.2** that browses a folder of Markdown files, converts them to HTML via **Pandoc**, and displays them in an embedded WebView with live refresh and cross-file navigation.

---

## Project Type & Stack

| Aspect | Details |
|--------|---------|
| **Runtime** | .NET 9 |
| **Output** | WinExe (Windows desktop) |
| **UI** | Avalonia 11.2 (cross-platform XAML) |
| **Pattern** | MVVM (CommunityToolkit.Mvvm) |
| **WebView** | WebView.Avalonia / WebView.Avalonia.Desktop 11.0.0.1 |
| **External tool** | Pandoc (must be on PATH) |

---

## Architecture

### Solution layout

- Single project: **RequestTracker** (no `.sln` file).
- Source lives under `RequestTracker/`; root also contains log files (`stderr*.log`, `stdout*.log`).

### Key files and roles

| Path | Role |
|------|------|
| `Program.cs` | Entry point; configures Avalonia, Inter font, desktop WebView, trace logging |
| `App.axaml` / `App.axaml.cs` | Application shell; ViewLocator; disables duplicate data-validation plugins; sets MainWindow + MainWindowViewModel |
| `ViewLocator.cs` | Resolves View from ViewModel by naming convention (ViewModel → View) |
| `Models/FileNode.cs` | Tree node: Path, Name, IsDirectory, Children, **IsExpanded** (two-way with TreeViewItem) |
| `ViewModels/ViewModelBase.cs` | Base view model (ObservableObject) |
| `ViewModels/MainWindowViewModel.cs` | Tree data, selection, Pandoc conversion, file watcher, navigation, change log |
| `Views/MainWindow.axaml` | UI: TreeView + ChangeLog ListBox + WebView |
| `Views/MainWindow.axaml.cs` | Intercepts file-navigation in WebView; cancels .md and calls `HandleNavigation` |
| `Assets/` | `avalonia-logo.ico`, **styles.css** (Pandoc stylesheet for rendered HTML) |
| `app.manifest` | Windows compatibility (e.g. supportedOS) |

---

## Features

1. **Tree of Markdown files**  
   Watches `E:\github\FunWasHad\docs\requests` (hardcoded). Builds a tree of directories and `.md` files. Auto-selects `readme.md` at root if present.

2. **Pandoc conversion**  
   Selected `.md` → HTML via Pandoc (`-f markdown -t html -s --css <styles.css>`). Output is cached under `%TEMP%\RequestTracker_Cache` (filename + path hash). Regenerated when source is newer.

3. **WebView preview**  
   Rendered HTML is shown in an Avalonia WebView. Styling comes from `Assets/styles.css` (GitHub-like markdown body). CSS path: first `AppContext.BaseDirectory/Assets/styles.css`, then dev fallback to a hardcoded project path.

4. **Cross-file navigation**  
   Links to other `.md` files (e.g. `copilot/session-log.md`) are intercepted in the WebView (`NavigationStarting`). If the URL is under `RequestTracker_Cache`, the relative path is mapped back to the source folder and the corresponding node is selected and expanded in the tree (`SelectNodeByPath` → `FindNode` + `ExpandToNode`). Fallback: same filename in current markdown’s directory.

5. **File system watcher**  
   `FileSystemWatcher` on the target path, filter `*.md`, subdirectories included. On create/delete/rename: tree is rebuilt (`InitializeTree`). On change: if the changed file is the currently viewed one, HTML is regenerated, WebView is refreshed (timestamp query on URI), and a line is prepended to **ChangeLog** (e.g. `[HH:mm:ss] Rebuilt: filename.md`).

6. **Change log**  
   `ChangeLog` is an `ObservableCollection<string>` bound to a ListBox under the tree (height 150). Used to show recent “Rebuilt” events.

---

## Dependencies (from .csproj)

- **Avalonia** 11.2.6 (core, Desktop, Fluent theme, Inter font)
- **Avalonia.Diagnostics** 11.2.6 (Debug only)
- **CommunityToolkit.Mvvm** 8.2.1
- **WebView.Avalonia** / **WebView.Avalonia.Desktop** 11.0.0.1

---

## Data Flow

- **Tree:** `Nodes` (root `FileNode`) → TreeView with `Children`; `SelectedNode` two-way bound; `FileNode.IsExpanded` two-way via `ItemContainerTheme`.
- **Preview:** `SelectedNode` change → `GenerateAndNavigate` → Pandoc → temp HTML → `HtmlSource` (Uri) → WebView.
- **Links:** WebView `NavigationStarting` → if target is `.md` (under file scheme), cancel and `HandleNavigation(path)` → resolve to source path → `SelectNodeByPath` → `SelectedNode` update → same pipeline as above.
- **Watcher:** Tree and file change events marshalled to UI thread via `Dispatcher.UIThread.InvokeAsync`.

---

## Notable implementation details

- **FileNode** is `ObservableObject` with `[ObservableProperty] bool _isExpanded` so the tree can expand parents when navigating to a linked file.
- **Single implementation** of `InitializeTree`, `LoadChildren`, `SetupWatcher`, `OnTreeChanged`, `OnFileChanged` (no duplicate methods). Watcher filter is `*.md`; live refresh and ChangeLog work as intended.
- **Navigation** supports both “path under RequestTracker_Cache” (relative path mapped to current dir) and “filename in current dir” fallback.
- **Pandoc** is invoked with standalone HTML and external CSS; inline styles were removed in favor of `styles.css`.

---

## Configuration / environment

- **Target path:** `MainWindowViewModel.TargetPath` = `E:\github\FunWasHad\docs\requests` (constant). Not configurable.
- **Pandoc:** Must be on system PATH; no in-app fallback if missing.
- **CSS fallback:** `ConvertMarkdownToHtml` uses a hardcoded source path for `styles.css` when not found next to the executable (dev scenario).

---

## Possible improvements

1. Make the watched/root path configurable (e.g. settings, command-line, or folder picker).
2. Add a `.gitignore` for `stderr*.log`, `stdout*.log` (and optionally `bin/`, `obj/`) if they are generated locally.
3. Add a `.sln` for easier opening in Visual Studio / Rider.
4. Consider removing the redundant `<Folder Include="Models\" />` in the csproj if the folder is already covered by the `FileNode.cs` item.
5. Optionally handle “Pandoc not found” (e.g. show a message or disable preview).

---

## Summary

RequestTracker is a small, focused Avalonia app for browsing and previewing a fixed folder of Markdown with Pandoc and an embedded WebView. Duplicate ViewModel methods have been removed; the watcher uses `*.md`, live refresh and ChangeLog work, and navigation between linked Markdown files is supported via tree selection and expansion. The main limitations are the hardcoded target path and dependency on Pandoc on PATH.
