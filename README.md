# Tail - Live text and log viewer

A **C# / .NET 10** console application for Windows and Linux, launched from
PowerShell, CMD or a Linux terminal. Its Microsoft Edit-inspired interface
supports mouse input, vertical and horizontal scrollbars, Word Wrap and terminal
resizing.

It does not open a separate graphical window or change the console size.

## Running a release

**PowerShell:**

```powershell
.\releases\1.0.0\win-x64\tail.exe "C:\Logs\my application.log"
```

**CMD:**

```cmd
releases\1.0.0\win-x64\tail.exe "C:\Logs\my application.log"
```

The published executable is **self-contained**: installing .NET is not required.
Use Windows Terminal, PowerShell or CMD in an interactive console.

On Linux, use the extensionless binary for the appropriate RID. If you copy it
from Windows, grant execute permission with `chmod +x tail`.
Run it as `./tail "application.log"` from its directory to avoid invoking the
system's standard `tail` command.

> Self-contained releases still require the operating system's native dependencies
> for .NET 10 and Terminal.Gui, such as ICU and ncurses on Linux.
> Redirected input/output and IDE output panels are not supported.

### Arguments

```text
tail --help       (or -h)
tail --version    (or -v)
tail -- "-file.log"
```

## Controls

| Control | Action |
|---|---|
| **Ctrl-W** | Toggle Word Wrap; also available through the bottom-bar button. |
| **Ctrl-F** | Pause/resume automatic scrolling to the end. Reading and file information continue updating. Pause to review earlier lines without new messages moving the view. |
| **Ctrl-E / encoding button** | Select Automatic, UTF-8, UTF-16 LE/BE, UTF-32 LE/BE, Windows-1252 or Latin-1. Reloads from the beginning; manual UTF modes also support files without a BOM. |
| **Ctrl-I / More information button** | Open a dialog showing the folder, file name, creation time, byte count, last modification time and active encoding. Metadata continues updating while the dialog is open. |
| **F5** | Reopen the same file from the beginning. |
| **Ctrl-Q / Quit button** | Close the application. |
| **Arrow keys, PageUp/PageDown, Ctrl-Home/Ctrl-End** | Navigate the content. |
| **Scrollbars** | Scroll vertically/horizontally with the mouse. Horizontal scrolling is unnecessary with Word Wrap enabled. |

The header shows only the file name. All actions are in the **bottom status bar**
and respond to clicks, including encoding, Word Wrap, follow, reload, information
and quit. The active encoding appears on its button; the automatic/manual setting
is shown in the information dialog.

Polling occurs every **250 ms**; reading does not block the interface.
The interface fills the available area and adapts to width/height changes.
Text is **gray on black**, with one row of padding above and below and two columns
on each side. The vertical scrollbar remains visible at the right edge; the
horizontal scrollbar appears below the text when Word Wrap is off.
Bottom-bar actions wrap to additional rows if they do not fit the available width.
A minimum of **40 columns and 16 rows** is recommended, including the selector.

## Formats and encodings

Plain-text files are supported, for example:

```text
.txt .log .csv .tsv .json .jsonl .ndjson .xml .yaml .yml
.ini .cfg .conf .config .properties .md .sql .ps1 .bat .cmd
.cs .html .css .js .dat (text only)
```

Files without an extension or with any other extension are also supported if they
contain text. Scripts are not executed and formats are not interpreted: their
text is displayed.

**An extension does not prove that a file contains text.** Each block read is
validated: strict UTF-8 (including ASCII), UTF-8 with a BOM, UTF-16 LE/BE with a BOM
and UTF-32 LE/BE with a BOM. Control characters other than tab, CR and LF are
rejected.

Unicode characters are preserved even when another process writes them in parts.
An incomplete character at the end remains pending until the rest arrives.

ANSI/Windows-1252 and BOM-less UTF-16/32 are not detected automatically:
select the encoding manually with **Ctrl-E** or the **encoding button**.
Logs containing ANSI/ESC sequences must be converted to text without controls.

PDF, DOC/DOCX, XLS/XLSX, images, executables, compressed files and binary formats
are not supported, even if renamed to `.log` or `.txt`.

### References

