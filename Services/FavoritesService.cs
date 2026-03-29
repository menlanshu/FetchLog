using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FetchLog.Services
{
    public class FavoritesService
    {
        private static readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { WriteIndented = true };

        private readonly string _favoritesFile =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "FetchLog", "favorites.json");

        public async Task<List<string>> LoadAsync()
        {
            if (!File.Exists(_favoritesFile)) return new List<string>();
            try
            {
                var json = await File.ReadAllTextAsync(_favoritesFile);
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public async Task AddAsync(string directory)
        {
            var favorites = await LoadAsync();
            if (!favorites.Any(f => string.Equals(f, directory, StringComparison.OrdinalIgnoreCase)))
            {
                favorites.Add(directory);
                await SaveAsync(favorites);
            }
        }

        public async Task RemoveAsync(string directory)
        {
            var favorites = await LoadAsync();
            favorites.RemoveAll(f => string.Equals(f, directory, StringComparison.OrdinalIgnoreCase));
            await SaveAsync(favorites);
        }

        private async Task SaveAsync(List<string> favorites)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_favoritesFile)!);
            await File.WriteAllTextAsync(_favoritesFile,
                JsonSerializer.Serialize(favorites, _jsonOptions));
        }
    }
}
