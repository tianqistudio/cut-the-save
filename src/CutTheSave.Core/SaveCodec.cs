using System.Text;

namespace CutTheSave.Core;

public sealed class UnsupportedSaveException(string message) : Exception(message);

public sealed class SaveCodec
{
    public const int DefaultMaximumEntryCount = 5_000;
    public const int MaximumFileBytes = 16 * 1024 * 1024;
    public const int MaximumStringBytes = 256 * 1024;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public CutTheRopeSave Read(Stream stream, int maximumEntryCount = DefaultMaximumEntryCount)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        if (stream.Length - stream.Position > MaximumFileBytes)
            throw new UnsupportedSaveException("The save exceeds the supported 16 MiB size limit.");
        try
        {
            using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
            var save = new CutTheRopeSave();
            var intCount = ReadCount(reader, maximumEntryCount, "integer");
            for (var i = 0; i < intCount; i++)
                save.IntEntries.Add(new SaveIntEntry(ReadString(reader), reader.ReadInt32()));
            var stringCount = ReadCount(reader, maximumEntryCount, "string");
            for (var i = 0; i < stringCount; i++)
                save.StringEntries.Add(new SaveStringEntry(ReadString(reader), ReadString(reader)));
            if (stream.Position != stream.Length)
                throw new UnsupportedSaveException("Unexpected trailing data was found after the save payload.");
            return save;
        }
        catch (Exception exception) when (exception is EndOfStreamException or FormatException or DecoderFallbackException)
        {
            throw new UnsupportedSaveException("The save contains truncated data, an invalid string length or invalid UTF-8. It was not repaired or rewritten.");
        }
    }

    public void Write(Stream stream, CutTheRopeSave save, int maximumEntryCount = DefaultMaximumEntryCount)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(save);
        if (!stream.CanWrite) throw new ArgumentException("The stream must be writable.", nameof(stream));
        if (maximumEntryCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));
        if (save.IntEntries.Count > maximumEntryCount || save.StringEntries.Count > maximumEntryCount)
            throw new UnsupportedSaveException("The save contains too many entries.");
        // Validate every string and the total size before emitting any bytes.
        long size = 8;
        try
        {
            foreach (var entry in save.IntEntries) size += EncodedSize(entry.Key) + 4;
            foreach (var entry in save.StringEntries) size += EncodedSize(entry.Key) + EncodedSize(entry.Value);
        }
        catch (EncoderFallbackException) { throw new UnsupportedSaveException("A string contains an invalid Unicode sequence."); }
        if (size > MaximumFileBytes) throw new UnsupportedSaveException("The save exceeds the supported 16 MiB size limit.");
        using var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true);
        writer.Write(save.IntEntries.Count);
        foreach (var entry in save.IntEntries) { writer.Write(entry.Key); writer.Write(entry.Value); }
        writer.Write(save.StringEntries.Count);
        foreach (var entry in save.StringEntries) { writer.Write(entry.Key); writer.Write(entry.Value); }
        writer.Flush();
    }

    private static int EncodedSize(string value)
    {
        var bytes = StrictUtf8.GetByteCount(value);
        if (bytes > MaximumStringBytes) throw new UnsupportedSaveException("A save string exceeds the supported 256 KiB size limit.");
        return bytes + (bytes < 128 ? 1 : bytes < 16_384 ? 2 : 3);
    }

    private static string ReadString(BinaryReader reader)
    {
        // Read the same 7-bit length format as BinaryReader, with a bound before allocation.
        var start = reader.BaseStream.Position;
        var length = reader.Read7BitEncodedInt();
        var prefixSize = reader.BaseStream.Position - start;
        if (length < 0 || length > MaximumStringBytes || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new UnsupportedSaveException("A save string length is invalid or outside the supported limit.");
        var canonicalSize = length < 128 ? 1 : length < 16_384 ? 2 : 3;
        if (prefixSize != canonicalSize)
            throw new UnsupportedSaveException("A non-canonical string length cannot be rewritten byte-for-byte.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return StrictUtf8.GetString(bytes);
    }

    private static int ReadCount(BinaryReader reader, int maximumEntryCount, string kind)
    {
        if (maximumEntryCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));
        var count = reader.ReadInt32();
        if (count < 0 || count > maximumEntryCount)
            throw new UnsupportedSaveException($"The {kind} entry count {count} is outside the allowed range.");
        return count;
    }
}
