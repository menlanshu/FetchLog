using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FetchLog.Models;
using FetchLog.Services;
using MessageBox = System.Windows.MessageBox;

namespace FetchLog
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<string> _directories;
        private ObservableCollection<SearchResult> _searchResults;
        private SearchService _searchService;
        private ExportService _exportService;
        private ProfileService _profileService;
        private HistoryService _historyService;
        private FavoritesService _favoritesService;
        private CancellationTokenSource? _cancellationTokenSource;

        // Column sort state
        private GridViewColumnHeader? _lastSortHeader;
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

        public MainWindow()
        {
            InitializeComponent();
            _directories = new ObservableCollection<string>();
            _searchResults = new ObservableCollection<SearchResult>();
            _searchService = new SearchService();
            _exportService = new ExportService();
            _profileService = new ProfileService();
            _historyService = new HistoryService();
            _favoritesService = new FavoritesService();

            lstDirectories.ItemsSource = _directories;
            lvResults.ItemsSource = _searchResults;

            txtOutputPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FetchLog_Results");

            _ = LoadHistoryAsync();
            _ = LoadFavoritesAsync();
        }

        // ── Directory management ─────────────────────────────────────────────

        private void BtnAddDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select a directory to search";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (!_directories.Contains(dialog.SelectedPath))
                    {
                        _directories.Add(dialog.SelectedPath);
                        UpdateStatus($"Added directory: {dialog.SelectedPath}");
                    }
                    else
                    {
                        MessageBox.Show("This directory is already in the list.", "Duplicate Directory",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void BtnRemoveDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (lstDirectories.SelectedItem != null)
            {
                var selectedDir = lstDirectories.SelectedItem.ToString();
                _directories.Remove(selectedDir!);
                UpdateStatus($"Removed directory: {selectedDir}");
            }
            else
            {
                MessageBox.Show("Please select a directory to remove.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnClearDirectories_Click(object sender, RoutedEventArgs e)
        {
            if (_directories.Count > 0)
            {
                var result = MessageBox.Show("Are you sure you want to clear all directories?",
                    "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _directories.Clear();
                    UpdateStatus("All directories cleared");
                }
            }
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select output directory for search results";
                dialog.ShowNewFolderButton = true;

                if (!string.IsNullOrWhiteSpace(txtOutputPath.Text) && Directory.Exists(txtOutputPath.Text))
                    dialog.SelectedPath = txtOutputPath.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtOutputPath.Text = dialog.SelectedPath;
                    UpdateStatus($"Output path set to: {dialog.SelectedPath}");
                }
            }
        }

        // ── Favorites (#13) ──────────────────────────────────────────────────

        private async Task LoadFavoritesAsync()
        {
            var favorites = await _favoritesService.LoadAsync();
            cmbFavorites.ItemsSource = favorites;
        }

        private async void BtnAddFavorite_Click(object sender, RoutedEventArgs e)
        {
            var dir = lstDirectories.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show("Select a directory from the list above to save as a favorite.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await _favoritesService.AddAsync(dir);
            await LoadFavoritesAsync();
            UpdateStatus($"Saved favorite: {dir}");
        }

        private void BtnUseFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (cmbFavorites.SelectedItem is string dir)
            {
                if (!_directories.Contains(dir))
                {
                    _directories.Add(dir);
                    UpdateStatus($"Added from favorites: {dir}");
                }
                else
                {
                    UpdateStatus($"Already in list: {dir}");
                }
            }
            else
            {
                MessageBox.Show("Select a favorite from the dropdown first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnRemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (cmbFavorites.SelectedItem is string dir)
            {
                await _favoritesService.RemoveAsync(dir);
                await LoadFavoritesAsync();
                UpdateStatus($"Removed favorite: {dir}");
            }
            else
            {
                MessageBox.Show("Select a favorite from the dropdown first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ── Drag & Drop (#14) ────────────────────────────────────────────────

        private void LstDirectories_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void LstDirectories_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            int added = 0;
            foreach (var path in paths)
            {
                if (Directory.Exists(path) && !_directories.Contains(path))
                {
                    _directories.Add(path);
                    added++;
                }
            }
            if (added > 0)
                UpdateStatus($"Added {added} director{(added == 1 ? "y" : "ies")} via drag & drop.");
        }

        // ── Search ───────────────────────────────────────────────────────────

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            if (_directories.Count == 0)
            {
                MessageBox.Show("Please add at least one directory to search.", "No Directories",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(txtOutputPath.Text))
            {
                MessageBox.Show("Please specify an output path.", "No Output Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var options = BuildSearchOptions();

            if (options.UseRegex && !string.IsNullOrWhiteSpace(options.ContentFilter))
            {
                try { Regex.IsMatch("", options.ContentFilter); }
                catch (ArgumentException ex)
                {
                    MessageBox.Show($"Invalid regex pattern:\n{ex.Message}", "Regex Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _searchResults.Clear();
            txtFilesFound.Text = "0";
            txtFilesCopied.Text = "0";
            txtSearchTime.Text = "0.00s";
            btnExportResults.IsEnabled = false;
            txtPreview.Text = "";
            txtPreviewHeader.Text = "Select a file to preview its content";

            // Apply current grouping now so new items are automatically grouped
            // as they stream in during incremental search (#17 + #21)
            ApplyGrouping(cmbGroupBy.SelectedIndex);

            SetSearchingState(true);
            _cancellationTokenSource = new CancellationTokenSource();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var progress = new Progress<string>(status => UpdateStatus(status));

                UpdateStatus("Searching for files...");
                progressBar.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = true;

                // Incremental results callback (#21) — adds each result to the list as it is found.
                // Since SearchFilesAsync runs in the UI thread's async context, this is safe without
                // an explicit Dispatcher call.
                Action<SearchResult>? resultCallback = options.EnableIncrementalDisplay
                    ? (r) =>
                    {
                        _searchResults.Add(r);
                        txtFilesFound.Text = _searchResults.Count.ToString();
                    }
                    : null;

                var results = await _searchService.SearchFilesAsync(
                    options, progress, _cancellationTokenSource.Token, resultCallback);

                stopwatch.Stop();
                txtSearchTime.Text = $"{stopwatch.Elapsed.TotalSeconds:F2}s";
                txtFilesFound.Text = results.Count.ToString();
                txtTotalSize.Text = FormatBytes(results.Sum(r => r.SizeInBytes));

                if (results.Count == 0)
                {
                    UpdateStatus("Search completed. No files found.");
                    MessageBox.Show("No files found matching the search criteria.", "Search Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Populate results list (if not already done by incremental callback)
                if (!options.EnableIncrementalDisplay)
                {
                    foreach (var r in results) _searchResults.Add(r);
                    ApplyGrouping(cmbGroupBy.SelectedIndex);
                }

                // Duplicate detection (#19)
                if (options.DetectDuplicates && results.Any(r => !r.IsInZip))
                {
                    UpdateStatus("Detecting duplicate files...");
                    await SearchService.MarkDuplicatesAsync(results);

                    // Refresh display so duplicate highlight triggers
                    _searchResults.Clear();
                    foreach (var r in results) _searchResults.Add(r);
                    ApplyGrouping(cmbGroupBy.SelectedIndex);

                    int dupCount = results.Count(r => r.IsDuplicate);
                    if (dupCount > 0)
                        UpdateStatus($"Found {dupCount} duplicate file(s) (highlighted in orange).");
                }

                string outputDetail;
                int copiedCount;

                if (options.CompressOutput)
                {
                    UpdateStatus("Compressing files into ZIP archive...");
                    var (count, zipPath) = await _searchService.CompressFilesToZipAsync(
                        results, options, progress, _cancellationTokenSource.Token);
                    copiedCount = count;
                    outputDetail = $"Archive: {zipPath}";
                }
                else
                {
                    UpdateStatus("Copying files to output directory...");
                    copiedCount = await _searchService.CopyFilesToOutputAsync(
                        results, options, progress, _cancellationTokenSource.Token);
                    outputDetail = $"Output folder: {options.OutputPath}";
                }

                txtFilesCopied.Text = copiedCount.ToString();
                btnExportResults.IsEnabled = true;

                // Save to history (#12)
                await _historyService.AddAsync(options, results.Count);
                await LoadHistoryAsync();

                UpdateStatus($"Search completed. Found {results.Count} file(s), collected {copiedCount} file(s).");

                MessageBox.Show(
                    $"Search completed successfully!\n\nFiles found: {results.Count}\nFiles collected: {copiedCount}\nTime: {stopwatch.Elapsed.TotalSeconds:F2}s\n\n{outputDetail}",
                    "Search Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                var openResult = MessageBox.Show("Do you want to open the output folder?", "Open Folder",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (openResult == MessageBoxResult.Yes)
                    Process.Start("explorer.exe", options.OutputPath);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                txtSearchTime.Text = $"{stopwatch.Elapsed.TotalSeconds:F2}s";
                UpdateStatus("Search cancelled by user.");
                MessageBox.Show("Search operation was cancelled.", "Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                txtSearchTime.Text = $"{stopwatch.Elapsed.TotalSeconds:F2}s";
                UpdateStatus($"Error: {ex.Message}");
                MessageBox.Show($"An error occurred during the search:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.IsIndeterminate = false;
                progressBar.Visibility = Visibility.Collapsed;
                SetSearchingState(false);
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            UpdateStatus("Cancelling search...");
        }

        // ── Content Match Preview Panel (#8) ─────────────────────────────────

        private async void LvResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lvResults.SelectedItem is not SearchResult result)
            {
                txtPreviewHeader.Text = "Select a file to preview its content";
                txtPreview.Text = "";
                return;
            }

            if (result.IsInZip)
            {
                txtPreviewHeader.Text = $"[Archive]  {result.FileName}";
                txtPreview.Text = "Preview not available for files inside archives.";
                return;
            }

            txtPreviewHeader.Text = "Loading preview...";
            txtPreview.Text = "";

            // Capture UI-thread values before going async
            var contentFilter = txtContentFilter.Text.Trim();
            var useRegex = chkUseRegex.IsChecked == true;
            var caseSensitive = chkCaseSensitive.IsChecked == true;

            try
            {
                var (header, content) = await Task.Run(() =>
                    BuildPreview(result, contentFilter, useRegex, caseSensitive));
                txtPreviewHeader.Text = header;
                txtPreview.Text = content;
            }
            catch (Exception ex)
            {
                txtPreviewHeader.Text = "Error loading preview";
                txtPreview.Text = ex.Message;
            }
        }

        private static (string header, string content) BuildPreview(
            SearchResult result, string contentFilter, bool useRegex, bool caseSensitive)
        {
            const int maxLines = 500;
            const int contextLines = 2;

            if (!File.Exists(result.SourcePath))
                return ("File not found", "The source file no longer exists.");

            string[] lines;
            try { lines = File.ReadAllLines(result.SourcePath); }
            catch { return ("Binary or unreadable file", "This file cannot be previewed."); }

            // No content filter or no matches → show first N lines
            if (string.IsNullOrWhiteSpace(contentFilter) || result.MatchCount == 0)
            {
                var preview = string.Join("\n", lines.Take(maxLines));
                if (lines.Length > maxLines)
                    preview += $"\n\n... ({lines.Length - maxLines} more lines not shown)";
                return ($"{result.FileName}  ·  {lines.Length} lines", preview);
            }

            // Show matching lines with ±contextLines around each hit (#8 context view)
            var showLines = new SortedSet<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                bool matched;
                try
                {
                    matched = useRegex
                        ? Regex.IsMatch(lines[i], contentFilter,
                            caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
                        : lines[i].Contains(contentFilter,
                            caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
                }
                catch { matched = false; }

                if (matched)
                    for (int j = Math.Max(0, i - contextLines); j <= Math.Min(lines.Length - 1, i + contextLines); j++)
                        showLines.Add(j);
            }

            var sb = new StringBuilder();
            int? lastIdx = null;
            foreach (var idx in showLines)
            {
                if (lastIdx.HasValue && idx > lastIdx.Value + 1)
                    sb.AppendLine("   ···");
                sb.AppendLine($"  {idx + 1,5}: {lines[idx]}");
                lastIdx = idx;
            }

            return (
                $"{result.FileName}  ·  {result.MatchCount} match(es), first at line {result.FirstMatchLine}",
                sb.ToString().TrimEnd()
            );
        }

        // ── Result Grouping (#17) ─────────────────────────────────────────────

        private void CmbGroupBy_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyGrouping(cmbGroupBy.SelectedIndex);
        }

        private void ApplyGrouping(int groupIndex)
        {
            if (lvResults?.ItemsSource == null) return;

            var view = CollectionViewSource.GetDefaultView(lvResults.ItemsSource);
            if (view == null) return;

            view.GroupDescriptions.Clear();

            switch (groupIndex)
            {
                case 1: // Extension
                    view.GroupDescriptions.Add(new PropertyGroupDescription("Extension"));
                    break;
                case 2: // Directory (search root)
                    view.GroupDescriptions.Add(new PropertyGroupDescription("SearchRootDirectory"));
                    break;
                case 3: // Date (Month)
                    view.GroupDescriptions.Add(new PropertyGroupDescription("MonthGroup"));
                    break;
                // case 0 (None): no grouping
            }
        }

        // ── Export Results (#4) ──────────────────────────────────────────────

        private async void BtnExportResults_Click(object sender, RoutedEventArgs e)
        {
            if (_searchResults.Count == 0)
            {
                MessageBox.Show("No results to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using (var dialog = new System.Windows.Forms.SaveFileDialog())
            {
                dialog.Filter = "CSV file (*.csv)|*.csv|HTML file (*.html)|*.html";
                dialog.FileName = $"FetchLog_Results_{DateTime.Now:yyyyMMdd_HHmmss}";
                dialog.Title = "Export Search Results";

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        var results = _searchResults.ToList();
                        if (dialog.FilterIndex == 1)
                            await _exportService.ExportToCsvAsync(results, dialog.FileName);
                        else
                            await _exportService.ExportToHtmlAsync(results, dialog.FileName);

                        UpdateStatus($"Results exported to: {dialog.FileName}");

                        var openResult = MessageBox.Show("Results exported successfully!\n\nOpen the exported file?",
                            "Export Complete", MessageBoxButton.YesNo, MessageBoxImage.Information);

                        if (openResult == MessageBoxResult.Yes)
                            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Export failed:\n{ex.Message}", "Export Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // ── Search Profiles (#5) ─────────────────────────────────────────────

        private async void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.SaveFileDialog())
            {
                dialog.Filter = "FetchLog Profile (*.json)|*.json";
                dialog.InitialDirectory = _profileService.ProfilesDirectory;
                dialog.Title = "Save Search Profile";
                dialog.FileName = "MyProfile";

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        await _profileService.SaveAsync(BuildSearchOptions(), dialog.FileName);
                        UpdateStatus($"Profile saved: {Path.GetFileName(dialog.FileName)}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to save profile:\n{ex.Message}", "Save Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnLoadProfile_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Filter = "FetchLog Profile (*.json)|*.json";
                dialog.InitialDirectory = _profileService.ProfilesDirectory;
                dialog.Title = "Load Search Profile";

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    try
                    {
                        var options = await _profileService.LoadAsync(dialog.FileName);
                        if (options != null)
                        {
                            ApplyOptionsToUI(options);
                            UpdateStatus($"Profile loaded: {Path.GetFileName(dialog.FileName)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to load profile:\n{ex.Message}", "Load Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // ── Search History (#12) ─────────────────────────────────────────────

        private async Task LoadHistoryAsync()
        {
            var entries = await _historyService.LoadAsync();
            cmbHistory.ItemsSource = entries;
        }

        private void BtnLoadHistory_Click(object sender, RoutedEventArgs e)
        {
            if (cmbHistory.SelectedItem is SearchHistoryEntry entry)
            {
                ApplyOptionsToUI(entry.Options);
                UpdateStatus($"Loaded search from {entry.Timestamp:yyyy-MM-dd HH:mm}  ({entry.ResultCount} results)");
            }
            else
            {
                MessageBox.Show("Please select a history entry first.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Clear all search history?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                await _historyService.ClearAsync();
                await LoadHistoryAsync();
                UpdateStatus("Search history cleared.");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private SearchOptions BuildSearchOptions()
        {
            var options = new SearchOptions
            {
                SearchDirectories = _directories.ToList(),
                Recursive = chkRecursive.IsChecked ?? true,
                SearchInZip = chkSearchInZip.IsChecked ?? true,
                CaseSensitive = chkCaseSensitive.IsChecked ?? false,
                UseRegex = chkUseRegex.IsChecked ?? false,
                MultilineSearch = chkMultilineSearch.IsChecked ?? false,
                CompressOutput = chkCompressOutput.IsChecked ?? false,
                PreserveStructure = chkPreserveStructure.IsChecked ?? false,
                OutputPath = txtOutputPath.Text,
                ContentFilter = txtContentFilter.Text.Trim(),
                EnableIncrementalDisplay = chkIncrementalSearch.IsChecked ?? true,
                DetectDuplicates = chkDetectDuplicates.IsChecked ?? false,
                ShowFileHash = chkShowFileHash.IsChecked ?? false,
                DetectLogFormat = chkDetectLogFormat.IsChecked ?? false,
            };

            if (!string.IsNullOrWhiteSpace(txtMinMatchCount.Text) &&
                int.TryParse(txtMinMatchCount.Text, out int minHits) && minHits > 0)
                options.MinMatchCount = minHits;

            if (chkDateFilter.IsChecked == true)
            {
                if (dtpDateFrom.SelectedDate.HasValue)
                    options.DateFrom = dtpDateFrom.SelectedDate.Value.Date;
                if (dtpDateTo.SelectedDate.HasValue)
                    options.DateTo = dtpDateTo.SelectedDate.Value.Date.AddDays(1).AddTicks(-1);
                options.DateFilterMode = cmbDateMode.SelectedIndex == 1
                    ? DateFilterMode.Created : DateFilterMode.LastModified;
            }

            if (chkSizeFilter.IsChecked == true)
            {
                if (!string.IsNullOrWhiteSpace(txtMinSize.Text) && double.TryParse(txtMinSize.Text, out double minVal))
                    options.MinSizeBytes = (long)(minVal * GetSizeMultiplier(cmbMinSizeUnit.SelectedIndex));
                if (!string.IsNullOrWhiteSpace(txtMaxSize.Text) && double.TryParse(txtMaxSize.Text, out double maxVal))
                    options.MaxSizeBytes = (long)(maxVal * GetSizeMultiplier(cmbMaxSizeUnit.SelectedIndex));
            }

            // Collection caps (#15)
            if (chkCollectionCap.IsChecked == true)
            {
                if (!string.IsNullOrWhiteSpace(txtMaxFileCount.Text) &&
                    int.TryParse(txtMaxFileCount.Text, out int maxFiles) && maxFiles > 0)
                    options.MaxFileCount = maxFiles;
                if (!string.IsNullOrWhiteSpace(txtMaxTotalSize.Text) &&
                    double.TryParse(txtMaxTotalSize.Text, out double maxSize) && maxSize > 0)
                    options.MaxTotalSizeBytes = (long)(maxSize * GetSizeMultiplier(cmbMaxTotalSizeUnit.SelectedIndex));
            }

            // Rename on copy (#16)
            if (chkRename.IsChecked == true)
            {
                options.RenamePrefix = txtRenamePrefix.Text;
                options.RenameSuffix = txtRenameSuffix.Text;
            }

            if (!string.IsNullOrWhiteSpace(txtFileExtensions.Text))
                options.FileExtensions = txtFileExtensions.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.Trim().StartsWith(".") ? ext.Trim() : "." + ext.Trim())
                    .ToList();

            if (!string.IsNullOrWhiteSpace(txtIncludePatterns.Text))
                options.IncludePatterns = txtIncludePatterns.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim()).ToList();

            if (!string.IsNullOrWhiteSpace(txtExcludePatterns.Text))
                options.ExcludePatterns = txtExcludePatterns.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim()).ToList();

            return options;
        }

        private void ApplyOptionsToUI(SearchOptions options)
        {
            _directories.Clear();
            foreach (var dir in options.SearchDirectories)
                _directories.Add(dir);

            txtFileExtensions.Text = string.Join(", ", options.FileExtensions);
            txtIncludePatterns.Text = string.Join(", ", options.IncludePatterns);
            txtExcludePatterns.Text = string.Join(", ", options.ExcludePatterns);
            txtContentFilter.Text = options.ContentFilter;
            txtMinMatchCount.Text = options.MinMatchCount.HasValue ? options.MinMatchCount.Value.ToString() : "";

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
                txtOutputPath.Text = options.OutputPath;

            chkRecursive.IsChecked = options.Recursive;
            chkSearchInZip.IsChecked = options.SearchInZip;
            chkCaseSensitive.IsChecked = options.CaseSensitive;
            chkUseRegex.IsChecked = options.UseRegex;
            chkMultilineSearch.IsChecked = options.MultilineSearch;
            chkCompressOutput.IsChecked = options.CompressOutput;
            chkPreserveStructure.IsChecked = options.PreserveStructure;
            chkIncrementalSearch.IsChecked = options.EnableIncrementalDisplay;
            chkDetectDuplicates.IsChecked = options.DetectDuplicates;
            chkShowFileHash.IsChecked = options.ShowFileHash;
            chkDetectLogFormat.IsChecked = options.DetectLogFormat;

            bool hasDate = options.DateFrom.HasValue || options.DateTo.HasValue;
            chkDateFilter.IsChecked = hasDate;
            dtpDateFrom.SelectedDate = options.DateFrom;
            dtpDateTo.SelectedDate = options.DateTo;
            cmbDateMode.SelectedIndex = options.DateFilterMode == DateFilterMode.Created ? 1 : 0;

            bool hasSize = options.MinSizeBytes.HasValue || options.MaxSizeBytes.HasValue;
            chkSizeFilter.IsChecked = hasSize;
            txtMinSize.Text = "";
            txtMaxSize.Text = "";
            if (options.MinSizeBytes.HasValue)
            {
                var (val, unit) = BytesToDisplayUnit(options.MinSizeBytes.Value);
                txtMinSize.Text = $"{val:0.##}";
                cmbMinSizeUnit.SelectedIndex = unit;
            }
            if (options.MaxSizeBytes.HasValue)
            {
                var (val, unit) = BytesToDisplayUnit(options.MaxSizeBytes.Value);
                txtMaxSize.Text = $"{val:0.##}";
                cmbMaxSizeUnit.SelectedIndex = unit;
            }

            // Collection caps (#15)
            bool hasCap = options.MaxFileCount.HasValue || options.MaxTotalSizeBytes.HasValue;
            chkCollectionCap.IsChecked = hasCap;
            txtMaxFileCount.Text = options.MaxFileCount.HasValue ? options.MaxFileCount.Value.ToString() : "";
            txtMaxTotalSize.Text = "";
            if (options.MaxTotalSizeBytes.HasValue)
            {
                var (val, unit) = BytesToDisplayUnit(options.MaxTotalSizeBytes.Value);
                txtMaxTotalSize.Text = $"{val:0.##}";
                cmbMaxTotalSizeUnit.SelectedIndex = unit;
            }

            // Rename on copy (#16)
            bool hasRename = !string.IsNullOrEmpty(options.RenamePrefix) || !string.IsNullOrEmpty(options.RenameSuffix);
            chkRename.IsChecked = hasRename;
            txtRenamePrefix.Text = options.RenamePrefix;
            txtRenameSuffix.Text = options.RenameSuffix;
        }

        private static (double value, int unitIndex) BytesToDisplayUnit(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024), 3);
            if (bytes >= 1024L * 1024)         return (bytes / (1024.0 * 1024), 2);
            if (bytes >= 1024L)                 return (bytes / 1024.0, 1);
            return (bytes, 0);
        }

        private static long GetSizeMultiplier(int unitIndex) => unitIndex switch
        {
            0 => 1L,
            1 => 1024L,
            2 => 1024L * 1024,
            3 => 1024L * 1024 * 1024,
            _ => 1L
        };

        private void ChkSizeFilter_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = chkSizeFilter.IsChecked == true;
            txtMinSize.IsEnabled = enabled;
            cmbMinSizeUnit.IsEnabled = enabled;
            txtMaxSize.IsEnabled = enabled;
            cmbMaxSizeUnit.IsEnabled = enabled;
        }

        private void ChkDateFilter_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = chkDateFilter.IsChecked == true;
            dtpDateFrom.IsEnabled = enabled;
            dtpDateTo.IsEnabled = enabled;
            cmbDateMode.IsEnabled = enabled;
        }

        private void ChkCollectionCap_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = chkCollectionCap.IsChecked == true;
            txtMaxFileCount.IsEnabled = enabled;
            txtMaxTotalSize.IsEnabled = enabled;
            cmbMaxTotalSizeUnit.IsEnabled = enabled;
        }

        private void ChkRename_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = chkRename.IsChecked == true;
            txtRenamePrefix.IsEnabled = enabled;
            txtRenameSuffix.IsEnabled = enabled;
        }

        private void SetSearchingState(bool isSearching)
        {
            btnSearch.IsEnabled = !isSearching;
            btnCancel.IsEnabled = isSearching;
            btnAddDirectory.IsEnabled = !isSearching;
            btnRemoveDirectory.IsEnabled = !isSearching;
            btnClearDirectories.IsEnabled = !isSearching;
            btnBrowseOutput.IsEnabled = !isSearching;
            txtFileExtensions.IsEnabled = !isSearching;
            txtIncludePatterns.IsEnabled = !isSearching;
            txtExcludePatterns.IsEnabled = !isSearching;
            txtContentFilter.IsEnabled = !isSearching;
            txtMinMatchCount.IsEnabled = !isSearching;
            chkRecursive.IsEnabled = !isSearching;
            chkSearchInZip.IsEnabled = !isSearching;
            chkCaseSensitive.IsEnabled = !isSearching;
            chkUseRegex.IsEnabled = !isSearching;
            chkMultilineSearch.IsEnabled = !isSearching;
            chkCompressOutput.IsEnabled = !isSearching;
            chkPreserveStructure.IsEnabled = !isSearching;
            btnSaveProfile.IsEnabled = !isSearching;
            btnLoadProfile.IsEnabled = !isSearching;
            btnLoadHistory.IsEnabled = !isSearching;
            btnClearHistory.IsEnabled = !isSearching;
            cmbHistory.IsEnabled = !isSearching;
            cmbGroupBy.IsEnabled = !isSearching;

            // Favorites (#13)
            btnAddFavorite.IsEnabled = !isSearching;
            btnUseFavorite.IsEnabled = !isSearching;
            btnRemoveFavorite.IsEnabled = !isSearching;
            cmbFavorites.IsEnabled = !isSearching;

            if (isSearching) btnExportResults.IsEnabled = false;

            chkIncrementalSearch.IsEnabled = !isSearching;
            chkDetectDuplicates.IsEnabled = !isSearching;
            chkShowFileHash.IsEnabled = !isSearching;
            chkDetectLogFormat.IsEnabled = !isSearching;

            chkSizeFilter.IsEnabled = !isSearching;
            bool sizeEnabled = !isSearching && (chkSizeFilter.IsChecked == true);
            txtMinSize.IsEnabled = sizeEnabled;
            cmbMinSizeUnit.IsEnabled = sizeEnabled;
            txtMaxSize.IsEnabled = sizeEnabled;
            cmbMaxSizeUnit.IsEnabled = sizeEnabled;

            chkDateFilter.IsEnabled = !isSearching;
            bool dateEnabled = !isSearching && (chkDateFilter.IsChecked == true);
            dtpDateFrom.IsEnabled = dateEnabled;
            dtpDateTo.IsEnabled = dateEnabled;
            cmbDateMode.IsEnabled = dateEnabled;

            // Collection caps (#15)
            chkCollectionCap.IsEnabled = !isSearching;
            bool capEnabled = !isSearching && (chkCollectionCap.IsChecked == true);
            txtMaxFileCount.IsEnabled = capEnabled;
            txtMaxTotalSize.IsEnabled = capEnabled;
            cmbMaxTotalSizeUnit.IsEnabled = capEnabled;

            // Rename (#16)
            chkRename.IsEnabled = !isSearching;
            bool renameEnabled = !isSearching && (chkRename.IsChecked == true);
            txtRenamePrefix.IsEnabled = renameEnabled;
            txtRenameSuffix.IsEnabled = renameEnabled;
        }

        // ── Double-click to open file ─────────────────────────────────────────

        private void LvResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lvResults.SelectedItem is not SearchResult result) return;
            if (result.IsInZip)
            {
                UpdateStatus("Cannot open files inside archives directly.");
                return;
            }
            if (!File.Exists(result.SourcePath))
            {
                MessageBox.Show("The file no longer exists at its original path.", "File Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try { Process.Start(new ProcessStartInfo(result.SourcePath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Cannot open file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ── Right-click context menu ──────────────────────────────────────────

        private void LvResultsContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var result = lvResults.SelectedItem as SearchResult;
            bool hasResult = result != null;
            bool isRegularFile = hasResult && !result!.IsInZip;

            cmOpenFile.IsEnabled   = isRegularFile && File.Exists(result!.SourcePath);
            cmOpenFolder.IsEnabled = hasResult;   // works for archives too (opens archive's folder)
            cmCopyPath.IsEnabled   = hasResult;
            cmCopyName.IsEnabled   = hasResult;
            cmCopyHash.IsEnabled   = hasResult && !string.IsNullOrEmpty(result!.Md5Hash);
        }

        private void CmOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (lvResults.SelectedItem is not SearchResult result || result.IsInZip) return;
            try { Process.Start(new ProcessStartInfo(result.SourcePath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show($"Cannot open file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void CmOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (lvResults.SelectedItem is not SearchResult result) return;
            // For archive-internal results, open the archive's containing folder
            var targetPath = result.IsInZip && result.ZipFilePath != null
                ? result.ZipFilePath
                : result.SourcePath;
            if (File.Exists(targetPath))
                Process.Start("explorer.exe", $"/select,\"{targetPath}\"");
            else if (Directory.Exists(Path.GetDirectoryName(targetPath) ?? ""))
                Process.Start("explorer.exe", Path.GetDirectoryName(targetPath)!);
        }

        private void CmCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (lvResults.SelectedItem is SearchResult result)
            {
                System.Windows.Clipboard.SetText(result.SourcePath);
                UpdateStatus($"Copied path: {result.SourcePath}");
            }
        }

        private void CmCopyName_Click(object sender, RoutedEventArgs e)
        {
            if (lvResults.SelectedItem is SearchResult result)
            {
                System.Windows.Clipboard.SetText(result.FileName);
                UpdateStatus($"Copied name: {result.FileName}");
            }
        }

        private void CmCopyHash_Click(object sender, RoutedEventArgs e)
        {
            if (lvResults.SelectedItem is SearchResult result && result.Md5Hash != null)
            {
                System.Windows.Clipboard.SetText(result.Md5Hash);
                UpdateStatus($"Copied MD5: {result.Md5Hash}");
            }
        }

        // ── Column-header sorting ─────────────────────────────────────────────

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader header || header.Role == GridViewColumnHeaderRole.Padding)
                return;

            // Map column header text to the correct sortable property name
            var sortProperty = header.Content?.ToString() switch
            {
                "File Name"   => "FileName",
                "Source Path" => "SourcePath",
                "Size"        => "SizeInBytes",
                "Type"        => "FileType",
                "Modified"    => "LastModified",
                "Matches"     => "MatchCount",
                "Line"        => "FirstMatchLine",
                "Hash (MD5)"  => "Md5Hash",
                "Format"      => "LogFormat",
                _             => null
            };
            if (sortProperty == null) return;

            var direction = (_lastSortHeader == header && _lastSortDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            var view = CollectionViewSource.GetDefaultView(lvResults.ItemsSource);
            if (view == null) return;

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sortProperty, direction));

            // Visual indicator: append ▲/▼ to active header, restore previous
            if (_lastSortHeader != null && _lastSortHeader != header)
            {
                var prev = _lastSortHeader.Content?.ToString()?.TrimEnd(' ', '▲', '▼');
                if (prev != null) _lastSortHeader.Content = prev;
            }
            var label = header.Content?.ToString()?.TrimEnd(' ', '▲', '▼') ?? "";
            header.Content = label + (direction == ListSortDirection.Ascending ? " ▲" : " ▼");

            _lastSortHeader = header;
            _lastSortDirection = direction;
        }

        // ── Size formatting helper ────────────────────────────────────────────

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int order = 0;
            while (val >= 1024 && order < units.Length - 1) { order++; val /= 1024; }
            return $"{val:0.##} {units[order]}";
        }

        private void UpdateStatus(string message)
        {
            txtStatus.Text = message;
        }
    }
}
