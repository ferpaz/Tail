using System.Formats.Tar;
using System.IO.Compression;
using Catl.Packaging;

namespace Catl.Tests;

public sealed class ReleaseArchiveTests
{
    [Theory]
    [InlineData(".zip", "catl.exe")]
    [InlineData(".tar.gz", "catl")]
    public void PackageContainsOnlyBinaryWithOriginalContent(string extension, string binaryName)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"catl-package-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var binaryPath = Path.Combine(directory, binaryName);
        var archivePath = Path.Combine(directory, "release" + extension);
        byte[] content = [0, 1, 2, 255, 128, 0];
        File.WriteAllBytes(binaryPath, content);
        try
        {
            ReleaseArchive.Create(binaryPath, archivePath);
            if (extension == ".zip")
            {
                using var archive = ZipFile.OpenRead(archivePath);
                var entry = Assert.Single(archive.Entries);
                Assert.Equal(binaryName, entry.FullName);
                using var stream = entry.Open();
                using var actual = new MemoryStream();
                stream.CopyTo(actual);
                Assert.Equal(content, actual.ToArray());
            }
            else
            {
                using var file = File.OpenRead(archivePath);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var archive = new TarReader(gzip);
                var entry = Assert.IsType<UstarTarEntry>(archive.GetNextEntry());
                Assert.Equal(binaryName, entry.Name);
                Assert.Equal(TarEntryType.RegularFile, entry.EntryType);
                Assert.Equal((UnixFileMode)Convert.ToInt32("755", 8), entry.Mode);
                using var actual = new MemoryStream();
                Assert.NotNull(entry.DataStream);
                entry.DataStream.CopyTo(actual);
                Assert.Equal(content, actual.ToArray());
                Assert.Null(archive.GetNextEntry());
            }
            Assert.Throws<IOException>(() => ReleaseArchive.Create(binaryPath, archivePath));
            Assert.Equal(content, File.ReadAllBytes(binaryPath));
        }
        finally
        {
            File.Delete(binaryPath);
            File.Delete(archivePath);
            Directory.Delete(directory);
        }
    }
}
