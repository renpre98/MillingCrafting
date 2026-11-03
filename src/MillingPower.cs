using System.Collections.Concurrent;
using HarmonyLib;
using Il2Cpp;
using Il2CppTLD.Electrostatic;
using Il2CppTLD.Interactions;
using Il2CppTLD.ModularElectrolizer;

namespace MillingCrafting;

// Powers the milling machine without an aurora when a power provider (MillingPowerApi, e.g. a generator
// mod) or the "Powered without aurora" setting says so. With a real aurora the game handles everything.
//
// How the game gates aurora electricity (found by disassembling Panel_Milling.Update in 2.59):
// ElectrostaticForce.IsActive is the global "electricity available" switch, fed by registered
// IElectrostaticSources (the aurora, ...). Milling machine, Panel_Milling, flashlights, radios, keypads,
// switch boxes and AuroraModularElectrolizer.HasActivePower all read it - often through inlined copies of
// the getter, so patching the getter would not even catch every caller.
// We set its backing fields only while the milling panel code runs and restore them right after, so the
// rest of the world (flashlights, wildlife, ...) does not see electricity.
//
// What did NOT work: ExternalPowerActive (+ m_RequiresExternalPower), m_UseAuroraStrengthOverride,
// ALLOW_EDITOR_OVERRIDES (resets itself), and faking AuroraManager.m_NormalizedActive & co.
internal static class MillingPower
{
    private sealed class MachineState
    {
        public MillingMachine Machine = null!;
        public AuroraModularElectrolizer[] Lamps = Array.Empty<AuroraModularElectrolizer>();
        public bool Powered;
    }

    // keyed by the machine's instance id; only touched on the main thread
    private static readonly Dictionary<int, MachineState> s_Machines = new();

    // Native pointers of the lamps (electrolizers) of powered machines. Read by the HasActivePower postfix,
    // which may run on job worker threads - so that postfix must stay free of Unity calls.
    private static readonly ConcurrentDictionary<IntPtr, byte> s_PoweredLamps = new();

    private static bool s_SelfTested;

    internal static readonly Type[] Patches =
    {
        typeof(PowerOnInitialize), typeof(ForcePower), typeof(MachineUpdate), typeof(PanelUpdate), typeof(PanelRepair), typeof(MachineLights),
    };

    private struct Electricity
    {
        public bool Active, FullyActive;
        public float NormalizedActive, Strength;
    }

    /// <summary>Powered by our side (setting or provider) - independent of a real aurora.</summary>
    internal static bool HasPowerWithoutAurora(MillingMachine? machine) =>
        machine && s_Machines.TryGetValue(machine!.GetInstanceID(), out MachineState? state) && state.Powered;

    private static MachineState GetState(MillingMachine machine)
    {
        int id = machine.GetInstanceID();
        if (s_Machines.TryGetValue(id, out MachineState? state))
        {
            return state;
        }
        state = new MachineState
        {
            Machine = machine,
            Lamps = machine.GetComponentsInChildren<AuroraModularElectrolizer>(true).ToArray(),
        };
        s_Machines[id] = state;
        MillingCraftingMod.Debug($"Milling machine '{machine.gameObject.name}' registered ({state.Lamps.Length} lamp(s))");
        return state;
    }

    // Asks the setting and the providers (once per frame from MachineUpdate) and updates the lamp lookup on changes.
    private static void UpdatePower(MachineState state)
    {
        bool powered = Settings.Options.AlwaysPowered || MillingPowerApi.AskProviders(state.Machine);
        if (powered == state.Powered)
        {
            return;
        }
        state.Powered = powered;
        foreach (AuroraModularElectrolizer lamp in state.Lamps)
        {
            if (powered)
            {
                s_PoweredLamps[lamp.Pointer] = 0;
            }
            else
            {
                s_PoweredLamps.TryRemove(lamp.Pointer, out _);
            }
        }
        MillingCraftingMod.Debug($"Milling machine '{state.Machine.gameObject.name}' power without aurora: {powered}");
    }

    // Returns true if the electricity was faked and must be restored with EndElectricity.
    private static bool BeginElectricity(MillingMachine? machine, out Electricity saved)
    {
        saved = default;
        if (ElectrostaticForce._IsActive_k__BackingField || !HasPowerWithoutAurora(machine))
        {
            return false;
        }
        saved = new Electricity
        {
            Active = ElectrostaticForce._IsActive_k__BackingField,
            FullyActive = ElectrostaticForce._IsFullyActive_k__BackingField,
            NormalizedActive = ElectrostaticForce._NormalizedActive_k__BackingField,
            Strength = ElectrostaticForce._Strength_k__BackingField,
        };
        // write the backing fields, not the setters: the setters fire the game's "electricity changed" events
        ElectrostaticForce._IsActive_k__BackingField = true;
        ElectrostaticForce._IsFullyActive_k__BackingField = true;
        ElectrostaticForce._NormalizedActive_k__BackingField = 1f;
        ElectrostaticForce._Strength_k__BackingField = 1f;
        if (!s_SelfTested)
        {
            s_SelfTested = true;
            MillingCraftingMod.Debug($"Faking electricity for the milling machine: IsActive={ElectrostaticForce.IsActive} (real: {saved.Active}, strength {saved.Strength})");
        }
        return true;
    }

