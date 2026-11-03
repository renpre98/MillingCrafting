using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTLD.Gear;
using UnityEngine;
using Il2CppActionList = Il2CppSystem.Collections.Generic.List<Il2Cpp.ActionPickerItemData>;
using Il2CppActionEnumerable = Il2CppSystem.Collections.Generic.IEnumerable<Il2Cpp.ActionPickerItemData>;

namespace MillingCrafting;

// Turns the milling machine into a crafting station:
// - interacting with it shows an action picker: "Repair" (vanilla Panel_Milling) or "Craft"
// - "Craft" opens the crafting panel at a hidden WorkBench child of the machine
// - blueprints named BP_Milling_* (location Workbench) are only shown at that station, and hidden at
//   real workbenches; every other blueprint is hidden at the milling machine
//
// Do NOT patch tiny native methods like WorkBench.GetLocation ("return 1"): IL2CPP folds identical
// method bodies into one function, so the patch runs for unrelated methods/objects too and crashed
// the game in the main menu without any log. Patch methods with real logic instead.
internal static class MillingStation
{
    private const string StationName = "MillingCraftingStation";

    // Sprite names from the UI atlas. Picked up at runtime from a vanilla workbench if possible.
    private static string s_CraftSprite = "ico_crafting";
    private static string s_RepairSprite = "ico_repair";

    private static bool s_RunVanillaInteraction;

    // native pointers of our hidden WorkBench stations, to recognise them as the crafting panel's location
    private static readonly HashSet<IntPtr> s_StationPointers = new();

    internal static readonly Type[] Patches =
    {
        typeof(ShowMillingActions), typeof(FilterBlueprints), typeof(BlockCrafting), typeof(CountMillingBlueprints), typeof(LocationLabel),
    };

    private static WorkBench GetOrCreateStation(MillingMachine machine)
    {
        Transform existing = machine.transform.Find(StationName);
        if (existing && existing.TryGetComponent(out WorkBench found))
        {
            s_StationPointers.Add(found.Pointer);
            return found;
        }

        GameObject go = new(StationName);
        go.transform.SetParent(machine.transform, false);
        WorkBench station = go.AddComponent<WorkBench>();

        // a fresh WorkBench has 0 for these; copy the values of a vanilla workbench when one is loaded
        WorkBench? vanilla = UnityEngine.Object.FindObjectsOfType<WorkBench>().FirstOrDefault(wb => wb.gameObject.name != StationName);
        station.m_CraftingAndRepairTimeModifier = vanilla ? vanilla!.m_CraftingAndRepairTimeModifier : 1f;
        station.m_CraftingAndRepairSkillModifier = vanilla ? vanilla!.m_CraftingAndRepairSkillModifier : 1f;
        s_StationPointers.Add(station.Pointer);
        MillingCraftingMod.Debug($"Created crafting station on '{machine.gameObject.name}' (time modifier {station.m_CraftingAndRepairTimeModifier}, skill modifier {station.m_CraftingAndRepairSkillModifier})");
        return station;
    }

    private static void TryPickUpVanillaSprites()
    {
        foreach (WorkBench wb in UnityEngine.Object.FindObjectsOfType<WorkBench>())
        {
            if (wb.m_Actions == null || wb.m_Actions.Count == 0)
            {
                continue;
            }
            foreach (ActionPickerItemData action in wb.m_Actions)
            {
                MillingCraftingMod.Debug($"Vanilla workbench action: sprite '{action.m_SpriteName}', loc '{action.m_LocID}'");
                if (action.m_LocID.Contains("Craft", StringComparison.OrdinalIgnoreCase))
                {
                    s_CraftSprite = action.m_SpriteName;
                }
            }
            return;
        }
    }

    private static void OpenCrafting(MillingMachine machine)
    {
        WorkBench station = GetOrCreateStation(machine);
        Panel_Crafting panel = InterfaceManager.GetPanel<Panel_Crafting>();
        panel.EnableCraftingAtLocation(new CraftingLocationInterface(station.Pointer));
        MelonLoader.MelonCoroutines.Start(RefreshRequirementsNextFrame(panel));
    }

