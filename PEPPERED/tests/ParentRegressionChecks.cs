using System;
using System.Collections.Generic;
using Peppered;

// Independent contract fixtures: explicit offline snapshots, not game memory.
public static class ParentRegressionChecks
{
    private static int checks, failures;
    private const string Gem = "ending.gem_dialogue";
    private static Options Opt()
    {
        return new Options {
            Enabled = true, AutoStart = true, AutoReset = true,
            SplitWorlds = true, SplitScenes = false, SplitEndings = true,
            BasicStart = true, BasicSplit = true, BasicReset = true,
            Worlds = new HashSet<int> { 0, 1, 2, 3, 4, 5 },
            SceneIds = new HashSet<string>(), EndingIds = new HashSet<string> { Gem }
        };
    }
    private static Snapshot S(string scene, bool? ready = true, string ending = null)
    {
        return new Snapshot {
            Valid = true, Scene = scene, Abyss = 0, HasCheckpoint = false,
            StartReady = ready,
            TheodoreMetadataReady = true,
            EndingSignals = ending == null ? new HashSet<string>() : new HashSet<string> { ending }
        };
    }
    private static void Check(bool ok, string name)
    {
        checks++;
        if (!ok) { failures++; Console.WriteLine("FAIL " + name); }
    }
    private static StateMachine Running(Options options)
    {
        var core = new StateMachine();
        core.Step(S("Office_1"), "NotRunning", options);
        core.OnStart(false);
        return core;
    }
    private static void SceneControlMatrix()
    {
        foreach (var definition in SceneCatalog.All)
        {
            for (int mask = 0; mask < 16; mask++)
            {
                var options = Opt();
                options.SplitWorlds = false;
                options.Enabled = (mask & 1) != 0;
                options.BasicSplit = (mask & 2) != 0;
                options.SplitScenes = (mask & 4) != 0;
                if ((mask & 8) != 0) options.SceneIds.Add(definition.Id);
                var core = Running(options);
                var sample = S(definition.Scene);
                Check((core.Step(sample, "Running", options).SplitId == definition.Id) == (mask == 15),
                    "scene AND " + definition.Id + " mask=" + mask);
                core.OnSplit(true);
                core.Invalidate();
                options.Enabled = options.BasicSplit = options.SplitScenes = true;
                options.SceneIds.Add(definition.Id);
                Check(core.Step(sample, "Running", options).SplitId == null,
                    "scene consumed across disabled/invalid " + definition.Id + " mask=" + mask);
            }
        }
    }

    private static Snapshot Golf(bool active, int batteries, string ending = null)
    {
        var sample = S("T_Boss", true, ending);
        sample.GolfBattleActive = active;
        sample.GolfBatteries = batteries;
        return sample;
    }

    private static Snapshot GolfMissingBatteries(bool active, string ending = null)
    {
        var sample = S("T_Boss", true, ending);
        sample.GolfBattleActive = active;
        sample.GolfBatteries = null;
        return sample;
    }

    private static void CheckGolfLoss(Options options, int baseline, int depleted,
        string name)
    {
        var core = Running(options);
        core.Step(Golf(true, baseline), "Running", options);
        var loss = Golf(false, depleted, "ending.theodore_death");
        Check(core.Step(loss, "Running", options).SplitId == "ending.theodore_death", name);
    }

