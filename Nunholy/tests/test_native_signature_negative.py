#!/usr/bin/env python3
"""Prove native verifier failures survive Python's optimized mode."""

from __future__ import annotations

import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERIFIER = ROOT / "tests" / "verify_native_signature.py"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def main() -> None:
    with tempfile.TemporaryDirectory(prefix="nunholy-native-negative-") as temporary_name:
        temporary_root = Path(temporary_name)
        wrong_hash = temporary_root / "wrong-hash.dll"
        wrong_hash.write_bytes(b"wrong UnityPlayer bytes")
        result = subprocess.run(
            [sys.executable, "-O", str(VERIFIER), str(wrong_hash)],
            cwd=ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
        require(
            result.returncode != 0 and "unsupported SHA-256" in result.stdout,
            "optimized verifier did not reject the UnityPlayer hash at the hash gate:\n"
            + result.stdout,
        )
        print("PASS: optimized verifier rejects wrong UnityPlayer hash")

        wrong_signature = temporary_root / "wrong-signature.dll"
        wrong_signature.write_bytes(b"\x00" * 128)
        driver = """
import hashlib
import sys
from pathlib import Path
sys.path.insert(0, sys.argv[2])
import verify_native_signature as verifier
path = Path(sys.argv[1])
verifier.EXPECTED_UNITYPLAYER_SHA256 = hashlib.sha256(path.read_bytes()).hexdigest()
verifier.verify_native(path)
"""
        result = subprocess.run(
            [
                sys.executable,
                "-O",
                "-c",
                driver,
                str(wrong_signature),
                str(ROOT / "tests"),
            ],
            cwd=ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
        require(
            result.returncode != 0 and "signature hits" in result.stdout,
            "optimized verifier did not reject the signature at the hit gate:\n"
            + result.stdout,
        )
        print("PASS: optimized verifier rejects wrong signature hits")

        managed_root = temporary_root / "managed-runtime"
        managed_directory = managed_root / "Nunholy_Data" / "Managed"
        managed_directory.mkdir(parents=True)
        (managed_directory / "Assembly-CSharp.dll").write_bytes(b"wrong managed bytes")
        (managed_directory / "UnityEngine.CoreModule.dll").write_bytes(
            b"wrong core module bytes"
        )
        managed_driver = """
import sys
from pathlib import Path
sys.path.insert(0, sys.argv[2])
import verify_native_signature as verifier
verifier.verify_managed(Path(sys.argv[1]))
"""
        result = subprocess.run(
            [
                sys.executable,
                "-O",
                "-c",
                managed_driver,
                str(managed_root),
                str(ROOT / "tests"),
            ],
            cwd=ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
        require(
            result.returncode != 0
            and "unsupported Nunholy_Data/Managed/Assembly-CSharp.dll SHA-256"
            in result.stdout,
            "optimized verifier did not reject the managed hash at the hash gate:\n"
            + result.stdout,
        )
        print("PASS: optimized verifier rejects wrong managed hashes")


if __name__ == "__main__":
    main()
