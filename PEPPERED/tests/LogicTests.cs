using System;
using System.Collections.Generic;
using Peppered;

public static class LogicTests
{
    private static int passed;

    private static Snapshot Sample(string scene)
    {
        return new Snapshot {
            Valid = true,
            Scene = scene,
            HasCheckpoint = false,
            StartReady = false,
            TheodoreMetadataReady = true,
            EndingSignals = new HashSet<string>(),
            BoundaryMetadataReady = true
        };
    }

    private static Snapshot StartSample(bool? ready, bool? checkpoint)
    {
        Snapshot sample = Sample("Office_1");
        sample.StartReady = ready;
        sample.HasCheckpoint = checkpoint;
        return sample;
    }

    private static Snapshot EndingSample(string signal)
    {
        Snapshot sample = Sample("Boundary");
        sample.EndingSignals.Add(signal);
        return sample;
    }

    private static Snapshot TheodoreSample(bool victory)
    {
        Snapshot sample = Sample("T_Boss");
        sample.TheodoreHp = victory ? 0 : 5;
        sample.TheodoreMetadataReady = true;
        if (victory) sample.EndingSignals.Add(EndingCatalog.TheodoreVictory);
        return sample;
    }

    private static Options Options(bool enabled, bool autoStart, bool autoReset,
        bool splitWorlds, bool splitScenes, bool splitEndings,
        bool basicStart, bool basicSplit, bool basicReset)
    {
        return new Options {
            Enabled = enabled,
            AutoStart = autoStart,
            AutoReset = autoReset,
            SplitWorlds = splitWorlds,
            SplitScenes = splitScenes,
            SplitEndings = splitEndings,
            BasicStart = basicStart,
            BasicSplit = basicSplit,
            BasicReset = basicReset,
            Worlds = new HashSet<int>(),
            SceneIds = new HashSet<string>(),
            EndingIds = new HashSet<string>()
        };
    }

    private static Options Full()
    {
        Options options = Options(true, true, true, true, true, true, true, true, true);
        options.Worlds = new HashSet<int> { 0, 1, 2, 3, 4, 5 };
        SceneDefinition[] scenes = SceneCatalog.All;
        for (int i = 0; i < scenes.Length; i++)
            if (scenes[i].DefaultEnabled) options.SceneIds.Add(scenes[i].Id);
        string[] signals = EndingCatalog.PhysicalSignals;
        for (int i = 0; i < signals.Length; i++) options.EndingIds.Add(signals[i]);
        return options;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed++;
    }

    private static void NoDecision(Decision decision, string message)
    {
        Check(!decision.Start && !decision.Reset && decision.SplitId == null, message);
    }

    private static void Split(StateMachine state, Snapshot sample, Options options,
        string expected, string message)
    {
        Decision decision = state.Step(sample, "Running", options);
        Check(expected == decision.SplitId,
            message + " expected=" + expected + " actual=" + decision.SplitId);
        state.OnSplit(true);
    }

    private static StateMachine Running(Options options, string scene)
    {
        StateMachine state = new StateMachine();
        state.Attach();
        state.OnStart(false);
        NoDecision(state.Step(Sample(scene), "Running", options),
            "manual first sample seeds " + scene);
        return state;
    }

