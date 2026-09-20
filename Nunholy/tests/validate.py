#!/usr/bin/env python3
"""Static checks for the Nunholy LiveSplit probe and manual-test package."""

from __future__ import annotations

import hashlib
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASL = ROOT / "Nunholy_IGT_probe.asl"
FINAL_ASL = ROOT / "Nunholy_IGT.asl"
COLLECTOR = ROOT / "Collect-NunholyRuntime.ps1"
README = ROOT / "README.md"
LICENSE = ROOT / "LICENSE"
PACKAGE = ROOT / "package.py"
FETCH = ROOT / "fetch_dependencies.py"
ADAPTER = ROOT / "tests/compile_and_test_asl.py"
LEGACY_GATE = ROOT / "tests/compile_asl_actions.py"
PACKAGING_TEST = ROOT / "tests/test_packaging.py"
NATIVE_VERIFIER = ROOT / "tests/verify_native_signature.py"
NATIVE_NEGATIVE_TEST = ROOT / "tests/test_native_signature_negative.py"
HELPER_SOURCE = ROOT / "third-party/asl-help/SOURCE.txt"
EXPECTED_VERSION = "1.2.0-rc4-cold-start-manual-test"
EXPECTED_HELPER_SHA256 = "c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5"
EXPECTED_BUILD_HASHES = (
    "208fd2c5bd25ff300f2dad8bdf8a3a04402eb8875b16719981ff24b8bc9c1b0d",
    "2fdb400a70f217b22e7fef27f9848be8b90e622479040313aa1ed8afc88598eb",
    "39a749674ededf51a44fcb7d08b35ffca932c11f73493df3b97a12f8f7af9bca",
)


def balanced(text: str, opening: str, closing: str) -> bool:
    depth = 0
    in_string = False
    escaped = False
    for char in text:
        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue
        if char == '"':
            in_string = True
        elif char == opening:
            depth += 1
        elif char == closing:
            depth -= 1
            if depth < 0:
                return False
    return depth == 0 and not in_string


def action_body(text: str, name: str) -> str:
    match = re.search(rf"(?m)^{re.escape(name)}\s*\n\{{", text)
    assert match, f"missing action: {name}"
    opening = text.find("{", match.start())
    depth = 0
    in_string = False
    escaped = False
    for index in range(opening, len(text)):
        char = text[index]
        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue
        if char == '"':
            in_string = True
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[opening + 1 : index]
    raise AssertionError(f"unclosed action: {name}")


def helper_path() -> Path | None:
    path = ROOT / "Components" / "asl-help"
    return path if path.is_file() else None


