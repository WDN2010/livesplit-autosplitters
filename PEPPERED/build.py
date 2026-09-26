#!/usr/bin/env python3
"""Build the PEPPERED assembly from one immutable source snapshot."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent
_ORIGINAL_OS_REPLACE = os.replace
HELPER_SOURCE = ROOT / "Components/asl-help"
HELPER_SHA = "c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5"
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
BUILD_COMMAND = (
    "mcs",
    "-target:library",
    "-sdk:4.8",
    "-r:Microsoft.CSharp",
    "-out:Components/Peppered.AutoSplitter.dll",
    "src/Logic.cs",
    "src/ReadOnlyReader.cs",
)


class BuildError(RuntimeError):
    """A fail-closed build validation error."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise BuildError(message)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_payload(path: Path, label: str, *, allow_empty: bool = False) -> bytes:
    require(path.is_file() and not path.is_symlink(), f"missing or invalid {label}: {path}")
    try:
        data = path.read_bytes()
    except OSError as exc:
        raise BuildError(f"unable to read {label}: {path}") from exc
    if not allow_empty:
        require(bool(data), f"empty {label}: {path}")
    return data


def _injected_failure(phase: str, index: int) -> None:
    configured = os.environ.get("PEPPERED_BUILD_INJECT_FAILURE", "")
    if configured == f"{phase}:{index}":
        raise OSError(f"injected PEPPERED build {phase} failure at {index}")


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
    """Publish all outputs together, restoring the old set on any failure."""
    require(bool(outputs), "build transaction has no outputs")
    staged: dict[Path, Path] = {}
    previous: dict[Path, bytes | None] = {}
    try:
        for index, (path, data) in enumerate(outputs):
            require(path not in staged, f"duplicate build transaction output: {path}")
            path.parent.mkdir(parents=True, exist_ok=True)
            require(not path.is_symlink(), f"invalid build transaction output: {path}")
            require(not path.exists() or path.is_file(), f"invalid build transaction output: {path}")
            previous[path] = path.read_bytes() if path.is_file() else None
            _injected_failure("stage", index)
            staged[path] = _stage_bytes(path.parent, path.name, data)
            _injected_failure("stage", index)

        try:
            for index, (path, _data) in enumerate(outputs):
                os.replace(staged[path], path)
                _injected_failure("publish", index)
            for path, data in outputs:
                require(path.read_bytes() == data, f"build transaction verification failed: {path}")
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
                            # Use the original primitive so injected/monkeypatched
                            # publish failures cannot prevent rollback.
                            _ORIGINAL_OS_REPLACE(restore, path)
                        finally:
                            restore.unlink(missing_ok=True)
                except Exception as restore_exc:  # pragma: no cover - disk failure
                    rollback_error = restore_exc
                    break
            if rollback_error is not None:
                raise BuildError("build transaction failed and rollback failed") from rollback_error
            raise BuildError("build transaction failed; previous outputs restored") from exc
    finally:
        for temporary in staged.values():
            temporary.unlink(missing_ok=True)


def tool_version(tool: str) -> tuple[str, str]:
    executable = shutil.which(tool)
    if executable is None:
        raise BuildError(f"missing required compiler tool: {tool}")
    result = subprocess.run(
        [executable, "--version"],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )
    require(result.returncode == 0, f"unable to identify compiler tool: {tool}")
    raw_version = result.stdout.strip()
    match = re.search(r"\bversion\s+([0-9]+(?:\.[0-9]+)+)", raw_version, flags=re.IGNORECASE)
    if match is None:
        raise BuildError(f"compiler tool returned no normalized version: {tool}")
    return executable, match.group(1)


def toolchain_identity() -> dict[str, str]:
    _compiler_path, compiler_version = tool_version("mcs")
    _runtime_path, runtime_version = tool_version("mono")
    return {
        "compiler": "mcs",
        "compiler_version": compiler_version,
        "runtime": "mono",
        "runtime_version": runtime_version,
    }


def compiler_toolchain() -> tuple[str, dict[str, str]]:
    compiler_path, compiler_version = tool_version("mcs")
    _runtime_path, runtime_version = tool_version("mono")
    return compiler_path, {
        "compiler": "mcs",
        "compiler_version": compiler_version,
        "runtime": "mono",
        "runtime_version": runtime_version,
    }


def validate_supported_build(data: bytes) -> dict[str, object]:
    try:
        identity = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise BuildError("invalid supported-build.json") from exc
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
        sha256_bytes(data) == SUPPORTED_BUILD_FILE_SHA256,
        "supported-build.json bytes are not the canonical identity",
    )
    return identity


