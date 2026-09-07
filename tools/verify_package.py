"""Check native payloads after SDK verification; never load or execute packaged code."""
import argparse
import hashlib
import json
import plistlib
from pathlib import Path
import re
import stat
import struct
import zipfile
import xml.etree.ElementTree as ET

MAC_HELPERS = {
    "osx-arm64": 0x0100000C,
    "osx-x64": 0x01000007,
}


def verify_package(path, require_all=False):
    with zipfile.ZipFile(path) as package:
        names = package.namelist()
        if len(set(names)) != len(names):
            raise ValueError("Duplicate package entries")

        def require(name):
            if name not in names:
                raise ValueError(f"Missing package entry: {name}")
            data = package.read(name)
            if not data:
                raise ValueError(f"Empty package entry: {name}")
            return data

        require("LICENSE")
        require("ui/index.html")
        require("ui/localization.js")
        locale = json.loads(require("ui/locales/zh-CN.json"))
        if not isinstance(locale, dict) or not locale or any(not isinstance(value, str) or not value for value in locale.values()):
            raise ValueError("Invalid Chinese translation catalog")
        xliff = ET.fromstring(require("localization/HapticAudioFeedback_zh-CN.xliff"))
        files = xliff.findall("file")
        if not files or any(file.get("target-language") != "zh-CN" for file in files):
            raise ValueError("Invalid SDK translation language")
        units = list(xliff.iter("trans-unit"))
        if not units or any(not unit.findtext("source") or not unit.findtext("target") for unit in units):
            raise ValueError("Missing SDK action translation")
        metadata = require("metadata/LoupedeckPackage.yaml").decode("utf-8-sig")
        if not re.search(r"^pluginFolderWin:\s*bin\s*$", metadata, re.M):
            raise ValueError("Windows plugin directory is not bin")
        for name in ["HapticAudioFeedbackPlugin.dll", "HapticAudioCapture.dll"]:
            require("bin/" + name)
        dll = require("bin/runtimes/win-x64/native/haptic_cpal.dll")
        if len(dll) < 64 or dll[:2] != b"MZ":
            raise ValueError("CPAL Windows library is not PE")
        offset = struct.unpack_from("<I", dll, 60)[0]
        if offset + 6 > len(dll) or dll[offset:offset + 4] != b"PE\0\0" or struct.unpack_from("<H", dll, offset + 4)[0] != 0x8664:
            raise ValueError("CPAL Windows library is not x64")
        if "bin/NAudio.Core.dll" in names or "bin/NAudio.Wasapi.dll" in names:
            raise ValueError("Windows package must use the host's NAudio assemblies")
        for name in ["CPAL-NOTICES.txt", "NAudio-MIT.txt", "FRONTEND-NOTICES.txt"]:
            require("licenses/" + name)
        report = json.loads(require("licenses/CPAL-dependencies.json"))
        dependencies = report.get("binaryDependencies", [])
        if not dependencies or any(d.get("selected") not in ("MIT", "Apache-2.0") for d in dependencies):
            raise ValueError("Invalid binary dependency license report")

        frontend = json.loads(require("licenses/FRONTEND-dependencies.json"))
        entries = frontend.get("dependencies", [])
        if not entries:
            raise ValueError("Missing frontend dependency inventory")
        for dependency in entries:
            if dependency.get("license") not in ("MIT", "Apache-2.0"):
                raise ValueError("Invalid frontend dependency license")
            require(dependency["licenseFile"])
            if hashlib.sha256(require(dependency["path"])).hexdigest() != dependency.get("sha256"):
                raise ValueError("Frontend dependency checksum mismatch")

        mac_declared = bool(re.search(r"^pluginFolderMac:\s*bin-mac\s*$", metadata, re.M))
        if require_all and not mac_declared:
            raise ValueError("Combined package does not advertise macOS")
        found = []
        for runtime, cpu in MAC_HELPERS.items():
            bundle = f"bin-mac/runtimes/{runtime}/native/Feel the Rhythm Capture.app/Contents/"
            executable = bundle + "MacOS/haptic-cpal-helper"
            if executable not in names and not require_all:
                continue
            data = require(executable)
            if len(data) < 32 or data[:4] != b"\xcf\xfa\xed\xfe" or struct.unpack_from("<I", data, 4)[0] != cpu or struct.unpack_from("<I", data, 12)[0] != 2:
                raise ValueError(f"Wrong Mac executable architecture: {runtime}")
            entry = package.getinfo(executable)
            mode = entry.external_attr >> 16
            if entry.create_system != 3 or not stat.S_ISREG(mode) or mode & 0o111 != 0o111:
                raise ValueError(f"Mac helper lost executable permissions: {runtime}")
            info = plistlib.loads(require(bundle + "Info.plist"))
            if info.get("CFBundleIconFile") != "FeelTheRhythm.icns":
                raise ValueError(f"Mac helper icon is not declared: {runtime}")
            icon = require(bundle + "Resources/FeelTheRhythm.icns")
            if len(icon) <= 8 or icon[:4] != b"icns" or struct.unpack_from(">I", icon, 4)[0] != len(icon):
                raise ValueError(f"Invalid Mac helper icon: {runtime}")
            require(bundle + "_CodeSignature/CodeResources")
            found.append(runtime)
        if mac_declared != bool(found):
            raise ValueError("Mac manifest and native payloads disagree")
        if found:
            for name in ["HapticAudioFeedbackPlugin.dll", "HapticAudioCapture.dll", "NAudio.Core.dll"]:
                require("bin-mac/" + name)
        return ["win-x64", *found]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--require-all", action="store_true")
    args = parser.parse_args()
    platforms = verify_package(args.package, args.require_all)
    print("PASS packaged native architectures, permissions and notices: " + ", ".join(platforms))


if __name__ == "__main__":
    main()
