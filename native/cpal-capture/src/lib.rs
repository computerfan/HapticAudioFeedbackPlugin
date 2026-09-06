//! Small C ABI over CPAL. Only Windows WASAPI and macOS CoreAudio are supported.
//! Audio stays in memory; the callback never invokes managed code or waits on its consumer.
use cpal::traits::{DeviceTrait, StreamTrait};
use cpal::{BufferSize, FromSample, Sample, SampleFormat, SizedSample, StreamConfig};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};
pub mod devices;

#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct Format {
    pub sample_rate: u32,
    pub channels: u32,
    pub capacity: u32,
    pub requested_ms: u32,
    pub default_buffer: u32,
}
#[repr(C)]
#[derive(Default, Clone, Copy)]
pub struct Packet {
    pub samples: u32,
    pub discontinuity: u32,
    pub dropped_frames: u64,
    pub newest_age_ms: f64,
}

struct Pending {
    samples: Vec<f32>,
    start: usize,
    len: usize,
    channels: usize,
    discontinuity: bool,
    dropped_frames: u64,
    received: Instant,
    newest_age_ms: f64,
}
impl Pending {
    fn new(capacity: usize, channels: usize) -> Self {
        Self { samples: vec![0.0; capacity], start: 0, len: 0, channels,
            discontinuity: false, dropped_frames: 0, received: Instant::now(), newest_age_ms: 0.0 }
    }
    fn push<T: Sample + Copy>(&mut self, data: &[T], age: f64) where f32: FromSample<T> {
        let capacity = self.samples.len();
        let incoming = data.len().min(capacity);
        let overflow = (self.len + incoming).saturating_sub(capacity);
        let skipped = data.len() - incoming;
        if overflow + skipped > 0 {
            self.discontinuity = true;
            self.dropped_frames = self.dropped_frames.saturating_add(((overflow + skipped) / self.channels) as u64);
            self.start = (self.start + overflow) % capacity;
            self.len -= overflow;
        }
        for value in &data[skipped..] {
            self.samples[(self.start + self.len) % capacity] = value.to_sample::<f32>();
            self.len += 1;
        }
        self.received = Instant::now();
        self.newest_age_ms = age;
    }
    fn drain(&mut self, output: &mut [f32]) -> Result<Packet, String> {
        if output.len() < self.len { return Err("Consumer buffer is too small".into()); }
        for (i, sample) in output[..self.len].iter_mut().enumerate() {
            *sample = self.samples[(self.start + i) % self.samples.len()];
        }
        let packet = Packet { samples: self.len as u32, discontinuity: u32::from(self.discontinuity),
            dropped_frames: self.dropped_frames,
            newest_age_ms: self.newest_age_ms + self.received.elapsed().as_secs_f64() * 1000.0 };
        self.len = 0;
        self.start = 0;
        self.discontinuity = false;
        Ok(packet)
    }
}
struct Shared {
    ready: std::sync::Condvar,
    pending: Mutex<Pending>,
    missed_frames: std::sync::atomic::AtomicU64,
    gap: std::sync::atomic::AtomicBool,
    error: Mutex<Option<String>>,
}
pub struct Capture { _stream: cpal::Stream, shared: Arc<Shared> }