    private static void EndElectricity(bool faked, Electricity saved)
    {
        if (faked)
        {
            ElectrostaticForce._IsActive_k__BackingField = saved.Active;
            ElectrostaticForce._IsFullyActive_k__BackingField = saved.FullyActive;
            ElectrostaticForce._NormalizedActive_k__BackingField = saved.NormalizedActive;
            ElectrostaticForce._Strength_k__BackingField = saved.Strength;
        }
    }

    [HarmonyPatch(typeof(MillingMachine), nameof(MillingMachine.InitializeInteraction))]
    private static class PowerOnInitialize
    {
        private static void Postfix(MillingMachine __instance)
        {
            Trace.First(nameof(PowerOnInitialize));
            try
            {
                UpdatePower(GetState(__instance));
            }
            catch (Exception e)
            {
                MillingCraftingMod.Warn("Could not register milling machine: " + e);
            }
        }
    }

    // HasActivePower has real logic (16 xrefs), so it is not folded with unrelated methods like
    // WorkBench.GetLocation was. Keep this postfix to a dictionary lookup: no Unity API, no Trace.
    [HarmonyPatch(typeof(AuroraModularElectrolizer), nameof(AuroraModularElectrolizer.HasActivePower))]
    private static class ForcePower
    {
        private static void Postfix(AuroraModularElectrolizer __instance, ref bool __result)
        {
            if (!__result && s_PoweredLamps.ContainsKey(__instance.Pointer))
            {
                __result = true;
            }
        }
    }

    // MillingMachine.Update enables the interaction (m_CanInteract) and its loud idle audio only with
    // electricity. We deliberately do NOT fake electricity here (no constant droning) and only unlock the
    // interaction; the repair panel has its own work sound.
    // Do NOT force SimpleInteraction.IsEnabled instead: it is false on purpose in safehouse edit mode,
    // forcing it broke picking the machine up with SCPlus.
    [HarmonyPatch(typeof(MillingMachine), nameof(MillingMachine.Update))]
    private static class MachineUpdate
    {
        private static void Postfix(MillingMachine __instance)
        {
            Trace.First(nameof(MachineUpdate));
            MachineState state = GetState(__instance);
            UpdatePower(state);
            if (!state.Powered)
            {
                return;
            }
            SimpleInteraction? si = __instance.GetComponent<SimpleInteraction>();
            if (si && !si!.m_CanInteract)
            {
                si.m_CanInteract = true;
            }
        }
    }

    // Panel_Milling.Update closes the panel (Disable / Interrupt) while ElectrostaticForce.IsActive is false.
    [HarmonyPatch(typeof(Panel_Milling), nameof(Panel_Milling.Update))]
    private static class PanelUpdate
    {
        private static void Prefix(Panel_Milling __instance, out (bool faked, Electricity saved) __state)
        {
            Trace.First(nameof(PanelUpdate));
            __state.faked = BeginElectricity(__instance.m_Machine, out __state.saved);
        }

        private static void Postfix((bool faked, Electricity saved) __state) => EndElectricity(__state.faked, __state.saved);
    }

    [HarmonyPatch]
    private static class PanelRepair
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Panel_Milling), nameof(Panel_Milling.OnRepair));
            yield return AccessTools.Method(typeof(Panel_Milling), nameof(Panel_Milling.BeginRepair));
        }

        private static void Prefix(Panel_Milling __instance, out (bool faked, Electricity saved) __state)
        {
            Trace.First(nameof(PanelRepair));
            __state.faked = BeginElectricity(__instance.m_Machine, out __state.saved);
        }

        private static void Postfix((bool faked, Electricity saved) __state) => EndElectricity(__state.faked, __state.saved);
    }

    // Light the indicator lamps of powered machines without an aurora (same approach as House Lights: drive
    // the electrolizer after the game's aurora update). Their electric hum stays off. Also drops machines
    // that were unloaded with their scene.
    [HarmonyPatch(typeof(AuroraManager), nameof(AuroraManager.UpdateForceAurora))]
    private static class MachineLights
    {
        private static readonly List<int> s_Gone = new();

        private static void Postfix(AuroraManager __instance)
        {
            if (s_Machines.Count == 0)
            {
                return;
            }
            Trace.First(nameof(MachineLights));
            bool aurora = __instance.AuroraIsActive();
            foreach (KeyValuePair<int, MachineState> entry in s_Machines)
            {
                MachineState state = entry.Value;
                if (!state.Machine)
                {
                    s_Gone.Add(entry.Key);
                    continue;
                }
                if (aurora || !state.Powered)
                {
                    continue;
                }
                foreach (AuroraModularElectrolizer lamp in state.Lamps)
                {
                    if (!lamp)
                    {
                        continue;
                    }
                    lamp.UpdateIntensity(1f, 1f);
                    lamp.UpdateLight(false);
                    lamp.UpdateEmissiveObjects(false);
                    lamp.StopAudio();
                }
            }
            foreach (int id in s_Gone)
            {
                // the native objects are gone and their pointers may be reused - drop the lamps too
                foreach (AuroraModularElectrolizer lamp in s_Machines[id].Lamps)
                {
                    s_PoweredLamps.TryRemove(lamp.Pointer, out _);
                }
                s_Machines.Remove(id);
            }
            s_Gone.Clear();
        }
    }
}
