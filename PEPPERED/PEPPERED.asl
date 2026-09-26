// PEPPERED original Windows x64 / Unity 2021.2.20f1 Mono.
// EXPERIMENTAL: diagnostic-only by default; no live Windows validation yet.
// Read-only game access. No hooks, remote calls, or timing estimates.
state("PEPPERED") { }

startup
{
    vars.HashBytes = (Func<byte[], string>)(bytes => {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    });
    if (!File.Exists("Components/asl-help"))
        throw new InvalidDataException("PEPPERED: required dependency missing: Components/asl-help");
    byte[] helperBytes = File.ReadAllBytes("Components/asl-help");
    if ((string)vars.HashBytes(helperBytes) != "c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5")
        throw new InvalidDataException("PEPPERED: asl-help does not match the reviewed dependency.");
    if (!File.Exists("Components/Peppered.AutoSplitter.dll"))
        throw new InvalidDataException("PEPPERED: required dependency missing: Components/Peppered.AutoSplitter.dll");
    byte[] logicBytes = File.ReadAllBytes("Components/Peppered.AutoSplitter.dll");
    vars.HashFile = (Func<string, string>)(path => {
        using (var stream = File.OpenRead(path))
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    });
    if ((string)vars.HashBytes(logicBytes) != "fe378022fb5c5a36700ee6fb76b68f1a37ce1cef1aabd41fa993b04ccbe6b270")
        throw new InvalidDataException("PEPPERED: logic/reader DLL does not match this ASL.");

    vars.Code = Assembly.Load(logicBytes);
    vars.Core = vars.Code.CreateInstance("Peppered.StateMachine");
    vars.RebindOnAttach = false;
    vars.Reader = vars.Code.CreateInstance("Peppered.ReadOnlyReader");
    vars.Decision = vars.Code.CreateInstance("Peppered.Decision");
    vars.SupportedBuild = false;
    vars.ReaderConfigured = false;
    vars.MonoMetadata = null;
    vars.LastMetadataScene = "";
    vars.LastTheodoreMetadataScene = "";
    vars.LastSurrenderMetadataScene = "";
    vars.LastKarminaMetadataScene = "";
    vars.LastTvStartMetadataScene = "";
    vars.TheodoreSceneAddress = IntPtr.Zero;
    vars.MetadataRetryAfter = 0L;
    vars.OptionalMetadataRetryAfter = 0L;
    vars.AutoStarting = false;
    vars.AutoSplitting = false;
    vars.LastTrace = "";
    vars.LastFault = "";
    vars.TimerModel = new TimerModel { CurrentState = timer };
    // asl-help owns vars.Log and replaces it during construction.
    // Keep the bounded file/DbgView sink under a script-specific name.
    // Startup runs before the saved checkbox preset is fully applied.
    // Stay silent until update, and check the current opt-in at every call.
    vars.PepperedLoggingReady = false;
    vars.PepperedLogBannerSent = false;
    vars.PepperedLog = (Action<string>)(text => {
        // Use a positive guard: ASL rewrites bare returns inside action bodies.
        if ((bool)vars.PepperedLoggingReady && settings["diagnostics"]) {
            string message = "PEPPERED_ASL " + text.Replace("\r", " ").Replace("\n", " ");
            print(message);
            try {
                const string logPath = "Components/PEPPERED-autosplitter.log";
                if (!File.Exists(logPath) || new FileInfo(logPath).Length < 2097152)
                    File.AppendAllText(logPath, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            } catch { /* Logging failure never changes timer or game state. */ }
        }
    });

    vars.PepperedBootMessage = "boot 0.4.0-rc14 optional-friend-loss; diagnostics opt-in; verified_reader_sha256=" + (string)vars.HashBytes(logicBytes) + "; helper=c0ece0762d65cb831082a465af2c2cd64cc745d5ede2b7eb339fb84030cb1fb5";
    Assembly.Load(helperBytes).CreateInstance("Unity");
    vars.Helper.GameName = "PEPPERED (experimental, original Windows build)";
    vars.Helper.LoadSceneManager = true;
    refreshRate = 60;

    settings.Add("enableTimers", false, "EXPERIMENTAL: enable timer actions (unverified on Windows)");
    settings.Add("diagnostics", false, "Log scene/state transitions for Windows validation");
    settings.Add("autoStart", true, "Auto-start fresh World 0 only after the intro is ready");
    settings.Add("autoReset", true, "Auto-reset on return to Main Menu (including Ended)");
    settings.Add("splitEndings", true, "Endings (select your category; no star validation)", "enableTimers");
    settings.Add("ending.1", false, "Ending 1: Guillotine — first execution black frame", "splitEndings");
    settings.Add("ending.2", false, "Ending 2: Family Reunion — first abrupt black frame", "splitEndings");
    settings.Add("ending.3", false, "Ending 3: Unsung Hero — first bar dialogue", "splitEndings");
    settings.Add("ending.4", false, "Ending 4: Final death — first permanent-death black frame", "splitEndings");
    settings.Add("ending.5.with", false, "Ending 5 (With Karmina): start watching TV with Karmina", "splitEndings");
    settings.Add("ending.5.against", false, "Ending 5 (Against Karmina): final CONTINUE / GIVE UP choice", "splitEndings");
    settings.Add("ending.6.with", false, "Ending 6 (With Karmina): start watching TV with Karmina", "splitEndings");
    settings.Add("ending.6.against", false, "Ending 6 (Against Karmina): final CONTINUE / GIVE UP choice", "splitEndings");
    settings.Add("ending.7.with", false, "Ending 7 (With Karmina): start watching TV with Karmina", "splitEndings");
    settings.Add("ending.7.against", false, "Ending 7 (Against Karmina): final CONTINUE / GIVE UP choice", "splitEndings");
    settings.Add("ending.8", false, "Ending 8: Merdeka surrender — final GIVE HER THE STARS confirmation", "splitEndings");
    settings.Add("ending.8.friend_loss", false, "Ending 8 (optional friendship route): Merdeka defeat - NOT Ending 7", "splitEndings");
    settings.Add("ending.9", false, "Ending 9: Theodore victory — first T_Boss HP-zero victory", "splitEndings");
    settings.Add("ending.10", false, "Ending 10: Theodore death — golf death cut", "splitEndings");
    settings.Add("ending.11", false, "Ending 11: Secret GEM — first final-dialogue dash", "splitEndings");

    settings.Add("splitWorlds", true, "World starts", "enableTimers");
    settings.Add("world0", true, "World 0: Office_1", "splitWorlds");
    settings.Add("world1", true, "World 1: A_1", "splitWorlds");
    settings.Add("world2", true, "World 2: A_7", "splitWorlds");
    settings.Add("world3", true, "World 3: B_0", "splitWorlds");
    settings.Add("world4", true, "World 4: G_0", "splitWorlds");
    settings.Add("world5", true, "World 5: G_End", "splitWorlds");
    settings.Add("splitScenes", true, "Ordinary scene transitions", "enableTimers");
    settings.Add("sceneWorld0", true, "World 0 (starts at Office_1)", "splitScenes");
    settings.Add("sceneWorld0Main", true, "Main scenes", "sceneWorld0");
    settings.Add("sceneWorld0Branches", false, "Optional branches", "sceneWorld0");
    settings.Add("sceneWorld1", true, "World 1 (starts at A_1)", "splitScenes");
    settings.Add("sceneWorld1Main", true, "Main scenes", "sceneWorld1");
    settings.Add("sceneWorld1Detailed", false, "Detailed scenes", "sceneWorld1");
    settings.Add("sceneWorld1Branches", false, "Optional branches", "sceneWorld1");
    settings.Add("sceneWorld2", true, "World 2 (starts at A_7)", "splitScenes");
    settings.Add("sceneWorld2Main", true, "Main scenes", "sceneWorld2");
    settings.Add("sceneWorld2Detailed", false, "Detailed scenes", "sceneWorld2");
    settings.Add("sceneWorld2Branches", false, "Optional branches", "sceneWorld2");
    settings.Add("sceneWorld3", true, "World 3 (starts at B_0)", "splitScenes");
    settings.Add("sceneWorld3Main", true, "Main scenes", "sceneWorld3");
    settings.Add("sceneWorld3Detailed", false, "Detailed scenes", "sceneWorld3");
    settings.Add("sceneWorld3Branches", false, "Optional branches", "sceneWorld3");
    settings.Add("sceneWorld4", true, "World 4 (starts at G_0)", "splitScenes");
    settings.Add("sceneWorld4Main", true, "Main scenes", "sceneWorld4");
    settings.Add("sceneWorld4Detailed", false, "Detailed scenes", "sceneWorld4");
    settings.Add("sceneWorld5", true, "World 5 (starts at G_End)", "splitScenes");
    settings.Add("sceneWorld5Main", true, "Main scenes", "sceneWorld5");
    settings.Add("sceneWorld5Branches", false, "Optional branches", "sceneWorld5");
    settings.Add("scene.w0.coarse.office2", true, "Office_2 (Pitch the Investors)", "sceneWorld0Main");
    settings.Add("scene.w0.coarse.office3", true, "Office_3 (Meet the Accountant)", "sceneWorld0Main");
    settings.Add("scene.w0.coarse.office4", true, "Office_4 (Family Reunion)", "sceneWorld0Main");
    settings.Add("scene.w0.branch.office_balcony_1", false, "Office_Balcony 1 (Life Star on Balcony)", "sceneWorld0Branches");
    settings.Add("scene.w1.coarse.a2", true, "A_2 (Merdeka Follows You)", "sceneWorld1Main");
    settings.Add("scene.w1.coarse.a3", true, "A_3 (Shock Platforms)", "sceneWorld1Main");
    settings.Add("scene.w1.coarse.a4", true, "A_4 (Cynthia Evidence)", "sceneWorld1Main");
    settings.Add("scene.w1.coarse.a5", true, "A_5 (Block Puzzle)", "sceneWorld1Main");
    settings.Add("scene.w1.coarse.a6_bf", true, "A_6_BF (Merdeka Bullet Fight)", "sceneWorld1Main");
    settings.Add("scene.w1.detailed.a3_1", false, "A_3.1 (Red Coin Machine)", "sceneWorld1Detailed");
    settings.Add("scene.w1.detailed.a4_5", false, "A_4.5 (Falling Desks)", "sceneWorld1Detailed");
    settings.Add("scene.w1.detailed.a4_8", false, "A_4.8 (Fish Window Shocks)", "sceneWorld1Detailed");
    settings.Add("scene.w1.branch.secret_level_1", false, "Secret_Level_1 (Bottom of the Abyss)", "sceneWorld1Branches");
    settings.Add("scene.w1.branch.secret_level_3", false, "Secret_Level_3 (Unlockable Abyss Platforms)", "sceneWorld1Branches");
    settings.Add("scene.w2.coarse.a8", true, "A_8 (Apartment District)", "sceneWorld2Main");
    settings.Add("scene.w2.coarse.a9", true, "A_9 (Getaway Coffee)", "sceneWorld2Main");
    settings.Add("scene.w2.coarse.a10", true, "A_10 (Bug Kids Playground)", "sceneWorld2Main");
    settings.Add("scene.w2.coarse.a12_prison", true, "A_12_Prison (Police Station Cell)", "sceneWorld2Main");
    settings.Add("scene.w2.coarse.a13_the_court", true, "A_13_The_Court (Televised Courtroom)", "sceneWorld2Main");
    settings.Add("scene.w2.coarse.a14", true, "A_14 (Backroom Paper Puzzle)", "sceneWorld2Main");
    settings.Add("scene.w2.coarse.a_end", true, "A_End (Merdeka Theater)", "sceneWorld2Main");
    settings.Add("scene.w2.detailed.a7_1", false, "A_7.1 (Dumpster Side Street)", "sceneWorld2Detailed");
    settings.Add("scene.w2.branch.a8_library", false, "A_8_Library (Library)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.a9_1", false, "A_9.1 (Red Coin Machine)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.a10_1", false, "A_10.1 (Electronics Store)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.a10_2", false, "A_10.2 (Merdeka's Childhood)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.a11", false, "A_11 (Supermarket)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.a11_2", false, "A_11.2 (Abyss Train)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.a15", false, "A_15 (Theater Backstage)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.s0", false, "S_0 (Theater Entrance)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.s1", false, "S_1 (Drone Encounter)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.s2", false, "S_2 (Caviar Vending Machine)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.s3", false, "S_3 (Elevator Ride)", "sceneWorld2Branches");
    settings.Add("scene.w2.branch.s4", false, "S_4 (Playground and Bar)", "sceneWorld2Branches");
    settings.Add("scene.w3.coarse.b1", true, "B_1 (Starstruck Diner)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b1_1", true, "B_1.1 (Caviar Vending Area)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b2", true, "B_2 (Life Stars Shop Basement)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b4_1", true, "B_4.1 (Muffin Dock)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b4", true, "B_4 (Cultist Showdown)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b5", true, "B_5 (Gambler's Cups)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b6_0", true, "B_6.0 (Tunnel Entrance)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b6_1", true, "B_6.1 (Karmina's Goons)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b6_2", true, "B_6.2 (Tunnel Elevator)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b6_4", true, "B_6.4 (Elevator Passage)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b6_6", true, "B_6.6 (Karmina Deal)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b_city", true, "B_City (Cultist City)", "sceneWorld3Main");
    settings.Add("scene.w3.coarse.b10_1", true, "B_10.1 (Rainy Dock)", "sceneWorld3Main");
    settings.Add("scene.w3.detailed.b1_0", false, "B_1.0 (Red Coin Machine)", "sceneWorld3Detailed");
    settings.Add("scene.w3.detailed.b5_1", false, "B_5.1 (Cultist Graffiti)", "sceneWorld3Detailed");
    settings.Add("scene.w3.detailed.b5_2", false, "B_5.2 (Coins Challenge)", "sceneWorld3Detailed");
    settings.Add("scene.w3.detailed.b5_3", false, "B_5.3 (Box Retrieval)", "sceneWorld3Detailed");
    settings.Add("scene.w3.detailed.b5_4", false, "B_5.4 (Locker Room Segment A)", "sceneWorld3Detailed");
    settings.Add("scene.w3.detailed.b5_5", false, "B_5.5 (Locker Room Segment B)", "sceneWorld3Detailed");
    settings.Add("scene.w3.detailed.b6_0_1", false, "B_6.0.1 (Tunnel Arch)", "sceneWorld3Detailed");
    settings.Add("scene.w3.detailed.b6_3", false, "B_6.3 (Tunnel Checkpoint)", "sceneWorld3Detailed");
    settings.Add("scene.w3.detailed.b6_5", false, "B_6.5 (Tunnel Plaque)", "sceneWorld3Detailed");
    settings.Add("scene.w3.branch.b3", false, "B_3 (Reika Street)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.b3_1", false, "B_3.1 (Abyss Train)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.b_merdeka", false, "B_Merdeka (Merdeka Boss)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.b_aftermath", false, "B_Aftermath (Karmina's Shop)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.b_store", false, "B_Store (Cultist Coffee Shop)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.subspace4", false, "Subspace_4 (Chair Course)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.subspace5", false, "Subspace_5 (Chair Course Complete)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.subspace_final", false, "Subspace_Final (God of Life Ending)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.b10", false, "B_10 (News Boat Dock)", "sceneWorld3Branches");
    settings.Add("scene.w3.branch.b_end", false, "B_End (Diner Basement Finale)", "sceneWorld3Branches");
    settings.Add("scene.w4.coarse.g1", true, "G_1 (Exploding Block Course)", "sceneWorld4Main");
    settings.Add("scene.w4.coarse.g2", true, "G_2 (Chandelier Trap Course)", "sceneWorld4Main");
    settings.Add("scene.w4.coarse.g3", true, "G_3 (Beach Sand Course)", "sceneWorld4Main");
    settings.Add("scene.w4.coarse.g4", true, "G_4 (Trap-Heavy Course)", "sceneWorld4Main");
    settings.Add("scene.w4.coarse.g6", true, "G_6 (Chandelier Cluster Course)", "sceneWorld4Main");
    settings.Add("scene.w4.coarse.g7", true, "G_7 (Mailbox Course)", "sceneWorld4Main");
    settings.Add("scene.w4.coarse.g8", true, "G_8 (Theo Moving-TNT Course)", "sceneWorld4Main");
    settings.Add("scene.w4.coarse.g9", true, "G_9 (Castle Chandelier Course)", "sceneWorld4Main");
    settings.Add("scene.w4.detailed.g5_1", false, "G_5.1 (Theo Chandelier Traps)", "sceneWorld4Detailed");
    settings.Add("scene.w4.detailed.g5_2", false, "G_5.2 (Rocky TNT Course)", "sceneWorld4Detailed");
    settings.Add("scene.w4.detailed.g5_3", false, "G_5.3 (Tree-Lined TNT Course)", "sceneWorld4Detailed");
    settings.Add("scene.w4.detailed.g5_4", false, "G_5.4 (Lantern Trap Course)", "sceneWorld4Detailed");
    settings.Add("scene.w5.coarse.t1", true, "T_1 (Mansion Opening)", "sceneWorld5Main");
    settings.Add("scene.w5.coarse.t2", true, "T_2 (Thorn Hedge Maze)", "sceneWorld5Main");
    settings.Add("scene.w5.coarse.t3", true, "T_3 (Chandelier Trap)", "sceneWorld5Main");
    settings.Add("scene.w5.coarse.t4", true, "T_4 (Theo Diary Flashback)", "sceneWorld5Main");
    settings.Add("scene.w5.coarse.t5", true, "T_5 (Launch Barrel Traps)", "sceneWorld5Main");
    settings.Add("scene.w5.coarse.t6", true, "T_6 (Buzzsaw Trap Course)", "sceneWorld5Main");
    settings.Add("scene.w5.coarse.t7", true, "T_7 (Mansion Flashback)", "sceneWorld5Main");
    settings.Add("scene.w5.coarse.t8", true, "T_8 (Mansion Door Approach)", "sceneWorld5Main");
    settings.Add("scene.w5.coarse.t9", true, "T_9 (Library Prison)", "sceneWorld5Main");
    settings.Add("scene.w5.branch.t_boss", false, "T_Boss (Theodore Boss Battle)", "sceneWorld5Branches");
    // The diagnostic banner is emitted only after an explicit opt-in in update.
}

init
{
    // A manually running timer (including pre-attach Start) is rebound only
    // after a valid sample, never from a stale process snapshot.
    vars.RebindOnAttach = timer.CurrentPhase == TimerPhase.Running
        || timer.CurrentPhase == TimerPhase.Paused;
    vars.SupportedBuild = false;
    vars.ReaderConfigured = false;
    vars.Core.Attach();
    vars.Decision = vars.Code.CreateInstance("Peppered.Decision");
    vars.AutoStarting = false;
    vars.AutoSplitting = false;
    vars.LastTrace = "";
    vars.LastFault = "";
    var playerModule = modules.FirstOrDefault(m =>
        m.ModuleName.Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase));
    if (playerModule == null)
        throw new InvalidOperationException("PEPPERED: UnityPlayer.dll is not loaded.");
    string gameRoot = Path.GetDirectoryName(playerModule.FileName);
    var expected = new Dictionary<string, string> {
        { "PEPPERED.exe", "d45d285f14ef68204bb2469bb563810bc0c1b62e51b397bb3f67f6ca3c973237" },
        { "UnityPlayer.dll", "09bc8eda55993e20f8a287e51381ac1e68e55df9736373334860279fcf95b91a" },
        { "PEPPERED_Data/Managed/Assembly-CSharp.dll", "9850435503f489ee42fb610f362017521b1f5af9f0fa2aa1318e1d9aec470253" },
        { "PEPPERED_Data/Managed/mscorlib.dll", "8548091dcf0d2b0015cc458e87f2b0ce70aff151ca85a86746b3494fcfce233f" },
        { "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll", "6a0d22bc964e88684a4291e0ecf7b24094dc616b274c2dec052ea77dc72f79cd" },
        { "PEPPERED_Data/globalgamemanagers", "eef07764258d9f43f96a2bb3d7f7023e1f4882e4ff045687749fc12854fba2c1" },
        { "PEPPERED_Data/globalgamemanagers.assets", "da9a061e193ffa5bbbd0275029b4b71f0366595dd1e52402f208c5261a0eec25" },
        { "PEPPERED_Data/level1", "13c198227474fc1c8d99cf1d1fb62cb5834934a8e861474c659e17e454a6517b" },
        { "PEPPERED_Data/level2", "1c2d8e4686c063c7a377b3b98821e97db6486f98bea459191a9812c937c8cc3c" },
        { "PEPPERED_Data/level9", "233225ab485d162afbb3c70db40963f6ec77be3a28fac37e9c3b4fbfddb0d9fc" },
        { "PEPPERED_Data/level18", "c8ec82a8f01037584085b7844ae1d24208cca79daddc19d6e72aa989b6adb893" },
        { "PEPPERED_Data/level37", "56734c17cd828501c2f159cef71fe84011dd10da23ca96cc79fedc13f8f1ba63" },
        { "PEPPERED_Data/level59", "4f863fdaee7248d02d27d9691c5a0ed5e0804ffe7a6c1b52c8feb271df762f33" },
        { "PEPPERED_Data/level63", "bd19dfb786963e54db15039b250ddf227d965546d5c288dd83426397532d4446" },
        { "PEPPERED_Data/level64", "5e53705664ce5c64a54549b6d53f651f7760cc7b0ab1cdd56bebab920e9a0cc3" },
        { "PEPPERED_Data/level80", "787d43758bc30e9250a52f3bfbf101a6cf9d18e70ba67781f54fac72a09a6f51" },
        { "PEPPERED_Data/level93", "b259f94ffae56b23d8da1049f30eef2a9f6c2724fe9d42f66973107ee5b13597" },
        { "PEPPERED_Data/level103", "1693ddc4bdf56b864fb56b2fcdf0603b3104b693d7a9ca3b6428dda2a9bbe89a" },
        { "PEPPERED_Data/level104", "d2e1b80f95b3f9d612e608815ff5123c2a8f4c0617df9509b5e32624dc7ee7af" },
        { "PEPPERED_Data/Managed/Unity.TextMeshPro.dll", "8cd39c3c5477f826b0f9efd9fd536b024fbd3e553e520a0aef85f7a08f108b7a" },
        { "PEPPERED_Data/level4", "76495bed5c3ebfdb6f8d67702196dff2d169fdb8f758487d7674e8d0d8acda52" },
        { "PEPPERED_Data/level5", "3c1d3f2b6347306bdd31748acdb0d1d33b12d3eb4bd67193dc893b3aaf3df940" },
        { "PEPPERED_Data/level72", "44434a176d4b9b50cadc6e8c9e5a04435f9d57bfb4e7fa557c475cd381818b32" },
        { "PEPPERED_Data/level79", "53d450ee1210e7fad9d238bef22bbfffa4a2260f21627c9922d9644694274f1b" },
        { "PEPPERED_Data/Managed/UnityEngine.UI.dll", "83e5e19e23bdc95bc92d21e3d8f03cce880dab60d253695b38fcd88e8d82610c" },
        { "PEPPERED_Data/Managed/UnityEngine.CoreModule.dll", "dd1053817d73f810f53ff73ddbeae7509ae4cef4106b03248366e8340c4b3b64" },
        { "PEPPERED_Data/level66", "0820915119ae2d42b45db69dee77a07c1e2b1c57745c2d022b13a6719885d902" }
    };
    foreach (var item in expected) {
        string path = Path.Combine(gameRoot, item.Key.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) || (string)vars.HashFile(path) != item.Value)
            throw new InvalidDataException("PEPPERED: unsupported original Windows build; mismatched " + item.Key);
    }
    vars.Reader = vars.Code.CreateInstance("Peppered.ReadOnlyReader");
    vars.MonoMetadata = null;
    vars.LastMetadataScene = "";
    vars.LastTheodoreMetadataScene = "";
    vars.LastSurrenderMetadataScene = "";
    vars.LastKarminaMetadataScene = "";
    vars.LastTvStartMetadataScene = "";
    vars.TheodoreSceneAddress = IntPtr.Zero;
    vars.MetadataRetryAfter = 0L;
    vars.OptionalMetadataRetryAfter = 0L;
    vars.Helper.TryLoad = (Func<dynamic, bool>)(mono => {
        if (vars.Helper.Scenes == null)
            throw new InvalidOperationException("PEPPERED: asl-help SceneManager unavailable; reload the ASL after helper scene setup.");
        vars.MonoMetadata = mono;
        long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        if (nowTicks < (long)vars.MetadataRetryAfter)
            return false;
        vars.MetadataRetryAfter = nowTicks + 2L * System.Diagnostics.Stopwatch.Frequency;
        bool wasConfigured = (bool)vars.ReaderConfigured;
        // Configure a candidate so a transient callback failure cannot discard a
        // reader that already passed the core/checkpoint readiness gate.
        dynamic candidateReader = vars.Code.CreateInstance("Peppered.ReadOnlyReader");
        candidateReader.Configure(vars.Helper, mono);
        // Configure may fail closed without throwing while statics initialize.
        // Returning true without a fresh checkpoint read would permanently stop retries.
        dynamic firstRead = candidateReader.Read("");
        if (!(bool)firstRead.Valid) {
            string waitReason = "metadata wait: " + (firstRead.Diagnostic ?? "unavailable");
            if (waitReason != (string)vars.LastFault) {
                vars.PepperedLog(waitReason);
                vars.LastFault = waitReason;
            }
            vars.ReaderConfigured = wasConfigured;
            return false;
        }
        vars.Reader = candidateReader;
        vars.LastMetadataScene = "";
        vars.LastTheodoreMetadataScene = "";
        vars.LastSurrenderMetadataScene = "";
        vars.LastKarminaMetadataScene = "";
        vars.LastTvStartMetadataScene = "";
        vars.TheodoreSceneAddress = IntPtr.Zero;
        vars.MetadataRetryAfter = 0L;
        vars.OptionalMetadataRetryAfter = 0L;
        vars.ReaderConfigured = true;
        return true;
    });
    vars.SupportedBuild = true;
    vars.PepperedLog("supported original Windows files; waiting for Mono metadata and active scene");
}

