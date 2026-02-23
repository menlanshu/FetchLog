using System;
using System.Collections.Generic;

namespace FetchLog.Models
{
    public class SearchOptions
    {
        public List<string> SearchDirectories { get; set; } = new List<string>();
        public List<string> FileExtensions { get; set; } = new List<string>();
        public List<string> IncludePatterns { get; set; } = new List<string>();
        public List<string> ExcludePatterns { get; set; } = new List<string>();
        public string ContentFilter { get; set; } = string.Empty;
        public bool Recursive { get; set; } = true;
        public bool SearchInZip { get; set; } = true;
        public bool CaseSensitive { get; set; } = false;
        public string OutputPath { get; set; } = string.Empty;

        // Date/Time range filter
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public DateFilterMode DateFilterMode { get; set; } = DateFilterMode.LastModified;

        // File size filter (bytes, null = no limit)
        public long? MinSizeBytes { get; set; }
        public long? MaxSizeBytes { get; set; }

        // Content search options
        public bool UseRegex { get; set; } = false;
        public bool MultilineSearch { get; set; } = false;  // regex: dot matches newline
        public int? MinMatchCount { get; set; }             // minimum content matches required

        // Output options
        public bool CompressOutput { get; set; } = false;
        public bool PreserveStructure { get; set; } = false;
    }

    public enum DateFilterMode
    {
        LastModified,
        Created
    }
}
