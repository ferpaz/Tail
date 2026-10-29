namespace Tail.Packaging;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Tail.Packaging <binary> <archive.zip|archive.tar.gz>");
            return 1;
        }
        try
        {
            ReleaseArchive.Create(args[0], args[1]);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Error creating the archive: {ex.Message}");
            return 1;
        }
    }
}
