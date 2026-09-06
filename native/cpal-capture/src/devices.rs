//! Stable, direction-qualified CPAL device choices. Enumeration never opens a recording stream.
use cpal::traits::{DeviceTrait, HostTrait};

pub fn selection(key: &str) -> Result<(bool, &str), String> {
    if key.is_empty() { return Ok((false, "default")); }
    if key.len() > 4096 || key.chars().any(char::is_control) { return Err("Invalid audio device ID".into()); }
    let (kind, id) = key.split_once(':').ok_or("Invalid audio device ID")?;
    if id.is_empty() { return Err("Missing audio device ID".into()); }
    match kind { "input" => Ok((true, id)), "output" => Ok((false, id)), _ => Err("Unknown audio device direction".into()) }
}

pub fn resolve(key: &str) -> Result<(cpal::Device, bool), String> {
    let (input, id) = selection(key)?;
    let host = cpal::default_host();
    let device = if id == "default" {
        if input { host.default_input_device() } else { host.default_output_device() }
    } else {
        host.device_by_id(&id.parse().map_err(|e| format!("Invalid audio device ID: {e}"))?)
    }.ok_or("Selected audio device is unavailable. Reconnect it or choose another device.")?;
    if input && !device.supports_input() || !input && !device.supports_output() {
        return Err("Selected device does not support this audio direction".into());
    }
    // CoreAudio's CPAL input path uses the real input on duplex devices, not an output tap.
    if cfg!(target_os = "macos") && !input && device.supports_input() {
        return Err("CPAL cannot loop back this combined input/output device. Select an output-only device, or explicitly select its input.".into());
    }
    Ok((device, input))
}

pub fn list() -> Result<Vec<u8>, String> {
    let host = cpal::default_host();
    let mut entries = Vec::new();
    for device in host.devices().map_err(|e| e.to_string())? {
        let Ok(id) = device.id() else { continue; };
        let name = device.description().map(|d| d.name().to_string()).unwrap_or_else(|_| device.to_string());
        for (kind, available) in [("output", device.supports_output() && !(cfg!(target_os = "macos") && device.supports_input())), ("input", device.supports_input())] {
            if available {
                let key = format!("{kind}:{id}");
                if key.len() > 4096 || name.len() > 4096 { continue; }
                if entries.len() >= 256 { return Err("Too many audio devices".into()); }
                entries.push((key, name.clone()));
            }
        }
    }
    let mut bytes = b"HCD1".to_vec();
    bytes.extend_from_slice(&(entries.len() as u32).to_le_bytes());
    for (key, name) in entries {
        for text in [key, name] {
            bytes.extend_from_slice(&(text.len() as u32).to_le_bytes());
            bytes.extend_from_slice(text.as_bytes());
        }
    }
    Ok(bytes)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn direction_is_explicit_and_default_remains_output() {
        assert_eq!(selection("").unwrap(), (false, "default"));
        assert_eq!(selection("input:CoreAudio:stable-id").unwrap(), (true, "CoreAudio:stable-id"));
        assert_eq!(selection("output:WASAPI:id:with:colons").unwrap(), (false, "WASAPI:id:with:colons"));
        for key in ["input:", "microphone:id", "input:id\0hidden", "arbitrary"] { assert!(selection(key).is_err()); }
    }
}
