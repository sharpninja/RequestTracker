# RequestTracker — Workspace Analysis

**Last updated:** 2026-02-02

---

## Overview

**RequestTracker** is a .NET 9 desktop application built with **Avalonia UI 11.2** that browses a folder of Markdown and JSON files, previews Markdown (via Pandoc + WebView) and JSON (via a tree view), with navigation history, live refresh, and cross-file link handling.

---

## Project Type & Stack

| Aspect | Details |
|--------|---------|
| **Runtime** | .NET 9 |
| **Output** | WinExe (Windows desktop) |
| **UI** | Avalonia 11.2 (cross-platform XAML) |
| **Pattern** | MVVM (CommunityToolkit.Mvvm, RelayCommand) |
| **WebView** | WebView.Avalonia / WebView.Avalonia.Desktop 11.0.0.1 |
| **External tool** | Pandoc (must be on PATH for Markdown) |

---

## Solution Layout

- **Root:** `.gitignore`, `ANALYSIS.md`, `src/`.
- **Source:** All app code under `src/RequestTracker/` (single project; no `.sln` in repo; `RequestTracker.slnx` is gitignored).
- **Build output:** `bin/`, `obj/` (gitignored).

---

## Architecture

### Key paths (under `src/RequestTracker/`)

| Path | Role |
|------|------|
| `Program.cs` | Entry point; Avalonia + Inter font + desktop WebView; trace logging |
| `App.axaml` / `App.axaml.cs` | Shell; ViewLocator; disables duplicate data-validation; sets MainWindow + MainWindowViewModel |
| `ViewLocator.cs` | Resolves View from ViewModel by naming convention |
| `Models/FileNode.cs` | Tree node: Path, Name, IsDirectory, Children, IsExpanded (two-way with TreeViewItem) |
| `Models/Json/JsonModels.cs` | DTOs: CopilotSessionLog, CursorRequestLog and related (WorkspaceInfo, StatisticsInfo, CostInfo, etc.) |
| `Models/Json/JsonTreeNode.cs` | Tree node for JSON: Name, Value, Type, IsExpanded, Children (ObservableObject) |
| `ViewModels/ViewModelBase.cs` | Base view model (ObservableObject) |
| `ViewModels/MainWindowViewModel.cs` | Tree, selection, Markdown/JSON handling, Pandoc, watcher, navigation history, ChangeLog, status |
| `Views/MainWindow.axaml` | UI: tree + splitter + changelog; content splitter; toolbar (Back/Forward/Refresh + path); Markdown WebView + JSON TreeView; status bar |
| `Views/MainWindow.axaml.cs` | WebView navigation interception; JSON node double-tap to copy and set status |
| `Assets/` | avalonia-logo.ico, styles.css (Pandoc stylesheet; copied to output via csproj) |
| `app.manifest` | Windows compatibility |

---

## Features

### 1. File tree

- **Target path:** `E:\github\FunWasHad\docs\requests` (constant in MainWindowViewModel).
- **Contents:** Directories plus files with extension `.md` or `.json` (see LoadChildren).
- **Auto-select:** Root `readme.md` is selected at startup if present.
- **Expand:** Directories with children are expanded by default; selection/navigation expands to the selected node.

### 2. Markdown preview

- **Selection of .md:** Converts to HTML via Pandoc (`-f markdown -t html -s --css <styles.css>`) and shows in WebView.
- **Cache:** Output under `%TEMP%\RequestTracker_Cache` (filename + path hash). Regenerated when source is newer.
- **CSS:** `Assets/styles.css` is included as Content with `CopyToOutputDirectory="PreserveNewest"`; path resolved from `AppContext.BaseDirectory` with dev fallback to hardcoded project path.
- **Visibility:** Markdown pane is shown when a .md file is selected (`IsMarkdownVisible` / `IsJsonVisible`).

### 3. JSON tree view

- **Selection of .json:** File is parsed with `System.Text.Json` (JsonNode). No external process.
- **Schema detection:** If root has `sessionId` + `statistics` → "Copilot Session Log"; if `entries` + `session` → "Cursor Request Log". Typed deserialize used only to validate; tree is built from JsonNode.
- **Tree:** `JsonTreeNode` (Name, Value, Type, IsExpanded, Children). Root shows schema type; objects/arrays expanded recursively; array items show index and optional preview from first string property; values show string representation.
- **UI:** TreeView in content area; double-tap on a node copies its text to clipboard and sets status bar to "Copied: …".
- **Visibility:** JSON pane is shown when a .json file is selected.

