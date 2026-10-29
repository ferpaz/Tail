using System.Text;

namespace Tail;

internal static class TextEncoding
{
    public static (Encoding Encoding, int PreambleLength)? Resolve(FileTextEncoding choice, ReadOnlySpan<byte> prefix)
    {
        if (choice == FileTextEncoding.Auto)
            return Detect(prefix);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = choice switch
        {
            FileTextEncoding.Utf8 => new UTF8Encoding(true, true),
            FileTextEncoding.Utf16Le => new UnicodeEncoding(false, true, true),
            FileTextEncoding.Utf16Be => new UnicodeEncoding(true, true, true),
            FileTextEncoding.Utf32Le => new UTF32Encoding(false, true, true),
            FileTextEncoding.Utf32Be => new UTF32Encoding(true, true, true),
            FileTextEncoding.Windows1252 => Encoding.GetEncoding(1252,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            FileTextEncoding.Latin1 => Encoding.Latin1,
            _ => throw new ArgumentOutOfRangeException(nameof(choice))
        };
        var preamble = encoding.GetPreamble();
        if (preamble.Length != 0 && prefix.Length < preamble.Length && preamble.AsSpan().StartsWith(prefix))
            return null;
        return (encoding, preamble.Length != 0 && prefix.StartsWith(preamble) ? preamble.Length : 0);
    }

    private static (Encoding Encoding, int PreambleLength)? Detect(ReadOnlySpan<byte> prefix)
    {
        // Wait for a split BOM rather than interpreting its bytes as UTF-8.
        if (prefix.IsEmpty ||
            (prefix.Length < 3 && new byte[] { 0xEF, 0xBB, 0xBF }.AsSpan().StartsWith(prefix)) ||
            (prefix.Length < 4 && new byte[] { 0, 0, 0xFE, 0xFF }.AsSpan().StartsWith(prefix)) ||
            (prefix.Length < 2 && prefix[0] is 0xFF or 0xFE))
            return null;

        if (prefix.StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }))
            return (new UTF32Encoding(false, false, true), 4);
        if (prefix.StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
            return (new UTF32Encoding(true, false, true), 4);
        if (prefix.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return (new UTF8Encoding(false, true), 3);
        if (prefix.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            if (prefix.Length < 4)
                return null;
            return (new UnicodeEncoding(false, false, true), 2);
        }
        if (prefix.StartsWith(new byte[] { 0xFE, 0xFF }))
            return (new UnicodeEncoding(true, false, true), 2);
        return (new UTF8Encoding(false, true), 0);
    }

    public static void Validate(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
                throw new InvalidDataException(
                    $"The file contains binary data or unsupported control characters (U+{(int)character:X4}).");
        }
    }
}
