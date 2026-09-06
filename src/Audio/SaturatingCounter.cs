namespace Loupedeck.HapticAudioFeedback;

internal static class SaturatingCounter
{
    public static long Add(long value, long amount) => value >= long.MaxValue - amount ? long.MaxValue : value + amount;
}
