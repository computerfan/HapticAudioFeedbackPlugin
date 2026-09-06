"""Pack with Logitech's tool and preserve macOS helper executable bits in the archive."""
import argparse
from pathlib import Path
import stat
import subprocess
import zipfile

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source",type=Path)
    parser.add_argument("output",type=Path)
    args=parser.parse_args()
    subprocess.run(["logiplugintool", "pack", str(args.source), str(args.output)],check=True)
    temporary=args.output.with_suffix(args.output.suffix+".tmp")
    try:
        with zipfile.ZipFile(args.output) as original, zipfile.ZipFile(temporary,"w") as updated:
            for entry in original.infolist():
                if entry.filename.endswith("/Contents/MacOS/haptic-cpal-helper"):
                    entry.create_system=3
                    entry.external_attr=(stat.S_IFREG|0o755)<<16
                updated.writestr(entry,original.read(entry.filename))
        temporary.replace(args.output)
    finally:
        if temporary.exists(): temporary.unlink()
    verification=subprocess.run(["logiplugintool","verify",str(args.output)],capture_output=True,text=True)
    print(verification.stdout)
    print(verification.stderr,end="")
    if verification.returncode or "ERROR" in (verification.stdout+verification.stderr).upper():
        raise RuntimeError("Plugin package verification failed")

if __name__ == "__main__": main()