    private static void TestCatalogAndSettingsContract()
    {
        SceneDefinition[] scenes = SceneCatalog.All;
        Check(scenes.Length == 88, "T_End removed from ordinary scene catalog");
        SceneDefinition missing;
        Check(!SceneCatalog.TryFind("T_End", out missing), "T_End has no ordinary scene definition");
        Check(SceneCatalog.GroupName(SceneCategory.Coarse) == "Main", "coarse uses Main group");
        Check(SceneCatalog.GroupName(SceneCategory.Detailed) == "Detailed", "detailed group name");
        Check(SceneCatalog.GroupName(SceneCategory.Branch) == "Branches", "branch group name");
        for (int i = 0; i < scenes.Length; i++)
        {
            Check(scenes[i].Label.IndexOf("World ", StringComparison.Ordinal) != 0,
                "scene label has no repeated world prefix " + scenes[i].Scene);
            Check(scenes[i].Label.IndexOf(" / ", StringComparison.Ordinal) < 0,
                "scene label has no repeated hierarchy prefix " + scenes[i].Scene);
            Check(scenes[i].Label.IndexOf(scenes[i].Scene + " (", StringComparison.Ordinal) == 0
                && scenes[i].Label.EndsWith(")"),
                "scene label preserves internal name and description " + scenes[i].Scene);
            Check(scenes[i].Id == scenes[i].SettingKey, "scene key is stable " + scenes[i].Scene);
        }
        EndingDefinition[] endings = EndingCatalog.All;
        Check(endings.Length == 15, "optional friendship loss adds only one ending leaf");
        string[] physical = EndingCatalog.PhysicalSignals;
        Check(physical.Length == 12, "twelve implemented physical ending events");
        HashSet<string> unique = new HashSet<string>(physical);
        Check(unique.Count == physical.Length, "physical ending event keys are unique");
        EndingDefinition with5;
        EndingDefinition with6;
        EndingDefinition with7;
        Check(EndingCatalog.TryFindById("ending.5.with", out with5)
            && with5.Label == "Ending 5 (With Karmina): start watching TV with Karmina"
            && with5.PhysicalSignal == EndingCatalog.KarminaTvStart,
            "Ending5 maps to the new TV-start event");
        Check(EndingCatalog.TryFindById("ending.6.with", out with6)
            && with6.Label == "Ending 6 (With Karmina): start watching TV with Karmina"
            && with6.PhysicalSignal == EndingCatalog.KarminaTvStart,
            "Ending6 shares the new TV-start event");
        Check(EndingCatalog.TryFindById("ending.7.with", out with7)
            && with7.Label == "Ending 7 (With Karmina): start watching TV with Karmina"
            && with7.PhysicalSignal == EndingCatalog.KarminaTvStart,
            "Ending7 shares the qualified TV-start event");
        EndingDefinition friendLoss;
        EndingDefinition surrender;
        Check(EndingCatalog.TryFindById("ending.8.friend_loss", out friendLoss)
            && friendLoss.Label == "Ending 8 (optional friendship route): Merdeka defeat - NOT Ending 7"
            && friendLoss.PhysicalSignal == EndingCatalog.MerdekaDefeat
            && friendLoss.Number == 8 && friendLoss.Variant == EndingVariant.None
            && !friendLoss.DefaultEnabled,
            "optional Ending8 friendship loss is a default-off alias of existing physical defeat");
        Check(EndingCatalog.TryFindById("ending.8", out surrender)
            && surrender.PhysicalSignal == EndingCatalog.MerdekaSurrender
            && surrender.Number == 8 && !surrender.DefaultEnabled,
            "original Ending8 surrender keeps its separate default-off physical endpoint");
        Check(endings[10].Id == "ending.8" && endings[11].Id == friendLoss.Id,
            "friendship loss follows original Ending8 in the catalog");
        for (int number = 5; number <= 7; number++)
        {
            EndingDefinition against;
            string id = "ending." + number + ".against";
            string label = "Ending " + number
                + " (Against Karmina): final CONTINUE / GIVE UP choice";
            Check(EndingCatalog.TryFindById(id, out against)
                && against.Label == label
                && against.PhysicalSignal == EndingCatalog.KarminaFinalChoice
                && against.Variant == EndingVariant.Against
                && !against.DefaultEnabled,
                "Against alias catalog contract " + id);
        }
        Check(!unique.Contains(EndingCatalog.KarminaTv),
            "obsolete Karmina TV event is not physical");
        Check(unique.Contains(EndingCatalog.KarminaTvStart),
            "new Karmina TV-start event is physical");
        for (int i = 0; i < endings.Length; i++)
            Check(!endings[i].DefaultEnabled, "ending leaves default off " + endings[i].Id);
    }

