using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FetchLog.Models;
using SharpCompress.Archives;

namespace FetchLog.Services
{
    public class SearchService
    {
        // ── Archive-type label helpers ────────────────────────────────────────

        /// <summary>Returns a short archive-type label, or null if the file is not a recognised archive.</summary>
        private static string? GetArchiveLabel(FileInfo fi)
        {
            var name = fi.Name.ToLowerInvariant();
            var ext  = fi.Extension.ToLowerInvariant();
            if (name.EndsWith(".tar.gz")  || name.EndsWith(".tgz"))  return "TAR.GZ";
            if (name.EndsWith(".tar.bz2") || name.EndsWith(".tbz2")) return "TAR.BZ2";
            return ext switch
            {
                ".zip" => "ZIP",
                ".7z"  => "7Z",
                ".rar" => "RAR",
                ".tar" => "TAR",
                _      => null
            };
        }

        // ── Main search entry-point ───────────────────────────────────────────

        /// <param name="resultCallback">
        /// Optional: called on the calling thread for every result that passes all filters
        /// (including collection caps). Use for incremental UI updates. (#21)
        /// </param>
        public async Task<List<SearchResult>> SearchFilesAsync(
            SearchOptions options,
            IProgress<string>? progress,
            CancellationToken cancellationToken,
            Action<SearchResult>? resultCallback = null)
        {
            var results = new List<SearchResult>();
            var seenArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long runningSize = 0;
            bool capReached = false;

            // Local helper: adds a result to the list, invokes the streaming callback,
            // and sets capReached if a collection cap is now exceeded.
            bool AddResult(SearchResult r)
            {
                runningSize += r.SizeInBytes;
                results.Add(r);
                resultCallback?.Invoke(r);

                if (options.MaxFileCount.HasValue && results.Count >= options.MaxFileCount.Value)
                {
                    progress?.Report($"File-count cap reached: {results.Count} file(s).");
                    return false;
                }
                if (options.MaxTotalSizeBytes.HasValue && runningSize > options.MaxTotalSizeBytes.Value)
                {
                    progress?.Report($"Total-size cap reached after {results.Count} file(s).");
                    return false;
                }
                return true;
            }

            foreach (var directory in options.SearchDirectories)
            {
                if (capReached) break;
                if (!Directory.Exists(directory))
                {
                    progress?.Report($"Directory not found: {directory}");
                    continue;
                }

                progress?.Report($"Searching in: {directory}");

                var searchOption = options.Recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                string[] files;
                try { files = Directory.GetFiles(directory, "*.*", searchOption); }
                catch (Exception ex) { progress?.Report($"Cannot enumerate {directory}: {ex.Message}"); continue; }

                foreach (var file in files)
                {
                    if (capReached) break;
                    cancellationToken.ThrowIfCancellationRequested();

                    var fi = new FileInfo(file);
                    var archiveLabel = options.SearchInZip ? GetArchiveLabel(fi) : null;

                    if (archiveLabel != null)
                    {
                        // ── Archive file ─────────────────────────────────────
                        if (seenArchives.Contains(file)) continue;

                        var archiveMatches = await SearchInArchiveAsync(file, archiveLabel, options, progress, cancellationToken);
                        if (archiveMatches.Count > 0)
                        {
                            var r = new SearchResult(
                                fi.Name, file, fi.Length, isInZip: true, zipFilePath: file,
                                fi.LastWriteTime, directory,
                                matchCount: archiveMatches.Count,
                                matchSnippet: $"{archiveMatches.Count} matching entr{(archiveMatches.Count == 1 ? "y" : "ies")}");
                            r.FileType = archiveLabel;
                            seenArchives.Add(file);
                            progress?.Report($"Found matches in {archiveLabel}: {fi.Name}");
                            capReached = !AddResult(r);
                        }
                    }
                    else
                    {
                        // ── Regular file ─────────────────────────────────────
                        if (!PassesStructuralFilters(fi, options)) continue;

                        string? fileContent = null; // may be populated below

                        MatchInfo matchInfo;
                        if (!string.IsNullOrWhiteSpace(options.ContentFilter))
                        {
                            var info = await GetMatchInfoAsync(file, options.ContentFilter,
                                options.CaseSensitive, options.UseRegex, options.MultilineSearch);
                            if (info == null) continue;
                            if (options.MinMatchCount.HasValue && info.MatchCount < options.MinMatchCount.Value) continue;
                            matchInfo = info;
                            fileContent = info.FullContent;
                        }
                        else
                        {
                            matchInfo = MatchInfo.Empty;
                        }

                        var result = new SearchResult(
                            fi.Name, file, fi.Length, isInZip: false, zipFilePath: null,
                            fi.LastWriteTime, directory,
                            matchInfo.MatchCount, matchInfo.FirstMatchLine, matchInfo.Snippet);

                        // ── MD5 hash (#20) ────────────────────────────────────
                        if (options.ShowFileHash)
                            result.Md5Hash = TryComputeMd5(file);

                        // ── Log format detection (#22) ───────────────────────
                        if (options.DetectLogFormat)
                            result.LogFormat = DetectLogFormat(file, fileContent);

                        progress?.Report($"Match found: {fi.Name}");
                        capReached = !AddResult(result);
                    }
                }
            }

            return results;
        }

