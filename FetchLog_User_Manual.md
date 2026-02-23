# FetchLog — User Manual

**Version:** 1.0
**Platform:** Windows (.NET 8, WPF)

---

## Table of Contents

1. [Overview](#1-overview)
2. [Quick Start](#2-quick-start)
3. [Search Directories](#3-search-directories)
4. [Filter Options](#4-filter-options)
5. [Search Options](#5-search-options)
6. [Output Location, Profiles & History](#6-output-location-profiles--history)
7. [Running a Search](#7-running-a-search)
8. [Search Results Panel](#8-search-results-panel)
9. [File Preview Panel](#9-file-preview-panel)
10. [Export Results](#10-export-results)
11. [Data Storage & File Locations](#11-data-storage--file-locations)
12. [Feature Reference](#12-feature-reference)

---

## 1. Overview

FetchLog is a Windows desktop tool for locating, previewing, and collecting log files (and any other text or binary files) across one or more directories. It supports rich filtering, archive searching, content matching, duplicate detection, log format classification, and flexible output options.

---

## 2. Quick Start

1. Click **Add Directory** and select the folder(s) you want to search.
2. *(Optional)* Type a file extension in **File Extensions** (e.g. `.log,.txt`).
3. *(Optional)* Type a word or phrase in **Content Filter**.
4. Set an **Output Location** (defaults to `Documents\FetchLog_Results`).
5. Click **Start Search**.
6. When complete, matched files are copied to the output folder and listed in the results panel.

---

## 3. Search Directories

### 3.1 Adding Directories

| Action | How |
|--------|-----|
| Add a directory via browser | Click **Add Directory** |
| Add multiple directories via drag & drop | Drag folders from Windows Explorer and drop onto the directory list |
| Remove a directory | Select it in the list and click **Remove Selected** |
| Clear all directories | Click **Clear All** (prompts for confirmation) |

Multiple directories are searched in sequence. Results from all directories are merged into a single list.

### 3.2 Favorite Directories

The **Favorites** row below the directory list lets you bookmark directories you use often.

| Button | Action |
|--------|--------|
| **★ Save Current** | Saves the currently *selected* directory from the list as a favorite |
| **Add to Search** | Appends the selected favorite to the current search directory list |
| **Remove** | Permanently removes the selected favorite |

Favorites are saved to `Documents\FetchLog\favorites.json` and persist between sessions.

---

## 4. Filter Options

All filters are optional. Leave a field blank to skip that filter.

### 4.1 File Extensions

Enter one or more extensions separated by commas or semicolons.
The leading dot is optional: `.log,.txt` and `log,txt` are equivalent.

**Examples:** `.log` · `.log,.txt,.csv` · `xml;json`

### 4.2 Include Patterns

Wildcard patterns for file *names* that **must match** to be included.
Supports `*` (any characters) and `?` (single character).

**Examples:** `error*` · `*debug*` · `app_?.log`

### 4.3 Exclude Patterns

Wildcard patterns for file names that are **always skipped**.

**Examples:** `temp*` · `*.bak` · `*_old*`

### 4.4 Content Filter

Text or regular expression that the file content must contain.

- With **Use regex** unchecked: plain text substring match.
- With **Use regex** checked: full .NET regex (e.g. `ERROR\s+\d{4}-\d{2}-\d{2}`).
- With **Multiline regex** also checked: the `.` character matches newline, allowing patterns to span multiple lines.
- With **Case-sensitive** checked: the match is case-sensitive.

**Min hits:** When set, only files that contain the pattern *at least* this many times are included.

### 4.5 File Size Filter

Check the **Min:** checkbox to activate size filtering.

| Field | Meaning |
|-------|---------|
| Min | Minimum file size (inclusive) |
| Max | Maximum file size (inclusive) |
| Unit | B · KB · MB · GB |

### 4.6 Date Range Filter

Check the **From:** checkbox to activate date filtering.

| Field | Meaning |
|-------|---------|
| From | Start date (inclusive) |
| To | End date (inclusive, end of day) |
| Filter by | **Last Modified** (default) or **Created** |

### 4.7 Collection Cap

Limits how many files are collected in a single search run. Both limits can be used together; whichever is hit first stops the collection.

| Field | Meaning |
|-------|---------|
| Max files | Stop collecting after this many matched files |
| Max total size | Stop collecting once combined file size exceeds this threshold |

### 4.8 Rename on Copy

When enabled, files are renamed as they are written to the output folder (or ZIP archive).

| Field | Meaning |
|-------|---------|
| Prefix | Text prepended to the original file name |
| Suffix | Text inserted before the file extension |

**Example:** Prefix `BACKUP_` + Suffix `_2024` applied to `app.log` → `BACKUP_app_2024.log`

---

## 5. Search Options

Checkboxes in the **Search Options** panel control how the search behaves.

| Option | Default | Description |
|--------|---------|-------------|
| Search subdirectories recursively | On | Descends into all sub-folders. When off, only the top-level folder is searched. |
| Search inside ZIP, 7z, TAR & RAR files | On | Opens `.zip`, `.7z`, `.tar`, `.tar.gz`, `.tgz`, `.tar.bz2`, `.tbz2`, and `.rar` archives and searches their entries against all active filters. |
| Case-sensitive content search | Off | Makes the Content Filter match case-sensitively. |
| Use regex for content search | Off | Treats the Content Filter as a .NET regular expression. |
| Multiline regex (dot matches newline) | Off | In regex mode, makes `.` match `\n` so a single pattern can span multiple lines. |
| Compress output to ZIP archive | Off | Bundles all collected files into a single timestamped `.zip` file in the output folder instead of copying them individually. |
| Preserve directory structure in output | Off | Mirrors the original folder tree under the output directory. When off, all files are copied flat into the output root (duplicated names get a counter suffix). |
| Show results as found | On | Displays each matched file in the results list immediately as it is found, rather than waiting for the search to finish. |
| Detect duplicate files | Off | After the search completes, computes MD5 hashes to identify files with identical content. Duplicate rows are highlighted in orange; hover over a highlighted row to see which file it duplicates. |
| Show MD5 hash | Off | Computes and displays the full MD5 checksum of each matched file in the **Hash (MD5)** column. |
| Detect log format | Off | Scans the first 15 lines of each file and classifies it as one of: **Apache**, **Syslog**, **Log4j**, **JSON**, **WinEvent**, **CSV**, or **Generic**. Shown in the **Format** column. |

---

## 6. Output Location, Profiles & History

### 6.1 Output Location

The path where matched files are copied (or compressed). Click **Browse…** to choose a folder. The default is `Documents\FetchLog_Results`. The folder is created automatically if it does not exist.

### 6.2 Search Profiles

Save and restore the entire form state (all filters, options, and directory list) as a `.json` file.

| Button | Action |
|--------|--------|
| **Save Profile** | Saves current settings to a chosen file under `Documents\FetchLog\Profiles\` |
| **Load Profile** | Opens a previously saved `.json` profile and restores all settings |

### 6.3 Search History

The last 20 completed searches are automatically saved.

| Control | Action |
|---------|--------|
| History dropdown | Shows entries in the format `date  \|  directories  \|  "filter"  →  N file(s)` |
| **Load** | Restores the selected history entry's settings into the form |
| **Clear** | Deletes all saved history entries |

---

## 7. Running a Search

### 7.1 Start / Cancel

| Button | Action |
|--------|--------|
| **Start Search** | Validates inputs, runs the search, copies files, saves to history |
| **Cancel** | Requests cancellation; the current file operation is allowed to finish cleanly |

### 7.2 Progress & Statistics

The progress bar at the bottom of the results panel is shown while a search is running.

The **Search Statistics** bar displays:

| Statistic | Meaning |
|-----------|---------|
| Search Time | Elapsed wall-clock time of the search + copy phase |
| Files Found | Number of files that matched all active filters |
| Files Copied | Number of files successfully written to the output location |
| Total Size | Combined uncompressed size of all matched files |

### 7.3 What happens at the end

When the search completes, a summary dialog shows the counts and output path. A second prompt asks whether to open the output folder in Windows Explorer. The completed search is saved to history automatically.

---

## 8. Search Results Panel

### 8.1 Results List Columns

| Column | Content |
|--------|---------|
| File Name | Name of the matched file |
| Source Path | Full path where the file was found |
| Size | Human-readable file size |
| Type | `File` for regular files; `ZIP`, `7Z`, `TAR`, `TAR.GZ`, `TAR.BZ2`, `RAR` for archive container rows |
| Modified | Last-modified timestamp (`yyyy-MM-dd HH:mm:ss`) |
| Matches | Number of content filter hits inside the file |
| Line | Line number of the first match |
| Hash (MD5) | First 8 hex characters of the MD5 checksum (requires **Show MD5 hash**) |
| Format | Detected log format (requires **Detect log format**) |

### 8.2 Sorting

Click any column header to sort the results by that column. Click again to reverse the sort direction. An **▲** or **▼** indicator appears on the active sort column.

### 8.3 Grouping

The **Group by:** dropdown above the results list organises rows into collapsible sections:

| Option | Groups by |
|--------|-----------|
| None | No grouping (default) |
| Extension | File extension (`.log`, `.txt`, etc.) |
| Directory | Search root directory the file was found under |
| Date (Month) | Year-month of the file's last-modified date (`yyyy-MM`) |

### 8.4 Duplicate Highlighting

When **Detect duplicate files** is enabled, rows that are content-identical to another file in the results are highlighted in **light orange**. Hovering over a highlighted row shows a tooltip with the path of the original (first-seen) file.

### 8.5 Double-Click to Open

Double-clicking a result row opens the file in its default Windows application. This is not available for archive-internal entries.

### 8.6 Right-Click Context Menu

Right-clicking any result row shows:

| Menu Item | Action |
|-----------|--------|
| Open File | Opens the file in its default application |
| Open Containing Folder | Opens Windows Explorer and selects the file (or the archive, for archive entries) |
| Copy Full Path | Copies the full source path to the clipboard |
| Copy File Name | Copies just the file name to the clipboard |
| Copy MD5 Hash | Copies the full MD5 hash to the clipboard (enabled only when the hash is available) |

---

## 9. File Preview Panel

Selecting a row in the results list loads the file's content into the preview panel below the GridSplitter.

- **No content filter active:** The first 500 lines of the file are shown, with a note if the file is longer.
- **Content filter active:** Only the matching lines are shown, with ±2 lines of context around each hit. Gaps between non-adjacent matches are indicated by `···`.

The preview header displays the file name and either the total line count, or the match count and first-match line number.

**Drag the splitter** (the grey bar between the list and preview) to resize the two panels.

> Binary files and files inside archives show a placeholder message instead of content.

---

## 10. Export Results

After a search completes, the **Export Results** button becomes active.

Clicking it opens a Save dialog with two formats:

| Format | Content |
|--------|---------|
| **CSV** (`.csv`) | All 10 columns: File Name, Source Path, Size, Type, Last Modified, Matches, First Match Line, Hash (MD5), Format, Duplicate Of |
| **HTML** (`.html`) | Styled table with the same 10 columns; duplicate rows are highlighted in orange |

After saving, a prompt asks whether to open the exported file.

---

## 11. Data Storage & File Locations

All persistent data is stored under `%USERPROFILE%\Documents\FetchLog\`:

| File | Contents |
|------|---------|
| `favorites.json` | Saved favorite directory paths |
| `search_history.json` | Last 20 search history entries (up to 20) |
| `Profiles\*.json` | User-saved search profiles |

The default output folder is `%USERPROFILE%\Documents\FetchLog_Results\`.

---

## 12. Feature Reference

| # | Feature | Where in UI |
|---|---------|-------------|
| 1 | Multi-directory search | Search Directories |
| 2 | File extension filter | Filter Options → File Extensions |
| 3 | Include / Exclude filename patterns | Filter Options → Include / Exclude Patterns |
| 4 | Export results to CSV or HTML | Export Results button |
| 5 | Search profiles (save/load) | Output Location → Profiles |
| 6 | Output path browser | Output Location → Browse… |
| 7 | Recursive vs. top-level search | Search Options → Search subdirectories recursively |
| 8 | Content preview with match context | File Preview panel |
| 9 | Content filter (text or regex) | Filter Options → Content Filter |
| 10 | Minimum match count | Filter Options → Content Filter → Min hits |
| 11 | File size filter | Filter Options → File Size |
| 12 | Search history | Output Location → History |
| 13 | Favorite directories | Search Directories → Favorites |
| 14 | Drag & drop directory support | Search Directories list (drop target) |
| 15 | Collection cap (file count & total size) | Filter Options → Collection Cap |
| 16 | Rename files on copy | Filter Options → Rename on Copy |
| 17 | Result grouping | Search Results → Group by |
| 18 | ZIP, 7z, TAR, TAR.GZ, TAR.BZ2, RAR archive search | Search Options → Search inside ZIP, 7z, TAR & RAR files |
| 19 | Duplicate file detection | Search Options → Detect duplicate files |
| 20 | MD5 hash display | Search Options → Show MD5 hash |
| 21 | Incremental / streaming results | Search Options → Show results as found |
| 22 | Log format auto-detection | Search Options → Detect log format |
| — | Column-header sorting | Click any column header in the results list |
| — | Double-click to open file | Double-click a result row |
| — | Right-click context menu | Right-click a result row |
| — | Date range filter | Filter Options → Date Range |
| — | Case-sensitive & regex search | Search Options |
| — | Multiline regex | Search Options |
| — | Compress output to ZIP | Search Options |
| — | Preserve directory structure | Search Options |
| — | Cancel in-progress search | Cancel button |
| — | Total Size statistic | Search Statistics bar |
