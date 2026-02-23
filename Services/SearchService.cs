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
using SharpCompress.Archives;
using SharpCompress.Common;

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

                    // Check if it's a ZIP or 7z archive
                    if (options.SearchInZip && (fileInfo.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                                                 fileInfo.Extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)))
                    {
                        var archiveMatches = await SearchInArchiveFileAsync(file, fileInfo.Extension, options, progress);
                        if (archiveMatches.Any() && !processedZipFiles.Contains(file))
                        {
                            var archiveType = fileInfo.Extension.ToUpper().TrimStart('.');
                            results.Add(new SearchResult(
                                fileInfo.Name, file, fileInfo.Length, true, file,
                                fileInfo.LastWriteTime, directory,
                                matchCount: archiveMatches.Count,
                                matchSnippet: $"{archiveMatches.Count} matching entr{(archiveMatches.Count == 1 ? "y" : "ies")}"
                            ));
                            processedZipFiles.Add(file);
                            progress?.Report($"Found matches in {archiveType}: {fileInfo.Name}");
                        }
                    }
                    else
                    {
                        // Regular file: check structural filters first, then content
                        if (!PassesStructuralFilters(fileInfo, options))
                            continue;

                        MatchInfo matchInfo;
                        if (!string.IsNullOrWhiteSpace(options.ContentFilter))
                        {
                            var info = await GetMatchInfoAsync(file, options.ContentFilter,
                                options.CaseSensitive, options.UseRegex, options.MultilineSearch);
                            if (info == null) continue;
                            if (options.MinMatchCount.HasValue && info.MatchCount < options.MinMatchCount.Value) continue;
                            matchInfo = info;
                        }
                        else
                        {
                            matchInfo = new MatchInfo(0, 0, "");
                        }

                        results.Add(new SearchResult(
                            fileInfo.Name, file, fileInfo.Length, false, null,
                            fileInfo.LastWriteTime, directory,
                            matchInfo.MatchCount, matchInfo.FirstMatchLine, matchInfo.Snippet
                        ));
                        progress?.Report($"Match found: {fileInfo.Name}");
                    }
                }
            }

            // Apply collection caps (#15) — total-size cap first, then file-count cap
            if (options.MaxTotalSizeBytes.HasValue)
            {
                long running = 0;
                int cutoff = results.Count;
                for (int i = 0; i < results.Count; i++)
                {
                    running += results[i].SizeInBytes;
                    if (running > options.MaxTotalSizeBytes.Value) { cutoff = i; break; }
                }
                if (cutoff < results.Count)
                {
                    results = results.Take(cutoff).ToList();
                    progress?.Report($"Total-size cap reached: keeping {cutoff} file(s).");
                }
            }

            if (options.MaxFileCount.HasValue && results.Count > options.MaxFileCount.Value)
            {
                results = results.Take(options.MaxFileCount.Value).ToList();
                progress?.Report($"File-count cap reached: keeping {options.MaxFileCount.Value} file(s).");
            }

            return results;
        }

        private record MatchInfo(int MatchCount, int FirstMatchLine, string Snippet);

        /// <summary>Extension, date, size, and filename pattern checks — no I/O beyond FileInfo.</summary>
        private bool PassesStructuralFilters(FileInfo fileInfo, SearchOptions options)
        {
            try
            {
                if (options.FileExtensions.Any())
                {
                    var ext = fileInfo.Extension.ToLowerInvariant();
                    if (!options.FileExtensions.Any(e => e.ToLowerInvariant() == ext))
                        return false;
                }

                if (options.DateFrom.HasValue || options.DateTo.HasValue)
                {
                    var fileDate = options.DateFilterMode == DateFilterMode.Created
                        ? fileInfo.CreationTime : fileInfo.LastWriteTime;
                    if (options.DateFrom.HasValue && fileDate < options.DateFrom.Value) return false;
                    if (options.DateTo.HasValue   && fileDate > options.DateTo.Value)   return false;
                }

                if (options.MinSizeBytes.HasValue && fileInfo.Length < options.MinSizeBytes.Value) return false;
                if (options.MaxSizeBytes.HasValue && fileInfo.Length > options.MaxSizeBytes.Value) return false;

                if (options.ExcludePatterns.Any(p => IsPatternMatch(fileInfo.Name, p))) return false;

                if (options.IncludePatterns.Any() && !options.IncludePatterns.Any(p => IsPatternMatch(fileInfo.Name, p)))
                    return false;

                return true;
            }
            catch { return false; }
        }

        /// <summary>Reads the file and returns match statistics, or null if no match found.</summary>
        private async Task<MatchInfo?> GetMatchInfoAsync(string filePath, string searchText,
            bool caseSensitive, bool useRegex, bool multiline)
        {
            try
            {
                if (IsBinaryFile(filePath)) return null;

                string content;
                using (var reader = new StreamReader(filePath))
                    content = await reader.ReadToEndAsync();

                if (useRegex)
                {
                    var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    if (multiline) regexOptions |= RegexOptions.Singleline; // dot matches \n
                    MatchCollection matches;
                    try { matches = Regex.Matches(content, searchText, regexOptions); }
                    catch { return null; }

                    if (matches.Count == 0) return null;

                    var firstIdx = matches[0].Index;
                    var lineNum = content[..firstIdx].Count(c => c == '\n') + 1;
                    var snippet = matches[0].Value.Replace('\r', ' ').Replace('\n', ' ');
                    if (snippet.Length > 100) snippet = snippet[..100] + "...";
                    return new MatchInfo(matches.Count, lineNum, snippet);
                }
                else
                {
                    var lines = content.Split('\n');
                    int count = 0, firstLine = 0;
                    string snippet = "";
                    var comp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains(searchText, comp))
                        {
                            count++;
                            if (count == 1)
                            {
                                firstLine = i + 1;
                                snippet = lines[i].Trim();
                                if (snippet.Length > 100) snippet = snippet[..100] + "...";
                            }
                        }
                    }
                    return count > 0 ? new MatchInfo(count, firstLine, snippet) : null;
                }
            }
            catch { return null; }
        }

        private async Task<List<string>> SearchInArchiveFileAsync(string archivePath, string extension, SearchOptions options, IProgress<string>? progress)
        {
            var matches = new List<string>();

            try
            {
                // Use native ZIP support for .zip files for better performance
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return await SearchInZipFileAsync(archivePath, options, progress);
                }
                // Use SharpCompress for .7z and other archive formats
                else if (extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
                {
                    return await SearchIn7zFileAsync(archivePath, options, progress);
                }
            }
            catch (Exception)
            {
                // Skip archive files that can't be read
            }

            return matches;
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
                                if (IsContentMatch(content, options.ContentFilter, options.CaseSensitive, options.UseRegex))
                                    matches.Add(entry.FullName);
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

        private async Task<List<string>> SearchIn7zFileAsync(string archivePath, SearchOptions options, IProgress<string>? progress)
        {
            var matches = new List<string>();

            try
            {
                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();

                        if (entry.IsDirectory) // Skip directories
                            continue;

                        var entryName = Path.GetFileName(entry.Key);

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
                            using (var stream = entry.OpenEntryStream())
                            using (var reader = new StreamReader(stream))
                            {
                                var content = await reader.ReadToEndAsync();
                                if (IsContentMatch(content, options.ContentFilter, options.CaseSensitive, options.UseRegex))
                                    matches.Add(entry.Key);
                            }
                        }
                        else
                        {
                            matches.Add(entry.Key);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Skip 7z files that can't be read
            }

            return matches;
        }

        private bool IsPatternMatch(string fileName, string pattern)
        {
            // Convert wildcard pattern to regex
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
        }

        private static bool IsContentMatch(string content, string pattern, bool caseSensitive, bool useRegex)
        {
            try
            {
                if (useRegex)
                {
                    var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    return Regex.IsMatch(content, pattern, regexOptions);
                }
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                return content.Contains(pattern, comparison);
            }
            catch { return false; }
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

        public async Task<int> CopyFilesToOutputAsync(List<SearchResult> results, SearchOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            int copiedCount = 0;
            var outputPath = options.OutputPath;

            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            foreach (var result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Apply prefix/suffix rename (#16)
                    var baseName = Path.GetFileNameWithoutExtension(result.FileName);
                    var fileExt = Path.GetExtension(result.FileName);
                    var renamedName = (!string.IsNullOrEmpty(options.RenamePrefix) || !string.IsNullOrEmpty(options.RenameSuffix))
                        ? $"{options.RenamePrefix}{baseName}{options.RenameSuffix}{fileExt}"
                        : result.FileName;

                    string destPath;

                    if (options.PreserveStructure && !string.IsNullOrEmpty(result.SearchRootDirectory))
                    {
                        // Mirror the source directory tree under outputPath, but apply rename to filename
                        var relDir = Path.GetDirectoryName(
                            Path.GetRelativePath(result.SearchRootDirectory, result.SourcePath)) ?? "";
                        destPath = Path.Combine(outputPath, relDir, renamedName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    }
                    else
                    {
                        // Flat copy — handle duplicate file names
                        destPath = Path.Combine(outputPath, renamedName);
                        int counter = 1;
                        while (File.Exists(destPath))
                        {
                            var nameWithoutExt = Path.GetFileNameWithoutExtension(renamedName);
                            var extension = Path.GetExtension(renamedName);
                            destPath = Path.Combine(outputPath, $"{nameWithoutExt}_{counter++}{extension}");
                        }
                    }

                    await Task.Run(() => File.Copy(result.SourcePath, destPath, overwrite: false), cancellationToken);
                    copiedCount++;
                    progress?.Report($"Copied: {Path.GetFileName(destPath)}");
                }
                catch (Exception ex)
                {
                    progress?.Report($"Error copying {result.FileName}: {ex.Message}");
                }
            }

            return copiedCount;
        }

        public async Task<(int count, string zipPath)> CompressFilesToZipAsync(List<SearchResult> results, SearchOptions options, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            int count = 0;
            var outputPath = options.OutputPath;

            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            var zipPath = Path.Combine(outputPath, $"FetchLog_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            await Task.Run(() =>
            {
                var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (var result in results)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            // Apply prefix/suffix rename (#16)
                            var baseName = Path.GetFileNameWithoutExtension(result.FileName);
                            var fileExt = Path.GetExtension(result.FileName);
                            var renamedName = (!string.IsNullOrEmpty(options.RenamePrefix) || !string.IsNullOrEmpty(options.RenameSuffix))
                                ? $"{options.RenamePrefix}{baseName}{options.RenameSuffix}{fileExt}"
                                : result.FileName;

                            string entryName;

                            if (options.PreserveStructure && !string.IsNullOrEmpty(result.SearchRootDirectory))
                            {
                                // Use the relative path as the ZIP entry, applying rename to the filename
                                var relDir = Path.GetDirectoryName(
                                    Path.GetRelativePath(result.SearchRootDirectory, result.SourcePath)) ?? "";
                                entryName = (string.IsNullOrEmpty(relDir) ? renamedName
                                    : $"{relDir.Replace('\\', '/')}/{renamedName}");
                            }
                            else
                            {
                                entryName = renamedName;
                                if (existingNames.Contains(entryName))
                                {
                                    int counter = 1;
                                    var ext = Path.GetExtension(renamedName);
                                    var nameNoExt = Path.GetFileNameWithoutExtension(renamedName);
                                    do { entryName = $"{nameNoExt}_{counter++}{ext}"; }
                                    while (existingNames.Contains(entryName));
                                }
                            }

                            existingNames.Add(entryName);
                            archive.CreateEntryFromFile(result.SourcePath, entryName, CompressionLevel.Optimal);
                            count++;
                            progress?.Report($"Compressed: {entryName}");
                        }
                        catch (Exception ex)
                        {
                            progress?.Report($"Error compressing {result.FileName}: {ex.Message}");
                        }
                    }
                }
            }, cancellationToken);

            progress?.Report($"Archive created: {Path.GetFileName(zipPath)}");
            return (count, zipPath);
        }
    }
}
