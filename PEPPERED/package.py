#!/usr/bin/env python3
"""Package only a source-verified PEPPERED build."""

from __future__ import annotations

from pathlib import Path
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent
_ORIGINAL_OS_REPLACE = os.replace
VERSION = "0.4.0-rc14-optional-friend-loss-manual-test"
PREFIX = f"PEPPERED-AutoSplitter-{VERSION}/"
INSTALL_ARCHIVE_NAME = "PEPPERED-AutoSplitter-0.4.0-rc14-LiveSplit-install.zip"
INSTALL_PATHS = (
    "PEPPERED.asl",
    "Components/Peppered.AutoSplitter.dll",
    "Components/asl-help",
)
HELPER_SHA256 = "c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5"
SOURCE_ALLOWLIST = ("Logic.cs", "ReadOnlyReader.cs", "PEPPERED.asl.in")
COMPILE_SOURCES = ("Logic.cs", "ReadOnlyReader.cs")
SUPPORTED_BUILD_FILE_SHA256 = "650b38cbecb50db26e6f9126223232be533ba7dc39e0f88eb06fe82eb5668bcf"
SUPPORTED_BUILD_FILES = {
    "PEPPERED.exe": "d45d285f14ef68204bb2469bb563810bc0c1b62e51b397bb3f67f6ca3c973237",
    "UnityPlayer.dll": "09bc8eda55993e20f8a287e51381ac1e68e55df9736373334860279fcf95b91a",
    "PEPPERED_Data/Managed/Assembly-CSharp.dll": "9850435503f489ee42fb610f362017521b1f5af9f0fa2aa1318e1d9aec470253",
    "PEPPERED_Data/Managed/mscorlib.dll": "8548091dcf0d2b0015cc458e87f2b0ce70aff151ca85a86746b3494fcfce233f",
    "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll": "6a0d22bc964e88684a4291e0ecf7b24094dc616b274c2dec052ea77dc72f79cd",
    "PEPPERED_Data/globalgamemanagers": "eef07764258d9f43f96a2bb3d7f7023e1f4882e4ff045687749fc12854fba2c1",
    "PEPPERED_Data/globalgamemanagers.assets": "da9a061e193ffa5bbbd0275029b4b71f0366595dd1e52402f208c5261a0eec25",
    "PEPPERED_Data/level1": "13c198227474fc1c8d99cf1d1fb62cb5834934a8e861474c659e17e454a6517b",
    "PEPPERED_Data/level2": "1c2d8e4686c063c7a377b3b98821e97db6486f98bea459191a9812c937c8cc3c",
    "PEPPERED_Data/level9": "233225ab485d162afbb3c70db40963f6ec77be3a28fac37e9c3b4fbfddb0d9fc",
    "PEPPERED_Data/level18": "c8ec82a8f01037584085b7844ae1d24208cca79daddc19d6e72aa989b6adb893",
    "PEPPERED_Data/level37": "56734c17cd828501c2f159cef71fe84011dd10da23ca96cc79fedc13f8f1ba63",
    "PEPPERED_Data/level59": "4f863fdaee7248d02d27d9691c5a0ed5e0804ffe7a6c1b52c8feb271df762f33",
    "PEPPERED_Data/level63": "bd19dfb786963e54db15039b250ddf227d965546d5c288dd83426397532d4446",
    "PEPPERED_Data/level64": "5e53705664ce5c64a54549b6d53f651f7760cc7b0ab1cdd56bebab920e9a0cc3",
    "PEPPERED_Data/level80": "787d43758bc30e9250a52f3bfbf101a6cf9d18e70ba67781f54fac72a09a6f51",
    "PEPPERED_Data/level93": "b259f94ffae56b23d8da1049f30eef2a9f6c2724fe9d42f66973107ee5b13597",
    "PEPPERED_Data/level103": "1693ddc4bdf56b864fb56b2fcdf0603b3104b693d7a9ca3b6428dda2a9bbe89a",
    "PEPPERED_Data/level104": "d2e1b80f95b3f9d612e608815ff5123c2a8f4c0617df9509b5e32624dc7ee7af",
    "PEPPERED_Data/Managed/Unity.TextMeshPro.dll": "8cd39c3c5477f826b0f9efd9fd536b024fbd3e553e520a0aef85f7a08f108b7a",
    "PEPPERED_Data/level4": "76495bed5c3ebfdb6f8d67702196dff2d169fdb8f758487d7674e8d0d8acda52",
    "PEPPERED_Data/level5": "3c1d3f2b6347306bdd31748acdb0d1d33b12d3eb4bd67193dc893b3aaf3df940",
    "PEPPERED_Data/level72": "44434a176d4b9b50cadc6e8c9e5a04435f9d57bfb4e7fa557c475cd381818b32",
    "PEPPERED_Data/level79": "53d450ee1210e7fad9d238bef22bbfffa4a2260f21627c9922d9644694274f1b",
    "PEPPERED_Data/Managed/UnityEngine.UI.dll": "83e5e19e23bdc95bc92d21e3d8f03cce880dab60d253695b38fcd88e8d82610c",
    "PEPPERED_Data/Managed/UnityEngine.CoreModule.dll": "dd1053817d73f810f53ff73ddbeae7509ae4cef4106b03248366e8340c4b3b64",
    "PEPPERED_Data/level66": "0820915119ae2d42b45db69dee77a07c1e2b1c57745c2d022b13a6719885d902",
}
TEST_ALLOWLIST = (
    "LogicTests.cs",
    "ParentRegressionChecks.cs",
    "ReaderBridgeTests.cs",
    "ReaderTests.cs",
    "compile_and_test_asl.py",
    "official_engine_probe.py",
    "official_engine_harness.cs",
    "package_regression.py",
    "run_all.py",
    "run_logic_tests.py",
    "run_reader_tests.py",
)
EVIDENCE_ALLOWLIST = ("PARENT-VERIFIED.md", "offline-tests-latest.json", "worlds-parent-verified.json")
FIXED_FILES = (
    "PEPPERED.asl",
    "README.md",
    "LICENSE",
    "supported-build.json",
    "build-receipt.json",
    "build.py",
    "fetch_dependencies.py",
    "package.py",
    "Components/Peppered.AutoSplitter.dll",
    "Components/asl-help",
    "third-party/asl-help/LICENSE",
)
EXPECTED_OFFLINE_COMMANDS = (
    "python3 build.py",
    "python3 tests/run_logic_tests.py",
    "python3 tests/run_reader_tests.py",
    "compile/run ParentRegressionChecks.cs",
    "compile/run ReaderBridgeTests.cs",
    "python3 tests/compile_and_test_asl.py",
)
FORBIDDEN_BASENAMES = {
    "PEPPERED.exe",
    "UnityPlayer.dll",
    "Assembly-CSharp.dll",
    "mscorlib.dll",
    "mono-2.0-bdwgc.dll",
    "globalgamemanagers",
    "globalgamemanagers.assets",
}
HEX64 = re.compile(r"^[0-9a-f]{64}$")
BUILD_COMMAND = (
    "mcs",
    "-target:library",
    "-sdk:4.8",
    "-r:Microsoft.CSharp",
    "-out:Components/Peppered.AutoSplitter.dll",
    "src/Logic.cs",
    "src/ReadOnlyReader.cs",
)


