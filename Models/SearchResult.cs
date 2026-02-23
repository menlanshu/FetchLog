using System;

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

        public SearchResult(string fileName, string sourcePath, long sizeInBytes, bool isInZip = false, string? zipFilePath = null, DateTime lastModified = default, string searchRootDirectory = "")
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
