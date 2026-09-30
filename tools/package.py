#!/usr/bin/env python3
"""Create portable full and framework-dependent packages."""
from pathlib import Path
import hashlib
import zipfile

root = Path(__file__).resolve().parents[1]
dist = root / "dist"
rows = []
for source, name in [(dist, "KeySwitch-win-x64.zip"), (dist / "small", "KeySwitch-small.zip")]:
    exe = source / "KeySwitch.exe"
    checksum = hashlib.sha256(exe.read_bytes()).hexdigest()
    (source / "SHA256SUMS").write_text(f"{checksum}  KeySwitch.exe\n", encoding="utf-8")
    with zipfile.ZipFile(dist / name, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in [exe, source / "SHA256SUMS", root / "README_RU.md", root / "LICENSE"]:
            archive.write(path, path.name)
        for path in sorted((root / "licenses").glob("*")):
            if path.is_file():
                archive.write(path, "licenses/" + path.name)
    rows.append(f"{checksum}  {exe.relative_to(dist)}")
    rows.append(f"{hashlib.sha256((dist / name).read_bytes()).hexdigest()}  {name}")
    print(f"{name}: EXE {exe.stat().st_size} bytes; ZIP {(dist / name).stat().st_size} bytes; EXE SHA256 {checksum}")
(dist / "SHA256SUMS-all").write_text("\n".join(rows) + "\n", encoding="utf-8")

