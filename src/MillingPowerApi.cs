using Il2Cpp;

namespace MillingCrafting;

/// <summary>
/// Lets other mods power milling machines without an aurora, e.g. a generator mod.
/// With a real aurora the machine works anyway; providers are only asked when there is none.
/// </summary>
/// <example>
/// <code>
/// MillingPowerApi.RegisterProvider("MyGenerator", machine => MyGenerator.IsInRange(machine.transform.position));
/// </code>
/// </example>
public static class MillingPowerApi
{
    private static readonly Dictionary<string, Func<MillingMachine, bool>> s_Providers = new();
    private static readonly HashSet<string> s_Failed = new();

    /// <summary>
    /// Adds (or replaces) a power provider. It is asked every frame for every loaded milling machine on the
    /// main thread, so keep it cheap. Return true if the machine has power. Exceptions count as "no power".
    /// </summary>
    public static void RegisterProvider(string id, Func<MillingMachine, bool> provider)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(provider);
        s_Providers[id] = provider;
        s_Failed.Remove(id);
        MillingCraftingMod.Log($"Power provider '{id}' registered");
    }

    public static void UnregisterProvider(string id)
    {
        if (s_Providers.Remove(id))
        {
            MillingCraftingMod.Log($"Power provider '{id}' removed");
        }
    }

    /// <summary>True if this machine works without an aurora (mod setting or a provider).</summary>
    public static bool HasPowerWithoutAurora(MillingMachine machine) => MillingPower.HasPowerWithoutAurora(machine);

    internal static bool AskProviders(MillingMachine machine)
    {
        foreach (KeyValuePair<string, Func<MillingMachine, bool>> provider in s_Providers)
        {
            try
            {
                if (provider.Value(machine))
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                if (s_Failed.Add(provider.Key))
                {
                    MillingCraftingMod.Warn($"Power provider '{provider.Key}' failed (logged once): {e}");
                }
            }
        }
        return false;
    }
}
