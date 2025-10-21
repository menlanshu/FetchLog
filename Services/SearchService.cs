using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FetchLog.Models;

namespace FetchLog.Services
{
    public class SearchService
    {
        private CancellationToken _cancellationToken;

        public async Task<List<SearchResult>> SearchFilesAsync(SearchOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            var results = new List<SearchResult>();
            var processedZipFiles = new HashSet<string>();

            foreach (var directory in options.SearchDirectories)
            {
                if (!Directory.Exists(directory))
                {
                    progress?.Report($"Directory not found: {directory}");
                    continue;
                }

                progress?.Report($"Searching in: {directory}");

                var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(directory, "*.*", searchOption);

                foreach (var file in files)
                {
                    _cancellationToken.ThrowIfCancellationRequested();

                    var fileInfo = new FileInfo(file);

                    // Check if it's a ZIP file
                    if (options.SearchInZip && fileInfo.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        var zipMatches = await SearchInZipFileAsync(file, options, progress);
                        if (zipMatches.Any() && !processedZipFiles.Contains(file))
                        {
                            // If ZIP contains matches, add the ZIP file itself to results
                            results.Add(new SearchResult(
                                fileInfo.Name,
                                file,
                                fileInfo.Length,
                                true,
                                file
                            ));
                            processedZipFiles.Add(file);
                            progress?.Report($"Found matches in ZIP: {fileInfo.Name}");
                        }
                    }
                    else
                    {
                        // Regular file search
                        if (await IsFileMatchAsync(file, fileInfo, options))
                        {
                            results.Add(new SearchResult(
                                fileInfo.Name,
                                file,
                                fileInfo.Length,
                                false,
                                null
                            ));
                            progress?.Report($"Match found: {fileInfo.Name}");
                        }
                    }
                }
            }

            return results;
        }

        private async Task<bool> IsFileMatchAsync(string filePath, FileInfo fileInfo, SearchOptions options)
        {
            try
            {
                // Check file extension filter
                if (options.FileExtensions.Any())
                {
                    var extension = fileInfo.Extension.ToLowerInvariant();
                    if (!options.FileExtensions.Any(ext => ext.ToLowerInvariant() == extension))
                    {
                        return false;
                    }
                }

                // Check exclude patterns
                if (options.ExcludePatterns.Any())
                {
                    foreach (var pattern in options.ExcludePatterns)
                    {
                        if (IsPatternMatch(fileInfo.Name, pattern))
                        {
                            return false;
                        }
                    }
                }

                // Check include patterns
                if (options.IncludePatterns.Any())
                {
                    bool matchesAny = false;
                    foreach (var pattern in options.IncludePatterns)
                    {
                        if (IsPatternMatch(fileInfo.Name, pattern))
                        {
                            matchesAny = true;
                            break;
                        }
                    }
                    if (!matchesAny)
                    {
                        return false;
                    }
                }

                // Check content filter
                if (!string.IsNullOrWhiteSpace(options.ContentFilter))
                {
                    return await ContainsTextAsync(filePath, options.ContentFilter, options.CaseSensitive);
                }

                return true;
            }
            catch (Exception)
            {
                // Skip files that can't be accessed
                return false;
            }
        }

        private async Task<List<string>> SearchInZipFileAsync(string zipFilePath, SearchOptions options, IProgress<string>? progress)
        {
            var matches = new List<string>();

            try
            {
                using (var archive = ZipFile.OpenRead(zipFilePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();

                        if (entry.FullName.EndsWith("/")) // Skip directories
                            continue;

                        var entryName = Path.GetFileName(entry.FullName);

                        // Check file extension filter
                        if (options.FileExtensions.Any())
                        {
                            var extension = Path.GetExtension(entryName).ToLowerInvariant();
                            if (!options.FileExtensions.Any(ext => ext.ToLowerInvariant() == extension))
                            {
                                continue;
                            }
                        }

                        // Check exclude patterns
                        bool excluded = false;
                        foreach (var pattern in options.ExcludePatterns)
                        {
                            if (IsPatternMatch(entryName, pattern))
                            {
                                excluded = true;
                                break;
                            }
                        }
                        if (excluded) continue;

                        // Check include patterns
                        if (options.IncludePatterns.Any())
                        {
                            bool matchesAny = false;
                            foreach (var pattern in options.IncludePatterns)
                            {
                                if (IsPatternMatch(entryName, pattern))
                                {
                                    matchesAny = true;
                                    break;
                                }
                            }
                            if (!matchesAny) continue;
                        }

                        // Check content filter
                        if (!string.IsNullOrWhiteSpace(options.ContentFilter))
                        {
                            using (var stream = entry.Open())
                            using (var reader = new StreamReader(stream))
                            {
                                var content = await reader.ReadToEndAsync();
                                var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                                if (content.Contains(options.ContentFilter, comparison))
                                {
                                    matches.Add(entry.FullName);
                                }
                            }
                        }
                        else
                        {
                            matches.Add(entry.FullName);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Skip ZIP files that can't be read
            }

            return matches;
        }

        private bool IsPatternMatch(string fileName, string pattern)
        {
            // Convert wildcard pattern to regex
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
        }

        private async Task<bool> ContainsTextAsync(string filePath, string searchText, bool caseSensitive)
        {
            try
            {
                // Only search in text-based files (skip binary files)
                if (IsBinaryFile(filePath))
                {
                    return false;
                }

                using (var reader = new StreamReader(filePath))
                {
                    var content = await reader.ReadToEndAsync();
                    var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    return content.Contains(searchText, comparison);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsBinaryFile(string filePath)
        {
            var textExtensions = new[] { ".txt", ".log", ".xml", ".json", ".csv", ".config", ".ini", ".yaml", ".yml", ".md", ".cs", ".js", ".html", ".css", ".sql", ".bat", ".sh", ".ps1" };
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            // If it's a known text extension, it's not binary
            if (textExtensions.Contains(extension))
            {
                return false;
            }

            // Otherwise, check the first few bytes for binary content
            try
            {
                using (var file = File.OpenRead(filePath))
                {
                    var buffer = new byte[512];
                    var bytesRead = file.Read(buffer, 0, buffer.Length);

                    for (int i = 0; i < bytesRead; i++)
                    {
                        // Check for null bytes or other binary indicators
                        if (buffer[i] == 0 || (buffer[i] < 9 && buffer[i] != 0))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                return true; // Assume binary if can't read
            }

            return false;
        }

        public async Task<int> CopyFilesToOutputAsync(List<SearchResult> results, string outputPath, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            int copiedCount = 0;

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            foreach (var result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var destFileName = result.FileName;
                    var destPath = Path.Combine(outputPath, destFileName);

                    // Handle duplicate file names
                    int counter = 1;
                    while (File.Exists(destPath))
                    {
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(result.FileName);
                        var extension = Path.GetExtension(result.FileName);
                        destFileName = $"{nameWithoutExt}_{counter}{extension}";
                        destPath = Path.Combine(outputPath, destFileName);
                        counter++;
                    }

                    await Task.Run(() => File.Copy(result.SourcePath, destPath), cancellationToken);
                    copiedCount++;
                    progress?.Report($"Copied: {destFileName}");
                }
                catch (Exception ex)
                {
                    progress?.Report($"Error copying {result.FileName}: {ex.Message}");
                }
            }

            return copiedCount;
        }
    }
}
