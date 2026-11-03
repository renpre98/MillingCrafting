using MelonLoader;
using System.Reflection;

[assembly: AssemblyCopyright("Created by Reno98")]
[assembly: MelonInfo(typeof(MillingCrafting.MillingCraftingMod), "MillingCrafting", "0.2.0", "Reno98")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

// Patches are applied one by one in MillingCraftingMod.OnInitializeMelon.
[assembly: HarmonyDontPatchAll]
