using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FetchLog.Models;

namespace FetchLog.Services
{
    public class ProfileService
    {
        private static readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { WriteIndented = true };

        public string ProfilesDirectory { get; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "FetchLog", "Profiles");

        public async Task SaveAsync(SearchOptions options, string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var json = JsonSerializer.Serialize(options, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<SearchOptions?> LoadAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<SearchOptions>(json);
        }
    }
}