    private static void TestDelayedFreshStart()
    {
        Options options = Full();
        StateMachine state = new StateMachine();
        state.Attach();
        NoDecision(state.Step(Sample("[Main Menu]"), "NotRunning", options), "menu only does not start");

        Snapshot intro = StartSample(false, false);
        NoDecision(state.Step(intro, "NotRunning", options), "intro not ready does not start");
        state.Invalidate();
        intro.StartReady = false;
        NoDecision(state.Step(intro, "NotRunning", options), "invalid poll preserves fresh-start arming");

        Snapshot ready = StartSample(true, false);
        Decision start = state.Step(ready, "NotRunning", options);
        Check(start.Start, "delayed new-game start does not require adjacent menu-ready samples");
        state.OnStart(true);
        NoDecision(state.Step(StartSample(true, false), "Running", options),
            "automatic start seeds Office_1 without duplicate world 0");

        StateMachine unknown = new StateMachine();
        unknown.Attach();
        NoDecision(unknown.Step(Sample("[Main Menu]"), "NotRunning", options), "unknown baseline menu");
        NoDecision(unknown.Step(StartSample(true, null), "NotRunning", options),
            "unknown checkpoint fails closed");
        Check(unknown.Step(StartSample(true, false), "NotRunning", options).Start,
            "checkpoint retry can prove a fresh start");

        StateMachine loaded = new StateMachine();
        loaded.Attach();
        loaded.Step(Sample("[Main Menu]"), "NotRunning", options);
        NoDecision(loaded.Step(StartSample(true, true), "NotRunning", options),
            "loaded checkpoint cannot auto-start");
        NoDecision(loaded.Step(StartSample(true, false), "NotRunning", options),
            "loaded start observation is not replayed on revisit");

        StateMachine loadedBeforeReady = new StateMachine();
        loadedBeforeReady.Attach();
        loadedBeforeReady.Step(Sample("[Main Menu]"), "NotRunning", options);
        loadedBeforeReady.Step(StartSample(null, true), "NotRunning", options);
        NoDecision(loadedBeforeReady.Step(StartSample(true, false), "NotRunning", options),
            "proven checkpoint blocks a later readiness retry");

        StateMachine disabledBeforeReady = new StateMachine();
        disabledBeforeReady.Attach();
        Options disabled = Full();
        disabled.Enabled = false;
        disabledBeforeReady.Step(Sample("[Main Menu]"), "NotRunning", disabled);
        disabledBeforeReady.Step(StartSample(false, false), "NotRunning", disabled);
        disabled.Enabled = true;
        Check(disabledBeforeReady.Step(StartSample(true, false), "NotRunning", disabled).Start,
            "disabled start is not consumed before actual readiness");

        StateMachine disabledAtReady = new StateMachine();
        disabledAtReady.Attach();
        disabledAtReady.Step(Sample("[Main Menu]"), "NotRunning", disabled);
        disabledAtReady.Step(StartSample(true, false), "NotRunning", disabled);
        disabled.Enabled = true;
        NoDecision(disabledAtReady.Step(StartSample(true, false), "NotRunning", disabled),
            "actual ready start is consumed while disabled");

        StateMachine stale = new StateMachine();
        stale.Attach();
        stale.Step(Sample("[Main Menu]"), "NotRunning", options);
        stale.Step(Sample("A_1"), "NotRunning", options);
        NoDecision(stale.Step(StartSample(true, false), "NotRunning", options),
            "non-menu revisit cannot use stale menu arming");
    }

    private static void TestWorldSceneAndNoBossQueue()
    {
        Options options = Full();
        StateMachine state = Running(options, "Office_1");
        Split(state, Sample("A_1"), options, "world1", "world transition");
        Snapshot merdeka = Sample("B_Merdeka");
        merdeka.Abyss = 52;
        NoDecision(state.Step(merdeka, "Running", options),
            "boss victory marker does not create a split");
        Snapshot theodore = Sample("T_Boss");
        theodore.Abyss = 300;
        NoDecision(state.Step(theodore, "Running", options),
            "Theodore boss marker does not create a split");

        Options sceneOptions = Full();
        sceneOptions.SceneIds.Clear();
        SceneDefinition definition;
        Check(SceneCatalog.TryFind("A_2", out definition), "ordinary scene remains mapped");
        sceneOptions.SceneIds.Add(definition.Id);
        StateMachine scenes = Running(sceneOptions, "A_1");
        Split(scenes, Sample("A_2"), sceneOptions, definition.Id, "ordinary scene split");
        NoDecision(scenes.Step(Sample("A_2"), "Running", sceneOptions),
            "ordinary scene latch dedupes revisits");

        StateMachine tBoss = Running(options, "G_End");
        NoDecision(tBoss.Step(Sample("T_End"), "Running", options),
            "T_End is not an Ending9 boundary");
        Split(tBoss, TheodoreSample(true), options, EndingCatalog.TheodoreVictory,
            "T_Boss reader signal produces Ending9");
        NoDecision(tBoss.Step(TheodoreSample(true), "Running", options),
            "T_Boss Ending9 signal is one shot");

        StateMachine wrongScene = Running(options, "G_End");
        Snapshot fake = TheodoreSample(true);
        fake.Scene = "T_End";
        NoDecision(wrongScene.Step(fake, "Running", options),
            "Ending9 signal is scoped to T_Boss");

        StateMachine wrongSeed = new StateMachine();
        wrongSeed.Step(fake, "NotRunning", options);
        wrongSeed.OnStart(false);
        Split(wrongSeed, TheodoreSample(true), options, EndingCatalog.TheodoreVictory,
            "wrong-scene Ending9 signal is not seeded as consumed");
    }

