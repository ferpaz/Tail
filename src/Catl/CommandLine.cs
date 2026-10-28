namespace Catl;

public enum CommandAction { View, Help, Version }

public sealed record CommandLine(CommandAction Action, string? FilePath = null)
{
    public const string Help = """
        catl - Visor de texto y logs en vivo

        Uso:
          catl <archivo>
          catl -- <archivo cuyo nombre empieza con guion>
          catl --help    | -h
          catl --version | -v

        Use comillas si la ruta contiene espacios.
        Ejemplo: catl "C:\Logs\mi aplicacion.log"

        Texto: .txt, .log, .dat (solo texto), .csv, .tsv, .json, .jsonl,
        .ndjson, .xml, .yaml, .yml, .ini, .cfg, .conf, .md y otros.
        Se valida el contenido, no la extension; tambien admite archivos sin extension.
        Automatico: UTF-8/ASCII y UTF-16/UTF-32 con BOM.
        Boton Encoding: tambien UTF sin BOM, Windows-1252 y Latin-1.
        Los archivos binarios y las codificaciones invalidas se rechazan.

        Controles:
          Ctrl-W    Activar/desactivar Word Wrap (tambien con el mouse)
          Ctrl-F    Activar/desactivar seguimiento automatico del final
          Ctrl-E    Cambiar encoding de lectura y recargar el archivo
          F5        Reabrir y recargar el archivo
          Ctrl-Q    Salir
          Flechas, RePag/AvPag, Ctrl-Home/Ctrl-End y barras: navegar

        Lectura compartida sin bloquear escritores; actualizacion cada 250 ms.
        El contenido se conserva en memoria. No modifica ni guarda el archivo.
        """;

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
            return new(CommandAction.Help);
        if (args.Length == 1 && args[0] is "--version" or "-v")
            return new(CommandAction.Version);

        var path = args switch
        {
            [var value] when !value.StartsWith('-') => value,
            ["--", var value] => value,
            _ => throw new ArgumentException("Indique exactamente un archivo o use --help / --version.")
        };
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("El nombre del archivo no puede estar vacio.");

        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
            throw new ArgumentException("La ruta corresponde a una carpeta, no a un archivo.");
        if (!File.Exists(path))
            throw new FileNotFoundException("No existe el archivo indicado.", path);
        return new(CommandAction.View, path);
    }
}
