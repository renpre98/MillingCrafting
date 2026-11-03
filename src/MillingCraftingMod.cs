using MelonLoader;

namespace MillingCrafting;

// MelonInfo/MelonGame are generated from the project file (version, author, link in one place).
public class MillingCraftingMod : MelonMod
{
    // ModComponent names blueprints "BP_" + Name, so a blueprint JSON with "Name": "Milling_Foo" becomes
    // "BP_Milling_Foo". Those blueprints declare "RequiredCraftingLocation": "Workbench" and are only
    // shown at the milling machine. Without this mod they simply stay craftable at a workbench.
    public const string BlueprintPrefix = "BP_Milling_";

    internal static void Log(string msg) => Melon<MillingCraftingMod>.Logger.Msg(msg);
    internal static void Warn(string msg) => Melon<MillingCraftingMod>.Logger.Warning(msg);

    // only with "Debug logging" enabled in the mod settings
    internal static void LogDebug(string msg)
    {
        if (Settings.Options.DebugLogging)
        {
            Log(msg);
        }
    }

    public override void OnInitializeMelon() => Settings.OnLoad();
}
