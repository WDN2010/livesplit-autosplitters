// Nunholy AutoSplitter v1.2.0-rc4-cold-start-manual-test
state("Nunholy") { }

startup
{
    const string helperHash = "c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5";
    byte[] helperBytes = File.ReadAllBytes("Components/asl-help");
    using (var helperSha = System.Security.Cryptography.SHA256.Create())
    {
        string actualHelperHash = BitConverter.ToString(helperSha.ComputeHash(helperBytes))
            .Replace("-", "").ToLowerInvariant();
        if (actualHelperHash != helperHash)
            throw new InvalidDataException(
                "Nunholy: asl-help SHA-256 mismatch: " + actualHelperHash
                + "; expected: " + helperHash);
    }
    // Hash and load the exact same byte[] so a replacement cannot pass the
    // hash check and then provide different code to the AppDomain.
    Assembly.Load(helperBytes).CreateInstance("Unity");

    vars.Helper.GameName = "Nunholy AutoSplitter";
    vars.Helper.LoadSceneManager = true;
    vars.Helper.AlertGameTime();

    refreshRate = 60;
    // Capture the ASL load boundary once. init{} uses this only to prove that
    // the attached process started after the splitter was loaded; a failed or
    // unavailable clock/process read remains fail-closed.
    vars.AslStartupUtc = DateTime.UtcNow;
    vars.NativeAttachDiagnostic = "";
    vars.Log = (Action<object>)(value => print("[Nunholy] " + value));
    vars.LogTransition = (Action<string>)(reason =>
    {
        if ((string)vars.NativeAttachDiagnostic != reason)
        {
            vars.NativeAttachDiagnostic = reason;
            vars.Log(reason);
        }
    });
    // ASL's start action is not called while LiveSplit is in TimerPhase.Ended.
    // TimerModel lets update() reset that phase exactly when a fresh run begins.
    vars.TimerModel = new TimerModel { CurrentState = timer };
    vars.SupportedBuild = false;
    vars.NativeAttachState = 0;
    vars.NativeAttachRejected = false;
    vars.NativeFilesVerified = false;
    vars.NativeAttachAttempts = 0;
    vars.NativeAttachRetryPolls = 0;
    vars.TimeManagerSlot = IntPtr.Zero;
    vars.TimeScaleManagerSlot = IntPtr.Zero;
    vars.AutoStartArmed = false;
    vars.ColdProcessProven = false;
    vars.ColdStartPending = false;
    vars.ColdStartObservationOpen = false;
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

    settings.Add("autoStart", true, "Auto-start on a fresh Classic/Any% run");
    settings.Add("autoReset", true, "Auto-reset after death or leaving the run");
    settings.Add("splitFloors", true, "Split when entering each next floor (1-4)");
    settings.Add("splitFinal", true, "Split after the fifth boss at the final result gate");
}

