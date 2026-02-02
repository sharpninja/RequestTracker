using System;
using System.Collections.Generic;

namespace RequestTracker.Models.Json
{
    public class UnifiedSessionLog
    {
        public string SourceType { get; set; } // "Copilot" or "Cursor"
        public string SessionId { get; set; }
        public string Title { get; set; } // Description (Cursor) or Workspace/Project (Copilot)
        public string Model { get; set; } // Default Model for the session (if any)
        public DateTime? Started { get; set; }
        public DateTime? LastUpdated { get; set; }
        public string Status { get; set; }
        
        // Unified Stats
        public int EntryCount { get; set; }
        public int TotalTokens { get; set; }
        
        public WorkspaceInfo? Workspace { get; set; }
        
        public List<UnifiedRequestEntry> Entries { get; set; } = new();

        // Specifics retained for fidelity
        public StatisticsInfo? CopilotStatistics { get; set; }
        public string? CursorSessionLabel { get; set; }
    }

    public class UnifiedRequestEntry
    {
        public string RequestId { get; set; }
        public DateTime? Timestamp { get; set; }
        public string Model { get; set; }
        public string ModelProvider { get; set; } // Copilot has specific field
        
        // Unified Query/Response
        public string QueryText { get; set; } // UserRequest / ExactRequest
        public string QueryTitle { get; set; } // Title/Slug / ExactRequestNote
        public string Response { get; set; } // Unified string response
        
        // Unified Context
        public List<string> ContextList { get; set; } = new(); // ContextApplied / Context (if list)
        public object RawContext { get; set; } // Original Context object
        
        // Unified Metrics
        public int TokenCount { get; set; }
        public bool IsPremium { get; set; }
        public double? Score { get; set; } // Successfulness Score
        public string Status { get; set; } // Added Status back
        
        // Metadata / Tags
        public List<string> Tags { get; set; } = new(); // Interpretation, ActionsTaken
        public string FailureNote { get; set; } // PriorFailureNote
        public string Interpretation { get; set; } // Explicit Interpretation Text
        
        // Structured Actions (for Grid)
        public List<UnifiedAction> Actions { get; set; } = new();

        public bool HasActions => Actions?.Count > 0;

        // Original Source Object (for JSON viewer)
        public object OriginalEntry { get; set; }
    }

    public class UnifiedAction
    {
        public int Order { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string FilePath { get; set; }
    }

    public static class UnifiedLogFactory
    {
        public static UnifiedSessionLog Create(CopilotSessionLog log)
        {
            var u = new UnifiedSessionLog
            {
                SourceType = "Copilot",
                SessionId = log.SessionId,
                Started = log.Started,
                LastUpdated = log.LastUpdated ?? log.Completed,
                Status = log.Status,
                EntryCount = log.Requests?.Count ?? 0,
                
                Title = log.Workspace?.Project ?? log.Workspace?.Repository ?? "Copilot Session",
                Model = log.Model, // Copilot has session-level model
                Workspace = log.Workspace,
                CopilotStatistics = log.Statistics,
                TotalTokens = (int)(log.Statistics?.TotalNetTokens ?? 0)
            };

            if (log.Requests != null)
            {
                foreach (var r in log.Requests)
                {
                    int tokens = ParseTokens(r.Cost?.Tokens);
                    
                    var entry = new UnifiedRequestEntry
                    {
                        RequestId = r.RequestId,
                        Timestamp = r.Timestamp,
                        Model = r.Model,
                        ModelProvider = r.ModelProvider,
                        
                        QueryText = r.UserRequest,
                        QueryTitle = !string.IsNullOrEmpty(r.Title) ? r.Title : r.Slug,
                        Response = ParseCopilotResponse(r.Response, r.Interpretation, r.Actions),
                        Status = "Completed",
                        
                        ContextList = ParseContextList(r.Context),
                        RawContext = r.Context,
                        
                        Interpretation = ExtractInterpretation(r.Interpretation),
                        Actions = ParseActions(r.Actions),

                        TokenCount = tokens,
                        IsPremium = ParseBool(r.Cost?.PremiumRequests),
                        
                        OriginalEntry = r
                    };
                    
                    if (r.RequestNumber.HasValue) entry.Tags.Add($"Request #{r.RequestNumber}");
                    if (!string.IsNullOrEmpty(r.Slug) && r.Slug != r.Title) entry.Tags.Add($"Slug: {r.Slug}");

                    u.Entries.Add(entry);
                }
            }
            return u;
        }

