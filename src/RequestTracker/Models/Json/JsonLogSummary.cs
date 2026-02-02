using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RequestTracker.Models.Json
{
    /// <summary>
    /// Roll-up statistics and metadata for a loaded JSON log (Copilot or Cursor).
    /// </summary>
    public partial class JsonLogSummary : ObservableObject
    {
        [ObservableProperty]
        private string _schemaType = "Unknown";

        [ObservableProperty]
        private ObservableCollection<string> _summaryLines = new();

        /// <summary>
        /// Total request/entry count.
        /// </summary>
        [ObservableProperty]
        private int _totalCount;

        /// <summary>
        /// Human-readable stats (e.g. "By model: gpt-4: 5, claude: 3").
        /// </summary>
        [ObservableProperty]
        private string _statsByModel = "";

        [ObservableProperty]
        private string _statsBySuccess = "";

        [ObservableProperty]
        private string _statsCostOrTokens = "";

        /// <summary>
        /// Flat list of searchable entries (requests/entries) for indexing and search. Populated during LoadJson.
        /// </summary>
        public List<SearchableEntry> SearchIndex { get; set; } = new();
    }

    /// <summary>
    /// One searchable entry in the log (a single request or Cursor entry).
    /// Used to build a flat index for search and to jump to the corresponding tree node.
    /// </summary>
    public partial class SearchableEntry : ObservableObject
    {
        /// <summary>
        /// Request ID or similar unique key.
        /// </summary>
        [ObservableProperty]
        private string _requestId = "";

        /// <summary>
        /// Primary display line (slug, title, or first part of request text).
        /// </summary>
        [ObservableProperty]
        private string _displayText = "";

        /// <summary>
        /// Timestamp string for display and search.
        /// </summary>
        [ObservableProperty]
        private string _timestamp = "";

        /// <summary>
        /// Model name for display and filter.
        /// </summary>
        [ObservableProperty]
        private string _model = "";

        /// <summary>
        /// 0-based index in requests[] or entries[] for this log type.
        /// </summary>
        [ObservableProperty]
        private int _entryIndex;

        /// <summary>
        /// Path into the JSON tree to locate this node, e.g. "requests[3]" or "entries[5]".
        /// </summary>
        [ObservableProperty]
        private string _sourcePath = "";

        /// <summary>
        /// Full text used for search (request body, title, slug, etc.).
        /// </summary>
        [ObservableProperty]
        private string _searchText = "";

        /// <summary>
        /// Single line for list display: "slug | model | timestamp".
        /// </summary>
        public string ListLine => string.IsNullOrEmpty(Timestamp)
            ? $"{DisplayText} | {Model}"
            : $"{DisplayText} | {Model} | {Timestamp}";

        /// <summary>
        /// Reference to the unified entry object for detailed viewing.
        /// </summary>
        public UnifiedRequestEntry? UnifiedEntry { get; set; }
    }
}
