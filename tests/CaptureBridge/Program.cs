using Loupedeck.HapticAudioFeedback;
using System.Buffers.Binary;
var checks = 0;
void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
byte[] Header() {
    var data = new byte[24]; "HCP1"u8.CopyTo(data);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4),48000);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8),2);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12),1920);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16),20);
    return data;
}
MemoryStream Packet(double time, uint count = 4) {
    var stream = new MemoryStream(); var writer = new BinaryWriter(stream);
    writer.Write(count); writer.Write(0u); writer.Write(12ul); writer.Write(time);
    foreach(var sample in new[]{.25f,-.5f,1f,-1f}) writer.Write(sample);
    stream.Position=0; return stream;
}
using (var handshake = new MemoryStream(Header()))
    Check((await CpalHelperProtocol.ReadHandshakeAsync(handshake, default)).SampleRate == 48000, "async socket handshake accepts PCM format");
foreach (var length in new uint[] { 3, 4097, uint.MaxValue }) {
    using var handshake = new MemoryStream();
    handshake.Write("HCE1"u8);
    var lengthBytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, length);
    handshake.Write(lengthBytes); handshake.Write("bad"u8); handshake.Position = 0;
    try { await CpalHelperProtocol.ReadHandshakeAsync(handshake, default); throw new Exception("Error handshake accepted"); }
    catch (IOException ex) { Check(ex.Message == (length == 3 ? "bad" : "Invalid helper error length."), "bounded helper startup errors preserve details: " + length); }
}
var protocol = new CpalHelperProtocol(Header());
using(var stream=Packet(995)) {
    var data=protocol.ReadPacket(stream,()=>1000);
    Check(data.Samples.Span.SequenceEqual(new[]{.25f,-.5f,1f,-1f}) && data.NewestSampleAgeMs==5 && data.DroppedFrames==12, "helper PCM framing retains sample order, age and dropped frames");
}
using(var stream=Packet(1100)) Check(protocol.ReadPacket(stream,()=>1000)==null,"future helper timestamps are discarded");
using(var stream=Packet(-1001)) Check(protocol.ReadPacket(stream,()=>1000)==null,"stale pipe backlog is discarded");
using(var stream=Packet(999)) Check(protocol.ReadPacket(stream,()=>1000).Discontinuity,"capture resumes with a discontinuity after discarded packets");
foreach(var count in new uint[]{0,3,1922,uint.MaxValue}) {
    using var stream=Packet(995,count);
    try { protocol.ReadPacket(stream,()=>1000); throw new Exception("Invalid packet accepted"); }
    catch(IOException) { Check(true,"reject invalid helper frame length "+count); }
}
using(var stream=Packet(995)) {
    stream.SetLength(27);
    try {protocol.ReadPacket(stream,()=>1000);throw new Exception("Truncation accepted");}
    catch(EndOfStreamException){Check(true,"truncated helper PCM fails without emitting samples");}
}
var invalid=Header(); invalid[0]=0;
try {new CpalHelperProtocol(invalid);throw new Exception("Wrong version accepted");}
catch(IOException){Check(true,"helper handshake rejects incompatible versions");}
byte[] Catalog(params (string Id, string Name)[] devices) {
    using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
    writer.Write("HCD1"u8); writer.Write((uint)devices.Length);
    foreach(var device in devices) foreach(var text in new[]{device.Id,device.Name}) {
        var bytes=System.Text.Encoding.UTF8.GetBytes(text); writer.Write((uint)bytes.Length); writer.Write(bytes);
    }
    return stream.ToArray();
}
var catalogBytes=Catalog(("output:WASAPI:one", "Same name"), ("input:CoreAudio:two", "麦克风 🎵"), ("output:WASAPI:three", "Same name"));
var devices=AudioDeviceCatalog.Decode(catalogBytes);
Check(devices.Length==3 && devices[1].Name=="麦克风 🎵" && devices[1].Kind=="input" && devices[0].Id!=devices[2].Id, "device catalog preserves stable IDs, duplicate names and Unicode");
foreach (var bad in new[]{Catalog(("wrong:one","bad")), Catalog(("input:","bad")), Catalog(("input:one","a"),("input:one","b")), catalogBytes[..^1], catalogBytes.Concat(new byte[]{0}).ToArray()}) {
    try {AudioDeviceCatalog.Decode(bad);throw new Exception("Bad catalog accepted");}
    catch(IOException){Check(true,"malformed device catalog is rejected");}
}
var oversized=Catalog();BinaryPrimitives.WriteUInt32LittleEndian(oversized.AsSpan(4),257);
try{AudioDeviceCatalog.Decode(oversized);throw new Exception("Oversized catalog accepted");}
catch(IOException){Check(true,"device count is bounded");}
using (var stderr = new StringReader(new string('x', 1000000))) {
    var retained = await BoundedTextReader.DrainAsync(stderr, 4096);
    Check(retained.Length == 4096 && stderr.Peek() == -1, "helper stderr is bounded while excess output is drained");
}
using (var cancelled = new CancellationTokenSource()) {
    cancelled.Cancel();
    try { await BoundedTextReader.DrainAsync(new StringReader("error"), 4096, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(true, "helper stderr drain respects cancellation"); }
}
if (args.Length == 2 && args[0] == "--device-smoke") {
    var directory=Path.GetFullPath(args[1]);
    var live=CpalAudioCapture.ListDevices(directory);
    Check(live.Length>0,"CPAL enumerates live devices through native ABI");
    foreach(var item in live) Console.WriteLine($"{item.Kind}: {item.Name}");
    var playback=live.First(d=>d.Kind=="output");
    using(var capture=new CpalAudioCapture(directory,playback.Id)) {
        capture.StartRecording();
        Check(capture.SampleRate>=8000 && capture.Channels>0,"explicit playback device opens through managed adapter");
    }
    try {using var missing=new CpalAudioCapture(directory,"output:WASAPI:feel-the-rhythm-nonexistent");throw new Exception("Missing device accepted");}
    catch(IOException){Check(true,"missing explicit device fails instead of capturing the default");}
    using(var restored=new CpalAudioCapture(directory)) {restored.StartRecording();Check(restored.Channels>0,"default capture opens after selected-device shutdown");}
}
Console.WriteLine($"{checks} capture bridge checks passed; no haptic events sent (live audio only with --device-smoke).");