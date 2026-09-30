#!/usr/bin/env python3
"""Validate both portable Windows packages without executing Windows code."""
from pathlib import Path
import hashlib
import json
import struct
import zipfile

root = Path(__file__).resolve().parents[1]
results = []
for relative, archive_name in [("dist", "KeySwitch-win-x64.zip"), ("dist/small", "KeySwitch-small.zip")]:
    folder = root / relative
    data = (folder / "KeySwitch.exe").read_bytes()
    assert data[:2] == b"MZ"
    pe = struct.unpack_from("<I", data, 0x3c)[0]
    assert data[pe:pe+4] == b"PE\0\0"
    assert struct.unpack_from("<H", data, pe + 4)[0] == 0x8664
    opt = pe + 24
    assert struct.unpack_from("<H", data, opt)[0] == 0x20b
    assert struct.unpack_from("<H", data, opt + 68)[0] == 2
    certificate_offset, certificate_size = struct.unpack_from("<II", data, opt + 112 + 4*8)
    sha = hashlib.sha256(data).hexdigest()
    assert (folder / "SHA256SUMS").read_text().split()[0] == sha
    archive_path = root / "dist" / archive_name
    with zipfile.ZipFile(archive_path) as archive:
        assert archive.testzip() is None
        assert hashlib.sha256(archive.read("KeySwitch.exe")).hexdigest() == sha
        assert {"KeySwitch.exe", "README_RU.md", "LICENSE", "SHA256SUMS", "licenses/DATASETS.md"}.issubset(archive.namelist())
        assert archive.read("README_RU.md") == (root / "README_RU.md").read_bytes()
        assert archive.read("licenses/DATASETS.md") == (root / "licenses/DATASETS.md").read_bytes()
    results.append({
        "package": archive_name, "format": "PE32+", "architecture": "AMD64",
        "subsystem": "Windows GUI", "authenticode_present": bool(certificate_offset and certificate_size),
        "exe_bytes": len(data), "exe_sha256": sha, "zip_bytes": archive_path.stat().st_size,
        "zip_crc_and_embedded_exe_hash": "PASS", "windows_executed": False,
    })
(root / "artifacts/package-check.json").write_text(json.dumps(results, indent=2) + "\n")
print(json.dumps(results, indent=2))