- [Text formats - FileFormat](https://docs.fileformat.com/misc/text-formats/)
- [Common file extensions - FileInfo](https://fileinfo.com/filetypes/common)
- [UTF and BOM - Unicode](https://www.unicode.org/faq/utf_bom.html)

## Reading and limitations

Files are opened with `FileShare.ReadWrite | FileShare.Delete`, requesting read
access only. The application does not save, modify or deliberately lock the file.
If the writer uses exclusive access, Windows may prevent reading; the error is
displayed and reading is retried. Changes become visible when the writer flushes
its buffers.

Rotation is detected using file identity, truncation and changes to the previously
read segment near the end. **F5** forces a complete reload. If the file disappears
or reading fails, the last content is preserved and an error is displayed; reading
resumes automatically when the file becomes available again.

**All content read is kept in memory, without hidden truncation.**
This is not a virtualized viewer for multi-GB files: memory usage and the cost of
refreshing the view grow with the content. Arbitrary rewrites of earlier parts
require **F5**; the intended use case is append/tail.

The file may change while being read; an atomic snapshot of content and metadata
from a file written by another process is not guaranteed.

On Linux, rotation is identified by device and inode. If the file system does not
record creation time, .NET may report an alternative timestamp.
File identity requires `statx` (Linux kernel **4.11 or later**); if a container
blocks it through seccomp, an error is reported rather than skipping rotation
detection.

## Development

Requires the **.NET 10 SDK**.
Visual Studio or VSCode solution: `src\Tail.sln`.

```powershell
dotnet build .\src\Tail.sln
dotnet run --project .\src\Tail -- "C:\Logs\application.log"
dotnet test .\src\Tail.sln
dotnet format .\src\Tail.sln --verify-no-changes
```

Tests use isolated temporary directories.
UI dependency: **Terminal.Gui 1.19.0**, MIT license.

### Metadata and version

Defined in `src\Directory.Build.props`.
Change `<Version>` there using the `major.minor.patch` format; the executable,
version output and release directory use the same value.

The .NET SDK derives `AssemblyVersion` and `FileVersion` from `Version`.
Author, copyright, product, title and description are defined in the same file.

## Publishing

**The entire RID matrix:**

```powershell
powershell -File .\publish.ps1
```

**A single RID:**

```powershell
.\publish.ps1 -Runtime win-arm64
```

**A subset from PowerShell:**

```powershell
.\publish.ps1 -Runtime win-x64,linux-x64,linux-arm64
```

### Platform executables

Output is organized as `releases\<version>\<RID>` with **one file per RID**:

| RID | Binary | Platform |
|---|---|---|
| `win-x64` | `tail.exe` | Windows x64 |
| `win-x86` | `tail.exe` | Windows x86 |
| `win-arm64` | `tail.exe` | Windows ARM64 |
| `linux-x64` | `tail` | Linux glibc x64 |
| `linux-arm` | `tail` | Linux glibc ARM32 (ARMv7) |
| `linux-arm64` | `tail` | Linux glibc ARM64 |
| `linux-musl-x64` | `tail` | Linux musl x64, for example Alpine |
| `linux-musl-arm` | `tail` | Linux musl ARM32 (ARMv7) |
| `linux-musl-arm64` | `tail` | Linux musl ARM64 |

No DLL, PDB, README or ZIP files are copied alongside the executable.

### GitHub Releases archives

One archive per RID is created inside `releases\<version>`:

```text
tail-<version>-win-x64.zip
tail-<version>-win-x86.zip
tail-<version>-win-arm64.zip
tail-<version>-linux-x64.tar.gz
tail-<version>-linux-arm.tar.gz
tail-<version>-linux-arm64.tar.gz
tail-<version>-linux-musl-x64.tar.gz
tail-<version>-linux-musl-arm.tar.gz
tail-<version>-linux-musl-arm64.tar.gz
```

Each archive contains **only the binary**, without an enclosing directory.
Linux TAR.GZ archives record **0755** permissions, even when created on Windows.
These archives can be attached directly to a GitHub Release.
The packager uses .NET 10; the script does not require tar, zip or 7-Zip.

### Behavior and compatibility

Temporary build directories are removed on completion, including after errors.
If the destination contains other files, the script fails without deleting them.

Windows ARM32 and Linux x86 do not have official self-contained .NET 10 runtimes.
Third-party-maintained Linux architectures, such as s390x or ppc64le, do not have
the official NuGet runtime packs required by this script.

Executables for other architectures are cross-compiled; running them requires the
appropriate OS, architecture and native libraries.
Release binaries and build artifacts are excluded from Git.
