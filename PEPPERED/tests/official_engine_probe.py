#!/usr/bin/env python3
"""Run the pinned official LiveSplit ASL parser/compiler and settings gate.

This intentionally keeps the official runtime outside the repository.  Supply an
extracted LiveSplit 1.8.37 directory with --runtime-dir (or
LIVESPLIT_RUNTIME_DIR).  The settings check constructs a real LiveSplitState,
then calls the official ASLScript.RunStartup on a separate ASL containing only
the source script's literal settings.Add calls.  It does not load or fake the
helper startup, and therefore does not claim Linux live-attach compatibility.

Example:
  python3 tests/official_engine_probe.py \
      --runtime-dir /path/to/LiveSplit-1.8.37 \
      --archive /path/to/LiveSplit_1.8.37.zip
"""

from __future__ import print_function

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

EXPECTED_ARCHIVE_SHA256 = (
    "14bc8ef8ded9ef4033fb2f0cb6a152386d393127da18a4de14f096c5347aa991"
)
EXPECTED_RUNTIME_SHA256 = {
    "LiveSplit.exe": "c15a1313bb36f64f631b868cf5bdffb103319c4dff0c53a14d848048a109907f",
    "LiveSplit.Core.dll": "6806e58d3f571fda45fe7ca339ebe23f12f3e7848112fef8678d98f49d1bd40e",
    "Components/LiveSplit.ScriptableAutoSplit.dll": (
        "be7a7068315be64e82f78284df905aed4b966d104468436a9475576bd12fa165"
    ),
    "Components/Irony.dll": "478bbd56ca1763ac23938919d7c680d7ce734320da83e9147a3a9de324a1b999",
}


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def fail(message):
    print("OFFICIAL_ENGINE_PROBE_FAIL\t" + message, file=sys.stderr)
    return 1


def skip(message):
    print("OFFICIAL_ENGINE_PROBE_SKIP\t" + message, file=sys.stderr)
    return 2


def balanced_end(text, opening):
    depth = 0
    state = "code"
    index = opening
    while index < len(text):
        ch = text[index]
        pair = text[index:index + 2]
        if state == "line":
            if ch == "\n":
                state = "code"
        elif state == "block":
            if pair == "*/":
                state = "code"
                index += 1
        elif state in ("string", "char"):
            if ch == "\\":
                index += 1
            elif (state == "string" and ch == '"') or (
                state == "char" and ch == "'"
            ):
                state = "code"
        elif pair == "//":
            state = "line"
            index += 1
        elif pair == "/*":
            state = "block"
            index += 1
        elif ch == '"':
            state = "string"
        elif ch == "'":
            state = "char"
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return index
        index += 1
    raise ValueError("unclosed brace at offset %d" % opening)


def split_arguments(text):
    result = []
    start = 0
    depth = 0
    state = "code"
    index = 0
    while index < len(text):
        ch = text[index]
        if state in ("string", "char"):
            if ch == "\\":
                index += 1
            elif (state == "string" and ch == '"') or (
                state == "char" and ch == "'"
            ):
                state = "code"
        elif ch == '"':
            state = "string"
        elif ch == "'":
            state = "char"
        elif ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        elif ch == "," and depth == 0:
            result.append(text[start:index].strip())
            start = index + 1
        index += 1
    result.append(text[start:].strip())
    return result


def decode_csharp_string(token):
    token = token.strip()
    if len(token) < 2 or token[0] != '"' or token[-1] != '"':
        raise ValueError("expected a literal string, got %r" % token)
    # ASL settings labels are ordinary C# string literals.  JSON decoding
    # covers the same escapes used by the generated source and rejects an
    # expression instead of silently inventing an expected setting.
    return json.loads(token)


