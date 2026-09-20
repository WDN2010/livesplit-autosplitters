#!/usr/bin/env python3
"""Build and verify the deterministic Nunholy manual-test package."""

from __future__ import annotations

import errno
import hashlib
import io
import json
import os
import stat
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
VERSION = "1.2.0-rc4-cold-start-manual-test"
DIST = ROOT / "dist"
ARCHIVE_NAME = f"Nunholy-AutoSplitter-{VERSION}.zip"
HELPER_RELATIVE = Path("Components/asl-help")
EXPECTED_HELPER_SHA256 = "c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5"
FIXED_ZIP_TIMESTAMP = (2020, 1, 1, 0, 0, 0)
OPEN_SUPPORTS_DIR_FD = os.open in os.supports_dir_fd

# Keep the package self-contained enough to reproduce the candidate, while
# excluding private collection output and all game-owned binaries.
PACKAGE_FILES = (
    Path("LICENSE"),
    Path("Nunholy_IGT.asl"),
    Path("README.md"),
    Path("AUTOSPLIT_EVIDENCE.md"),
    Path("NATIVE_TIME_EVIDENCE.md"),
    Path("FINISH_TRIGGER_EVIDENCE.md"),
    Path("fetch_dependencies.py"),
    Path("package.py"),
    Path("tests/compile_asl_actions.py"),
    Path("tests/compile_and_test_asl.py"),
    Path("tests/test_lifecycle.py"),
    Path("tests/test_packaging.py"),
    Path("tests/validate.py"),
    Path("tests/verify_native_signature.py"),
    Path("tests/test_native_signature_negative.py"),
    HELPER_RELATIVE,
    Path("third-party/asl-help/LICENSE"),
    Path("third-party/asl-help/SOURCE.txt"),
)
# Backward-compatible name for callers that used the original package script.
FILES = PACKAGE_FILES
FORBIDDEN_BASENAMES = {
    "Nunholy.exe",
    "UnityPlayer.dll",
    "Assembly-CSharp.dll",
    "UnityEngine.CoreModule.dll",
    "mscorlib.dll",
    "mono-2.0-bdwgc.dll",
    "globalgamemanagers",
    "globalgamemanagers.assets",
}


class PackageError(RuntimeError):
    pass


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def package_source_parts(relative: Path) -> tuple[str, ...]:
    """Validate a package member name before descriptor-relative traversal."""
    relative = Path(relative)
    parts = relative.parts
    if (
        relative.is_absolute()
        or not parts
        or any(part in {"", ".", ".."} for part in parts)
    ):
        raise PackageError(f"package source escapes ROOT: {relative}")
    return parts


def read_package_source(relative: Path) -> bytes:
    """Read one regular source beneath ROOT without following any symlink."""
    parts = package_source_parts(relative)
    nofollow = getattr(os, "O_NOFOLLOW", 0)
    directory = getattr(os, "O_DIRECTORY", 0)
    nonblock = getattr(os, "O_NONBLOCK", 0)
    if not nofollow or not directory or not OPEN_SUPPORTS_DIR_FD:
        raise PackageError("race-safe package source opening is unsupported")

    directory_fd: int | None = None
    source_fd: int | None = None
    try:
        directory_fd = os.open(ROOT, os.O_RDONLY | directory | nofollow)
        for component in parts[:-1]:
            try:
                next_fd = os.open(
                    component,
                    os.O_RDONLY | directory | nofollow,
                    dir_fd=directory_fd,
                )
            except OSError as error:
                if error.errno in {errno.ELOOP, errno.ENOTDIR}:
                    raise PackageError(
                        f"symlink package source component: {relative}"
                    ) from error
                raise PackageError(
                    f"missing or invalid package source: {relative}"
                ) from error
            previous_fd = directory_fd
            directory_fd = next_fd
            os.close(previous_fd)

        try:
            source_fd = os.open(
                parts[-1],
                os.O_RDONLY | nofollow | nonblock,
                dir_fd=directory_fd,
            )
        except OSError as error:
            if error.errno == errno.ELOOP:
                raise PackageError(
                    f"symlink package source component: {relative}"
                ) from error
            raise PackageError(
                f"missing or invalid package source: {relative}"
            ) from error

        metadata = os.fstat(source_fd)
        if not stat.S_ISREG(metadata.st_mode):
            raise PackageError(f"missing or invalid package source: {relative}")
        with os.fdopen(source_fd, "rb") as source:
            source_fd = None
            return source.read()
    finally:
        if source_fd is not None:
            os.close(source_fd)
        if directory_fd is not None:
            os.close(directory_fd)