    private static void TestPhysicalEndingLatches()
    {
        Options options = Full();
        options.SplitWorlds = false;
        options.SplitScenes = false;
        StateMachine state = Running(options, "A_1");
        HashSet<string> callerSet = new HashSet<string>();
        callerSet.Add(EndingCatalog.MerdekaVictory);
        Snapshot tv = Sample("B_Merdeka");
        tv.MerdekaStage = 52;
        tv.EndingSignals = callerSet;
        Decision decision = state.Step(tv, "Running", options);
        Check(decision.SplitId == EndingCatalog.MerdekaVictory, "physical Merdeka win emits physical key");
        callerSet.Clear();
        state.OnSplit(true);
        NoDecision(state.Step(Sample("B_Merdeka"), "Running", options),
            "caller mutation cannot alter copied Merdeka event baseline");

        Snapshot shared = Sample("B_Merdeka");
        shared.MerdekaStage = 52;
        shared.EndingSignals.Add(EndingCatalog.MerdekaVictory);
        NoDecision(state.Step(shared, "Running", options),
            "shared Merdeka endpoint has one per-run latch");

        Snapshot obsoleteTv = Sample("B_Aftermath");
        obsoleteTv.EndingSignals.Add(EndingCatalog.KarminaTv);
        NoDecision(state.Step(obsoleteTv, "Running", options),
            "obsolete TV endpoint is no longer physical");

        Options disabled = Full();
        disabled.SplitWorlds = false;
        disabled.SplitScenes = false;
        disabled.EndingIds.Remove(EndingCatalog.ExecutionBlack);
        StateMachine disabledState = Running(disabled, "A_1");
        NoDecision(disabledState.Step(EndingSample(EndingCatalog.ExecutionBlack), "Running", disabled),
            "disabled ending event is consumed");
        disabled.EndingIds.Add(EndingCatalog.ExecutionBlack);
        NoDecision(disabledState.Step(Sample("Boundary"), "Running", disabled),
            "re-enabled ending cannot replay consumed event");

        StateMachine invalid = Running(options, "A_1");
        invalid.Invalidate();
        Split(invalid, EndingSample(EndingCatalog.GemDialogue), options,
            EndingCatalog.GemDialogue, "ending recovers after invalid sample");
        NoDecision(invalid.Step(EndingSample(EndingCatalog.GemDialogue), "Running", options),
            "ending event dedupes after callback");

        StateMachine paused = Running(options, "A_1");
        NoDecision(paused.Step(TheodoreSample(true), "Paused", options),
            "paused ending event is consumed without action");
        NoDecision(paused.Step(TheodoreSample(true), "Running", options),
            "paused ending cannot replay");
    }

    private static Snapshot TvStartSample(bool active = true,
        bool metadataReady = true)
    {
        Snapshot sample = Sample("B_Aftermath");
        sample.BoundaryMetadataReady = metadataReady;
        sample.TvStartMetadataReady = metadataReady;
        sample.TvStartActive = active;
        if (active && metadataReady)
            sample.EndingSignals.Add(EndingCatalog.KarminaTvStart);
        return sample;
    }

