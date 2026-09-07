namespace Loupedeck.HapticAudioFeedback;

/// <summary>An explicit CPAL authorization failure, not an inference from silent samples.</summary>
public sealed class CapturePermissionException : IOException
{
    private const string Prefix = "HAPTIC_PERMISSION_DENIED:";
    private CapturePermissionException(string message) : base(message) { }
    public static IOException FromNativeMessage(string message) => message.StartsWith(Prefix, StringComparison.Ordinal)
        ? new CapturePermissionException(message[Prefix.Length..]) : new IOException(message);
    public static bool IsDenied(Exception error)
    {
        for (var current = error; current != null; current = current.InnerException)
            if (current is CapturePermissionException) return true;
        return false;
    }
}
