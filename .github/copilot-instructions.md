# Tail repository instructions

Tail is a read-only, full-screen live log viewer for Windows and Linux, written
in C# targeting .NET 10. User documentation is in `README.md`; UI labels,
command-line help, error messages, scripts and code identifiers are English.

## Build, test and formatting

Run these commands from the repository root in PowerShell with the .NET 10 SDK:

```powershell
dotnet build .\src\Tail.sln --configuration Release
dotnet test .\src\Tail.sln --configuration Release

# One test method (all cases if the method is a theory):
dotnet test .\src\Tail.Tests\Tail.Tests.csproj --filter "FullyQualifiedName=Tail.Tests.TailReaderTests.PreservesSplitUtf8CharacterAndBom"

# One test class:
dotnet test .\src\Tail.Tests\Tail.Tests.csproj --filter "FullyQualifiedName~Tail.Tests.LogWindowTests"

# Formatting/analyzer check, or apply formatting:
dotnet format .\src\Tail.sln --verify-no-changes
dotnet format .\src\Tail.sln

dotnet run --project .\src\Tail -- "C:\Logs\application.log"
```

`src\Directory.Build.props` enables warnings-as-errors, latest analysis and
build-time code-style enforcement. The viewer requires an interactive terminal;
redirected stdin/stdout and IDE output panels are rejected. Help/version work
without an interactive terminal. Linux execution also needs .NET's native OS
dependencies, including ICU, and Terminal.Gui's ncurses dependencies.

## Architecture and state flow

- `Program` uses `CommandLine.Parse`, handles informational arguments, validates
  read access, creates an empty metadata snapshot and starts `LogWindow`.
  Initial content loading happens in the background so encoding failures can be
  corrected from the UI rather than terminating before the selector is available.
- `TailReader.Poll` is the stateful incremental reader. Each poll opens and closes
  a read-only stream with `FileShare.ReadWrite | FileShare.Delete`, reads at most
  256 KiB and returns `FileUpdate`: appended text, reset flag, metadata, encoding
  and whether it has caught up. A reset means replace displayed content, not append.
- Rotation detection combines native file identity, length/last-write checks and
  a checkpoint of up to 64 previously consumed bytes. `FileIdentity` uses Windows
  handle information or Linux `statx` via libc syscall with architecture-specific
  numbers and an explicit kernel ABI layout. Linux needs kernel 4.11+ and access
  to `statx`; retain explicit failures if identity cannot be obtained.
- `LogWindow` uses Terminal.Gui **1.x** APIs. Its background monitor owns the reader;
  UI reload/encoding requests cross threads using `Interlocked`/`Volatile`.
  Updates pass through a capacity-one, single-reader/single-writer channel with
  backpressure. The UI consumes messages on a 100 ms main-loop timer; the monitor
  waits 250 ms only after catching up. Do not mutate views from the worker.
- The UI retains the entire decoded file in a `StringBuilder` and replaces the
  `TextView.Text` on content changes; it is not virtualized. Pausing follow stops
  automatic scrolling, not reading or metadata refresh. F5 and encoding changes
  reset the reader. Expected read/decode errors preserve the view, show status
  and retry; shutdown cancels/joins the worker and restores the terminal.

## Encoding and UI conventions

Validate decoded content rather than maintaining an extension allowlist.
Automatic mode uses BOM detection or strict UTF-8; manual modes also allow
BOM-less UTF-16/32, Windows-1252 and Latin-1. Retain the decoder across polls and
never flush it at temporary EOF: writers may split a BOM or Unicode character
across writes. Reject control characters except CR, LF and tab, including ESC.

`FileTextEncoding` values correspond by index to the encoding dialog's radio
options; keep the enum, `TextEncoding.Resolve` and dialog choices synchronized.
The status-bar encoding button and information dialog display the encoding
carried by the applied update, not merely the requested selection.
Keep shortcut changes consistent with `CommandLine.Help`,
`README.md` and UI tests. Ctrl-Q must also exit from the modal encoding dialog.
The header contains only the filename; metadata lives in a live information
dialog (Ctrl-I). Bottom status-bar buttons wrap to additional rows as needed and
share actions with keyboard shortcuts. Both dialogs must support Ctrl-Q.
Text stays gray on black even when focused, with one row and two columns of
padding. The right-edge scrollbar stays visible; the horizontal bar hides during
Word Wrap. Terminal.Gui 1.x recalculates scrollbar layout using its own Bounds
when Size changes, so restore scrollbar frames against the parent after updates.

## Tests

Tests use xUnit. `TailReaderTests` creates isolated temporary directories and
exercises real file appends, concurrent writers, rotation and split encodings.
`LogWindowTests` initializes Terminal.Gui's `FakeDriver`, drives keys/mouse and
resize events through the main loop, and always calls `Application.Shutdown`.
Terminal.Gui application state is global: keep UI tests serialized (currently
they reside in one xUnit test class). The app exposes internals to `Tail.Tests`
using `InternalsVisibleTo`. Archive tests check exact contents and Linux mode.

## Versioning and publication

Change version and executable metadata only in `src\Directory.Build.props`.
The SDK derives assembly/file versions; `--version` reads the assembly version.
`publish.ps1` reads the evaluated MSBuild version and requires `major.minor.patch`.
Run publication only when the user requests a commit of approved changes, not
after each change. Publication must succeed before creating that commit.

```powershell
.\publish.ps1                              # Entire nine-RID matrix
.\publish.ps1 -Runtime win-x64             # One RID
.\publish.ps1 -Runtime win-x64,linux-arm64  # Subset
```

The matrix is Windows x64/x86/ARM64 plus Linux glibc and musl x64/ARM32/ARM64.
Publications are self-contained single-file, untrimmed executables. Each
`releases\<version>\<RID>` contains only `tail.exe` (Windows) or `tail` (Linux).
Sibling archives named `tail-<version>-<RID>.zip` or `.tar.gz` contain only that
binary at the archive root; Linux TAR entries must have mode 0755 even when
packaged on Windows. `Tail.Packaging` implements this with .NET compression/TAR
APIs, invoked by the script. Preserve staging cleanup and refusal to delete
unexpected files in destination folders. Release binaries, archives, `bin` and
`obj` are ignored by Git; only `releases\.gitkeep` tracks the release directory.
