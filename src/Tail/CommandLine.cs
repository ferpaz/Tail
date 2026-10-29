namespace Tail;

public enum CommandAction { View, Help, Version }

public sealed record CommandLine(CommandAction Action, string? FilePath = null)
{
    public const string Help = """
        tail - Live text and log viewer

        Usage:
          tail <file>
          tail -- <file whose name starts with a hyphen>
          tail --help    | -h
          tail --version | -v

        Use quotes if the path contains spaces.
        Example: tail "C:\Logs\my application.log"

        Text: .txt, .log, .dat (text only), .csv, .tsv, .json, .jsonl,
        .ndjson, .xml, .yaml, .yml, .ini, .cfg, .conf, .md and others.
        Validates content, not extensions; files without extensions are also supported.
        Automatic: UTF-8/ASCII and UTF-16/UTF-32 with a BOM.
        Encoding button: also BOM-less UTF, Windows-1252 and Latin-1.
        Binary files and invalid encodings are rejected.

        Controls:
          Ctrl-W    Toggle Word Wrap (also available in the bottom bar)
          Ctrl-F    Toggle automatic follow at the end of the file
          Ctrl-E    Change the reading encoding and reload the file
          Ctrl-I    Show file information
          F5        Reopen and reload the file
          Ctrl-Q    Quit
          Arrow keys, PageUp/PageDown, Ctrl-Home/Ctrl-End and scrollbars: navigate

        Shared read access without blocking writers; updates every 250 ms.
        Bottom-bar actions also respond to mouse clicks.
        Content is kept in memory. The file is never modified or saved.
        """;

    public static CommandLine Parse(string[] args)
    {
        switch (args)
        {
            case ["--help" or "-h"]:
                return new(CommandAction.Help);
            case ["--version" or "-v"]:
                return new(CommandAction.Version);
        }

        var path = args switch
        {
            [var value] when !value.StartsWith('-') => value,
            ["--", var value] => value,
            _ => throw new ArgumentException("Specify exactly one file or use --help / --version.")
        };

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The file name cannot be empty.");

        path = Path.GetFullPath(path);

        if (Directory.Exists(path))
            throw new ArgumentException("The path refers to a directory, not a file.");

        return !File.Exists(path) 
            ? throw new FileNotFoundException("The specified file does not exist.", path) 
            : new(CommandAction.View, path);
    }
}
