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
    }
}