init
{
    // Native attachment is deliberately deferred until the helper's generated
    // Load/Loaded gate has completed. Process discovery can happen before the
    // UnityPlayer module, managed files, or native text are ready; init must
    // not make that transient state fatal.
    vars.SupportedBuild = false;
    vars.NativeAttachState = 0; // 0 = pending, 1 = attached, 2 = rejected
    vars.NativeAttachRejected = false;
    vars.NativeFilesVerified = false;
    vars.NativeAttachAttempts = 0;
    vars.NativeAttachRetryPolls = 0;
    vars.NativeAttachDiagnostic = "";
    vars.LogTransition("init/new process pending");
    vars.ColdProcessProven = false;
    vars.ColdStartPending = false;
    vars.ColdStartObservationOpen = false;
    // This is intentionally init-only. A valid nonzero StartTime may be
    // enough for a process proven newer than the ASL load, but never for an
    // ordinary attach to an already-running process. Process.StartTime is a
    // local DateTime, so compare its guarded UTC conversion to the captured
    // ASL clock and reject impossible/future evidence.
    try
    {
        if (game != null)
        {
            DateTime aslStartupUtc = (DateTime)vars.AslStartupUtc;
            DateTime processStartUtc = game.StartTime.ToUniversalTime();
            DateTime nowUtc = DateTime.UtcNow;
            if (aslStartupUtc != DateTime.MinValue
                && processStartUtc != DateTime.MinValue
                && processStartUtc > aslStartupUtc
                && processStartUtc <= nowUtc)
                vars.ColdProcessProven = true;
        }
    }
    catch (Exception)
    {
        vars.ColdProcessProven = false;
    }
    vars.TimeManagerSlot = IntPtr.Zero;
    vars.TimeScaleManagerSlot = IntPtr.Zero;
    // Reconstruct every process-scoped sample/run latch. The LiveSplit timer
    // itself is intentionally not touched here: an Ended result belongs to
    // LiveSplit, not to the departing game's process, and must survive init
    // re-entry until update observes a proven fresh-run transition.
    vars.AutoStartArmed = false;
    vars.ColdStartPending = false;
    vars.ColdStartObservationOpen = true;
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

    // Give asl-help a bounded startup window for a process whose module list is
    // still settling. These are helper lifecycle controls, not a replacement
    // for the native attach retry below.
    vars.Helper.ModuleLoadTimeout = 1500;
    vars.Helper.ModuleLoadAttempts = 8;

    vars.Helper.TryLoad = (Func<dynamic, bool>)(mono =>
    {
        var mission = mono["MissionManager"];
        var gameManager = mono["GameManager"];
        vars.Helper["StartTime"] = mono.Make<float>(mission, "startTime");
        vars.Helper["Chapter"] = mono.Make<int>(mission, "Inst", "curChapter");
        vars.Helper["IsBattle"] = mono.Make<bool>(mission, "Inst", "isBattle");
        vars.Helper["TargetVampire"] = mono.Make<IntPtr>(gameManager, "instance", "targetVampire");
        return true;
    });
}