    private static void TheodoreZeroBatteryMatrix()
    {
        var options = Opt();
        options.EndingIds.Add("ending.theodore_death");

        CheckGolfLoss(options, 0, -1, "zero-battery active window then decrement finishes");
        CheckGolfLoss(options, -2, -3, "negative-battery active window then decrement finishes");
        CheckGolfLoss(options, 1, 0, "positive-battery active window then zero finishes");

        var continuation = Running(options);
        continuation.Step(Golf(true, 2), "Running", options);
        Check(continuation.Step(Golf(false, 1), "Running", options).SplitId == null,
            "positive battery loss candidate remains above zero");
        Check(continuation.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId
            == "ending.theodore_death", "positive two decrements to zero finish once");
        continuation.OnSplit(true);
        Check(continuation.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "Theodore death is one shot after decrement confirmation");

        var unchangedZero = Running(options);
        unchangedZero.Step(Golf(true, 0), "Running", options);
        Check(unchangedZero.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "unchanged zero battery is not a death");
        var unchangedNegative = Running(options);
        unchangedNegative.Step(Golf(true, -2), "Running", options);
        Check(unchangedNegative.Step(Golf(false, -2, "ending.theodore_death"), "Running", options).SplitId == null,
            "unchanged negative battery is not a death");

        var cutsceneEntry = Running(options);
        Check(cutsceneEntry.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "already-cutscene entry cannot arm a death");
        var falseWindowZero = Running(options);
        falseWindowZero.Step(Golf(true, 0), "Running", options);
        Check(falseWindowZero.Step(Golf(true, 0), "Running", options).SplitId == null,
            "unchanged active zero window is not a death");
        Check(falseWindowZero.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "zero active window to same zero cutscene is not a death");

        var cutsceneAfterDecrement = Running(options);
        cutsceneAfterDecrement.Step(Golf(true, 1), "Running", options);
        cutsceneAfterDecrement.Step(Golf(true, 0), "Running", options);
        Check(cutsceneAfterDecrement.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId
            == "ending.theodore_death", "decrement before cutscene transition is retained");

        var invalid = Running(options);
        invalid.Step(Golf(true, 2), "Running", options);
        invalid.Invalidate();
        Check(invalid.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId
            == "ending.theodore_death", "invalid gap preserves golf corroboration");

        var missing = Running(options);
        missing.Step(GolfMissingBatteries(true), "Running", options);
        Check(missing.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "missing battery field cannot manufacture a baseline");

        var manualAfterLoss = new StateMachine();
        var loss = Golf(false, 0, "ending.theodore_death");
        manualAfterLoss.Step(loss, "NotRunning", options);
        manualAfterLoss.OnStart(false);
        Check(manualAfterLoss.Step(loss, "Running", options).SplitId == null,
            "manual start after loss does not retro-finish");

        var reentry = Running(options);
        reentry.Step(Golf(true, 2), "Running", options);
        reentry.Step(S("T_9"), "Running", options);
        Check(reentry.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "scene exit clears golf corroboration");
        reentry.Step(Golf(true, 1), "Running", options);
        Check(reentry.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId
            == "ending.theodore_death", "reentry requires and accepts a new battle baseline");

        var paused = Running(options);
        paused.Step(Golf(true, 1), "Running", options);
        Check(paused.Step(Golf(false, 0, "ending.theodore_death"), "Paused", options).SplitId == null,
            "paused confirmed death is consumed silently");
        Check(paused.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "paused confirmed death cannot replay");

        var disabled = Running(options);
        disabled.Step(Golf(true, 1), "Running", options);
        options.Enabled = false;
        Check(disabled.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "disabled confirmed death is consumed silently");
        Check(disabled.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId == null,
            "disabled confirmed death cannot replay");
        options.Enabled = true;

        var reset = Running(options);
        reset.Step(Golf(true, 1), "Running", options);
        reset.OnReset();
        reset.Step(S("[Main Menu]"), "NotRunning", options);
        reset.Step(S("Office_1"), "NotRunning", options);
        reset.OnStart(false);
        reset.Step(Golf(true, 1), "Running", options);
        Check(reset.Step(Golf(false, 0, "ending.theodore_death"), "Running", options).SplitId
            == "ending.theodore_death", "reset clears high-water for a second attempt");
    }


