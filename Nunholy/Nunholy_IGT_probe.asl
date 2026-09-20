state("Nunholy") { }

startup
{
    Assembly.Load(File.ReadAllBytes("Components/asl-help")).CreateInstance("Unity");

    vars.Helper.GameName = "Nunholy IGT Probe";
    vars.Helper.LoadSceneManager = true;
    vars.Log = (Action<object>)(value => print("[Nunholy IGT Probe] " + value));
    vars.LastLogAt = DateTime.MinValue;

    settings.Add("logValues", true, "Log MissionManager values and scene changes");
}

init
{
    vars.Helper.TryLoad = (Func<dynamic, bool>)(mono =>
    {
        var mission = mono["MissionManager"];

        vars.Helper["StartTime"] = mono.Make<float>(mission, "startTime");
        vars.Helper["Chapter"] = mono.Make<int>(mission, "Inst", "curChapter");
        vars.Helper["IsBattle"] = mono.Make<bool>(mission, "Inst", "isBattle");

        return true;
    });

    vars.Log("Process: " + game.ProcessName + " (PID " + game.Id + ")");
    vars.Log("Unity version: " + vars.Helper.UnityVersion);

    try
    {
        var unityPlayer = game.Modules.Cast<ProcessModule>()
            .FirstOrDefault(module => module.ModuleName.Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase));

        if (unityPlayer != null)
        {
            vars.Log("UnityPlayer path: " + unityPlayer.FileName);
            vars.Log("UnityPlayer file version: " + unityPlayer.FileVersionInfo.FileVersion);
            vars.Log("UnityPlayer memory size: " + unityPlayer.ModuleMemorySize);
        }
        else
        {
            vars.Log("UnityPlayer.dll was not found in the process module list.");
        }
    }
    catch (Exception exception)
    {
        vars.Log("Could not inspect UnityPlayer.dll: " + exception.Message);
    }
}

update
{
    if (!vars.Helper.Loaded)
        return false;

    current.Scene = vars.Helper.Scenes.Active.Name ?? "";

    bool sceneChanged = old.Scene != current.Scene;
    bool startTimeChanged = Math.Abs(old.StartTime - current.StartTime) > 0.001f;
    bool chapterChanged = old.Chapter != current.Chapter;
    bool battleChanged = old.IsBattle != current.IsBattle;

    if (settings["logValues"] && (sceneChanged || startTimeChanged || chapterChanged || battleChanged))
    {
        vars.Log(
            "scene=" + current.Scene
            + ", startTime=" + current.StartTime.ToString("F6")
            + ", chapter=" + current.Chapter
            + ", isBattle=" + current.IsBattle
        );
    }
}

exit
{
    vars.Log("Nunholy process exited.");
}

shutdown
{
    vars.Log("Probe unloaded.");
}