update
{
    // A pending action is valid only for the complete sample that queued it.
    // Clear it before every new poll so an invalid poll cannot leave callback
    // work behind for a later, unrelated sample.
    vars.PendingSplitKind = 0;
    vars.StartCandidate = false;
    vars.ResetCandidate = false;
    vars.HaveValidSample = false;
    vars.FreshRunSample = false;

    const int nativeAttachReady = 1;
    const int nativeAttachRejected = 2;
    const int nativeAttachAttemptCap = 20;
    const int nativeAttachCooldownPolls = 30;

    if ((int)vars.NativeAttachState == nativeAttachRejected)
        return false;

    if ((int)vars.NativeAttachState != nativeAttachReady)
    {
        int retryPolls = (int)vars.NativeAttachRetryPolls;
        if (retryPolls > 0)
        {
            vars.NativeAttachRetryPolls = retryPolls - 1;
            return false;
        }

        if ((string)vars.NativeAttachDiagnostic == "init/new process pending")
            vars.LogTransition("helper-loaded/native pending");

        int attempts = (int)vars.NativeAttachAttempts;
        if (attempts < nativeAttachAttemptCap)
            vars.NativeAttachAttempts = attempts + 1;

        bool transientFailure = false;
        bool hashMismatch = false;
        bool requiredFileFailure = false;
        bool signatureFailure = false;
        IntPtr timeManagerSlot = IntPtr.Zero;
        IntPtr timeScaleManagerSlot = IntPtr.Zero;
        try
        {
            var unityPlayer = modules.FirstOrDefault(module =>
                module.ModuleName.Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase));
            if (unityPlayer == null
                || String.IsNullOrEmpty(unityPlayer.FileName)
                || unityPlayer.BaseAddress == IntPtr.Zero
                || unityPlayer.ModuleMemorySize <= 0)
            {
                transientFailure = true;
                vars.LogTransition("Unity module unavailable");
            }
            else
            {
                string gameRoot = Path.GetDirectoryName(unityPlayer.FileName);
                if (String.IsNullOrEmpty(gameRoot))
                    gameRoot = ".";

                if (!(bool)vars.NativeFilesVerified)
                {
                    const string unityPlayerHash = "208fd2c5bd25ff300f2dad8bdf8a3a04402eb8875b16719981ff24b8bc9c1b0d";
                    const string assemblyCSharpHash = "2fdb400a70f217b22e7fef27f9848be8b90e622479040313aa1ed8afc88598eb";
                    const string coreModuleHash = "39a749674ededf51a44fcb7d08b35ffca932c11f73493df3b97a12f8f7af9bca";
                    var expectedHashes = new Dictionary<string, string>
                    {
                        { "UnityPlayer.dll", unityPlayerHash },
                        { "Nunholy_Data/Managed/Assembly-CSharp.dll", assemblyCSharpHash },
                        { "Nunholy_Data/Managed/UnityEngine.CoreModule.dll", coreModuleHash },
                    };

                    foreach (var item in expectedHashes)
                    {
                        string path = Path.Combine(gameRoot, item.Key.Replace('/', Path.DirectorySeparatorChar));
                        bool fileExists = false;
                        try
                        {
                            fileExists = File.Exists(path);
                        }
                        catch (Exception)
                        {
                            requiredFileFailure = true;
                        }
                        if (!fileExists)
                        {
                            requiredFileFailure = true;
                            break;
                        }

                        byte[] fileBytes;
                        try
                        {
                            fileBytes = File.ReadAllBytes(path);
                        }
                        catch (Exception)
                        {
                            requiredFileFailure = true;
                            break;
                        }
                        try
                        {
                            string actualHash;
                            using (var sha256 = System.Security.Cryptography.SHA256.Create())
                            {
                                actualHash = BitConverter.ToString(sha256.ComputeHash(fileBytes))
                                    .Replace("-", "").ToLowerInvariant();
                            }
                            if (actualHash != item.Value)
                            {
                                hashMismatch = true;
                                break;
                            }
                        }
                        catch (Exception)
                        {
                            requiredFileFailure = true;
                            break;
                        }
                    }
                    if (requiredFileFailure)
                    {
                        transientFailure = true;
                        vars.LogTransition("required file unavailable/read failure");
                    }
                    else if (hashMismatch)
                    {
                        vars.LogTransition("supported-build hash mismatch");
                    }
                    else
                        vars.NativeFilesVerified = true;
                }

                if (!transientFailure && !hashMismatch)
                {
                    try
                    {
                        // Unity 2022.3.56f1, x64. Matching-PDB Time.time wrapper RVA: 0x110E40.
                        var target = new SigScanTarget(3,
                            "48 8B 05 ???????? F2 0F 10 80 90 00 00 00 66 0F 5A C0 C3");
                        target.OnFound = (process, signatureScanner, displacement) =>
                            displacement + 4 + process.ReadValue<int>(displacement);

                        var moduleScanner = new SignatureScanner(
                            game,
                            unityPlayer.BaseAddress,
                            unityPlayer.ModuleMemorySize
                        );
                        timeManagerSlot = moduleScanner.Scan(target);

                        // Same native TimeManager, exact Time.timeScale getter. The
                        // independent signature also proves the manager slot and
                        // the +0xFC float field.
                        var timeScaleTarget = new SigScanTarget(3,
                            "48 8B 05 ???????? F3 0F 10 80 FC 00 00 00 C3");
                        timeScaleTarget.OnFound = (process, signatureScanner, displacement) =>
                            displacement + 4 + process.ReadValue<int>(displacement);
                        timeScaleManagerSlot = moduleScanner.Scan(timeScaleTarget);
                        if (timeManagerSlot == IntPtr.Zero
                            || timeScaleManagerSlot == IntPtr.Zero
                            || timeScaleManagerSlot != timeManagerSlot)
                            signatureFailure = true;
                    }
                    catch (Exception)
                    {
                        signatureFailure = true;
                    }
                    if (signatureFailure)
                    {
                        transientFailure = true;
                        vars.LogTransition("signature unavailable");
                    }
                }
            }
        }
        catch (Exception)
        {
            // Module enumeration, file publication, and native scanning can
            // race process startup. Keep the failure path content-free,
            // diagnose it as a transient native-signature failure, and retry
            // instead of killing the ASL.
            transientFailure = true;
            if (!hashMismatch && !requiredFileFailure)
            {
                signatureFailure = true;
                vars.LogTransition("signature unavailable");
            }
        }

        if (hashMismatch)
        {
            vars.TimeManagerSlot = IntPtr.Zero;
            vars.TimeScaleManagerSlot = IntPtr.Zero;
            vars.SupportedBuild = false;
            vars.NativeAttachState = nativeAttachRejected;
            vars.NativeAttachRejected = true;
            return false;
        }

        if (transientFailure)
        {
            vars.TimeManagerSlot = IntPtr.Zero;
            vars.TimeScaleManagerSlot = IntPtr.Zero;
            vars.SupportedBuild = false;
            vars.NativeAttachRetryPolls = nativeAttachCooldownPolls;
            return false;
        }

        vars.TimeManagerSlot = timeManagerSlot;
        vars.TimeScaleManagerSlot = timeScaleManagerSlot;
        vars.SupportedBuild = true;
        vars.NativeAttachState = nativeAttachReady;
        vars.NativeAttachRejected = false;
        vars.LogTransition("native attached");
    }

    if (!vars.SupportedBuild
        || (int)vars.NativeAttachState != nativeAttachReady
        || vars.TimeManagerSlot == IntPtr.Zero
        || vars.TimeScaleManagerSlot == IntPtr.Zero)
        return false;

    if (!vars.SupportedBuild || !vars.Helper.Loaded
        || vars.TimeManagerSlot == IntPtr.Zero
        || vars.TimeScaleManagerSlot == IntPtr.Zero)
        return false;

    if (vars.Helper.Scenes == null)
        return false;

    dynamic activeScene = vars.Helper.Scenes.Active;
    if (activeScene == null || !(bool)activeScene.IsValid)
        return false;

    IntPtr activeSceneAddress = (IntPtr)activeScene.Address;
    string sceneName = (string)activeScene.Name;
    if (activeSceneAddress == IntPtr.Zero
        || String.IsNullOrEmpty(sceneName)
        || sceneName.Length > 200)
        return false;

    var timeManager = game.ReadPointer((IntPtr)vars.TimeManagerSlot);
    if (timeManager == IntPtr.Zero)
        return false;

    double nativeTime = game.ReadValue<double>(timeManager + 0x90, double.NaN);
    if (double.IsNaN(nativeTime) || double.IsInfinity(nativeTime) || nativeTime < 0.0)
        return false;

    float timeScale = game.ReadValue<float>(timeManager + 0xFC, float.NaN);
    if (float.IsNaN(timeScale) || float.IsInfinity(timeScale) || timeScale < 0f)
        return false;

    // Preserve ResultCanvas semantics: float Time.time - float startTime.
    float unityTime = (float)nativeTime;
    float sampleStartTime = (float)current.StartTime;
    int sampleChapter = (int)current.Chapter;
    bool sampleIsBattle = (bool)current.IsBattle;
    IntPtr sampleTargetVampire = (IntPtr)current.TargetVampire;
    float rawIgt = unityTime - sampleStartTime;
    if (float.IsNaN(rawIgt) || float.IsInfinity(rawIgt))
        return false;

    // Unity may switch the active scene while the managed/native fields above
    // are being read. Revalidate the bounded scene identity before changing
    // pause/run latches or committing any observation.
    if (!vars.Helper.Loaded || vars.Helper.Scenes == null)
        return false;
    dynamic activeSceneAfterRead = vars.Helper.Scenes.Active;
    if (activeSceneAfterRead == null || !(bool)activeSceneAfterRead.IsValid)
        return false;
    IntPtr activeSceneAddressAfterRead = (IntPtr)activeSceneAfterRead.Address;
    string sceneNameAfterRead = (string)activeSceneAfterRead.Name;
    if (activeSceneAddressAfterRead == IntPtr.Zero
        || String.IsNullOrEmpty(sceneNameAfterRead)
        || sceneNameAfterRead.Length > 200
        || activeSceneAddressAfterRead != activeSceneAddress
        || sceneNameAfterRead != sceneName)
        return false;

    if (sceneName != "Battle")
        vars.AutoStartArmed = true;

    // A zero managed startTime is unknown, not fresh. It can qualify a cold
    // process only when the first coherent observation is a Classic/Any%
    // chapter-zero Battle sample while the timer is able to begin a run.
    // Menu observations continue to use the existing warm arm, including a
    // legitimate menu sample whose startTime is zero.
    bool firstColdObservation = (bool)vars.ColdStartObservationOpen;
    vars.ColdStartObservationOpen = false;
    bool coldPhaseEligible = timer.CurrentPhase == TimerPhase.NotRunning
        || timer.CurrentPhase == TimerPhase.Ended;
    bool coldZeroObservation = coldPhaseEligible
        && firstColdObservation
        && sceneName == "Battle"
        && sampleChapter == 0
        && sampleTargetVampire == IntPtr.Zero
        && sampleStartTime == 0f;
    if (coldZeroObservation)
        vars.ColdStartPending = true;
    else if (!coldPhaseEligible || sceneName != "Battle"
        || sampleChapter != 0 || sampleTargetVampire != IntPtr.Zero)
    {
        vars.ColdStartPending = false;
        vars.ColdProcessProven = false;
    }

    // A final split leaves LiveSplit in TimerPhase.Ended, where normal ASL
    // reset{} and start{} actions are not called. Reset once the completed
    // result screen has been left, while preserving the result until then.
    if (vars.FinishSeen
        && timer.CurrentPhase == TimerPhase.Ended
        && settings["autoReset"]
        && settings.ResetEnabled
        && sceneName != "Battle")
    {
        vars.ResetForFreshStart = false;
        vars.TimerModel.Reset();
    }

    current.Scene = sceneName;
    current.UnityTime = unityTime;
    current.TimeScale = timeScale;
    current.RawIGT = rawIgt;
    current.IGT = rawIgt;

    // Detect a changed accepted run identity before consulting pause state. A
    // same-scene restart can arrive at exact zero scale, and its fresh raw IGT
    // must not inherit the previous run's frozen latch.
    bool acceptedRunReplaced = vars.RunActive
        && !float.IsNaN((float)vars.CapturedRunStart)
        && Math.Abs(sampleStartTime - (float)vars.CapturedRunStart) > 0.001f;
    if (acceptedRunReplaced)
    {
        vars.FrozenIGT = float.NaN;
        vars.WasTimerPaused = false;
        vars.ResultFreezeObserved = false;
    }

    float previousFrozenIgt = (float)vars.FrozenIGT;
    bool previouslyPaused = (bool)vars.WasTimerPaused;
    bool timerPaused = timeScale <= 0.0001f;

    // Moon/VampiricPower/Pause use ToggleTimer(false), which sets exact
    // timeScale zero. Hold the first zero-scale sample defensively. Fractional
    // hitstop and victory slowdown (0.01/0.02/0.1) continue using raw Time.time.
    float effectiveIgt = rawIgt;
    if (vars.RunActive && timerPaused && !acceptedRunReplaced)
    {
        if (!previouslyPaused || float.IsNaN(previousFrozenIgt))
            vars.FrozenIGT = rawIgt;
        effectiveIgt = (float)vars.FrozenIGT;
    }
    else
    {
        vars.FrozenIGT = rawIgt;
    }

    // ResultCanvas freezes Time.time before YouHuntedCo clears isBattle in the
    // same Unity frame. Exact zero is therefore the result discriminator; do
    // not require a second frozen poll or the one-poll isBattle edge can be lost.
    vars.ResultFreezeObserved = timeScale == 0f
        && vars.RunActive;
    vars.WasTimerPaused = timerPaused;

    current.IGT = effectiveIgt;

    bool freshRun = sampleChapter == 0 && current.IGT >= 0f && current.IGT <= 10f;
    bool classicAnyPercent = sampleTargetVampire == IntPtr.Zero;
    vars.FreshRunSample = sceneName == "Battle" && freshRun && classicAnyPercent;
    vars.LatestScene = sceneName;
    vars.HaveValidSample = true;

    bool newRunStart = float.IsNaN((float)vars.LastAcceptedRunStart)
        || Math.Abs(sampleStartTime - (float)vars.LastAcceptedRunStart) > 0.001f;

    // Complete the explicit zero/unsettled -> settled identity, or consume
    // the independent new-process proof. A settled but late/non-fresh sample
    // consumes the evidence without starting; it must not be retried later.
    bool coldSettledIdentity = sceneName == "Battle"
        && sampleChapter == 0
        && sampleTargetVampire == IntPtr.Zero
        && sampleStartTime > 0f;
    bool coldIdentityObservation = coldSettledIdentity
        && ((bool)vars.ColdStartPending
            || ((bool)vars.ColdProcessProven && firstColdObservation));
    if (coldIdentityObservation)
    {
        vars.ColdProcessProven = false;
        vars.ColdStartPending = false;
        vars.ColdStartObservationOpen = false;
    }

    bool leftActiveRun = vars.RunActive && sceneName != "Battle";
    vars.ResetCandidate = acceptedRunReplaced || leftActiveRun;

    // A manually started timer may be waiting in the menu. Bind it to the
    // first fresh Classic/Any% Battle sample without resetting while waiting.
    if (vars.ManualStartWaiting && vars.FreshRunSample)
    {
        vars.AutoStartArmed = false;
        vars.RunActive = true;
        vars.CapturedRunStart = sampleStartTime;
        vars.LastAcceptedRunStart = sampleStartTime;
        vars.ExpectedChapter = 1;
        vars.ManualStartWaiting = false;
        vars.ManualStartRejected = false;
        vars.FinishSeen = false;
        vars.PendingSplitKind = 0;
        vars.ResultFreezeObserved = false;
        vars.ResetCandidate = false;
    }

    // A restart can reload Battle in place without exposing a non-Battle
    // scene. A changed startTime is the authoritative new-run identity.
    if (acceptedRunReplaced && sceneName == "Battle" && vars.FreshRunSample)
        vars.AutoStartArmed = true;

    // Consume a fresh auto-start observation independently of both the custom
    // and base Start controls. Controls gate only the queued candidate; they
    // must not make the same fresh Battle sample replay after re-enabling.
    bool freshAutoStartObservation = (bool)vars.AutoStartArmed
        && (bool)vars.FreshRunSample
        && newRunStart;
    bool coldAutoStartObservation = coldIdentityObservation
        && coldPhaseEligible
        && (bool)vars.FreshRunSample
        && newRunStart;
    freshAutoStartObservation = freshAutoStartObservation || coldAutoStartObservation;
    vars.StartCandidate = settings["autoStart"]
        && settings.StartEnabled
        && freshAutoStartObservation
        && !vars.ManualStartWaiting;
    if (freshAutoStartObservation)
    {
        vars.AutoStartArmed = false;
        vars.ColdProcessProven = false;
        vars.ColdStartPending = false;
        vars.ColdStartObservationOpen = false;
    }

    bool sameRun = vars.RunActive
        && sceneName == "Battle"
        && Math.Abs(sampleStartTime - (float)vars.CapturedRunStart) <= 0.001f;

    // Observe floor/final edges in every timer phase so they are consumed once,
    // but only Running may publish a split candidate. A paused or Ended result
    // must not replay on resume or after a phase change.
    bool timerRunning = timer.CurrentPhase == TimerPhase.Running;
    if (sameRun)
    {
        int expectedChapter = (int)vars.ExpectedChapter;
        bool haveLastValidSample = (bool)vars.HaveLastValidSample;
        int lastValidChapter = (int)vars.LastValidChapter;
        bool lastValidIsBattle = (bool)vars.LastValidIsBattle;
        bool enteredNextFloor = expectedChapter >= 1
            && expectedChapter <= 4
            && haveLastValidSample
            && lastValidChapter == expectedChapter - 1
            && sampleChapter == expectedChapter;
        if (enteredNextFloor)
        {
            vars.ExpectedChapter = expectedChapter + 1;
            if (timerRunning && settings.SplitEnabled && settings["splitFloors"])
                vars.PendingSplitKind = 1;
        }

        bool finalResult = (int)vars.ExpectedChapter == 5
            && sampleChapter == 4
            && haveLastValidSample
            && lastValidIsBattle
            && !sampleIsBattle
            && current.TimeScale == 0f
            && vars.ResultFreezeObserved;
        if (finalResult)
        {
            vars.FinishSeen = true;
            if (timerRunning && settings.SplitEnabled && settings["splitFinal"])
                vars.PendingSplitKind = 2;
        }
    }

    // The framework refreshes old/current before calling update(), including
    // on a poll that returns false above. Commit this observation only after
    // every native and derived-value check has succeeded, so the next valid
    // poll can still compare against the last complete sample.
    vars.HaveLastValidSample = true;
    vars.LastValidChapter = sampleChapter;
    vars.LastValidIsBattle = sampleIsBattle;

    // LiveSplit does not call start{} in TimerPhase.Ended. Preserve the
    // completed result until the next fresh run, then reset to NotRunning;
    // start{} is evaluated later in this same ASL cycle. onReset preserves
    // this exact StartCandidate for the remainder of the cycle.
    if (vars.StartCandidate
        && settings["autoReset"]
        && settings.ResetEnabled
        && timer.CurrentPhase == TimerPhase.Ended)
    {
        vars.ResetForFreshStart = true;
        vars.TimerModel.Reset();
    }

    return true;
}