        public static UnifiedSessionLog Create(CursorRequestLog log)
        {
            var u = new UnifiedSessionLog
            {
                SourceType = "Cursor",
                SessionId = log.Session,
                Status = "Unknown",
                Title = log.Description ?? log.SessionLabel ?? "Cursor Session",
                CursorSessionLabel = log.SessionLabel
            };

            if (log.Entries != null && log.Entries.Count > 0)
            {
                // Cursor doesn't have session-level model, try to get from first entry
                u.Model = log.Entries[0].Model;
            }

            if (DateTime.TryParse(log.Updated, out var dt))
            {
                u.LastUpdated = dt;
            }

            if (log.Entries != null)
            {
                u.EntryCount = log.Entries.Count;
                int totalTokens = 0;
                
                foreach (var r in log.Entries)
                {
                    DateTime? ts = ParseCursorTimestamp(r.Timestamp);
                    int tokens = ParseTokens(r.Cost?.Tokens);
                    totalTokens += tokens;

                    var entry = new UnifiedRequestEntry
                    {
                        RequestId = r.RequestId,
                        Timestamp = ts,
                        Model = r.Model,
                        
                        QueryText = r.ExactRequest,
                        QueryTitle = r.ExactRequestNote,
                        Response = ParseCursorResponse(r.Response),
                        Status = r.Successfulness?.Score != null ? "Scored" : "Unknown",
                        
                        ContextList = r.ContextApplied ?? new List<string>(),

                        Interpretation = r.Interpretation != null ? string.Join("\n", r.Interpretation) : null,
                        
                        TokenCount = tokens,
                        IsPremium = ParseBool(r.Cost?.PremiumRequests),
                        Score = r.Successfulness?.Score,
                        FailureNote = r.PriorFailureNote,
                        
                        OriginalEntry = r
                    };
                    
                    if (r.ActionsTaken != null) entry.Tags.AddRange(r.ActionsTaken);
                    if (r.Successfulness?.Notes != null) entry.Tags.AddRange(r.Successfulness.Notes);

                    u.Entries.Add(entry);
                }
                u.TotalTokens = totalTokens;
            }
            return u;
        }
        
        private static List<UnifiedAction> ParseActions(object? actionsObj)
        {
            var list = new List<UnifiedAction>();
            if (actionsObj == null) return list;
            
            System.Text.Json.JsonElement? arrElement = null;

            if (actionsObj is System.Text.Json.JsonElement element)
            {
                arrElement = element;
            }
            else if (actionsObj is string jsonString)
            {
                 try {
                     var doc = System.Text.Json.JsonDocument.Parse(jsonString);
                     arrElement = doc.RootElement;
                 } catch {}
            }

            if (arrElement.HasValue && arrElement.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                int idx = 1;
                foreach (var item in arrElement.Value.EnumerateArray())
                {
                    var action = new UnifiedAction();
                    
                    // Order
                    if (item.TryGetProperty("order", out var order) && order.ValueKind == System.Text.Json.JsonValueKind.Number) 
                        action.Order = order.GetInt32();
                    else 
                        action.Order = idx++;
                    
                    // Description
                    if (item.TryGetProperty("description", out var desc)) action.Description = desc.ToString();
                    
                    // Type
                    if (item.TryGetProperty("type", out var type)) action.Type = type.ToString();
                    
                    // Status
                    if (item.TryGetProperty("status", out var status)) action.Status = status.ToString();
                    
                    // FilePath
                    if (item.TryGetProperty("filePath", out var fp)) action.FilePath = fp.ToString();
                    else if (item.TryGetProperty("filePaths", out var fps)) action.FilePath = "Multiple files";
                    
                    list.Add(action);
                }
            }
            return list;
        }

        private static int ParseTokens(object? tokenObj)
        {
            if (tokenObj == null) return 0;
            if (tokenObj is int i) return i;
            if (tokenObj is long l) return (int)l;
            if (tokenObj is double d) return (int)d;
            if (tokenObj is System.Text.Json.JsonElement e) 
            {
                if (e.ValueKind == System.Text.Json.JsonValueKind.Number && e.TryGetInt32(out var val)) return val;
                if (e.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(e.GetString(), out var val2)) return val2;
            }
            if (tokenObj is string s && int.TryParse(s, out var val3)) return val3;
            return 0;
        }

        private static bool ParseBool(object? obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is string s) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
             if (obj is System.Text.Json.JsonElement e) 
             {
                 if (e.ValueKind == System.Text.Json.JsonValueKind.True) return true;
             }
            return false;
        }

        private static DateTime? ParseCursorTimestamp(string ts)
        {
            if (string.IsNullOrEmpty(ts)) return null;
            if (DateTime.TryParse(ts, out var t)) return t;
            if (long.TryParse(ts, out var l)) 
            {
                 if (l > 1000000000000) return DateTimeOffset.FromUnixTimeMilliseconds(l).UtcDateTime;
                 return DateTimeOffset.FromUnixTimeSeconds(l).UtcDateTime;
            }
            return null;
        }

