using System.Text;

namespace Catl;

public sealed record FileMetadata(string Folder, string Name, DateTime CreationTime, long Length,
    DateTime LastWriteTime);

public sealed record FileUpdate(string Text, bool Reset, FileMetadata Metadata, string EncodingName,
    bool CaughtUp, FileTextEncoding EncodingChoice = FileTextEncoding.Auto);

public sealed class TailReader(string path)
{
    private const int ChunkSize = 256 * 1024;
    private long offset;
    private FileIdentity? identity;
    private Decoder? decoder;
    private string encodingName = "pendiente";
    private byte[] checkpoint = [];
    private DateTime lastWrite;
    private bool resetPending = true;
    private FileTextEncoding encodingChoice;

    public string FilePath { get; } = Path.GetFullPath(path);

    public void ChangeEncoding(FileTextEncoding choice)
    {
        if (!Enum.IsDefined(choice))
            throw new ArgumentOutOfRangeException(nameof(choice));
        encodingChoice = choice;
        Reset();
    }

    public void Reset()
    {
        offset = 0;
        identity = null;
        decoder = null;
        checkpoint = [];
        encodingName = "pendiente";
        resetPending = true;
    }

    public FileUpdate Poll()
    {
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        var currentIdentity = FileIdentity.Read(stream);
        var info = new FileInfo(FilePath);
        info.Refresh();
        if (!info.Exists)
            throw new FileNotFoundException("El archivo desaparecio; esperando a que vuelva a existir.", FilePath);
        var metadata = new FileMetadata(info.DirectoryName!, info.Name, info.CreationTime, stream.Length,
            info.LastWriteTime);

        if (identity != currentIdentity || stream.Length < offset ||
            (stream.Length == offset && lastWrite != metadata.LastWriteTime) || !CheckpointMatches(stream))
            Reset();
        identity = currentIdentity;
        lastWrite = metadata.LastWriteTime;

        if (decoder is null)
        {
            Span<byte> prefix = stackalloc byte[4];
            stream.Position = 0;
            var count = stream.Read(prefix);
            var detected = TextEncoding.Resolve(encodingChoice, prefix[..count]);
            if (detected is null)
                return new("", resetPending, metadata, encodingName, true, encodingChoice);
            decoder = detected.Value.Encoding.GetDecoder();
            encodingName = detected.Value.Encoding.WebName;
            offset = detected.Value.PreambleLength;
        }

        stream.Position = offset;
        var bytes = new byte[(int)Math.Min(ChunkSize, Math.Max(0, stream.Length - offset))];
        var read = stream.Read(bytes);
        var chars = new char[read * 2 + 4];
        // Never flush at temporary EOF: another process can split a Unicode character across writes.
        var characters = decoder.GetChars(bytes.AsSpan(0, read), chars, flush: false);
        TextEncoding.Validate(chars.AsSpan(0, characters));
        offset += read;
        var checkpointLength = (int)Math.Min(64, offset);
        checkpoint = new byte[checkpointLength];
        stream.Position = offset - checkpointLength;
        stream.ReadExactly(checkpoint);
        var reset = resetPending;
        resetPending = false;
        return new(new string(chars, 0, characters), reset, metadata, encodingName, offset >= stream.Length,
            encodingChoice);
    }

    private bool CheckpointMatches(FileStream stream)
    {
        if (checkpoint.Length == 0)
            return true;
        Span<byte> actual = stackalloc byte[checkpoint.Length];
        stream.Position = offset - checkpoint.Length;
        var count = stream.ReadAtLeast(actual, actual.Length, throwOnEndOfStream: false);
        return count == actual.Length && actual.SequenceEqual(checkpoint);
    }
}