start
{
    // This predicate may only record an automatic-start attempt. The actual
    // run latch is committed in onStart, after LiveSplit accepts the start.
    if (!settings["autoStart"] || !settings.StartEnabled
        || !vars.SupportedBuild || !vars.Helper.Loaded || !vars.StartCandidate)
        return false;

    vars.AutoStartAttempt = true;
    return true;
}

gameTime
{
    if (!vars.SupportedBuild || !vars.Helper.Loaded)
        return null;

    float igt = (float)current.IGT;
    if (float.IsNaN(igt) || float.IsInfinity(igt))
        return null;

    return TimeSpan.FromSeconds((double)Math.Max(0f, igt));
}

split
{
    if (!vars.SupportedBuild || !vars.Helper.Loaded || !vars.RunActive
        || !settings.SplitEnabled)
    {
        vars.PendingSplitKind = 0;
        return false;
    }

    int pendingSplit = (int)vars.PendingSplitKind;
    if (pendingSplit == 1 && !settings["splitFloors"])
    {
        vars.PendingSplitKind = 0;
        return false;
    }
    if (pendingSplit == 2 && !settings["splitFinal"])
    {
        vars.PendingSplitKind = 0;
        return false;
    }

    return pendingSplit == 1 || pendingSplit == 2;
}