class PackageError(RuntimeError):
    """A fail-closed package validation error."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise PackageError(message)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_payload(path: Path, relative: str, *, allow_empty: bool = False) -> bytes:
    require(path.is_file() and not path.is_symlink(), f"missing or invalid package source: {relative}")
    try:
        data = path.read_bytes()
    except OSError as exc:
        raise PackageError(f"unable to read package source: {relative}") from exc
    if not allow_empty:
        require(bool(data), f"empty package source: {relative}")
    return data


def _injected_failure(phase: str, index: int) -> None:
    configured = os.environ.get("PEPPERED_PACKAGE_INJECT_FAILURE", "")
    if configured == f"{phase}:{index}":
        raise OSError(f"injected PEPPERED package {phase} failure at {index}")


def _stage_bytes(directory: Path, name: str, data: bytes) -> Path:
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb", dir=directory, prefix=f".{name}.", suffix=".tmp", delete=False
        ) as stream:
            temporary = Path(stream.name)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        return temporary
    except OSError:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
        raise


def publish_transaction(outputs: list[tuple[Path, bytes]]) -> None:
    """Publish archive, receipt, and checksum as one rollback-safe set."""
    require(bool(outputs), "package transaction has no outputs")
    staged: dict[Path, Path] = {}
    previous: dict[Path, bytes | None] = {}
    try:
        for index, (path, data) in enumerate(outputs):
            require(path not in staged, f"duplicate package transaction output: {path}")
            path.parent.mkdir(parents=True, exist_ok=True)
            require(not path.is_symlink(), f"invalid package transaction output: {path}")
            require(not path.exists() or path.is_file(), f"invalid package transaction output: {path}")
            previous[path] = path.read_bytes() if path.is_file() else None
            _injected_failure("stage", index)
            staged[path] = _stage_bytes(path.parent, path.name, data)
            _injected_failure("stage", index)

        try:
            for index, (path, _data) in enumerate(outputs):
                os.replace(staged[path], path)
                _injected_failure("publish", index)
            for path, data in outputs:
                require(path.read_bytes() == data, f"package transaction verification failed: {path}")
        except Exception as exc:
            rollback_error: Exception | None = None
            for path, _data in reversed(outputs):
                try:
                    old_data = previous[path]
                    if old_data is None:
                        path.unlink(missing_ok=True)
                    else:
                        restore = _stage_bytes(path.parent, f"{path.name}.restore", old_data)
                        try:
                            # Bypass an injected/monkeypatched publish primitive.
                            _ORIGINAL_OS_REPLACE(restore, path)
                        finally:
                            restore.unlink(missing_ok=True)
                except Exception as restore_exc:  # pragma: no cover - disk failure
                    rollback_error = restore_exc
                    break
            if rollback_error is not None:
                raise PackageError("package transaction failed and rollback failed") from rollback_error
            raise PackageError("package transaction failed; previous outputs restored") from exc
    finally:
        for temporary in staged.values():
            temporary.unlink(missing_ok=True)


def validate_boundary(
    root: Path, relative: str, allowed: tuple[str, ...], optional: tuple[str, ...] = ()
) -> None:
    directory = root / relative
    require(directory.is_dir() and not directory.is_symlink(), f"missing or invalid package directory: {relative}")
    actual = {path.name for path in directory.iterdir()}
    expected = set(allowed)
    permitted = expected | set(optional)
    unexpected = sorted(actual - permitted)
    if unexpected:
        raise PackageError(f"unexpected package file: {relative}/{unexpected[0]}")
    missing = sorted(expected - actual)
    if missing:
        raise PackageError(f"missing package source: {relative}/{missing[0]}")


def package_paths(root: Path) -> tuple[str, ...]:
    validate_boundary(root, "src", SOURCE_ALLOWLIST)
    validate_boundary(root, "Components", ("Peppered.AutoSplitter.dll", "asl-help"))
    validate_boundary(root, "third-party/asl-help", ("LICENSE",))
    # A generated output receipt may exist, but must not be required to bootstrap.
    validate_boundary(root, "evidence", EVIDENCE_ALLOWLIST, ("package-verification.json",))
    names = list(FIXED_FILES)
    names.extend(f"src/{name}" for name in SOURCE_ALLOWLIST)
    names.extend(f"tests/{name}" for name in TEST_ALLOWLIST)
    names.extend(f"evidence/{name}" for name in EVIDENCE_ALLOWLIST)
    require(len(names) == len(set(names)), "duplicate package member")
    return tuple(names)


def collect_entries(root: Path) -> dict[str, bytes]:
    helper = root / "Components/asl-help"
    if not helper.is_file() or helper.is_symlink():
        raise PackageError("missing required Components/asl-help")
    entries: dict[str, bytes] = {}
    for relative in package_paths(root):
        entries[relative] = read_payload(root / relative, relative)
    return entries


def parse_json(data: bytes, relative: str) -> object:
    try:
        return json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PackageError(f"invalid JSON package source: {relative}") from exc


def digest_field(value: object, label: str) -> str:
    require(isinstance(value, str) and HEX64.fullmatch(value) is not None, f"invalid {label}")
    return value


def tool_version(tool: str) -> tuple[str, str]:
    executable = shutil.which(tool)
    if executable is None:
        raise PackageError(f"missing required compiler tool: {tool}")
    result = subprocess.run(
        [executable, "--version"],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )
    require(result.returncode == 0 and bool(result.stdout.strip()), f"unable to identify compiler tool: {tool}")
    match = re.search(
        r"\bversion\s+([0-9]+(?:\.[0-9]+)+)", result.stdout.strip(), flags=re.IGNORECASE
    )
    if match is None:
        raise PackageError(f"compiler tool returned no normalized version: {tool}")
    return executable, match.group(1)


def expected_toolchain() -> dict[str, str]:
    _compiler_path, compiler_version = tool_version("mcs")
    _runtime_path, runtime_version = tool_version("mono")
    return {
        "compiler": "mcs",
        "compiler_version": compiler_version,
        "runtime": "mono",
        "runtime_version": runtime_version,
    }


def validate_supported_build(data: bytes) -> dict[str, object]:
    try:
        identity = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PackageError("invalid supported-build.json") from exc
    require(isinstance(identity, dict), "invalid supported-build.json identity")
    files = identity.get("files")
    require(isinstance(files, dict), "invalid supported-build.json files")
    require(len(files) == len(SUPPORTED_BUILD_FILES) == 27, "supported-build.json file count is not canonical")
    for name in files:
        require(
            isinstance(name, str)
            and bool(name)
            and not Path(name).is_absolute()
            and ".." not in Path(name).parts
            and "\\" not in name,
            "supported-build.json contains an unsafe file path",
        )
    require(files == SUPPORTED_BUILD_FILES, "supported-build.json file identity/map is not canonical")
    require(
        sha(data) == SUPPORTED_BUILD_FILE_SHA256,
        "supported-build.json bytes are not the canonical identity",
    )
    return identity


def validate_build_receipt(entries: dict[str, bytes]) -> dict[str, object]:
    relative = "build-receipt.json"
    build = parse_json(entries[relative], relative)
    require(isinstance(build, dict), "invalid build receipt")
    expected_keys = {
        "asl_sha256",
        "compiler_command",
        "compiler_version",
        "dll_sha256",
        "helper_sha256",
        "runtime_tested",
        "sources",
        "supported_build_sha256",
        "toolchain",
    }
    require(set(build) == expected_keys, "invalid build receipt fields")
    require(build["runtime_tested"] is False, "build receipt runtime_tested must be false")
    require(build["compiler_command"] == list(BUILD_COMMAND), "build receipt compiler command is not expected")
    toolchain = expected_toolchain()
    require(build["toolchain"] == toolchain, "build receipt toolchain identity is not current")
    require(build["compiler_version"] == toolchain["compiler_version"], "build receipt compiler version is not current")
    require(build["helper_sha256"] == HELPER_SHA256, "build receipt helper hash is not pinned")
    validate_supported_build(entries["supported-build.json"])
    require(
        build["supported_build_sha256"] == SUPPORTED_BUILD_FILE_SHA256,
        "build receipt supported-build identity is not pinned",
    )

    sources = build["sources"]
    require(isinstance(sources, dict) and set(sources) == set(COMPILE_SOURCES), "build receipt source list is not exact")
    for name in COMPILE_SOURCES:
        digest_field(sources[name], f"build receipt source hash: {name}")
        actual = sha(entries[f"src/{name}"])
        require(actual == sources[name], f"source differs from build receipt: {name}")

    dll_digest = digest_field(build["dll_sha256"], "build receipt DLL hash")
    asl_digest = digest_field(build["asl_sha256"], "build receipt ASL hash")
    require(sha(entries["Components/Peppered.AutoSplitter.dll"]) == dll_digest, "DLL differs from build receipt")
    require(sha(entries["PEPPERED.asl"]) == asl_digest, "ASL differs from build receipt")
    require(sha(entries["Components/asl-help"]) == HELPER_SHA256, "Components/asl-help is not the pinned dependency")
    return build


def validate_offline_receipt(entries: dict[str, bytes], build: dict[str, object]) -> dict[str, object]:
    relative = "evidence/offline-tests-latest.json"
    tests = parse_json(entries[relative], relative)
    require(isinstance(tests, dict), "invalid offline test receipt")
    require(
        set(tests) == {"attestation", "generated_by", "live_windows_runtime", "results"},
        "invalid offline test receipt fields",
    )
    require(tests["generated_by"] == "tests/run_all.py", "offline test receipt generator is not canonical")
    require(tests["live_windows_runtime"] is False, "live_windows_runtime must be false")
    results = tests["results"]
    require(isinstance(results, list) and len(results) == len(EXPECTED_OFFLINE_COMMANDS), "offline test receipt result count is not exact")
    for result, expected_command in zip(results, EXPECTED_OFFLINE_COMMANDS):
        require(isinstance(result, dict), "invalid offline test result")
        require(set(result) == {"command", "exit_code", "output"}, "invalid offline test result fields")
        require(result["command"] == expected_command, "offline test command is not canonical")
        require(type(result["exit_code"]) is int and result["exit_code"] == 0, "offline test result was not successful")
        output = result["output"]
        if not isinstance(output, str):
            raise PackageError("offline test result output is invalid")
        require(
            re.search(r"(?<![\w<>])[A-Za-z]:\\[^\s]+", output) is None
            and re.search(r"(?<![\w<>])/(?:[^\s]+)", output) is None,
            "offline test output contains an absolute path",
        )

    source_hashes = {f"src/{name}": sha(entries[f"src/{name}"]) for name in SOURCE_ALLOWLIST}
    attestation = {
        "asl_sha256": sha(entries["PEPPERED.asl"]),
        "build_receipt_sha256": sha(entries["build-receipt.json"]),
        "build_sources": build["sources"],
        "dll_sha256": sha(entries["Components/Peppered.AutoSplitter.dll"]),
        "helper_sha256": sha(entries["Components/asl-help"]),
        "source_hashes": source_hashes,
        "supported_build_sha256": SUPPORTED_BUILD_FILE_SHA256,
    }
    require(tests["attestation"] == attestation, "offline test receipt is stale or fabricated")
    require(build["dll_sha256"] == attestation["dll_sha256"], "offline receipt DLL hash is not current")
    require(build["asl_sha256"] == attestation["asl_sha256"], "offline receipt ASL hash is not current")
    return tests


def validate_security_text(entries: dict[str, bytes]) -> None:
    for relative in ("src/Logic.cs", "src/ReadOnlyReader.cs", "PEPPERED.asl"):
        try:
            text = entries[relative].decode("utf-8")
        except UnicodeDecodeError as exc:
            raise PackageError(f"{relative} is not valid UTF-8") from exc
        require(
            not re.search(
                r"WriteProcessMemory|CreateRemoteThread|VirtualAllocEx|VirtualProtectEx|DllImport|WriteValue\s*\(",
                text,
            ),
            relative,
        )


def materialize_snapshot(root: Path, entries: dict[str, bytes]) -> None:
    for relative, data in entries.items():
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)


def run_canonical_gate(entries: dict[str, bytes], tests: dict[str, object]) -> None:
    """Rebuild and rerun the bounded gate from the exact cached package snapshot."""
    with tempfile.TemporaryDirectory(prefix="peppered-package-gate-") as raw_temp:
        stage = Path(raw_temp) / "PEPPERED"
        stage.mkdir()
        materialize_snapshot(stage, entries)
        env = dict(os.environ)
        env.pop("PEPPERED_GAME_ROOT", None)
        env.pop("PEPPERED_BUILD_INJECT_FAILURE", None)
        env.pop("PEPPERED_PACKAGE_INJECT_FAILURE", None)
        try:
            result = subprocess.run(
                [sys.executable, str(stage / "tests/run_all.py")],
                cwd=stage,
                env=env,
                text=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                check=False,
                timeout=300,
            )
        except subprocess.TimeoutExpired as exc:
            raise PackageError("offline gate timed out") from exc
        if result.returncode != 0:
            raise PackageError("offline gate failed:\n" + result.stdout.rstrip())

        generated_build = (stage / "build-receipt.json").read_bytes()
        generated_dll = (stage / "Components/Peppered.AutoSplitter.dll").read_bytes()
        generated_asl = (stage / "PEPPERED.asl").read_bytes()
        generated_tests = (stage / "evidence/offline-tests-latest.json").read_bytes()
        require(generated_dll == entries["Components/Peppered.AutoSplitter.dll"], "DLL does not match canonical source rebuild")
        require(generated_asl == entries["PEPPERED.asl"], "ASL does not match canonical source rebuild")
        require(generated_build == entries["build-receipt.json"], "build receipt is not canonical")
        require(generated_tests == entries["evidence/offline-tests-latest.json"], "offline test receipt is stale or fabricated")
        generated_object = parse_json(generated_tests, "canonical offline test receipt")
        require(generated_object == tests, "offline test receipt is stale or fabricated")


def build_archive(archive: Path, entries: dict[str, bytes], *, prefix: str) -> bytes:
    """Build and verify archive bytes; publish them in the output transaction."""
    archive.parent.mkdir(parents=True, exist_ok=True)
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb", dir=archive.parent, prefix=f".{archive.name}.", suffix=".tmp", delete=False
        ) as stream:
            temporary = Path(stream.name)
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
            z.comment = b""
            for name, data in sorted(entries.items()):
                info = zipfile.ZipInfo(prefix + name, date_time=(2026, 1, 1, 0, 0, 0))
                info.create_system = 3
                info.create_version = 20
                info.extract_version = 20
                info.flag_bits = 0
                info.internal_attr = 0
                info.external_attr = 0o100644 << 16
                info.extra = b""
                info.comment = b""
                info.compress_type = zipfile.ZIP_DEFLATED
                z.writestr(info, data)
        with zipfile.ZipFile(temporary) as z:
            require(z.testzip() is None, "package archive integrity check failed")
            require(set(z.namelist()) == {prefix + name for name in entries}, "package archive member set changed")
            for name, data in entries.items():
                require(z.read(prefix + name) == data, f"package archive payload changed: {name}")
        archive_bytes = temporary.read_bytes()
        return archive_bytes
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main() -> None:
    entries = collect_entries(ROOT)
    helper_digest = sha(entries["Components/asl-help"])
    require(helper_digest == HELPER_SHA256, "Components/asl-help is not the pinned dependency")
    build = validate_build_receipt(entries)
    tests = validate_offline_receipt(entries, build)
    validate_security_text(entries)

    for name in entries:
        require(Path(name).name not in FORBIDDEN_BASENAMES, f"forbidden runtime file: {name}")

    run_canonical_gate(entries, tests)

    manifest = {
        name: {"size": len(entries[name]), "sha256": sha(entries[name])}
        for name in sorted(entries)
    }
    archive_entries = dict(entries)
    archive_entries["manifest.json"] = (json.dumps(manifest, indent=2, sort_keys=True) + "\n").encode("utf-8")

    dist = ROOT / "dist"
    archive = dist / f"PEPPERED-AutoSplitter-{VERSION}.zip"
    archive_bytes = build_archive(archive, archive_entries, prefix=PREFIX)
    archive_hash = sha(archive_bytes)
    install_archive = dist / INSTALL_ARCHIVE_NAME
    install_entries = {name: entries[name] for name in INSTALL_PATHS}
    install_archive_bytes = build_archive(install_archive, install_entries, prefix="")
    install_archive_hash = sha(install_archive_bytes)
    receipt = {
        "archive": str(archive.relative_to(ROOT)),
        "default_timer_actions": False,
        "helper_sha256": HELPER_SHA256,
        "install_archive": str(install_archive.relative_to(ROOT)),
        "install_members": len(install_entries),
        "install_sha256": install_archive_hash,
        "install_size": len(install_archive_bytes),
        "live_windows_runtime": False,
        "members": len(archive_entries),
        "sha256": archive_hash,
        "size": len(archive_bytes),
        "source_binaries_excluded": True,
        "supported_build_sha256": SUPPORTED_BUILD_FILE_SHA256,
        "verified_payloads": len(manifest),
        "version": VERSION,
    }
    receipt_bytes = (json.dumps(receipt, indent=2, sort_keys=True) + "\n").encode("utf-8")
    checksum_path = dist / (archive.name + ".sha256")
    checksum_bytes = (archive_hash + "  " + archive.name + "\n").encode("utf-8")
    install_checksum_path = dist / (install_archive.name + ".sha256")
    install_checksum_bytes = (
        install_archive_hash + "  " + install_archive.name + "\n"
    ).encode("utf-8")
    publish_transaction(
        [
            (archive, archive_bytes),
            (install_archive, install_archive_bytes),
            (ROOT / "evidence/package-verification.json", receipt_bytes),
            (checksum_path, checksum_bytes),
            (install_checksum_path, install_checksum_bytes),
        ]
    )
    print(json.dumps(receipt, sort_keys=True))


if __name__ == "__main__":
    main()
