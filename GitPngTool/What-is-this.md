# Git-to-PNG Commit Stashing Tool

A WPF application built on .NET 8/9 that packs Git commit code changes into compressed, encrypted-style PNG noise images for transport, offline backup, or visual stashing, with reverse extraction and execution capabilities.

---

## Architecture Overview

```
[ Git Repo ] ──► [ Extract Diffs ] ──► [ ZIP Directory ] ──► [ Brotli Compression ] ──► [ PNG Image ]
                                                                                               │
[ Git Repo ] ◄── [ Run delete_files.ps1 ] ◄── [ Extract Files ] ◄── [ Decompress Stream ] ◄────┘

```

---

## 1. Encode Workflow (Git Commit to PNG)

### **Step 1: Input & Selection**

* **Repository Target:** Accepts a valid local Git repository path.
* **Commit Resolution:** Queries the git log (`git log -20 --oneline`) and displays the latest 20 commits in a selectable grid containing:
* Commit Hash
* Commit Message



### **Step 2: Change Extraction**

* Calculates the target commit diff range: `[Selected_Commit_ID]^..HEAD`.
* Queries git status via `git diff-tree --no-commit-id --name-status -r <Range>`.
* Creates an isolated temporary directory (`%TEMP%\GitPngExport_<GUID>`).
* **File Handling Logic:**
* **Added / Modified / Copied / Renamed (`A`, `M`, `C`, `R`):** Reads the complete, full file content as raw bytes directly from `HEAD` using `git show HEAD:<FilePath>` via `StandardOutput.BaseStream` (preserving Arabic UTF-8 strings, Windows-1256, and binary files without corruption).
* **Deleted (`D`):** Converts POSIX forward slashes (`/`) to standard Windows backslashes (`\`) and writes a `delete_files.ps1` PowerShell script inside the temporary folder using `Join-Path` syntax.



### **Step 3: Dual-Stage Compression**

1. **Archive Generation:** Compresses the temporary directory into a `.zip` archive using `CompressionLevel.SmallestSize`.
2. **Stream Shrinking:** Passes the `.zip` binary stream through a `BrotliStream` compressor (`CompressionLevel.SmallestSize`) to yield a `.br` compressed binary payload.

### **Step 4: Image Encoding**

* Reads the raw byte array of the Brotli-compressed file.
* Calculates total 24-bit RGB pixels needed: $\lceil \text{FileSizeBytes} / 3 \rceil$.
* Determines square image dimensions: $S = \lceil \sqrt{\text{TotalPixels}} \rceil$.
* Locks bitmap bytes using `BitmapData.LockBits` (`PixelFormat.Format24bppRgb`).


* Copies byte data row-by-row into image scanlines taking GDI+ stride alignment into account.


* Saves the resulting image as a lossless PNG (`git_change_<Random6Digits>.png`) directly into the root of the selected Git repository.

---

## 2. Decode Workflow (PNG to Repository State)

### **Step 1: Target Specification**

* Accepts the target repository directory where changes should be applied.
* Accepts the source PNG image created by the encoder.

### **Step 2: Image Decoding**

* Opens the PNG file with `System.Drawing.Bitmap`.


* Locks image bits (`Format24bppRgb`) and extracts total raw byte data row-by-row into a byte array.


* Writes the raw extracted byte array to a temporary `.br` file.

### **Step 3: Decompression & Extraction**

* Decompresses the Brotli stream (`BrotliStream.Decompress`) back into a temporary `.zip` archive.
* Extracts all files from the `.zip` archive into the specified target repository path (overwriting existing files).

### **Step 4: Post-Extraction Cleanup**

* Checks if `delete_files.ps1` exists in the root of the target repository.
* If present, initializes an in-process PowerShell engine (`System.Management.Automation.PowerShell`).
* Sets process execution policy (`Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope Process`).
* Executes `delete_files.ps1` to remove obsolete/deleted files safely without throwing errors on missing paths.
* Displays an execution log in the UI detailing each completed stage.

---

## 3. Data Safety & Encoding Rules

* **Text & Binary Preservation:** All file retrievals rely on stdout byte streams instead of string parsing to prevent character distortion in localized files (e.g., Arabic text, UTF-8 without BOM, non-ASCII comments).
* **Path Normalization:** Forward slashes from Git diff trees are explicitly sanitized to backslashes for Windows path execution.
* **Lossless Output:** The encoder enforces PNG formatting to prevent lossy compression algorithms (like JPEG) from altering pixel byte streams.