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
5. **Markdown preview**: On Linux the embedded WebView is unsupported (per [Avalonia WebView docs](https://docs.avaloniaui.net/accelerate/components/webview/quickstart): use **NativeWebDialog** instead of NativeWebView). This app opens the generated HTML in your **default browser** on Linux/WSL so preview works in WSLg.

## Publish for Linux

```bash
dotnet publish src/RequestTracker/RequestTracker.csproj -r linux-x64 -c Release -o publish/linux-x64 --self-contained false
cd publish/linux-x64
./RequestTracker
```

Ensure Pandoc is on your PATH when running the published app (same machine or same PATH in the environment).

## WebView on Linux / WSL

Per [Avalonia WebView Quick Start](https://docs.avaloniaui.net/accelerate/components/webview/quickstart):

- **NativeWebView** (embedded) is **not supported** on Linux; use **NativeWebDialog** (separate window) instead.
- **Linux prerequisites**: GTK 3 and WebKitGTK 4.1 — e.g. `apt install libgtk-3-0 libwebkit2gtk-4.1-0`.

This project uses **WebView.Avalonia** (not Avalonia Accelerate). On Linux/WSL we:

1. Remove the embedded WebView from the main window so the app window opens correctly.
2. Open the generated Markdown→HTML in the **system default browser** (`xdg-open`) so preview works in WSLg.

To use the official embedded WebView on Linux you would need [Avalonia Accelerate WebView](https://docs.avaloniaui.net/accelerate/components/webview/quickstart) (license required) and **NativeWebDialog** for the preview window.
