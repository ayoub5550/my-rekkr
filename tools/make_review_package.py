#!/usr/bin/env python3
"""Create a deterministic, review-only Unity source ZIP; never upload it.

Standard library only. Run from any directory with --output outside the project.
Copyright (c) 2026 Ayoub Teke
SPDX-License-Identifier: GPL-2.0-or-later
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ALLOWED_ROOTS = {"Assets", "Packages", "ProjectSettings", "ThirdParty", "tools"}
ALLOWED_FILES = {"LICENSE", "THIRD_PARTY_NOTICES.md"}
SKIP_DIRS = {
    ".git", ".worktrees", "library", "temp", "logs", "obj", "bin",
    "builds", "artifacts", "usersettings", "__pycache__", ".vs", ".idea",
}
SECRET_SUFFIXES = {".keystore", ".jks", ".p12", ".pfx", ".pem", ".key"}
SKIP_SUFFIXES = {".apk", ".aab", ".pyc", ".log", ".sln", ".user"}
REQUIRED = {
    "LICENSE", "THIRD_PARTY_NOTICES.md",
    "ThirdParty/licenses/LICENSE_ManagedDoom.txt",
    "ThirdParty/REKKR/rekkr.txt",
    "Packages/manifest.json", "ProjectSettings/ProjectVersion.txt",
    "ProjectSettings/ProjectSettings.asset",
    "Assets/Scenes/Main.unity",
    "Assets/Rekkr/Scripts/RekkrApp.cs",
    "Assets/Rekkr/Editor/RekkrBuild.cs",
    "Assets/StreamingAssets/rekkr.wad",
    "Assets/StreamingAssets/rekkr-compat.wad",
    "Assets/StreamingAssets/TimGM6mb.sf2",
    "Assets/StreamingAssets/GeneralUser-GS.sf2",
    "docs/buyer/README.md", "docs/buyer/BUILDING.md",
    "docs/buyer/CUSTOMIZING.md", "docs/buyer/VALIDATION.md",
}
TOKEN_PATTERN = re.compile(
    rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"
    rb"|gh[pousr]_[A-Za-z0-9]{30,}"
    rb"|github_pat_[A-Za-z0-9_]{40,}"
)
START_HERE = """# my-rekkr — review package

Start with [the English project guide](docs/buyer/README.md).

**Review edition, not a marketplace release.**
Existing upstream notices and licences are retained. No new distribution
rights are granted by this archive. No Unity installation, credentials,
signing key, APK or AAB is included.

See `PACKAGE_MANIFEST.json` for per-file SHA-256 hashes.
"""


def included(rel):
    """Allow known project areas only; exclude generated and sensitive paths."""
    parts = rel.parts
    if not parts:
        return False
    if any(p.lower() in SKIP_DIRS or p.startswith(".") for p in parts):
        return False
    if rel.suffix.lower() in SECRET_SUFFIXES | SKIP_SUFFIXES:
        return False
    if rel.name.lower() in {"credentials.json", "secrets.json", "id_rsa", "id_ed25519"}:
        return False
    return (parts[0] in ALLOWED_ROOTS or rel.as_posix() in ALLOWED_FILES
            or parts[:2] == ("docs", "buyer"))


def project_files(root):
    found = []
    for folder, dirs, files in os.walk(root, followlinks=False):
        base = Path(folder)
        for name in dirs:
            p = base / name
            if p.is_symlink() and included(p.relative_to(root)):
                raise ValueError("Symlink directory is not allowed: " + str(p.relative_to(root)))
        dirs[:] = [d for d in dirs if d.lower() not in SKIP_DIRS
                   and not d.startswith(".") and not (base / d).is_symlink()]
        for name in files:
            p = base / name
            rel = p.relative_to(root)
            if not included(rel):
                continue
            if p.is_symlink():
                raise ValueError("Symlink file is not allowed: " + str(rel))
            found.append(rel)
    missing = REQUIRED - {p.as_posix() for p in found}
    if missing:
        raise ValueError("Missing required files: " + ", ".join(sorted(missing)))
    return sorted(found)


def validate_data(rel, data):
    if data.startswith(b"version https://git-lfs.github.com/spec/"):
        raise ValueError("Unresolved Git LFS pointer: " + rel.as_posix())
    if TOKEN_PATTERN.search(data):
        raise ValueError("Potential secret in: " + rel.as_posix())
    if rel.as_posix() == "ProjectSettings/ProjectSettings.asset":
        text = data.decode("utf-8")
        for line in text.splitlines():
            if re.match(r"\s*androidUseCustomKeystore:\s*1\s*$", line):
                raise ValueError("Disable custom signing before packaging")
            m = re.match(r"\s*(AndroidKeystoreName|AndroidKeyaliasName|"
                         r"AndroidKeystorePass|AndroidKeyaliasPass):\s*(.*)$", line)
            if m and m[2].strip().strip("'\"").replace("{inproject}:", "").strip():
                raise ValueError("Clear signing fields before packaging: " + m[1])


def write_entry(archive, name, data, executable=False):
    info = zipfile.ZipInfo("MyRekkrReview/" + name, date_time=(1980, 1, 1, 0, 0, 0))
    info.create_system = 3
    info.external_attr = (0o100755 if executable else 0o100644) << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    archive.writestr(info, data)


def build_package(root, output):
    root, output = root.resolve(), output.resolve()
    if output == root or root in output.parents:
        raise ValueError("Output must be outside the source project")
    if output.suffix.lower() != ".zip":
        raise ValueError("Output must end in .zip")
    if output.exists():
        raise ValueError("Output already exists; choose a new filename")
    files = project_files(root)
    output.parent.mkdir(parents=True, exist_ok=True)
    manifest = {"status": "review-only", "files": []}
    fd, temporary = tempfile.mkstemp(suffix=".zip", dir=output.parent)
    os.close(fd)
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED,
                             compresslevel=6) as archive:
            for rel in files:
                data = (root / rel).read_bytes()
                validate_data(rel, data)
                write_entry(archive, rel.as_posix(), data,
                            rel.suffix.lower() in {".sh", ".py"})
                manifest["files"].append({
                    "path": rel.as_posix(), "bytes": len(data),
                    "sha256": hashlib.sha256(data).hexdigest(),
                })
            data = START_HERE.encode()
            write_entry(archive, "README.md", data)
            manifest["files"].append({
                "path": "README.md", "bytes": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
            })
            write_entry(archive, "PACKAGE_MANIFEST.json",
                        (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode())
        with zipfile.ZipFile(temporary) as archive:
            bad = archive.testzip()
            if bad:
                raise ValueError("ZIP CRC failed: " + bad)
        os.replace(temporary, output)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    return {
        "status": "review-only", "output": str(output),
        "files": len(manifest["files"]),
        "bytes": output.stat().st_size,
        "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = build_package(ROOT, args.output)
    except ValueError as error:
        parser.exit(1, f"Packaging refused: {error}\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
