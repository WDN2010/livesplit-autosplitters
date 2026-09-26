using System;
using System.Collections.Generic;

namespace Peppered
{
    public class Snapshot
    {
        public bool Valid;
        public string Scene;
        public int Abyss;
        public int? MerdekaStage;
        public bool? Bigman;
        public bool? Prison;
        public bool? HasCheckpoint;
        public bool? DialoguePlaying;
        public bool? TvStartActive;
        public bool? StartReady;
        public bool? GolfBattleActive;
        public int? GolfBatteries;
        public int? TheodoreHp;
        public bool TheodoreMetadataReady;
        public int? KarminaChoice;
        public bool KarminaMetadataReady;
        public int? SurrenderChoice;
        public bool SurrenderMetadataReady;
        public HashSet<string> EndingSignals;
        public bool BoundaryMetadataReady;
        public bool TvStartMetadataReady;
    }

    public enum SceneCategory
    {
        Coarse,
        Detailed,
        Branch
    }

    public enum EndingVariant
    {
        None,
        With,
        Against
    }

    public sealed class SceneDefinition
    {
        public readonly string Id;
        public readonly string Scene;
        public readonly string Label;
        public readonly string SettingKey;
        public readonly int World;
        public readonly SceneCategory Category;
        public readonly bool DefaultEnabled;

        public SceneDefinition(string id, string scene, string label, int world,
            SceneCategory category, bool defaultEnabled)
        {
            Id = id;
            Scene = scene;
            Label = label;
            SettingKey = id;
            World = world;
            Category = category;
            DefaultEnabled = defaultEnabled;
        }
    }

    public sealed class EndingDefinition
    {
        public readonly string Id;
        public readonly string Label;
        public readonly string PhysicalSignal;
        public readonly int Number;
        public readonly EndingVariant Variant;
        public readonly bool DefaultEnabled;

        public EndingDefinition(string id, string label, string physicalSignal,
            int number, EndingVariant variant)
        {
            Id = id;
            Label = label;
            PhysicalSignal = physicalSignal;
            Number = number;
            Variant = variant;
            DefaultEnabled = false;
        }
    }

