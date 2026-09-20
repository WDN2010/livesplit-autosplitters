#!/usr/bin/env python3
"""Compile and execute the production Nunholy ASL adapter against fixtures.

The C# fixture below contains the actual extracted ASL action bodies. It is not
an independent Python model and is not the Windows LiveSplit process or a live
game-memory test.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import tempfile
from pathlib import Path

from compile_asl_actions import run_gate

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


def action_body(text: str, name: str) -> str:
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


HARNESS = r'''
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

public enum TimerPhase { NotRunning, Running, Paused, Ended }
public sealed class TimerState { public TimerPhase CurrentPhase; }
public sealed class TimerModel
{
    public object CurrentState;
    public Action ResetEvent;
    public void Reset()
    {
        ((TimerState)CurrentState).CurrentPhase = TimerPhase.NotRunning;
        if (ResetEvent != null) ResetEvent();
    }
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
    public void Add(string key, bool value, string description) { values[key] = value; }
}
public sealed class FakeScene
{
    public string Name = "Title";
    public IntPtr Address = new IntPtr(1);
    public bool IsValid = true;
}
public sealed class FakeScenes { public FakeScene Active = new FakeScene(); }
public sealed class FakeHelper
{
    public bool Loaded = true;
    public bool LoadResult = true;
    public int LoadCalls;
    public int MapPointersCalls;
    public int DisposeCalls;
    public List<string> Lifecycle = new List<string>();
    public FakeScenes Scenes = new FakeScenes();
    public Func<dynamic, bool> TryLoad;
    public uint ModuleLoadTimeout;
    public uint ModuleLoadAttempts;
    public string GameName;
    public bool LoadSceneManager;
    public object this[string key] { get { return null; } set { } }
    public void AlertGameTime() { }
    public void Load()
    {
        LoadCalls++;
        Lifecycle.Add("helper.load");
        Loaded = LoadResult;
    }
    public void MapPointers() { MapPointersCalls++; Lifecycle.Add("helper.map"); }
    public void Dispose()
    {
        DisposeCalls++;
        Lifecycle.Add("helper.dispose");
        Loaded = false;
    }
}
public sealed class GameStub
{
    public double NativeTime;
    public float TimeScale;
    public DateTime StartTimeValue;
    public bool ThrowStartTime;
    public DateTime StartTime
    {
        get
        {
            if (ThrowStartTime) throw new InvalidOperationException("simulated process StartTime failure");
            return StartTimeValue;
        }
    }
    public int ResolverReadCalls;
    public int ResolverDisplacementRead = 0x100;
    public bool InvalidSample;
    public Action AfterRead;
    private void NotifyRead()
    {
        Action callback = AfterRead;
        AfterRead = null;
        if (callback != null) callback();
    }
    public IntPtr ReadPointer(IntPtr address)
    {
        return InvalidSample ? IntPtr.Zero : new IntPtr(1);
    }
    public T ReadValue<T>(IntPtr address) { return ReadValue(address, default(T)); }
    public T ReadValue<T>(IntPtr address, T fallback)
    {
        NotifyRead();
        if (typeof(T) == typeof(int))
        {
            ResolverReadCalls++;
            return (T)(object)ResolverDisplacementRead;
        }
        if (typeof(T) == typeof(double)) return (T)(object)NativeTime;
        if (typeof(T) == typeof(float)) return (T)(object)TimeScale;
        return fallback;
    }
}
public sealed class ModuleStub
{
    public string ModuleName = "UnityPlayer.dll";
    public string FileName = "UnityPlayer.dll";
    public IntPtr BaseAddress = IntPtr.Zero;
    public int ModuleMemorySize = 1;
}
public sealed class SigScanTarget
{
    public Func<GameStub, SignatureScanner, IntPtr, IntPtr> OnFound;
    public SigScanTarget(int offset, string pattern) { }
}
public sealed class SignatureScanner
{
    public static bool ThrowOnScan;
    private readonly GameStub game;
    public SignatureScanner(GameStub game, IntPtr address, int size)
    {
        this.game = game;
    }
    public IntPtr Scan(SigScanTarget target)
    {
        if (ThrowOnScan) throw new InvalidOperationException("simulated signature race");
        if (target == null || target.OnFound == null)
            throw new InvalidOperationException("signature target has no resolver");
        return target.OnFound(game, this, new IntPtr(0x1000));
    }
}
public sealed class Actions
{
    public dynamic vars = new ExpandoObject();
    public dynamic current = new ExpandoObject();
    public dynamic old = new ExpandoObject();
    public SettingsStub settings = new SettingsStub();
    public TimerState timer = new TimerState();
    public List<ModuleStub> modules = new List<ModuleStub>();
    public GameStub game = new GameStub();
    public int refreshRate;
    public int Starts;
    public int Splits;
    public int Resets;
    public List<string> Logs = new List<string>();

    public void print(object value) { Logs.Add(Convert.ToString(value)); }

    public void Setup()
    {
        vars.Helper = new FakeHelper();
        vars.AslStartupUtc = DateTime.UtcNow;
        vars.NativeAttachDiagnostic = "";
        vars.Log = (Action<object>)(value => print(value));
        vars.LogTransition = (Action<string>)(reason =>
        {
            if ((string)vars.NativeAttachDiagnostic == reason) return;
            vars.NativeAttachDiagnostic = reason;
            vars.Log(reason);
        });
        vars.SupportedBuild = true;
        vars.NativeAttachState = 1;
        vars.NativeAttachRejected = false;
        vars.NativeFilesVerified = true;
        vars.NativeAttachAttempts = 0;
        vars.NativeAttachRetryPolls = 0;
        vars.TimeManagerSlot = new IntPtr(1);
        vars.TimeScaleManagerSlot = new IntPtr(1);
        game.StartTimeValue = ((DateTime)vars.AslStartupUtc).AddSeconds(-1.0);
        game.ThrowStartTime = false;
        vars.AutoStartArmed = false;
        vars.RunActive = false;
        vars.CapturedRunStart = float.NaN;
        vars.LastAcceptedRunStart = float.NaN;
        vars.ExpectedChapter = 1;
        vars.StartCandidate = false;
        vars.AutoStartAttempt = false;
        vars.FreshRunSample = false;
        vars.ManualStartWaiting = false;
        vars.ManualStartRejected = false;
        vars.ResetCandidate = false;
        vars.ResetForFreshStart = false;
        vars.PendingSplitKind = 0;
        vars.FinishSeen = false;
        vars.FrozenIGT = float.NaN;
        vars.WasTimerPaused = false;
        vars.ResultFreezeObserved = false;
        vars.HaveValidSample = false;
        vars.LatestScene = "";
        vars.HaveLastValidSample = false;
        vars.LastValidChapter = 0;
        vars.LastValidIsBattle = false;
        settings["autoStart"] = true;
        settings["autoReset"] = true;
        settings["splitFloors"] = true;
        settings["splitFinal"] = true;
        timer.CurrentPhase = TimerPhase.NotRunning;
        var model = new TimerModel { CurrentState = timer };
        model.ResetEvent = () => { Resets++; Action_onReset(); };
        vars.TimerModel = model;
        current.Scene = "";
        current.Chapter = 0;
        current.StartTime = 0.0f;
        current.IsBattle = false;
        current.TargetVampire = IntPtr.Zero;
        current.IGT = 0.0f;
        old.Scene = "";
        old.Chapter = 0;
        old.StartTime = 0.0f;
        old.IsBattle = false;
        ProductionInit();
        vars.SupportedBuild = true;
        vars.NativeAttachState = 1;
        vars.NativeFilesVerified = true;
        vars.TimeManagerSlot = new IntPtr(1);
        vars.TimeScaleManagerSlot = new IntPtr(1);
    }

    public void SetSample(string scene, int chapter, float rawIgt,
        float startTime, bool isBattle, float timeScale, int targetVampire = 0,
        bool valid = true, bool sceneValid = true, int sceneAddress = 1,
        bool changeSceneAfterRead = false)
    {
        old.Scene = current.Scene;
        old.Chapter = current.Chapter;
        old.StartTime = current.StartTime;
        old.IsBattle = current.IsBattle;
        current.Scene = scene;
        current.Chapter = chapter;
        current.StartTime = startTime;
        current.IsBattle = isBattle;
        current.TargetVampire = new IntPtr(targetVampire);
        game.NativeTime = startTime + rawIgt;
        game.TimeScale = timeScale;
        game.InvalidSample = !valid;
        game.AfterRead = null;
        ((FakeHelper)vars.Helper).Scenes.Active.Name = scene;
        ((FakeHelper)vars.Helper).Scenes.Active.Address = new IntPtr(sceneAddress);
        ((FakeHelper)vars.Helper).Scenes.Active.IsValid = sceneValid;
        if (changeSceneAfterRead)
        {
            game.AfterRead = () =>
                ((FakeHelper)vars.Helper).Scenes.Active.Name = "ChangedDuringRead";
        }
    }

    // These wrappers model HelperBase.GenerateCode at the actual boundary:
    // init is explicit body then helper load, update is Loaded gate then
    // MapPointers then explicit body, and exit/shutdown prepend Dispose before
    // the explicit action body.
    public void ProductionInit()
    {
        FakeHelper helper = (FakeHelper)vars.Helper;
        helper.Lifecycle.Add("action.init");
        Action_init();
        helper.Load();
    }

    public bool ProductionUpdate()
    {
        FakeHelper helper = (FakeHelper)vars.Helper;
        if (!helper.Loaded) return false;
        helper.MapPointers();
        helper.Lifecycle.Add("action.update");
        return Action_update();
    }

    public void ProductionExit()
    {
        FakeHelper helper = (FakeHelper)vars.Helper;
        helper.Dispose();
        helper.Lifecycle.Add("action.exit");
        Action_exit();
    }

    public void ProductionShutdown()
    {
        FakeHelper helper = (FakeHelper)vars.Helper;
        helper.Dispose();
        helper.Lifecycle.Add("action.shutdown");
        Action_shutdown();
    }

    // Harness-only model of an ASLScript.DoInit retry boundary. This does not
    // prove the historical user report; it only keeps the candidate safe if a
    // wrapper retries an exception before the explicit init body completes.
    public int SimulateDoInitRetry()
    {
        int attempts = 0;
        bool initialized = false;
        while (!initialized && attempts < 2)
        {
            attempts++;
            try
            {
                if (attempts == 1)
                    throw new InvalidOperationException("unconfirmed init-boundary model");
                ProductionInit();
                initialized = true;
            }
            catch (InvalidOperationException)
            {
            }
        }
        return attempts;
    }

    public void Tick(string scene, int chapter, float rawIgt, float startTime,
        bool isBattle = true, float timeScale = 1.0f, int targetVampire = 0,
        bool valid = true, bool sceneValid = true, int sceneAddress = 1,
        bool changeSceneAfterRead = false)
    {
        SetSample(scene, chapter, rawIgt, startTime, isBattle, timeScale,
            targetVampire, valid, sceneValid, sceneAddress, changeSceneAfterRead);
        bool actionValid = ProductionUpdate();
        if (!actionValid) return;

        if (timer.CurrentPhase == TimerPhase.Running || timer.CurrentPhase == TimerPhase.Paused)
        {
            if (Action_reset() && settings.ResetEnabled)
            {
                vars.TimerModel.Reset();
            }
        }
        if (timer.CurrentPhase == TimerPhase.NotRunning && Action_start() && settings.StartEnabled)
        {
            timer.CurrentPhase = TimerPhase.Running;
            Starts++;
            Action_onStart();
        }
        else if (timer.CurrentPhase == TimerPhase.Running || timer.CurrentPhase == TimerPhase.Paused)
        {
            if (Action_split() && settings.SplitEnabled)
            {
                Splits++;
                Action_onSplit();
            }
        }
    }

    public void ManualStart()
    {
        timer.CurrentPhase = TimerPhase.Running;
        Action_onStart();
    }

__METHODS__
}

public static class AdapterTests
{
    private static int count;
    private static void Check(bool condition, string label)
    {
        count++;
        if (!condition) throw new Exception("FAIL " + label);
    }
    private static Actions New()
    {
        var actions = new Actions();
        actions.Setup();
        return actions;
    }
    private static void Ready(Actions actions)
    {
        actions.vars.SupportedBuild = true;
        actions.vars.NativeAttachState = 1;
        actions.vars.NativeAttachRejected = false;
        actions.vars.NativeFilesVerified = true;
        actions.vars.NativeAttachRetryPolls = 0;
        actions.vars.TimeManagerSlot = new IntPtr(1);
        actions.vars.TimeScaleManagerSlot = new IntPtr(1);
        ((FakeHelper)actions.vars.Helper).Loaded = true;
    }
    private static void Reinit(Actions actions, bool newProcess, bool startTimeFailure = false)
    {
        DateTime nowUtc = DateTime.UtcNow;
        actions.vars.AslStartupUtc = nowUtc.AddSeconds(-1.0);
        actions.game.StartTimeValue = newProcess
            ? nowUtc.AddMilliseconds(-100.0)
            : nowUtc.AddSeconds(-2.0);
        actions.game.ThrowStartTime = startTimeFailure;
        actions.ProductionInit();
        Ready(actions);
    }
    private static void AutoStart(Actions actions, float startTime)
    {
        actions.Tick("Title", 0, 4.0f, startTime, false);
        actions.Tick("Battle", 0, 0.2f, startTime);
        Check(actions.Starts == 1 && actions.timer.CurrentPhase == TimerPhase.Running,
            "actual ASL automatic start callback");
    }

    public static int Main(string[] args)
    {
        try
        {
            // Model the generated lifecycle rather than calling handwritten
            // actions directly: explicit init, helper Load, Loaded gate,
            // MapPointers, then update. The first helper load remains pending
            // so the real update body is not entered early.
            var reattach = New();
            FakeHelper reattachHelper = (FakeHelper)reattach.vars.Helper;
            reattachHelper.LoadResult = false;
            reattachHelper.Loaded = false;
            reattach.modules.Clear();
            reattachHelper.Lifecycle.Clear();
            reattach.ProductionInit();
            Check(reattachHelper.LoadCalls == 2
                && reattachHelper.Lifecycle[0] == "action.init"
                && reattachHelper.Lifecycle[1] == "helper.load",
                "generated init orders explicit action before helper Load");
            Check((int)reattach.vars.NativeAttachState == 0
                && (IntPtr)reattach.vars.TimeManagerSlot == IntPtr.Zero
                && !(bool)reattach.vars.NativeAttachRejected,
                "early init leaves native attach pending and slots clear");
            int mapCallsBeforeLoad = reattachHelper.MapPointersCalls;
            for (int pendingPoll = 0; pendingPoll < 5; pendingPoll++)
                Check(!reattach.ProductionUpdate(),
                    "generated update remains gated while helper Load is pending: " + pendingPoll);
            Check(reattachHelper.MapPointersCalls == mapCallsBeforeLoad,
                "pending helper load never reaches MapPointers or handwritten update");

            // The generated Loaded gate can suppress the only menu sample.
            // A later coherent Battle sample with the observed zero sentinel
            // must recover once managed startTime settles.
            var coldHelper = New();
            FakeHelper coldHelperState = (FakeHelper)coldHelper.vars.Helper;
            coldHelperState.Loaded = false;
            coldHelper.SetSample("Title", 0, 4.0f, 0.0f, false, 1.0f);
            Check(!coldHelper.ProductionUpdate() && coldHelper.Starts == 0
                && !(bool)coldHelper.vars.ColdStartPending,
                "helper gate suppresses cold menu without arming or pending state");
            Ready(coldHelper);
            coldHelper.Tick("Battle", 0, 0.2f, 0.0f);
            Check(coldHelper.Starts == 0
                && (bool)coldHelper.vars.ColdStartPending
                && (bool)coldHelper.vars.FreshRunSample,
                "first cold Battle zero below ten is held as unknown");
            coldHelper.Tick("Battle", 0, 0.2f, 20.0f);
            Check(coldHelper.Starts == 1 && (bool)coldHelper.vars.RunActive,
                "settled startTime recovers helper-gated cold process");

            // A native-pending menu is suppressed by the handwritten attach
            // gate; it must take the same zero -> settled recovery path.
            var coldNative = New();
            coldNative.vars.SupportedBuild = false;
            coldNative.vars.NativeAttachState = 0;
            coldNative.vars.TimeManagerSlot = IntPtr.Zero;
            coldNative.vars.TimeScaleManagerSlot = IntPtr.Zero;
            coldNative.Tick("Title", 0, 4.0f, 0.0f, false);
            Check(coldNative.Starts == 0 && !(bool)coldNative.vars.ColdStartPending,
                "native-pending menu cannot arm cold recovery");
            Ready(coldNative);
            coldNative.Tick("Battle", 0, 15.0f, 0.0f);
            Check(coldNative.Starts == 0 && (bool)coldNative.vars.ColdStartPending
                && !(bool)coldNative.vars.FreshRunSample,
                "first cold Battle zero above ten remains unknown");
            coldNative.Tick("Battle", 0, 0.2f, 20.0f);
            Check(coldNative.Starts == 1 && (bool)coldNative.vars.RunActive,
                "settled startTime recovers native-pending cold process");

            // Cold qualification is process-scoped, not reset-scoped. A reset
            // must not manufacture another "first" observation from the same process.
            var coldReset = New();
            coldReset.Tick("Battle", 0, 15.0f, 0.0f);
            coldReset.Action_onReset();
            coldReset.Tick("Battle", 0, 15.0f, 0.0f);
            coldReset.Tick("Battle", 0, 0.2f, 20.0f);
            Check(coldReset.Starts == 0, "manual reset cannot rearm cold zero evidence");

            var coldChapterDiversion = New();
            coldChapterDiversion.Tick("Battle", 0, 15.0f, 0.0f);
            coldChapterDiversion.Tick("Battle", 1, 20.0f, 20.0f);
            coldChapterDiversion.Tick("Battle", 0, 0.2f, 100.0f);
            Check(coldChapterDiversion.Starts == 0, "higher chapter cancels cold pending evidence");

            var coldModeDiversion = New();
            coldModeDiversion.Tick("Battle", 0, 15.0f, 0.0f);
            coldModeDiversion.Tick("Battle", 0, 0.2f, 20.0f, true, 1.0f, 1);
            coldModeDiversion.Tick("Battle", 0, 0.2f, 100.0f);
            Check(coldModeDiversion.Starts == 0, "non-Classic mode cancels cold pending evidence");

            // A native-invalid gap must clear the current poll's work but keep
            // the durable cold identity evidence for the next valid sample.
            var coldGap = New();
            coldGap.Tick("Battle", 0, 0.2f, 0.0f);
            coldGap.SetSample("Battle", 0, 0.2f, 20.0f, true, 1.0f,
                0, false);
            Check(!coldGap.ProductionUpdate()
                && (bool)coldGap.vars.ColdStartPending
                && coldGap.Starts == 0,
                "invalid settled poll preserves cold pending evidence");
            coldGap.Tick("Battle", 0, 0.2f, 20.0f);
            Check(coldGap.Starts == 1 && (bool)coldGap.vars.RunActive,
                "valid settled poll recovers after cold invalid gap");

            var initRetryModel = New();
            FakeHelper initRetryHelper = (FakeHelper)initRetryModel.vars.Helper;
            Check(initRetryModel.SimulateDoInitRetry() == 2
                && initRetryHelper.LoadCalls == 2
                && (int)initRetryModel.vars.NativeAttachState == 0,
                "unconfirmed DoInit exception-retry model re-enters safe pending init");

            // The helper can become ready one poll before the native module list.
            // More than twenty module misses must remain transient; the
            // diagnostic attempt counter may saturate but cannot reject them.
            reattachHelper.Loaded = true;
            int lifecycleBeforeUpdate = reattachHelper.Lifecycle.Count;
            Check(!reattach.ProductionUpdate(),
                "ready helper tolerates missing native module");
            Check(reattachHelper.Lifecycle[lifecycleBeforeUpdate] == "helper.map"
                && reattachHelper.Lifecycle[lifecycleBeforeUpdate + 1] == "action.update",
                "generated update orders MapPointers before explicit action");
            Check((int)reattach.vars.NativeAttachAttempts == 1
                && (int)reattach.vars.NativeAttachRetryPolls == 30,
                "native miss enters bounded cooldown");
            Check(reattach.Logs.Contains("init/new process pending")
                && reattach.Logs.Contains("helper-loaded/native pending")
                && reattach.Logs.Contains("Unity module unavailable"),
                "attach transition diagnostics name the safe pending/module states");
            int logsAfterModuleMiss = reattach.Logs.Count;
            for (int miss = 1; miss <= 25; miss++)
            {
                for (int poll = 0; poll < 30; poll++)
                    reattach.ProductionUpdate();
                Check(!reattach.ProductionUpdate(),
                    "transient module miss remains retryable: " + miss);
            }
            Check((int)reattach.vars.NativeAttachAttempts == 20
                && (int)reattach.vars.NativeAttachState == 0
                && !(bool)reattach.vars.NativeAttachRejected,
                "transient attach misses saturate diagnostics without rejection");
            Check(reattach.Logs.Count == logsAfterModuleMiss
                && reattach.Logs.FindAll(item => item == "Unity module unavailable").Count == 1,
                "repeated transient misses do not spam diagnostics");

            // Private game binaries are intentionally unavailable to this
            // offline fixture. This flag represents the exact-hash phase having
            // passed, so the extracted production action exercises the real
            // post-readiness signature-binding path.
            reattach.vars.NativeFilesVerified = true;
            reattach.modules.Add(new ModuleStub { BaseAddress = new IntPtr(1) });
            for (int poll = 0; poll < 30; poll++)
                reattach.ProductionUpdate();
            reattach.Tick("Title", 0, 4.0f, 10.0f, false);
            Check((int)reattach.vars.NativeAttachState == 1
                && (IntPtr)reattach.vars.TimeManagerSlot != IntPtr.Zero
                && (IntPtr)reattach.vars.TimeScaleManagerSlot
                    == (IntPtr)reattach.vars.TimeManagerSlot
                && reattach.game.ResolverReadCalls == 2
                && reattach.Logs.Contains("native attached"),
                "ready process binds native slots after repeated early misses and executes both resolvers");

            // A real file-hash mismatch is not treated as startup noise. It
            // permanently rejects this process until exit/init resets state.
            string mismatchRoot = Path.Combine(
                Path.GetTempPath(), "nunholy-asl-mismatch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(mismatchRoot, "Nunholy_Data", "Managed"));
            File.WriteAllBytes(Path.Combine(mismatchRoot, "UnityPlayer.dll"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(
                mismatchRoot, "Nunholy_Data", "Managed", "Assembly-CSharp.dll"), new byte[] { 4 });
            File.WriteAllBytes(Path.Combine(
                mismatchRoot, "Nunholy_Data", "Managed", "UnityEngine.CoreModule.dll"), new byte[] { 5 });
            var mismatch = New();
            mismatch.Action_init();
            ((FakeHelper)mismatch.vars.Helper).Loaded = true;
            mismatch.modules.Add(new ModuleStub
            {
                FileName = Path.Combine(mismatchRoot, "UnityPlayer.dll"),
                BaseAddress = new IntPtr(1),
            });
            try
            {
                Check(!mismatch.ProductionUpdate()
                    && (int)mismatch.vars.NativeAttachState == 2
                    && (bool)mismatch.vars.NativeAttachRejected
                    && (IntPtr)mismatch.vars.TimeManagerSlot == IntPtr.Zero,
                    "real build hash mismatch permanently rejects attach");
            }
            finally
            {
                Directory.Delete(mismatchRoot, true);
            }

            var missingFiles = New();
            missingFiles.ProductionInit();
            missingFiles.modules.Clear();
            missingFiles.modules.Add(new ModuleStub
            {
                FileName = Path.Combine(Path.GetTempPath(),
                    "nunholy-asl-missing-" + Guid.NewGuid().ToString("N"),
                    "UnityPlayer.dll"),
                BaseAddress = new IntPtr(1),
            });
            Check(!missingFiles.ProductionUpdate()
                && (int)missingFiles.vars.NativeAttachState == 0
                && !(bool)missingFiles.vars.NativeAttachRejected
                && missingFiles.Logs.Contains("required file unavailable/read failure"),
                "missing required file stays transient and is diagnosed safely");

            var signatureMiss = New();
            signatureMiss.ProductionInit();
            signatureMiss.vars.NativeFilesVerified = true;
            signatureMiss.modules.Add(new ModuleStub { BaseAddress = new IntPtr(1) });
            SignatureScanner.ThrowOnScan = true;
            try
            {
                Check(!signatureMiss.ProductionUpdate()
                    && (int)signatureMiss.vars.NativeAttachState == 0
                    && !(bool)signatureMiss.vars.NativeAttachRejected
                    && signatureMiss.Logs.Contains("signature unavailable"),
                    "signature exception stays transient and is diagnosed safely");
            }
            finally
            {
                SignatureScanner.ThrowOnScan = false;
            }

            var newProcess = New();
            Reinit(newProcess, true);
            Check((bool)newProcess.vars.ColdProcessProven,
                "init proves process started after ASL startup");
            newProcess.Tick("Battle", 0, 0.2f, 40.0f);
            Check(newProcess.Starts == 1 && (bool)newProcess.vars.RunActive,
                "known new process starts first settled nonzero Battle");

            var existingProcess = New();
            Reinit(existingProcess, false);
            Check(!(bool)existingProcess.vars.ColdProcessProven,
                "existing process has no cold proof");
            existingProcess.Tick("Battle", 0, 0.2f, 40.0f);
            Check(existingProcess.Starts == 0 && !(bool)existingProcess.vars.RunActive,
                "existing-process first settled Battle stays unarmed");

            var startTimeFailure = New();
            Reinit(startTimeFailure, false, true);
            startTimeFailure.Tick("Battle", 0, 0.2f, 40.0f);
            Check(startTimeFailure.Starts == 0
                && !(bool)startTimeFailure.vars.ColdProcessProven
                && !(bool)startTimeFailure.vars.ColdStartPending,
                "StartTime failure has no unguarded cold fallback");

            var startupClockFailure = New();
            startupClockFailure.vars.AslStartupUtc = DateTime.MinValue;
            startupClockFailure.game.StartTimeValue = DateTime.UtcNow.AddMilliseconds(-100.0);
            startupClockFailure.game.ThrowStartTime = false;
            startupClockFailure.ProductionInit();
            Ready(startupClockFailure);
            startupClockFailure.Tick("Battle", 0, 0.2f, 40.0f);
            Check(startupClockFailure.Starts == 0
                && !(bool)startupClockFailure.vars.ColdProcessProven,
                "missing ASL evidence clock has no unguarded cold fallback");

            var lateCold = New();
            lateCold.Tick("Battle", 0, 0.2f, 0.0f);
            lateCold.Tick("Battle", 0, 11.0f, 20.0f);
            Check(lateCold.Starts == 0
                && !(bool)lateCold.vars.ColdStartPending,
                "settled late IGT consumes cold evidence without starting");
            lateCold.Tick("Battle", 0, 0.2f, 20.0f);
            Check(lateCold.Starts == 0,
                "late cold identity cannot replay after clock becomes fresh");

            var coldStartDisabled = New();
            coldStartDisabled.settings.StartEnabled = false;
            coldStartDisabled.Tick("Battle", 0, 0.2f, 0.0f);
            coldStartDisabled.Tick("Battle", 0, 0.2f, 30.0f);
            Check(coldStartDisabled.Starts == 0
                && !(bool)coldStartDisabled.vars.ColdStartPending,
                "disabled Start consumes settled cold observation");
            coldStartDisabled.settings.StartEnabled = true;
            coldStartDisabled.Tick("Battle", 0, 0.3f, 30.0f);
            Check(coldStartDisabled.Starts == 0,
                "re-enabled Start cannot replay settled cold observation");

            var pausedCold = New();
            pausedCold.timer.CurrentPhase = TimerPhase.Paused;
            pausedCold.Tick("Battle", 0, 0.2f, 0.0f);
            Check(!(bool)pausedCold.vars.ColdStartPending,
                "paused timer cannot qualify zero cold evidence");

            var endedCold = New();
            Reinit(endedCold, true);
            endedCold.timer.CurrentPhase = TimerPhase.Ended;
            endedCold.Tick("Battle", 0, 0.2f, 50.0f);
            Check(endedCold.Starts == 1 && endedCold.Resets == 1
                && endedCold.timer.CurrentPhase == TimerPhase.Running
                && !(bool)endedCold.vars.ColdProcessProven
                && !(bool)endedCold.vars.ColdStartPending,
                "Ended result resets once then accepts proven fresh process");

            var manualCold = New();
            manualCold.vars.ColdProcessProven = true;
            manualCold.ManualStart();
            Check((bool)manualCold.vars.ManualStartWaiting
                && !(bool)manualCold.vars.ColdProcessProven,
                "manual start consumes cold process proof");
            manualCold.Tick("Battle", 0, 0.2f, 60.0f);
            Check(manualCold.Starts == 0 && (bool)manualCold.vars.RunActive,
                "manual start binds fresh Battle without auto-start replay");

            var warmZero = New();
            warmZero.Tick("Title", 0, 4.0f, 0.0f, false);
            Check((bool)warmZero.vars.AutoStartArmed,
                "proven menu arms warm path with zero startTime");
            warmZero.Tick("Battle", 0, 0.2f, 0.0f);
            Check(warmZero.Starts == 1 && (bool)warmZero.vars.RunActive,
                "warm menu zero startTime remains a legitimate auto-start");

            // Simulate close/relaunch on the same loaded ASL. Exit must preserve
            // a completed LiveSplit result while clearing every process-scoped
            // latch; the next generated init/load then starts from clean slots.
            reattach.timer.CurrentPhase = TimerPhase.Ended;
            reattach.vars.AutoStartArmed = true;
            reattach.vars.ColdProcessProven = true;
            reattach.vars.ColdStartPending = true;
            reattach.vars.RunActive = true;
            reattach.vars.CapturedRunStart = 12.0f;
            reattach.vars.LastAcceptedRunStart = 12.0f;
            reattach.vars.ExpectedChapter = 5;
            reattach.vars.StartCandidate = true;
            reattach.vars.AutoStartAttempt = true;
            reattach.vars.FreshRunSample = true;
            reattach.vars.ManualStartWaiting = true;
            reattach.vars.ManualStartRejected = true;
            reattach.vars.ResetCandidate = true;
            reattach.vars.ResetForFreshStart = true;
            reattach.vars.PendingSplitKind = 2;
            reattach.vars.FinishSeen = true;
            reattach.vars.FrozenIGT = 40.0f;
            reattach.vars.WasTimerPaused = true;
            reattach.vars.ResultFreezeObserved = true;
            reattach.vars.HaveValidSample = true;
            reattach.vars.LatestScene = "Battle";
            reattach.vars.HaveLastValidSample = true;
            reattach.vars.LastValidChapter = 4;
            reattach.vars.LastValidIsBattle = true;
            reattach.current.Scene = "stale-before-init";
            reattachHelper.Lifecycle.Clear();
            reattach.settings["autoReset"] = false;
            reattach.settings.ResetEnabled = false;
            int resetsBeforeEndedExit = reattach.Resets;
            reattach.ProductionExit();
            Check((int)reattach.vars.NativeAttachState == 0
                && (IntPtr)reattach.vars.TimeManagerSlot == IntPtr.Zero
                && !(bool)reattach.vars.NativeAttachRejected
                && reattachHelper.DisposeCalls == 1
                && reattachHelper.Lifecycle[0] == "helper.dispose"
                && reattachHelper.Lifecycle[1] == "action.exit"
                && reattach.Logs.Contains("process exit/reset")
                && !(bool)reattach.vars.ColdProcessProven
                && !(bool)reattach.vars.ColdStartPending
                && reattach.timer.CurrentPhase == TimerPhase.Ended
                && reattach.Resets == resetsBeforeEndedExit,
                "process exit clears native state without resetting Ended result");

            foreach (TimerPhase unfinished in new[] { TimerPhase.Running, TimerPhase.Paused })
            {
                var forcedExit = New();
                AutoStart(forcedExit, 800.0f + (int)unfinished);
                forcedExit.timer.CurrentPhase = unfinished;
                forcedExit.settings["autoReset"] = false;
                forcedExit.settings.ResetEnabled = false;
                int resetsBeforeForcedExit = forcedExit.Resets;
                forcedExit.ProductionExit();
                Check(forcedExit.timer.CurrentPhase == TimerPhase.NotRunning
                    && forcedExit.Resets == resetsBeforeForcedExit + 1
                    && !(bool)forcedExit.vars.RunActive
                    && float.IsNaN((float)forcedExit.vars.LastAcceptedRunStart),
                    "process exit force-resets unfinished phase despite disabled controls: "
                        + unfinished);
            }

            reattachHelper.LoadResult = true;
            reattach.modules.Clear();
            DateTime relaunchNowUtc = DateTime.UtcNow;
            reattach.vars.AslStartupUtc = relaunchNowUtc.AddSeconds(-1.0);
            reattach.game.StartTimeValue = relaunchNowUtc.AddMilliseconds(-100.0);
            reattach.game.ThrowStartTime = false;
            reattach.ProductionInit();
            Check(reattachHelper.LoadCalls == 3
                && (int)reattach.vars.NativeAttachState == 0
                && (IntPtr)reattach.vars.TimeManagerSlot == IntPtr.Zero
                && (IntPtr)reattach.vars.TimeScaleManagerSlot == IntPtr.Zero
                && !(bool)reattach.vars.RunActive
                && !(bool)reattach.vars.AutoStartArmed
                && (bool)reattach.vars.ColdProcessProven
                && !(bool)reattach.vars.ColdStartPending
                && !(bool)reattach.vars.StartCandidate
                && !(bool)reattach.vars.AutoStartAttempt
                && !(bool)reattach.vars.FreshRunSample
                && !(bool)reattach.vars.ManualStartWaiting
                && !(bool)reattach.vars.ManualStartRejected
                && !(bool)reattach.vars.ResetCandidate
                && !(bool)reattach.vars.ResetForFreshStart
                && (int)reattach.vars.PendingSplitKind == 0
                && !(bool)reattach.vars.FinishSeen
                && !(bool)reattach.vars.WasTimerPaused
                && !(bool)reattach.vars.ResultFreezeObserved
                && !(bool)reattach.vars.HaveValidSample
                && !(bool)reattach.vars.HaveLastValidSample
                && (int)reattach.vars.ExpectedChapter == 1
                && float.IsNaN((float)reattach.vars.CapturedRunStart)
                && float.IsNaN((float)reattach.vars.LastAcceptedRunStart)
                && reattach.current.Scene == "stale-before-init"
                && reattach.timer.CurrentPhase == TimerPhase.Ended
                && reattach.Resets == resetsBeforeEndedExit,
                "re-entered init proves relaunch without touching current or Ended result");
            reattach.vars.NativeFilesVerified = true;
            reattach.modules.Add(new ModuleStub { BaseAddress = new IntPtr(1) });
            reattach.Tick("Title", 0, 4.0f, 20.0f, false);
            Check((int)reattach.vars.NativeAttachState == 1
                && (IntPtr)reattach.vars.TimeManagerSlot != IntPtr.Zero
                && !(bool)reattach.vars.RunActive,
                "second native attach starts clean after process relaunch without ASL reload");

            // shutdown{} is wrapped by asl-help's generated Dispose call. The
            // production action must remain empty so a helper is disposed once,
            // not once by the wrapper and once by handwritten ASL.
            var shutdown = New();
            FakeHelper shutdownHelper = (FakeHelper)shutdown.vars.Helper;
            shutdownHelper.Lifecycle.Clear();
            shutdown.ProductionShutdown();
            Check(shutdownHelper.DisposeCalls == 1
                && shutdownHelper.Lifecycle.Count == 2
                && shutdownHelper.Lifecycle[0] == "helper.dispose"
                && shutdownHelper.Lifecycle[1] == "action.shutdown",
                "generated shutdown cleanup disposes helper exactly once");

            // The actual update/start/onStart bodies honor the base Start gate.
            var startDisabled = New();
            startDisabled.settings.StartEnabled = false;
            startDisabled.Tick("Title", 0, 4.0f, 10.0f, false);
            startDisabled.Tick("Battle", 0, 0.2f, 10.0f);
            Check(startDisabled.Starts == 0 && !startDisabled.vars.RunActive,
                "base Start disabled cannot commit automatic run");
            startDisabled.settings.StartEnabled = true;
            startDisabled.Tick("Battle", 0, 0.3f, 10.0f);
            Check(startDisabled.Starts == 0 && !startDisabled.vars.RunActive,
                "re-enabling Start cannot replay consumed fresh observation");

            // A pending split is tied to the poll that observed it. An invalid
            // poll must discard callback work without clearing run identity or
            // durable last-valid observations.
            var stalePending = New();
            AutoStart(stalePending, 15.0f);
            stalePending.SetSample("Battle", 1, 10.0f, 15.0f, true, 1.0f);
            Check(stalePending.Action_update() && (int)stalePending.vars.PendingSplitKind == 1,
                "valid floor poll queues a pending split");
            stalePending.SetSample("Battle", 1, 10.1f, 15.0f, true, 1.0f, 0, false);
            Check(!stalePending.Action_update()
                && (int)stalePending.vars.PendingSplitKind == 0
                && (bool)stalePending.vars.RunActive
                && (int)stalePending.vars.LastValidChapter == 1,
                "invalid poll clears pending split and preserves run latch");
            stalePending.timer.CurrentPhase = TimerPhase.Running;
            Check(!stalePending.Action_split(),
                "cleared pending split cannot emit after invalid poll");

            // Empty, null, overlong, and invalid scene identities do not arm a
            // run or replace the last complete observation.
            var emptyScene = New();
            emptyScene.Tick("", 0, 4.0f, 16.0f, false);
            emptyScene.Tick("Battle", 0, 0.2f, 16.0f);
            Check(emptyScene.Starts == 0 && (bool)emptyScene.vars.HaveValidSample
                && !(bool)emptyScene.vars.AutoStartArmed,
                "empty active scene fails closed");
            var nullScene = New();
            nullScene.Tick(null, 0, 4.0f, 17.0f, false);
            nullScene.Tick("Battle", 0, 0.2f, 17.0f);
            Check(nullScene.Starts == 0,
                "null active scene fails closed");
            var nullActiveObject = New();
            nullActiveObject.SetSample("Title", 0, 4.0f, 17.5f, false, 1.0f);
            ((FakeHelper)nullActiveObject.vars.Helper).Scenes.Active = null;
            Check(!nullActiveObject.Action_update()
                && !(bool)nullActiveObject.vars.HaveValidSample,
                "null active scene object fails closed");
            var overlongScene = New();
            overlongScene.Tick(new string('x', 201), 0, 4.0f, 18.0f, false);
            overlongScene.Tick("Battle", 0, 0.2f, 18.0f);
            Check(overlongScene.Starts == 0,
                "overlong active scene fails closed");
            var invalidScene = New();
            invalidScene.Tick("Title", 0, 4.0f, 19.0f, false, 1.0f, 0, true, false);
            invalidScene.Tick("Battle", 0, 0.2f, 19.0f);
            Check(invalidScene.Starts == 0,
                "invalid active scene fails closed");

            // A scene identity change during native reads invalidates the poll
            // before it can replace the durable source sample.
            var incoherentScene = New();
            AutoStart(incoherentScene, 19.0f);
            incoherentScene.Tick("Battle", 1, 10.0f, 19.0f,
                true, 1.0f, 0, true, true, 1, true);
            Check(incoherentScene.Splits == 0
                && (int)incoherentScene.vars.ExpectedChapter == 1
                && (int)incoherentScene.vars.LastValidChapter == 0
                && (bool)incoherentScene.vars.HaveLastValidSample
                && !(bool)incoherentScene.vars.HaveValidSample,
                "scene change during read fails closed without poisoning source");
            incoherentScene.Tick("Battle", 1, 10.1f, 19.0f);
            Check(incoherentScene.Splits == 1,
                "coherent scene retry recovers after identity gap");

            var noRetro = New();

            AutoStart(noRetro, 20.0f);
            noRetro.settings.SplitEnabled = false;
            noRetro.Tick("Battle", 1, 2.0f, 20.0f);
            Check(noRetro.vars.ExpectedChapter == 2 && noRetro.Splits == 0,
                "base Split disabled consumes floor observation");
            noRetro.settings.SplitEnabled = true;
            noRetro.Tick("Battle", 1, 2.1f, 20.0f);
            Check(noRetro.Splits == 0,
                "re-enabling Split cannot emit an old floor observation");

            // A native-invalid destination poll still refreshes the framework's
            // current/old values. Every durable floor level must survive that
            // source -> invalid destination -> same destination valid gap.
            var floorGaps = New();
            AutoStart(floorGaps, 40.0f);
            for (int chapter = 1; chapter <= 4; chapter++)
            {
                int splitsBefore = floorGaps.Splits;
                floorGaps.Tick("Battle", chapter, chapter * 10.0f, 40.0f,
                    true, 1.0f, 0, false);
                Check(floorGaps.Splits == splitsBefore
                    && (int)floorGaps.vars.ExpectedChapter == chapter,
                    "invalid floor destination emits nothing: " + chapter);
                floorGaps.Tick("Battle", chapter, chapter * 10.0f + 0.1f, 40.0f);
                Check(floorGaps.Splits == splitsBefore + 1
                    && (int)floorGaps.vars.ExpectedChapter == chapter + 1,
                    "floor survives invalid destination gap: " + chapter);
            }

            // The final result is subject to the same gap: the invalid poll
            // refreshes isBattle=false before update can accept its native data.
            int splitsBeforeFinalGap = floorGaps.Splits;
            floorGaps.Tick("Battle", 4, 40.0f, 40.0f,
                false, 0.0f, 0, false);
            Check(floorGaps.Splits == splitsBeforeFinalGap
                && !(bool)floorGaps.vars.FinishSeen,
                "invalid final destination emits nothing");
            floorGaps.Tick("Battle", 4, 40.1f, 40.0f,
                false, 0.0f);
            Check(floorGaps.Splits == splitsBeforeFinalGap + 1
                && (bool)floorGaps.vars.FinishSeen,
                "final survives invalid destination gap");

            // Paused and other non-Running phases consume floor/final
            // observations but never queue a split that resume could replay.
            var pausedFloor = New();
            AutoStart(pausedFloor, 55.0f);
            pausedFloor.timer.CurrentPhase = TimerPhase.Paused;
            pausedFloor.Tick("Battle", 1, 10.0f, 55.0f);
            Check((int)pausedFloor.vars.ExpectedChapter == 2
                && pausedFloor.Splits == 0
                && (int)pausedFloor.vars.PendingSplitKind == 0,
                "paused floor observation is consumed without queueing");
            pausedFloor.timer.CurrentPhase = TimerPhase.Running;
            pausedFloor.Tick("Battle", 1, 10.1f, 55.0f);
            Check(pausedFloor.Splits == 0
                && (int)pausedFloor.vars.PendingSplitKind == 0,
                "resuming unchanged paused floor does not emit");

            var endedObservation = New();
            AutoStart(endedObservation, 56.0f);
            endedObservation.timer.CurrentPhase = TimerPhase.Ended;
            endedObservation.Tick("Battle", 1, 10.0f, 56.0f);
            Check((int)endedObservation.vars.ExpectedChapter == 2
                && endedObservation.Splits == 0
                && (int)endedObservation.vars.PendingSplitKind == 0,
                "Ended floor observation is consumed without queueing");

            var notRunningObservation = New();
            AutoStart(notRunningObservation, 56.5f);
            notRunningObservation.timer.CurrentPhase = TimerPhase.NotRunning;
            notRunningObservation.Tick("Battle", 1, 10.0f, 56.5f);
            Check((int)notRunningObservation.vars.ExpectedChapter == 2
                && notRunningObservation.Splits == 0
                && (int)notRunningObservation.vars.PendingSplitKind == 0,
                "NotRunning floor observation is consumed without queueing");

            var pausedFinal = New();
            pausedFinal.settings["splitFloors"] = false;
            AutoStart(pausedFinal, 57.0f);
            pausedFinal.Tick("Battle", 1, 10.0f, 57.0f);
            pausedFinal.Tick("Battle", 2, 20.0f, 57.0f);
            pausedFinal.Tick("Battle", 3, 30.0f, 57.0f);
            pausedFinal.Tick("Battle", 4, 40.0f, 57.0f);
            int splitsBeforePausedFinal = pausedFinal.Splits;
            pausedFinal.timer.CurrentPhase = TimerPhase.Paused;
            pausedFinal.Tick("Battle", 4, 40.0f, 57.0f, false, 0.0f);
            Check((bool)pausedFinal.vars.FinishSeen
                && pausedFinal.Splits == splitsBeforePausedFinal
                && (int)pausedFinal.vars.PendingSplitKind == 0,
                "paused final observation is consumed without queueing");
            pausedFinal.timer.CurrentPhase = TimerPhase.Running;
            pausedFinal.Tick("Battle", 4, 40.1f, 57.0f, false, 1.0f);
            Check(pausedFinal.Splits == splitsBeforePausedFinal
                && (int)pausedFinal.vars.PendingSplitKind == 0,
                "resuming unchanged paused final does not emit");

            // Manual Start runs the actual onStart body before any valid sample,
            // then update binds the first fresh Battle sample.
            var manual = New();
            manual.ManualStart();
            Check((bool)manual.vars.ManualStartWaiting && !manual.vars.RunActive,
                "manual start before valid sample waits");
            manual.Tick("Title", 0, 4.0f, 30.0f, false);
            manual.Tick("Battle", 0, 0.2f, 31.0f);
            Check((bool)manual.vars.RunActive && !(bool)manual.vars.ResetCandidate,
                "manual start binds first fresh Battle sample");

            // Lock the paused same-scene restart against the actual extracted
            // update/reset/start/onReset/onStart callback ordering.
            var restart = New();
            AutoStart(restart, 100.0f);
            restart.Tick("Battle", 0, 40.0f, 100.0f, true, 0.0f);
            Check((float)restart.vars.FrozenIGT == 40.0f,
                "old run pause latch captured");
            restart.Tick("Battle", 0, 0.2f, 200.0f, true, 0.0f);
            Check(Math.Abs((float)restart.current.IGT - 0.2f) < 0.0001f,
                "same-scene restart uses new raw IGT");
            Check(restart.Starts == 2 && restart.Resets == 1
                && (bool)restart.vars.RunActive
                && restart.timer.CurrentPhase == TimerPhase.Running,
                "same-scene restart reset then start callbacks");

            // An ordinary reset clears the accepted identity. If the game
            // reuses the same startTime, the next fresh Battle sample is still
            // a new accepted run rather than a duplicate.
            var equalStartReset = New();
            AutoStart(equalStartReset, 700.0f);
            equalStartReset.Tick("Title", 0, 4.0f, 700.0f, false);
            Check(float.IsNaN((float)equalStartReset.vars.LastAcceptedRunStart)
                && equalStartReset.timer.CurrentPhase == TimerPhase.NotRunning,
                "ordinary reset clears accepted start identity");
            equalStartReset.Tick("Battle", 0, 0.2f, 700.0f);
            Check(equalStartReset.Starts == 2
                && (bool)equalStartReset.vars.RunActive,
                "equal-start fresh run can auto-start after ordinary reset");

            // The result edge is one poll: old isBattle=true, current=false,
            // exact zero scale. No second frozen poll is supplied.
            var final = New();
            AutoStart(final, 500.0f);
            final.Tick("Battle", 1, 10.0f, 500.0f);
            final.Tick("Battle", 2, 20.0f, 500.0f);
            final.Tick("Battle", 3, 30.0f, 500.0f);
            final.Tick("Battle", 4, 40.0f, 500.0f);
            int splitsBeforeFinal = final.Splits;
            final.Tick("Battle", 4, 40.0f, 500.0f, false, 0.0f);
            Check(final.Splits == splitsBeforeFinal + 1 && (bool)final.vars.FinishSeen,
                "first-poll final result edge");

            // An Ended result is preserved until a fresh run candidate arrives;
            // update() resets it and the same cycle starts the new run.
            var ended = New();
            AutoStart(ended, 600.0f);
            ended.timer.CurrentPhase = TimerPhase.Ended;
            ended.vars.FinishSeen = true;
            ended.Tick("Battle", 4, 40.0f, 600.0f, false, 0.0f);
            Check(ended.timer.CurrentPhase == TimerPhase.Ended,
                "Ended result remains while result scene is active");
            ended.Tick("Title", 0, 4.0f, 600.0f, false);
            Check(ended.timer.CurrentPhase == TimerPhase.NotRunning,
                "Ended result resets only after leaving Battle");
            ended.Tick("Battle", 0, 0.2f, 601.0f);
            Check(ended.Starts == 2 && ended.timer.CurrentPhase == TimerPhase.Running,
                "Ended reset then fresh start");

            Console.WriteLine("PASS ASL adapter fixtures: " + count
                + " assertions; all action bodies compiled. NOT live Windows validation.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return 1;
        }
    }
}
'''


def main() -> None:
    run_gate()
    text = ASL.read_text(encoding="utf-8")
    methods = "\n\n".join(
        f"public {return_type} Action_{name}()\n{{{action_body(text, name)}\n}}"
        for name, return_type in ACTIONS.items()
    )
    harness = HARNESS.replace("__METHODS__", methods)

    with tempfile.TemporaryDirectory(prefix="nunholy-asl-adapter-") as temp:
        source = Path(temp) / "Adapter.cs"
        output = Path(temp) / "Adapter.exe"
        source.write_text(harness, encoding="utf-8")
        compile_result = subprocess.run(
            [
                "mcs",
                "-target:exe",
                "-sdk:4.8",
                "-r:Microsoft.CSharp",
                f"-out:{output}",
                str(source),
            ],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
        )
        if compile_result.returncode != 0:
            raise AssertionError("ASL adapter compile failed:\n" + compile_result.stdout)
        run_result = subprocess.run(
            ["mono", str(output)],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=30,
        )
        print(run_result.stdout.strip())
        if run_result.returncode != 0:
            raise AssertionError("ASL adapter fixture failed")

    # Keep the machine-readable line deterministic and truthful for callers.
    print(json.dumps({"compiled_actions": list(ACTIONS), "live_windows_runtime": False}))


if __name__ == "__main__":
    main()
