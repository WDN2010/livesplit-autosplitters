#!/usr/bin/env python3
"""Fetch the exact asl-help dependency and install it atomically."""

from __future__ import annotations

import hashlib
import os
import tempfile
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parent
DESTINATION = ROOT / "Components" / "asl-help"
SOURCE_URL = "https://github.com/ero-qt/asl-help/raw/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c/lib/asl-help"
EXPECTED_SHA256 = "c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5"
MAX_BYTES = 16 * 1024 * 1024


def fetch_bytes() -> bytes:
    request = Request(
        SOURCE_URL,
        headers={"User-Agent": "Nunholy-AutoSplitter-dependency-fetch/1"},
    )
    with urlopen(request, timeout=30) as response:
        data = response.read(MAX_BYTES + 1)
        final_url = response.geturl()
    if len(data) > MAX_BYTES:
        raise RuntimeError(f"dependency response exceeds {MAX_BYTES} bytes")
    digest = hashlib.sha256(data).hexdigest()
    if digest != EXPECTED_SHA256:
        raise RuntimeError(
            "asl-help SHA-256 mismatch after fetching "
            f"{final_url}: got {digest}, expected {EXPECTED_SHA256}"
        )
    return data


def install(data: bytes) -> None:
    DESTINATION.parent.mkdir(parents=True, exist_ok=True)
    temporary_name: str | None = None
    try:
        descriptor, temporary_name = tempfile.mkstemp(
            prefix=".asl-help.", dir=DESTINATION.parent
        )
        with os.fdopen(descriptor, "wb") as temporary:
            temporary.write(data)
            temporary.flush()
            os.fsync(temporary.fileno())
        os.replace(temporary_name, DESTINATION)
        temporary_name = None
    finally:
        if temporary_name is not None:
            try:
                os.unlink(temporary_name)
            except FileNotFoundError:
                pass


def main() -> int:
    try:
        data = fetch_bytes()
        install(data)
    except Exception as error:
        print(f"ERROR: could not fetch asl-help: {error}")
        print(f"URL: {SOURCE_URL}")
        print("No dependency file was installed.")
        return 1

    print(f"Fetched: {DESTINATION.relative_to(ROOT).as_posix()}")
    print(f"SHA-256: {hashlib.sha256(data).hexdigest()}")
    print("Source: " + SOURCE_URL)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
