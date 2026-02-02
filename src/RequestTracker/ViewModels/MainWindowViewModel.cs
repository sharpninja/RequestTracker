using System;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RequestTracker.Models;
using RequestTracker.Models.Json;

namespace RequestTracker.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string TargetPath = @"E:\github\FunWasHad\docs\requests";
    private FileSystemWatcher? _watcher;

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
    private string _statusMessage;

    private string? _currentMarkdownPath;
    
    // Navigation History
    private readonly Stack<FileNode> _backStack = new();
    private readonly Stack<FileNode> _forwardStack = new();
    private bool _isNavigatingHistory;

    public MainWindowViewModel()
    {
        InitializeTree();
        SetupWatcher();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void NavigateBack()
    {
        if (_backStack.Count > 0 && _selectedNode != null)
        {
            _isNavigatingHistory = true;
            _forwardStack.Push(_selectedNode);
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
        if (_forwardStack.Count > 0 && _selectedNode != null)
        {
            _isNavigatingHistory = true;
            _backStack.Push(_selectedNode);
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
        if (_selectedNode != null)
        {
            // Force regenerate
             string hash = _selectedNode.Path.GetHashCode().ToString("X");
             string tempFileName = $"{Path.GetFileNameWithoutExtension(_selectedNode.Path)}_{hash}.html";
             string tempDir = Path.Combine(Path.GetTempPath(), "RequestTracker_Cache");
             string tempPath = Path.Combine(tempDir, tempFileName);
             
             if (File.Exists(tempPath)) File.Delete(tempPath);
             
             GenerateAndNavigate(_selectedNode);
        }
    }

    partial void OnSelectedNodeChanging(FileNode? value)
    {
        if (!_isNavigatingHistory && _selectedNode != null && value != null && _selectedNode != value)
        {
            _backStack.Push(_selectedNode);
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


    private void GenerateAndNavigate(FileNode? node)
    {
         if (node == null || node.IsDirectory)
         {
             return;
         }

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

            // Try to detect type and deserialize to specific model first
            // But for display, we want a tree. 
            // We can deserialize to specific model to validate, then use JsonNode for tree generation
            // or reflect over the deserialized model.
            // Using JsonNode is easier for generic tree display.
            
            // Determine Schema
            string schemaType = "Unknown";
            var jsonNode = JsonNode.Parse(jsonContent);
            if (jsonNode is JsonObject obj)
            {
                if (obj.ContainsKey("sessionId") && obj.ContainsKey("statistics"))
                {
                    schemaType = "Copilot Session Log";
                    // Deserialize to validate
                    var model = JsonSerializer.Deserialize<CopilotSessionLog>(jsonContent);
                }
                else if (obj.ContainsKey("entries") && obj.ContainsKey("session"))
                {
                    schemaType = "Cursor Request Log";
                    // Deserialize to validate
                    var model = JsonSerializer.Deserialize<CursorRequestLog>(jsonContent);
                }
            }

            var root = new JsonTreeNode("Root", schemaType, "Object");
            root.IsExpanded = true;
            BuildJsonTree(jsonNode, root);
            JsonTree.Add(root);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing JSON: {ex}");
            JsonTree.Clear();
            JsonTree.Add(new JsonTreeNode("Error", ex.Message, "Error"));
        }
    }

    private void BuildJsonTree(JsonNode? node, JsonTreeNode parent)
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
                // Simplify type name
                if (property.Value is JsonArray) type = "Array";
                else if (property.Value is JsonObject) type = "Object";
                else if (property.Value is JsonValue) type = "Value";

                var child = new JsonTreeNode(property.Key, "", type);
                // Expand if it's the root's direct children or simple
                if (parent.Name == "Root") child.IsExpanded = true;

                BuildJsonTree(property.Value, child);
                
                // If value is simple, set it
                if (property.Value is JsonValue val)
                {
                    child.Value = val.ToString();
                }

                parent.Children.Add(child);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            int index = 0;
            foreach (var item in jsonArray)
            {
                var child = new JsonTreeNode($"[{index}]", "", "Item");
                
                // Peek at item to find a preview text
                if (item is JsonObject objItem)
                {
                    // Find first string property that is not empty
                    foreach (var prop in objItem)
                    {
                        if (prop.Value is JsonValue propVal && propVal.TryGetValue(out string? text) && !string.IsNullOrEmpty(text))
                        {
                            // Truncate if too long
                            if (text.Length > 50) text = text.Substring(0, 47) + "...";
                            child.Value = text;
                            break;
                        }
                    }
                }

                BuildJsonTree(item, child);
                if (item is JsonValue val)
                {
                    child.Value = val.ToString();
                }
                parent.Children.Add(child);
                index++;
            }
        }
        else if (node is JsonValue jsonValue)
        {
            // Already handled in parent loop usually, but if root is value
            // But we don't add to parent here, we assume BuildJsonTree populates children of parent
            // But for Value types, they don't have children.
        }
    }

    private void InitializeTree()
    {
        Nodes.Clear();
        if (!Directory.Exists(TargetPath))
        {
            return;
        }

        var root = new FileNode(TargetPath, true);
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
        if (!Directory.Exists(TargetPath)) return;

        _watcher = new FileSystemWatcher(TargetPath);
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