### 4. Navigation

- **Back / Forward:** Stacks `_backStack` / `_forwardStack`. On selection change (not from history), current node is pushed to back and forward is cleared. Back/Forward buttons use RelayCommand with CanExecute (CanNavigateBack / CanNavigateForward).
- **Refresh:** RelayCommand; deletes cached HTML for current node and calls GenerateAndNavigate (Markdown only).
- **Cross-file links (Markdown):** WebView `NavigationStarting` intercepts file URLs. If target is `.md`, navigation is cancelled and `HandleNavigation(path)` maps path (RequestTracker_Cache relative → source dir, or filename in current dir) and selects node via `SelectNodeByPath` (FindNode + ExpandToNode).

### 5. File system watcher

- **Filter:** `*.*` (all files); watches TargetPath and subdirectories (create, delete, rename, last write).
- **On tree structure change:** Rebuilds tree (`InitializeTree`).
- **On file change:** If the changed path equals `_currentMarkdownPath`, regenerates HTML, refreshes WebView (timestamp on URI), and prepends a line to ChangeLog (e.g. `[HH:mm:ss] Rebuilt: filename.md`). JSON files do not set `_currentMarkdownPath`, so changing a JSON file does not trigger this path.

### 6. Change log & status

- **ChangeLog:** ObservableCollection<string> bound to ListBox under tree (with GridSplitter). Shows recent "Rebuilt" lines.
- **StatusMessage:** Bound to status bar (blue bar at bottom). Set on JSON node copy ("Copied: …").

---

## Data Flow (summary)

- **Tree:** `Nodes` (root FileNode) → TreeView; `SelectedNode` two-way; `FileNode.IsExpanded` two-way via ItemContainerTheme.
- **Selection:** On change → `GenerateAndNavigate`: if directory or null, return; if .json → LoadJson, JsonTree, IsJsonVisible=true, IsMarkdownVisible=false; if .md → Pandoc, HtmlSource, IsMarkdownVisible=true, IsJsonVisible=false, _currentMarkdownPath set.
- **History:** OnSelectedNodeChanging (when not navigating history) pushes current to back, clears forward. NavigateBack/Forward pop/push and set SelectedNode with _isNavigatingHistory.
- **Watcher:** Events marshalled to UI via Dispatcher.UIThread.InvokeAsync.

---

## Dependencies (.csproj)

- Avalonia 11.2.6 (core, Desktop, Fluent theme, Inter font)
- Avalonia.Diagnostics 11.2.6 (Debug only)
- CommunityToolkit.Mvvm 8.2.1
- WebView.Avalonia / WebView.Avalonia.Desktop 11.0.0.1
- **BCL:** System.Text.Json (and JsonNode) used for JSON; no extra JSON package.

---

## Configuration / environment

- **Target path:** Constant `TargetPath` in MainWindowViewModel; not configurable.
- **Pandoc:** Required on PATH for Markdown preview; no in-app fallback if missing.
- **CSS:** `Assets/styles.css` copied to output; fallback path in code points to `src/RequestTracker/Assets/styles.css` for dev.

---

## .gitignore

- `bin/`, `obj/`, `.vs/`, `*.user`, `RequestTracker.slnx`, `stdout*.log`, `stderr*.log`.

---

## Possible improvements

1. Make the watched/root path configurable (e.g. settings, command-line, or folder picker).
2. Add a solution file (e.g. `RequestTracker.sln`) if opening in Visual Studio/Rider without slnx is desired.
3. Remove redundant `<Folder Include="Models\" />` in csproj if the folder is already covered by source items.
4. Consider initializing `_statusMessage` to `""` to avoid null in binding (if nullable reference types are enabled).
5. Remove duplicate `using System;` at top of MainWindowViewModel.cs (minor lint).
6. Optionally handle "Pandoc not found" (e.g. show message or disable Markdown preview).

---

## Summary

RequestTracker is an Avalonia desktop app for browsing a fixed folder of Markdown and JSON files. It previews Markdown via Pandoc and WebView, and JSON via a built-in tree with schema hints for Copilot and Cursor log formats. Features include Back/Forward navigation, Refresh, ChangeLog, status bar, and live refresh for the current Markdown file when it changes on disk. Source lives under `src/RequestTracker/`; dependencies are Avalonia, CommunityToolkit.Mvvm, and WebView.Avalonia; Pandoc is required for Markdown.