def main() -> None:
    for path in (ASL, FINAL_ASL, COLLECTOR, README, LICENSE, PACKAGE, FETCH, ADAPTER, LEGACY_GATE, PACKAGING_TEST, NATIVE_VERIFIER, NATIVE_NEGATIVE_TEST, HELPER_SOURCE):
        assert path.exists(), f"missing package file: {path}"
        assert path.stat().st_size > 0, f"empty package file: {path}"

    assert README.read_text(encoding="utf-8").startswith(
        f"# Nunholy AutoSplitter + exact IGT v{EXPECTED_VERSION}"
    )
    assert "https://github.com/ero-qt/asl-help/raw/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c/lib/asl-help" in README.read_text(encoding="utf-8")
    assert f'VERSION = "{EXPECTED_VERSION}"' in PACKAGE.read_text(encoding="utf-8")
    root_license = ROOT.parent / "LICENSE"
    assert root_license.is_file() and LICENSE.read_text(encoding="utf-8") == root_license.read_text(
        encoding="utf-8"
    )

    asl = ASL.read_text(encoding="utf-8")
    final_asl = FINAL_ASL.read_text(encoding="utf-8")
    collector = COLLECTOR.read_text(encoding="utf-8")
    fetch = FETCH.read_text(encoding="utf-8")
    adapter = ADAPTER.read_text(encoding="utf-8")
    legacy_gate = LEGACY_GATE.read_text(encoding="utf-8")
    packaging_test = PACKAGING_TEST.read_text(encoding="utf-8")
    native_verifier = NATIVE_VERIFIER.read_text(encoding="utf-8")
    helper_source = HELPER_SOURCE.read_text(encoding="utf-8")
    package = PACKAGE.read_text(encoding="utf-8")

    assert re.search(r'^state\("Nunholy"\)', asl, re.MULTILINE)
    assert final_asl.startswith(
        "// Nunholy AutoSplitter v1.2.0-rc4-cold-start-manual-test\n"
    )
    for action in ("startup", "init", "update", "exit", "shutdown"):
        assert re.search(rf"^{action}\s*$", asl, re.MULTILINE), f"missing ASL action: {action}"

    for field in ("startTime", "Inst", "curChapter", "isBattle"):
        assert f'"{field}"' in asl, f"missing managed field: {field}"

    assert balanced(asl, "{", "}"), "ASL braces or strings are unbalanced"
    assert balanced(final_asl, "{", "}"), "final ASL braces or strings are unbalanced"
    assert balanced(collector, "{", "}"), "PowerShell braces or strings are unbalanced"

    for action in (
        "startup",
        "init",
        "update",
        "start",
        "gameTime",
        "split",
        "reset",
        "onStart",
        "onSplit",
        "onReset",
        "isLoading",
        "exit",
        "shutdown",
    ):
        assert re.search(rf"^{action}\s*$", final_asl, re.MULTILINE), f"missing final ASL action: {action}"

    assert "diagnostics" not in final_asl, "production ASL still contains diagnostic logger controls"
    assert 'settings.Add("autoStart"' in final_asl
    assert 'settings.Add("autoReset"' in final_asl
    assert 'new TimerModel { CurrentState = timer }' in final_asl
    assert 'timer.CurrentPhase == TimerPhase.Ended' in final_asl
    assert 'vars.TimerModel.Reset()' in final_asl
    assert 'settings.Add("splitFloors"' in final_asl
    assert 'settings.Add("splitFinal"' in final_asl
    shutdown_body = action_body(final_asl, "shutdown")
    assert "Dispose" not in shutdown_body, "shutdown action duplicates generated helper cleanup"
    assert "if ((string)vars.NativeAttachDiagnostic != reason)" in final_asl
    assert 'settings.StartEnabled' in final_asl
    assert 'settings.SplitEnabled' in final_asl
    assert 'vars.AutoStartAttempt' in final_asl
    assert 'vars.AutoStartArmed' in final_asl
    assert 'vars.ColdProcessProven' in final_asl
    assert 'vars.ColdStartPending' in final_asl
    assert 'vars.ColdStartObservationOpen = true' in final_asl
    assert 'bool firstColdObservation = (bool)vars.ColdStartObservationOpen' in final_asl
    assert 'vars.AslStartupUtc = DateTime.UtcNow' in final_asl
    assert 'game.StartTime.ToUniversalTime()' in final_asl
    assert 'DateTime.MinValue' in final_asl
    assert 'coldZeroObservation' in final_asl
    assert 'coldSettledIdentity' in final_asl
    assert 'sampleStartTime == 0f' in final_asl
    assert 'sampleStartTime > 0f' in final_asl
    assert 'TimerPhase.NotRunning' in final_asl
    assert 'TimerPhase.Ended' in final_asl
    assert 'freshAutoStartObservation = freshAutoStartObservation || coldAutoStartObservation' in final_asl
    assert 'Thread.Sleep' not in final_asl
    assert 'vars.PendingSplitKind' in final_asl
    assert 'vars.ResultFreezeObserved' in final_asl
    assert 'vars.HaveValidSample' in final_asl
    assert 'vars.LatestScene' in final_asl
    assert 'vars.HaveLastValidSample' in final_asl
    assert 'vars.LastValidChapter' in final_asl
    assert 'vars.LastValidIsBattle' in final_asl
    assert '"TargetVampire"' in final_asl
    assert 'lastValidChapter == expectedChapter - 1' in final_asl
    assert 'sampleChapter == expectedChapter' in final_asl
    assert 'lastValidIsBattle' in final_asl and '!sampleIsBattle' in final_asl
    assert 'vars.HaveLastValidSample = true' in final_asl
    assert 'old.Chapter' not in final_asl and 'old.IsBattle' not in final_asl
    assert 'current.TimeScale == 0f' in final_asl
    assert 'second frozen poll' in final_asl
    assert 'previouslyPaused || Math.Abs(rawIgt - previousFrozenIgt)' not in final_asl
    assert 'acceptedRunReplaced' in final_asl
    assert final_asl.index('bool acceptedRunReplaced') < final_asl.index('float previousFrozenIgt')
    assert '&& !acceptedRunReplaced' in final_asl
    assert 'Math.Abs(sampleStartTime - (float)vars.CapturedRunStart) > 0.001f' in final_asl
    assert 'vars.ResetCandidate = acceptedRunReplaced || leftActiveRun' in final_asl
    assert 'return vars.ResetCandidate' in final_asl
    assert 'sceneName == "Battle"' in final_asl
    assert 'activeScene.IsValid' in final_asl
    assert 'activeScene.Address' in final_asl
    assert 'String.IsNullOrEmpty(sceneName)' in final_asl
    assert 'sceneName.Length > 200' in final_asl
    assert 'activeSceneAfterRead' in final_asl
    assert 'activeSceneAddressAfterRead != activeSceneAddress' in final_asl
    assert 'sceneNameAfterRead != sceneName' in final_asl
    assert final_asl.index('dynamic activeSceneAfterRead') > final_asl.index('float rawIgt')
    assert final_asl.index('dynamic activeSceneAfterRead') < final_asl.index('vars.HaveLastValidSample = true')
    assert 'freshAutoStartObservation' in final_asl
    assert 'if (freshAutoStartObservation)' in final_asl
    assert 'bool timerRunning = timer.CurrentPhase == TimerPhase.Running' in final_asl
    assert 'if (timerRunning && settings.SplitEnabled && settings["splitFloors"])' in final_asl
    assert 'if (timerRunning && settings.SplitEnabled && settings["splitFinal"])' in final_asl
    assert 'preserveFreshStart' in final_asl
    assert 'if (!preserveFreshStart)' in final_asl
    init_body = action_body(final_asl, "init")
    assert init_body.count('game.StartTime') == 1
    assert 'DateTime.UtcNow' in init_body
    assert 'catch (Exception)' in init_body
    assert 'modules.FirstOrDefault' not in init_body
    assert 'SignatureScanner' not in init_body
    assert 'File.ReadAllBytes' not in init_body
    assert 'throw ' not in init_body
    assert 'vars.NativeAttachState = 0' in init_body
    assert 'vars.NativeAttachRejected = false' in init_body
    assert 'vars.Helper.ModuleLoadAttempts = 8' in init_body
    for process_latch in (
        'vars.AutoStartArmed = false',
        'vars.ColdProcessProven = false',
        'vars.ColdStartPending = false',
        'vars.RunActive = false',
        'vars.CapturedRunStart = float.NaN',
        'vars.LastAcceptedRunStart = float.NaN',
        'vars.ExpectedChapter = 1',
        'vars.StartCandidate = false',
        'vars.AutoStartAttempt = false',
        'vars.FreshRunSample = false',
        'vars.ManualStartWaiting = false',
        'vars.ManualStartRejected = false',
        'vars.ResetCandidate = false',
        'vars.ResetForFreshStart = false',
        'vars.PendingSplitKind = 0',
        'vars.FinishSeen = false',
        'vars.FrozenIGT = float.NaN',
        'vars.WasTimerPaused = false',
        'vars.ResultFreezeObserved = false',
        'vars.HaveValidSample = false',
        'vars.LatestScene = ""',
        'vars.HaveLastValidSample = false',
        'vars.LastValidChapter = 0',
        'vars.LastValidIsBattle = false',
    ):
        assert process_latch in init_body, f"init does not reconstruct latch: {process_latch}"
    for forbidden_init_token in ('current.', 'modules.First', 'TimerModel.Reset'):
        assert forbidden_init_token not in init_body, f"init touches unavailable state: {forbidden_init_token}"
    assert 'if (game != null)' in init_body
    assert 'game.StartTime.ToUniversalTime()' in init_body
    assert 'nativeAttachCooldownPolls = 30' in final_asl
    assert 'nativeAttachAttemptCap = 20' in final_asl
    assert 'vars.NativeAttachRetryPolls = nativeAttachCooldownPolls' in final_asl
    assert 'vars.LogTransition("init/new process pending")' in final_asl
    assert 'vars.LogTransition("helper-loaded/native pending")' in final_asl
    assert 'vars.LogTransition("Unity module unavailable")' in final_asl
    assert 'vars.LogTransition("required file unavailable/read failure")' in final_asl
    assert 'vars.LogTransition("supported-build hash mismatch")' in final_asl
    assert 'vars.LogTransition("signature unavailable")' in final_asl
    assert 'vars.LogTransition("native attached")' in final_asl
    assert 'vars.LogTransition("process exit/reset")' in final_asl
    assert 'vars.NativeAttachDiagnostic' in final_asl
    assert 'game.ProcessName' not in final_asl and 'game.Id' not in final_asl
    assert 'vars.NativeFilesVerified' in final_asl
    assert 'hashMismatch' in final_asl
    assert 'vars.NativeAttachState = nativeAttachRejected' in final_asl
    assert final_asl.count('File.ReadAllBytes(path)') == 1
    for expected_hash in EXPECTED_BUILD_HASHES:
        assert expected_hash in final_asl, f"missing supported hash: {expected_hash}"
    assert EXPECTED_HELPER_SHA256 in final_asl
    assert "byte[] helperBytes = File.ReadAllBytes(\"Components/asl-help\")" in final_asl
    assert "Assembly.Load(helperBytes)" in final_asl
    assert "Assembly.Load(File.ReadAllBytes" not in final_asl
    assert 'https://github.com/ero-qt/asl-help/raw/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c/lib/asl-help' in fetch
    assert EXPECTED_HELPER_SHA256 in fetch
    assert 'os.replace(temporary_name, DESTINATION)' in fetch
    assert 'def read_helper() -> bytes' in package
    assert 'helper_data = read_helper()' in package
    assert 'read_package_source' in package
    assert 'OPEN_SUPPORTS_DIR_FD = os.open in os.supports_dir_fd' in package
    assert 'os.O_RDONLY | directory | nofollow' in package
    assert 'os.fstat(source_fd)' in package
    assert 'symlink package source component' in package
    assert 'os.path.lexists(destination)' in package
    assert 'os.path.lexists(backup)' in package
    assert 'collect_entries(helper_data)' in package
    assert 'def stage_output' in package
    assert 'def publish_staged' in package
    assert 'receipt_bytes' in package and 'checksum_bytes' in package
    assert 'python3 fetch_dependencies.py' in package
    assert 'NUNHOLY_RUNTIME_DIR' not in package
    assert 'ZipInfo' in package and 'FIXED_ZIP_TIMESTAMP' in package
    assert 'member_hashes' in package and 'archive.read(name)' in package
    assert 'https://github.com/ero-qt/asl-help/raw/9a17c5ec7108aba1fe1932358703f4e6adaa6b2c/lib/asl-help' in helper_source
    assert 'GNU General Public License, version 3' in helper_source
    assert 'action_body' in adapter and 'Action_update' in adapter
    assert 'Action_onReset' in adapter and 'Action_onStart' in adapter
    assert 'NOT live Windows validation' in adapter
    assert 'from compile_asl_actions import run_gate' in adapter
    assert 'ProductionShutdown' in adapter
    assert 'code.replace("return;", "return null;")' in legacy_gate
    assert 'public dynamic Execute' in legacy_gate
    assert 'four settings constructed and reachable after Execute' in legacy_gate
    assert 'not Windows/LiveSplit RunStartup' in legacy_gate
    assert 'byte-identical' in packaging_test and 'st_mtime_ns' in packaging_test
    assert 'optimized verifier rejects wrong UnityPlayer hash' in NATIVE_NEGATIVE_TEST.read_text(encoding="utf-8")
    assert 'def require(' in native_verifier and 'assert ' not in native_verifier
    assert "48 8B 05 ???????? F2 0F 10 80 90 00 00 00 66 0F 5A C0 C3" in final_asl
    assert "48 8B 05 ???????? F3 0F 10 80 FC 00 00 00 C3" in final_asl
    assert "ReadValue<double>(timeManager + 0x90" in final_asl
    assert "ReadValue<float>(timeManager + 0xFC" in final_asl
    assert "float unityTime = (float)nativeTime" in final_asl
    assert "timeScale <= 0.0001f" in final_asl
    assert "vars.FrozenIGT" in final_asl

    exit_body = action_body(final_asl, "exit")
    for forbidden_exit_token in ("current.", "game.", "modules.First"):
        assert forbidden_exit_token not in exit_body, f"exit reads game-dependent state: {forbidden_exit_token}"
    for reset_token in (
        "vars.SupportedBuild = false",
        "vars.NativeAttachState = 0",
        "vars.NativeAttachRejected = false",
        "vars.NativeFilesVerified = false",
        "vars.NativeAttachAttempts = 0",
        "vars.NativeAttachRetryPolls = 0",
        "vars.TimeManagerSlot = IntPtr.Zero",
        "vars.TimeScaleManagerSlot = IntPtr.Zero",
    ):
        assert reset_token in exit_body, f"exit does not reset native state: {reset_token}"

    forbidden = (
        "WriteProcessMemory",
        "VirtualProtectEx",
        "CreateRemoteThread",
        "ProcessMemory.Write",
        "vars.Helper.Write",
    )
    for token in forbidden:
        assert token not in asl, f"probe contains forbidden write primitive: {token}"
        assert token not in final_asl, f"final ASL contains forbidden write primitive: {token}"

    required_runtime_files = (
        "UnityPlayer.dll",
        "globalgamemanagers",
        "Assembly-CSharp.dll",
        "UnityEngine.CoreModule.dll",
    )
    for name in required_runtime_files:
        assert name in collector, f"collector omits required runtime file: {name}"
    assert "[switch]$IncludeGamePath" in collector
    assert "PRIVATE EVIDENCE" in collector and "NOT PUBLISHABLE" in collector
    assert "[REDACTED; rerun with -IncludeGamePath" in collector
    assert "if ($IncludeGamePath)" in collector

    helper = helper_path()
    if helper is None:
        print("SKIP: local asl-help unavailable; run python3 fetch_dependencies.py")
    else:
        actual_helper_hash = hashlib.sha256(helper.read_bytes()).hexdigest()
        assert actual_helper_hash == EXPECTED_HELPER_SHA256, actual_helper_hash
        print(f"PASS: asl-help SHA-256 {actual_helper_hash}")

    print("PASS: package files present")
    print("PASS: ASL structure, callbacks, build tuple, and managed fields")
    print("PASS: no game-memory write primitives")
    print("PASS: runtime collector scope")


if __name__ == "__main__":
    main()