    public static int Main()
    {
        SceneControlMatrix();
        TheodoreZeroBatteryMatrix();
        var options = Opt();
        var core = new StateMachine();
        core.Step(S("[Main Menu]"), "NotRunning", options);
        Check(!core.Step(S("Office_1", false), "NotRunning", options).Start, "scene load is not start");
        Check(!core.Step(S("Office_1", null), "NotRunning", options).Start, "unknown control is not start");
        core.Invalidate();
        Check(core.Step(S("Office_1", true), "NotRunning", options).Start, "control-ready survives intro and invalid gap");
        core.OnStart(true);
        Check(core.Step(S("Office_1"), "Running", options).SplitId == null, "accepted start seeds world0");

        core = new StateMachine();
        core.Step(S("Main Menu"), "NotRunning", options);
        Check(!core.Step(S("Office_1"), "NotRunning", options).Start, "do not repair literal menu token");
        core = new StateMachine();
        core.Step(S("[Main Menu]"), "NotRunning", options);
        var loaded = S("Office_1"); loaded.HasCheckpoint = true;
        Check(!core.Step(loaded, "NotRunning", options).Start, "checkpoint load cannot auto-start");

        for (int mask = 0; mask < 16; mask++)
        {
            options = Opt(); options.Enabled = (mask & 1) != 0;
            options.BasicSplit = (mask & 2) != 0; options.SplitWorlds = (mask & 4) != 0;
            if ((mask & 8) == 0) options.Worlds.Clear();
            core = Running(options);
            Check((core.Step(S("A_1"), "Running", options).SplitId != null) == (mask == 15), "world AND mask=" + mask);

            options = Opt(); options.Enabled = (mask & 1) != 0;
            options.BasicSplit = (mask & 2) != 0; options.SplitEndings = (mask & 4) != 0;
            if ((mask & 8) == 0) options.EndingIds.Clear();
            core = Running(options);
            Check((core.Step(S("Subspace_Final", true, Gem), "Running", options).SplitId == Gem) == (mask == 15), "ending AND mask=" + mask);
            core.OnSplit(true);
            options.Enabled = options.BasicSplit = options.SplitEndings = true; options.EndingIds.Add(Gem);
            Check(core.Step(S("Subspace_Final", true, Gem), "Running", options).SplitId == null, "no retroactive ending mask=" + mask);
        }

        options = Opt(); core = Running(options);
        Check(core.Step(S("A_1"), "Running", options).SplitId == "world1", "world pending setup");
        options.BasicSplit = false;
        Check(core.Step(S("A_1"), "Running", options).SplitId == null, "disabled base clears pending");
        options.BasicSplit = true;
        Check(core.Step(S("A_1"), "Running", options).SplitId == null, "pending cannot resurrect");
        core.Invalidate();
        Check(core.Step(S("[Main Menu]"), "Running", options).Reset, "menu reset survives invalid gap");

        options = Opt(); core = Running(options);
        Check(core.Step(S("Subspace_Final", true, Gem), "Paused", options).SplitId == null, "paused ending consumed silently");
        Check(core.Step(S("Subspace_Final", true, Gem), "Running", options).SplitId == null, "paused ending cannot replay");
        core.OnReset(); core.Step(S("[Main Menu]"), "NotRunning", options);
        core.Step(S("Office_1"), "NotRunning", options); core.OnStart(true);
        Check(core.Step(S("Subspace_Final", true, Gem), "Running", options).SplitId == Gem, "next run clears ending latch");
        core.OnSplit(true);
        Check(!core.Step(S("Subspace_Final", true, Gem), "Ended", options).Reset, "ended result retained away from menu");

        options = Opt(); core = new StateMachine();
        var initial = S("Subspace_Final", true, Gem);
        core.Step(initial, "NotRunning", options);
        initial.EndingSignals.Clear();
        core.OnStart(false);
        Check(core.Step(S("Subspace_Final", true, Gem), "Running", options).SplitId == null, "manual baseline copies mutable ending set");
        core = new StateMachine(); core.OnStart(false);
        Check(core.Step(S("Subspace_Final", true, Gem), "Running", options).SplitId == null, "pre-valid manual start seeds ending");
        Check(core.Step(S("Subspace_Final", true, Gem), "Running", options).SplitId == null, "seeded ending one shot");

        options = Opt(); options.EndingIds.Add("ending.theodore_victory");
        core = Running(options);
        var end9 = S("T_Boss", true, "ending.theodore_victory");
        end9.TheodoreHp = 0;
        Check(core.Step(end9, "Running", options).SplitId == "ending.theodore_victory",
            "reader-owned T_Boss signal produces Ending9");
        core.OnSplit(true);
        Check(core.Step(end9, "Running", options).SplitId == null,
            "reader-owned Ending9 cannot double split");

        var wrongSceneCore = Running(options);
        var fakeEnd9 = S("T_End", true, "ending.theodore_victory");
        fakeEnd9.TheodoreHp = 0;
        Check(wrongSceneCore.Step(fakeEnd9, "Running", options).SplitId == null,
            "T_End cannot fake reader-owned Ending9");
        var afterLoss = S("T_End", true, "ending.theodore_victory");
        afterLoss.Abyss = 300;
        Check(wrongSceneCore.Step(afterLoss, "Running", options).SplitId == null,
            "saved T_End cannot fake reader-owned Ending9");

        Console.WriteLine("Parent regression assertions=" + checks + " failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
