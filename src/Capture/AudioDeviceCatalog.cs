namespace Loupedeck.HapticAudioFeedback;

using System.Text;

public sealed record AudioCaptureDevice(string Id, string Name)
{
    public string Kind => Id.StartsWith("input:", StringComparison.Ordinal) ? "input" : "output";
}

public static class AudioDeviceCatalog
{
    public const int MaximumBytes = 3 * 1024 * 1024;
    public static AudioCaptureDevice[] Decode(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new IOException("Audio device catalog too large.");
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("HCD1"u8)) throw new IOException("Incompatible audio device catalog.");
        var count = reader.ReadUInt32();
        if (count > 256) throw new IOException("Too many audio devices.");
        var devices = new List<AudioCaptureDevice>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string Text()
        {
            var length = reader.ReadUInt32();
            if (length > 4096 || length > stream.Length - stream.Position) throw new IOException("Invalid device text length.");
            return new UTF8Encoding(false, true).GetString(reader.ReadBytes((int)length));
        }
        for (var i = 0; i < count; i++)
        {
            var id = Text(); var name = Text();
            if (!(id.StartsWith("input:", StringComparison.Ordinal) || id.StartsWith("output:", StringComparison.Ordinal)) ||
                id[(id.IndexOf(':') + 1)..].Length == 0 || id.Any(char.IsControl) || !ids.Add(id))
                throw new IOException("Invalid or duplicate audio device ID.");
            devices.Add(new(id, name));
        }
        if (stream.Position != stream.Length) throw new IOException("Unexpected device catalog data.");
        return devices.ToArray();
    }
}
