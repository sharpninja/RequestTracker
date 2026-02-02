# RequestTracker

Avalonia desktop app for browsing Markdown and JSON request logs, with Markdown preview (via Pandoc) and JSON tree view.

## Prerequisites

- **.NET 9 SDK** — [Install .NET](https://dotnet.microsoft.com/download)
- **Pandoc** — Required for Markdown → HTML preview.

### Install Pandoc locally

**Debian / Ubuntu / WSL:**

```bash
sudo apt-get update
sudo apt-get install -y pandoc
```

**Fedora / RHEL:**

```bash
sudo dnf install -y pandoc
```

**macOS (Homebrew):**

```bash
brew install pandoc
```

**Windows:** Download from [pandoc.org](https://pandoc.org/installing.html) or `winget install JohnMacFarlane.Pandoc`.

Verify:

```bash
pandoc --version
```

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
5. **Markdown preview**: On Linux the WebView is disabled (placeholder shown); use JSON view or run on Windows for full preview.

## Publish for Linux

```bash
dotnet publish src/RequestTracker/RequestTracker.csproj -r linux-x64 -c Release -o publish/linux-x64 --self-contained false
cd publish/linux-x64
./RequestTracker
```

Ensure Pandoc is on your PATH when running the published app (same machine or same PATH in the environment).
