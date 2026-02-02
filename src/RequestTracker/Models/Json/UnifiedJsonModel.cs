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
                        Response = ParseCopilotResponse(r.Response),
                        Status = "Completed",
                        
                        RawContext = r.Context,
                        
                        TokenCount = tokens,
                        IsPremium = ParseBool(r.Cost?.PremiumRequests),
                    };
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
                        
                        TokenCount = tokens,
                        IsPremium = ParseBool(r.Cost?.PremiumRequests),
                        Score = r.Successfulness?.Score,
                        FailureNote = r.PriorFailureNote
                    };
                    
                    if (r.Interpretation != null) entry.Tags.AddRange(r.Interpretation);
                    if (r.ActionsTaken != null) entry.Tags.AddRange(r.ActionsTaken);
                    if (r.Successfulness?.Notes != null) entry.Tags.AddRange(r.Successfulness.Notes);

                    u.Entries.Add(entry);
                }
                u.TotalTokens = totalTokens;
            }
            return u;
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

        private static string ParseCopilotResponse(object? responseObj)
        {
            if (responseObj == null) return "";
            if (responseObj is string s) return s;
            if (responseObj is System.Text.Json.JsonElement e) return e.ToString();
            return responseObj.ToString() ?? "";
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
    }
}
