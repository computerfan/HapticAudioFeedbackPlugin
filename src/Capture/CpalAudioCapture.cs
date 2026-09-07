namespace Loupedeck.HapticAudioFeedback;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>Windows CPAL capture is isolated from the SDK host in a supervised process.</summary>
public sealed class CpalAudioCapture : HelperProcessAudioCapture
{
    public static string LibraryRelativePath() => Path.Combine("runtimes", "win-" + ArchitectureName(), "native", "haptic_cpal.dll");
    private static string ArchitectureName()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows capture helper required.");
        return RuntimeInformation.ProcessArchitecture switch {
            Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => throw new PlatformNotSupportedException("Capture requires a 64-bit host.") };
    }
    private static ProcessStartInfo Command(string directory, params string[] arguments)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) throw new ArgumentException("An absolute plugin binary directory is required.");
        var start = new ProcessStartInfo(Path.Combine(directory, "runtimes", "win-" + ArchitectureName(), "native", "haptic-cpal-helper.exe"));
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
    public CpalAudioCapture(string pluginBinaryDirectory, string deviceId = "", CancellationToken cancellation = default)
        : base(Command(pluginBinaryDirectory, "--device", deviceId ?? throw new ArgumentNullException(nameof(deviceId))), TimeSpan.FromSeconds(10), cancellation) { }
    public static AudioCaptureDevice[] ListDevices(string pluginBinaryDirectory)
        => CaptureHelperProcess.ReadDevices(Command(pluginBinaryDirectory, "--list-devices"), TimeSpan.FromSeconds(10));
}
