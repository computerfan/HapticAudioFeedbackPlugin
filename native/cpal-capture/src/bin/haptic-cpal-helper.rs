//! Permission-owning macOS helper. CPAL remains the capture engine.
//! stdout is binary PCM framing only; stdin EOF terminates capture.
use haptic_cpal::*;
use std::io::{self, Read, Write};
use std::sync::{Arc, atomic::{AtomicBool, Ordering}};
use std::time::{SystemTime, UNIX_EPOCH};

fn run() -> Result<(), String> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args == ["--list-devices"] {
        io::stdout().write_all(&devices::list()?).map_err(|e| e.to_string())?;
        return Ok(());
    }
    let key = match args.as_slice() { [] => "", [flag, key] if flag == "--device" => key.as_str(), _ => return Err("Invalid capture arguments".into()) };
    devices::selection(key)?;
    let stopped = Arc::new(AtomicBool::new(false));
    let stop = stopped.clone();
    std::thread::spawn(move || {
        let mut byte = [0u8];
        let _ = io::stdin().read(&mut byte);
        stop.store(true, Ordering::Relaxed);
    });
    let mut format = Format::default();
    let mut error = [0u8; 2048];
    let handle = unsafe { haptic_cpal_open_device(key.as_ptr(), key.len() as u32, &mut format, error.as_mut_ptr(), error.len() as u32) };
    let message = |error: &[u8]| String::from_utf8_lossy(&error[..error.iter().position(|b| *b == 0).unwrap_or(error.len())]).into_owned();
    if handle.is_null() { return Err(message(&error)); }
    struct Guard(*mut Capture);
    impl Drop for Guard { fn drop(&mut self) { unsafe { haptic_cpal_close(self.0); } } }
    let _guard = Guard(handle);
    let mut output = io::stdout().lock();
    let write_error = |e: io::Error| e.to_string();
    output.write_all(b"HCP1").map_err(write_error)?;
    for value in [format.sample_rate, format.channels, format.capacity, format.requested_ms, format.default_buffer] {
        output.write_all(&value.to_le_bytes()).map_err(write_error)?;
    }
    output.flush().map_err(write_error)?;
    let mut samples = vec![0f32; format.capacity as usize];
    let mut bytes = Vec::with_capacity(samples.len() * 4 + 32);
    while !stopped.load(Ordering::Relaxed) {
        let mut packet = Packet::default();
        let result = unsafe { haptic_cpal_read(handle, samples.as_mut_ptr(), samples.len() as u32, &mut packet, error.as_mut_ptr(), error.len() as u32) };
        if result < 0 { return Err(message(&error)); }
        if result > 0 {
            let now = SystemTime::now().duration_since(UNIX_EPOCH).map_err(|e| e.to_string())?.as_secs_f64() * 1000.0;
            bytes.clear();
            bytes.extend_from_slice(&packet.samples.to_le_bytes());
            bytes.extend_from_slice(&packet.discontinuity.to_le_bytes());
            bytes.extend_from_slice(&packet.dropped_frames.to_le_bytes());
            bytes.extend_from_slice(&(now - packet.newest_age_ms).to_le_bytes());
            for sample in &samples[..packet.samples as usize] { bytes.extend_from_slice(&sample.to_le_bytes()); }
            output.write_all(&bytes).map_err(write_error)?;
            output.flush().map_err(write_error)?;
        }

    }
    Ok(())
}
fn main() {
    if let Err(error) = run() { eprintln!("{error}"); std::process::exit(2); }
}