    // Opened from the action picker, the requirement list stays empty until the panel is reopened.
    // Refreshing the selected blueprint one frame later fills it.
    private static System.Collections.IEnumerator RefreshRequirementsNextFrame(Panel_Crafting panel)
    {
        yield return null;
        if (!panel || !panel.IsEnabled() || !IsAtMillingStation(panel))
        {
            yield break;
        }
        try
        {
            panel.RefreshSelectedBlueprint(panel.m_ScrollBehaviour.SelectedIndex);
            MillingCraftingMod.Debug($"Refreshed requirements, container active: {panel.m_RequirementContainer.gameObject.activeInHierarchy}");
        }
        catch (Exception e)
        {
            MillingCraftingMod.Warn("Could not refresh crafting requirements: " + e);
        }
    }

    private static bool IsMillingBlueprint(BlueprintData bp) =>
        bp && bp.name.StartsWith(MillingCraftingMod.BlueprintPrefix, StringComparison.Ordinal);

    private static bool IsAtMillingStation(Panel_Crafting panel) =>
        panel.m_CurrentLocation != null && s_StationPointers.Contains(panel.m_CurrentLocation.Pointer);

    // milling blueprints only at the milling machine, and only milling blueprints there
    private static bool BelongsHere(Panel_Crafting panel, BlueprintData bp) =>
        IsMillingBlueprint(bp) == IsAtMillingStation(panel);

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

    [HarmonyPatch(typeof(MillingMachine), nameof(MillingMachine.PerformInteraction))]
    private static class ShowMillingActions
    {
        private static bool Prefix(MillingMachine __instance)
        {
            Trace.First(nameof(ShowMillingActions));
            if (s_RunVanillaInteraction)
            {
                return true;
            }

            try
            {
                TryPickUpVanillaSprites();
                MillingMachine machine = __instance;
                Il2CppActionList actions = new();
                actions.Add(new ActionPickerItemData(s_RepairSprite, "MC_Action_Repair",
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => OpenRepair(machine)))));
                actions.Add(new ActionPickerItemData(s_CraftSprite, "MC_Action_Craft",
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => OpenCrafting(machine)))));

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

    [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.ItemPassesFilter))]
    private static class FilterBlueprints
    {
        private static void Postfix(Panel_Crafting __instance, BlueprintData bpi, ref bool __result)
        {
            Trace.First(nameof(FilterBlueprints));
            if (__result && !BelongsHere(__instance, bpi))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Panel_Crafting), nameof(Panel_Crafting.CanCraftBlueprint))]
    private static class BlockCrafting
    {
        private static void Postfix(Panel_Crafting __instance, BlueprintData bpi, ref bool __result)
        {
            Trace.First(nameof(BlockCrafting));
            if (__result && !BelongsHere(__instance, bpi))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(BlueprintManager), nameof(BlueprintManager.LoadAllUserBlueprints))]
    private static class CountMillingBlueprints
    {
        // run after ModComponent added its blueprints in its own postfix
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BlueprintManager __instance)
        {
            Trace.First(nameof(CountMillingBlueprints));
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

    // The location requirement would say "Work Bench" (the station is a hidden WorkBench).
    [HarmonyPatch(typeof(CraftingRequirementLocation), nameof(CraftingRequirementLocation.Enable), new[] { typeof(Panel_Crafting), typeof(BlueprintData) })]
    private static class LocationLabel
    {
        private static void Postfix(CraftingRequirementLocation __instance, Panel_Crafting ownerPanel)
        {
            Trace.First(nameof(LocationLabel));
            if (ownerPanel && IsAtMillingStation(ownerPanel) && __instance.m_Display)
            {
                __instance.m_Display.text = Localization.Get("MC_Location_Milling");
            }
        }
    }
}
