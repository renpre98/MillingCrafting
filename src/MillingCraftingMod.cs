using MelonLoader;

namespace MillingCrafting;

public class MillingCraftingMod : MelonMod
{
    // ModComponent names blueprints "BP_" + Name, so a blueprint JSON with "Name": "Milling_Foo" becomes
    // "BP_Milling_Foo". Those blueprints declare "RequiredCraftingLocation": "Workbench" (ModComponent
    // rejects unknown locations; the station at the milling machine is a hidden WorkBench) and are only
    // shown at the milling machine. Without this mod they simply stay craftable at a workbench.
    public const string BlueprintPrefix = "BP_Milling_";

    internal static void Log(string msg) => Melon<MillingCraftingMod>.Logger.Msg(msg);
    internal static void Warn(string msg) => Melon<MillingCraftingMod>.Logger.Warning(msg);

    // diagnostics, only with "Debug logging" enabled in the mod settings
    internal static void Debug(string msg)
    {
        if (Settings.Options.DebugLogging)
        {
            Log(msg);
        }
    }

    public override void OnInitializeMelon()
    {
        Settings.OnLoad();

        // Patches are applied one at a time so a failing one does not take the others down.
        foreach (Type patch in MillingStation.Patches.Concat(MillingPower.Patches))
        {
            try
            {
                HarmonyInstance.CreateClassProcessor(patch).Patch();
                Debug($"Patch {patch.Name}: applied");
            }
            catch (Exception e)
            {
                Warn($"Patch {patch.Name}: FAILED {e}");
            }
        }
    }
}

// Logs the first call of each patch (debug logging only), so the last line before a hard crash shows
// which one was running.
internal static class Trace
{
    private static readonly HashSet<string> s_Seen = new();

    internal static void First(string name)
    {
        if (Settings.Options.DebugLogging && s_Seen.Add(name))
        {
            MillingCraftingMod.Log($"first call: {name}");
        }
    }
}