reset
{
    if (!settings["autoReset"] || !settings.ResetEnabled
        || !vars.SupportedBuild || !vars.Helper.Loaded)
        return false;

    if (vars.ResetCandidate && vars.StartCandidate)
        vars.ResetForFreshStart = true;
    return vars.ResetCandidate;
}

onStart
{
    bool automaticStart = (bool)vars.AutoStartAttempt;
    vars.AutoStartAttempt = false;
    vars.StartCandidate = false;
    vars.ManualStartRejected = false;

    if (automaticStart)
    {
        if ((bool)vars.FreshRunSample && settings["autoStart"] && settings.StartEnabled)
        {
            vars.AutoStartArmed = false;
            vars.ColdProcessProven = false;
            vars.ColdStartPending = false;
            vars.ColdStartObservationOpen = false;
            vars.RunActive = true;
            vars.CapturedRunStart = (float)current.StartTime;
            vars.LastAcceptedRunStart = (float)current.StartTime;
            vars.ExpectedChapter = 1;
            vars.ManualStartWaiting = false;
            vars.FinishSeen = false;
            vars.PendingSplitKind = 0;
            vars.ResultFreezeObserved = false;
        }
        return;
    }

    // Manual Start before attach or from a menu is supported. It waits without
    // touching dynamic current fields until a valid sample has been recorded.
    // A manual mid-run start is fail-closed.
    vars.AutoStartArmed = false;
    vars.ColdProcessProven = false;
    vars.ColdStartPending = false;
    vars.ColdStartObservationOpen = false;
    if (!vars.HaveValidSample || (string)vars.LatestScene != "Battle")
    {
        vars.ManualStartWaiting = true;
        return;
    }
    if ((bool)vars.FreshRunSample)
    {
        vars.RunActive = true;
        vars.CapturedRunStart = (float)current.StartTime;
        vars.LastAcceptedRunStart = (float)current.StartTime;
        vars.ExpectedChapter = 1;
        vars.ManualStartWaiting = false;
        vars.FinishSeen = false;
        vars.PendingSplitKind = 0;
        vars.ResultFreezeObserved = false;
    }
    else
    {
        vars.ManualStartWaiting = false;
        vars.ManualStartRejected = true;
    }
}

