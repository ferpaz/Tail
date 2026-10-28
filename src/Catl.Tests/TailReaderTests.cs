using System.Text;
using Catl;

namespace Catl.Tests;

public sealed class TailReaderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"catl-tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(directory, "test.log");

    public TailReaderTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void ReadsInitialContentAndAppendedPartialLinesWithoutDuplicates()
    {
        File.WriteAllText(FilePath, "inicio\nparcial", new UTF8Encoding(false));
        var reader = new TailReader(FilePath);
        var initial = reader.Poll();
        Assert.True(initial.Reset);
        Assert.Equal("inicio\nparcial", initial.Text);
        Assert.Equal("", reader.Poll().Text);
        File.AppendAllText(FilePath, " completada\notra\n", new UTF8Encoding(false));
        var update = reader.Poll();
        Assert.False(update.Reset);
        Assert.Equal(" completada\notra\n", update.Text);
        Assert.Equal(new FileInfo(FilePath).Length, update.Metadata.Length);
        Assert.Equal("", reader.Poll().Text);
    }

    [Fact]
    public void DoesNotBlockConcurrentWriterOrRotation()
    {
        File.WriteAllText(FilePath, "viejo\n");
        using var writer = new FileStream(FilePath, FileMode.Open, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        var reader = new TailReader(FilePath);
        reader.Poll();
        writer.Seek(0, SeekOrigin.End);
        writer.Write(Encoding.UTF8.GetBytes("nuevo\n"));
        writer.Flush();
        Assert.Equal("nuevo\n", reader.Poll().Text);
        File.Move(FilePath, Path.Combine(directory, "rotado.log"));
        File.WriteAllText(FilePath, "archivo nuevo\n");
        var update = reader.Poll();
        Assert.True(update.Reset);
        Assert.Equal("archivo nuevo\n", update.Text);
    }

    [Fact]
    public void IdentityDistinguishesReplacementEvenWithSameContentAndTimestamp()
    {
        File.WriteAllText(FilePath, "contenido identico\n");
        var timestamp = File.GetLastWriteTimeUtc(FilePath);
        var reader = new TailReader(FilePath);
        reader.Poll();
        File.Move(FilePath, Path.Combine(directory, "rotado.log"));
        File.WriteAllText(FilePath, "contenido identico\n");
        File.SetLastWriteTimeUtc(FilePath, timestamp);
        var update = reader.Poll();
        Assert.True(update.Reset);
        Assert.Equal("contenido identico\n", update.Text);
    }

    [Theory]
    [InlineData("x\n")]
    [InlineData("contenido reescrito y mas largo que el original\n")]
    public void ReloadsAfterTruncationOrRewrite(string replacement)
    {
        File.WriteAllText(FilePath, "original\n");
        var reader = new TailReader(FilePath);
        reader.Poll();
        File.WriteAllText(FilePath, replacement);
        var update = reader.Poll();
        Assert.True(update.Reset);
        Assert.Equal(replacement, update.Text);
    }

    [Fact]
    public void PreservesSplitUtf8CharacterAndBom()
    {
        File.WriteAllBytes(FilePath, [0xEF]);
        var reader = new TailReader(FilePath);
        Assert.Empty(reader.Poll().Text);
        Append([0xBB, 0xBF, 0xF0, 0x9F]);
        Assert.Empty(reader.Poll().Text);
        Append([0x98, 0x80, 0x0A]);
        Assert.Equal("\U0001F600\n", reader.Poll().Text);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void DetectsUnicodeBomAndPreservesSplitCharacters(string name)
    {
        var encoding = name == "utf-32BE" ? new UTF32Encoding(true, true, true) : Encoding.GetEncoding(name);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("Espa\u00f1ol \U0001F600\n")).ToArray();
        File.WriteAllBytes(FilePath, bytes[..5]);
        var reader = new TailReader(FilePath);
        var first = reader.Poll().Text;
        Append(bytes[5..]);
        Assert.Equal("Espa\u00f1ol \U0001F600\n", first + reader.Poll().Text);
    }

    [Theory]
    [InlineData(new byte[] { 0x41, 0, 0x42 })]
    [InlineData(new byte[] { 0xFF, 0x41, 0x42 })]
    [InlineData(new byte[] { 0x1B, 0x5B, 0x32, 0x4A })]
    public void RejectsBinaryInvalidEncodingAndTerminalControls(byte[] bytes)
    {
        File.WriteAllBytes(FilePath, bytes);
        var reader = new TailReader(FilePath);
        var exception = Record.Exception(() => reader.Poll());
        Assert.True(exception is InvalidDataException or DecoderFallbackException);
    }

    [Fact]
    public void EmptyFileCanLaterReceiveUnicodeText()
    {
        File.WriteAllBytes(FilePath, []);
        var reader = new TailReader(FilePath);
        Assert.Empty(reader.Poll().Text);
        File.WriteAllText(FilePath, "hola\n", Encoding.Unicode);
        Assert.Equal("hola\n", reader.Poll().Text);
    }

    [Fact]
    public void ReopenRereadsAndMissingFileCanRecover()
    {
        File.WriteAllText(FilePath, "hola\n");
        var reader = new TailReader(FilePath);
        reader.Poll();
        reader.Reset();
        Assert.Equal("hola\n", reader.Poll().Text);
        File.Delete(FilePath);
        Assert.Throws<FileNotFoundException>(() => reader.Poll());
        File.WriteAllText(FilePath, "recuperado\n");
        Assert.Equal("recuperado\n", reader.Poll().Text);
    }

    [Fact]
    public void ReadsLargeFileInBoundedChunks()
    {
        var expected = new string('a', 300_000) + "\n";
        File.WriteAllText(FilePath, expected);
        var reader = new TailReader(FilePath);
        var first = reader.Poll();
        Assert.False(first.CaughtUp);
        var second = reader.Poll();
        Assert.True(second.CaughtUp);
        Assert.False(second.Reset);
        Assert.Equal(expected, first.Text + second.Text);
    }

    [Theory]
    [InlineData(FileTextEncoding.Windows1252, 1252)]
    [InlineData(FileTextEncoding.Latin1, 28591)]
    [InlineData(FileTextEncoding.Utf16Le, 1200)]
    [InlineData(FileTextEncoding.Utf16Be, 1201)]
    [InlineData(FileTextEncoding.Utf32Le, 12000)]
    [InlineData(FileTextEncoding.Utf32Be, 12001)]
    public void ManualEncodingReadsLegacyOrBomlessText(FileTextEncoding choice, int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllBytes(FilePath, Encoding.GetEncoding(codePage).GetBytes("Espa\u00f1ol\n"));
        var reader = new TailReader(FilePath);
        reader.ChangeEncoding(choice);
        var update = reader.Poll();
        Assert.True(update.Reset);
        Assert.Equal("Espa\u00f1ol\n", update.Text);
    }

    [Fact]
    public void ChangingEncodingRereadsWholeFile()
    {
        File.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes("Espa\u00f1ol\n"));
        var reader = new TailReader(FilePath);
        Assert.Equal("Espa\u00f1ol\n", reader.Poll().Text);
        reader.ChangeEncoding(FileTextEncoding.Latin1);
        var update = reader.Poll();
        Assert.True(update.Reset);
        Assert.Equal("Espa\u00c3\u00b1ol\n", update.Text);
        reader.ChangeEncoding(FileTextEncoding.Auto);
        Assert.Equal("Espa\u00f1ol\n", reader.Poll().Text);
    }

    private void Append(byte[] bytes)
    {
        using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(bytes);
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(directory))
            File.Delete(file);
        Directory.Delete(directory);
    }
}