def validate_source_tree() -> Path:
    source_dir = ROOT / "src"
    require(source_dir.is_dir() and not source_dir.is_symlink(), f"missing source directory: {source_dir}")
    actual = {path.name for path in source_dir.iterdir()}
    expected = set(SOURCE_ALLOWLIST)
    unexpected = sorted(actual - expected)
    missing = sorted(expected - actual)
    if unexpected:
        raise BuildError(f"unexpected PEPPERED source file: src/{unexpected[0]}")
    if missing:
        raise BuildError(f"missing PEPPERED source file: src/{missing[0]}")
    for name in SOURCE_ALLOWLIST:
        path = source_dir / name
        require(path.is_file() and not path.is_symlink(), f"missing or invalid source: {path}")
    return source_dir


def compile_snapshot(source_bytes: dict[str, bytes], components: Path, compiler_path: str) -> bytes:
    with tempfile.TemporaryDirectory(prefix=".peppered-build-", dir=components) as raw_temp:
        temp = Path(raw_temp)
        snapshot = temp / "src"
        snapshot.mkdir()
        for name, data in source_bytes.items():
            (snapshot / name).write_bytes(data)
        output = temp / "Peppered.AutoSplitter.dll"
        command = [
            compiler_path,
            "-target:library",
            "-sdk:4.8",
            "-r:Microsoft.CSharp",
            f"-out:{output}",
            str(snapshot / "Logic.cs"),
            str(snapshot / "ReadOnlyReader.cs"),
        ]
        result = subprocess.run(
            command,
            cwd=ROOT,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            check=False,
        )
        require(result.returncode == 0, result.stdout.rstrip() or "compiler failed")
        return read_payload(output, "compiled PEPPERED DLL")


def main() -> None:
    components = ROOT / "Components"
    components.mkdir(exist_ok=True)
    require(components.is_dir() and not components.is_symlink(), f"invalid Components directory: {components}")

    if not HELPER_SOURCE.is_file() or HELPER_SOURCE.is_symlink():
        raise BuildError(
            "Missing required PEPPERED/Components/asl-help; install the pinned helper locally."
        )
    helper_bytes = read_payload(HELPER_SOURCE, "required PEPPERED/Components/asl-help")
    require(sha256_bytes(helper_bytes) == HELPER_SHA, "Wrong PEPPERED/Components/asl-help hash; refusing to build.")

    source_dir = validate_source_tree()
    source_bytes = {
        name: read_payload(source_dir / name, f"source src/{name}")
        for name in SOURCE_ALLOWLIST
    }
    supported_bytes = read_payload(ROOT / "supported-build.json", "supported-build.json")
    validate_supported_build(supported_bytes)

    compiler_path, compiler = compiler_toolchain()
    dll_bytes = compile_snapshot(
        {name: source_bytes[name] for name in COMPILE_SOURCES}, components, compiler_path
    )

    pairs = ",\n".join(
        "        { " + json.dumps(key) + ", " + json.dumps(value) + " }"
        for key, value in SUPPORTED_BUILD_FILES.items()
    )

    template_bytes = source_bytes["PEPPERED.asl.in"]
    try:
        template = template_bytes.decode("utf-8")
    except UnicodeDecodeError as exc:
        raise BuildError("PEPPERED.asl.in is not valid UTF-8") from exc
    require(template.count("__LOGIC_SHA256__") == 1, "PEPPERED.asl.in must contain one __LOGIC_SHA256__ placeholder")
    require(template.count("__SUPPORTED_HASHES__") == 1, "PEPPERED.asl.in must contain one __SUPPORTED_HASHES__ placeholder")
    asl_bytes = template.replace("__LOGIC_SHA256__", sha256_bytes(dll_bytes)).replace(
        "__SUPPORTED_HASHES__", pairs
    ).encode("utf-8")

    receipt = {
        "asl_sha256": sha256_bytes(asl_bytes),
        "compiler_command": list(BUILD_COMMAND),
        "compiler_version": compiler["compiler_version"],
        "dll_sha256": sha256_bytes(dll_bytes),
        "helper_sha256": HELPER_SHA,
        "runtime_tested": False,
        "sources": {name: sha256_bytes(source_bytes[name]) for name in COMPILE_SOURCES},
        "supported_build_sha256": SUPPORTED_BUILD_FILE_SHA256,
        "toolchain": compiler,
    }
    receipt_bytes = (json.dumps(receipt, indent=2, sort_keys=True) + "\n").encode("utf-8")

    publish_transaction(
        [
            (components / "Peppered.AutoSplitter.dll", dll_bytes),
            (ROOT / "PEPPERED.asl", asl_bytes),
            (ROOT / "build-receipt.json", receipt_bytes),
        ]
    )
    print(json.dumps(receipt, sort_keys=True))


if __name__ == "__main__":
    main()