    private static void TestKarminaTvStartScopeAndConsumption()
    {
        Options options = Full();
        options.SplitWorlds = false;
        options.SplitScenes = false;

        StateMachine state = Running(options, "A_1");
        Snapshot wrongScene = TvStartSample(true);
        wrongScene.Scene = "B_End";
        NoDecision(state.Step(wrongScene, "Running", options),
            "TV start is scoped to B_Aftermath");
        NoDecision(state.Step(TvStartSample(false), "Running", options),
            "inactive managed TV frame does not finish");
        NoDecision(state.Step(TvStartSample(true, false), "Running", options),
            "TV metadata failure cannot admit a candidate");
        Split(state, TvStartSample(true), options, EndingCatalog.KarminaTvStart,
            "exact m36 TV start produces the With5/6 physical key");
        NoDecision(state.Step(TvStartSample(true), "Running", options),
            "TV start is one shot after split");

        StateMachine paused = Running(options, "A_1");
        NoDecision(paused.Step(TvStartSample(true), "Paused", options),
            "paused TV start is consumed without action");
        NoDecision(paused.Step(TvStartSample(true), "Running", options),
            "paused TV start cannot replay");

        Options disabledOptions = Full();
        disabledOptions.SplitWorlds = false;
        disabledOptions.SplitScenes = false;
        StateMachine disabled = Running(disabledOptions, "A_1");
        disabledOptions.Enabled = false;
        NoDecision(disabled.Step(TvStartSample(true), "Running", disabledOptions),
            "disabled TV start is consumed without action");
        disabledOptions.Enabled = true;
        NoDecision(disabled.Step(TvStartSample(true), "Running", disabledOptions),
            "disabled TV start cannot replay");

        StateMachine manual = new StateMachine();
        manual.Step(TvStartSample(true), "NotRunning", options);
        manual.OnStart(false);
        NoDecision(manual.Step(TvStartSample(true), "Running", options),
            "manual start with an already-active TV does not retro-finish");

        StateMachine invalid = Running(options, "A_1");
        invalid.Invalidate();
        Split(invalid, TvStartSample(true), options, EndingCatalog.KarminaTvStart,
            "invalid gap does not seed a TV latch before a valid start");
    }

    private static void TestMerdekaSurrenderScopeAndConsumption()
    {
        Options options = Full();
        options.SplitWorlds = false;
        options.SplitScenes = false;

        StateMachine scoped = Running(options, "A_1");
        Snapshot wrongScene = Sample("B_5");
        wrongScene.EndingSignals.Add(EndingCatalog.MerdekaSurrender);
        NoDecision(scoped.Step(wrongScene, "Running", options),
            "Ending8 signal is scoped to B_Merdeka");
        Snapshot surrender = Sample("B_Merdeka");
        surrender.EndingSignals.Add(EndingCatalog.MerdekaSurrender);
        Split(scoped, surrender, options, EndingCatalog.MerdekaSurrender,
            "final surrender confirmation produces Ending8");
        NoDecision(scoped.Step(surrender, "Running", options),
            "Ending8 is one shot after split");

        StateMachine paused = Running(options, "A_1");
        NoDecision(paused.Step(surrender, "Paused", options),
            "paused surrender is consumed without action");
        NoDecision(paused.Step(surrender, "Running", options),
            "paused surrender cannot replay");

        Options disabled = Full();
        disabled.Enabled = false;
        StateMachine disabledState = Running(disabled, "A_1");
        NoDecision(disabledState.Step(surrender, "Running", disabled),
            "disabled surrender is consumed without action");
        disabled.Enabled = true;
        NoDecision(disabledState.Step(surrender, "Running", disabled),
            "disabled surrender cannot replay");

        StateMachine ended = Running(options, "A_1");
        NoDecision(ended.Step(surrender, "Ended", options),
            "Ended surrender is consumed without action");
        NoDecision(ended.Step(surrender, "Running", options),
            "Ended surrender cannot replay");

        StateMachine manual = new StateMachine();
        manual.Step(surrender, "NotRunning", options);
        manual.OnStart(false);
        NoDecision(manual.Step(surrender, "Running", options),
            "manual start after surrender does not retro-finish");

        StateMachine reset = Running(options, "A_1");
        Split(reset, surrender, options, EndingCatalog.MerdekaSurrender,
            "first surrender run split");
        reset.OnReset();
        reset.Step(Sample("[Main Menu]"), "NotRunning", options);
        reset.OnStart(false);
        reset.Step(Sample("A_1"), "Running", options);
        Split(reset, surrender, options, EndingCatalog.MerdekaSurrender,
            "reset allows a new surrender attempt");
    }