def extract_setting_calls(source):
    calls = []
    expected = []
    cursor = 0
    state = "code"
    while cursor < len(source):
        ch = source[cursor]
        pair = source[cursor:cursor + 2]
        if state == "line":
            if ch == "\n":
                state = "code"
            cursor += 1
            continue
        if state == "block":
            if pair == "*/":
                state = "code"
                cursor += 2
            else:
                cursor += 1
            continue
        if state in ("string", "char"):
            if ch == "\\":
                cursor += 2
            elif (state == "string" and ch == '"') or (
                state == "char" and ch == "'"
            ):
                state = "code"
                cursor += 1
            else:
                cursor += 1
            continue
        if pair == "//":
            state = "line"
            cursor += 2
            continue
        if pair == "/*":
            state = "block"
            cursor += 2
            continue
        if ch == '"':
            state = "string"
            cursor += 1
            continue
        if ch == "'":
            state = "char"
            cursor += 1
            continue

        marker = "settings.Add"
        if source.startswith(marker, cursor):
            before = source[cursor - 1] if cursor else " "
            after = source[cursor + len(marker):cursor + len(marker) + 1]
            if (not (before.isalnum() or before == "_")) and after == "(":
                opening = cursor + len(marker)
                if source[opening] != "(":
                    raise ValueError("settings.Add did not open with parenthesis")
                depth = 0
                index = opening
                closing = None
                string_state = "code"
                while index < len(source):
                    c = source[index]
                    pair2 = source[index:index + 2]
                    if string_state == "line":
                        if c == "\\n":
                            string_state = "code"
                    elif string_state == "block":
                        if pair2 == "*/":
                            string_state = "code"
                            index += 1
                    elif string_state in ("string", "char"):
                        if c == "\\":
                            index += 1
                        elif (string_state == "string" and c == '"') or (
                            string_state == "char" and c == "'"
                        ):
                            string_state = "code"
                    elif pair2 == "//":
                        string_state = "line"
                        index += 1
                    elif pair2 == "/*":
                        string_state = "block"
                        index += 1
                    elif c == '"':
                        string_state = "string"
                    elif c == "'":
                        string_state = "char"
                    elif c == "(":
                        depth += 1
                    elif c == ")":
                        depth -= 1
                        if depth == 0:
                            closing = index
                            break
                    index += 1
                if closing is None:
                    raise ValueError("unclosed settings.Add call")
                call_end = closing + 1
                while call_end < len(source) and source[call_end].isspace():
                    call_end += 1
                if call_end >= len(source) or source[call_end] != ";":
                    raise ValueError("settings.Add call is missing semicolon")
                call_end += 1
                call_text = source[cursor:call_end].strip()
                args = split_arguments(source[opening + 1:closing])
                if len(args) not in (3, 4):
                    raise ValueError("settings.Add expected 3 or 4 arguments: %s" % call_text)
                setting_id = decode_csharp_string(args[0])
                default = args[1].strip().lower()
                if default not in ("true", "false"):
                    raise ValueError("settings.Add default is not literal bool: %s" % call_text)
                label = decode_csharp_string(args[2])
                parent = decode_csharp_string(args[3]) if len(args) == 4 else ""
                calls.append(call_text)
                expected.append(
                    {
                        "id": setting_id,
                        "default": default == "true",
                        "parent": parent,
                        "label": label,
                    }
                )
                cursor = call_end
                continue
        cursor += 1
    return calls, expected


def build_settings_only(calls, path):
    path.write_text(
        'state("PEPPERED") { }\nstartup\n{\n'
        + "\n".join("    " + call for call in calls)
        + "\n}\n",
        encoding="utf-8",
    )


def run_command(command, cwd, env):
    return subprocess.run(
        command,
        cwd=str(cwd),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        universal_newlines=True,
    )