def read_helper() -> bytes:
    """Read and validate asl-help once; return those exact bytes."""
    try:
        data = read_package_source(HELPER_RELATIVE)
    except PackageError as error:
        raise PackageError(
            f"{error}; run python3 fetch_dependencies.py before python3 package.py"
        ) from error
    actual = sha256_bytes(data)
    if actual != EXPECTED_HELPER_SHA256:
        raise PackageError(
            "wrong Components/asl-help SHA-256: "
            f"got {actual}, expected {EXPECTED_HELPER_SHA256}; "
            "rerun python3 fetch_dependencies.py"
        )
    return data


def collect_entries(helper_data: bytes) -> dict[str, bytes]:
    if sha256_bytes(helper_data) != EXPECTED_HELPER_SHA256:
        raise PackageError("validated helper bytes do not match expected SHA-256")
    names = [relative.as_posix() for relative in PACKAGE_FILES]
    if len(set(names)) != len(names):
        raise PackageError("duplicate package member")

    entries: dict[str, bytes] = {}
    for relative in PACKAGE_FILES:
        if relative.name in FORBIDDEN_BASENAMES or relative.suffix.lower() == ".log":
            raise PackageError(f"private/runtime file is not packageable: {relative}")
        if relative == HELPER_RELATIVE:
            data = helper_data
        else:
            data = read_package_source(relative)
        if not data:
            raise PackageError(f"empty package source: {relative}")
        entries[relative.as_posix()] = data
    return entries


def fixed_zip_info(name: str) -> zipfile.ZipInfo:
    info = zipfile.ZipInfo(filename=name, date_time=FIXED_ZIP_TIMESTAMP)
    info.create_system = 3  # Unix, independent of the host filesystem.
    info.create_version = 20
    info.extract_version = 20
    info.flag_bits = 0
    info.compress_type = zipfile.ZIP_DEFLATED
    info.comment = b""
    info.extra = b""
    info.internal_attr = 0
    info.external_attr = 0o100644 << 16
    return info


def archive_bytes(entries: dict[str, bytes]) -> bytes:
    output = io.BytesIO()
    with zipfile.ZipFile(
        output,
        mode="w",
        compression=zipfile.ZIP_DEFLATED,
        compresslevel=9,
        strict_timestamps=True,
    ) as archive:
        for name in sorted(entries):
            archive.writestr(fixed_zip_info(name), entries[name])
    return output.getvalue()


