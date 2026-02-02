using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const string TargetPath = @"E:\github\FunWasHad\docs\requests";

    /// <summary>
    /// Target path resolved for the current OS (Windows path on Windows, /mnt/... on Linux).
    /// </summary>
    private static string GetResolvedTargetPath() => PathConverter.ToDisplayPath(TargetPath);

    private FileSystemWatcher? _watcher;
    private readonly IClipboardService _clipboardService;

    [ObservableProperty]
    private ObservableCollection<FileNode> _nodes = new();

    [ObservableProperty]
    private FileNode? _selectedNode;

    [ObservableProperty]
    private Uri? _htmlSource;

    [ObservableProperty]
    private ObservableCollection<string> _changeLog = new();

    [ObservableProperty]
    private bool _isMarkdownVisible = true;

    [ObservableProperty]
    private bool _isJsonVisible = false;

    [ObservableProperty]
    private ObservableCollection<JsonTreeNode> _jsonTree = new();

    [ObservableProperty]
    private JsonLogSummary? _jsonLogSummary;

    [ObservableProperty]
    private ObservableCollection<SearchableEntry> _searchableEntries = new();

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private ObservableCollection<SearchableEntry> _filteredSearchEntries = new();

    [ObservableProperty]
    private SearchableEntry? _selectedSearchEntry;

    [ObservableProperty]
    private JsonTreeNode? _selectedJsonNode;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>
    /// True on Windows; false on Linux to avoid WebView creating a separate GTK window and blocking the main app window.
    /// </summary>
    public bool IsWebViewSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// True when WebView is not used (Linux); show placeholder instead.
    /// </summary>
    public bool IsWebViewPlaceholderVisible => !IsWebViewSupported;

    private string? _currentMarkdownPath;
    
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
        InitializeTree();
        SetupWatcher();
    }

    partial void OnSearchQueryChanged(string value)
    {
        UpdateFilteredSearchEntries();
    }

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
        if (string.IsNullOrEmpty(q))
        {
            FilteredSearchEntries = new ObservableCollection<SearchableEntry>(SearchableEntries);
            return;
        }
        var lower = q.ToLowerInvariant();
        var filtered = SearchableEntries
            .Where(e => (e.SearchText ?? "").ToLowerInvariant().Contains(lower) ||
                        (e.RequestId ?? "").ToLowerInvariant().Contains(lower) ||
                        (e.DisplayText ?? "").ToLowerInvariant().Contains(lower) ||
                        (e.Model ?? "").ToLowerInvariant().Contains(lower) ||
                        (e.Timestamp ?? "").ToLowerInvariant().Contains(lower))
            .ToList();
        FilteredSearchEntries = new ObservableCollection<SearchableEntry>(filtered);
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

    public bool ShouldInterceptNavigation(Uri url)
    {
        if (url.Scheme == "file")
        {
            string path = url.LocalPath;
            if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) return false;

            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || 
                path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                HandleNavigation(path);
                return true;
            }
        }
        return false;
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
        // The path coming from WebView might be absolute in temp dir
        // e.g. C:\Users\kingd\AppData\Local\Temp\RequestTracker_Cache\copilot\session-2026-02-02-073300\session-log.md
        // But we want to map it to E:\github\FunWasHad\docs\requests\copilot\session-2026-02-02-073300\session-log.md
        
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


    private void GenerateAndNavigate(FileNode? node)
    {
         if (node == null)
         {
             return;
         }

         if (node.Path == "ALL_JSON_VIRTUAL_NODE")
         {
             IsMarkdownVisible = false;
             IsJsonVisible = true;
             LoadAllJson();
             return;
         }

         if (node.IsDirectory) return;

         if (node.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
         {
             IsMarkdownVisible = false;
             IsJsonVisible = true;
             LoadJson(node.Path);
             return;
         }

         if (node.Path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
         {
             IsMarkdownVisible = true;
             IsJsonVisible = false;
             _currentMarkdownPath = node.Path;

             // Check if we already generated it and it's up to date?
         string hash = node.Path.GetHashCode().ToString("X");
         string tempFileName = $"{Path.GetFileNameWithoutExtension(node.Path)}_{hash}.html";
         string tempDir = Path.Combine(Path.GetTempPath(), "RequestTracker_Cache");
         string tempPath = Path.Combine(tempDir, tempFileName);
         
         Directory.CreateDirectory(tempDir);

         bool needsGeneration = true;
         if (File.Exists(tempPath))
         {
             // Check modification time
             var srcTime = File.GetLastWriteTimeUtc(node.Path);
             var dstTime = File.GetLastWriteTimeUtc(tempPath);
             // If source is older than dest, we are good.
             if (srcTime < dstTime) needsGeneration = false;
         }

         if (needsGeneration)
         {
            Console.WriteLine($"Generating HTML for {node.Path}...");
            if (ConvertMarkdownToHtml(node.Path, tempPath))
            {
                 Console.WriteLine($"Generated: {tempPath}");
            }
            else
            {
                Console.WriteLine("Failed to generate HTML");
                return;
            }
         }
         else
         {
             Console.WriteLine($"Using cached HTML for {node.Path}");
         }

         // Force reload if needed by appending timestamp or just new Uri
         HtmlSource = new Uri($"file:///{tempPath.Replace('\\', '/')}");

         // Per Avalonia docs: on Linux use a separate window/dialog for web content (NativeWebView is unsupported).
         // With current WebView.Avalonia we open the HTML in the system default browser so preview works in WSL/WSLg.
         if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
         {
             OpenHtmlInDefaultBrowser(tempPath);
         }
         }
    }

    private void LoadAllJson()
    {
        var rootPath = GetResolvedTargetPath();
        if (!Directory.Exists(rootPath))
        {
            StatusMessage = "Root directory not found.";
            return;
        }

        StatusMessage = "Aggregating all JSON files...";
        
        // Use a safe wrapper to catch background exceptions
        Task.Run(async () => 
        {
            try
            {
                var jsonFiles = Directory.GetFiles(rootPath, "*.json", SearchOption.AllDirectories);
                var unifiedLogs = new List<UnifiedSessionLog>();
                int totalRequests = 0;

                foreach (var file in jsonFiles)
                {
                    try
                    {
                        var text = await File.ReadAllTextAsync(file);
                        
                        // Try Copilot first
                        try 
                        {
                            var copilotLog = JsonSerializer.Deserialize<CopilotSessionLog>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (copilotLog?.Requests != null)
                            {
                                var unified = UnifiedLogFactory.Create(copilotLog);
                                unifiedLogs.Add(unified);
                                totalRequests += unified.EntryCount;
                                continue;
                            }
                        }
                        catch {}

                        // Try Cursor
                        try
                        {
                            var cursorLog = JsonSerializer.Deserialize<CursorRequestLog>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (cursorLog?.Entries != null)
                            {
                                 var unified = UnifiedLogFactory.Create(cursorLog);
                                 unifiedLogs.Add(unified);
                                 totalRequests += unified.EntryCount;
                                 continue;
                            }
                        }
                        catch {}
                    }
                    catch 
                    {
                        // Skip unreadable files
                    }
                }

                // Aggregate into a single master log
                var masterLog = new UnifiedSessionLog
                {
                    SourceType = "Aggregated",
                    SessionId = "ALL-JSON",
                    Title = "All Requests",
                    Model = "Various",
                    Started = DateTime.Now,
                    Status = "Aggregated",
                    EntryCount = totalRequests,
                    Entries = unifiedLogs.SelectMany(l => l.Entries).OrderByDescending(e => e.Timestamp).ToList(),
                    TotalTokens = unifiedLogs.Sum(l => l.TotalTokens)
                };
                
                // Update UI on main thread
                Dispatcher.UIThread.Post(() => 
                {
                    try
                    {
                        BuildUnifiedSummaryAndIndex(masterLog);
                        StatusMessage = $"Loaded {totalRequests} requests from {jsonFiles.Length} files.";
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
                Dispatcher.UIThread.Post(() => StatusMessage = $"Error aggregating JSON: {ex.Message}");
                Console.WriteLine($"Aggregation Error: {ex}");
            }
        });
    }

    /// <summary>
    /// Opens an HTML file in the system default browser. Used on Linux/WSL where embedded WebView is unsupported
    /// (per Avalonia docs: use NativeWebDialog or a separate window on Linux; we use xdg-open as a workaround).
    /// </summary>
    private static void OpenHtmlInDefaultBrowser(string htmlFilePath)
    {
        if (!File.Exists(htmlFilePath)) return;
        try
        {
            var path = Path.GetFullPath(htmlFilePath);
            var psi = new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = path,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not open in browser: {ex.Message}");
        }
    }

    private bool ConvertMarkdownToHtml(string srcPath, string destPath)
    {
        try
        {
            // Resolve CSS path relative to app execution
            string cssPath = Path.Combine(AppContext.BaseDirectory, "Assets", "styles.css");
            // If running from dotnet run, BaseDirectory might be bin/Debug/...
            // The Assets folder is in project root. We might need to copy it or look up.
            
            // For development, let's try to find it in project source if not in bin
            if (!File.Exists(cssPath))
            {
                 // Fallback to source location (hardcoded for this env)
                 string sourceCss = @"E:\github\RequestTracker\src\RequestTracker\Assets\styles.css";
                 if (File.Exists(sourceCss)) cssPath = sourceCss;
            }

            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "pandoc";
            // Use -s (standalone) and --css to include stylesheet
            // Use --metadata title="Request" to set a title
            process.StartInfo.Arguments = $"\"{srcPath}\" -f markdown -t html -s --css \"{cssPath}\" --metadata title=\"Request Tracker\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            string htmlContent = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            File.WriteAllText(destPath, htmlContent);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting: {ex}");
            return false;
        }
    }

    private void LoadJson(string path)
    {
        try
        {
            string jsonContent = File.ReadAllText(path);
            JsonTree.Clear();
            SearchableEntries.Clear();
            JsonLogSummary = null;

            var jsonNode = JsonNode.Parse(jsonContent);
            string schemaType = "Unknown";
            var summary = new JsonLogSummary();
            UnifiedSessionLog? unifiedLog = null;

            if (jsonNode is JsonObject obj)
            {
                if (obj.ContainsKey("sessionId") && obj.ContainsKey("statistics"))
                {
                    schemaType = "Copilot Session Log";
                    var model = JsonSerializer.Deserialize<CopilotSessionLog>(jsonContent);
                    if (model != null)
                    {
                        BuildCopilotSummaryAndIndex(model, summary); // Keep for stats
                        unifiedLog = UnifiedLogFactory.Create(model);
                    }
                }
                else if (obj.ContainsKey("entries") && obj.ContainsKey("session"))
                {
                    schemaType = "Cursor Request Log";
                    var model = JsonSerializer.Deserialize<CursorRequestLog>(jsonContent);
                    if (model != null)
                    {
                        BuildCursorSummaryAndIndex(model, summary); // Keep for stats
                        unifiedLog = UnifiedLogFactory.Create(model);
                    }
                }
            }

            if (unifiedLog != null)
            {
                // Force usage of Unified Model for Tree and Search Index
                // This ensures the UI reflects the Unified Format as requested
                schemaType = $"{unifiedLog.SourceType} (Unified)";
                
                // Rebuild Search Index based on Unified Log to ensure paths match the new tree (entries[i])
                BuildUnifiedSummaryAndIndex(unifiedLog, summary);
                
                // Serialize Unified Log to JsonNode for the Tree
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
                var unifiedNode = JsonSerializer.SerializeToNode(unifiedLog, options);
                
                // Update Summary Header
                 summary.SummaryLines.Clear();
                 summary.SummaryLines.Add($"Type: {unifiedLog.SourceType}");
                 summary.SummaryLines.Add($"Session: {unifiedLog.SessionId}");
                 summary.SummaryLines.Add($"Entries: {unifiedLog.EntryCount}");
                 if (!string.IsNullOrEmpty(unifiedLog.Model)) 
                     summary.SummaryLines.Add($"Model: {unifiedLog.Model}");
                 if (unifiedLog.LastUpdated.HasValue) 
                     summary.SummaryLines.Add($"Last Updated: {unifiedLog.LastUpdated}");
                     
                // Retain the rich stats from the specific parsers if available (StatsByModel, etc) 
                // but prepend Unified info.
            
                JsonLogSummary = summary;
                
                var root = new JsonTreeNode("Root", schemaType, "Object");
                root.IsExpanded = true;
                BuildJsonTree(unifiedNode, root, null);
                JsonTree.Add(root);
            }
            else
            {
                // Fallback to raw view
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing JSON: {ex}");
            JsonTree.Clear();
            SearchableEntries.Clear();
            FilteredSearchEntries.Clear();
            JsonLogSummary = null;
            JsonTree.Add(new JsonTreeNode("Error", ex.Message, "Error"));
        }
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
                e.Timestamp?.ToString() ?? "",
                e.Status ?? "");

            summary.SearchIndex.Add(new SearchableEntry
            {
                RequestId = e.RequestId ?? "",
                DisplayText = string.IsNullOrEmpty(display) ? $"Entry {i + 1}" : display,
                Timestamp = e.Timestamp?.ToString("o") ?? "",
                Model = e.Model ?? "",
                EntryIndex = i,
                SourcePath = $"entries[{i}]", // Matches Unified Model structure
                SearchText = searchText
            });
        }
        SearchableEntries = new ObservableCollection<SearchableEntry>(summary.SearchIndex);
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
            bool isSorted = false;
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
                isSorted = true;
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
                         if (tsVal.TryGetValue(out DateTime dt)) tsStr = dt.ToString("MM/dd HH:mm");
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

    private void InitializeTree()
    {
        Nodes.Clear();
        string resolvedPath = GetResolvedTargetPath();
        
        // Add "All JSON" node
        var allJsonNode = new FileNode("ALL_JSON_VIRTUAL_NODE", false) { Name = "All JSON" };
        Nodes.Add(allJsonNode);

        if (!Directory.Exists(resolvedPath))
        {
            SetStatus($"Directory not found: {resolvedPath}");
            Nodes.Add(new FileNode(resolvedPath, true) { Name = "Directory not found" });
            return;
        }

        SetStatus($"Loaded: {resolvedPath}");
        var root = new FileNode(resolvedPath, true);
        LoadChildren(root);
        Nodes.Add(root);

        // Try to find readme.md
        var readme = root.Children.FirstOrDefault(n => n.Name.Equals("readme.md", StringComparison.OrdinalIgnoreCase));
        if (readme != null)
        {
            SelectedNode = readme;
        }
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
                if (file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) || 
                    file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    node.Children.Add(new FileNode(file.FullName, false));
                }
            }

            if (node.Children.Count > 0)
            {
                node.IsExpanded = true;
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

        _watcher = new FileSystemWatcher(resolvedPath);
        _watcher.IncludeSubdirectories = true;
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
        _watcher.Filter = "*.*";

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
        Dispatcher.UIThread.InvokeAsync(() => 
        {
             InitializeTree();
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
                
                // Refresh the WebView by resetting the property or notifying? 
                // Since HtmlSource is a Uri, setting it to the SAME Uri might not trigger property changed or webview reload.
                // We might need to toggle it or use a query param.
                
                // Hack: Append timestamp to URL to force reload
                var currentUri = HtmlSource;
                if (currentUri != null)
                {
                     // Just re-setting the same URI should work if the file changed? 
                     // WebView might cache.
                     // Let's create a NEW Uri object.
                     HtmlSource = new Uri(currentUri.AbsoluteUri + "?t=" + DateTime.Now.Ticks);
                }
            }
        });
    }
}
