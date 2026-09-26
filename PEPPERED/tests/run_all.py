#!/usr/bin/env python3
"""Run the bounded PEPPERED offline gate and refresh its attested receipt."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
RECEIPT = ROOT / "evidence" / "offline-tests-latest.json"
SOURCE_ALLOWLIST = ("Logic.cs", "ReadOnlyReader.cs", "PEPPERED.asl.in")
SUPPORTED_BUILD_FILE_SHA256 = "650b38cbecb50db26e6f9126223232be533ba7dc39e0f88eb06fe82eb5668bcf"
EXPECTED_COMMANDS = (
    "python3 build.py",
    "python3 tests/run_logic_tests.py",
    "python3 tests/run_reader_tests.py",
    "compile/run ParentRegressionChecks.cs",
    "compile/run ReaderBridgeTests.cs",
    "python3 tests/compile_and_test_asl.py",
)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb", dir=path.parent, prefix=f".{path.name}.", suffix=".tmp", delete=False
        ) as stream:
            temporary = Path(stream.name)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def normalized(text: str, temp: Path | None = None) -> str:
    value = text.replace(str(ROOT), "<ROOT>")
    if temp is not None:
        value = value.replace(str(temp), "<TMP>")
    value = re.sub(r"(?<![\w<>])[A-Za-z]:\\[^\s]+", "<ABS_PATH>", value)
    value = re.sub(r"(?<![\w<>])/(?:[^\s]+)", "<ABS_PATH>", value)
    return value.rstrip()


def run(label: str, argv: list[str], temp: Path | None = None) -> dict[str, object]:
    try:
        result = subprocess.run(
            argv,
            cwd=ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
            timeout=60,
        )
    except subprocess.TimeoutExpired as exc:
        output = exc.stdout or ""
        if isinstance(output, bytes):
            output = output.decode("utf-8", errors="replace")
        return {"command": label, "output": normalized(output, temp) + "\nTIMEOUT", "exit_code": 124}
    return {
        "command": label,
        "output": normalized(result.stdout, temp),
        "exit_code": result.returncode,
    }


def compile_and_run(
    label: str,
    output_name: str,
    sources: list[str],
    references: list[str] | None = None,
) -> dict[str, object]:
    with tempfile.TemporaryDirectory(prefix="peppered-offline-") as raw_temp:
        temp = Path(raw_temp)
        executable = temp / output_name
        compile_argv = ["mcs", "-sdk:4.8"]
        for reference in references or []:
            compile_argv.append("-r:" + reference)
        compile_argv.extend(["-out:" + str(executable), *sources])
        try:
            compiled = subprocess.run(
                compile_argv,
                cwd=ROOT,
                text=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                check=False,
                timeout=60,
            )
        except subprocess.TimeoutExpired as exc:
            output = exc.stdout or ""
            if isinstance(output, bytes):
                output = output.decode("utf-8", errors="replace")
            return {"command": label, "output": normalized(output, temp) + "\nTIMEOUT", "exit_code": 124}
        output = compiled.stdout
        exit_code = compiled.returncode
        if exit_code == 0:
            try:
                executed = subprocess.run(
                    ["mono", str(executable)],
                    cwd=ROOT,
                    text=True,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    check=False,
                    timeout=60,
                )
            except subprocess.TimeoutExpired as exc:
                timed_output = exc.stdout or ""
                if isinstance(timed_output, bytes):
                    timed_output = timed_output.decode("utf-8", errors="replace")
                return {
                    "command": label,
                    "output": normalized(output + timed_output, temp) + "\nTIMEOUT",
                    "exit_code": 124,
                }
            output += executed.stdout
            exit_code = executed.returncode
        return {
            "command": label,
            "output": normalized(output, temp),
            "exit_code": exit_code,
        }


def attestation() -> dict[str, object]:
    build_bytes = (ROOT / "build-receipt.json").read_bytes()
    build = json.loads(build_bytes.decode("utf-8"))
    source_hashes = {
        f"src/{name}": sha256_bytes((ROOT / "src" / name).read_bytes())
        for name in SOURCE_ALLOWLIST
    }
    return {
        "asl_sha256": sha256_bytes((ROOT / "PEPPERED.asl").read_bytes()),
        "build_receipt_sha256": sha256_bytes(build_bytes),
        "dll_sha256": sha256_bytes((ROOT / "Components/Peppered.AutoSplitter.dll").read_bytes()),
        "helper_sha256": sha256_bytes((ROOT / "Components/asl-help").read_bytes()),
        "source_hashes": source_hashes,
        "supported_build_sha256": SUPPORTED_BUILD_FILE_SHA256,
        "build_sources": build.get("sources"),
    }


def main() -> int:
    results = [
        run("python3 build.py", [sys.executable, "build.py"]),
        run("python3 tests/run_logic_tests.py", [sys.executable, "tests/run_logic_tests.py"]),
        run("python3 tests/run_reader_tests.py", [sys.executable, "tests/run_reader_tests.py"]),
        compile_and_run(
            "compile/run ParentRegressionChecks.cs",
            "peppered-parent-regressions.exe",
            ["src/Logic.cs", "tests/ParentRegressionChecks.cs"],
        ),
        compile_and_run(
            "compile/run ReaderBridgeTests.cs",
            "peppered-reader-bridge.exe",
            ["src/Logic.cs", "src/ReadOnlyReader.cs", "tests/ReaderBridgeTests.cs"],
            ["Microsoft.CSharp"],
        ),
        run("python3 tests/compile_and_test_asl.py", [sys.executable, "tests/compile_and_test_asl.py"]),
    ]
    failures = [result for result in results if result["exit_code"] != 0]
    try:
        current_attestation = attestation()
    except (OSError, UnicodeDecodeError, json.JSONDecodeError, KeyError, TypeError, ValueError):
        current_attestation = {
            "asl_sha256": None,
            "build_receipt_sha256": None,
            "build_sources": None,
            "dll_sha256": None,
            "helper_sha256": None,
            "source_hashes": None,
            "supported_build_sha256": None,
        }
    receipt = {
        "attestation": current_attestation,
        "generated_by": "tests/run_all.py",
        "live_windows_runtime": False,
        "results": results,
    }
    atomic_write(RECEIPT, (json.dumps(receipt, indent=2, sort_keys=True) + "\n").encode("utf-8"))

    if failures:
        for failure in failures:
            print(f"FAIL: {failure['command']} exit={failure['exit_code']}")
            print(failure["output"])
        return 1

    print(f"PASS PEPPERED offline gate commands={len(EXPECTED_COMMANDS)}")
    for result in results:
        last_line = str(result["output"]).splitlines()[-1] if result["output"] else "(no output)"
        print(f"PASS {result['command']}: {last_line}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