onSplit
{
    // Consume the observation only after LiveSplit accepted the split.
    vars.PendingSplitKind = 0;
}

onReset
{
    bool preserveFreshStart = (bool)vars.ResetForFreshStart;
    vars.RunActive = false;
    vars.FinishSeen = false;
    vars.CapturedRunStart = float.NaN;
    vars.LastAcceptedRunStart = float.NaN;
    vars.ExpectedChapter = 1;
    vars.ResetCandidate = false;
    vars.ResetForFreshStart = false;
    vars.AutoStartAttempt = false;
    vars.ColdProcessProven = false;
    vars.ColdStartPending = false;
    vars.ColdStartObservationOpen = false;
    vars.ManualStartWaiting = false;
    vars.ManualStartRejected = false;
    vars.PendingSplitKind = 0;
    vars.FrozenIGT = float.NaN;
    vars.WasTimerPaused = false;
    vars.ResultFreezeObserved = false;
    if (!preserveFreshStart)
    {
        vars.HaveLastValidSample = false;
        vars.LastValidChapter = 0;
        vars.LastValidIsBattle = false;
        vars.StartCandidate = false;
    }
}

isLoading
{
    // Never let LiveSplit accumulate wall time between exact game-time samples.
    return true;
}

exit
{
    vars.LogTransition("process exit/reset");
    // Process identity is lost here, so this is the deliberate exception to
    // ordinary custom/base Reset controls: clear an orphaned unfinished timer
    // even when either control is disabled. Never reset a completed result.
    // This callback cannot read current/game/modules because the process is
    // already gone.
    if (timer.CurrentPhase == TimerPhase.Running
        || timer.CurrentPhase == TimerPhase.Paused)
        vars.TimerModel.Reset();

    vars.AutoStartArmed = false;
    vars.ColdProcessProven = false;
    vars.ColdStartPending = false;
    vars.ColdStartObservationOpen = false;
    vars.SupportedBuild = false;
    vars.NativeAttachState = 0;
    vars.NativeAttachRejected = false;
    vars.NativeFilesVerified = false;
    vars.NativeAttachAttempts = 0;
    vars.NativeAttachRetryPolls = 0;
    vars.TimeManagerSlot = IntPtr.Zero;
    vars.TimeScaleManagerSlot = IntPtr.Zero;
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
}

shutdown
{
    // asl-help's generated shutdown wrapper performs helper cleanup.
}
