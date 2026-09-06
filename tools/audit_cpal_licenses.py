"""Audit the locked Windows/macOS binary dependency closures and collect license notices."""
import argparse
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "native" / "cpal-capture" / "Cargo.toml"
TARGETS = ["x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc", "x86_64-apple-darwin", "aarch64-apple-darwin"]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    included = {}
    tooling = {}
    for target in TARGETS:
        metadata = json.loads(subprocess.check_output(["cargo", "+1.90.0", "metadata", "--manifest-path", str(MANIFEST),
            "--locked", "--format-version", "1", "--filter-platform", target], text=True))
        packages = {p["id"]: p for p in metadata["packages"]}
        nodes = {n["id"]: n for n in metadata["resolve"]["nodes"]}
        def visit(identity):
            package = packages[identity]
            if any("proc-macro" in t["kind"] for t in package["targets"]):
                tooling[identity] = package
                return
            if identity in seen:
                return
            seen.add(identity)
            if package["source"] is not None:
                included[identity] = package
            for dependency in nodes[identity]["deps"]:
                if any(k["kind"] is None for k in dependency["dep_kinds"]):
                    visit(dependency["pkg"])
                else:
                    tooling[dependency["pkg"]] = packages[dependency["pkg"]]
        seen = set()
        visit(metadata["resolve"]["root"])
        for identity in nodes:
            if identity not in seen and packages[identity]["source"] is not None:
                tooling[identity] = packages[identity]
    sections = ["CPAL binary dependencies for Windows x64/ARM64 and macOS Intel/Apple Silicon.\n"
                "Generated from Cargo.lock by tools/audit_cpal_licenses.py.\n"
                "Licenses selected only from MIT or Apache-2.0 alternatives.\n"
                "Compiler, procedural macros and build-only tools are not bundled in the plugin.\n"]
    report = []
    for identity, package in sorted(included.items(), key=lambda item: (item[1]["name"], item[1]["version"])):
        expression = package.get("license") or ""
        if " AND " in expression: raise RuntimeError(f"License needs additional terms: {expression}")
        alternatives = [x.strip(" ()") for x in expression.replace("/", " OR ").split(" OR ")]
        selected = next((x for x in ["MIT", "Apache-2.0"] if x in alternatives), None)
        if selected is None:
            raise RuntimeError(f"License outside allowlist: {package['name']} {expression}")
        directory = Path(package["manifest_path"]).parent
        files = sorted(p for p in directory.iterdir() if p.is_file() and re.match(r"^(licen[cs]e|copying|notice|copyright)", p.name, re.I))
        if not files:
            vcs = json.loads((directory / ".cargo_vcs_info.json").read_text())
            saved = ROOT / "native" / "licenses" / vcs["git"]["sha1"]
            files = sorted(saved.glob("LICENSE*"))
            if package.get("repository") == "https://github.com/madsmtm/objc2":
                files.append(ROOT / "native" / "licenses" / "objc2-LICENSE-MIT.txt")
        if not files or any(not p.exists() for p in files):
            raise RuntimeError(f"Missing license/notice text: {package['name']}")
        sections.append(f"\n{'=' * 72}\n{package['name']} {package['version']}\nUpstream: {package.get('repository')}\nAuthors: {', '.join(package.get('authors', [])) or '(not listed in package metadata)'}\nDeclared: {expression}\nSelected: {selected}\n")
        for path in files:
            sections.append(f"\n--- {path.name} ---\n{path.read_text(encoding='utf-8')}\n")
        report.append({"name": package["name"], "version": package["version"], "declared": expression, "selected": selected})
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "CPAL-NOTICES.txt").write_text("".join(sections).rstrip() + "\n", encoding="utf-8")
    (args.output / "CPAL-dependencies.json").write_text(json.dumps({"targets": TARGETS, "binaryDependencies": report,
        "excludedBuildTools": [{"name": p["name"], "version": p["version"], "license": p["license"]} for p in tooling.values()]}, indent=2) + "\n", encoding="utf-8")
    print(f"PASS {len(report)} CPAL binary dependencies have MIT/Apache-2.0 choices; license texts collected.")

if __name__ == "__main__":
    main()