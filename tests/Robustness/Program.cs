using Loupedeck.HapticAudioFeedback;
var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var signal = new CaptureSignalDiagnostics();
var now = DateTime.UtcNow;
signal.Observe(ReadOnlySpan<float>.Empty, now);
Check(signal.Packets == 0 && signal.LastPacketUtc == null, "empty callback is not received audio");
signal.Observe(new float[] { 0, 0, float.NaN, float.PositiveInfinity }, now);
Check(signal.Packets == 1 && signal.Samples == 4 && signal.PeakDb == -180 && signal.LastSignalUtc == null, "silent and invalid samples do not claim a signal");
signal.Observe(new float[] { -.5f, .25f }, now.AddSeconds(1));
Check(Math.Abs(signal.PeakDb + 6.0206) < .001 && signal.LastSignalUtc == now.AddSeconds(1), "raw peak detects signal independently of onset thresholds");
signal.Observe(new float[] { 0, 0 }, now.AddSeconds(2));
Check(signal.LastPacketUtc == now.AddSeconds(2) && signal.LastSignalUtc == now.AddSeconds(1) && signal.Samples == 8, "silence advances packets but preserves last signal time");
var root = Path.Combine(Path.GetTempPath(), "haptic-log-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try {
    var repeat = Path.Combine(root, "repeat");
    using (var log = new BoundedPluginLogger(repeat, () => 0)) {
        for (var i=0;i<10000;i++) log.Write("Warning", "The same capture error", new IOException("Device disconnected"));
        Check(log.SuppressedCount==9999,"repeated failures produce one log entry per window");
    }
    Check(File.ReadAllLines(Path.Combine(repeat,"feel-the-rhythm.log")).Length==1,"suppressed errors do not produce per-event disk writes");
    var flood = Path.Combine(root,"flood");
    using(var log=new BoundedPluginLogger(flood,()=>0)) {
        for(var i=0;i<10000;i++) log.Write("Error","Varying failure "+i);
        Check(log.SuppressedCount==9970,"global budget bounds distinct failure storms and dedup storage");
    }
    Check(File.ReadAllLines(Path.Combine(flood,"feel-the-rhythm.log")).Length==30,"global log budget permits at most 30 entries per minute");
    var rotation=Path.Combine(root,"rotation");Directory.CreateDirectory(rotation);
    foreach(var name in new[]{"feel-the-rhythm.log","feel-the-rhythm.1.log","feel-the-rhythm.2.log"}) File.WriteAllBytes(Path.Combine(rotation,name),new byte[BoundedPluginLogger.FileLimitBytes]);
    File.WriteAllText(Path.Combine(rotation,"unrelated.log"),"retain this");
    var token=new string('A',64);
    using(var log=new BoundedPluginLogger(rotation)) log.Write("Error","token="+token+"\n"+new string('x',10000));
    var files=Directory.GetFiles(rotation,"feel-the-rhythm*.log");
    Check(files.Length==3 && files.All(f=>new FileInfo(f).Length<=BoundedPluginLogger.FileLimitBytes),"rotation caps exactly three files at 512 KiB each");
    var line=File.ReadAllText(Path.Combine(rotation,"feel-the-rhythm.log"));
    Check(!line.Contains(token)&&line.Contains("[redacted]")&&line.Length<2300&&File.ReadAllLines(Path.Combine(rotation,"feel-the-rhythm.log")).Length==1,"large multiline entries are bounded and session tokens are redacted");
    Check(File.ReadAllText(Path.Combine(rotation,"unrelated.log"))=="retain this","rotation leaves other logs untouched");
    var snapshotDirectory = Path.Combine(root, "snapshots"); Directory.CreateDirectory(snapshotDirectory);
    File.WriteAllText(Path.Combine(snapshotDirectory, "feel-the-rhythm.2.log"), "oldest entry\n");
    File.WriteAllText(Path.Combine(snapshotDirectory, "feel-the-rhythm.1.log"), "middle entry\n");
    var oversized = string.Concat(Enumerable.Repeat("Unicode 日志 entry\n", 60000)) + "newest token=" + token + "\n";
    File.WriteAllText(Path.Combine(snapshotDirectory, "feel-the-rhythm.log"), oversized);
    File.WriteAllText(Path.Combine(snapshotDirectory, "unrelated.log"), "never expose this");
    using (var log = new BoundedPluginLogger(snapshotDirectory)) {
        var snapshot = log.ReadSnapshot();
        Check(snapshot.Text.IndexOf("oldest entry") < snapshot.Text.IndexOf("middle entry") && snapshot.Text.Contains("newest token="), "support snapshot orders retained logs oldest to newest");
        Check(!snapshot.Text.Contains(token) && snapshot.Text.Contains("[redacted]") && !snapshot.Text.Contains("never expose"), "support snapshot redacts legacy tokens and excludes unrelated files");
        Check(snapshot.RecentText.Length <= 32768 && snapshot.Text.Length < 3 * BoundedPluginLogger.FileLimitBytes + 200 && snapshot.Warnings.Length == 1, "support snapshot bounds oversized files and the preview");
        Check(File.ReadAllText(Path.Combine(snapshotDirectory, "feel-the-rhythm.log")) == oversized, "reading support logs does not rotate or modify files");
    }
    using (var log = new BoundedPluginLogger(Path.Combine(root, "empty-snapshot")))
        Check(log.ReadSnapshot().Text == "", "empty log directory yields an empty preview");
    var blocked=Path.Combine(root,"not-a-directory");File.WriteAllText(blocked,"block");
    using(var log=new BoundedPluginLogger(blocked)) {
        log.Write("Info","Write will fail");
        Check(SpinWait.SpinUntil(()=>log.LastError!=null,2000),"disk failures are surfaced without throwing into callers");
        log.Write("Error","A later failure does not recurse into logging");
    }
    var time = 0.0;
    var recovery = Path.Combine(root, "recovery"); File.WriteAllText(recovery, "blocked");
    using (var log = new BoundedPluginLogger(recovery, () => Volatile.Read(ref time))) {
        log.Write("Error", "Recoverable failure");
        Check(SpinWait.SpinUntil(() => log.SuppressedCount == 1 && log.LastError != null, 2000), "failed writes count as suppressed");
        File.Delete(recovery); Volatile.Write(ref time, 61);
        log.Write("Error", "Recoverable failure");
        Check(SpinWait.SpinUntil(() => log.LastError == null && File.Exists(Path.Combine(recovery, "feel-the-rhythm.log")), 2000), "logging recovers after its backoff and rate window reset");
    }
    Check(File.ReadAllText(Path.Combine(recovery, "feel-the-rhythm.log")).Contains("suppressed 1 log messages"), "recovered logging summarizes suppressed messages");
    Check(!BoundedPluginLogger.SafeText(new string('x', 2040) + new string('A', 64)).Contains("AAAAAAAA"), "tokens crossing the truncation boundary are redacted");
    Check(SaturatingCounter.Add(long.MaxValue-1,2)==long.MaxValue && SaturatingCounter.Add(long.MaxValue,1)==long.MaxValue,"counters saturate without wrapping negative");
    Console.WriteLine($"{checks} robustness checks passed.");
} finally { Directory.Delete(root,true); }