update
{
    vars.PepperedLoggingReady = true;
    if (settings["diagnostics"] && !(bool)vars.PepperedLogBannerSent) {
        vars.PepperedLog((string)vars.PepperedBootMessage);
        vars.PepperedLog("loaded 0.4.0-rc14 optional-friend-loss; diagnostics opt-in; timing is RTA, not IGT/LRT");
        vars.PepperedLogBannerSent = true;
    }
    vars.AutoStarting = false;
    vars.AutoSplitting = false;
    vars.Decision = vars.Code.CreateInstance("Peppered.Decision");
    long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    if ((bool)vars.SupportedBuild
        && !(bool)vars.ReaderConfigured && vars.MonoMetadata != null
        && nowTicks >= (long)vars.MetadataRetryAfter) {
        vars.MetadataRetryAfter = nowTicks + 2L * System.Diagnostics.Stopwatch.Frequency;
        try {
            dynamic retryReader = vars.Code.CreateInstance("Peppered.ReadOnlyReader");
            retryReader.Configure(vars.Helper, vars.MonoMetadata);
            dynamic retryRead = retryReader.Read("");
            if ((bool)retryRead.Valid) {
                vars.Reader = retryReader;
                vars.ReaderConfigured = true;
                vars.LastMetadataScene = "";
                vars.LastTheodoreMetadataScene = "";
                vars.LastSurrenderMetadataScene = "";
                vars.LastKarminaMetadataScene = "";
                vars.LastTvStartMetadataScene = "";
                vars.TheodoreSceneAddress = IntPtr.Zero;
                vars.OptionalMetadataRetryAfter = 0L;
                vars.MetadataRetryAfter = 0L;
                vars.LastFault = "";
            } else {
                string waitReason = "metadata wait: " + (retryRead.Diagnostic ?? "unavailable");
                if (waitReason != (string)vars.LastFault) {
                    vars.PepperedLog(waitReason);
                    vars.LastFault = waitReason;
                }
            }
        } catch (Exception error) {
            string fault = error.GetType().Name;
            if (settings["diagnostics"] && fault != (string)vars.LastFault) {
                vars.PepperedLog("metadata retry failed: " + fault + "; no timer action");
                vars.LastFault = fault;
            }
        }
    }
    if (!(bool)vars.SupportedBuild || !(bool)vars.ReaderConfigured || !(bool)vars.Helper.Loaded) {
        vars.Core.Invalidate();
        return false;
    }
    try {
        dynamic active = vars.Helper.Scenes.Active;
        if (!(bool)active.IsValid) {
            vars.Reader.ResetTheodoreBinding();
            vars.Reader.ResetSurrenderBinding();
            vars.Reader.ResetMerdekaBinding();
            vars.Reader.ResetKarminaBinding();
            vars.TheodoreSceneAddress = IntPtr.Zero;
            vars.Core.Invalidate();
            return false;
        }
        IntPtr addressBefore = (IntPtr)active.Address;
        string scene = (string)active.Name;
        if (String.IsNullOrEmpty(scene) || scene.Length > 200) {
            vars.Reader.ResetTheodoreBinding();
            vars.Reader.ResetSurrenderBinding();
            vars.Reader.ResetMerdekaBinding();
            vars.Reader.ResetKarminaBinding();
            vars.TheodoreSceneAddress = IntPtr.Zero;
            vars.Core.Invalidate();
            return false;
        }
        if (addressBefore != (IntPtr)vars.TheodoreSceneAddress) {
            vars.Reader.ResetTheodoreBinding();
            vars.Reader.ResetSurrenderBinding();
            vars.Reader.ResetMerdekaBinding();
            vars.Reader.ResetKarminaBinding();
            vars.TheodoreSceneAddress = addressBefore;
        }
        bool boundaryTarget = scene == "Office_1" || scene == "Office_3"
            || scene == "Office_4" || scene == "S_4"
            || scene == "C_End" || scene == "T_End"
            || scene == "T_Boss" || scene == "Subspace_Final"
            || (scene == "B_Merdeka" && settings["ending.8"])
            || (scene == "B_End" && (settings["ending.5.against"]
                || settings["ending.6.against"] || settings["ending.7.against"]))
            || (scene == "B_Aftermath" && (settings["ending.5.with"] || settings["ending.6.with"] || settings["ending.7.with"]));
        if (!boundaryTarget) vars.LastMetadataScene = "";
        bool theodoreMetadataRequired = scene == "T_Boss" && settings["ending.9"];
        if (!theodoreMetadataRequired) vars.LastTheodoreMetadataScene = "";
        bool surrenderMetadataRequired = scene == "B_Merdeka" && settings["ending.8"];
        if (!surrenderMetadataRequired) vars.LastSurrenderMetadataScene = "";
        bool karminaMetadataRequired = scene == "B_End" && (settings["ending.5.against"]
            || settings["ending.6.against"] || settings["ending.7.against"]);
        if (!karminaMetadataRequired) vars.LastKarminaMetadataScene = "";
        bool tvStartMetadataRequired = scene == "B_Aftermath" && (settings["ending.5.with"] || settings["ending.6.with"] || settings["ending.7.with"]);
        if (!tvStartMetadataRequired) vars.LastTvStartMetadataScene = "";
        if (boundaryTarget && (scene != (string)vars.LastMetadataScene
            || (theodoreMetadataRequired && scene != (string)vars.LastTheodoreMetadataScene)
            || (surrenderMetadataRequired && scene != (string)vars.LastSurrenderMetadataScene)
            || (karminaMetadataRequired && scene != (string)vars.LastKarminaMetadataScene)
            || (tvStartMetadataRequired && scene != (string)vars.LastTvStartMetadataScene))
            && vars.MonoMetadata != null
            && nowTicks >= (long)vars.OptionalMetadataRetryAfter) {
            vars.OptionalMetadataRetryAfter = nowTicks + 2L * System.Diagnostics.Stopwatch.Frequency;
            // Failed target metadata never replaces an otherwise usable reader.
            // Match the helper OnTryLoadFailure: refresh cached image wrappers.
            vars.MonoMetadata.Images.Clear();
            dynamic candidateReader = vars.Code.CreateInstance("Peppered.ReadOnlyReader");
            candidateReader.Configure(vars.Helper, vars.MonoMetadata);
            dynamic candidateRead = candidateReader.Read(scene);
            if ((bool)candidateRead.Valid && (bool)candidateRead.BoundaryMetadataReady
                && (!theodoreMetadataRequired || (bool)candidateRead.TheodoreMetadataReady)
                && (!surrenderMetadataRequired || (bool)candidateRead.SurrenderMetadataReady)
                && (!karminaMetadataRequired || (bool)candidateRead.KarminaMetadataReady)
                && (!tvStartMetadataRequired || (bool)candidateRead.TvStartMetadataReady)) {
                vars.Reader = candidateReader;
                vars.LastMetadataScene = scene;
                if (theodoreMetadataRequired) vars.LastTheodoreMetadataScene = scene;
                if (surrenderMetadataRequired) vars.LastSurrenderMetadataScene = scene;
                if (karminaMetadataRequired) vars.LastKarminaMetadataScene = scene;
                if (tvStartMetadataRequired) vars.LastTvStartMetadataScene = scene;
            }
        }
        dynamic reading = vars.Reader.Read(scene);
        bool sceneIdentityChanged = !(bool)active.IsValid
            || addressBefore != (IntPtr)active.Address || scene != (string)active.Name;
        if (!(bool)reading.Valid || sceneIdentityChanged) {
            if (sceneIdentityChanged) {
                vars.Reader.ResetTheodoreBinding();
                vars.Reader.ResetSurrenderBinding();
                vars.Reader.ResetMerdekaBinding();
                vars.Reader.ResetKarminaBinding();
                vars.TheodoreSceneAddress = IntPtr.Zero;
            }
            vars.Core.Invalidate();
            if (settings["diagnostics"] && (string)vars.LastFault != "invalid sample") {
                vars.PepperedLog("invalid sample; pending action discarded, run/dedupe latches preserved");
                vars.LastFault = "invalid sample";
            }
            return false;
        }
        vars.LastFault = "";
        dynamic sample = vars.Code.CreateInstance("Peppered.Snapshot");
        sample.Valid = true;
        sample.Scene = scene;
        sample.Abyss = (int)reading.Abyss;
        sample.Bigman = (bool?)reading.Bigman;
        sample.Prison = (bool?)reading.Prison;
        sample.HasCheckpoint = (bool?)reading.HasCheckpoint;
        sample.DialoguePlaying = (bool?)reading.DialoguePlaying;
        sample.StartReady = (bool?)reading.StartReady;
        sample.GolfBattleActive = (bool?)reading.GolfBattleActive;
        sample.GolfBatteries = (int?)reading.GolfBatteries;
        sample.TheodoreHp = (int?)reading.TheodoreHp;
        sample.TheodoreMetadataReady = (bool)reading.TheodoreMetadataReady;
        sample.TvStartActive = (bool?)reading.TvStartActive;
        sample.TvStartMetadataReady = (bool)reading.TvStartMetadataReady;
        sample.KarminaChoice = (int?)reading.KarminaChoice;
        sample.KarminaMetadataReady = (bool)reading.KarminaMetadataReady;
        sample.MerdekaStage = (int?)reading.MerdekaStage;
        sample.SurrenderChoice = (int?)reading.SurrenderChoice;
        sample.SurrenderMetadataReady = (bool)reading.SurrenderMetadataReady;
        sample.EndingSignals = reading.EndingSignals == null
            ? new HashSet<string>() : new HashSet<string>(reading.EndingSignals);
        sample.BoundaryMetadataReady = (bool)reading.BoundaryMetadataReady;
        dynamic options = vars.Code.CreateInstance("Peppered.Options");
        options.Enabled = settings["enableTimers"];
        options.AutoStart = settings["autoStart"];
        options.AutoReset = settings["autoReset"];
        options.SplitWorlds = settings["splitWorlds"];
        options.SplitScenes = settings["splitScenes"];
        options.SplitEndings = settings["splitEndings"];
        options.BasicStart = settings.StartEnabled;
        options.BasicSplit = settings.SplitEnabled;
        options.BasicReset = settings.ResetEnabled;
        options.Worlds = new HashSet<int>();
        for (int i = 0; i < 6; i++)
            if (settings["world" + i]) options.Worlds.Add(i);
        options.SceneIds = new HashSet<string>();
        if (settings["sceneWorld0"] && settings["sceneWorld0Main"] && settings["scene.w0.coarse.office2"]) options.SceneIds.Add("scene.w0.coarse.office2");
        if (settings["sceneWorld0"] && settings["sceneWorld0Main"] && settings["scene.w0.coarse.office3"]) options.SceneIds.Add("scene.w0.coarse.office3");
        if (settings["sceneWorld0"] && settings["sceneWorld0Main"] && settings["scene.w0.coarse.office4"]) options.SceneIds.Add("scene.w0.coarse.office4");
        if (settings["sceneWorld0"] && settings["sceneWorld0Branches"] && settings["scene.w0.branch.office_balcony_1"]) options.SceneIds.Add("scene.w0.branch.office_balcony_1");
        if (settings["sceneWorld1"] && settings["sceneWorld1Main"] && settings["scene.w1.coarse.a2"]) options.SceneIds.Add("scene.w1.coarse.a2");
        if (settings["sceneWorld1"] && settings["sceneWorld1Main"] && settings["scene.w1.coarse.a3"]) options.SceneIds.Add("scene.w1.coarse.a3");
        if (settings["sceneWorld1"] && settings["sceneWorld1Main"] && settings["scene.w1.coarse.a4"]) options.SceneIds.Add("scene.w1.coarse.a4");
        if (settings["sceneWorld1"] && settings["sceneWorld1Main"] && settings["scene.w1.coarse.a5"]) options.SceneIds.Add("scene.w1.coarse.a5");
        if (settings["sceneWorld1"] && settings["sceneWorld1Main"] && settings["scene.w1.coarse.a6_bf"]) options.SceneIds.Add("scene.w1.coarse.a6_bf");
        if (settings["sceneWorld1"] && settings["sceneWorld1Detailed"] && settings["scene.w1.detailed.a3_1"]) options.SceneIds.Add("scene.w1.detailed.a3_1");
        if (settings["sceneWorld1"] && settings["sceneWorld1Detailed"] && settings["scene.w1.detailed.a4_5"]) options.SceneIds.Add("scene.w1.detailed.a4_5");
        if (settings["sceneWorld1"] && settings["sceneWorld1Detailed"] && settings["scene.w1.detailed.a4_8"]) options.SceneIds.Add("scene.w1.detailed.a4_8");
        if (settings["sceneWorld1"] && settings["sceneWorld1Branches"] && settings["scene.w1.branch.secret_level_1"]) options.SceneIds.Add("scene.w1.branch.secret_level_1");
        if (settings["sceneWorld1"] && settings["sceneWorld1Branches"] && settings["scene.w1.branch.secret_level_3"]) options.SceneIds.Add("scene.w1.branch.secret_level_3");
        if (settings["sceneWorld2"] && settings["sceneWorld2Main"] && settings["scene.w2.coarse.a8"]) options.SceneIds.Add("scene.w2.coarse.a8");
        if (settings["sceneWorld2"] && settings["sceneWorld2Main"] && settings["scene.w2.coarse.a9"]) options.SceneIds.Add("scene.w2.coarse.a9");
        if (settings["sceneWorld2"] && settings["sceneWorld2Main"] && settings["scene.w2.coarse.a10"]) options.SceneIds.Add("scene.w2.coarse.a10");
        if (settings["sceneWorld2"] && settings["sceneWorld2Main"] && settings["scene.w2.coarse.a12_prison"]) options.SceneIds.Add("scene.w2.coarse.a12_prison");
        if (settings["sceneWorld2"] && settings["sceneWorld2Main"] && settings["scene.w2.coarse.a13_the_court"]) options.SceneIds.Add("scene.w2.coarse.a13_the_court");
        if (settings["sceneWorld2"] && settings["sceneWorld2Main"] && settings["scene.w2.coarse.a14"]) options.SceneIds.Add("scene.w2.coarse.a14");
        if (settings["sceneWorld2"] && settings["sceneWorld2Main"] && settings["scene.w2.coarse.a_end"]) options.SceneIds.Add("scene.w2.coarse.a_end");
        if (settings["sceneWorld2"] && settings["sceneWorld2Detailed"] && settings["scene.w2.detailed.a7_1"]) options.SceneIds.Add("scene.w2.detailed.a7_1");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.a8_library"]) options.SceneIds.Add("scene.w2.branch.a8_library");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.a9_1"]) options.SceneIds.Add("scene.w2.branch.a9_1");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.a10_1"]) options.SceneIds.Add("scene.w2.branch.a10_1");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.a10_2"]) options.SceneIds.Add("scene.w2.branch.a10_2");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.a11"]) options.SceneIds.Add("scene.w2.branch.a11");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.a11_2"]) options.SceneIds.Add("scene.w2.branch.a11_2");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.a15"]) options.SceneIds.Add("scene.w2.branch.a15");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.s0"]) options.SceneIds.Add("scene.w2.branch.s0");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.s1"]) options.SceneIds.Add("scene.w2.branch.s1");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.s2"]) options.SceneIds.Add("scene.w2.branch.s2");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.s3"]) options.SceneIds.Add("scene.w2.branch.s3");
        if (settings["sceneWorld2"] && settings["sceneWorld2Branches"] && settings["scene.w2.branch.s4"]) options.SceneIds.Add("scene.w2.branch.s4");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b1"]) options.SceneIds.Add("scene.w3.coarse.b1");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b1_1"]) options.SceneIds.Add("scene.w3.coarse.b1_1");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b2"]) options.SceneIds.Add("scene.w3.coarse.b2");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b4_1"]) options.SceneIds.Add("scene.w3.coarse.b4_1");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b4"]) options.SceneIds.Add("scene.w3.coarse.b4");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b5"]) options.SceneIds.Add("scene.w3.coarse.b5");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b6_0"]) options.SceneIds.Add("scene.w3.coarse.b6_0");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b6_1"]) options.SceneIds.Add("scene.w3.coarse.b6_1");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b6_2"]) options.SceneIds.Add("scene.w3.coarse.b6_2");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b6_4"]) options.SceneIds.Add("scene.w3.coarse.b6_4");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b6_6"]) options.SceneIds.Add("scene.w3.coarse.b6_6");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b_city"]) options.SceneIds.Add("scene.w3.coarse.b_city");
        if (settings["sceneWorld3"] && settings["sceneWorld3Main"] && settings["scene.w3.coarse.b10_1"]) options.SceneIds.Add("scene.w3.coarse.b10_1");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b1_0"]) options.SceneIds.Add("scene.w3.detailed.b1_0");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b5_1"]) options.SceneIds.Add("scene.w3.detailed.b5_1");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b5_2"]) options.SceneIds.Add("scene.w3.detailed.b5_2");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b5_3"]) options.SceneIds.Add("scene.w3.detailed.b5_3");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b5_4"]) options.SceneIds.Add("scene.w3.detailed.b5_4");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b5_5"]) options.SceneIds.Add("scene.w3.detailed.b5_5");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b6_0_1"]) options.SceneIds.Add("scene.w3.detailed.b6_0_1");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b6_3"]) options.SceneIds.Add("scene.w3.detailed.b6_3");
        if (settings["sceneWorld3"] && settings["sceneWorld3Detailed"] && settings["scene.w3.detailed.b6_5"]) options.SceneIds.Add("scene.w3.detailed.b6_5");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.b3"]) options.SceneIds.Add("scene.w3.branch.b3");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.b3_1"]) options.SceneIds.Add("scene.w3.branch.b3_1");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.b_merdeka"]) options.SceneIds.Add("scene.w3.branch.b_merdeka");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.b_aftermath"]) options.SceneIds.Add("scene.w3.branch.b_aftermath");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.b_store"]) options.SceneIds.Add("scene.w3.branch.b_store");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.subspace4"]) options.SceneIds.Add("scene.w3.branch.subspace4");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.subspace5"]) options.SceneIds.Add("scene.w3.branch.subspace5");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.subspace_final"]) options.SceneIds.Add("scene.w3.branch.subspace_final");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.b10"]) options.SceneIds.Add("scene.w3.branch.b10");
        if (settings["sceneWorld3"] && settings["sceneWorld3Branches"] && settings["scene.w3.branch.b_end"]) options.SceneIds.Add("scene.w3.branch.b_end");
        if (settings["sceneWorld4"] && settings["sceneWorld4Main"] && settings["scene.w4.coarse.g1"]) options.SceneIds.Add("scene.w4.coarse.g1");
        if (settings["sceneWorld4"] && settings["sceneWorld4Main"] && settings["scene.w4.coarse.g2"]) options.SceneIds.Add("scene.w4.coarse.g2");
        if (settings["sceneWorld4"] && settings["sceneWorld4Main"] && settings["scene.w4.coarse.g3"]) options.SceneIds.Add("scene.w4.coarse.g3");
        if (settings["sceneWorld4"] && settings["sceneWorld4Main"] && settings["scene.w4.coarse.g4"]) options.SceneIds.Add("scene.w4.coarse.g4");
        if (settings["sceneWorld4"] && settings["sceneWorld4Main"] && settings["scene.w4.coarse.g6"]) options.SceneIds.Add("scene.w4.coarse.g6");
        if (settings["sceneWorld4"] && settings["sceneWorld4Main"] && settings["scene.w4.coarse.g7"]) options.SceneIds.Add("scene.w4.coarse.g7");
        if (settings["sceneWorld4"] && settings["sceneWorld4Main"] && settings["scene.w4.coarse.g8"]) options.SceneIds.Add("scene.w4.coarse.g8");
        if (settings["sceneWorld4"] && settings["sceneWorld4Main"] && settings["scene.w4.coarse.g9"]) options.SceneIds.Add("scene.w4.coarse.g9");
        if (settings["sceneWorld4"] && settings["sceneWorld4Detailed"] && settings["scene.w4.detailed.g5_1"]) options.SceneIds.Add("scene.w4.detailed.g5_1");
        if (settings["sceneWorld4"] && settings["sceneWorld4Detailed"] && settings["scene.w4.detailed.g5_2"]) options.SceneIds.Add("scene.w4.detailed.g5_2");
        if (settings["sceneWorld4"] && settings["sceneWorld4Detailed"] && settings["scene.w4.detailed.g5_3"]) options.SceneIds.Add("scene.w4.detailed.g5_3");
        if (settings["sceneWorld4"] && settings["sceneWorld4Detailed"] && settings["scene.w4.detailed.g5_4"]) options.SceneIds.Add("scene.w4.detailed.g5_4");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t1"]) options.SceneIds.Add("scene.w5.coarse.t1");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t2"]) options.SceneIds.Add("scene.w5.coarse.t2");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t3"]) options.SceneIds.Add("scene.w5.coarse.t3");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t4"]) options.SceneIds.Add("scene.w5.coarse.t4");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t5"]) options.SceneIds.Add("scene.w5.coarse.t5");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t6"]) options.SceneIds.Add("scene.w5.coarse.t6");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t7"]) options.SceneIds.Add("scene.w5.coarse.t7");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t8"]) options.SceneIds.Add("scene.w5.coarse.t8");
        if (settings["sceneWorld5"] && settings["sceneWorld5Main"] && settings["scene.w5.coarse.t9"]) options.SceneIds.Add("scene.w5.coarse.t9");
        if (settings["sceneWorld5"] && settings["sceneWorld5Branches"] && settings["scene.w5.branch.t_boss"]) options.SceneIds.Add("scene.w5.branch.t_boss");
        options.EndingIds = new HashSet<string>();
        if (settings["ending.1"]) options.EndingIds.Add("ending.execution_black");
        if (settings["ending.2"]) options.EndingIds.Add("ending.family_black");
        if (settings["ending.3"]) options.EndingIds.Add("ending.bar_dialogue");
        if (settings["ending.4"]) options.EndingIds.Add("ending.final_death_black");
        if (settings["ending.5.with"]) options.EndingIds.Add("ending.karmina_tv_start");
        if (settings["ending.5.against"]) options.EndingIds.Add("ending.karmina_final_choice");
        if (settings["ending.6.with"]) options.EndingIds.Add("ending.karmina_tv_start");
        if (settings["ending.6.against"]) options.EndingIds.Add("ending.karmina_final_choice");
        if (settings["ending.7.with"]) options.EndingIds.Add("ending.karmina_tv_start");
        if (settings["ending.7.against"]) options.EndingIds.Add("ending.karmina_final_choice");
        if (settings["ending.8"]) options.EndingIds.Add("ending.merdeka_surrender");
        if (settings["ending.8.friend_loss"]) options.EndingIds.Add("ending.merdeka_defeat");
        if (settings["ending.9"]) options.EndingIds.Add("ending.theodore_victory");
        if (settings["ending.10"]) options.EndingIds.Add("ending.theodore_death");
        if (settings["ending.11"]) options.EndingIds.Add("ending.gem_dialogue");
        if ((bool)vars.RebindOnAttach) {
            if (timer.CurrentPhase == TimerPhase.Running || timer.CurrentPhase == TimerPhase.Paused)
                vars.Core.OnStart(false);
            vars.RebindOnAttach = false;
        }
        vars.Decision = vars.Core.Step(sample, timer.CurrentPhase.ToString(), options);
        string trace = "scene=" + scene + " abyss=" + reading.Abyss
            + " checkpoint=" + (((bool?)reading.HasCheckpoint).HasValue ? reading.HasCheckpoint.ToString() : "unknown")
            + " startReady=" + (((bool?)reading.StartReady).HasValue ? reading.StartReady.ToString() : "unknown")
            + " boundaryReady=" + reading.BoundaryMetadataReady
            + " golfBattle=" + (((bool?)reading.GolfBattleActive).HasValue ? reading.GolfBattleActive.ToString() : "unknown")
            + " golfBatteries=" + (((int?)reading.GolfBatteries).HasValue ? reading.GolfBatteries.ToString() : "unknown")
            + " golfCutscene=" + (((bool?)reading.GolfCutscenePlaying).HasValue ? reading.GolfCutscenePlaying.ToString() : "unknown")
            + " tvStartReady=" + reading.TvStartMetadataReady
            + " tvStartActive=" + (((bool?)reading.TvStartActive).HasValue ? reading.TvStartActive.ToString() : "unknown")
            + " karminaReady=" + reading.KarminaMetadataReady
            + " karminaChoice=" + (((int?)reading.KarminaChoice).HasValue ? reading.KarminaChoice.ToString() : "unknown")
            + " merdekaStage=" + (((int?)reading.MerdekaStage).HasValue ? reading.MerdekaStage.ToString() : "unknown")
            + " surrenderReady=" + reading.SurrenderMetadataReady
            + " surrenderChoice=" + (((int?)reading.SurrenderChoice).HasValue ? reading.SurrenderChoice.ToString() : "unknown")
            + " theodoreReady=" + reading.TheodoreMetadataReady
            + " theodoreHp=" + (((int?)reading.TheodoreHp).HasValue ? reading.TheodoreHp.ToString() : "unknown")
            + " endingSignals=" + (reading.EndingSignals == null ? 0 : reading.EndingSignals.Count)
            + " endingIds=" + (reading.EndingSignals == null || reading.EndingSignals.Count == 0 ? "none" : String.Join(",", (IEnumerable<string>)reading.EndingSignals))
            + " phase=" + timer.CurrentPhase.ToString()
            + " enabled=" + settings["enableTimers"]
            + " start=" + vars.Decision.Start + " reset=" + vars.Decision.Reset
            + " split=" + (vars.Decision.SplitId ?? "none")
            + " candidate=" + (vars.Decision.Diagnostic ?? "none")
            + " reader=" + (reading.Diagnostic ?? "");
        if (settings["diagnostics"] && trace != (string)vars.LastTrace) {
            vars.PepperedLog(trace);
            vars.LastTrace = trace;
        }
        if ((bool)vars.Decision.Reset && timer.CurrentPhase == TimerPhase.Ended
            && settings["enableTimers"] && settings["autoReset"] && settings.ResetEnabled) {
            // Ordinary reset{} is not evaluated in Ended. onReset owns cleanup.
            vars.TimerModel.Reset();
        }
        return true;
    } catch (Exception error) {
        vars.Core.Invalidate();
        string fault = error.GetType().Name;
        if (settings["diagnostics"] && fault != (string)vars.LastFault) {
            vars.PepperedLog("read failed: " + fault + "; no timer action");
            vars.LastFault = fault;
        }
        return false;
    }
}

