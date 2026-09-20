#!/usr/bin/env python3
"""Verify the read-only Unity native paths and supported Nunholy build tuple."""

from __future__ import annotations

import hashlib
import os
import struct
import sys
from pathlib import Path

EXPECTED_UNITYPLAYER_SHA256 = "208fd2c5bd25ff300f2dad8bdf8a3a04402eb8875b16719981ff24b8bc9c1b0d"
EXPECTED_MANAGED_HASHES = {
    "Nunholy_Data/Managed/Assembly-CSharp.dll":
        "2fdb400a70f217b22e7fef27f9848be8b90e622479040313aa1ed8afc88598eb",
    "Nunholy_Data/Managed/UnityEngine.CoreModule.dll":
        "39a749674ededf51a44fcb7d08b35ffca932c11f73493df3b97a12f8f7af9bca",
}
EXPECTED_SLOT_RVA = 0x1CC09F8
TEXT_FILE_OFFSET = 0x400
TEXT_RVA = 0x1000

SIGNATURES = (
    {
        "name": "Time.time",
        "pattern": bytes.fromhex(
            "48 8B 05 00 00 00 00 F2 0F 10 80 90 00 00 00 66 0F 5A C0 C3"
        ),
        "mask": bytes([1, 1, 1, 0, 0, 0, 0] + [1] * 13),
        "file_offset": 0x110240,
        "rva": 0x110E40,
        "storage": "*(double *)(*slot + 0x90), then float conversion",
    },
    {
        "name": "Time.timeScale",
        "pattern": bytes.fromhex(
            "48 8B 05 00 00 00 00 F3 0F 10 80 FC 00 00 00 C3"
        ),
        "mask": bytes([1, 1, 1, 0, 0, 0, 0] + [1] * 9),
        "file_offset": 0x1104A0,
        "rva": 0x1110A0,
        "storage": "*(float *)(*slot + 0xFC)",
    },
)


def require(condition: bool, message: str) -> None:
    """Enforce verifier gates even when Python runs with ``-O``."""
    if not condition:
        raise RuntimeError(message)


def matches(data: bytes, offset: int, pattern: bytes, mask: bytes) -> bool:
    return all(not required or data[offset + i] == pattern[i] for i, required in enumerate(mask))


def resolve_runtime() -> tuple[Path, Path] | None:
    if len(sys.argv) > 2:
        raise SystemExit("usage: verify_native_signature.py [RUNTIME_DIR|UnityPlayer.dll]")

    supplied = Path(sys.argv[1]) if len(sys.argv) == 2 else None
    if supplied is None:
        configured = os.environ.get("NUNHOLY_RUNTIME_DIR")
        if not configured:
            print("SKIP: NUNHOLY_RUNTIME_DIR is not set; private Nunholy binaries are unavailable")
            return None
        supplied = Path(configured)

    if supplied.is_file():
        return supplied.parent, supplied
    return supplied, supplied / "UnityPlayer.dll"


def verify_native(path: Path) -> None:
    data = path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    require(digest == EXPECTED_UNITYPLAYER_SHA256, f"unsupported SHA-256: {digest}")
    print(f"PASS UnityPlayer SHA-256: {digest}")

    resolved_slots: list[int] = []
    for spec in SIGNATURES:
        pattern = spec["pattern"]
        mask = spec["mask"]
        hits = [
            i for i in range(len(data) - len(pattern) + 1)
            if matches(data, i, pattern, mask)
        ]
        require(
            hits == [spec["file_offset"]],
            f"{spec['name']} signature hits: {[hex(i) for i in hits]}",
        )

        hit = hits[0]
        hit_rva = TEXT_RVA + hit - TEXT_FILE_OFFSET
        require(hit_rva == spec["rva"], f"{spec['name']} RVA: {hit_rva:#x}")
        displacement = struct.unpack_from("<i", data, hit + 3)[0]
        slot_rva = hit_rva + 7 + displacement
        require(
            slot_rva == EXPECTED_SLOT_RVA,
            f"{spec['name']} slot RVA: {slot_rva:#x}",
        )
        resolved_slots.append(slot_rva)

        print(f"PASS {spec['name']} wrapper: file={hit:#x}, RVA={hit_rva:#x}")
        print(f"PASS {spec['name']} storage: {spec['storage']}")

    require(len(set(resolved_slots)) == 1, "TimeManager* slot RVAs disagree")
    print(f"PASS shared TimeManager* slot RVA: {resolved_slots[0]:#x}")


def verify_managed(runtime_root: Path) -> bool:
    missing = [relative for relative in EXPECTED_MANAGED_HASHES if not (runtime_root / relative).is_file()]
    if missing:
        print(
            "SKIP: managed Nunholy build tuple unavailable; missing "
            + ", ".join(missing)
        )
        return False

    for relative, expected in EXPECTED_MANAGED_HASHES.items():
        path = runtime_root / relative
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        require(
            actual == expected,
            f"unsupported {relative} SHA-256: {actual}",
        )
        print(f"PASS {relative} SHA-256: {actual}")
    return True


def main() -> None:
    resolved = resolve_runtime()
    if resolved is None:
        return
    runtime_root, unity_player = resolved
    if not unity_player.is_file():
        print(f"SKIP: UnityPlayer.dll unavailable under {runtime_root}")
        return

    verify_native(unity_player)
    verify_managed(runtime_root)


if __name__ == "__main__":
    main()