        // ── Duplicate detection (#19) ─────────────────────────────────────────

        /// <summary>
        /// Groups results by file size, then computes MD5 for size-collision groups,
        /// and marks all but the first identical file as duplicates.
        /// Also populates Md5Hash for any file that hadn't been hashed yet.
        /// </summary>
        public static async Task MarkDuplicatesAsync(List<SearchResult> results)
        {
            var candidates = results.Where(r => !r.IsInZip && File.Exists(r.SourcePath)).ToList();

            // Pre-filter by size — only files sharing a size can be duplicates
            var bySizeGroups = candidates
                .GroupBy(r => r.SizeInBytes)
                .Where(g => g.Count() > 1);

            foreach (var sizeGroup in bySizeGroups)
            {
                // Hash each candidate (may already have a hash from search)
                var hashed = await Task.Run(() =>
                    sizeGroup.Select(r =>
                    {
                        var h = r.Md5Hash ?? TryComputeMd5(r.SourcePath);
                        if (h != null) r.Md5Hash = h;
                        return (result: r, hash: h);
                    })
                    .Where(x => x.hash != null)
                    .ToList());

                foreach (var hashGroup in hashed.GroupBy(x => x.hash!).Where(g => g.Count() > 1))
                {
                    var first = hashGroup.First().result;
                    foreach (var (dup, _) in hashGroup.Skip(1))
                    {
                        dup.IsDuplicate = true;
                        dup.DuplicateOf = first.SourcePath;
                    }
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private record MatchInfo(int MatchCount, int FirstMatchLine, string Snippet, string? FullContent)
        {
            public static readonly MatchInfo Empty = new(0, 0, "", null);
        }

        private static bool PassesStructuralFilters(FileInfo fi, SearchOptions options)
        {
            try
            {
                if (options.FileExtensions.Any())
                {
                    var ext = fi.Extension.ToLowerInvariant();
                    if (!options.FileExtensions.Any(e => e.ToLowerInvariant() == ext))
                        return false;
                }

                if (options.DateFrom.HasValue || options.DateTo.HasValue)
                {
                    var fileDate = options.DateFilterMode == DateFilterMode.Created
                        ? fi.CreationTime : fi.LastWriteTime;
                    if (options.DateFrom.HasValue && fileDate < options.DateFrom.Value) return false;
                    if (options.DateTo.HasValue   && fileDate > options.DateTo.Value)   return false;
                }

                if (options.MinSizeBytes.HasValue && fi.Length < options.MinSizeBytes.Value) return false;
                if (options.MaxSizeBytes.HasValue && fi.Length > options.MaxSizeBytes.Value) return false;

                if (options.ExcludePatterns.Any(p => IsPatternMatch(fi.Name, p))) return false;

                if (options.IncludePatterns.Any() &&
                    !options.IncludePatterns.Any(p => IsPatternMatch(fi.Name, p)))
                    return false;

                return true;
            }
            catch { return false; }
        }

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
                    if (multiline) regexOptions |= RegexOptions.Singleline;
                    MatchCollection matches;
                    try { matches = Regex.Matches(content, searchText, regexOptions); }
                    catch { return null; }

                    if (matches.Count == 0) return null;

                    var firstIdx = matches[0].Index;
                    var lineNum = content[..firstIdx].Count(c => c == '\n') + 1;
                    var snippet = matches[0].Value.Replace('\r', ' ').Replace('\n', ' ');
                    if (snippet.Length > 100) snippet = snippet[..100] + "...";
                    return new MatchInfo(matches.Count, lineNum, snippet, content);
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
                    return count > 0 ? new MatchInfo(count, firstLine, snippet, content) : null;
                }
            }
            catch { return null; }
        }

