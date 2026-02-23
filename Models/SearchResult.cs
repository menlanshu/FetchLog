using System;
using System.IO;

namespace FetchLog.Models
{
    public class SearchResult
    {
        public string FileName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public long SizeInBytes { get; set; }
        public bool IsInZip { get; set; }
        public string? ZipFilePath { get; set; }
        public DateTime LastModified { get; set; }
        public string LastModifiedDisplay => LastModified == default ? "" : LastModified.ToString("yyyy-MM-dd HH:mm:ss");
        // The search root directory this file was found under (used for structure preservation)
        public string SearchRootDirectory { get; set; } = string.Empty;
        // Content match details (#9)
        public int MatchCount { get; set; }
        public int FirstMatchLine { get; set; }
        public string MatchSnippet { get; set; } = string.Empty;
        public string MatchCountDisplay => MatchCount > 0 ? MatchCount.ToString() : "";
        public string FirstMatchLineDisplay => FirstMatchLine > 0 ? $"L{FirstMatchLine}" : "";

        // Grouping helpers (#17)
        public string Extension
        {
            get
            {
                var ext = Path.GetExtension(FileName).ToUpperInvariant();
                return string.IsNullOrEmpty(ext) ? "(no extension)" : ext;
            }
        }
        public string MonthGroup => LastModified == default ? "Unknown" : LastModified.ToString("yyyy-MM");

        // Duplicate detection (#19)
        public bool IsDuplicate { get; set; }
        public string? DuplicateOf { get; set; }

        // File hash (#20)
        public string? Md5Hash { get; set; }
        public string Md5HashShort => Md5Hash is { Length: > 0 } h ? h[..8] + "…" : "";

        // Log format auto-detection (#22)
        public string LogFormat { get; set; } = "";

        public SearchResult(string fileName, string sourcePath, long sizeInBytes, bool isInZip = false,
            string? zipFilePath = null, DateTime lastModified = default, string searchRootDirectory = "",
            int matchCount = 0, int firstMatchLine = 0, string matchSnippet = "")
        {
            FileName = fileName;
            SourcePath = sourcePath;
            SizeInBytes = sizeInBytes;
            Size = FormatSize(sizeInBytes);
            FileType = isInZip ? "ZIP" : "File";
            IsInZip = isInZip;
            ZipFilePath = zipFilePath;
            LastModified = lastModified;
            SearchRootDirectory = searchRootDirectory;
            MatchCount = matchCount;
            FirstMatchLine = firstMatchLine;
            MatchSnippet = matchSnippet;
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