    private static void TestMerdekaOutcomeScopeAndConsumption()
    {
        Options options = Full();
        options.SplitWorlds = false;
        options.SplitScenes = false;
        // The legacy Merdeka victory marker remains observable for diagnostics,
        // but With5/6 now select the later TV-start event instead.
        options.EndingIds.Remove(EndingCatalog.MerdekaVictory);

        StateMachine state = Running(options, "A_1");
        Snapshot wrongScene = Sample("B_10");
        wrongScene.MerdekaStage = 51;
        wrongScene.EndingSignals.Add(EndingCatalog.MerdekaDefeat);
        NoDecision(state.Step(wrongScene, "Running", options),
            "Merdeka defeat is scoped to B_Merdeka");

        Snapshot firstTerminal = Sample("B_Merdeka");
        firstTerminal.MerdekaStage = 52;
        NoDecision(state.Step(firstTerminal, "Running", options),
            "core cannot retro-finish an unarmed terminal sample");

        Snapshot victory = Sample("B_Merdeka");
        victory.MerdekaStage = 52;
        victory.EndingSignals.Add(EndingCatalog.MerdekaVictory);
        NoDecision(state.Step(victory, "Running", options),
            "legacy Merdeka victory does not finish the With5/6 TV endpoint");

        StateMachine loss = Running(options, "A_1");
        Snapshot defeat = Sample("B_Merdeka");
        defeat.MerdekaStage = 51;
        defeat.EndingSignals.Add(EndingCatalog.MerdekaDefeat);
        Split(loss, defeat, options, EndingCatalog.MerdekaDefeat,
            "raw source-qualified Merdeka defeat remains an observable physical event");

        // Ending 7 is explicitly preset to the later shared TV-start event.
        // The reader may still report raw defeat diagnostics, but that event
        // is not an Ending 7 finish source.
        Options ending7 = Full();
        ending7.EndingIds.Clear();
        ending7.EndingIds.Add(EndingCatalog.KarminaTvStart);
        StateMachine ending7State = Running(ending7, "A_1");
        NoDecision(ending7State.Step(defeat, "Running", ending7),
            "Ending7 preset ignores raw Merdeka defeat before the TV start");
        Split(ending7State, TvStartSample(true), ending7,
            EndingCatalog.KarminaTvStart,
            "Ending7 preset finishes at the qualified TV-start event");

        StateMachine paused = Running(options, "A_1");
        NoDecision(paused.Step(defeat, "Paused", options),
            "paused Merdeka defeat is consumed without action");
        NoDecision(paused.Step(defeat, "Running", options),
            "paused Merdeka defeat cannot replay");

        StateMachine disabled = Running(options, "A_1");
        options.Enabled = false;
        NoDecision(disabled.Step(victory, "Running", options),
            "disabled Merdeka victory is consumed without action");
        options.Enabled = true;
        NoDecision(disabled.Step(victory, "Running", options),
            "disabled Merdeka victory cannot replay");

        StateMachine reset = Running(options, "A_1");
        Split(reset, defeat, options, EndingCatalog.MerdekaDefeat,
            "first Merdeka defeat run split");
        reset.OnReset();
        reset.Step(Sample("[Main Menu]"), "NotRunning", options);
        reset.OnStart(false);
        reset.Step(Sample("A_1"), "Running", options);
        Split(reset, defeat, options, EndingCatalog.MerdekaDefeat,
            "reset clears Merdeka endpoint latch");
    }