    public static class SceneCatalog
    {
        private static readonly SceneDefinition[] scenes = new SceneDefinition[]
        {
            new SceneDefinition("scene.w0.coarse.office2", "Office_2", "Office_2 (Pitch the Investors)", 0, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w0.coarse.office3", "Office_3", "Office_3 (Meet the Accountant)", 0, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w0.coarse.office4", "Office_4", "Office_4 (Family Reunion)", 0, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w0.branch.office_balcony_1", "Office_Balcony 1", "Office_Balcony 1 (Life Star on Balcony)", 0, SceneCategory.Branch, false),
            new SceneDefinition("scene.w1.coarse.a2", "A_2", "A_2 (Merdeka Follows You)", 1, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w1.coarse.a3", "A_3", "A_3 (Shock Platforms)", 1, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w1.coarse.a4", "A_4", "A_4 (Cynthia Evidence)", 1, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w1.coarse.a5", "A_5", "A_5 (Block Puzzle)", 1, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w1.coarse.a6_bf", "A_6_BF", "A_6_BF (Merdeka Bullet Fight)", 1, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w1.detailed.a3_1", "A_3.1", "A_3.1 (Red Coin Machine)", 1, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w1.detailed.a4_5", "A_4.5", "A_4.5 (Falling Desks)", 1, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w1.detailed.a4_8", "A_4.8", "A_4.8 (Fish Window Shocks)", 1, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w1.branch.secret_level_1", "Secret_Level_1", "Secret_Level_1 (Bottom of the Abyss)", 1, SceneCategory.Branch, false),
            new SceneDefinition("scene.w1.branch.secret_level_3", "Secret_Level_3", "Secret_Level_3 (Unlockable Abyss Platforms)", 1, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.coarse.a8", "A_8", "A_8 (Apartment District)", 2, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w2.coarse.a9", "A_9", "A_9 (Getaway Coffee)", 2, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w2.coarse.a10", "A_10", "A_10 (Bug Kids Playground)", 2, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w2.coarse.a12_prison", "A_12_Prison", "A_12_Prison (Police Station Cell)", 2, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w2.coarse.a13_the_court", "A_13_The_Court", "A_13_The_Court (Televised Courtroom)", 2, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w2.coarse.a14", "A_14", "A_14 (Backroom Paper Puzzle)", 2, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w2.coarse.a_end", "A_End", "A_End (Merdeka Theater)", 2, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w2.detailed.a7_1", "A_7.1", "A_7.1 (Dumpster Side Street)", 2, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w2.branch.a8_library", "A_8_Library", "A_8_Library (Library)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.a9_1", "A_9.1", "A_9.1 (Red Coin Machine)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.a10_1", "A_10.1", "A_10.1 (Electronics Store)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.a10_2", "A_10.2", "A_10.2 (Merdeka's Childhood)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.a11", "A_11", "A_11 (Supermarket)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.a11_2", "A_11.2", "A_11.2 (Abyss Train)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.a15", "A_15", "A_15 (Theater Backstage)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.s0", "S_0", "S_0 (Theater Entrance)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.s1", "S_1", "S_1 (Drone Encounter)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.s2", "S_2", "S_2 (Caviar Vending Machine)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.s3", "S_3", "S_3 (Elevator Ride)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w2.branch.s4", "S_4", "S_4 (Playground and Bar)", 2, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.coarse.b1", "B_1", "B_1 (Starstruck Diner)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b1_1", "B_1.1", "B_1.1 (Caviar Vending Area)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b2", "B_2", "B_2 (Life Stars Shop Basement)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b4_1", "B_4.1", "B_4.1 (Muffin Dock)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b4", "B_4", "B_4 (Cultist Showdown)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b5", "B_5", "B_5 (Gambler's Cups)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b6_0", "B_6.0", "B_6.0 (Tunnel Entrance)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b6_1", "B_6.1", "B_6.1 (Karmina's Goons)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b6_2", "B_6.2", "B_6.2 (Tunnel Elevator)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b6_4", "B_6.4", "B_6.4 (Elevator Passage)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b6_6", "B_6.6", "B_6.6 (Karmina Deal)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b_city", "B_City", "B_City (Cultist City)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.coarse.b10_1", "B_10.1", "B_10.1 (Rainy Dock)", 3, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w3.detailed.b1_0", "B_1.0", "B_1.0 (Red Coin Machine)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.detailed.b5_1", "B_5.1", "B_5.1 (Cultist Graffiti)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.detailed.b5_2", "B_5.2", "B_5.2 (Coins Challenge)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.detailed.b5_3", "B_5.3", "B_5.3 (Box Retrieval)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.detailed.b5_4", "B_5.4", "B_5.4 (Locker Room Segment A)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.detailed.b5_5", "B_5.5", "B_5.5 (Locker Room Segment B)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.detailed.b6_0_1", "B_6.0.1", "B_6.0.1 (Tunnel Arch)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.detailed.b6_3", "B_6.3", "B_6.3 (Tunnel Checkpoint)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.detailed.b6_5", "B_6.5", "B_6.5 (Tunnel Plaque)", 3, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w3.branch.b3", "B_3", "B_3 (Reika Street)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.b3_1", "B_3.1", "B_3.1 (Abyss Train)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.b_merdeka", "B_Merdeka", "B_Merdeka (Merdeka Boss)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.b_aftermath", "B_Aftermath", "B_Aftermath (Karmina's Shop)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.b_store", "B_Store", "B_Store (Cultist Coffee Shop)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.subspace4", "Subspace_4", "Subspace_4 (Chair Course)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.subspace5", "Subspace_5", "Subspace_5 (Chair Course Complete)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.subspace_final", "Subspace_Final", "Subspace_Final (God of Life Ending)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.b10", "B_10", "B_10 (News Boat Dock)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w3.branch.b_end", "B_End", "B_End (Diner Basement Finale)", 3, SceneCategory.Branch, false),
            new SceneDefinition("scene.w4.coarse.g1", "G_1", "G_1 (Exploding Block Course)", 4, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w4.coarse.g2", "G_2", "G_2 (Chandelier Trap Course)", 4, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w4.coarse.g3", "G_3", "G_3 (Beach Sand Course)", 4, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w4.coarse.g4", "G_4", "G_4 (Trap-Heavy Course)", 4, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w4.coarse.g6", "G_6", "G_6 (Chandelier Cluster Course)", 4, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w4.coarse.g7", "G_7", "G_7 (Mailbox Course)", 4, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w4.coarse.g8", "G_8", "G_8 (Theo Moving-TNT Course)", 4, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w4.coarse.g9", "G_9", "G_9 (Castle Chandelier Course)", 4, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w4.detailed.g5_1", "G_5.1", "G_5.1 (Theo Chandelier Traps)", 4, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w4.detailed.g5_2", "G_5.2", "G_5.2 (Rocky TNT Course)", 4, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w4.detailed.g5_3", "G_5.3", "G_5.3 (Tree-Lined TNT Course)", 4, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w4.detailed.g5_4", "G_5.4", "G_5.4 (Lantern Trap Course)", 4, SceneCategory.Detailed, false),
            new SceneDefinition("scene.w5.coarse.t1", "T_1", "T_1 (Mansion Opening)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.coarse.t2", "T_2", "T_2 (Thorn Hedge Maze)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.coarse.t3", "T_3", "T_3 (Chandelier Trap)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.coarse.t4", "T_4", "T_4 (Theo Diary Flashback)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.coarse.t5", "T_5", "T_5 (Launch Barrel Traps)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.coarse.t6", "T_6", "T_6 (Buzzsaw Trap Course)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.coarse.t7", "T_7", "T_7 (Mansion Flashback)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.coarse.t8", "T_8", "T_8 (Mansion Door Approach)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.coarse.t9", "T_9", "T_9 (Library Prison)", 5, SceneCategory.Coarse, true),
            new SceneDefinition("scene.w5.branch.t_boss", "T_Boss", "T_Boss (Theodore Boss Battle)", 5, SceneCategory.Branch, false),
        };

        public static SceneDefinition[] All
        {
            get { return (SceneDefinition[])scenes.Clone(); }
        }

        public static int RegularCount
        {
            get { return scenes.Length; }
        }

        public static string GroupName(SceneCategory category)
        {
            switch (category)
            {
                case SceneCategory.Coarse: return "Main";
                case SceneCategory.Detailed: return "Detailed";
                default: return "Branches";
            }
        }

        public static bool TryFind(string scene, out SceneDefinition definition)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].Scene == scene)
                {
                    definition = scenes[i];
                    return true;
                }
            }
            definition = null;
            return false;
        }

