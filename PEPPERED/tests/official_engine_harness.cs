using System;
using System.IO;
using System.Text;
using LiveSplit.ASL;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;

public static class OfficialEngineHarness
{
    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
    }

    private static int ParseAndCompile(string path)
    {
        ASLScript script = ASLParser.Parse(File.ReadAllText(path));
        if (script == null)
            throw new InvalidOperationException("ASLParser.Parse returned null");

        // ASLParser.Parse performs the official Irony parse and constructs
        // every ASLMethod, whose constructor invokes the official C# compiler.
        Console.WriteLine("PARSE_COMPILE_OK");
        return 0;
    }

    private static int RunSettingsStartup(string path)
    {
        ASLScript script = ASLParser.Parse(File.ReadAllText(path));
        if (script == null)
            throw new InvalidOperationException("ASLParser.Parse returned null");

        // This is the real LiveSplit model constructor. Only optional UI objects
        // are null; Run and its comparison factory are real runtime objects.
        LiveSplitState state = new LiveSplitState(
            new Run(new StandardComparisonGeneratorsFactory()),
            null, null, null, null);
        ASLSettings settings = script.RunStartup(state);
        if (settings == null)
            throw new InvalidOperationException("ASLScript.RunStartup returned null");

        Console.WriteLine("VALID_LIVESPLIT_STATE\tRun\tStandardComparisonGeneratorsFactory");
        Console.WriteLine("SETTINGS_COUNT\t" + settings.Settings.Count);
        Console.WriteLine("ORDERED_SETTINGS_COUNT\t" + settings.OrderedSettings.Count);
        foreach (ASLSetting setting in settings.OrderedSettings)
        {
            Console.WriteLine("SETTING\t" + Encode(setting.Id)
                + "\t" + (setting.DefaultValue ? "true" : "false")
                + "\t" + Encode(setting.Parent)
                + "\t" + Encode(setting.Label)
                + "\t" + (setting.Value ? "true" : "false"));
        }
        return 0;
    }

    public static int Main(string[] args)
    {
        if (args.Length != 2 || (args[0] != "parse" && args[0] != "settings"))
        {
            Console.Error.WriteLine("usage: official_engine_harness.exe parse|settings <script.asl>");
            return 2;
        }

        try
        {
            return args[0] == "parse"
                ? ParseAndCompile(args[1])
                : RunSettingsStartup(args[1]);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("OFFICIAL_ENGINE_FAIL\t" + error);
            return 1;
        }
    }
}