start
{
    if (!settings["enableTimers"] || !settings["autoStart"] || !settings.StartEnabled
        || !(bool)vars.Decision.Start) return false;
    vars.AutoStarting = true;
    return true;
}

split
{
    if (!settings["enableTimers"] || !settings.SplitEnabled
        || String.IsNullOrEmpty((string)vars.Decision.SplitId)) return false;
    vars.AutoSplitting = true;
    return true;
}

reset
{
    return settings["enableTimers"] && settings["autoReset"] && settings.ResetEnabled
        && (bool)vars.Decision.Reset;
}

isLoading
{
    // Neither helper initialization, fade, pause nor CutscenePlaying is a load signal.
    return false;
}

onStart
{
    if (vars.Reader != null) vars.Reader.ResetTheodoreBinding();
    if (vars.Reader != null) vars.Reader.ResetSurrenderBinding();
    if (vars.Reader != null) vars.Reader.ResetMerdekaBinding();
    if (vars.Reader != null) vars.Reader.ResetKarminaBinding();
    vars.RebindOnAttach = false;
    vars.Core.OnStart((bool)vars.AutoStarting);
    vars.AutoStarting = false;
    vars.Decision = vars.Code.CreateInstance("Peppered.Decision");
}

onSplit
{
    vars.Core.OnSplit((bool)vars.AutoSplitting);
    vars.AutoSplitting = false;
    vars.Decision = vars.Code.CreateInstance("Peppered.Decision");
}