        public static bool TryFindById(string id, out SceneDefinition definition)
        {
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].Id == id)
                {
                    definition = scenes[i];
                    return true;
                }
            }
            definition = null;
            return false;
        }
    }

    public static class EndingCatalog
    {
        public const string ExecutionBlack = "ending.execution_black";
        public const string FamilyBlack = "ending.family_black";
        public const string BarDialogue = "ending.bar_dialogue";
        public const string FinalDeathBlack = "ending.final_death_black";
        public const string KarminaTv = "ending.karmina_tv";
        public const string KarminaTvStart = "ending.karmina_tv_start";
        public const string MerdekaVictory = "ending.merdeka_victory";
        public const string MerdekaDefeat = "ending.merdeka_defeat";
        public const string TheodoreVictory = "ending.theodore_victory";
        public const string TheodoreDeath = "ending.theodore_death";
        public const string GemDialogue = "ending.gem_dialogue";
        public const string KarminaFinalChoice = "ending.karmina_final_choice";
        public const string MerdekaSurrender = "ending.merdeka_surrender";
        public const string MasterId = "splitEndings";

        private static readonly string[] physicalSignals = new string[] {
            ExecutionBlack, FamilyBlack, BarDialogue, FinalDeathBlack,
            KarminaTvStart, MerdekaVictory, MerdekaDefeat, TheodoreVictory,
            TheodoreDeath, GemDialogue, KarminaFinalChoice, MerdekaSurrender
        };

        private static readonly EndingDefinition[] endings = new EndingDefinition[] {
            new EndingDefinition("ending.1", "Ending 1: Guillotine — first execution black frame", "ending.execution_black", 1, EndingVariant.None),
            new EndingDefinition("ending.2", "Ending 2: Family Reunion — first abrupt black frame", "ending.family_black", 2, EndingVariant.None),
            new EndingDefinition("ending.3", "Ending 3: Unsung Hero — first bar dialogue", "ending.bar_dialogue", 3, EndingVariant.None),
            new EndingDefinition("ending.4", "Ending 4: Final death — first permanent-death black frame", "ending.final_death_black", 4, EndingVariant.None),
            new EndingDefinition("ending.5.with", "Ending 5 (With Karmina): start watching TV with Karmina", KarminaTvStart, 5, EndingVariant.With),
            new EndingDefinition("ending.5.against", "Ending 5 (Against Karmina): final CONTINUE / GIVE UP choice", KarminaFinalChoice, 5, EndingVariant.Against),
            new EndingDefinition("ending.6.with", "Ending 6 (With Karmina): start watching TV with Karmina", KarminaTvStart, 6, EndingVariant.With),
            new EndingDefinition("ending.6.against", "Ending 6 (Against Karmina): final CONTINUE / GIVE UP choice", KarminaFinalChoice, 6, EndingVariant.Against),
            new EndingDefinition("ending.7.with", "Ending 7 (With Karmina): start watching TV with Karmina", KarminaTvStart, 7, EndingVariant.With),
            new EndingDefinition("ending.7.against", "Ending 7 (Against Karmina): final CONTINUE / GIVE UP choice", KarminaFinalChoice, 7, EndingVariant.Against),
            new EndingDefinition("ending.8", "Ending 8: Merdeka surrender — final GIVE HER THE STARS confirmation", MerdekaSurrender, 8, EndingVariant.None),
            new EndingDefinition("ending.8.friend_loss", "Ending 8 (optional friendship route): Merdeka defeat - NOT Ending 7", MerdekaDefeat, 8, EndingVariant.None),
            new EndingDefinition("ending.9", "Ending 9: Theodore victory — first T_Boss HP-zero victory", "ending.theodore_victory", 9, EndingVariant.None),
            new EndingDefinition("ending.10", "Ending 10: Theodore death — golf death cut", "ending.theodore_death", 10, EndingVariant.None),
            new EndingDefinition("ending.11", "Ending 11: Secret GEM — first final-dialogue dash", "ending.gem_dialogue", 11, EndingVariant.None),
        };

        public static string[] PhysicalSignals
        {
            get { return (string[])physicalSignals.Clone(); }
        }

        public static EndingDefinition[] All
        {
            get { return (EndingDefinition[])endings.Clone(); }
        }

        public static bool TryFindById(string id, out EndingDefinition definition)
        {
            for (int i = 0; i < endings.Length; i++)
            {
                if (endings[i].Id == id)
                {
                    definition = endings[i];
                    return true;
                }
            }
            definition = null;
            return false;
        }
    }

    public class Options
    {
        public bool Enabled;
        public bool AutoStart;
        public bool AutoReset;
        public bool SplitWorlds;
        public bool SplitScenes;
        public bool SplitEndings;
        public bool BasicStart;
        public bool BasicSplit;
        public bool BasicReset;
        public HashSet<int> Worlds;
        public HashSet<string> SceneIds;
        // These are physical event keys. Several visible category leaves may map
        // to the same key (the With/Against 5/6/7 choices).
        public HashSet<string> EndingIds;
    }

    public class Decision
    {
        public bool Start;
        public bool Reset;
        public string SplitId;
        public string Diagnostic;
    }

    public class StateMachine
    {
        private const string Menu = "[Main Menu]";
        private const string Office = "Office_1";

        private enum PendingKind
        {
            World,
            Scene,
            Ending
        }

        private sealed class PendingEvent
        {
            public PendingKind Kind;
            public int World;
            public string Id;

            public string PublicId
            {
                get { return Kind == PendingKind.World ? "world" + World : Id; }
            }
        }

        private sealed class Observation
        {
            public string Scene;
            public int Abyss;
            public int? MerdekaStage;
            public bool? Bigman;
            public bool? Prison;
            public bool? HasCheckpoint;
            public bool? DialoguePlaying;
            public bool? TvStartActive;
            public bool? StartReady;
            public bool? GolfBattleActive;
            public int? GolfBatteries;
            public int? TheodoreHp;
            public bool TheodoreMetadataReady;
            public int? KarminaChoice;
            public bool KarminaMetadataReady;
            public int? SurrenderChoice;
            public bool SurrenderMetadataReady;
            public HashSet<string> EndingSignals;
            public bool BoundaryMetadataReady;
            public bool TvStartMetadataReady;
        }

        private readonly HashSet<int> worldLatches = new HashSet<int>();
        private readonly HashSet<string> sceneLatches = new HashSet<string>();
        private readonly HashSet<string> endingLatches = new HashSet<string>();
        private readonly Queue<PendingEvent> pending = new Queue<PendingEvent>();
        private Observation previous;
        private bool hasPrevious;
        private bool acceptedRun;
        private bool runEnded;
        private bool seenMenu;
        private bool startArmed;
        private bool golfBattleArmed;
        private int? golfBattleBatteryHighWater;
        private bool observedNonMenu;
        private bool resetLatched;
        private bool waitingForFirstValidSample;
        private int highestWorldFloor = -1;

        public Decision Step(Snapshot snapshot, string phase, Options options)
        {
            Decision decision = new Decision();
            if (snapshot == null || !snapshot.Valid || String.IsNullOrEmpty(snapshot.Scene))
            {
                Invalidate();
                decision.Diagnostic = "invalid-observation";
                return decision;
            }

            Observation current = Copy(snapshot);
            if (waitingForFirstValidSample)
            {
                SeedObservation(current);
                previous = current;
                hasPrevious = true;
                waitingForFirstValidSample = false;
                return decision;
            }

            bool enabled = options != null && options.Enabled;
            bool isRunning = phase == "Running";
            bool isResetPhase = isRunning || phase == "Paused" || phase == "Ended";
            bool isNotRunning = phase == "NotRunning";
            bool currentMenu = current.Scene == Menu;
            bool currentOffice = current.Scene == Office;
            bool startObserved = !acceptedRun && isNotRunning && startArmed && currentOffice
                && current.StartReady == true && current.HasCheckpoint == false;

            bool resetObserved = isResetPhase && currentMenu && observedNonMenu && !resetLatched;
            bool resetCandidate = resetObserved && options != null && options.AutoReset
                && options.BasicReset;

            int world = -1;
            bool worldObserved = false;
            bool worldFloorAllowed = false;
            SceneDefinition sceneDefinition = null;
            bool sceneObserved = false;
            bool sceneFloorAllowed = false;
            int floorBefore = highestWorldFloor;
            if (acceptedRun)
            {
                if (TryWorld(current.Scene, out world) && !worldLatches.Contains(world))
                {
                    worldObserved = true;
                    worldFloorAllowed = world >= floorBefore;
                    if (world > highestWorldFloor) highestWorldFloor = world;
                }
                if (SceneCatalog.TryFind(current.Scene, out sceneDefinition)
                    && !sceneLatches.Contains(sceneDefinition.Id))
                {
                    sceneObserved = true;
                    sceneFloorAllowed = sceneDefinition.World >= floorBefore;
                    if (sceneDefinition.World > highestWorldFloor)
                        highestWorldFloor = sceneDefinition.World;
                }
            }

            bool worldCandidate = worldObserved && worldFloorAllowed && isRunning
                && !runEnded && WorldEnabled(options, world);
            bool sceneCandidate = sceneObserved && sceneFloorAllowed && isRunning
                && !runEnded && SceneEnabled(options, sceneDefinition);

            if (current.Scene != "T_Boss")
            {
                golfBattleArmed = false;
                golfBattleBatteryHighWater = null;
            }
            else if (acceptedRun && current.GolfBattleActive == true
                && current.GolfBatteries.HasValue)
            {
                golfBattleArmed = true;
                if (!golfBattleBatteryHighWater.HasValue
                    || current.GolfBatteries.Value > golfBattleBatteryHighWater.Value)
                    golfBattleBatteryHighWater = current.GolfBatteries.Value;
            }

            List<string> endingObserved = new List<string>();
            if (acceptedRun && current.EndingSignals != null)
            {
                string[] signals = EndingCatalog.PhysicalSignals;
                for (int i = 0; i < signals.Length; i++)
                {
                    string signal = signals[i];
                    if (signal == EndingCatalog.TheodoreDeath
                        && !TheodoreDeathCorroborated(current)) continue;
                    if (signal == EndingCatalog.TheodoreVictory
                        && current.Scene != "T_Boss") continue;
                    if (signal == EndingCatalog.MerdekaVictory
                        && current.Scene != "B_Merdeka") continue;
                    if (signal == EndingCatalog.MerdekaDefeat
                        && current.Scene != "B_Merdeka") continue;
                    if (signal == EndingCatalog.MerdekaSurrender
                        && current.Scene != "B_Merdeka") continue;
                    if (signal == EndingCatalog.KarminaTvStart
                        && (current.Scene != "B_Aftermath"
                            || !current.BoundaryMetadataReady
                            || !current.TvStartMetadataReady
                            || current.TvStartActive != true)) continue;
                    if (signal == EndingCatalog.KarminaFinalChoice
                        && (current.Scene != "B_End"
                            || !current.BoundaryMetadataReady
                            || !current.KarminaMetadataReady)) continue;
                    if (current.EndingSignals.Contains(signal) && !endingLatches.Contains(signal))
                        endingObserved.Add(signal);
                }
            }

            bool endingCandidate = isRunning && !runEnded && endingObserved.Count > 0;
            List<string> diagnostics = new List<string>();
            if (startObserved) diagnostics.Add("candidate:start");
            if (startObserved && !enabled) diagnostics.Add("candidate:start-disabled");
            if (resetObserved) diagnostics.Add("candidate:reset");
            if (worldObserved) diagnostics.Add("candidate:split:world" + world);
            if (sceneObserved) diagnostics.Add("candidate:split:" + sceneDefinition.Id);
            for (int i = 0; i < endingObserved.Count; i++)
                diagnostics.Add("candidate:finish:" + endingObserved[i]);
            if (diagnostics.Count > 0)
                decision.Diagnostic = String.Join(";", diagnostics.ToArray());

            // The menu arms only a fresh new-game transition. Intro polls retain
            // this arming until StartReady is proven; other scenes cancel it.
            if (!acceptedRun)
            {
                if (currentMenu)
                {
                    seenMenu = true;
                    startArmed = true;
                }
                else if (currentOffice)
                {
                    seenMenu = false;
                    if (startObserved || current.HasCheckpoint == true
                        || (current.StartReady == true && current.HasCheckpoint.HasValue))
                        startArmed = false;
                }
                else
                {
                    seenMenu = false;
                    startArmed = false;
                }
            }
            else if (currentMenu)
            {
                seenMenu = true;
            }
            else
            {
                seenMenu = false;
            }

            if (currentMenu)
            {
                if (observedNonMenu) resetLatched = true;
            }
            else
            {
                observedNonMenu = true;
                resetLatched = false;
            }
            if (resetObserved) resetLatched = true;

            if (worldObserved) worldLatches.Add(world);
            if (sceneObserved) sceneLatches.Add(sceneDefinition.Id);
            for (int i = 0; i < endingObserved.Count; i++) endingLatches.Add(endingObserved[i]);

            if (!enabled || options == null || !options.BasicSplit)
                pending.Clear();
            else
                RetainEnabledPending(options);
            if (!isRunning || !acceptedRun || runEnded || resetObserved)
                pending.Clear();

            if (enabled && isRunning && acceptedRun && !runEnded)
            {
                if (worldCandidate)
                    pending.Enqueue(new PendingEvent { Kind = PendingKind.World, World = world });
                if (sceneCandidate)
                    pending.Enqueue(new PendingEvent { Kind = PendingKind.Scene, Id = sceneDefinition.Id });
                if (endingCandidate)
                {
                    for (int i = 0; i < endingObserved.Count; i++)
                    {
                        string id = endingObserved[i];
                        if (EndingEnabled(options, id))
                            pending.Enqueue(new PendingEvent { Kind = PendingKind.Ending, Id = id });
                    }
                }
            }

            if (enabled && startObserved && options != null && options.AutoStart
                && options.BasicStart)
                decision.Start = true;
            if (enabled && resetCandidate) decision.Reset = true;
            if (phase == "Ended") runEnded = true;

            previous = current;
            hasPrevious = true;
            if (enabled && isRunning && acceptedRun && !runEnded && pending.Count > 0)
                decision.SplitId = pending.Peek().PublicId;
            return decision;
        }

        private bool TheodoreDeathCorroborated(Observation observation)
        {
            return observation.Scene == "T_Boss"
                && golfBattleArmed
                && golfBattleBatteryHighWater.HasValue
                && observation.GolfBatteries.HasValue
                && observation.GolfBatteries.Value <= 0
                && observation.GolfBatteries.Value < golfBattleBatteryHighWater.Value;
        }

        private void SeedObservation(Observation observation)
        {
            golfBattleArmed = observation.Scene == "T_Boss"
                && observation.GolfBattleActive == true
                && observation.GolfBatteries.HasValue;
            golfBattleBatteryHighWater = golfBattleArmed
                ? observation.GolfBatteries.Value : (int?)null;
            int world;
            if (TryWorld(observation.Scene, out world))
            {
                worldLatches.Add(world);
                if (world > highestWorldFloor) highestWorldFloor = world;
            }
            SceneDefinition sceneDefinition;
            if (SceneCatalog.TryFind(observation.Scene, out sceneDefinition))
            {
                sceneLatches.Add(sceneDefinition.Id);
                if (sceneDefinition.World > highestWorldFloor)
                    highestWorldFloor = sceneDefinition.World;
            }
            if (observation.EndingSignals != null)
            {
                foreach (string signal in observation.EndingSignals)
                    if (IsPhysicalEnding(signal)
                        && (signal != EndingCatalog.TheodoreDeath
                            || TheodoreDeathCorroborated(observation))
                        && (signal != EndingCatalog.TheodoreVictory
                            || observation.Scene == "T_Boss")
                        && (signal != EndingCatalog.MerdekaVictory
                            || observation.Scene == "B_Merdeka")
                        && (signal != EndingCatalog.MerdekaDefeat
                            || observation.Scene == "B_Merdeka")
                        && (signal != EndingCatalog.MerdekaSurrender
                            || observation.Scene == "B_Merdeka")
                        && (signal != EndingCatalog.KarminaTvStart
                            || (observation.Scene == "B_Aftermath"
                                && observation.BoundaryMetadataReady
                                && observation.TvStartMetadataReady
                                && observation.TvStartActive == true))
                        && (signal != EndingCatalog.KarminaFinalChoice
                            || (observation.Scene == "B_End"
                                && observation.BoundaryMetadataReady
                                && observation.KarminaMetadataReady)))
                        endingLatches.Add(signal);
            }
            if (observation.Scene == Menu)
            {
                seenMenu = true;
                startArmed = true;
                observedNonMenu = false;
                resetLatched = false;
            }
            else
            {
                seenMenu = false;
                startArmed = false;
                observedNonMenu = true;
                resetLatched = false;
            }
        }

        public void OnStart(bool automatic)
        {
            acceptedRun = true;
            runEnded = false;
            worldLatches.Clear();
            sceneLatches.Clear();
            endingLatches.Clear();
            golfBattleArmed = false;
            golfBattleBatteryHighWater = null;
            highestWorldFloor = -1;
            pending.Clear();
            waitingForFirstValidSample = !automatic && !hasPrevious;
            observedNonMenu = hasPrevious && previous.Scene != Menu;
            if (hasPrevious) SeedObservation(previous);
        }

        public void OnSplit(bool automatic)
        {
            if (automatic && pending.Count > 0) pending.Dequeue();
        }

        public void OnReset()
        {
            acceptedRun = false;
            runEnded = false;
            worldLatches.Clear();
            sceneLatches.Clear();
            endingLatches.Clear();
            golfBattleArmed = false;
            golfBattleBatteryHighWater = null;
            highestWorldFloor = -1;
            pending.Clear();
            observedNonMenu = false;
            waitingForFirstValidSample = false;
            seenMenu = hasPrevious && previous.Scene == Menu;
            startArmed = seenMenu;
            resetLatched = seenMenu;
        }

        public void Attach()
        {
            previous = null;
            hasPrevious = false;
            acceptedRun = false;
            runEnded = false;
            seenMenu = false;
            startArmed = false;
            observedNonMenu = false;
            resetLatched = false;
            waitingForFirstValidSample = false;
            highestWorldFloor = -1;
            worldLatches.Clear();
            sceneLatches.Clear();
            endingLatches.Clear();
            golfBattleArmed = false;
            golfBattleBatteryHighWater = null;
            pending.Clear();
        }

        public void Invalidate()
        {
            previous = null;
            hasPrevious = false;
            pending.Clear();
        }

        private static Observation Copy(Snapshot snapshot)
        {
            HashSet<string> signals = snapshot.EndingSignals == null
                ? new HashSet<string>() : new HashSet<string>(snapshot.EndingSignals);
            return new Observation {
                Scene = snapshot.Scene,
                Abyss = snapshot.Abyss,
                MerdekaStage = snapshot.MerdekaStage,
                Bigman = snapshot.Bigman,
                Prison = snapshot.Prison,
                HasCheckpoint = snapshot.HasCheckpoint,
                DialoguePlaying = snapshot.DialoguePlaying,
                TvStartActive = snapshot.TvStartActive,
                StartReady = snapshot.StartReady,
                GolfBattleActive = snapshot.GolfBattleActive,
                GolfBatteries = snapshot.GolfBatteries,
                TheodoreHp = snapshot.TheodoreHp,
                TheodoreMetadataReady = snapshot.TheodoreMetadataReady,
                KarminaChoice = snapshot.KarminaChoice,
                KarminaMetadataReady = snapshot.KarminaMetadataReady,
                SurrenderChoice = snapshot.SurrenderChoice,
                SurrenderMetadataReady = snapshot.SurrenderMetadataReady,
                EndingSignals = signals,
                BoundaryMetadataReady = snapshot.BoundaryMetadataReady,
                TvStartMetadataReady = snapshot.TvStartMetadataReady
            };
        }

        private static bool IsPhysicalEnding(string id)
        {
            string[] signals = EndingCatalog.PhysicalSignals;
            for (int i = 0; i < signals.Length; i++)
                if (signals[i] == id) return true;
            return false;
        }

        private static bool IsWorldScene(string scene, int id)
        {
            switch (id)
            {
                case 0: return scene == "Office_1";
                case 1: return scene == "A_1";
                case 2: return scene == "A_7";
                case 3: return scene == "B_0";
                case 4: return scene == "G_0";
                case 5: return scene == "G_End";
                default: return false;
            }
        }

        private static bool TryWorld(string scene, out int id)
        {
            for (int i = 0; i <= 5; i++)
            {
                if (IsWorldScene(scene, i))
                {
                    id = i;
                    return true;
                }
            }
            id = -1;
            return false;
        }

        private static bool WorldEnabled(Options options, int id)
        {
            return options != null && options.BasicSplit && options.SplitWorlds
                && options.Worlds != null && options.Worlds.Contains(id);
        }

        private static bool SceneEnabled(Options options, SceneDefinition definition)
        {
            return options != null && definition != null && options.BasicSplit
                && options.SplitScenes && options.SceneIds != null
                && options.SceneIds.Contains(definition.Id);
        }

        private static bool SceneEnabledById(Options options, string id)
        {
            SceneDefinition definition;
            return SceneCatalog.TryFindById(id, out definition)
                && SceneEnabled(options, definition);
        }

        private static bool EndingEnabled(Options options, string id)
        {
            return options != null && id != null && options.BasicSplit
                && options.SplitEndings && options.EndingIds != null
                && options.EndingIds.Contains(id);
        }

        private static bool PendingEnabled(Options options, PendingEvent item)
        {
            if (item.Kind == PendingKind.World) return WorldEnabled(options, item.World);
            if (item.Kind == PendingKind.Scene) return SceneEnabledById(options, item.Id);
            return EndingEnabled(options, item.Id);
        }

        private void RetainEnabledPending(Options options)
        {
            int queued = pending.Count;
            for (int i = 0; i < queued; i++)
            {
                PendingEvent item = pending.Dequeue();
                if (PendingEnabled(options, item)) pending.Enqueue(item);
            }
        }
    }
}
