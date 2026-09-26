#!/usr/bin/env python3
"""Exercise adversarial PEPPERED package/build and receipt validation."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = "dist/PEPPERED-AutoSplitter-0.4.0-rc14-optional-friend-loss-manual-test.zip"
INSTALL_ARCHIVE = "dist/PEPPERED-AutoSplitter-0.4.0-rc14-LiveSplit-install.zip"
RECEIPT = "evidence/package-verification.json"
CHECKSUM = ARCHIVE + ".sha256"
INSTALL_CHECKSUM = INSTALL_ARCHIVE + ".sha256"
PACKAGE_OUTPUTS = (ARCHIVE, INSTALL_ARCHIVE, RECEIPT, CHECKSUM, INSTALL_CHECKSUM)
OFFLINE = "evidence/offline-tests-latest.json"


def run(
    root: Path,
    argv: list[str],
    *,
    env: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        argv,
        cwd=root,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
    )


def run_build(root: Path, *, env: dict[str, str] | None = None) -> subprocess.CompletedProcess[str]:
    return run(root, [sys.executable, str(root / "build.py")], env=env)


def run_package(
    root: Path,
    *,
    optimized: bool = False,
    env: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    executable = [sys.executable]
    if optimized:
        executable.append("-O")
    executable.append(str(root / "package.py"))
    return run(root, executable, env=env)


def copy_case(base: Path, destination: Path, *, include_dist: bool = False) -> Path:
    ignored = [".testbuild", "__pycache__"]
    if not include_dist:
        ignored.append("dist")
    shutil.copytree(
        base,
        destination,
        ignore=shutil.ignore_patterns(*ignored),
    )
    return destination


def assert_rejected(label: str, result: subprocess.CompletedProcess[str]) -> None:
    if result.returncode == 0:
        raise AssertionError(f"{label} was accepted\n{result.stdout}")


def assert_outputs_unchanged(root: Path, before: dict[Path, bytes]) -> None:
    for path, content in before.items():
        if not path.is_file() or path.read_bytes() != content:
            raise AssertionError(f"package output replaced after rejection: {path}")


def with_failure(name: str, value: str) -> dict[str, str]:
    env = dict(os.environ)
    env[name] = value
    return env


def mutate_supported_build(path: Path, *, traversal: bool = False) -> None:
    identity = json.loads(path.read_text(encoding="utf-8"))
    files = identity["files"]
    if traversal:
        files["../../outside.dll"] = files.pop("PEPPERED.exe")
    else:
        identity["files"] = {"PEPPERED.exe": files["PEPPERED.exe"]}
    path.write_text(json.dumps(identity, indent=2) + "\n", encoding="utf-8")


def load_package_module(root: Path):
    spec = importlib.util.spec_from_file_location("peppered_package_regression", root / "package.py")
    if spec is None or spec.loader is None:
        raise AssertionError("unable to load package module")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main() -> None:
    with tempfile.TemporaryDirectory(prefix="peppered-package-regression-") as raw:
        workspace = Path(raw)
        base = workspace / "base" / "PEPPERED"
        copy_case(ROOT, base)
        pristine = run_package(base)
        if pristine.returncode != 0:
            raise AssertionError(
                "committed receipts are not packageable before regeneration:\n"
                + pristine.stdout
            )
        gate = run(base, [sys.executable, str(base / "tests/run_all.py")])
        if gate.returncode != 0:
            raise AssertionError("baseline offline gate failed:\n" + gate.stdout)
        baseline = run_package(base)
        if baseline.returncode != 0:
            raise AssertionError("baseline package failed:\n" + baseline.stdout)

        output_paths = [base / relative for relative in PACKAGE_OUTPUTS]
        baseline_outputs = {path: path.read_bytes() for path in output_paths}
        build_paths = [
            base / "Components/Peppered.AutoSplitter.dll",
            base / "PEPPERED.asl",
            base / "build-receipt.json",
        ]
        baseline_build_outputs = {path: path.read_bytes() for path in build_paths}

        stable_build = run_build(base)
        if stable_build.returncode != 0:
            raise AssertionError("repeat build failed:\n" + stable_build.stdout)
        if {path: path.read_bytes() for path in build_paths} != baseline_build_outputs:
            raise AssertionError("repeat build was not byte-stable")
        stable_package = run_package(base)
        if stable_package.returncode != 0:
            raise AssertionError("repeat package failed:\n" + stable_package.stdout)
        if {path: path.read_bytes() for path in output_paths} != baseline_outputs:
            raise AssertionError("repeat package was not byte-stable")

        # Public clones/source archives must package without a previous output receipt.
        bootstrap = copy_case(base, workspace / "no-prior-package-receipt")
        (bootstrap / RECEIPT).unlink()
        fresh = run_package(bootstrap)
        if fresh.returncode != 0:
            raise AssertionError("first package without old receipt failed:\n" + fresh.stdout)
        for relative in PACKAGE_OUTPUTS:
            if (bootstrap / relative).read_bytes() != baseline_outputs[base / relative]:
                raise AssertionError("first packaging changed deterministic output: " + relative)

        extracted_parent = workspace / "source-archive-repack"
        with zipfile.ZipFile(base / ARCHIVE) as source_archive:
            source_archive.extractall(extracted_parent)
        extracted = extracted_parent / Path(ARCHIVE).stem
        if (extracted / RECEIPT).exists():
            raise AssertionError("source ZIP must not require its own generated output receipt")
        if not (extracted / "fetch_dependencies.py").is_file():
            raise AssertionError("public source ZIP omits dependency fetcher")
        repacked = run_package(extracted)
        if repacked.returncode != 0:
            raise AssertionError("clean source archive cannot reproduce package:\n" + repacked.stdout)
        for relative in PACKAGE_OUTPUTS:
            if (extracted / relative).read_bytes() != baseline_outputs[base / relative]:
                raise AssertionError("source archive repack is not deterministic: " + relative)

        for required_evidence in ("PARENT-VERIFIED.md", "worlds-parent-verified.json", "offline-tests-latest.json"):
            missing = copy_case(base, workspace / ("missing-required-" + required_evidence))
            (missing / "evidence" / required_evidence).unlink()
            assert_rejected("required input evidence missing", run_package(missing))

        for label, traversal in (("one-entry", False), ("traversal-entry", True)):
            build_case = copy_case(base, workspace / ("supported-build-" + label))
            mutate_supported_build(build_case / "supported-build.json", traversal=traversal)
            build_before = {
                build_case / relative: baseline_build_outputs[base / relative]
                for relative in (
                    "Components/Peppered.AutoSplitter.dll",
                    "PEPPERED.asl",
                    "build-receipt.json",
                )
            }
            build_result = run_build(build_case)
            assert_rejected("build " + label + " supported-build mutation", build_result)
            assert_outputs_unchanged(build_case, build_before)

            package_case = copy_case(
                base,
                workspace / ("package-supported-build-" + label),
                include_dist=True,
            )
            mutate_supported_build(package_case / "supported-build.json", traversal=traversal)
            package_before = {
                package_case / relative: baseline_outputs[base / relative]
                for relative in PACKAGE_OUTPUTS
            }
            package_result = run_package(package_case, optimized=True)
            assert_rejected("package " + label + " supported-build mutation", package_result)
            assert_outputs_unchanged(package_case, package_before)

        for phase in ("stage:1", "publish:1"):
            build_case = copy_case(base, workspace / ("build-transaction-" + phase.replace(":", "-")))
            build_before = {
                build_case / relative: baseline_build_outputs[base / relative]
                for relative in (
                    "Components/Peppered.AutoSplitter.dll",
                    "PEPPERED.asl",
                    "build-receipt.json",
                )
            }
            build_result = run_build(
                build_case,
                env=with_failure("PEPPERED_BUILD_INJECT_FAILURE", phase),
            )
            assert_rejected("build transaction " + phase, build_result)
            assert_outputs_unchanged(build_case, build_before)

            package_case = copy_case(
                base,
                workspace / ("package-transaction-" + phase.replace(":", "-")),
                include_dist=True,
            )
            package_before = {
                package_case / relative: baseline_outputs[base / relative]
                for relative in PACKAGE_OUTPUTS
            }
            package_result = run_package(
                package_case,
                env=with_failure("PEPPERED_PACKAGE_INJECT_FAILURE", phase),
            )
            assert_rejected("package transaction " + phase, package_result)
            assert_outputs_unchanged(package_case, package_before)

        helper_case = copy_case(base, workspace / "helper" / "PEPPERED", include_dist=True)
        helper = helper_case / "Components/asl-help"
        helper_data = bytearray(helper.read_bytes())
        helper_data[0] ^= 1
        helper.write_bytes(helper_data)
        helper_result = run_package(helper_case, optimized=True)
        assert_rejected("python -O helper mutation", helper_result)
        assert_outputs_unchanged(
            helper_case,
            {
                helper_case / relative: baseline_outputs[base / relative]
                for relative in PACKAGE_OUTPUTS
            },
        )

        fake_case = copy_case(base, workspace / "fake-receipt" / "PEPPERED", include_dist=True)
        fake_tests = {
            "generated_by": "tests/run_all.py",
            "live_windows_runtime": False,
            "results": [
                {"command": command, "exit_code": 0, "output": "fabricated success"}
                for command in (
                    "python3 build.py",
                    "python3 tests/run_logic_tests.py",
                    "python3 tests/run_reader_tests.py",
                    "compile/run ParentRegressionChecks.cs",
                    "compile/run ReaderBridgeTests.cs",
                    "python3 tests/compile_and_test_asl.py",
                )
            ],
        }
        (fake_case / OFFLINE).write_text(json.dumps(fake_tests, indent=2) + "\n", encoding="utf-8")
        fake_before = {
            fake_case / relative: baseline_outputs[base / relative]
            for relative in PACKAGE_OUTPUTS
        }
        fake_result = run_package(fake_case, optimized=True)
        assert_rejected("python -O fabricated six-result receipt", fake_result)
        assert_outputs_unchanged(fake_case, fake_before)

        binary_case = copy_case(base, workspace / "forged-binary" / "PEPPERED", include_dist=True)
        dll = binary_case / "Components/Peppered.AutoSplitter.dll"
        original_dll = dll.read_bytes()
        forged_dll = bytearray(original_dll)
        forged_dll[0] ^= 1
        dll.write_bytes(forged_dll)
        build_path = binary_case / "build-receipt.json"
        build = json.loads(build_path.read_text(encoding="utf-8"))
        build["dll_sha256"] = hashlib.sha256(forged_dll).hexdigest()
        forged_build = (json.dumps(build, indent=2, sort_keys=True) + "\n").encode("utf-8")
        build_path.write_bytes(forged_build)
        offline_path = binary_case / OFFLINE
        offline = json.loads(offline_path.read_text(encoding="utf-8"))
        offline["attestation"]["build_receipt_sha256"] = hashlib.sha256(forged_build).hexdigest()
        offline["attestation"]["dll_sha256"] = hashlib.sha256(forged_dll).hexdigest()
        offline_path.write_text(json.dumps(offline, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        binary_before = {
            binary_case / relative: baseline_outputs[base / relative]
            for relative in PACKAGE_OUTPUTS
        }
        binary_result = run_package(binary_case, optimized=True)
        assert_rejected("python -O forged DLL plus edited build receipt", binary_result)
        assert_outputs_unchanged(binary_case, binary_before)

        unlisted_case = copy_case(base, workspace / "unlisted-source" / "PEPPERED", include_dist=True)
        (unlisted_case / "src/rogue.dll").write_bytes(b"not a source")
        unlisted_result = run_package(unlisted_case, optimized=True)
        assert_rejected("python -O unlisted source binary", unlisted_result)

        toctou_case = copy_case(base, workspace / "toctou" / "PEPPERED", include_dist=True)
        toctou_dll = toctou_case / "Components/Peppered.AutoSplitter.dll"
        cached_dll = toctou_dll.read_bytes()
        package_module = load_package_module(toctou_case)
        from pathlib import Path as PathType

        original_read_bytes = PathType.read_bytes
        reads = {"dll": 0}

        def swapped_read(self: PathType) -> bytes:
            data = original_read_bytes(self)
            if self == toctou_dll:
                reads["dll"] += 1
                if reads["dll"] == 1:
                    self.write_bytes(data + b"swap-after-validation")
            return data

        setattr(PathType, "read_bytes", swapped_read)
        try:
            setattr(package_module, "ROOT", toctou_case)
            package_module.main()
        finally:
            setattr(PathType, "read_bytes", original_read_bytes)
            toctou_dll.write_bytes(cached_dll)
        if reads["dll"] != 1:
            raise AssertionError(f"TOCTOU probe observed {reads['dll']} DLL reads; expected one")
        with zipfile.ZipFile(toctou_case / ARCHIVE) as archive:
            member = package_module.PREFIX + "Components/Peppered.AutoSplitter.dll"
            if archive.read(member) != cached_dll:
                raise AssertionError("archive used bytes read after the validation snapshot")
        with zipfile.ZipFile(toctou_case / INSTALL_ARCHIVE) as archive:
            if archive.read("Components/Peppered.AutoSplitter.dll") != cached_dll:
                raise AssertionError("install archive used bytes read after the validation snapshot")

        for relative in (ARCHIVE, INSTALL_ARCHIVE):
            with zipfile.ZipFile(base / relative) as archive:
                names = archive.namelist()
                if names != sorted(names):
                    raise AssertionError("archive member order is not deterministic")
                for info in archive.infolist():
                    if (
                        info.create_system != 3
                        or info.create_version != 20
                        or info.extract_version != 20
                        or info.flag_bits != 0
                        or info.internal_attr != 0
                        or info.external_attr != 0o100644 << 16
                        or info.extra != b""
                        or info.comment != b""
                    ):
                        raise AssertionError("archive member metadata is not canonical")

        expected_install = {
            "PEPPERED.asl",
            "Components/Peppered.AutoSplitter.dll",
            "Components/asl-help",
        }
        with zipfile.ZipFile(base / INSTALL_ARCHIVE) as archive:
            if set(archive.namelist()) != expected_install:
                raise AssertionError("install archive is not rooted at the LiveSplit directory")
            for relative in expected_install:
                if archive.read(relative) != (base / relative).read_bytes():
                    raise AssertionError("install archive runtime payload differs from source snapshot")
            with tempfile.TemporaryDirectory(prefix="peppered-install-shape-") as raw_install:
                install_root = Path(raw_install)
                archive.extractall(install_root)
                if not all((install_root / relative).is_file() for relative in expected_install):
                    raise AssertionError("install archive does not satisfy startup-relative paths")

        package_receipt = json.loads((base / RECEIPT).read_text(encoding="utf-8"))
        source_bytes = (base / ARCHIVE).read_bytes()
        install_bytes = (base / INSTALL_ARCHIVE).read_bytes()
        source_hash = hashlib.sha256(source_bytes).hexdigest()
        install_hash = hashlib.sha256(install_bytes).hexdigest()
        with zipfile.ZipFile(base / ARCHIVE) as archive:
            source_members = len(archive.namelist())

        if package_receipt["archive"] != ARCHIVE:
            raise AssertionError("receipt source archive path is not canonical")
        if package_receipt["members"] != source_members:
            raise AssertionError("receipt source member count is not canonical")
        if package_receipt["size"] != len(source_bytes):
            raise AssertionError("receipt source archive size is stale")
        if package_receipt["sha256"] != source_hash:
            raise AssertionError("receipt source archive hash is stale")
        if package_receipt["verified_payloads"] != source_members - 1:
            raise AssertionError("receipt verified payload count is not canonical")
        if (base / CHECKSUM).read_text(encoding="utf-8") != (
            source_hash + "  " + Path(ARCHIVE).name + "\n"
        ):
            raise AssertionError("source archive checksum sidecar is stale")

        if package_receipt["install_archive"] != INSTALL_ARCHIVE:
            raise AssertionError("receipt install archive path is not canonical")
        if package_receipt["install_members"] != len(expected_install):
            raise AssertionError("receipt install member count is not canonical")
        if package_receipt["install_size"] != len(install_bytes):
            raise AssertionError("receipt install archive size is stale")
        if package_receipt["install_sha256"] != install_hash:
            raise AssertionError("receipt install archive hash is stale")
        if (base / INSTALL_CHECKSUM).read_text(encoding="utf-8") != (
            install_hash + "  " + Path(INSTALL_ARCHIVE).name + "\n"
        ):
            raise AssertionError("install archive checksum sidecar is stale")

    print("PASS package helper mutation under python -O")
    print("PASS fabricated six-result receipt rejection")
    print("PASS forged DLL plus edited receipt rejection")
    print("PASS unlisted source binary rejection")
    print("PASS TOCTOU single-read archive pinning")
    print("PASS canonical supported-build one-entry/traversal rejection")
    print("PASS build/package transaction rollback staging/publish failures")
    print("PASS deterministic ZIP member metadata and repeat outputs")
    print("PASS LiveSplit-root install archive shape")
    print("PASS source/install receipt and checksum synchronization")
    print("PASS first package without prior generated receipt")
    print("PASS deterministic repack from clean source archive including fetcher")
    print("PASS missing required input evidence still rejected")


if __name__ == "__main__":
    main()