onReset
{
    if (vars.Reader != null) vars.Reader.ResetTheodoreBinding();
    if (vars.Reader != null) vars.Reader.ResetSurrenderBinding();
    if (vars.Reader != null) vars.Reader.ResetMerdekaBinding();
    if (vars.Reader != null) vars.Reader.ResetKarminaBinding();
    vars.RebindOnAttach = false;
    vars.Core.OnReset();
    vars.AutoStarting = false;
    vars.AutoSplitting = false;
    vars.Decision = vars.Code.CreateInstance("Peppered.Decision");
}

exit
{
    if (vars.Reader != null) vars.Reader.ResetTheodoreBinding();
    if (vars.Reader != null) vars.Reader.ResetSurrenderBinding();
    if (vars.Reader != null) vars.Reader.ResetMerdekaBinding();
    if (vars.Reader != null) vars.Reader.ResetKarminaBinding();
    vars.RebindOnAttach = timer.CurrentPhase == TimerPhase.Running
        || timer.CurrentPhase == TimerPhase.Paused;
    if ((bool)vars.RebindOnAttach && settings["enableTimers"]
        && settings["autoReset"] && settings.ResetEnabled) {
        vars.TimerModel.Reset();
        vars.RebindOnAttach = false;
    }
    vars.Core.Attach();
    vars.SupportedBuild = false;
    vars.ReaderConfigured = false;
    vars.Decision = vars.Code.CreateInstance("Peppered.Decision");
    vars.AutoStarting = false;
    vars.AutoSplitting = false;
    vars.LastTrace = "";
    vars.LastFault = "";
    vars.MonoMetadata = null;
    vars.LastMetadataScene = "";
    vars.LastTheodoreMetadataScene = "";
    vars.LastSurrenderMetadataScene = "";
    vars.LastKarminaMetadataScene = "";
    vars.LastTvStartMetadataScene = "";
    vars.TheodoreSceneAddress = IntPtr.Zero;
    vars.MetadataRetryAfter = 0L;
    vars.OptionalMetadataRetryAfter = 0L;
    if (settings["diagnostics"]) vars.PepperedLog("process detached; run latch cleared");
}

shutdown
{
    if (vars.Reader != null) vars.Reader.ResetTheodoreBinding();
    if (vars.Reader != null) vars.Reader.ResetSurrenderBinding();
    if (vars.Reader != null) vars.Reader.ResetMerdekaBinding();
    if (vars.Reader != null) vars.Reader.ResetKarminaBinding();
    vars.RebindOnAttach = false;
    vars.Core.Attach();
    vars.SupportedBuild = false;
    vars.LastTrace = "";
    vars.LastFault = "";
    try { vars.Helper.Dispose(); } catch { }
}
