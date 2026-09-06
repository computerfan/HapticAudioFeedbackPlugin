"""Build/package the pinned CPAL engine. No SDK reload or audio capture."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
CRATE = ROOT / "native" / "cpal-capture"
TARGETS = {"x86_64-pc-windows-msvc": "win-x64", "aarch64-pc-windows-msvc": "win-arm64",
           "x86_64-apple-darwin": "osx-x64", "aarch64-apple-darwin": "osx-arm64"}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", choices=TARGETS, required=True)
    parser.add_argument("--output", type=Path, required=True, help="Destination runtimes directory")
    args = parser.parse_args()
    env = os.environ.copy()
    mac = "apple" in args.target
    if mac:
        env["MACOSX_DEPLOYMENT_TARGET"] = "14.6"
        env["CARGO_ENCODED_RUSTFLAGS"] = "\x1f".join(["-C", "link-arg=-Wl,-sectcreate,__TEXT,__info_plist," + str(CRATE / "Info.plist")])
    subprocess.run(["cargo", "+1.90.0", "build", "--manifest-path", str(CRATE / "Cargo.toml"),
                    "--locked", "--release", "--target", args.target], env=env, check=True)
    source = CRATE / "target" / args.target / "release"
    destination = args.output.resolve() / TARGETS[args.target] / "native"
    destination.mkdir(parents=True, exist_ok=True)
    if mac:
        bundle = destination / "Feel the Rhythm Capture.app"
        executable = bundle / "Contents" / "MacOS" / "haptic-cpal-helper"
        executable.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source / "haptic-cpal-helper", executable)
        shutil.copy2(CRATE / "Info.plist", bundle / "Contents" / "Info.plist")
        executable.chmod(0o755)
        # Development signing only. Release distribution needs a stable Developer ID identity.
        subprocess.run(["codesign", "--force", "--sign", "-", "--timestamp=none", str(bundle)], check=True)
    else:
        shutil.copy2(source / "haptic_cpal.dll", destination / "haptic_cpal.dll")
    print(f"Packaged CPAL for {TARGETS[args.target]} at {destination}")

if __name__ == "__main__":
    main()