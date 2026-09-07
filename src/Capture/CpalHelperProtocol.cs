namespace Loupedeck.HapticAudioFeedback;

using System.Buffers.Binary;

/// <summary>Versioned little-endian helper framing, shared by the Mac adapter and protocol tests.</summary>
public sealed class CpalHelperProtocol
{
    public int SampleRate { get; }
    public int Channels { get; }
    public int Capacity { get; }
    public bool DefaultBuffer { get; }
    private readonly byte[] _bytes;
    private readonly float[] _samples;
    private bool _skipped;
    public static async Task<CpalHelperProtocol> ReadHandshakeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[24];
        await stream.ReadExactlyAsync(header.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
        if (header.AsSpan(0, 4).SequenceEqual("HCE1"u8))
        {
            var lengthBytes = new byte[4];
            await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
            if (length > 4096) throw new IOException("Invalid helper error length.");
            var message = new byte[(int)length];
            await stream.ReadExactlyAsync(message, cancellationToken).ConfigureAwait(false);
            throw CapturePermissionException.FromNativeMessage(System.Text.Encoding.UTF8.GetString(message));
        }
        await stream.ReadExactlyAsync(header.AsMemory(4), cancellationToken).ConfigureAwait(false);
        return new CpalHelperProtocol(header);
    }
    public CpalHelperProtocol(ReadOnlySpan<byte> header)
    {
        if (header.Length != 24 || !header[..4].SequenceEqual("HCP1"u8)) throw new IOException("Invalid CPAL helper protocol.");
        SampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        Channels = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        Capacity = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (SampleRate is < 8000 or > 384000 || Channels is < 1 or > 32 || Capacity <= 0 ||
            Capacity > SampleRate * Channels || Capacity % Channels != 0) throw new IOException("Invalid CPAL helper format.");
        DefaultBuffer = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]) != 0;
        _bytes = new byte[checked(Capacity * 4)];
        _samples = new float[Capacity];
    }
    public AudioCaptureData ReadPacket(Stream stream, Func<double> nowUnixMilliseconds)
    {
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var discontinuity = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != 0;
        var dropped = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
        var newestUnixMs = BinaryPrimitives.ReadDoubleLittleEndian(header[16..]);
        if (count == 0 || count > Capacity || count % Channels != 0 || !double.IsFinite(newestUnixMs))
            throw new IOException("Invalid CPAL helper packet.");
        stream.ReadExactly(_bytes.AsSpan(0, checked((int)count * 4)));
        var age = nowUnixMilliseconds() - newestUnixMs;
        // Reject packets straddling a wall-clock change; never reinterpret stale/future PCM as fresh.
        if (!double.IsFinite(age) || age < 0 || age > 1000) { _skipped = true; return null; }
        for (var i = 0; i < count; i++) _samples[i] = BinaryPrimitives.ReadSingleLittleEndian(_bytes.AsSpan(i * 4));
        var result = new AudioCaptureData(_samples.AsMemory(0, (int)count), age, discontinuity || _skipped, dropped);
        _skipped = false;
        return result;
    }
}