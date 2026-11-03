using HarmonyLib;
using Il2Cpp;
using Il2CppTLD.Gear;
using UnityEngine;
using Il2CppActionList = Il2CppSystem.Collections.Generic.List<Il2Cpp.ActionPickerItemData>;
using Il2CppActionEnumerable = Il2CppSystem.Collections.Generic.IEnumerable<Il2Cpp.ActionPickerItemData>;

namespace MillingCrafting;

// Turns the milling machine into a crafting station:
// - interacting with it shows an action picker: "Repair" (vanilla Panel_Milling) or "Fabricate"
// - "Fabricate" opens the crafting panel at a hidden WorkBench child of the machine, the same way a
//   vanilla workbench does (WorkBench.OnCrafting -> Panel_Crafting.EnableCraftingAtLocation)
// - blueprints named BP_Milling_* (location Workbench) are only shown at that station, and hidden at
//   real workbenches; every other blueprint is hidden at the milling machine
//
// A hidden WorkBench because the crafting location has to be a CraftingLocation enum value, and
// ModComponent rejects values it does not know.
//
// Do NOT patch tiny native methods like WorkBench.GetLocation ("return 1"): IL2CPP folds identical
// method bodies into one function, so the patch runs for unrelated methods/objects too and crashed
// the game in the main menu without any log. Patch methods with real logic instead.
internal static class MillingStation
{
    private const string StationName = "MillingCraftingStation";

    // Neutral station: no bonus or malus of its own. Note that the game still multiplies a blueprint's
    // DurationMinutes by the selected tool's m_CraftingAndRepairTimeModifier (Panel_Crafting.GetModifiedCraftingDuration),
    // so better tools craft faster.
    private const float StationTimeModifier = 0f;
    private const float StationSkillModifier = 0f;

    // sprites from the game's UI atlas
    private const string CraftSprite = "ico_crafting";
    private const string RepairSprite = "ico_repair";

    private static bool s_RunVanillaInteraction;

    private static WorkBench GetOrCreateStation(MillingMachine machine)
    {
        Transform existing = machine.transform.Find(StationName);
        if (existing && existing.TryGetComponent(out WorkBench found))
        {
            return found;
        }

        GameObject go = new(StationName);
        go.transform.SetParent(machine.transform, false);
        WorkBench station = go.AddComponent<WorkBench>();
        station.m_CraftingAndRepairTimeModifier = StationTimeModifier;
        station.m_CraftingAndRepairSkillModifier = StationSkillModifier;
        MillingCraftingMod.LogDebug($"Created crafting station on '{machine.gameObject.name}'");
        return station;
    }

    private static void OpenCrafting(MillingMachine machine) => GetOrCreateStation(machine).OnCrafting();

    private static void OpenRepair(MillingMachine machine)
    {
        s_RunVanillaInteraction = true;
        try
        {
            machine.PerformInteraction();
        }
        finally
        {
            s_RunVanillaInteraction = false;
        }
    }

    private static bool IsMillingBlueprint(BlueprintData bp) =>
        bp && bp.name.StartsWith(MillingCraftingMod.BlueprintPrefix, StringComparison.Ordinal);

    // Checks the object itself instead of remembering native pointers: those are reused for new objects
    // once a scene is unloaded.
    private static bool IsAtMillingStation(Panel_Crafting panel)
    {
        WorkBench? station = panel.m_CurrentLocation?.TryCast<WorkBench>();
        return station && station!.name == StationName;
    }

    // milling blueprints only at the milling machine, and only milling blueprints there
    private static bool BelongsHere(Panel_Crafting panel, BlueprintData bp) =>
        IsMillingBlueprint(bp) == IsAtMillingStation(panel);

    [HarmonyPatch(typeof(MillingMachine), nameof(MillingMachine.PerformInteraction))]
    private static class ShowMillingActions
    {
        private static bool Prefix(MillingMachine __instance)
        {
            if (s_RunVanillaInteraction)
            {
                return true;
            }

            try
            {
                MillingMachine machine = __instance;
                Action repair = () => OpenRepair(machine);
                Action craft = () => OpenCrafting(machine);
                Il2CppActionList actions = new();
                actions.Add(new ActionPickerItemData(RepairSprite, "MC_Action_Repair", repair));
                actions.Add(new ActionPickerItemData(CraftSprite, "MC_Action_Craft", craft));

                // what a vanilla workbench does in WorkBench.InteractWithWorkbench
                InterfaceManager.GetPanel<Panel_ActionPicker>().ShowActionPicker(machine.gameObject, actions.Cast<Il2CppActionEnumerable>());
                return false;
            }
            catch (Exception e)
            {
                MillingCraftingMod.Warn("Could not show milling actions, falling back to repair: " + e);
                return true;
            }
        }
    }

    // A postfix on purpose: ModComponent replaces ItemPassesFilter with its own prefix and complains
    // about other mods that skip the original. Postfixes still run after it.
    // This is the only filter needed: Panel_Crafting.CanCraftBlueprint is only called for list entries.
    [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.ItemPassesFilter))]
    private static class FilterBlueprints
    {
        private static void Postfix(Panel_Crafting __instance, BlueprintData bpi, ref bool __result)
        {
            if (__result && !BelongsHere(__instance, bpi))
            {
                __result = false;
            }
        }
    }

    // Game quirk (2.59): the first time the crafting panel shows requirements in a session, the
    // requirement container is activated and filled, and only then runs its Start(), which calls Disable().
    // The list stays empty until the panel is opened again. Refill it right after Start.
    [HarmonyPatch(typeof(CraftingRequirementContainer), nameof(CraftingRequirementContainer.Start))]
    private static class RequirementsOnFirstOpen
    {
        private static void Postfix()
        {
            Panel_Crafting panel = InterfaceManager.GetPanel<Panel_Crafting>();
            if (panel && panel.IsEnabled() && IsAtMillingStation(panel))
            {
                panel.RefreshSelectedBlueprint(panel.m_ScrollBehaviour.SelectedIndex);
                MillingCraftingMod.LogDebug("Refilled the crafting requirements after their first Start()");
            }
        }
    }

    // The location requirement would say "Work Bench" (the station is a hidden WorkBench).
    [HarmonyPatch(typeof(CraftingRequirementLocation), nameof(CraftingRequirementLocation.Enable), new[] { typeof(Panel_Crafting), typeof(BlueprintData) })]
    private static class LocationLabel
    {
        private static void Postfix(CraftingRequirementLocation __instance, Panel_Crafting ownerPanel)
        {
            if (ownerPanel && IsAtMillingStation(ownerPanel) && __instance.m_Display)
            {
                __instance.m_Display.text = Localization.Get("MC_Location_Milling");
            }
        }
    }

    // Feedback for recipe authors: how many milling blueprints ModComponent loaded.
    [HarmonyPatch(typeof(BlueprintManager), nameof(BlueprintManager.LoadAllUserBlueprints))]
    private static class CountMillingBlueprints
    {
        // run after ModComponent added its blueprints in its own postfix
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BlueprintManager __instance)
        {
            int count = 0;
            foreach (BlueprintData bp in __instance.m_AllBlueprints)
            {
                if (IsMillingBlueprint(bp))
                {
                    count++;
                }
            }
            MillingCraftingMod.Log($"{count} milling machine blueprint(s) loaded");
        }
    }
}
