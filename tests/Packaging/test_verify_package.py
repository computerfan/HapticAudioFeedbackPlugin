import importlib.util
import json
from pathlib import Path
import stat
import struct
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("verify_package", Path(__file__).resolve().parents[2] / "tools/verify_package.py")
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)


class PackageChecks(unittest.TestCase):
    def package(self, *, mac=True, missing=None, wrong_cpu=False, executable=True, bad_license=False):
        root = tempfile.TemporaryDirectory()
        self.addCleanup(root.cleanup)
        path = Path(root.name) / "fixture.lplug4"
        pe = bytearray(70)
        pe[:2] = b"MZ"
        struct.pack_into("<I", pe, 60, 64)
        pe[64:68] = b"PE\0\0"
        struct.pack_into("<H", pe, 68, 0x8664)
        payloads = {
            "LICENSE": "project MIT license fixture",
            "metadata/LoupedeckPackage.yaml": "pluginFolderWin: bin\n" + ("pluginFolderMac: bin-mac\n" if mac else "#pluginFolderMac: bin-mac\n"),
            "bin/HapticAudioFeedbackPlugin.dll": b"managed fixture",
            "bin/HapticAudioCapture.dll": b"managed fixture",
            "bin/runtimes/win-x64/native/haptic_cpal.dll": pe,
            "licenses/CPAL-NOTICES.txt": "notice fixture",
            "licenses/NAudio-MIT.txt": "notice fixture",
            "licenses/CPAL-dependencies.json": json.dumps({"binaryDependencies": [{"selected": "GPL-3.0" if bad_license else "MIT"}]}),
        }
        if mac:
            for name in ["HapticAudioFeedbackPlugin.dll", "HapticAudioCapture.dll", "NAudio.Core.dll"]:
                payloads["bin-mac/" + name] = b"managed fixture"
            for runtime, cpu in verifier.MAC_HELPERS.items():
                bundle = f"bin-mac/runtimes/{runtime}/native/Feel the Rhythm Capture.app/Contents/"
                payloads[bundle + "Info.plist"] = "plist fixture"
                payloads[bundle + "_CodeSignature/CodeResources"] = "signature fixture"
                header = bytearray(32)
                struct.pack_into("<IIII", header, 0, 0xFEEDFACF, 0 if wrong_cpu else cpu, 0, 2)
                payloads[bundle + "MacOS/haptic-cpal-helper"] = header
        with zipfile.ZipFile(path, "w") as archive:
            for name, value in payloads.items():
                if missing and name.endswith(missing):
                    continue
                entry = zipfile.ZipInfo(name)
                entry.create_system = 3
                entry.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
                archive.writestr(entry, value)
        return path

    def test_combined_package(self):
        self.assertEqual(len(verifier.verify_package(self.package(), True)), 3)

    def test_windows_package_is_valid_locally_but_not_for_combined_ci(self):
        path = self.package(mac=False)
        self.assertEqual(verifier.verify_package(path), ["win-x64"])
        with self.assertRaisesRegex(ValueError, "advertise macOS"):
            verifier.verify_package(path, True)

    def test_wrong_architecture(self):
        with self.assertRaisesRegex(ValueError, "architecture"):
            verifier.verify_package(self.package(wrong_cpu=True), True)

    def test_lost_executable_permissions(self):
        with self.assertRaisesRegex(ValueError, "permissions"):
            verifier.verify_package(self.package(executable=False), True)

    def test_missing_files(self):
        for missing in ["LICENSE", "haptic_cpal.dll", "haptic-cpal-helper", "CodeResources", "NAudio-MIT.txt", "NAudio.Core.dll"]:
            with self.subTest(missing=missing), self.assertRaisesRegex(ValueError, "Missing package entry"):
                verifier.verify_package(self.package(missing=missing), True)

    def test_license_report_must_use_allowed_choices(self):
        with self.assertRaisesRegex(ValueError, "license report"):
            verifier.verify_package(self.package(bad_license=True), True)


if __name__ == "__main__":
    unittest.main()