fn make_stream<T>(device: &cpal::Device, config: StreamConfig, shared: Arc<Shared>) -> Result<cpal::Stream, cpal::Error>
where T: SizedSample + Copy, f32: FromSample<T> {
    let errors = shared.clone();
    let channels = config.channels as usize;
    let rate = config.sample_rate as f64;
    device.build_input_stream::<T, _, _>(config, move |data, info| {
        use std::sync::atomic::Ordering;
        // Contention drops a packet, never blocks the OS audio thread.
        if let Ok(mut pending) = shared.pending.try_lock() {
            let missed = shared.missed_frames.swap(0, Ordering::Relaxed);
            let gap = shared.gap.swap(false, Ordering::Relaxed);
            pending.dropped_frames = pending.dropped_frames.saturating_add(missed);
            if missed > 0 || gap { pending.dropped_frames = pending.dropped_frames.saturating_add((pending.len / channels) as u64); pending.len = 0; pending.start = 0; }
            pending.discontinuity |= missed > 0 || gap;
            if data.len() % channels != 0 { return; }
            let stamp = info.timestamp();
            let oldest_age = stamp.callback.duration_since(stamp.capture).as_secs_f64() * 1000.0;
            let newest_age = (oldest_age - data.len() as f64 / channels as f64 / rate * 1000.0).max(0.0);
            pending.push(data, newest_age);
            drop(pending);
            shared.ready.notify_one();
        } else {
            let _ = shared.missed_frames.fetch_update(Ordering::Relaxed, Ordering::Relaxed, |value| Some(value.saturating_add((data.len() / channels) as u64)));
        }
    }, move |error| {
        match error.kind() {
            // WASAPI reports an initial discontinuity and recoverable packet gaps as Xrun.
            cpal::ErrorKind::Xrun | cpal::ErrorKind::DeviceChanged => { errors.gap.store(true, std::sync::atomic::Ordering::Relaxed); }
            cpal::ErrorKind::RealtimeDenied => { }
            _ => { if let Ok(mut slot) = errors.error.lock() { *slot = Some(error.to_string()); } }
        }
    }, Some(Duration::from_secs(3)))
}
fn open(key: &str) -> Result<(Capture, Format), String> {
    if !cfg!(any(target_os = "windows", target_os = "macos")) {
        return Err("Audio capture supports Windows and macOS only".into());
    }
    let (device, input) = devices::resolve(key)?;
    let supported = if input { device.default_input_config() } else { device.default_output_config() }.map_err(|e| e.to_string())?;
    let mut config = supported.config();
    if !(8000..=384000).contains(&config.sample_rate) || !(1..=32).contains(&config.channels) {
        return Err("Unsupported audio sample rate or channel count".into());
    }
    // At most 40 ms of pending PCM, dropping oldest whole frames when a consumer stalls.
    let capacity = (config.sample_rate as usize * 40 / 1000).max(1) * config.channels as usize;
    let shared = Arc::new(Shared { ready: std::sync::Condvar::new(), pending: Mutex::new(Pending::new(capacity, config.channels as usize)),
        missed_frames: std::sync::atomic::AtomicU64::new(0), gap: std::sync::atomic::AtomicBool::new(false), error: Mutex::new(None) });
    let requested = config.sample_rate * 20 / 1000;
    let requested = match supported.buffer_size() {
        cpal::SupportedBufferSize::Range { min, max } => requested.clamp(*min, *max),
        _ => requested,
    };
    config.buffer_size = BufferSize::Fixed(requested);
    let build = |config| match supported.sample_format() {
        SampleFormat::F32 => make_stream::<f32>(&device, config, shared.clone()),
        SampleFormat::F64 => make_stream::<f64>(&device, config, shared.clone()),
        SampleFormat::I16 => make_stream::<i16>(&device, config, shared.clone()),
        SampleFormat::I32 => make_stream::<i32>(&device, config, shared.clone()),
        SampleFormat::U16 => make_stream::<u16>(&device, config, shared.clone()),
        SampleFormat::U32 => make_stream::<u32>(&device, config, shared.clone()),
        _ => Err(cpal::Error::with_message(cpal::ErrorKind::UnsupportedConfig, "Unsupported PCM sample format")),
    };
    let (stream, default_buffer) = match build(config) {
        Ok(stream) => (stream, 0),
        Err(error) if error.kind() == cpal::ErrorKind::UnsupportedConfig => {
            config.buffer_size = BufferSize::Default;
            (build(config).map_err(|e| e.to_string())?, 1)
        }
        Err(error) => return Err(error.to_string()),
    };
    stream.play().map_err(|e| e.to_string())?;
    let format = Format { sample_rate: config.sample_rate, channels: config.channels as u32,
        capacity: capacity as u32, requested_ms: 20, default_buffer };
    Ok((Capture { _stream: stream, shared }, format))
}

unsafe fn error_text(destination: *mut u8, capacity: u32, error: &str) {
    if destination.is_null() || capacity == 0 { return; }
    let count = error.len().min(capacity as usize - 1);
    std::ptr::copy_nonoverlapping(error.as_ptr(), destination, count);
    *destination.add(count) = 0;
}
#[no_mangle]
pub extern "C" fn haptic_cpal_abi_version() -> u32 { 2 }

