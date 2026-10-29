using System.Text;

namespace Tail;

public sealed record FileMetadata(string Folder, string Name, DateTime CreationTime, long Length,
    DateTime LastWriteTime);

public sealed record FileUpdate(string Text, bool Reset, FileMetadata Metadata, string EncodingName,
    bool CaughtUp, FileTextEncoding EncodingChoice = FileTextEncoding.Auto);

public sealed class TailReader(string path)
{
    private const int _chunkSize = 256 * 1024;
    private long _offset;
    private FileIdentity? _identity;
    private Decoder? _decoder;
    private string _encodingName = "pending";
    private byte[] _checkpoint = [];
    private DateTime _lastWrite;
    private bool _resetPending = true;
    private FileTextEncoding _encodingChoice;

    public string FilePath { get; } = Path.GetFullPath(path);

    public void ChangeEncoding(FileTextEncoding choice)
    {
        if (!Enum.IsDefined(choice))
            throw new ArgumentOutOfRangeException(nameof(choice));
        _encodingChoice = choice;
        Reset();
    }

    public void Reset()
    {
        _offset = 0;
        _identity = null;
        _decoder = null;
        _checkpoint = [];
        _encodingName = "pending";
        _resetPending = true;
    }

    public FileUpdate Poll()
    {
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        var currentIdentity = FileIdentity.Read(stream);
        var info = new FileInfo(FilePath);
        info.Refresh();
        if (!info.Exists)
            throw new FileNotFoundException("The file disappeared; waiting for it to become available again.", FilePath);
        var metadata = new FileMetadata(info.DirectoryName!, info.Name, info.CreationTime, stream.Length,
            info.LastWriteTime);

        if (_identity != currentIdentity || stream.Length < _offset ||
            (stream.Length == _offset && _lastWrite != metadata.LastWriteTime) || !CheckpointMatches(stream))
            Reset();
        _identity = currentIdentity;
        _lastWrite = metadata.LastWriteTime;

        if (_decoder is null)
        {
            Span<byte> prefix = stackalloc byte[4];
            stream.Position = 0;
            var count = stream.Read(prefix);
            var detected = TextEncoding.Resolve(_encodingChoice, prefix[..count]);
            if (detected is null)
                return new("", _resetPending, metadata, _encodingName, true, _encodingChoice);
            _decoder = detected.Value.Encoding.GetDecoder();
            _encodingName = detected.Value.Encoding.WebName;
            _offset = detected.Value.PreambleLength;
        }

        stream.Position = _offset;
        var bytes = new byte[(int)Math.Min(_chunkSize, Math.Max(0, stream.Length - _offset))];
        var read = stream.Read(bytes);
        var chars = new char[read * 2 + 4];
        // Never flush at temporary EOF: another process can split a Unicode character across writes.
        var characters = _decoder.GetChars(bytes.AsSpan(0, read), chars, flush: false);
        TextEncoding.Validate(chars.AsSpan(0, characters));
        _offset += read;
        var checkpointLength = (int)Math.Min(64, _offset);
        _checkpoint = new byte[checkpointLength];
        stream.Position = _offset - checkpointLength;
        stream.ReadExactly(_checkpoint);
        var reset = _resetPending;
        _resetPending = false;
        return new(new string(chars, 0, characters), reset, metadata, _encodingName, _offset >= stream.Length,
            _encodingChoice);
    }

    private bool CheckpointMatches(FileStream stream)
    {
        if (_checkpoint.Length == 0)
            return true;
        Span<byte> actual = stackalloc byte[_checkpoint.Length];
        stream.Position = _offset - _checkpoint.Length;
        var count = stream.ReadAtLeast(actual, actual.Length, throwOnEndOfStream: false);
        return count == actual.Length && actual.SequenceEqual(_checkpoint);
    }
}
