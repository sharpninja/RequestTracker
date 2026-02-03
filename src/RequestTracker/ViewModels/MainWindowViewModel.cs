using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestTracker.Converters;
using RequestTracker.Models;
using RequestTracker.Models.Json;
using RequestTracker.Services;

namespace RequestTracker.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string TargetPath = @"E:\github\FunWasHad\docs\sessions";

    private static readonly JsonSerializerOptions CopilotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new WorkspaceInfoConverter() }
    };

    /// <summary>
    /// Target path resolved for the current OS (Windows path on Windows, /mnt/... on Linux).
    /// </summary>
    private static string GetResolvedTargetPath() => PathConverter.ToDisplayPath(TargetPath);

    private FileSystemWatcher? _watcher;
    private readonly IClipboardService _clipboardService;

    [ObservableProperty]
    private ObservableCollection<FileNode> _nodes = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNodePathDisplay))]
    private FileNode? _selectedNode;

    /// <summary>Null-safe path for binding (avoids 'Value is null' when SelectedNode is null).</summary>
    public string SelectedNodePathDisplay => SelectedNode?.Path ?? "";

    [ObservableProperty]
    private ObservableCollection<string> _changeLog = new();

    [ObservableProperty]
    private bool _isMarkdownVisible = true;

    [ObservableProperty]
    private bool _isJsonVisible = false;

    [ObservableProperty]
    private bool _isRequestDetailsVisible = false;

    [ObservableProperty]
    private UnifiedRequestEntry? _selectedUnifiedRequest;

    [ObservableProperty]
    private ObservableCollection<JsonTreeNode> _jsonTree = new();

    [ObservableProperty]
    private JsonLogSummary _jsonLogSummary = new();

    [ObservableProperty]
    private ObservableCollection<SearchableEntry> _searchableEntries = new();

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private string _requestIdFilter = "";

    [ObservableProperty]
    private string _displayFilter = "";

    [ObservableProperty]
    private string _modelFilter = "";

    [ObservableProperty]
    private string _timestampFilter = "";

    [ObservableProperty]
    private string _agentFilter = "";

    /// <summary>Distinct values for filter ComboBoxes (includes "" for "All").</summary>
    [ObservableProperty]
    private ObservableCollection<string> _distinctRequestIds = new() { "" };

    [ObservableProperty]
    private ObservableCollection<string> _distinctDisplayTexts = new() { "" };

    [ObservableProperty]
    private ObservableCollection<string> _distinctModels = new() { "" };

    [ObservableProperty]
    private ObservableCollection<string> _distinctAgents = new() { "" };

    [ObservableProperty]
    private ObservableCollection<string> _distinctTimestamps = new() { "" };

    [ObservableProperty]
    private ObservableCollection<SearchableEntry> _filteredSearchEntries = new();

    [ObservableProperty]
    private SearchableEntry? _selectedSearchEntry;

    [ObservableProperty]
    private JsonTreeNode? _selectedJsonNode;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>True when markdown preview was opened in the system browser.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMarkdownLoadingPlaceholder))]
    private bool _isPreviewOpenedInBrowser;

    /// <summary>True when markdown is selected but preview not yet loaded (show loading message).</summary>
    public bool ShowMarkdownLoadingPlaceholder => !IsPreviewOpenedInBrowser;

    /// <summary>Path to the current preview HTML file (for "Open in browser" button).</summary>
    [ObservableProperty]
    private string? _currentPreviewHtmlPath;

    /// <summary>Raw markdown of the selected file when preview is opened externally.</summary>
    [ObservableProperty]
    private string _currentPreviewMarkdownText = "";

    private string? _currentMarkdownPath;

    private CancellationTokenSource? _markdownPreviewCts;

    /// <summary>Cancels any in-flight markdown preview task (e.g. pandoc generation).</summary>
    public void CancelMarkdownPreview()
    {
        _markdownPreviewCts?.Cancel();
        _markdownPreviewCts?.Dispose();
        _markdownPreviewCts = null;
    }

    // Navigation History
    private readonly Stack<FileNode> _backStack = new();
    private readonly Stack<FileNode> _forwardStack = new();
    private bool _isNavigatingHistory;

    public MainWindowViewModel() : this(new ClipboardService())
    {
    }

    public MainWindowViewModel(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
        // InitializeTree and SetupWatcher run in InitializeAfterWindowShown() when the window is opened,
        // so the window displays even if file system or watcher setup fails.
    }

    /// <summary>Called by MainWindow when it has opened. Builds the file tree off the UI thread and applies on UI; starts the watcher.</summary>
    public void InitializeAfterWindowShown()
    {
        DispatchToUi(() => StatusMessage = "Loading file tree...");
        Task.Run(() =>
        {
            try
            {
                OllamaLogAgentService.TryStartOllamaIfNeeded();

                string resolvedPath = GetResolvedTargetPath();
                var (allJsonNode, rootDto) = BuildTreeOffThread(resolvedPath);
                SetupWatcher(); // lightweight; can run on background
                DispatchToUi(() =>
                {
                    try
                    {
                        Nodes.Clear();
                        Nodes.Add(allJsonNode);
                        if (rootDto != null)
                        {
                            var root = ApplyTreeDtoToNodes(rootDto);
                            Nodes.Add(root);
                            SelectedNode = allJsonNode;
                            SetStatus($"Loaded: {resolvedPath}");
                        }
                        else
                        {
                            Nodes.Add(new FileNode(resolvedPath, true) { Name = "Directory not found" });
                            SelectedNode = allJsonNode;
                            SetStatus($"Directory not found: {resolvedPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ApplyTreeDto failed: {ex}");
                        SetStatus($"Error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitializeAfterWindowShown failed: {ex}");
                DispatchToUi(() => SetStatus($"Failed to load tree: {ex.Message}"));
            }
        });
    }

    partial void OnSearchQueryChanged(string value)
    {
        UpdateFilteredSearchEntries();
    }

    partial void OnRequestIdFilterChanged(string value) => UpdateFilteredSearchEntries();
    partial void OnDisplayFilterChanged(string value) => UpdateFilteredSearchEntries();
    partial void OnModelFilterChanged(string value) => UpdateFilteredSearchEntries();
    partial void OnTimestampFilterChanged(string value) => UpdateFilteredSearchEntries();
    partial void OnAgentFilterChanged(string value) => UpdateFilteredSearchEntries();

    partial void OnSelectedSearchEntryChanged(SearchableEntry? value)
    {
        if (value == null) return;
        var node = FindJsonNodeBySourcePath(JsonTree, value.SourcePath);
        if (node != null)
        {
            SelectedJsonNode = node;
            ExpandToJsonNode(JsonTree, node);
        }
    }

    private void UpdateFilteredSearchEntries()
    {
        var q = (SearchQuery ?? "").Trim();
        var rid = (RequestIdFilter ?? "").Trim().ToLowerInvariant();
        var disp = (DisplayFilter ?? "").Trim().ToLowerInvariant();
        var mod = (ModelFilter ?? "").Trim().ToLowerInvariant();
        var ts = (TimestampFilter ?? "").Trim().ToLowerInvariant();

        IEnumerable<SearchableEntry> filtered = SearchableEntries;

        if (!string.IsNullOrEmpty(q))
        {
            var lower = q.ToLowerInvariant();
            filtered = filtered.Where(e => (e.SearchText ?? "").ToLowerInvariant().Contains(lower) ||
                                          (e.RequestId ?? "").ToLowerInvariant().Contains(lower) ||
                                          (e.DisplayText ?? "").ToLowerInvariant().Contains(lower) ||
                                          (e.Model ?? "").ToLowerInvariant().Contains(lower) ||
                                          (e.Agent ?? "").ToLowerInvariant().Contains(lower) ||
                                          (e.Timestamp ?? "").ToLowerInvariant().Contains(lower));
        }
        if (!string.IsNullOrEmpty(rid))
            filtered = filtered.Where(e => string.Equals(e.RequestId ?? "", rid, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(disp))
            filtered = filtered.Where(e => string.Equals(e.DisplayText ?? "", disp, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(mod))
            filtered = filtered.Where(e => string.Equals(e.Model ?? "", mod, StringComparison.OrdinalIgnoreCase));
        var agent = (AgentFilter ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(agent))
            filtered = filtered.Where(e => string.Equals(e.Agent ?? "", agent, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(ts))
            filtered = filtered.Where(e => string.Equals(e.TimestampDisplay ?? "", ts, StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(e.Timestamp ?? "", ts, StringComparison.OrdinalIgnoreCase));

        var sorted = filtered.OrderByDescending(e => e.SortableTimestamp ?? DateTime.MinValue).ToList();
        FilteredSearchEntries = new ObservableCollection<SearchableEntry>(sorted);

        if (IsRequestDetailsVisible)
        {
            NavigateToPreviousRequestCommand.NotifyCanExecuteChanged();
            NavigateToNextRequestCommand.NotifyCanExecuteChanged();
        }

        NotifyContextConsumer();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void NavigateBack()
    {
        if (_backStack.Count > 0 && SelectedNode != null)
        {
            _isNavigatingHistory = true;
            _forwardStack.Push(SelectedNode);
            SelectedNode = _backStack.Pop();
            _isNavigatingHistory = false;

            NavigateBackCommand.NotifyCanExecuteChanged();
            NavigateForwardCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanNavigateBack() => _backStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    private void NavigateForward()
    {
        if (_forwardStack.Count > 0 && SelectedNode != null)
        {
            _isNavigatingHistory = true;
            _backStack.Push(SelectedNode);
            SelectedNode = _forwardStack.Pop();
            _isNavigatingHistory = false;

            NavigateBackCommand.NotifyCanExecuteChanged();
            NavigateForwardCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanNavigateForward() => _forwardStack.Count > 0;

    [RelayCommand]
    private void Refresh()
    {
        if (SelectedNode != null)
        {
            // Force regenerate
             string hash = SelectedNode.Path.GetHashCode().ToString("X");
             string tempFileName = $"{Path.GetFileNameWithoutExtension(SelectedNode.Path)}_{hash}.html";
             string tempDir = Path.Combine(Path.GetTempPath(), "RequestTracker_Cache");
             string tempPath = Path.Combine(tempDir, tempFileName);

             if (File.Exists(tempPath)) File.Delete(tempPath);

             GenerateAndNavigate(SelectedNode);
        }
    }

    partial void OnSelectedUnifiedRequestChanged(UnifiedRequestEntry? value)
    {
        LogUnifiedEntryActionsForGrid(value);
    }

    /// <summary>
    /// Detailed logging when the unified entry (and its Actions) are bound by the details grid.
    /// </summary>
    private static void LogUnifiedEntryActionsForGrid(UnifiedRequestEntry? entry)
    {
        const string prefix = "[Unified Grid Actions]";
        if (entry == null)
        {
            Console.WriteLine($"{prefix} Bound entry is null; grid will show no actions.");
            return;
        }
        string requestId = entry.RequestId ?? "(null)";
        Console.WriteLine($"{prefix} Binding entry for details grid RequestId={requestId} HasActions={entry.HasActions}");
        var actions = entry.Actions;
        if (actions == null || actions.Count == 0)
        {
            Console.WriteLine($"{prefix}   Actions count=0 (grid will be empty/hidden).");
            return;
        }
        Console.WriteLine($"{prefix}   Actions count={actions.Count}");
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            string desc = string.IsNullOrEmpty(a.Description) ? "" : (a.Description.Length <= 80 ? a.Description : a.Description.Substring(0, 77) + "...");
            Console.WriteLine($"{prefix}   [{i + 1}] Order={a.Order} Type={a.Type} Status={a.Status} FilePath={a.FilePath} Description={desc}");
        }
    }

    [RelayCommand]
    private void ShowRequestDetails(SearchableEntry entry)
    {
        if (entry != null && entry.UnifiedEntry != null)
        {
            SelectedUnifiedRequest = entry.UnifiedEntry;
            IsMarkdownVisible = false;
            IsJsonVisible = false;
            IsRequestDetailsVisible = true;
            ArchiveCommand.NotifyCanExecuteChanged();
            NavigateToPreviousRequestCommand.NotifyCanExecuteChanged();
            NavigateToNextRequestCommand.NotifyCanExecuteChanged();
            NotifyContextConsumer();
        }
    }

    /// <summary>Navigates to the request details view for the currently selected search entry (e.g. on double-click).</summary>
    public void TryNavigateToSelectedSearchEntry()
    {
        if (SelectedSearchEntry is { } e && e.UnifiedEntry != null)
            ShowRequestDetails(e);
    }

    /// <summary>Called when the chat window is open; we push current context so the agent stays in sync with navigation.</summary>
    private Action<string>? _contextConsumer;

    /// <summary>Register or clear the context consumer (chat window). When set, we call it when the user navigates to JSON or details.</summary>
    public void SetContextConsumer(Action<string>? consumer) => _contextConsumer = consumer;

    private void NotifyContextConsumer()
    {
        if (_contextConsumer == null) return;
        try
        {
            var ctx = GetLogContextForAgent();
            _contextConsumer(ctx);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NotifyContextConsumer: {ex.Message}");
        }
    }

    /// <summary>Builds a short summary of the current log view for the AI assistant (filtered entries and selected request). Includes agent config file content when present.</summary>
    public string GetLogContextForAgent()
    {
        var entries = FilteredSearchEntries ?? SearchableEntries;
        var sb = new StringBuilder();

        var agentConfig = AgentConfigIo.ReadContent();
        if (!string.IsNullOrWhiteSpace(agentConfig))
        {
            sb.AppendLine("--- Agent instructions (from agent_config.md) ---");
            sb.AppendLine(agentConfig.Trim());
            sb.AppendLine();
        }

        // Navigation context: what the user is currently viewing
        if (IsRequestDetailsVisible)
            sb.AppendLine("Navigation: Request details view (one request selected).");
        else if (IsJsonVisible && SelectedNode != null)
            sb.AppendLine(SelectedNode.Path == "ALL_JSON_VIRTUAL_NODE"
                ? "Navigation: All JSON (aggregated log from all files)."
                : $"Navigation: JSON file: {SelectedNode.Path}");
        else if (IsMarkdownVisible && !string.IsNullOrEmpty(_currentMarkdownPath))
            sb.AppendLine($"Navigation: Markdown: {_currentMarkdownPath}");
        else
            sb.AppendLine("Navigation: (list or loading)");
        sb.AppendLine();

        if (entries == null || entries.Count == 0)
        {
            sb.AppendLine("(No log loaded or no entries in current view.)");
            return sb.ToString();
        }
        sb.AppendLine($"Current view: {entries.Count} request(s).");
        sb.AppendLine("Columns: RequestId | DisplayText | Model | Agent | Timestamp");
        int take = Math.Min(50, entries.Count);
        for (int i = 0; i < take; i++)
        {
            var e = entries[i];
            sb.AppendLine($"  {e.RequestId} | {TruncateForContext(e.DisplayText, 60)} | {e.Model} | {e.Agent} | {e.TimestampDisplay}");
        }
        if (entries.Count > take)
            sb.AppendLine($"  ... and {entries.Count - take} more.");
        if (SelectedUnifiedRequest != null)
        {
            sb.AppendLine();
            sb.AppendLine("Selected request:");
            var r = SelectedUnifiedRequest;
            sb.AppendLine($"  RequestId: {r.RequestId}; Model: {r.Model}; Agent: {r.Agent}; Status: {r.Status}");
            if (!string.IsNullOrWhiteSpace(r.QueryTitle)) sb.AppendLine($"  Title: {r.QueryTitle}");
            if (!string.IsNullOrWhiteSpace(r.QueryText)) sb.AppendLine($"  Query: {TruncateForContext(r.QueryText, 200)}");
        }
        return sb.ToString();
    }

    private static string TruncateForContext(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= maxLen ? s : s.AsSpan(0, maxLen).ToString() + "...";
    }

    /// <summary>If the given path matches a request/entry (e.g. "entries[0]", "requests[1]"), navigates to that request's detail view. Returns true if navigation occurred.</summary>
    public bool TryNavigateToDetailsForSourcePath(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;
        var entries = FilteredSearchEntries ?? SearchableEntries;
        if (entries == null) return false;
        foreach (var entry in entries)
        {
            if (string.Equals(entry.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase) && entry.UnifiedEntry != null)
            {
                SelectedSearchEntry = entry;
                ShowRequestDetails(entry);
                return true;
            }
        }
        return false;
    }

    [RelayCommand]
    private void SelectSearchEntry(SearchableEntry entry)
    {
        if (entry != null)
            SelectedSearchEntry = entry;
    }

    [RelayCommand]
    private void CloseRequestDetails()
    {
        IsRequestDetailsVisible = false;
        IsJsonVisible = true;
        IsMarkdownVisible = false;
        ArchiveCommand.NotifyCanExecuteChanged();
        NotifyContextConsumer();
    }

    private int GetCurrentRequestIndexInFilteredList()
    {
        if (SelectedUnifiedRequest == null || FilteredSearchEntries == null || FilteredSearchEntries.Count == 0)
            return -1;
        for (int i = 0; i < FilteredSearchEntries.Count; i++)
        {
            if (FilteredSearchEntries[i].UnifiedEntry == SelectedUnifiedRequest)
                return i;
        }
        return -1;
    }

    private bool CanNavigateToPreviousRequest()
    {
        return GetCurrentRequestIndexInFilteredList() > 0;
    }

    private bool CanNavigateToNextRequest()
    {
        int i = GetCurrentRequestIndexInFilteredList();
        return i >= 0 && i < FilteredSearchEntries.Count - 1;
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToPreviousRequest))]
    private void NavigateToPreviousRequest()
    {
        int i = GetCurrentRequestIndexInFilteredList();
        if (i <= 0) return;
        var entry = FilteredSearchEntries[i - 1];
        if (entry?.UnifiedEntry == null) return;
        SelectedSearchEntry = entry;
        ShowRequestDetails(entry);
        NavigateToPreviousRequestCommand.NotifyCanExecuteChanged();
        NavigateToNextRequestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToNextRequest))]
    private void NavigateToNextRequest()
    {
        int i = GetCurrentRequestIndexInFilteredList();
        if (i < 0 || i >= FilteredSearchEntries.Count - 1) return;
        var entry = FilteredSearchEntries[i + 1];
        if (entry?.UnifiedEntry == null) return;
        SelectedSearchEntry = entry;
        ShowRequestDetails(entry);
        NavigateToPreviousRequestCommand.NotifyCanExecuteChanged();
        NavigateToNextRequestCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedNodeChanging(FileNode? value)
    {
        if (!_isNavigatingHistory && SelectedNode != null && value != null && SelectedNode != value)
        {
            _backStack.Push(SelectedNode);
            _forwardStack.Clear();

            NavigateBackCommand.NotifyCanExecuteChanged();
            NavigateForwardCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedNodeChanged(FileNode? value)
    {
        Console.WriteLine($"Selected Node Changed: {value?.Path}");
        GenerateAndNavigate(value);

        if (value != null)
        {
             ExpandToNode(Nodes, value);
        }
    }

    [RelayCommand]
    private async Task CopyText(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            await _clipboardService.SetTextAsync(text);
            SetStatus($"Copied: {text}");
        }
    }

    [RelayCommand]
    private async Task CopyOriginalJson(UnifiedRequestEntry? entry)
    {
        if (entry?.OriginalEntry == null)
        {
            SetStatus("No original JSON to copy.");
            return;
        }
        try
        {
            var json = JsonSerializer.Serialize(entry.OriginalEntry, new JsonSerializerOptions { WriteIndented = true });
            await _clipboardService.SetTextAsync(json);
            SetStatus("Copied original JSON to clipboard.");
        }
        catch (Exception ex)
        {
            SetStatus($"Copy failed: {ex.Message}");
        }
    }

    public void HandleNavigation(string path)
    {
        // Path might be the resolved path in the temp directory, e.g., C:\Users\...\Temp\RequestTracker_Cache\next.md
        // We need to resolve it relative to the _currentMarkdownPath

        if (_currentMarkdownPath == null) return;

        string fileName = Path.GetFileName(path);
        // Assuming relative links are just filenames or relative paths.
        // If the browser resolved it to the temp dir, we just want the relative part.
        // But extracting the relative part from the temp path is hard if we don't know the temp root.

        // Simple approach: Take the filename and look in the current markdown's directory.
        // Better approach: If the path contains "RequestTracker_Cache", strip it and the prefix.

        // Actually, pandoc generates relative links.
        // If I am at "doc.html" in "temp/", and link is "sub/next.md", browser goes to "temp/sub/next.md".
        // I want "original_dir/sub/next.md".

        string? currentDir = Path.GetDirectoryName(_currentMarkdownPath);
        if (currentDir == null) return;

        // Try to handle navigation relative to the current markdown file's directory
        // The path might be absolute (e.g. temp dir) or relative
        // e.g. C:\Users\kingd\AppData\Local\Temp\RequestTracker_Cache\copilot\session-2026-02-02-073300\session-log.md
        // But we want to map it to E:\github\FunWasHad\docs\sessions\copilot\session-2026-02-02-073300\session-log.md

        // If the path contains "RequestTracker_Cache", we should try to extract the relative part
        // But since we flatten the cache (or do we?), wait, let's check GenerateAndNavigate.
        // We generate into tempDir directly: Path.Combine(tempDir, tempFileName);
        // tempFileName is Name_Hash.html.
        // So all HTML files are in the root of RequestTracker_Cache.

        // However, pandoc generated links might be relative.
        // If README.md links to "copilot/session.md", browser tries to go to "RequestTracker_Cache/copilot/session.md"
        // In that case, 'path' will be ".../RequestTracker_Cache/copilot/session.md"

        string tempDir = Path.Combine(Path.GetTempPath(), "RequestTracker_Cache");
        if (path.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
        {
             // It's inside our temp dir structure.
             // Get the relative path from tempDir
             string relativePath = Path.GetRelativePath(tempDir, path);

             // Now combine with the root target path? Or current markdown's directory?
             // Since we don't replicate directory structure in temp (we flatten or hash), this is tricky.
             // But wait, if pandoc generates relative links, and the browser resolves them against the base URL (the temp html file),
             // then "copilot/session.md" becomes "temp/copilot/session.md".

             // If we assume the link was relative to the markdown file, we should combine it with the markdown's directory.

             string targetPath = Path.Combine(currentDir, relativePath);

             // Normalize path
             targetPath = Path.GetFullPath(targetPath);

             if (File.Exists(targetPath))
             {
                 SelectNodeByPath(targetPath);
                 return;
             }
        }

        // Fallback: Try just the filename in current dir
        string targetPathSimple = Path.Combine(currentDir, fileName);
        if (File.Exists(targetPathSimple))
        {
            SelectNodeByPath(targetPathSimple);
        }
        else
        {
            Console.WriteLine($"Could not resolve navigation: {path} -> {targetPathSimple}");
        }
    }

    private void SelectNodeByPath(string path)
    {
        // Recursive search for the node
        var node = FindNode(Nodes, path);
        if (node != null)
        {
            SelectedNode = node;
            ExpandToNode(Nodes, node);
        }
        else
        {
             Console.WriteLine($"Node not found in tree for: {path}");
        }
    }

    private void ExpandToNode(ObservableCollection<FileNode> nodes, FileNode target)
    {
        foreach (var node in nodes)
        {
            if (node == target) return;

            if (ContainsNode(node, target))
            {
                node.IsExpanded = true;
                ExpandToNode(node.Children, target);
                return;
            }
        }
    }

    private bool ContainsNode(FileNode parent, FileNode target)
    {
        foreach (var child in parent.Children)
        {
            if (child == target) return true;
            if (ContainsNode(child, target)) return true;
        }
        return false;
    }

    private FileNode? FindNode(ObservableCollection<FileNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) return node;

            var found = FindNode(node.Children, path);
            if (found != null) return found;
        }
        return null;
    }

    private static JsonTreeNode? FindJsonNodeBySourcePath(ObservableCollection<JsonTreeNode> nodes, string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return null;
        foreach (var node in nodes)
        {
            if (string.Equals(node.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                return node;
            var found = FindJsonNodeBySourcePath(node.Children, sourcePath);
            if (found != null) return found;
        }
        return null;
    }

    private static void ExpandToJsonNode(ObservableCollection<JsonTreeNode> nodes, JsonTreeNode target)
    {
        foreach (var node in nodes)
        {
            if (node == target) return;
            if (ContainsJsonNode(node, target))
            {
                node.IsExpanded = true;
                ExpandToJsonNode(node.Children, target);
                return;
            }
        }
    }

    private static bool ContainsJsonNode(JsonTreeNode parent, JsonTreeNode target)
    {
        foreach (var child in parent.Children)
        {
            if (child == target) return true;
            if (ContainsJsonNode(child, target)) return true;
        }
        return false;
    }


    /// <summary>Dispatches an action to the UI thread. Use for any property/collection updates from background work.</summary>
    private void DispatchToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(() => action());
    }

    private void GenerateAndNavigate(FileNode? node)
    {
         CancelMarkdownPreview();
         IsPreviewOpenedInBrowser = false;
         CurrentPreviewHtmlPath = null;
         CurrentPreviewMarkdownText = "";

         // Reset details view
         IsRequestDetailsVisible = false;

         if (node == null)
         {
             return;
         }

         if (node.Path == "ALL_JSON_VIRTUAL_NODE")
         {
             IsMarkdownVisible = false;
             IsJsonVisible = true;
             ArchiveCommand.NotifyCanExecuteChanged();
             LoadAllJson();
             return;
         }

         if (node.IsDirectory) return;

         if (node.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
         {
             IsMarkdownVisible = false;
             IsJsonVisible = true;
             ArchiveCommand.NotifyCanExecuteChanged();
             LoadJson(node.Path);
             return;
         }

         if (node.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
         {
             IsMarkdownVisible = true;
             IsJsonVisible = false;
             _currentMarkdownPath = node.Path;
             ArchiveCommand.NotifyCanExecuteChanged();
             NotifyContextConsumer();

             string hash = node.Path.GetHashCode().ToString("X");
             string tempFileName = $"{Path.GetFileNameWithoutExtension(node.Path)}_{hash}.html";
             string tempDir = Path.Combine(Path.GetTempPath(), "RequestTracker_Cache");
             string tempPath = Path.Combine(tempDir, tempFileName);
             Directory.CreateDirectory(tempDir);

             bool needsGeneration = true;
             if (File.Exists(tempPath))
             {
                 var srcTime = File.GetLastWriteTimeUtc(node.Path);
                 var dstTime = File.GetLastWriteTimeUtc(tempPath);
                 if (srcTime < dstTime) needsGeneration = false;
             }

             DispatchToUi(() => StatusMessage = needsGeneration ? "Generating preview..." : "Loading preview...");

             _markdownPreviewCts?.Dispose();
             _markdownPreviewCts = new CancellationTokenSource();
             var token = _markdownPreviewCts.Token;
             string pathForThisPreview = node.Path;

             _ = Task.Run(async () =>
             {
                 try
                 {
                     if (needsGeneration)
                     {
                         Console.WriteLine($"Generating HTML for {pathForThisPreview}...");
                         bool ok = await ConvertMarkdownToHtmlAsync(pathForThisPreview, tempPath);
                         if (token.IsCancellationRequested) return;
                         if (!ok)
                         {
                             DispatchToUi(() =>
                             {
                                 if (token.IsCancellationRequested) return;
                                 Console.WriteLine("Failed to generate HTML");
                                 SetFallbackHtmlSource("Markdown preview unavailable. Install <a href=\"https://pandoc.org/\">pandoc</a> to generate HTML preview.");
                             });
                             return;
                         }
                         Console.WriteLine($"Generated: {tempPath}");
                     }

                     if (token.IsCancellationRequested) return;

                     string html = await File.ReadAllTextAsync(tempPath);
                     string pathForBrowser = tempPath;
                     string markdownText = "";
                     try
                     {
                         markdownText = await File.ReadAllTextAsync(pathForThisPreview);
                     }
                     catch
                     {
                         // Ignore; we'll show empty if file can't be read
                     }

                     if (token.IsCancellationRequested) return;

                     DispatchToUi(() =>
                     {
                         if (token.IsCancellationRequested) return;
                         if (_currentMarkdownPath != pathForThisPreview) return;

                         try
                         {
                             if (string.IsNullOrWhiteSpace(html))
                             {
                                 SetFallbackHtmlSource("Generated HTML was empty.");
                                 return;
                             }
                             CurrentPreviewHtmlPath = pathForBrowser;
                             CurrentPreviewMarkdownText = markdownText ?? "";
                             IsPreviewOpenedInBrowser = true;
                         }
                         catch (Exception ex)
                         {
                             Console.WriteLine($"Error loading HTML: {ex.Message}");
                             SetFallbackHtmlSource("Could not load preview.");
                         }
                     });
                 }
                 catch (OperationCanceledException)
                 {
                     // Task was cancelled (e.g. user navigated away)
                 }
                 catch (Exception ex)
                 {
                     if (!token.IsCancellationRequested)
                     {
                         Console.WriteLine($"Error generating preview: {ex.Message}");
                         DispatchToUi(() => SetFallbackHtmlSource("Could not load preview."));
                     }
                 }
             });
             return;
         }
    }

    /// <summary>Removes duplicate entries by RequestId (case-insensitive). Keeps the first occurrence when ordered by timestamp descending (newest wins). Entries with empty RequestId are not deduplicated.</summary>
    private static List<UnifiedRequestEntry> DeduplicateUnifiedEntries(List<UnifiedRequestEntry> orderedByNewestFirst)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<UnifiedRequestEntry>();
        foreach (var e in orderedByNewestFirst)
        {
            if (!string.IsNullOrWhiteSpace(e.RequestId))
            {
                if (!seenIds.Add(e.RequestId.Trim()))
                    continue;
            }
            result.Add(e);
        }
        return result;
    }

    private void LoadAllJson()
    {
        var rootPath = GetResolvedTargetPath();
        if (!Directory.Exists(rootPath))
        {
            StatusMessage = "Root directory not found.";
            return;
        }

        DispatchToUi(() => StatusMessage = "Aggregating all JSON files...");

        Task.Run(async () =>
        {
            try
            {
                var jsonFiles = Directory.GetFiles(rootPath, "*.json", SearchOption.AllDirectories)
                    .Where(f => !IsArchivedName(Path.GetFileName(f)))
                    .ToArray();
                var unifiedLogs = new List<UnifiedSessionLog>();
                int totalRequests = 0;
                int totalFiles = jsonFiles.Length;
                int processed = 0;

                foreach (var file in jsonFiles)
                {
                    try
                    {
                        var text = await File.ReadAllTextAsync(file);
                        var (unified, count) = TryParseFileToUnifiedLog(text);
                        if (unified != null && count >= 0)
                        {
                            unifiedLogs.Add(unified);
                            totalRequests += count;
                        }
                    }
                    catch
                    {
                        // Skip unreadable files
                    }
                    finally
                    {
                        processed++;
                        if (totalFiles > 0 && (processed % 5 == 0 || processed == totalFiles))
                        {
                            var p = processed;
                            var t = totalFiles;
                            DispatchToUi(() => StatusMessage = $"Reading files... {p}/{t}");
                        }
                    }
                }

                foreach (var log in unifiedLogs)
                {
                    var agent = string.IsNullOrWhiteSpace(log.SourceType) ? "Unknown" : log.SourceType.Trim();
                    if (log.Entries == null) continue;
                    foreach (var entry in log.Entries)
                    {
                        if (entry != null && string.IsNullOrWhiteSpace(entry.Agent))
                            entry.Agent = agent;
                    }
                }

                var allEntries = unifiedLogs.SelectMany(l => l.Entries).OrderByDescending(e => e.Timestamp).ToList();
                var deduped = DeduplicateUnifiedEntries(allEntries);

                var masterLog = new UnifiedSessionLog
                {
                    SourceType = "Aggregated",
                    SessionId = "ALL-JSON",
                    Title = "All Requests",
                    Model = "Various",
                    Started = DateTime.Now,
                    Status = "Aggregated",
                    EntryCount = deduped.Count,
                    Entries = deduped,
                    TotalTokens = unifiedLogs.Sum(l => l.TotalTokens)
                };

                var reqCount = deduped.Count;
                var fileCount = jsonFiles.Length;
                DispatchToUi(() =>
                {
                    try
                    {
                        BuildUnifiedSummaryAndIndex(masterLog);
                        StatusMessage = $"Loaded {reqCount} requests from {fileCount} files.";
                    }
                    catch (Exception ex)
                    {
                        StatusMessage = $"Error building UI: {ex.Message}";
                        Console.WriteLine($"UI Build Error: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                DispatchToUi(() => StatusMessage = $"Error aggregating JSON: {msg}");
                Console.WriteLine($"Aggregation Error: {ex}");
            }
        });
    }

    /// <summary>
    /// Opens an HTML file in the system default browser.
    /// Opens the generated HTML in the default browser (pandoc output).
    /// </summary>
    private static void OpenHtmlInDefaultBrowser(string htmlFilePath)
    {
        if (!File.Exists(htmlFilePath)) return;
        try
        {
            var path = Path.GetFullPath(htmlFilePath);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = path,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not open in browser: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenPreviewInBrowser()
    {
        if (!string.IsNullOrEmpty(CurrentPreviewHtmlPath))
            OpenHtmlInDefaultBrowser(CurrentPreviewHtmlPath);
    }

    [RelayCommand]
    private void OpenAgentConfig()
    {
        AgentConfigIo.EnsureExists();
        OpenFileInDefaultEditor(AgentConfigIo.GetFilePath(), "config");
    }

    [RelayCommand]
    private void OpenPromptTemplates()
    {
        PromptTemplatesIo.EnsureExists();
        OpenFileInDefaultEditor(PromptTemplatesIo.GetFilePath(), "prompts");
    }

    private void OpenFileInDefaultEditor(string path, string label)
    {
        if (!File.Exists(path))
        {
            SetStatus($"Could not open {label}: file not found.");
            return;
        }
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Use cmd /c start so the file reliably opens with the default app (Process.Start with
                // the file path can succeed without opening when launched from IDE or certain contexts).
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" \"{fullPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = fullPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            SetStatus($"Opened {label}: {path}");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open {label}: {ex.Message}");
        }
    }

    private bool CanArchive() =>
        _currentMarkdownPath != null &&
        File.Exists(_currentMarkdownPath) &&
        IsMarkdownVisible &&
        !IsArchivedName(Path.GetFileName(_currentMarkdownPath));

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private void Archive()
    {
        string? path = _currentMarkdownPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;
        string name = Path.GetFileName(path);
        string newName = "Archived-" + name;
        string newPath = Path.Combine(dir, newName);
        Task.Run(() =>
        {
            try
            {
                File.Move(path, newPath);
                DispatchToUi(() =>
                {
                    _currentMarkdownPath = null;
                    RebuildFileTree();
                    ArchiveCommand.NotifyCanExecuteChanged();
                    StatusMessage = $"Archived: {newName}";
                });
            }
            catch (Exception ex)
            {
                DispatchToUi(() =>
                {
                    StatusMessage = $"Archive failed: {ex.Message}";
                    ArchiveCommand.NotifyCanExecuteChanged();
                });
            }
        });
    }

    /// <summary>Opens (navigates to) the tree node. Used by tree context menu.</summary>
    [RelayCommand]
    private void OpenTreeItem(FileNode? node)
    {
        if (node == null) return;
        SelectedNode = node;
        ExpandToNode(Nodes, node);
    }

    /// <summary>Archives the file represented by the tree node by renaming to archive-&lt;name&gt;. Used by tree context menu. Not available for All JSON or directories.</summary>
    [RelayCommand(CanExecute = nameof(CanArchiveTreeItem))]
    private void ArchiveTreeItem(FileNode? node)
    {
        if (node == null || node.Path == "ALL_JSON_VIRTUAL_NODE" || node.IsDirectory) return;
        string path = node.Path;
        if (!File.Exists(path)) return;
        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;
        string name = Path.GetFileName(path);
        if (IsArchivedName(name)) return;
        string newName = "archive-" + name;
        string newPath = Path.Combine(dir, newName);
        Task.Run(() =>
        {
            try
            {
                File.Move(path, newPath);
                DispatchToUi(() =>
                {
                    if (SelectedNode == node)
                        _currentMarkdownPath = null;
                    RebuildFileTree();
                    StatusMessage = $"Archived: {newName}";
                });
            }
            catch (Exception ex)
            {
                DispatchToUi(() => StatusMessage = $"Archive failed: {ex.Message}");
            }
        });
    }

    private static bool CanArchiveTreeItem(FileNode? node) =>
        node != null &&
        node.Path != "ALL_JSON_VIRTUAL_NODE" &&
        !node.IsDirectory &&
        !IsArchivedName(Path.GetFileName(node.Path));

    /// <summary>Rebuilds the file tree on a background thread and applies on UI; selects All JSON.</summary>
    private void RebuildFileTree()
    {
        string resolvedPath = GetResolvedTargetPath();
        Task.Run(() =>
        {
            try
            {
                var (allJsonNode, rootDto) = BuildTreeOffThread(resolvedPath);
                DispatchToUi(() =>
                {
                    try
                    {
                        Nodes.Clear();
                        Nodes.Add(allJsonNode);
                        if (rootDto != null)
                        {
                            var root = ApplyTreeDtoToNodes(rootDto);
                            Nodes.Add(root);
                            SelectedNode = allJsonNode;
                        }
                        else
                        {
                            Nodes.Add(new FileNode(resolvedPath, true) { Name = "Directory not found" });
                            SelectedNode = allJsonNode;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"RebuildFileTree apply failed: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"RebuildFileTree build failed: {ex}");
                DispatchToUi(() => StatusMessage = $"Rebuild failed: {ex.Message}");
            }
        });
    }

    private static string? ResolveCssPath()
    {
        string cssPath = Path.Combine(AppContext.BaseDirectory, "Assets", "styles.css");
        if (File.Exists(cssPath)) return cssPath;
        string sourceCss = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "RequestTracker", "Assets", "styles.css");
        if (File.Exists(sourceCss)) return Path.GetFullPath(sourceCss);
        string hardcoded = @"E:\github\RequestTracker\src\RequestTracker\Assets\styles.css";
        return File.Exists(hardcoded) ? hardcoded : null;
    }

    private bool ConvertMarkdownToHtml(string srcPath, string destPath)
    {
        try
        {
            string? cssPath = ResolveCssPath();
            if (cssPath == null) return false;

            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "pandoc";
            process.StartInfo.Arguments = $"\"{srcPath}\" -f markdown -t html -s --css \"{cssPath}\" --metadata title=\"Request Tracker\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            // Read before WaitForExit to avoid deadlock when pipe buffer fills
            string htmlContent = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(htmlContent))
            {
                Console.WriteLine($"Pandoc failed: ExitCode={process.ExitCode}. stderr: {stderr}");
                return false;
            }
            File.WriteAllText(destPath, htmlContent);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting: {ex}");
            return false;
        }
    }

    private static async Task<bool> ConvertMarkdownToHtmlAsync(string srcPath, string destPath)
    {
        try
        {
            string? cssPath = ResolveCssPath();
            if (cssPath == null)
            {
                Console.WriteLine("ConvertMarkdownToHtmlAsync: CSS path not found");
                return false;
            }

            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "pandoc";
            process.StartInfo.Arguments = $"\"{srcPath}\" -f markdown -t html -s --css \"{cssPath}\" --metadata title=\"Request Tracker\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            // Read output before waiting for exit to avoid deadlock (full pipe blocks process)
            Task<string> outTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string htmlContent = await outTask;
            string stderr = await errTask;

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"Pandoc failed: ExitCode={process.ExitCode}. stderr: {stderr}");
                return false;
            }
            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                Console.WriteLine($"Pandoc produced no output. stderr: {stderr}");
                return false;
            }
            await File.WriteAllTextAsync(destPath, htmlContent);
            Console.WriteLine($"Pandoc completed: wrote {htmlContent.Length} chars to {destPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting: {ex}");
            return false;
        }
    }

    private void SetFallbackHtmlSource(string message)
    {
        CurrentPreviewMarkdownText = message;
        IsPreviewOpenedInBrowser = true;
    }

    private void LoadJson(string path)
    {
        DispatchToUi(() => StatusMessage = "Loading JSON...");

        Task.Run(() =>
        {
            try
            {
                string jsonContent = File.ReadAllText(path);
                var jsonNode = JsonNode.Parse(jsonContent);
                string schemaType = "Unknown";
                var summary = new JsonLogSummary();
                UnifiedSessionLog? unifiedLog = null;

                if (jsonNode is JsonObject obj)
                {
                    if (obj.ContainsKey("sessionId") && obj.ContainsKey("statistics"))
                    {
                        schemaType = "Copilot Session Log";
                        var model = JsonSerializer.Deserialize<CopilotSessionLog>(jsonContent, CopilotJsonOptions);
                        if (model != null)
                        {
                            BuildCopilotSummaryAndIndex(model, summary);
                            unifiedLog = UnifiedLogFactory.Create(model);
                        }
                    }
                    else if (HasKeyCI(obj, "entries") && HasKeyCI(obj, "session"))
                    {
                        schemaType = "Cursor Request Log";
                        var model = JsonSerializer.Deserialize<CursorRequestLog>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (model != null)
                        {
                            BuildCursorSummaryAndIndex(model, summary);
                            unifiedLog = UnifiedLogFactory.Create(model);
                            if (unifiedLog != null)
                                FillActionsFromRawJson(jsonContent, unifiedLog);
                        }
                    }
                    else if (IsSingleCursorRequest(obj))
                    {
                        schemaType = "Cursor Request (single)";
                        var singleEntry = JsonSerializer.Deserialize<CursorRequestEntry>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (singleEntry != null)
                        {
                            var syntheticLog = new CursorRequestLog
                            {
                                Session = "Single Request",
                                Description = singleEntry.RequestId ?? "Request",
                                Entries = new List<CursorRequestEntry> { singleEntry }
                            };
                            unifiedLog = UnifiedLogFactory.Create(syntheticLog);
                            if (unifiedLog != null && unifiedLog.Entries.Count > 0 && unifiedLog.Entries[0].Actions.Count == 0)
                                FillActionsFromSingleEntryJson(jsonContent, unifiedLog);
                        }
                    }
                    else if (HasKeyCI(obj, "entries") && HasKeyCI(obj, "sourceType"))
                    {
                        schemaType = "Unified Session Log";
                        var unified = JsonSerializer.Deserialize<UnifiedSessionLog>(jsonContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (unified?.Entries != null)
                        {
                            UnifiedLogFactory.EnsureOriginalEntriesSet(unified);
                            unifiedLog = unified;
                        }
                    }
                }

                DispatchToUi(() => ApplyLoadedJsonToUi(path, jsonNode, schemaType, summary, unifiedLog));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing JSON: {ex}");
                var msg = ex.Message;
                DispatchToUi(() =>
                {
                    JsonTree.Clear();
                    SearchableEntries.Clear();
                    FilteredSearchEntries.Clear();
                    UpdateDistinctFilterValues();
                    JsonLogSummary = new JsonLogSummary();
                    JsonTree.Add(new JsonTreeNode("Error", msg, "Error"));
                    StatusMessage = $"Error loading JSON: {msg}";
                });
            }
        });
    }

    private void ApplyLoadedJsonToUi(string path, JsonNode? jsonNode, string schemaType, JsonLogSummary summary, UnifiedSessionLog? unifiedLog)
    {
        JsonTree.Clear();
        SearchableEntries.Clear();
        UpdateDistinctFilterValues();
        JsonLogSummary = new JsonLogSummary();

        if (unifiedLog != null)
        {
            schemaType = $"{unifiedLog.SourceType} (Unified)";
            BuildUnifiedSummaryAndIndex(unifiedLog, summary);
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
            var unifiedNode = JsonSerializer.SerializeToNode(unifiedLog, options);
            summary.SummaryLines.Clear();
            summary.SummaryLines.Add($"Type: {unifiedLog.SourceType}");
            summary.SummaryLines.Add($"Session: {unifiedLog.SessionId}");
            summary.SummaryLines.Add($"Entries: {unifiedLog.EntryCount}");
            if (!string.IsNullOrEmpty(unifiedLog.Model))
                summary.SummaryLines.Add($"Model: {unifiedLog.Model}");
            if (unifiedLog.LastUpdated.HasValue)
                summary.SummaryLines.Add($"Last Updated: {unifiedLog.LastUpdated}");
            JsonLogSummary = summary;
            var root = new JsonTreeNode("Root", schemaType, "Object");
            root.IsExpanded = true;
            BuildJsonTree(unifiedNode, root, null);
            JsonTree.Add(root);
        }
        else
        {
            summary.SchemaType = schemaType;
            summary.SummaryLines.Clear();
            summary.SummaryLines.Add($"Schema: {schemaType}");
            summary.SummaryLines.Add($"Total: {summary.TotalCount}");
            if (!string.IsNullOrEmpty(summary.StatsByModel)) summary.SummaryLines.Add(summary.StatsByModel);
            if (!string.IsNullOrEmpty(summary.StatsBySuccess)) summary.SummaryLines.Add(summary.StatsBySuccess);
            if (!string.IsNullOrEmpty(summary.StatsCostOrTokens)) summary.SummaryLines.Add(summary.StatsCostOrTokens);
            JsonLogSummary = summary;
            var root = new JsonTreeNode("Root", schemaType, "Object");
            root.IsExpanded = true;
            BuildJsonTree(jsonNode, root, null);
            JsonTree.Add(root);
        }

        UpdateFilteredSearchEntries();
        StatusMessage = $"Loaded {Path.GetFileName(path)}";
    }

    /// <summary>
    /// Fills Actions on unified entries from raw JSON when the deserialized Cursor entry didn't have them
    /// (e.g. "Actions" vs "actions" or different structure). Ensures req-001-logging-system and others show actions.
    /// </summary>
    private static void FillActionsFromRawJson(string jsonContent, UnifiedSessionLog unifiedLog)
    {
        if (unifiedLog?.Entries == null || unifiedLog.Entries.Count == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;
            if (!TryGetPropertyCI(root, "entries", out var entriesProp) || entriesProp.ValueKind != JsonValueKind.Array)
                return;
            int i = 0;
            foreach (var entryEl in entriesProp.EnumerateArray())
            {
                if (i >= unifiedLog.Entries.Count) break;
                if (unifiedLog.Entries[i].Actions.Count == 0)
                {
                    var actions = UnifiedLogFactory.ParseActionsFromEntryElement(entryEl);
                    if (actions.Count > 0)
                        unifiedLog.Entries[i].Actions = new ObservableCollection<UnifiedAction>(actions);
                }
                i++;
            }
        }
        catch
        {
            // Ignore; we already have whatever the deserializer gave us
        }
    }

    private static bool TryGetPropertyCI(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (element.TryGetProperty(propertyName, out value)) return true;
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        return false;
    }

    private static bool HasKeyCI(JsonObject obj, string key)
    {
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsSingleCursorRequest(JsonObject obj)
    {
        if (!HasKeyCI(obj, "requestId")) return false;
        return HasKeyCI(obj, "exactRequest") || HasKeyCI(obj, "exactRequestNote") ||
               HasKeyCI(obj, "actions");
    }

    /// <summary>Parses a JSON file into a unified log using key-based format detection (matches single-file logic).
    /// Returns (unifiedLog, entryCount) or (null, -1) if not recognized.</summary>
    private static (UnifiedSessionLog? log, int entryCount) TryParseFileToUnifiedLog(string text)
    {
        try
        {
            var node = JsonNode.Parse(text);
            if (node is not JsonObject obj) return (null, -1);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // 1) Unified (entries + sourceType)
            if (HasKeyCI(obj, "entries") && HasKeyCI(obj, "sourceType"))
            {
                var unifiedLog = JsonSerializer.Deserialize<UnifiedSessionLog>(text, options);
                if (unifiedLog?.Entries != null)
                {
                    UnifiedLogFactory.EnsureOriginalEntriesSet(unifiedLog);
                    return (unifiedLog, unifiedLog.EntryCount);
                }
            }

            // 2) Cursor log (entries + session)
            if (HasKeyCI(obj, "entries") && HasKeyCI(obj, "session"))
            {
                var cursorLog = JsonSerializer.Deserialize<CursorRequestLog>(text, options);
                if (cursorLog?.Entries != null)
                {
                    var unified = UnifiedLogFactory.Create(cursorLog);
                    if (unified != null) return (unified, unified.EntryCount);
                }
            }

            // 3) Copilot (sessionId + statistics) — require non-empty Requests so Cursor files aren't mis-detected
            if (obj.ContainsKey("sessionId") && obj.ContainsKey("statistics"))
            {
                var copilotLog = JsonSerializer.Deserialize<CopilotSessionLog>(text, CopilotJsonOptions);
                if (copilotLog?.Requests != null && copilotLog.Requests.Count > 0)
                {
                    var unified = UnifiedLogFactory.Create(copilotLog);
                    if (unified != null) return (unified, unified.EntryCount);
                }
            }

            // 4) Single Cursor request
            if (IsSingleCursorRequest(obj))
            {
                var singleEntry = JsonSerializer.Deserialize<CursorRequestEntry>(text, options);
                if (singleEntry != null)
                {
                    var syntheticLog = new CursorRequestLog
                    {
                        Session = "Single Request",
                        Description = singleEntry.RequestId ?? "Request",
                        Entries = new List<CursorRequestEntry> { singleEntry }
                    };
                    var unified = UnifiedLogFactory.Create(syntheticLog);
                    if (unified != null) return (unified, unified.EntryCount);
                }
            }

            // 5) Older non-unified: entries array only (e.g. Cursor without "session")
            if (HasKeyCI(obj, "entries"))
            {
                var cursorLog = JsonSerializer.Deserialize<CursorRequestLog>(text, options);
                if (cursorLog?.Entries != null && cursorLog.Entries.Count > 0)
                {
                    var unified = UnifiedLogFactory.Create(cursorLog);
                    if (unified != null) return (unified, unified.EntryCount);
                }
            }

            return (null, -1);
        }
        catch
        {
            return (null, -1);
        }
    }

    private static void FillActionsFromSingleEntryJson(string jsonContent, UnifiedSessionLog unifiedLog)
    {
        if (unifiedLog?.Entries == null || unifiedLog.Entries.Count == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var actions = UnifiedLogFactory.ParseActionsFromEntryElement(doc.RootElement);
            if (actions.Count > 0)
                unifiedLog.Entries[0].Actions = new ObservableCollection<UnifiedAction>(actions);
        }
        catch { /* ignore */ }
    }

    private void BuildCopilotSummaryAndIndex(CopilotSessionLog log, JsonLogSummary summary)
    {
        var requests = log.Requests ?? new List<CopilotRequestEntry>();
        summary.TotalCount = requests.Count;
        summary.SearchIndex = new List<SearchableEntry>();

        var byModel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < requests.Count; i++)
        {
            var r = requests[i];
            var model = r.Model ?? "(unknown)";
            byModel[model] = byModel.GetValueOrDefault(model) + 1;

            var display = (r.Slug ?? r.Title ?? r.UserRequest ?? "").Trim();
            if (display.Length > 60) display = display.Substring(0, 57) + "...";
            var searchText = string.Join(" ", r.RequestId ?? "", r.Slug ?? "", r.Title ?? "", r.UserRequest ?? "", r.Model ?? "", r.Timestamp?.ToString() ?? "");
            summary.SearchIndex.Add(new SearchableEntry
            {
                RequestId = r.RequestId ?? "",
                DisplayText = string.IsNullOrEmpty(display) ? $"Request {i + 1}" : display,
                Timestamp = r.Timestamp?.ToString("o") ?? "",
                Model = r.Model ?? "",
                EntryIndex = i,
                SourcePath = $"requests[{i}]",
                SearchText = searchText
            });
        }
        summary.StatsByModel = "By model: " + string.Join(", ", byModel.Select(kv => $"{kv.Key}: {kv.Value}"));
        if (log.Statistics != null)
        {
            var s = log.Statistics;
            summary.StatsBySuccess = $"Completed: {s.CompletedCount}, In progress: {s.InProgressCount}, Failed: {s.FailedCount}";
            if (s.AverageSuccessScore.HasValue) summary.StatsBySuccess += $", Avg score: {s.AverageSuccessScore:F1}";
            if (s.TotalNetTokens.HasValue) summary.StatsCostOrTokens = $"Total tokens: {s.TotalNetTokens:N0}";
            if (s.TotalNetPremiumRequests.HasValue) summary.StatsCostOrTokens += $", Premium requests: {s.TotalNetPremiumRequests:N0}";
        }
    }

    private void BuildCursorSummaryAndIndex(CursorRequestLog log, JsonLogSummary summary)
    {
        var entries = log.Entries ?? new List<CursorRequestEntry>();
        summary.TotalCount = entries.Count;
        summary.SearchIndex = new List<SearchableEntry>();

        var byModel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var successScores = new List<int>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var model = e.Model ?? "(unknown)";
            byModel[model] = byModel.GetValueOrDefault(model) + 1;
            if (e.Successfulness?.Score.HasValue == true) successScores.Add(e.Successfulness.Score!.Value);

            var display = (e.ExactRequest ?? e.ExactRequestNote ?? "").Trim();
            if (display.Length > 60) display = display.Substring(0, 57) + "...";
            var searchText = string.Join(" ", e.RequestId ?? "", e.ExactRequest ?? "", e.ExactRequestNote ?? "", e.Model ?? "", e.Timestamp ?? "",
                e.Successfulness?.Score?.ToString() ?? "", e.ActionsTaken ?? new List<string>());
            summary.SearchIndex.Add(new SearchableEntry
            {
                RequestId = e.RequestId ?? "",
                DisplayText = string.IsNullOrEmpty(display) ? $"Entry {i + 1}" : display,
                Timestamp = e.Timestamp ?? "",
                Model = e.Model ?? "",
                EntryIndex = i,
                SourcePath = $"entries[{i}]",
                SearchText = searchText
            });
        }
        summary.StatsByModel = "By model: " + string.Join(", ", byModel.Select(kv => $"{kv.Key}: {kv.Value}"));
        if (successScores.Count > 0)
        {
            summary.StatsBySuccess = $"Success scores: min {successScores.Min()}, max {successScores.Max()}, avg {successScores.Average():F1}";
        }
    }

    private void BuildUnifiedSummaryAndIndex(UnifiedSessionLog log)
    {
         var summary = new JsonLogSummary();
         BuildUnifiedSummaryAndIndex(log, summary);

         // Update Summary Header for Aggregated view
         summary.SummaryLines.Clear();
         summary.SummaryLines.Add($"Type: {log.SourceType}");
         summary.SummaryLines.Add($"Total Entries: {log.EntryCount}");
         summary.SummaryLines.Add($"Total Tokens: {log.TotalTokens:N0}");
         summary.SummaryLines.Add($"Aggregated at: {log.Started}");

         JsonLogSummary = summary;

         // Build Tree
         JsonTree.Clear();
         var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
         var unifiedNode = JsonSerializer.SerializeToNode(log, options);
         var root = new JsonTreeNode("Root", "Aggregated Unified Log", "Object");
         root.IsExpanded = true;
         BuildJsonTree(unifiedNode, root, null);
         JsonTree.Add(root);

         UpdateFilteredSearchEntries();
    }

    private void BuildUnifiedSummaryAndIndex(UnifiedSessionLog log, JsonLogSummary summary)
    {
        summary.SearchIndex = new List<SearchableEntry>();
        var entries = log.Entries;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var display = (e.QueryText ?? "").Trim(); // Updated field name
            if (display.Length > 60) display = display.Substring(0, 57) + "...";

            var searchText = string.Join(" ",
                e.RequestId ?? "",
                e.QueryText ?? "",  // Updated field name
                e.QueryTitle ?? "", // Updated field name
                e.Model ?? "",
                e.Agent ?? "",
                e.Timestamp?.ToString() ?? "",
                e.Status ?? "");

            summary.SearchIndex.Add(new SearchableEntry
            {
                RequestId = e.RequestId ?? "",
                DisplayText = string.IsNullOrEmpty(display) ? $"Entry {i + 1}" : display,
                Timestamp = e.Timestamp?.ToString("o") ?? "",
                Model = e.Model ?? "",
                Agent = e.Agent ?? "",
                EntryIndex = i,
                SourcePath = $"entries[{i}]", // Matches Unified Model structure
                SearchText = searchText,
                UnifiedEntry = e
            });
        }
        var sorted = summary.SearchIndex.OrderByDescending(e => e.SortableTimestamp ?? DateTime.MinValue).ToList();
        SearchableEntries = new ObservableCollection<SearchableEntry>(sorted);
        UpdateDistinctFilterValues();
    }

    private void UpdateDistinctFilterValues()
    {
        var entries = SearchableEntries ?? new ObservableCollection<SearchableEntry>();
        var requestIds = new List<string> { "" };
        requestIds.AddRange(entries.Select(e => e.RequestId ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        var displayTexts = new List<string> { "" };
        displayTexts.AddRange(entries.Select(e => e.DisplayText ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        var models = new List<string> { "" };
        models.AddRange(entries.Select(e => e.Model ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        var agents = new List<string> { "" };
        agents.AddRange(entries.Select(e => e.Agent ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        var timestamps = new List<string> { "" };
        timestamps.AddRange(entries.Select(e => e.TimestampDisplay ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

        DistinctRequestIds = new ObservableCollection<string>(requestIds);
        DistinctDisplayTexts = new ObservableCollection<string>(displayTexts);
        DistinctModels = new ObservableCollection<string>(models);
        DistinctAgents = new ObservableCollection<string>(agents);
        DistinctTimestamps = new ObservableCollection<string>(timestamps);
    }

    private void BuildJsonTree(JsonNode? node, JsonTreeNode parent, string? pathPrefix)
    {
        if (node == null)
        {
            parent.Children.Add(new JsonTreeNode("null", "null", "null"));
            return;
        }

        if (node is JsonObject jsonObj)
        {
            foreach (var property in jsonObj)
            {
                string type = property.Value?.GetType().Name ?? "null";
                if (property.Value is JsonArray) type = "Array";
                else if (property.Value is JsonObject) type = "Object";
                else if (property.Value is JsonValue) type = "Value";

                var childKey = property.Key;
                var childPath = string.IsNullOrEmpty(pathPrefix) ? childKey : pathPrefix + "." + childKey;
                var child = new JsonTreeNode(childKey, "", type);
                if (parent.Name == "Root") child.IsExpanded = true;

                BuildJsonTree(property.Value, child, childPath);

                if (property.Value is JsonValue val)
                {
                    child.Value = val.ToString();
                }

                parent.Children.Add(child);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            IEnumerable<JsonNode?> items = jsonArray;

            // Sort requests/entries by timestamp descending if applicable
            if (parent.Name.Equals("requests", StringComparison.OrdinalIgnoreCase) ||
                parent.Name.Equals("entries", StringComparison.OrdinalIgnoreCase))
            {
                items = jsonArray.OrderByDescending(n => {
                     if (n is JsonObject obj)
                     {
                         if (obj.TryGetPropertyValue("timestamp", out var tsNode) && tsNode is JsonValue tsVal)
                         {
                             if (tsVal.TryGetValue(out DateTime dt)) return dt;
                             if (tsVal.TryGetValue(out string? s) && DateTime.TryParse(s, out var dt2)) return dt2;

                             // Try numeric timestamp (Unix seconds or milliseconds)
                             if (tsVal.TryGetValue(out long tsLong))
                             {
                                 // Assume milliseconds if large, seconds if small
                                 // 2020-01-01 is ~1.5 billion seconds or 1.5 trillion ms
                                 if (tsLong > 1000000000000) return DateTimeOffset.FromUnixTimeMilliseconds(tsLong).UtcDateTime;
                                 return DateTimeOffset.FromUnixTimeSeconds(tsLong).UtcDateTime;
                             }
                             // Try parsing string as long
                             if (tsVal.TryGetValue(out string? sLong) && long.TryParse(sLong, out var tsLong2))
                             {
                                 if (tsLong2 > 1000000000000) return DateTimeOffset.FromUnixTimeMilliseconds(tsLong2).UtcDateTime;
                                 return DateTimeOffset.FromUnixTimeSeconds(tsLong2).UtcDateTime;
                             }
                         }
                     }
                     return DateTime.MinValue;
                }).ToList();
            }

            int index = 0;
            foreach (var item in items)
            {
                var itemPath = pathPrefix + "[" + index + "]";
                var child = (pathPrefix == "requests" || pathPrefix == "entries")
                    ? new JsonTreeNode($"[{index}]", "", "Item", itemPath)
                    : new JsonTreeNode($"[{index}]", "", "Item");

                string timestampPrefix = "";
                if (item is JsonObject objTS && objTS.TryGetPropertyValue("timestamp", out var tsNode))
                {
                    // Format timestamp nicely if possible
                    string tsStr = tsNode?.ToString() ?? "";
                    if (tsNode is JsonValue tsVal)
                    {
                         if (tsVal.TryGetValue(out DateTime dt))
                         {
                             if (dt.Kind == DateTimeKind.Utc) dt = dt.ToLocalTime();
                             else if (dt.Kind == DateTimeKind.Unspecified) dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
                             tsStr = dt.ToString("MM/dd HH:mm");
                         }
                         else if (tsVal.TryGetValue(out long l))
                         {
                             // Try to format unix time
                             try {
                                 if (l > 1000000000000) tsStr = DateTimeOffset.FromUnixTimeMilliseconds(l).LocalDateTime.ToString("MM/dd HH:mm");
                                 else tsStr = DateTimeOffset.FromUnixTimeSeconds(l).LocalDateTime.ToString("MM/dd HH:mm");
                             } catch {}
                         }
                    }
                    timestampPrefix = $"[{tsStr}] ";
                }

                if (item is JsonObject objItem)
                {
                    foreach (var prop in objItem)
                    {
                        if (prop.Value is JsonValue propVal && propVal.TryGetValue(out string? text) && !string.IsNullOrEmpty(text))
                        {
                            if (text.Length > 50) text = text.Substring(0, 47) + "...";
                            child.Value = timestampPrefix + text;
                            break;
                        }
                    }
                }

                // If no text found but we have timestamp, show it
                if (string.IsNullOrEmpty(child.Value) && !string.IsNullOrEmpty(timestampPrefix))
                {
                    child.Value = timestampPrefix.Trim();
                }

                BuildJsonTree(item, child, itemPath);
                if (item is JsonValue val)
                {
                    child.Value = val.ToString();
                }
                parent.Children.Add(child);
                index++;
            }
        }
    }

    /// <summary>DTO for building the file tree on a background thread (no ObservableCollection).</summary>
    private sealed class TreeDto
    {
        public string Path { get; set; } = "";
        public bool IsDirectory { get; set; }
        public List<TreeDto> Children { get; } = new();
    }

    /// <summary>Builds the file tree on a background thread; returns allJson node and root DTO (or null if dir missing).</summary>
    private static (FileNode allJsonNode, TreeDto? rootDto) BuildTreeOffThread(string resolvedPath)
    {
        var allJsonNode = new FileNode("ALL_JSON_VIRTUAL_NODE", false) { Name = "All JSON" };
        if (!Directory.Exists(resolvedPath))
            return (allJsonNode, null);

        var rootDto = new TreeDto { Path = resolvedPath, IsDirectory = true };
        LoadChildrenDto(rootDto);
        return (allJsonNode, rootDto);
    }

    private static bool IsArchivedName(string name) =>
        name.StartsWith("archived-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("archive-", StringComparison.OrdinalIgnoreCase);

    private static void LoadChildrenDto(TreeDto node)
    {
        try
        {
            var dirInfo = new DirectoryInfo(node.Path);
            foreach (var dir in dirInfo.GetDirectories())
            {
                var child = new TreeDto { Path = dir.FullName, IsDirectory = true };
                LoadChildrenDto(child);
                node.Children.Add(child);
            }
            foreach (var file in dirInfo.GetFiles())
            {
                if (IsArchivedName(file.Name))
                    continue;
                if (file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                    file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    node.Children.Add(new TreeDto { Path = file.FullName, IsDirectory = false });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error accessing {node.Path}: {ex.Message}");
        }
    }

    /// <summary>Converts DTO tree to FileNode tree on the UI thread (ObservableCollection safe).</summary>
    private static FileNode ApplyTreeDtoToNodes(TreeDto dto)
    {
        var node = new FileNode(dto.Path, dto.IsDirectory);
        foreach (var childDto in dto.Children)
            node.Children.Add(ApplyTreeDtoToNodes(childDto));
        return node;
    }

    private void InitializeTree()
    {
        Nodes.Clear();
        string resolvedPath = GetResolvedTargetPath();
        var allJsonNode = new FileNode("ALL_JSON_VIRTUAL_NODE", false) { Name = "All JSON" };
        Nodes.Add(allJsonNode);
        if (!Directory.Exists(resolvedPath))
        {
            SetStatus($"Directory not found: {resolvedPath}");
            Nodes.Add(new FileNode(resolvedPath, true) { Name = "Directory not found" });
            SelectedNode = allJsonNode;
            return;
        }
        SetStatus($"Loaded: {resolvedPath}");
        var root = new FileNode(resolvedPath, true);
        LoadChildren(root);
        Nodes.Add(root);
        SelectedNode = allJsonNode;
    }

    private void LoadChildren(FileNode node)
    {
        try
        {
            var dirInfo = new DirectoryInfo(node.Path);

            foreach (var dir in dirInfo.GetDirectories())
            {
                var childNode = new FileNode(dir.FullName, true);
                LoadChildren(childNode);
                node.Children.Add(childNode);
            }

            foreach (var file in dirInfo.GetFiles())
            {
                if (IsArchivedName(file.Name))
                    continue;
                if (file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                    file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    node.Children.Add(new FileNode(file.FullName, false));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error accessing {node.Path}: {ex.Message}");
        }
    }

    private void SetupWatcher()
    {
        string resolvedPath = GetResolvedTargetPath();
        if (!Directory.Exists(resolvedPath)) return;

        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(resolvedPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            Filter = "*"
        };
        // Reduce dropped events when many files change at once (default 8KB can overflow)
        try { _watcher.InternalBufferSize = 65536; } catch { /* ignore on unsupported platforms */ }

        _watcher.Created += OnTreeChanged;
        _watcher.Deleted += OnTreeChanged;
        _watcher.Renamed += OnTreeChanged;
        _watcher.Changed += OnFileChanged;

        _watcher.EnableRaisingEvents = true;
    }

    public void SetStatus(string message)
    {
        StatusMessage = message;
    }

    private void OnTreeChanged(object sender, FileSystemEventArgs e)
    {
        string resolvedPath = GetResolvedTargetPath();
        Task.Run(() =>
        {
            try
            {
                var (allJsonNode, rootDto) = BuildTreeOffThread(resolvedPath);
                DispatchToUi(() =>
                {
                    try
                    {
                        Nodes.Clear();
                        Nodes.Add(allJsonNode);
                        if (rootDto != null)
                        {
                            var root = ApplyTreeDtoToNodes(rootDto);
                            Nodes.Add(root);
                            SelectedNode = allJsonNode;
                        }
                        else
                        {
                            Nodes.Add(new FileNode(resolvedPath, true) { Name = "Directory not found" });
                            SelectedNode = allJsonNode;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"OnTreeChanged apply failed: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnTreeChanged build failed: {ex}");
            }
        });
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // If the changed file is the current one, regenerate
            if (_currentMarkdownPath != null && e.FullPath.Equals(_currentMarkdownPath, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Detected change in current file: {e.FullPath}");

                // Log the change
                ChangeLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] Rebuilt: {Path.GetFileName(e.FullPath)}");

                // Invalidate cache by forcing generation (the date check in GenerateAndNavigate will handle it since file date is newer)
                // But we need to call GenerateAndNavigate with a node.
                var node = new FileNode(e.FullPath, false);
                GenerateAndNavigate(node);
            }
        });
    }
}
