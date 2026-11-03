using ModSettings;

namespace MillingCrafting;

internal sealed class MillingSettings : JsonModSettings
{
    [Section("Power")]
    [Name("Powered without aurora")]
    [Description("Cheat: milling machines work without an aurora. Off = vanilla (aurora only), or powered by another mod such as a generator.")]
    public bool AlwaysPowered = false;

    [Section("Debug")]
    [Name("Debug logging")]
    [Description("Writes diagnostic messages (first call of each patch, stations, power) to the MelonLoader log.")]
    public bool DebugLogging = false;
}

internal static class Settings
{
    internal static readonly MillingSettings Options = new();

    internal static void OnLoad() => Options.AddToModSettings("Milling Crafting");
}