def parse_engine_settings(stdout):
    count = None
    ordered_count = None
    valid_state = False
    records = []
    for line in stdout.splitlines():
        fields = line.split("\t")
        if fields[0] == "VALID_LIVESPLIT_STATE" and len(fields) == 3:
            valid_state = fields[1:] == ["Run", "StandardComparisonGeneratorsFactory"]
        elif fields[0] == "SETTINGS_COUNT" and len(fields) == 2:
            count = int(fields[1])
        elif fields[0] == "ORDERED_SETTINGS_COUNT" and len(fields) == 2:
            ordered_count = int(fields[1])
        elif fields[0] == "SETTING" and len(fields) == 6:
            records.append(
                {
                    "id": base64.b64decode(fields[1]).decode("utf-8"),
                    "default": fields[2] == "true",
                    "parent": base64.b64decode(fields[3]).decode("utf-8"),
                    "label": base64.b64decode(fields[4]).decode("utf-8"),
                    "value": fields[5] == "true",
                }
            )
    return valid_state, count, ordered_count, records


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--asl", type=Path, default=Path(__file__).resolve().parents[1] / "PEPPERED.asl")
    parser.add_argument("--runtime-dir", type=Path, default=None)
    parser.add_argument("--archive", type=Path, default=None)
    parser.add_argument("--work-dir", type=Path, default=Path(tempfile.gettempdir()) / "peppered-official-engine-probe")
    args = parser.parse_args(argv)

    runtime = args.runtime_dir or (
        Path(os.environ["LIVESPLIT_RUNTIME_DIR"])
        if os.environ.get("LIVESPLIT_RUNTIME_DIR")
        else None
    )
    if runtime is None:
        return skip("provide --runtime-dir or LIVESPLIT_RUNTIME_DIR")
    runtime = runtime.resolve()
    asl = args.asl.resolve()
    if not asl.is_file():
        return fail("ASL not found: %s" % asl)
    if not runtime.is_dir():
        return skip("official runtime directory not found: %s" % runtime)

    mcs = shutil.which("mcs")
    mono = shutil.which("mono")
    if not mcs or not mono:
        return skip("required commands missing: mcs=%s mono=%s" % (mcs, mono))

    if args.archive is not None:
        archive = args.archive.resolve()
        if not archive.is_file():
            return fail("archive not found: %s" % archive)
        archive_hash = sha256(archive)
        if archive_hash != EXPECTED_ARCHIVE_SHA256:
            return fail("LiveSplit 1.8.37 archive hash mismatch: %s" % archive_hash)
    else:
        archive = None
        archive_hash = "not-supplied"

    runtime_hashes = {}
    for relative, expected_hash in EXPECTED_RUNTIME_SHA256.items():
        path = runtime / relative
        if not path.is_file():
            return fail("pinned runtime file missing: %s" % path)
        actual_hash = sha256(path)
        runtime_hashes[relative] = actual_hash
        if actual_hash != expected_hash:
            return fail("pinned runtime hash mismatch for %s: %s" % (relative, actual_hash))
    core_bytes = (runtime / "LiveSplit.Core.dll").read_bytes()
    if b"1.8.37" not in core_bytes:
        return fail("LiveSplit.Core.dll has no 1.8.37 informational version marker")

    calls, expected = extract_setting_calls(asl.read_text(encoding="utf-8"))
    if not calls:
        return fail("no literal settings.Add calls found in %s" % asl)
    duplicate_ids = sorted(
        setting_id for setting_id in {item["id"] for item in expected}
        if sum(1 for item in expected if item["id"] == setting_id) > 1
    )
    if duplicate_ids:
        return fail("source contains duplicate settings IDs: %s" % ", ".join(duplicate_ids))

    work = args.work_dir.resolve()
    work.mkdir(parents=True, exist_ok=True)
    harness_source = Path(__file__).with_name("official_engine_harness.cs").resolve()
    if not harness_source.is_file():
        return fail("harness source missing: %s" % harness_source)
    harness_exe = work / "official_engine_harness.exe"
    compile_command = [
        mcs,
        "-langversion:4",
        "-r:" + str(runtime / "Components/LiveSplit.ScriptableAutoSplit.dll"),
        "-r:" + str(runtime / "LiveSplit.Core.dll"),
        "-r:" + str(runtime / "Components/Irony.dll"),
        "-r:System.Core",
        "-r:System.Drawing",
        "-r:System.Windows.Forms",
        "-r:Microsoft.CSharp",
        "-out:" + str(harness_exe),
        str(harness_source),
    ]
    compile_result = run_command(compile_command, work, os.environ.copy())
    if compile_result.returncode != 0:
        print(compile_result.stdout, end="")
        print(compile_result.stderr, end="", file=sys.stderr)
        return fail("could not compile official-engine harness")

    settings_only = work / (asl.stem + ".settings-only.asl")
    build_settings_only(calls, settings_only)
    env = os.environ.copy()
    env["MONO_PATH"] = os.pathsep.join(
        [str(runtime / "Components"), str(runtime), env.get("MONO_PATH", "")]
    ).rstrip(os.pathsep)

    parse_result = run_command([mono, str(harness_exe), "parse", str(asl)], runtime, env)
    settings_result = run_command(
        [mono, str(harness_exe), "settings", str(settings_only)], runtime, env
    )

    print("OFFICIAL_ENGINE_PROBE")
    print("runtime_version=1.8.37")
    print("runtime_dir=%s" % runtime)
    print("archive=%s" % (archive if archive is not None else "not-supplied"))
    print("archive_sha256=%s" % archive_hash)
    for relative in sorted(runtime_hashes):
        print("runtime_sha256[%s]=%s" % (relative, runtime_hashes[relative]))
    print("asl=%s" % asl)
    print("settings_only_asl=%s" % settings_only)
    print("settings_expected_count=%d" % len(expected))
    print("full_parser_compiler_exit=%d" % parse_result.returncode)
    if parse_result.stdout:
        print(parse_result.stdout, end="")
    if parse_result.stderr:
        print(parse_result.stderr, end="", file=sys.stderr)

    parse_pass = parse_result.returncode == 0 and "PARSE_COMPILE_OK" in parse_result.stdout
    print("full_parser_compiler=%s" % ("PASS" if parse_pass else "FAIL"))

    print("settings_runstartup_exit=%d" % settings_result.returncode)
    if settings_result.stdout:
        print(settings_result.stdout, end="")
    if settings_result.stderr:
        print(settings_result.stderr, end="", file=sys.stderr)

    settings_pass = False
    if settings_result.returncode == 0:
        try:
            valid_state, count, ordered_count, actual = parse_engine_settings(settings_result.stdout)
            expected_projection = [
                {
                    "id": item["id"],
                    "default": item["default"],
                    "parent": item["parent"],
                    "label": item["label"],
                }
                for item in expected
            ]
            actual_projection = [
                {key: item[key] for key in ("id", "default", "parent", "label")}
                for item in actual
            ]
            value_defaults = all(item["value"] == item["default"] for item in actual)
            settings_pass = (
                valid_state
                and count == len(expected)
                and ordered_count == len(expected)
                and len(actual) == len(expected)
                and actual_projection == expected_projection
                and value_defaults
            )
            print("settings_valid_livesplit_state=%s" % ("PASS" if valid_state else "FAIL"))
            print("settings_dictionary_count=%s" % count)
            print("settings_ordered_count=%s" % ordered_count)
            print("settings_readback=%s" % ("PASS" if settings_pass else "FAIL"))
            if not settings_pass:
                for index, (want, got) in enumerate(zip(expected_projection, actual_projection)):
                    if want != got:
                        print("settings_first_mismatch_index=%d" % index)
                        print("settings_expected=%s" % json.dumps(want, ensure_ascii=False, sort_keys=True))
                        print("settings_actual=%s" % json.dumps(got, ensure_ascii=False, sort_keys=True))
                        break
                if len(actual) != len(expected):
                    print("settings_actual_records=%d" % len(actual))
        except Exception as error:
            print("settings_readback=FAIL (%s)" % error)
    else:
        print("settings_readback=FAIL")

    print("full_helper_startup=NOT_RUN (settings-only gate; no mocked helper startup)")
    overall = parse_pass and settings_pass
    print("overall=%s" % ("PASS" if overall else "FAIL"))
    return 0 if overall else 1


if __name__ == "__main__":
    raise SystemExit(main())