        // ── Archive searching (#18 extends existing ZIP/7z support) ───────────

        private async Task<List<string>> SearchInArchiveAsync(
            string archivePath, string archiveLabel, SearchOptions options,
            IProgress<string>? progress, CancellationToken cancellationToken)
        {
            try
            {
                // ZIP uses System.IO.Compression for best performance
                if (archiveLabel == "ZIP")
                    return await SearchInZipAsync(archivePath, options, cancellationToken);

                // 7Z, TAR, TAR.GZ, TAR.BZ2, RAR — all handled by SharpCompress
                return await SearchInSharpCompressArchiveAsync(archivePath, options, cancellationToken);
            }
            catch (Exception ex)
            {
                progress?.Report($"Cannot read {archiveLabel} archive {Path.GetFileName(archivePath)}: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<List<string>> SearchInZipAsync(
            string zipPath, SearchOptions options, CancellationToken cancellationToken)
        {
            var matches = new List<string>();
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith("/")) continue;

                var entryName = Path.GetFileName(entry.FullName);
                if (!PassesArchiveEntryFilters(entryName, options)) continue;

                if (!string.IsNullOrWhiteSpace(options.ContentFilter))
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync();
                    if (IsContentMatch(content, options.ContentFilter, options.CaseSensitive, options.UseRegex))
                        matches.Add(entry.FullName);
                }
                else
                {
                    matches.Add(entry.FullName);
                }
            }
            return matches;
        }

