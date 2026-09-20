#!/usr/bin/env python3
"""Compile every real Nunholy action through the legacy ASL compiler shape.

Scriptable Auto Splitter 1.8.17 performs a global ``return;`` -> ``return
null;`` rewrite before placing an action in a dynamic ``Execute`` method.  The
fixture below deliberately models that boundary instead of compiling extracted
bodies as typed C# methods.
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASL = ROOT / "Nunholy_IGT.asl"
ACTIONS = {
    "startup": "void",
    "init": "void",
    "update": "bool",
    "start": "bool",
    "gameTime": "TimeSpan?",
    "split": "bool",
    "reset": "bool",
    "onStart": "void",
    "onSplit": "void",
    "onReset": "void",
    "isLoading": "bool",
    "exit": "void",
    "shutdown": "void",
}
EXPECTED_SETTINGS = ("autoStart", "autoReset", "splitFloors", "splitFinal")
HELPER_CANDIDATES = (
    ROOT / "Components" / "asl-help",
    ROOT.parent / "PEPPERED" / "Components" / "asl-help",
)

# This is intentionally the same action boundary as ASLMethod.Execute at the
# pinned Scriptable Auto Splitter 1.8.17 source: dynamic return, memory/module
# locals, user code, and a trailing null return.
HARNESS = r'''
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LiveSplit.ComponentUtil;
using LiveSplit.Model;
using LiveSplit.Options;

namespace LiveSplit.Model
{
    public enum TimerPhase { NotRunning, Running, Paused, Ended }
    public sealed class LiveSplitState
    {
        public TimerPhase CurrentPhase;
    }
    public sealed class TimerModel
    {
        public object CurrentState;
        public void Reset() { }
    }
}

namespace LiveSplit.ComponentUtil
{
    public sealed class ModuleStub
    {
        public string ModuleName = "UnityPlayer.dll";
        public string FileName = "UnityPlayer.dll";
        public IntPtr BaseAddress = IntPtr.Zero;
        public int ModuleMemorySize = 1;
    }
    public static class ProcessExtensions
    {
        public static List<ModuleStub> ModulesWow64Safe(this Process game)
        {
            return new List<ModuleStub>();
        }
        public static T ReadValue<T>(this Process game, IntPtr address)
        {
            return default(T);
        }
        public static T ReadValue<T>(this Process game, IntPtr address, T fallback)
        {
            return fallback;
        }
        public static IntPtr ReadPointer(this Process game, IntPtr address)
        {
            return IntPtr.Zero;
        }
    }
    public sealed class SigScanTarget
    {
        public Func<Process, SignatureScanner, IntPtr, IntPtr> OnFound;
        public SigScanTarget(int offset, string pattern) { }
    }
    public sealed class SignatureScanner
    {
        public SignatureScanner(Process game, IntPtr address, int size) { }
        public IntPtr Scan(SigScanTarget target) { return IntPtr.Zero; }
    }
}

namespace LiveSplit.Options
{
}

public sealed class SettingsStub
{
    private readonly Dictionary<string, bool> values = new Dictionary<string, bool>();
    public bool StartEnabled = true;
    public bool SplitEnabled = true;
    public bool ResetEnabled = true;
    public bool this[string key]
    {
        get { return values.ContainsKey(key) && values[key]; }
        set { values[key] = value; }
    }
    public void Add(string key, bool value, string description)
    {
        values[key] = value;
    }
    public bool Has(string key) { return values.ContainsKey(key); }
    public int Count { get { return values.Count; } }
}

public sealed class FakeHelper
{
    public string GameName;
    public bool LoadSceneManager;
    public Func<dynamic, bool> TryLoad;
    public uint ModuleLoadTimeout;
    public uint ModuleLoadAttempts;
    public object this[string key] { get { return null; } set { } }
    public void AlertGameTime() { }
}

public class CompiledScript
{
    public string version;
    public double refreshRate;
    void print(string s) { }

    public dynamic Execute(LiveSplitState timer, dynamic old, dynamic current,
        dynamic vars, Process game, dynamic settings)
    {
        var memory = game;
        var modules = game != null ? game.ModulesWow64Safe() : null;
        // USER_CODE_START
        __USER_CODE__
        return null;
    }
}

public static class LegacyCompilerFixture
{
    public static int Main(string[] args)
    {
        dynamic vars = new ExpandoObject();
        vars.Helper = new FakeHelper();
        dynamic old = new ExpandoObject();
        dynamic current = new ExpandoObject();
        var settings = new SettingsStub();
        var script = new CompiledScript();
        script.Execute(new LiveSplitState(), old, current, vars, null, settings);

        if ("__ACTION__" == "startup")
        {
            string[] expected = { "autoStart", "autoReset", "splitFloors", "splitFinal" };
            foreach (string key in expected)
                if (!settings.Has(key))
                    throw new Exception("startup did not construct setting: " + key);
            if (settings.Count != expected.Length)
                throw new Exception("startup constructed an unexpected setting count: " + settings.Count);
            Console.WriteLine("PASS legacy startup fixture: four settings constructed and reachable after Execute");
        }
        return 0;
    }
}
'''


def action_body(text: str, name: str) -> str:
    """Extract one ASL action while ignoring braces in comments/literals."""
    match = re.search(rf"(?m)^{re.escape(name)}\s*\n\{{", text)
    assert match, f"missing action: {name}"
    opening = text.find("{", match.start())
    depth = 0
    state = "code"
    index = opening
    while index < len(text):
        char = text[index]
        pair = text[index : index + 2]
        if state == "line":
            if char == "\n":
                state = "code"
        elif state == "block":
            if pair == "*/":
                state = "code"
                index += 1
        elif state in {"string", "char"}:
            if char == "\\":
                index += 1
            elif (state == "string" and char == '"') or (
                state == "char" and char == "'"
            ):
                state = "code"
        elif pair == "//":
            state = "line"
            index += 1
        elif pair == "/*":
            state = "block"
            index += 1
        elif char == '"':
            state = "string"
        elif char == "'":
            state = "char"
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[opening + 1 : index]
        index += 1
    raise AssertionError(f"unclosed action: {name}")


def legacy_rewrite(code: str) -> str:
    """Apply the exact global rewrite made by ASLMethod 1.8.17."""
    return code.replace("return;", "return null;")


def offline_startup_body(code: str) -> str:
    """Skip only helper assembly activation unavailable outside LiveSplit."""
    marker = 'Assembly.Load(helperBytes).CreateInstance("Unity");'
    assert marker in code, "startup helper activation marker moved"
    return code.replace(
        marker,
        "// Offline fixture: helper activation needs LiveSplit.Core and is not RunStartup.",
    )


def helper_path() -> Path:
    for candidate in HELPER_CANDIDATES:
        if candidate.is_file():
            return candidate
    searched = ", ".join(str(path) for path in HELPER_CANDIDATES)
    raise AssertionError("asl-help fixture binary unavailable; searched: " + searched)


def compile_action(
    temp: Path, name: str, body: str, *, run: bool, helper: Path
) -> None:
    rewritten = legacy_rewrite(body)
    source = temp / f"{name}.cs"
    output = temp / f"{name}.dll"
    source.write_text(
        HARNESS.replace("__USER_CODE__", rewritten).replace("__ACTION__", name),
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            "mcs",
            "-target:library",
            "-sdk:4.8",
            "-r:System.dll",
            "-r:System.Core.dll",
            "-r:System.Data.dll",
            "-r:System.Data.DataSetExtensions.dll",
            "-r:System.Drawing.dll",
            "-r:System.Windows.Forms.dll",
            "-r:System.Xml.dll",
            "-r:System.Xml.Linq.dll",
            "-r:Microsoft.CSharp.dll",
            f"-out:{output}",
            str(source),
        ],
        cwd=temp,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    if result.returncode != 0:
        raise AssertionError(
            f"exact legacy Execute compile failed for {name}:\n{result.stdout}"
        )
    if not output.is_file() or output.stat().st_size == 0:
        raise AssertionError(f"compiler produced no output for {name}")

    if run:
        # The real startup body above was compiled unchanged. Running the
        # actual helper assembly would require LiveSplit.Core, which is not a
        # claim this offline repository can make. Run a second exact-wrapper
        # fixture that skips only Assembly.Load(...), then assert settings are
        # constructed by the compiled startup code.
        fixture_source = temp / "startup-settings-fixture.cs"
        fixture_output = temp / "startup-settings-fixture.exe"
        fixture_source.write_text(
            HARNESS.replace("__USER_CODE__", legacy_rewrite(offline_startup_body(body)))
            .replace("__ACTION__", name),
            encoding="utf-8",
        )
        fixture_result = subprocess.run(
            [
                "mcs",
                "-target:exe",
                "-sdk:4.8",
                "-r:System.dll",
                "-r:System.Core.dll",
                "-r:System.Data.dll",
                "-r:System.Data.DataSetExtensions.dll",
                "-r:System.Drawing.dll",
                "-r:System.Windows.Forms.dll",
                "-r:System.Xml.dll",
                "-r:System.Xml.Linq.dll",
                "-r:Microsoft.CSharp.dll",
                f"-out:{fixture_output}",
                str(fixture_source),
            ],
            cwd=temp,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
        )
        if fixture_result.returncode != 0:
            raise AssertionError(
                "offline startup settings fixture did not compile:\n"
                + fixture_result.stdout
            )
        components = temp / "Components"
        components.mkdir()
        shutil.copyfile(helper, components / "asl-help")
        executed = subprocess.run(
            ["mono", str(fixture_output)],
            cwd=temp,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=30,
        )
        if executed.returncode != 0:
            raise AssertionError("offline startup settings fixture failed:\n" + executed.stdout)
        print(executed.stdout.strip())


def run_gate() -> list[str]:
    text = ASL.read_text(encoding="utf-8")
    helper = helper_path()
    with tempfile.TemporaryDirectory(prefix="nunholy-asl-legacy-") as directory:
        temp = Path(directory)
        for name in ACTIONS:
            compile_action(temp, name, action_body(text, name), run=name == "startup", helper=helper)
    names = list(ACTIONS)
    print(f"PASS exact legacy ASL compiler: {len(names)} real actions compiled in Execute")
    print("PASS scope: offline CodeDOM-shaped fixture only; not Windows/LiveSplit RunStartup")
    return names


def main() -> None:
    names = run_gate()
    print(
        json.dumps(
            {
                "compiled_actions": names,
                "compiler_wrapper": "Scriptable Auto Splitter 1.8.17 ASLMethod.Execute shape",
                "global_return_rewrite": "return; -> return null;",
                "live_windows_runtime": False,
            },
            sort_keys=True,
        )
    )


if __name__ == "__main__":
    main()
