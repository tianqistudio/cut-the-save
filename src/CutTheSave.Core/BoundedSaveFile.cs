namespace CutTheSave.Core;

public static class BoundedSaveFile
{
    public static byte[] Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > SaveCodec.MaximumFileBytes)
            throw new IOException("The save or recovery file exceeds the 16 MiB limit; its contents were retained.");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new IOException("The save changed while being read.");
        return bytes;
    }
}
