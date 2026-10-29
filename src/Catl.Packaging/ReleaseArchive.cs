using System.Formats.Tar;
using System.IO.Compression;

namespace Catl.Packaging;

public static class ReleaseArchive
{
    public static void Create(string binaryPath, string archivePath)
    {
        var zip = archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (!zip && !archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The archive must have a .zip or .tar.gz extension.", nameof(archivePath));
        if (Path.GetFullPath(binaryPath).Equals(Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The binary and archive paths must be different.", nameof(archivePath));

        using var input = File.OpenRead(binaryPath);
        using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write);
        if (zip)
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create);
            using var entry = archive.CreateEntry(Path.GetFileName(binaryPath), CompressionLevel.Optimal).Open();
            input.CopyTo(entry);
        }
        else
        {
            using var compressed = new GZipStream(output, CompressionLevel.Optimal);
            using var archive = new TarWriter(compressed, TarEntryFormat.Ustar);
            var entry = new UstarTarEntry(TarEntryType.RegularFile, Path.GetFileName(binaryPath))
            {
                DataStream = input,
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                       UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                       UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
                ModificationTime = File.GetLastWriteTimeUtc(binaryPath)
            };
            archive.WriteEntry(entry);
        }
    }
}