        private async Task<List<string>> SearchInSharpCompressArchiveAsync(
            string archivePath, SearchOptions options, CancellationToken cancellationToken)
        {
            var matches = new List<string>();
            using var archive = ArchiveFactory.Open(archivePath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory) continue;

                var entryName = Path.GetFileName(entry.Key ?? "");
                if (string.IsNullOrEmpty(entryName)) continue;
                if (!PassesArchiveEntryFilters(entryName, options)) continue;

                if (!string.IsNullOrWhiteSpace(options.ContentFilter))
                {
                    using var stream = entry.OpenEntryStream();
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync();
                    if (IsContentMatch(content, options.ContentFilter, options.CaseSensitive, options.UseRegex))
                        matches.Add(entry.Key!);
                }
                else
                {
                    matches.Add(entry.Key!);
                }
            }
            return matches;
        }

        private bool PassesArchiveEntryFilters(string entryName, SearchOptions options)
        {
            if (options.FileExtensions.Any())
            {
                var ext = Path.GetExtension(entryName).ToLowerInvariant();
                if (!options.FileExtensions.Any(e => e.ToLowerInvariant() == ext)) return false;
            }
            if (options.ExcludePatterns.Any(p => IsPatternMatch(entryName, p))) return false;
            if (options.IncludePatterns.Any() && !options.IncludePatterns.Any(p => IsPatternMatch(entryName, p)))
                return false;
            return true;
        }

        // ── MD5 hash (#20) ────────────────────────────────────────────────────

        private static string? TryComputeMd5(string filePath)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
            }
            catch { return null; }
        }

        // ── Log format detection (#22) ────────────────────────────────────────

        private static string DetectLogFormat(string filePath, string? cachedContent)
        {
            try
            {
                string[] sampleLines;
                if (cachedContent != null)
                    sampleLines = cachedContent.Split('\n').Take(15).ToArray();
                else
                    sampleLines = File.ReadLines(filePath).Take(15).ToArray();

                return LogFormatDetector.Detect(sampleLines);
            }
            catch { return ""; }
        }

        // ── Pattern / content helpers ─────────────────────────────────────────

        private static bool IsPatternMatch(string fileName, string pattern)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
        }

        private static bool IsContentMatch(string content, string pattern, bool caseSensitive, bool useRegex)
        {
            try
            {
                if (useRegex)
                {
                    var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    return Regex.IsMatch(content, pattern, opts);
                }
                var comp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                return content.Contains(pattern, comp);
            }
            catch { return false; }
        }

        private static bool IsBinaryFile(string filePath)
        {
            var textExtensions = new[]
            {
                ".txt", ".log", ".xml", ".json", ".csv", ".config", ".ini",
                ".yaml", ".yml", ".md", ".cs", ".js", ".html", ".css",
                ".sql", ".bat", ".sh", ".ps1"
            };
            if (textExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant()))
                return false;

            try
            {
                using var file = File.OpenRead(filePath);
                var buf = new byte[512];
                int read = file.Read(buf, 0, buf.Length);
                for (int i = 0; i < read; i++)
                    if (buf[i] == 0 || (buf[i] < 9 && buf[i] != 0)) return true;
            }
            catch { return true; }
            return false;
        }

        // ── Copy / Compress ───────────────────────────────────────────────────

        public async Task<int> CopyFilesToOutputAsync(
            List<SearchResult> results, SearchOptions options,
            IProgress<string>? progress, CancellationToken cancellationToken)
        {
            int copiedCount = 0;
            var outputPath = options.OutputPath;
            if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

            foreach (var result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var baseName = Path.GetFileNameWithoutExtension(result.FileName);
                    var fileExt  = Path.GetExtension(result.FileName);
                    var renamedName = (!string.IsNullOrEmpty(options.RenamePrefix) || !string.IsNullOrEmpty(options.RenameSuffix))
                        ? $"{options.RenamePrefix}{baseName}{options.RenameSuffix}{fileExt}"
                        : result.FileName;

                    string destPath;
                    if (options.PreserveStructure && !string.IsNullOrEmpty(result.SearchRootDirectory))
                    {
                        var relDir = Path.GetDirectoryName(
                            Path.GetRelativePath(result.SearchRootDirectory, result.SourcePath)) ?? "";
                        destPath = Path.Combine(outputPath, relDir, renamedName);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    }
                    else
                    {
                        destPath = Path.Combine(outputPath, renamedName);
                        int counter = 1;
                        while (File.Exists(destPath))
                        {
                            var n = Path.GetFileNameWithoutExtension(renamedName);
                            var e = Path.GetExtension(renamedName);
                            destPath = Path.Combine(outputPath, $"{n}_{counter++}{e}");
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

        public async Task<(int count, string zipPath)> CompressFilesToZipAsync(
            List<SearchResult> results, SearchOptions options,
            IProgress<string>? progress, CancellationToken cancellationToken)
        {
            int count = 0;
            var outputPath = options.OutputPath;
            if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);

            var zipPath = Path.Combine(outputPath, $"FetchLog_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            await Task.Run(() =>
            {
                var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                foreach (var result in results)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var baseName = Path.GetFileNameWithoutExtension(result.FileName);
                        var fileExt  = Path.GetExtension(result.FileName);
                        var renamedName = (!string.IsNullOrEmpty(options.RenamePrefix) || !string.IsNullOrEmpty(options.RenameSuffix))
                            ? $"{options.RenamePrefix}{baseName}{options.RenameSuffix}{fileExt}"
                            : result.FileName;

                        string entryName;
                        if (options.PreserveStructure && !string.IsNullOrEmpty(result.SearchRootDirectory))
                        {
                            var relDir = Path.GetDirectoryName(
                                Path.GetRelativePath(result.SearchRootDirectory, result.SourcePath)) ?? "";
                            entryName = string.IsNullOrEmpty(relDir) ? renamedName
                                : $"{relDir.Replace('\\', '/')}/{renamedName}";
                        }
                        else
                        {
                            entryName = renamedName;
                            if (existingNames.Contains(entryName))
                            {
                                int ctr = 1;
                                var ext = Path.GetExtension(renamedName);
                                var noExt = Path.GetFileNameWithoutExtension(renamedName);
                                do { entryName = $"{noExt}_{ctr++}{ext}"; }
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
            }, cancellationToken);

            progress?.Report($"Archive created: {Path.GetFileName(zipPath)}");
            return (count, zipPath);
        }
    }
}
