using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RequestTracker.Models.Json
{
    // Copilot Session Log
    public class CopilotSessionLog
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; }

        [JsonPropertyName("started")]
        public DateTime? Started { get; set; }

        [JsonPropertyName("lastUpdated")]
        public DateTime? LastUpdated { get; set; }

        [JsonPropertyName("completed")]
        public DateTime? Completed { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("totalRequests")]
        public int? TotalRequests { get; set; }

        [JsonPropertyName("completedRequests")]
        public int? CompletedRequests { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("modelProvider")]
        public string ModelProvider { get; set; }

        [JsonPropertyName("workspace")]
        public WorkspaceInfo Workspace { get; set; }

        [JsonPropertyName("statistics")]
        public StatisticsInfo Statistics { get; set; }

        [JsonPropertyName("requests")]
        public List<CopilotRequestEntry> Requests { get; set; }
    }

    public class CopilotRequestEntry
    {
        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }

        [JsonPropertyName("requestNumber")]
        public int? RequestNumber { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("modelProvider")]
        public string ModelProvider { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("userRequest")]
        public string UserRequest { get; set; }

        [JsonPropertyName("context")]
        public object Context { get; set; } // Complex object, use object or define specific

        [JsonPropertyName("response")]
        public object Response { get; set; } // Complex object

        [JsonPropertyName("cost")]
        public CostInfo Cost { get; set; }
    }

    public class WorkspaceInfo
    {
        [JsonPropertyName("project")]
        public string Project { get; set; }

        [JsonPropertyName("targetFramework")]
        public string TargetFramework { get; set; }

        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        [JsonPropertyName("branch")]
        public string Branch { get; set; }
    }

    public class StatisticsInfo
    {
        [JsonPropertyName("averageSuccessScore")]
        public double? AverageSuccessScore { get; set; }

        [JsonPropertyName("totalNetTokens")]
        public double? TotalNetTokens { get; set; }

        [JsonPropertyName("totalNetPremiumRequests")]
        public double? TotalNetPremiumRequests { get; set; }
        
        [JsonPropertyName("completedCount")]
        public int? CompletedCount { get; set; }

        [JsonPropertyName("inProgressCount")]
        public int? InProgressCount { get; set; }

        [JsonPropertyName("failedCount")]
        public int? FailedCount { get; set; }
    }

    // Cursor Request Log
    public class CursorRequestLog
    {
        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("session")]
        public string Session { get; set; }

        [JsonPropertyName("sessionLabel")]
        public string SessionLabel { get; set; }

        [JsonPropertyName("updated")]
        public string Updated { get; set; }

        [JsonPropertyName("entries")]
        public List<CursorRequestEntry> Entries { get; set; }
    }

    public class CursorRequestEntry
    {
        [JsonPropertyName("requestId")]
        public string RequestId { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; }

        [JsonPropertyName("exactRequest")]
        public string ExactRequest { get; set; }

        [JsonPropertyName("exactRequestNote")]
        public string ExactRequestNote { get; set; }

        [JsonPropertyName("contextApplied")]
        public List<string> ContextApplied { get; set; }

        [JsonPropertyName("interpretation")]
        public List<string> Interpretation { get; set; }

        [JsonPropertyName("response")]
        public List<string> Response { get; set; }

        [JsonPropertyName("actionsTaken")]
        public List<string> ActionsTaken { get; set; }

        [JsonPropertyName("successfulness")]
        public SuccessfulnessInfo Successfulness { get; set; }

        [JsonPropertyName("priorFailureNote")]
        public string PriorFailureNote { get; set; }

        [JsonPropertyName("cost")]
        public CostInfo Cost { get; set; }
    }

    public class SuccessfulnessInfo
    {
        [JsonPropertyName("score")]
        public int? Score { get; set; }

        [JsonPropertyName("maxScore")]
        public int? MaxScore { get; set; }

        [JsonPropertyName("notes")]
        public List<string> Notes { get; set; }
    }

    public class CostInfo
    {
        [JsonPropertyName("tokens")]
        public object Tokens { get; set; } // Could be string or number

        [JsonPropertyName("premiumRequests")]
        public object PremiumRequests { get; set; }

        [JsonPropertyName("costOfLogEntryExcluded")]
        public string CostOfLogEntryExcluded { get; set; }
    }
}