/// Caller supplies writable Format and error storage; returned handle must be closed exactly once.
#[no_mangle]
pub unsafe extern "C" fn haptic_cpal_open(format: *mut Format, error: *mut u8, error_capacity: u32) -> *mut Capture {
    haptic_cpal_open_device(std::ptr::null(), 0, format, error, error_capacity)
}
#[no_mangle]
pub unsafe extern "C" fn haptic_cpal_open_device(key: *const u8, key_length: u32, format: *mut Format, error: *mut u8, error_capacity: u32) -> *mut Capture {
    if format.is_null() { error_text(error, error_capacity, "Missing format storage"); return std::ptr::null_mut(); }
    match catch_unwind(AssertUnwindSafe(|| {
        if key_length > 4096 || key.is_null() && key_length != 0 { return Err("Invalid audio device ID".into()); }
        let key = if key_length == 0 { "" } else { std::str::from_utf8(std::slice::from_raw_parts(key, key_length as usize)).map_err(|_| "Invalid device ID encoding")? };
        open(key)
    })) {
        Ok(Ok((capture, actual))) => { *format = actual; Box::into_raw(Box::new(capture)) }
        result => {
            let message = match result { Ok(Err(error)) => error, _ => "CPAL initialization panicked".into() };
            error_text(error, error_capacity, &message);
            std::ptr::null_mut()
        }
    }
}
/// Writes HCD1 device catalog. A fixed bounded caller buffer avoids native ownership transfer.
#[no_mangle]
pub unsafe extern "C" fn haptic_cpal_devices(output: *mut u8, capacity: u32, error: *mut u8, error_capacity: u32) -> i32 {
    match catch_unwind(AssertUnwindSafe(devices::list)) {
        Ok(Ok(bytes)) if !output.is_null() && bytes.len() <= capacity as usize => {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), output, bytes.len()); bytes.len() as i32
        }
        result => {
            let message = match result { Ok(Err(e)) => e, Ok(Ok(_)) => "Device catalog buffer too small".into(), _ => "Device enumeration panicked".into() };
            error_text(error, error_capacity, &message); -1
        }
    }
}
/// One consumer only. Do not race read with close. Output has capacity floats, not bytes.
#[no_mangle]
pub unsafe extern "C" fn haptic_cpal_read(handle: *mut Capture, output: *mut f32, capacity: u32,
    packet: *mut Packet, error: *mut u8, error_capacity: u32) -> i32 {
    let result = catch_unwind(AssertUnwindSafe(|| -> Result<i32, String> {
        let capture = handle.as_ref().ok_or("Missing capture handle")?;
        if output.is_null() || packet.is_null() || capacity == 0 { return Err("Missing read storage".into()); }
        if let Some(error) = capture.shared.error.lock().map_err(|_| "Capture error lock poisoned")?.as_ref() {
            return Err(error.clone());
        }
        let mut pending = capture.shared.pending.lock().map_err(|_| "Capture buffer lock poisoned")?;
        if pending.len == 0 {
            pending = capture.shared.ready.wait_timeout(pending, Duration::from_millis(20)).map_err(|_| "Capture buffer lock poisoned")?.0;
        }
        *packet = pending.drain(std::slice::from_raw_parts_mut(output, capacity as usize))?;
        Ok(i32::from((*packet).samples > 0))
    }));
    match result {
        Ok(Ok(status)) => status,
        result => { let message = match result { Ok(Err(error)) => error, _ => "CPAL read panicked".into() };
            error_text(error, error_capacity, &message); -1 }
    }
}
#[no_mangle]
pub unsafe extern "C" fn haptic_cpal_close(handle: *mut Capture) {
    if !handle.is_null() { let _ = catch_unwind(AssertUnwindSafe(|| drop(Box::from_raw(handle)))); }
}

#[cfg(test)]
mod tests {
    #[test]
    fn dropped_counter_saturates_without_affecting_pcm() {
        let mut pending = super::Pending::new(4, 2);
        pending.dropped_frames = u64::MAX - 1;
        pending.push(&[1.0f32, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0], 0.0);
        pending.push(&[9.0f32, 10.0], 0.0);
        let mut pcm = [0.0; 4];
        let packet = pending.drain(&mut pcm).unwrap();
        assert_eq!(packet.dropped_frames, u64::MAX);
        assert_eq!(pcm, [7.0, 8.0, 9.0, 10.0]);
    }

    use super::*;
    #[test] fn combines_callbacks_and_drains_once() {
        let mut pending = Pending::new(8, 2);
        pending.push(&[1f32, 2., 3., 4.], 0.0);
        pending.push(&[5f32, 6.], 0.0);
        let mut output = [0f32; 8];
        assert_eq!(pending.drain(&mut output).unwrap().samples, 6);
        assert_eq!(&output[..6], &[1., 2., 3., 4., 5., 6.]);
        assert_eq!(pending.drain(&mut output).unwrap().samples, 0);
    }
    #[test] fn stalled_consumer_gets_newest_whole_frames() {
        let mut pending = Pending::new(4, 2);
        pending.push(&[1f32, 2., 3., 4.], 0.0);
        pending.push(&[5f32, 6.], 0.0);
        let mut output = [0f32; 4];
        let packet = pending.drain(&mut output).unwrap();
        assert_eq!(output, [3., 4., 5., 6.]);
        assert_eq!(packet.dropped_frames, 1);
        assert_eq!(packet.discontinuity, 1);
        assert_eq!(pending.drain(&mut output).unwrap().discontinuity, 0);
    }
    #[test] fn oversized_packet_keeps_tail_and_converts_pcm() {
        let mut pending = Pending::new(4, 2);
        pending.push(&[0i16, 0, -32768, 32767, 16384, -16384], 5.0);
        let mut output = [0f32; 4];
        let packet = pending.drain(&mut output).unwrap();
        assert_eq!(output[0], -1.0);
        assert_eq!(output[2], 0.5);
        assert_eq!(packet.dropped_frames, 1);
        assert!(packet.newest_age_ms >= 5.0);
    }
    #[test] fn short_destination_preserves_pending_data() {
        let mut pending = Pending::new(4, 2);
        pending.push(&[1f32, 2., 3., 4.], 0.0);
        assert!(pending.drain(&mut [0f32; 2]).is_err());
        assert_eq!(pending.len, 4);
    }
    #[test] fn abi_rejects_null_arguments_without_unwinding() {
        unsafe {
            let mut error = [0u8; 128];
            assert!(haptic_cpal_open(std::ptr::null_mut(), error.as_mut_ptr(), 128).is_null());
            assert_ne!(error[0], 0);
            haptic_cpal_close(std::ptr::null_mut());
        }
        assert_eq!(std::mem::size_of::<Format>(), 20);
        assert_eq!(std::mem::size_of::<Packet>(), 24);
    }
}