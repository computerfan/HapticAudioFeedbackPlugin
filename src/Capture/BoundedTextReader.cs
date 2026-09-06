namespace Loupedeck.HapticAudioFeedback;

/// <summary>Drain a helper's stderr continuously, retaining only a bounded prefix.</summary>
public static class BoundedTextReader
{
    public static async Task<string> DrainAsync(TextReader reader, int limit, CancellationToken cancellation = default)
    {
        if (limit < 0 || limit > 65536) throw new ArgumentOutOfRangeException(nameof(limit));
        var result = new System.Text.StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0) {
            var keep = Math.Min(count, limit - result.Length);
            if (keep > 0) result.Append(buffer, 0, keep);
        }
        return result.ToString();
    }
}
