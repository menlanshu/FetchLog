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

        // Collection caps (#15)
        public int? MaxFileCount { get; set; }
        public long? MaxTotalSizeBytes { get; set; }

        // Rename on copy (#16)
        public string RenamePrefix { get; set; } = string.Empty;
        public string RenameSuffix { get; set; } = string.Empty;

        // Incremental display (#21) — stream results to the list as files are found
        public bool EnableIncrementalDisplay { get; set; } = true;

        // Duplicate detection (#19) — mark files with identical content after search
        public bool DetectDuplicates { get; set; } = false;

        // File hash (#20) — compute and display MD5 hash for each result
        public bool ShowFileHash { get; set; } = false;

        // Log format auto-detection (#22) — classify log files by format
        public bool DetectLogFormat { get; set; } = false;
    }

    public enum DateFilterMode
    {
        LastModified,
        Created
    }
}
