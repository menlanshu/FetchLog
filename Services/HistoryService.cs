using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FetchLog.Models;

namespace FetchLog.Services
{
    public class SearchHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string Label { get; set; } = string.Empty;
        public SearchOptions Options { get; set; } = new();
        public int ResultCount { get; set; }
    }

    public class HistoryService
    {
        private static readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { WriteIndented = true };

        private const int MaxEntries = 20;

        private readonly string _historyFile =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "FetchLog", "search_history.json");

        public async Task<List<SearchHistoryEntry>> LoadAsync()
        {
            if (!File.Exists(_historyFile)) return new List<SearchHistoryEntry>();
            try
            {
                var json = await File.ReadAllTextAsync(_historyFile);
                return JsonSerializer.Deserialize<List<SearchHistoryEntry>>(json) ?? new List<SearchHistoryEntry>();
            }
            catch { return new List<SearchHistoryEntry>(); }
        }

        public async Task AddAsync(SearchOptions options, int resultCount)
        {
            var entries = await LoadAsync();

            // Build a human-readable label
            var dirs = string.Join(", ", options.SearchDirectories.Take(2));
            if (options.SearchDirectories.Count > 2)
                dirs += $" (+{options.SearchDirectories.Count - 2} more)";

            var label = $"{DateTime.Now:yyyy-MM-dd HH:mm}  |  {dirs}";
            if (!string.IsNullOrWhiteSpace(options.ContentFilter))
                label += $"  |  \"{options.ContentFilter}\"";
            label += $"  →  {resultCount} file(s)";

            entries.Insert(0, new SearchHistoryEntry
            {
                Timestamp = DateTime.Now,
                Label = label,
                Options = options,
                ResultCount = resultCount
            });

            if (entries.Count > MaxEntries)
                entries = entries.Take(MaxEntries).ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(_historyFile)!);
            await File.WriteAllTextAsync(_historyFile,
                JsonSerializer.Serialize(entries, _jsonOptions));
        }

        public async Task ClearAsync()
        {
            if (File.Exists(_historyFile))
                await File.WriteAllTextAsync(_historyFile, "[]");
        }
    }
}