    private static void TestOptionalFriendLossPhysicalIsolation()
    {
        Options onlyFriendLoss = Full();
        onlyFriendLoss.SplitWorlds = false;
        onlyFriendLoss.SplitScenes = false;
        onlyFriendLoss.EndingIds.Clear();
        onlyFriendLoss.EndingIds.Add(EndingCatalog.MerdekaDefeat);
        Snapshot loss = Sample("B_Merdeka");
        loss.MerdekaStage = 51;
        loss.EndingSignals.Add(EndingCatalog.MerdekaDefeat);
        StateMachine state = Running(onlyFriendLoss, "A_1");
        Split(state, loss, onlyFriendLoss, EndingCatalog.MerdekaDefeat,
            "optional Ending8 selects only physical Merdeka defeat");
        NoDecision(state.Step(loss, "Running", onlyFriendLoss), "one physical defeat per run");
        Snapshot victory = Sample("B_Merdeka");
        victory.MerdekaStage = 52;
        victory.EndingSignals.Add(EndingCatalog.MerdekaVictory);
        NoDecision(Running(onlyFriendLoss, "A_1").Step(victory, "Running", onlyFriendLoss),
            "Merdeka win cannot finish optional Ending8 loss");
        Snapshot surrender = Sample("B_Merdeka");
        surrender.EndingSignals.Add(EndingCatalog.MerdekaSurrender);
        NoDecision(Running(onlyFriendLoss, "A_1").Step(surrender, "Running", onlyFriendLoss),
            "old Ending8 confirmation cannot finish loss-only checkbox");
        Options onlySurrender = Full();
        onlySurrender.SplitWorlds = false;
        onlySurrender.SplitScenes = false;
        onlySurrender.EndingIds.Clear();
        onlySurrender.EndingIds.Add(EndingCatalog.MerdekaSurrender);
        StateMachine old8 = Running(onlySurrender, "A_1");
        NoDecision(old8.Step(loss, "Running", onlySurrender),
            "original Ending8 does not finish on defeat without optional checkbox");
        Split(old8, surrender, onlySurrender, EndingCatalog.MerdekaSurrender,
            "original Ending8 remains independently enabled");
        Options ending7 = Full();
        ending7.SplitWorlds = false;
        ending7.SplitScenes = false;
        ending7.EndingIds.Clear();
        ending7.EndingIds.Add(EndingCatalog.KarminaTvStart);
        StateMachine tv = Running(ending7, "A_1");
        NoDecision(tv.Step(loss, "Running", ending7), "Ending7 does not inherit Ending8 loss");
        Split(tv, TvStartSample(), ending7, EndingCatalog.KarminaTvStart,
            "Ending7 still finishes only at TV start");
        StateMachine paused = Running(onlyFriendLoss, "A_1");
        NoDecision(paused.Step(loss, "Paused", onlyFriendLoss), "paused optional loss consumed");
        NoDecision(paused.Step(loss, "Running", onlyFriendLoss), "paused loss cannot replay");
        Options disabled = Full();
        disabled.SplitWorlds = false;
        disabled.SplitScenes = false;
        disabled.EndingIds.Clear();
        StateMachine toggled = Running(disabled, "A_1");
        NoDecision(toggled.Step(loss, "Running", disabled), "default-off optional loss consumed");
        disabled.EndingIds.Add(EndingCatalog.MerdekaDefeat);
        NoDecision(toggled.Step(loss, "Running", disabled), "hot-enabled optional loss cannot replay");
    }

    private static Snapshot KarminaSample(int choice, bool ready = true,
        bool includeSignal = true)
    {
        Snapshot sample = Sample("B_End");
        sample.KarminaChoice = choice;
        sample.KarminaMetadataReady = ready;
        sample.BoundaryMetadataReady = ready;
        if (includeSignal && ready)
            sample.EndingSignals.Add(EndingCatalog.KarminaFinalChoice);
        return sample;
    }

    private static Options KarminaOptions()
    {
        Options options = Full();
        options.SplitWorlds = false;
        options.SplitScenes = false;
        options.EndingIds.Clear();
        options.EndingIds.Add(EndingCatalog.KarminaFinalChoice);
        return options;
    }

