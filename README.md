# RequestTracker

RequestTracker is an Avalonia UI desktop application designed to visualize and analyze session logs and request data from AI coding assistants like **GitHub Copilot** and **Cursor**. It provides a unified view of interactions, enabling developers to review prompt history, context usage, and automated actions taken by these tools.

## Key Features

*   **Unified Dashboard**: Aggregates logs from different AI providers (Copilot, Cursor) into a single, searchable interface.
*   **Tree Navigation**: filesystem watcher monitors a target directory for log files, automatically indexing them into a navigation tree.
*   **Detailed Request Analysis**:
    *   **Interpretation & Metadata**: Displays extracted metadata, user intent interpretation, and key decisions.
    *   **Context Inspection**: View the exact context (code snippets, files) sent to the LLM.
    *   **Action Tracking**: visualizes automated actions (file creation, edits, command execution) in a structured grid.
    *   **JSON Inspector**: Built-in JSON viewer to inspect the raw underlying data for deep debugging.
*   **Markdown Rendering**: Integrated `markdig`-based markdown viewer for rendering log content and associated documentation.
*   **Responsive UI**: Modern, light-theme interface with collapsible sections and persistent window state.

## Data Ingestion

The application monitors a specified directory (e.g., `docs/requests`) for JSON and Markdown log files. It supports custom schema formats used to track AI interactions:

*   **Copilot Logs**: JSON-based session logs containing request/response cycles, token usage metrics, and workspace context.
*   **Cursor Logs**: JSON/Markdown hybrid logs capturing "Tab" requests, diffs, and chat history.

## Usage

1.  **Configure Directory**: Point the application to the root folder containing your request logs.
2.  **Browse Sessions**: Use the tree view on the left to navigate through sessions organized by date or folder structure.
3.  **Inspect Requests**: Click on a specific request to view its details in the main panel.
    *   Expand the **Actions** grid to see what file changes were performed.
    *   Check **Interpretation** to understand how the AI understood the task.
    *   Use the **Original JSON** expander to see the raw data fields.

## Tech Stack

*   **Framework**: Avalonia UI (Cross-platform .NET XAML framework)
*   **Language**: C# / .NET 8
*   **Parsing**: `System.Text.Json` with custom robust parsing for flexible schemas.
*   **Markdown**: `Markdig.Avalonia` for rendering formatted text.

## Build and run

```bash
# From repo root
cd src/RequestTracker
dotnet run
```

### WSL with WSLg

On WSL with WSLg enabled (Windows 11), the app window should appear on the Windows desktop. If it doesn’t:

1. **Check WSLg**: Ensure you’re on Windows 11 with WSLg (no separate X server needed).
2. **Run from project**: `cd src/RequestTracker && dotnet run -c Debug`
3. **Or use the script**: From repo root, `chmod +x run-wslg.sh && ./run-wslg.sh`
4. **Taskbar**: The window may show in the Windows taskbar; click it to bring it to front.

