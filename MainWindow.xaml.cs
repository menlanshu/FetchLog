using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
        private CancellationTokenSource? _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            _directories = new ObservableCollection<string>();
            _searchResults = new ObservableCollection<SearchResult>();
            _searchService = new SearchService();

            lstDirectories.ItemsSource = _directories;
            lvResults.ItemsSource = _searchResults;

            // Set default output path
            txtOutputPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FetchLog_Results");
        }

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
                var result = MessageBox.Show("Are you sure you want to clear all directories?", "Confirm Clear",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

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
                {
                    dialog.SelectedPath = txtOutputPath.Text;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtOutputPath.Text = dialog.SelectedPath;
                    UpdateStatus($"Output path set to: {dialog.SelectedPath}");
                }
            }
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            // Validate inputs
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

            // Prepare search options
            var options = new SearchOptions
            {
                SearchDirectories = _directories.ToList(),
                Recursive = chkRecursive.IsChecked ?? true,
                SearchInZip = chkSearchInZip.IsChecked ?? true,
                CaseSensitive = chkCaseSensitive.IsChecked ?? false,
                OutputPath = txtOutputPath.Text,
                ContentFilter = txtContentFilter.Text.Trim()
            };

            // Parse file extensions
            if (!string.IsNullOrWhiteSpace(txtFileExtensions.Text))
            {
                options.FileExtensions = txtFileExtensions.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(ext => ext.Trim().StartsWith(".") ? ext.Trim() : "." + ext.Trim())
                    .ToList();
            }

            // Parse include patterns
            if (!string.IsNullOrWhiteSpace(txtIncludePatterns.Text))
            {
                options.IncludePatterns = txtIncludePatterns.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .ToList();
            }

            // Parse exclude patterns
            if (!string.IsNullOrWhiteSpace(txtExcludePatterns.Text))
            {
                options.ExcludePatterns = txtExcludePatterns.Text
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .ToList();
            }

            // Clear previous results
            _searchResults.Clear();
            txtFilesFound.Text = "0";
            txtFilesCopied.Text = "0";
            txtSearchTime.Text = "0.00s";

            // Set UI state
            SetSearchingState(true);

            // Create cancellation token
            _cancellationTokenSource = new CancellationTokenSource();

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var progress = new Progress<string>(status => UpdateStatus(status));

                // Search for files
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

                // Copy files to output
                UpdateStatus("Copying files to output directory...");
                var copiedCount = await _searchService.CopyFilesToOutputAsync(results, options.OutputPath, progress, _cancellationTokenSource.Token);

                txtFilesCopied.Text = copiedCount.ToString();

                // Update results display
                foreach (var result in results)
                {
                    _searchResults.Add(result);
                }

                UpdateStatus($"Search completed. Found {results.Count} file(s), copied {copiedCount} file(s).");

                MessageBox.Show($"Search completed successfully!\n\nFiles found: {results.Count}\nFiles copied: {copiedCount}\nTime: {stopwatch.Elapsed.TotalSeconds:F2}s\n\nOutput location: {options.OutputPath}",
                    "Search Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                // Ask if user wants to open output folder
                var openResult = MessageBox.Show("Do you want to open the output folder?", "Open Folder",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (openResult == MessageBoxResult.Yes)
                {
                    Process.Start("explorer.exe", options.OutputPath);
                }
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
            chkRecursive.IsEnabled = !isSearching;
            chkSearchInZip.IsEnabled = !isSearching;
            chkCaseSensitive.IsEnabled = !isSearching;
        }

        private void UpdateStatus(string message)
        {
            txtStatus.Text = message;
        }
    }
}