def stage_output(destination: Path, data: bytes) -> Path:
    """Write one output to a durable sibling temporary file."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{destination.name}.", suffix=".tmp", dir=destination.parent
    )
    try:
        with os.fdopen(descriptor, "wb") as temporary:
            temporary.write(data)
            temporary.flush()
            os.fsync(temporary.fileno())
    except BaseException:
        try:
            os.unlink(temporary_name)
        except FileNotFoundError:
            pass
        raise
    return Path(temporary_name)


def cleanup_staged(staged: list[tuple[Path, Path]]) -> None:
    for _, temporary in staged:
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass


def publish_staged(staged: list[tuple[Path, Path]]) -> None:
    """Publish fully written outputs with rollback-safe atomic replacements."""
    backups: list[tuple[Path, Path]] = []
    replaced: list[Path] = []
    try:
        for destination, temporary in staged:
            if os.path.lexists(destination):
                descriptor, backup_name = tempfile.mkstemp(
                    prefix=f".{destination.name}.", suffix=".bak", dir=destination.parent
                )
                os.close(descriptor)
                backup = Path(backup_name)
                backup.unlink()
                backups.append((destination, backup))
                os.replace(destination, backup)
            os.replace(temporary, destination)
            replaced.append(destination)
    except BaseException:
        for destination in reversed(replaced):
            try:
                destination.unlink()
            except FileNotFoundError:
                pass
        for destination, backup in reversed(backups):
            if os.path.lexists(backup):
                os.replace(backup, destination)
        raise
    finally:
        cleanup_staged(staged)
        for _, backup in backups:
            try:
                backup.unlink()
            except FileNotFoundError:
                pass


def write_archive(archive_path: Path, entries: dict[str, bytes]) -> None:
    """Retain the single-file API while using an atomic replacement."""
    staged = [(archive_path, stage_output(archive_path, archive_bytes(entries)))]
    publish_staged(staged)


def verify_archive(archive_path: Path, entries: dict[str, bytes]) -> dict[str, str]:
    expected_names = set(entries)
    with zipfile.ZipFile(archive_path, mode="r") as archive:
        if archive.testzip() is not None:
            raise PackageError("ZIP CRC verification failed")
        actual_names = set(archive.namelist())
        if actual_names != expected_names:
            raise PackageError(
                f"ZIP member set mismatch: got {sorted(actual_names)}, "
                f"expected {sorted(expected_names)}"
            )
        member_hashes: dict[str, str] = {}
        for name in sorted(entries):
            info = archive.getinfo(name)
            if info.date_time != FIXED_ZIP_TIMESTAMP:
                raise PackageError(f"non-deterministic ZIP timestamp: {name}")
            if info.external_attr != (0o100644 << 16):
                raise PackageError(f"non-deterministic ZIP permissions: {name}")
            member = archive.read(name)
            if member != entries[name]:
                raise PackageError(f"ZIP member bytes mismatch: {name}")
            member_hashes[name] = sha256_bytes(member)
    return member_hashes


def make_receipt(
    archive_path: Path,
    archive_hash: str,
    entries: dict[str, bytes],
    member_hashes: dict[str, str],
) -> dict[str, object]:
    return {
        "version": VERSION,
        "archive": archive_path.relative_to(ROOT).as_posix(),
        "sha256": archive_hash,
        "members": len(entries),
        "member_count": len(entries),
        "verified_members": len(member_hashes),
        "member_sha256": member_hashes,
        "source_binaries_excluded": True,
        "live_windows_runtime": False,
    }


def receipt_bytes(receipt: dict[str, object]) -> bytes:
    return (json.dumps(receipt, indent=2, sort_keys=True) + "\n").encode("utf-8")


def checksum_bytes(archive_hash: str) -> bytes:
    return f"{archive_hash}  {ARCHIVE_NAME}\n".encode("utf-8")


def write_receipt(
    archive_path: Path,
    archive_hash: str,
    entries: dict[str, bytes],
    member_hashes: dict[str, str],
) -> None:
    """Retain the metadata API while publishing both files atomically."""
    receipt = make_receipt(archive_path, archive_hash, entries, member_hashes)
    receipt_path = ROOT / "evidence" / "package-verification.json"
    checksum_path = DIST / f"{ARCHIVE_NAME}.sha256"
    staged = [
        (receipt_path, stage_output(receipt_path, receipt_bytes(receipt))),
        (checksum_path, stage_output(checksum_path, checksum_bytes(archive_hash))),
    ]
    publish_staged(staged)
    print(json.dumps(receipt, sort_keys=True))


def build_package() -> tuple[Path, str, dict[str, object]]:
    """Build, verify, and publish all package outputs as one transaction."""
    helper_data = read_helper()
    entries = collect_entries(helper_data)
    manifest = "".join(
        f"{sha256_bytes(entries[name])}  {name}\n" for name in sorted(entries)
    ).encode("utf-8")
    entries["SHA256SUMS.txt"] = manifest

    archive_path = DIST / ARCHIVE_NAME
    receipt_path = ROOT / "evidence" / "package-verification.json"
    checksum_path = DIST / f"{ARCHIVE_NAME}.sha256"
    staged: list[tuple[Path, Path]] = []
    try:
        staged_archive = stage_output(archive_path, archive_bytes(entries))
        staged.append((archive_path, staged_archive))
        member_hashes = verify_archive(staged_archive, entries)
        archive_hash = sha256_bytes(staged_archive.read_bytes())
        receipt = make_receipt(archive_path, archive_hash, entries, member_hashes)
        staged.append((receipt_path, stage_output(receipt_path, receipt_bytes(receipt))))
        staged.append((checksum_path, stage_output(checksum_path, checksum_bytes(archive_hash))))
        publish_staged(staged)
    except BaseException:
        cleanup_staged(staged)
        raise
    return archive_path, archive_hash, receipt


def main() -> int:
    try:
        archive_path, archive_hash, receipt = build_package()
        print(json.dumps(receipt, sort_keys=True))
        print(f"PACKAGE: {archive_path.relative_to(ROOT).as_posix()}")
        print(f"SHA-256: {archive_hash}")
        print(f"MEMBERS: {receipt['members']}")
        print("SOURCE BINARIES EXCLUDED: true")
        print("LIVE WINDOWS RUNTIME: false")
        return 0
    except (PackageError, OSError) as error:
        print(f"ERROR: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
