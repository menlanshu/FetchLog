using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
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
        private CancellationTokenSource? _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            _directories = new ObservableCollection<string>();
            _searchResults = new ObservableCollection<SearchResult>();
            _searchService = new SearchService();
            _exportService = new ExportService();
            _profileService = new ProfileService();
            _historyService = new HistoryService();

            lstDirectories.ItemsSource = _directories;
            lvResults.ItemsSource = _searchResults;

            txtOutputPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FetchLog_Results");

            _ = LoadHistoryAsync();
        }

        // ── Directory management ─────────────────────────────────────────────

        private void BtnAddDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
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
            using (var dialog = new FolderBrowserDialog())
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

            SetSearchingState(true);
            _cancellationTokenSource = new CancellationTokenSource();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var progress = new Progress<string>(status => UpdateStatus(status));

                UpdateStatus("Searching for files...");
                progressBar.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = true;

                var results = await _searchService.SearchFilesAsync(options, progress, _cancellationTokenSource.Token);

                stopwatch.Stop();
                txtSearchTime.Text = $"{stopwatch.Elapsed.TotalSeconds:F2}s";
                txtFilesFound.Text = results.Count.ToString();

                if (results.Count == 0)
                {
                    UpdateStatus("Search completed. No files found.");
                    MessageBox.Show("No files found matching the search criteria.", "Search Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
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

                foreach (var result in results)
                    _searchResults.Add(result);

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

        // ── Export Results (#4) ──────────────────────────────────────────────

        private async void BtnExportResults_Click(object sender, RoutedEventArgs e)
        {
            if (_searchResults.Count == 0)
            {
                MessageBox.Show("No results to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using (var dialog = new SaveFileDialog())
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
            using (var dialog = new SaveFileDialog())
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
            using (var dialog = new OpenFileDialog())
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
                ContentFilter = txtContentFilter.Text.Trim()
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
            if (isSearching) btnExportResults.IsEnabled = false;

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
        }

        private void UpdateStatus(string message)
        {
            txtStatus.Text = message;
        }
    }
}