        private static string ExtractInterpretation(object? interpretationObj)
        {
             if (interpretationObj == null) return null;
             
             if (interpretationObj is System.Text.Json.JsonElement interp && interp.ValueKind == System.Text.Json.JsonValueKind.Object)
             {
                 var sb = new System.Text.StringBuilder();
                 
                 // Summary
                 if (interp.TryGetProperty("summary", out var summary))
                 {
                     sb.AppendLine(summary.GetString());
                     sb.AppendLine();
                 }
                 
                 // Requirements
                 if (interp.TryGetProperty("requirements", out var reqs) && reqs.ValueKind == System.Text.Json.JsonValueKind.Array)
                 {
                     sb.AppendLine("Requirements:");
                     foreach (var req in reqs.EnumerateArray())
                     {
                         sb.AppendLine($"- {req}");
                     }
                     sb.AppendLine();
                 }

                 // Purpose
                 if (interp.TryGetProperty("purpose", out var purp) && purp.ValueKind == System.Text.Json.JsonValueKind.Array)
                 {
                     sb.AppendLine("Purpose:");
                     foreach (var p in purp.EnumerateArray())
                     {
                         sb.AppendLine($"- {p}");
                     }
                     sb.AppendLine();
                 }
                 
                 // Key Decisions (from req-002)
                 if (interp.TryGetProperty("keyDecisions", out var decisions) && decisions.ValueKind == System.Text.Json.JsonValueKind.Array)
                 {
                     sb.AppendLine("Key Decisions:");
                     foreach (var d in decisions.EnumerateArray())
                     {
                         sb.AppendLine($"- {d}");
                     }
                     sb.AppendLine();
                 }

                 return sb.ToString().Trim();
             }
             return interpretationObj.ToString();
        }

        private static string ParseCopilotResponse(object? responseObj, object? interpretationObj = null, object? actionsObj = null)
        {
            // If explicit response exists, use it
            if (responseObj != null)
            {
                string resp = "";
                if (responseObj is string s) resp = s;
                else if (responseObj is System.Text.Json.JsonElement e) 
                {
                     if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
                     {
                        var sb = new System.Text.StringBuilder();
                        foreach (var item in e.EnumerateArray()) sb.Append(item.ToString());
                        resp = sb.ToString();
                     }
                     else resp = e.ToString();
                }
                else resp = responseObj.ToString() ?? "";
                
                if (!string.IsNullOrWhiteSpace(resp)) return resp;
            }

            // Fallback: Synthesize response from Interpretation and Actions
            if (interpretationObj != null || actionsObj != null)
            {
                var sb = new System.Text.StringBuilder();
                
                if (interpretationObj is System.Text.Json.JsonElement interp && interp.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (interp.TryGetProperty("summary", out var summary))
                    {
                        sb.AppendLine("### Summary");
                        sb.AppendLine(summary.GetString());
                        sb.AppendLine();
                    }
                    
                    if (interp.TryGetProperty("requirements", out var reqs) && reqs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        sb.AppendLine("### Requirements");
                        foreach (var req in reqs.EnumerateArray())
                        {
                            sb.AppendLine($"- {req}");
                        }
                        sb.AppendLine();
                    }
                }

                if (actionsObj is System.Text.Json.JsonElement actions && actions.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    sb.AppendLine("### Actions Taken");
                    foreach (var action in actions.EnumerateArray())
                    {
                        string desc = "";
                        if (action.TryGetProperty("description", out var d)) desc = d.GetString() ?? "";
                        else desc = action.ToString();
                        
                        if (action.TryGetProperty("order", out var order))
                            sb.AppendLine($"{order}. {desc}");
                        else
                            sb.AppendLine($"- {desc}");
                    }
                }
                
                return sb.ToString();
            }

            return "";
        }

        private static string ParseCursorResponse(object? responseObj)
        {
            if (responseObj == null) return "";
            if (responseObj is string s) return s;
            if (responseObj is System.Text.Json.JsonElement e) 
            {
                if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in e.EnumerateArray())
                    {
                        sb.Append(item.ToString());
                    }
                    return sb.ToString();
                }
                return e.ToString();
            }
            if (responseObj is System.Collections.IEnumerable list && !(responseObj is string))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var item in list) sb.Append(item?.ToString());
                return sb.ToString();
            }
            return responseObj.ToString() ?? "";
        }

        private static List<string> ParseContextList(object? contextObj)
        {
            var list = new List<string>();
            if (contextObj == null) return list;

            if (contextObj is System.Text.Json.JsonElement e)
            {
                if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in e.EnumerateArray())
                    {
                        list.Add(item.ToString());
                    }
                }
                else
                {
                    list.Add(e.ToString());
                }
            }
            else if (contextObj is System.Collections.IEnumerable enumerable && !(contextObj is string))
            {
                foreach (var item in enumerable)
                {
                    list.Add(item?.ToString() ?? "");
                }
            }
            else
            {
                list.Add(contextObj.ToString() ?? "");
            }
            return list;
        }
    }
}
