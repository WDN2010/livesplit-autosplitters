#!/usr/bin/env python3
"""Exercise package.py twice and prove source mtimes do not affect the ZIP."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = ROOT / "dist" / "Nunholy-AutoSplitter-1.2.0-rc4-cold-start-manual-test.zip"
RECEIPT = ROOT / "evidence" / "package-verification.json"


def run_package() -> tuple[bytes, dict[str, object]]:
    result = subprocess.run(
        [sys.executable, "package.py"],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
    )
    if result.returncode != 0:
        raise AssertionError("package.py failed:\n" + result.stdout)
    return ARCHIVE.read_bytes(), json.loads(RECEIPT.read_text(encoding="utf-8"))


def load_package_module():
    spec = importlib.util.spec_from_file_location("nunholy_package", ROOT / "package.py")
    if spec is None or spec.loader is None:
        raise AssertionError("could not load package.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main() -> None:
    if not (ROOT / "Components" / "asl-help").is_file():
        raise SystemExit(
            "SKIP: fetch dependency first with python3 fetch_dependencies.py"
        )

    original_stat = (ROOT / "README.md").stat()
    try:
        first_bytes, first_receipt = run_package()
        os.utime(
            ROOT / "README.md",
            ns=(original_stat.st_atime_ns, original_stat.st_mtime_ns + 5_000_000_000),
        )
        second_bytes, second_receipt = run_package()
    finally:
        os.utime(
            ROOT / "README.md",
            ns=(original_stat.st_atime_ns, original_stat.st_mtime_ns),
        )

    first_hash = hashlib.sha256(first_bytes).hexdigest()
    second_hash = hashlib.sha256(second_bytes).hexdigest()
    assert first_bytes == second_bytes
    assert first_hash == second_hash
    assert first_receipt == second_receipt
    assert second_receipt["sha256"] == second_hash
    assert second_receipt["version"] == "1.2.0-rc4-cold-start-manual-test"
    assert second_receipt["archive"] == (
        "dist/Nunholy-AutoSplitter-1.2.0-rc4-cold-start-manual-test.zip"
    )
    assert second_receipt["source_binaries_excluded"] is True
    assert second_receipt["live_windows_runtime"] is False
    print(f"PASS: two clean packages are byte-identical ({second_hash})")
    print("PASS: touched source mtime does not change ZIP bytes")

    # Metadata is staged before any output is replaced. A failure while staging
    # the receipt must preserve the prior archive, receipt, and checksum.
    package_module = load_package_module()
    checksum = ROOT / "dist" / "Nunholy-AutoSplitter-1.2.0-rc4-cold-start-manual-test.zip.sha256"
    outputs = (ARCHIVE, RECEIPT, checksum)
    before = {path: path.read_bytes() for path in outputs}

    # A package source is rejected when the leaf itself is a symlink and when
    # any parent directory is a symlink. Neither rejection may touch the
    # existing archive, receipt, or checksum.
    original_package_files = package_module.PACKAGE_FILES
    with tempfile.TemporaryDirectory(prefix=".package-symlink-", dir=ROOT) as temporary_name:
        temporary_root = Path(temporary_name)
        real_directory = temporary_root / "real"
        real_directory.mkdir()
        real_source = real_directory / "source.txt"
        real_source.write_bytes(b"untrusted source")
        direct_link = temporary_root / "direct.txt"
        direct_link.symlink_to(ROOT / "README.md")
        parent_link = temporary_root / "parent-link"
        parent_link.symlink_to(real_directory, target_is_directory=True)
        cases = (
            (
                temporary_root.relative_to(ROOT) / "direct.txt",
                "symlink package source component",
            ),
            (
                temporary_root.relative_to(ROOT) / "parent-link" / "source.txt",
                "symlink package source component",
            ),
            (Path("../outside-root.txt"), "package source escapes ROOT"),
            (Path("/tmp/outside-root.txt"), "package source escapes ROOT"),
        )
        try:
            for relative, expected_error in cases:
                setattr(
                    package_module,
                    "PACKAGE_FILES",
                    original_package_files + (relative,),
                )
                try:
                    package_module.build_package()
                except package_module.PackageError as error:
                    assert expected_error in str(error)
                else:
                    raise AssertionError(f"unsafe source was accepted: {relative}")
                assert {path: path.read_bytes() for path in outputs} == before
        finally:
            setattr(package_module, "PACKAGE_FILES", original_package_files)

        # Swap a regular leaf to a symlink immediately after its descriptor is
        # opened. The reader must return the original descriptor bytes rather
        # than reopen the attacker-controlled pathname.
        race_source = temporary_root / "race.txt"
        race_source.write_bytes(b"descriptor-bound source")
        race_relative = race_source.relative_to(ROOT)
        real_open = package_module.os.open
        race_swapped = False

        def swap_after_open(path, flags, mode=0o777, *, dir_fd=None):
            nonlocal race_swapped
            descriptor = real_open(path, flags, mode, dir_fd=dir_fd)
            if path == "race.txt" and dir_fd is not None and not race_swapped:
                race_swapped = True
                race_source.unlink()
                race_source.symlink_to(ROOT / "README.md")
            return descriptor

        setattr(package_module.os, "open", swap_after_open)
        try:
            race_bytes = package_module.read_package_source(race_relative)
        finally:
            setattr(package_module.os, "open", real_open)
        assert race_swapped and race_source.is_symlink()
        assert race_bytes == b"descriptor-bound source"
    assert not list((ROOT / "dist").glob(".*.tmp"))
    assert not list((ROOT / "evidence").glob(".*.tmp"))
    print("PASS: symlink sources are rejected and descriptor reads resist leaf swaps")

    real_stage_output = package_module.stage_output

    def fail_metadata(destination, data):
        if destination == RECEIPT:
            raise OSError("simulated metadata-write failure")
        return real_stage_output(destination, data)

    setattr(package_module, "stage_output", fail_metadata)
    try:
        try:
            package_module.build_package()
        except OSError as error:
            assert str(error) == "simulated metadata-write failure"
        else:
            raise AssertionError("simulated metadata-write failure was not raised")
    finally:
        setattr(package_module, "stage_output", real_stage_output)

    after = {path: path.read_bytes() for path in outputs}
    assert after == before
    assert not list((ROOT / "dist").glob(".*.tmp"))
    assert not list((ROOT / "evidence").glob(".*.tmp"))
    print("PASS: metadata staging failure preserves existing outputs")

    # A failed metadata replacement is also rolled back after an archive
    # replacement, so the three published files remain one generation.
    real_replace = package_module.os.replace

    def fail_receipt_replace(source, destination):
        if destination == RECEIPT and Path(source).suffix == ".tmp":
            raise OSError("simulated metadata publish failure")
        return real_replace(source, destination)

    setattr(package_module.os, "replace", fail_receipt_replace)
    try:
        try:
            package_module.build_package()
        except OSError as error:
            assert str(error) == "simulated metadata publish failure"
        else:
            raise AssertionError("simulated metadata publish failure was not raised")
    finally:
        setattr(package_module.os, "replace", real_replace)

    assert {path: path.read_bytes() for path in outputs} == before
    assert not list((ROOT / "dist").glob(".*.bak"))
    assert not list((ROOT / "evidence").glob(".*.bak"))
    print("PASS: metadata publish failure rolls back existing outputs")

    # A dangling destination symlink is still an existing directory entry. Let
    # its replacement publish successfully, then fail a later output so rollback
    # must restore the original link rather than merely leave it untouched.
    with tempfile.TemporaryDirectory(prefix=".package-output-", dir=ROOT) as temporary_name:
        temporary_root = Path(temporary_name)
        dangling_output = temporary_root / "dangling.out"
        later_output = temporary_root / "later.out"
        dangling_output.symlink_to("missing-target")
        staged = [
            (
                dangling_output,
                package_module.stage_output(dangling_output, b"replacement"),
            ),
            (later_output, package_module.stage_output(later_output, b"later")),
        ]
        real_replace = package_module.os.replace

        def fail_later_replace(source, destination):
            if destination == later_output and Path(source).suffix == ".tmp":
                raise OSError("simulated later-output publish failure")
            return real_replace(source, destination)

        setattr(package_module.os, "replace", fail_later_replace)
        try:
            try:
                package_module.publish_staged(staged)
            except OSError as error:
                assert str(error) == "simulated later-output publish failure"
            else:
                raise AssertionError("later-output publish failure was not raised")
        finally:
            setattr(package_module.os, "replace", real_replace)

        assert not os.path.lexists(later_output)
        assert dangling_output.is_symlink()
        assert os.readlink(dangling_output) == "missing-target"
        assert not list(temporary_root.glob(".*.tmp"))
        assert not list(temporary_root.glob(".*.bak"))
    print("PASS: rollback restores a replaced dangling output symlink")


if __name__ == "__main__":
    main()
