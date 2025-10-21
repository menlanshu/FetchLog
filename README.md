# FetchLog

A powerful WPF-based log file search and collection tool that helps you quickly find and gather log files from multiple directories with advanced filtering capabilities.

## Features

### 🔍 Advanced Search Capabilities
- **Multiple Directory Search**: Specify one or multiple directories to search simultaneously
- **Recursive Search**: Option to search through subdirectories
- **ZIP File Support**: Automatically searches inside ZIP files (in-memory) without extraction
- **Smart File Detection**: Handles both regular files and compressed archives

### 🎯 Flexible Filtering
- **File Extension Filter**: Search specific file types (e.g., .log, .txt) or all files
- **Include Patterns**: Use wildcard patterns to include specific files (e.g., error*, *debug*)
- **Exclude Patterns**: Use wildcard patterns to exclude unwanted files (e.g., temp*, *.bak)
- **Content Search**: Search for files containing specific text
- **Case-Sensitive Option**: Toggle case sensitivity for content searches

### 📊 Real-Time Feedback
- **Search Statistics**: View search time, files found, and files copied
- **Progress Tracking**: Real-time status updates during search operations
- **Result Preview**: See all matching files in a detailed list view
- **Cancellation Support**: Cancel long-running searches at any time

### 💾 Configurable Output
- **Custom Output Location**: Choose where to save search results
- **Automatic File Organization**: Handles duplicate file names automatically
- **Complete ZIP Files**: When matches are found in ZIP files, the entire ZIP is copied

## System Requirements

- Windows 10 or later
- .NET 8.0 Runtime or later
- Visual Studio 2022 (for development)

## Building the Application

### Prerequisites
1. Install [Visual Studio 2022](https://visualstudio.microsoft.com/) with .NET desktop development workload
2. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or later

### Build Instructions
```bash
# Clone the repository
git clone https://github.com/menlanshu/FetchLog.git
cd FetchLog

# Build the project
dotnet build

# Run the application
dotnet run
```

Or open `FetchLog.csproj` in Visual Studio and press F5 to build and run.

## How to Use

### 1. Add Search Directories
- Click **"Add Directory"** to select one or more directories to search
- Use **"Remove Selected"** to remove a directory from the list
- Use **"Clear All"** to remove all directories

### 2. Configure Filters

#### File Extensions
- Enter file extensions separated by commas (e.g., `.log,.txt,.xml`)
- Leave empty to search all file types

#### Include Patterns
- Enter filename patterns to include (e.g., `error*,*debug*,app.log`)
- Supports wildcards: `*` (any characters) and `?` (single character)

#### Exclude Patterns
- Enter filename patterns to exclude (e.g., `temp*,*.bak,*old*`)
- Useful for filtering out unwanted files

#### Content Filter
- Enter text to search for within files
- Only text-based files are searched for content
- Binary files are automatically skipped

### 3. Search Options
- ✅ **Search subdirectories recursively**: Enable to search all subdirectories
- ✅ **Search inside ZIP files (in-memory)**: Enable to search within ZIP archives
- ☐ **Case-sensitive content search**: Enable for case-sensitive text matching

### 4. Set Output Location
- Click **"Browse..."** to select where search results will be saved
- Default location: `Documents/FetchLog_Results`

### 5. Start Search
- Click **"Start Search"** to begin the search operation
- View real-time progress in the status bar
- Click **"Cancel"** to stop the search at any time

### 6. Review Results
- View search statistics: time taken, files found, files copied
- See detailed results in the list view
- Open output folder to access copied files

## Usage Examples

### Example 1: Find All Error Logs
```
Directories: C:\Logs
File Extensions: .log,.txt
Include Patterns: *error*,*exception*
Content Filter: (leave empty)
Recursive: ✅
```

### Example 2: Find Configuration Files with Specific Text
```
Directories: C:\Program Files\MyApp
File Extensions: .config,.xml,.json
Include Patterns: (leave empty)
Exclude Patterns: temp*,*.bak
Content Filter: database
Recursive: ✅
```

### Example 3: Find Logs in ZIP Archives
```
Directories: D:\Archives
File Extensions: .log
Include Patterns: (leave empty)
Content Filter: ERROR
Search inside ZIP files: ✅
Recursive: ✅
```

## Technical Details

### Architecture
- **Framework**: WPF (.NET 8.0)
- **Pattern**: MVVM-inspired architecture
- **Async/Await**: All I/O operations are asynchronous
- **ZIP Handling**: In-memory processing using System.IO.Compression

### File Search Algorithm
1. Enumerate files in specified directories (recursive or non-recursive)
2. Apply file extension filter
3. Apply exclude patterns
4. Apply include patterns
5. For ZIP files: search entries in-memory
6. Apply content filter (text search)
7. Copy matching files to output directory

### Performance Considerations
- Binary file detection to avoid content searching in non-text files
- In-memory ZIP processing for faster searches
- Asynchronous operations for responsive UI
- Cancellation token support for long-running operations

## Project Structure

```
FetchLog/
├── Models/
│   ├── SearchResult.cs      # Data model for search results
│   └── SearchOptions.cs     # Configuration for search operations
├── Services/
│   └── SearchService.cs     # Core search and file handling logic
├── App.xaml                 # Application resources and styles
├── App.xaml.cs             # Application entry point
├── MainWindow.xaml         # Main UI definition
├── MainWindow.xaml.cs      # Main UI logic and event handlers
├── FetchLog.csproj         # Project configuration
└── README.md               # This file
```

## License

MIT License - Copyright (c) 2020 menlan

See [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## Changelog

### Version 1.0.0 (2025)
- Initial release
- Multiple directory search support
- Include/exclude pattern filtering
- Content-based search
- ZIP file in-memory search
- Configurable output location
- Real-time search statistics
- Recursive search option

## Support

For issues, questions, or suggestions, please open an issue on GitHub.
