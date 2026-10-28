namespace Catl.Packaging;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Uso: Catl.Packaging <binario> <archivo.zip|archivo.tar.gz>");
            return 1;
        }
        try
        {
            ReleaseArchive.Create(args[0], args[1]);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Error al crear el paquete: {ex.Message}");
            return 1;
        }
    }
}
