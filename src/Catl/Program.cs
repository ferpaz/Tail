using System.Reflection;
using System.Text;

namespace Catl;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = CommandLine.Parse(args);
            if (options.Action == CommandAction.Help)
            {
                Console.WriteLine(CommandLine.Help);
                return 0;
            }

            if (options.Action == CommandAction.Version)
            {
                Console.WriteLine($"catl {Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}");
                return 0;
            }

            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                Console.Error.WriteLine("Error: catl requires an interactive terminal (PowerShell, CMD or a Linux terminal).");
                return 1;
            }

            var reader = new TailReader(options.FilePath!);
            using (var access = new FileStream(reader.FilePath, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
            }
            var info = new FileInfo(reader.FilePath);
            var initial = new FileUpdate("", true,
                new(info.DirectoryName!, info.Name, info.CreationTime, info.Length, info.LastWriteTime),
                "pending", true);
            LogWindow.Run(reader, initial);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                   or DecoderFallbackException or NotSupportedException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine("Use catl --help for usage information.");
            return 1;
        }
    }
}