    private static void TestKarminaFinalChoiceScopeAndConsumption()
    {
        for (int number = 5; number <= 7; number++)
        {
            EndingDefinition alias;
            Check(EndingCatalog.TryFindById("ending." + number + ".against", out alias),
                "Against alias exists for core test " + number);
            Options options = KarminaOptions();
            StateMachine state = Running(options, "A_1");
            NoDecision(state.Step(KarminaSample(0, true, false), "Running", options),
                "Karmina prompt appearance does not finish alias " + number);
            Split(state, KarminaSample(1), options, EndingCatalog.KarminaFinalChoice,
                "CONTINUE finishes alias " + number);
        }

        StateMachine giveUp = Running(KarminaOptions(), "A_1");
        NoDecision(giveUp.Step(KarminaSample(0, true, false), "Running",
            KarminaOptions()), "GIVE UP prompt appearance does not finish");
        Split(giveUp, KarminaSample(2), KarminaOptions(),
            EndingCatalog.KarminaFinalChoice,
            "GIVE UP finishes the same physical Karmina event");

        Options allAliases = KarminaOptions();
        StateMachine onePhysicalEvent = Running(allAliases, "A_1");
        Split(onePhysicalEvent, KarminaSample(1), allAliases,
            EndingCatalog.KarminaFinalChoice,
            "all three aliases share one physical event");
        NoDecision(onePhysicalEvent.Step(KarminaSample(2), "Running", allAliases),
            "shared Karmina event cannot replay for another alias");

        StateMachine wrongScene = Running(KarminaOptions(), "A_1");
        Snapshot wrong = KarminaSample(1);
        wrong.Scene = "B_Merdeka";
        NoDecision(wrongScene.Step(wrong, "Running", KarminaOptions()),
            "Karmina final choice is scoped to B_End");

        StateMachine missing = Running(KarminaOptions(), "A_1");
        NoDecision(missing.Step(KarminaSample(1, false), "Running", KarminaOptions()),
            "missing Karmina metadata cannot admit a candidate");
        Split(missing, KarminaSample(1), KarminaOptions(),
            EndingCatalog.KarminaFinalChoice,
            "Karmina event recovers after metadata becomes ready");

        Options pausedOptions = KarminaOptions();
        StateMachine paused = Running(pausedOptions, "A_1");
        NoDecision(paused.Step(KarminaSample(1), "Paused", pausedOptions),
            "paused Karmina event is consumed without action");
        NoDecision(paused.Step(KarminaSample(1), "Running", pausedOptions),
            "paused Karmina event cannot replay");

        Options disabledOptions = KarminaOptions();
        StateMachine disabled = Running(disabledOptions, "A_1");
        disabledOptions.Enabled = false;
        NoDecision(disabled.Step(KarminaSample(1), "Running", disabledOptions),
            "disabled Karmina event is consumed without action");
        disabledOptions.Enabled = true;
        NoDecision(disabled.Step(KarminaSample(1), "Running", disabledOptions),
            "disabled Karmina event cannot replay");

        StateMachine reset = Running(KarminaOptions(), "A_1");
        Options resetOptions = KarminaOptions();
        Split(reset, KarminaSample(1), resetOptions,
            EndingCatalog.KarminaFinalChoice, "first Karmina run split");
        reset.OnReset();
        reset.Step(Sample("[Main Menu]"), "NotRunning", resetOptions);
        reset.OnStart(false);
        reset.Step(Sample("A_1"), "Running", resetOptions);
        Split(reset, KarminaSample(2), resetOptions,
            EndingCatalog.KarminaFinalChoice,
            "reset clears the Karmina physical-event latch");
    }

    private static void TestPhasesResetAndManualStart()
    {
        Options options = Full();
        options.SplitWorlds = false;
        options.SplitScenes = false;
        StateMachine state = Running(options, "A_1");
        NoDecision(state.Step(EndingSample(EndingCatalog.FinalDeathBlack), "Ended", options),
            "Ended consumes ending without action");
        NoDecision(state.Step(EndingSample(EndingCatalog.FinalDeathBlack), "Running", options),
            "Ended ending cannot replay");

        StateMachine reset = Running(options, "A_1");
        Split(reset, EndingSample(EndingCatalog.ExecutionBlack), options,
            EndingCatalog.ExecutionBlack, "first run ending");
        reset.OnReset();
        reset.Step(Sample("[Main Menu]"), "NotRunning", options);
        reset.OnStart(false);
        reset.Step(Sample("A_1"), "Running", options);
        Split(reset, EndingSample(EndingCatalog.ExecutionBlack), options,
            EndingCatalog.ExecutionBlack, "reset clears ending latch");

        StateMachine manual = new StateMachine();
        manual.Attach();
        manual.OnStart(false);
        NoDecision(manual.Step(EndingSample(EndingCatalog.BarDialogue), "Running", options),
            "manual start before first valid sample seeds signal");
        NoDecision(manual.Step(EndingSample(EndingCatalog.BarDialogue), "Running", options),
            "seeded manual event remains consumed");
    }

    public static int Main()
    {
        try
        {
            TestCatalogAndSettingsContract();
            TestDelayedFreshStart();
            TestWorldSceneAndNoBossQueue();
            TestPhysicalEndingLatches();
            TestKarminaTvStartScopeAndConsumption();
            TestMerdekaSurrenderScopeAndConsumption();
            TestMerdekaOutcomeScopeAndConsumption();
            TestOptionalFriendLossPhysicalIsolation();
            TestKarminaFinalChoiceScopeAndConsumption();
            TestPhasesResetAndManualStart();
            Console.WriteLine("PASS pure PEPPERED state-machine tests: " + passed + " assertions");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(error);
            return 1;
        }
    }
}
