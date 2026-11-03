# Milling Crafting

A mod for **The Long Dark** that turns the milling machine into a crafting station.

Interacting with a milling machine shows two actions:

- **Repair**: the vanilla repair menu, unchanged.
- **Fabricate**: opens the crafting menu at the milling machine. It shows only blueprints made for the milling machine, and those blueprints do not show up at workbenches.

As in vanilla, the milling machine needs electricity. That means an aurora, a power provider from another mod (for example a generator, see [Power API](#power-api-for-mod-authors)), or the cheat setting below.

> **Status:** prototype (0.2.0). It includes one test recipe (hunting knife from an improvised knife and scrap metal). Real recipes and balancing will follow.

## Requirements

- The Long Dark 2.59
- [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.x
- [ModComponent](https://github.com/dommrogers/ModComponent) (loads the blueprints and localization)
- [ModSettings](https://github.com/DigitalzombieTLD/ModSettings)

## Installation

Copy `MillingCrafting.dll` and `MillingCrafting.modcomponent` into the game's `Mods` folder.

## Settings

Go to *Options → Mod Settings → Milling Crafting*.

| Setting | Default | Effect |
|---|---|---|
| Powered without aurora | off | Cheat: all milling machines work without an aurora. Their indicator lamps glow, the idle hum stays off. |
| Debug logging | off | Writes diagnostic messages to the MelonLoader log. |

## Adding recipes (for mod authors)

Any ModComponent mod can add milling recipes; there is no code dependency. Write a normal ModComponent blueprint with these settings:

- set `"Name"` to start with **`Milling_`** (ModComponent turns it into `BP_Milling_…`);
- set `"RequiredCraftingLocation": "Workbench"`.

```json
{
    "Name": "Milling_HuntingKnife",
    "RequiredGear": [
        { "Item": "GEAR_KnifeImprovised", "Count": 1 },
        { "Item": "GEAR_ScrapMetal", "Count": 2 }
    ],
    "RequiredCraftingLocation": "Workbench",
    "RequiresLight": true,
    "CraftedResult": "GEAR_Knife",
    "CraftedResultCount": 1,
    "DurationMinutes": 90,
    "CraftingAudio": "PLAY_MILLINGMACHINEREPAIR",
    "AppliedSkill": "None",
    "ImprovedSkill": "None"
}
```

With Milling Crafting installed, the recipe is available only at the milling machine. Without it, the recipe stays an ordinary workbench recipe, so your mod keeps working.

## Power API (for mod authors)

Another mod can power milling machines without an aurora by registering a provider. Reference `MillingCrafting.dll` and call:

```csharp
using MillingCrafting;

// e.g. in OnInitializeMelon
MillingPowerApi.RegisterProvider("MyGenerator", machine =>
    MyGenerator.IsRunningNear(machine.transform.position));
```

- The provider gets the `Il2Cpp.MillingMachine` and returns `true` if that machine has power.
- It runs on the main thread, every frame, for every loaded milling machine. Keep it cheap: cache the result and check a distance or a flag, not the whole scene.
- Exceptions count as "no power" and are logged once.
- `MillingPowerApi.UnregisterProvider(id)` removes the provider again.
- `MillingPowerApi.HasPowerWithoutAurora(machine)` tells whether a machine currently has power without an aurora.
- During a real aurora the machine works anyway; providers do not matter then.

If Milling Crafting should be optional for your mod, call the API only when it is loaded, for example after checking `MelonBase.FindMelon("MillingCrafting", "Reno98") != null`. Keep those calls in a separate method, so your mod does not load Milling Crafting types when it is missing.

### How the power works

The game's milling code checks the global aurora electricity (`ElectrostaticForce.IsActive`). For a powered machine, Milling Crafting sets that only while the milling machine's own panel code runs and restores it right after. The rest of the world (flashlights, radios, door keypads, …) does not see the electricity. The machine's indicator lamps are lit directly, and its constant idle hum stays off.

## Building from source

The project compiles against the assemblies that MelonLoader generated for your game installation:

```sh
dotnet build -c Release                       # output in bin/Release/net6.0
dotnet build -c Release -p:CopyToMods=true    # also copy into the game's Mods folder
```

The game path defaults to the Steam library on Linux (`~/.local/share/Steam/steamapps/common/TheLongDark`) or Windows. Override it with `-p:TheLongDarkPath=…`.

`BuildSupport/` holds a small polyfill for a compiler conflict with Unity 6's `NullableAttribute`.

## Credits

- Lighting the machine's lamps without an aurora uses the same approach as [House Lights](https://github.com/DemonBunnyBon/tld-house-lights) by DemonBunnyBon.

Author: Reno98

## License

[MIT](LICENSE)
