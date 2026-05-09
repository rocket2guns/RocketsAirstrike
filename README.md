# Rocket's Airstrike

RimWorld 1.6 mod. Adds an airstrike gizmo to aerial vehicles built on [Vehicle Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3014915404).

The vehicle takes off, makes a bombing pass over a chosen cell, drops a bomb, and lands at a chosen cell on the same map.

## Supported vehicles

Out of the box, the mod patches these [Vanilla Vehicles Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3014906877) vehicles:

- **VVE_Mosquito** (helicopter, vertical takeoff)
- **VVE_Warbird** (plane, runway takeoff)

Patches are mod-gated via `PatchOperationFindMod`, so they no-op if VVE isn't loaded. Add more vehicles by editing `1.6/Patches/Vehicle_Patches.xml`. The target vehicle must have `Vehicles.CompProperties_VehicleLauncher`.

## Using the gizmo

Select an airstrike-capable vehicle, click the **Airstrike** gizmo. Two-stage targeting:

1. **Crosshair** picks the bomb cell. A radius ring previews the explosion.
2. **Landing indicator** picks where the vehicle lands once the strike is done. For runway vehicles, the standard runway-clearance check applies.

Disable conditions match the vanilla Launch gizmo 1:1 (runway clear, fuel, operators, not under roof, not moving, not rotated, etc.). Hovering shows the takeoff restriction overlay for runway vehicles.

## Settings

Mod settings expose:

- Fuel cost per airstrike (chemfuel)
- Bomb explosion radius
- Bomb damage
- Fast takeoff/landing animation toggle (doubles takeoff/landing tick rate during airstrikes; off-strike launches are unaffected)

Per-vehicle XML overrides (in `CompProperties_Airstrike`): `fuelCost`, `bombRadius`, `bombDamage`.

## Project layout

```
About/                       Mod metadata
1.6/
  Defs/Skyfallers.xml        Bombing skyfaller ThingDef
  Patches/Vehicle_Patches.xml  Per-vehicle CompAirstrike injections (mod-gated)
  Assemblies/                Released DLL
Source/
  AirstrikeMod/              C# source (csproj + cs files)
  Assemblies/                Build output (gitignored)
Languages/English/Keyed/     Translatable strings
```

## Building from source

1. Open `Source/AirstrikeMod/AirstrikeMod.csproj` in Rider / Visual Studio.
2. The csproj references RimWorld and Vehicle Framework DLLs via `HintPath`. Adjust the `<RimWorldDir>` and `<VehicleFrameworkDir>` properties at the top of the csproj if your installs aren't at the default Steam locations.
3. Build. Output goes to `Source/Assemblies/AirstrikeMod.dll`.
4. Copy the DLL to `1.6/Assemblies/` for release. (Or change `OutputPath` in the csproj to deploy directly.)

Target framework: `net48`, language version `latest`.

## Architecture (high-level)

- `CompAirstrike` exposes the gizmo and runs the two-stage targeter; mirrors VF's vanilla Launch viability checks.
- `ArrivalAction_BombMap` fires on world-flight arrival (the world leg is a degenerate same-tile flight) and synchronously spawns the bombing skyfaller before destroying the `AerialVehicleInFlight`.
- `VehicleSkyfaller_Bombing` extends `VehicleSkyfaller` directly (not `_FlyOver`, which is incomplete in VF). Owns its own animation: lerps west→east through the bomb cell, drops the bomb on a side-of-line cross-product trip, then spawns a vanilla `VehicleSkyfaller_Arriving` at the chosen landing cell. No second world flight on the return leg.
- Harmony patches: takeoff/landing tick speed doubling for the airstrike round-trip; cleanup postfix on `VehicleSkyfaller_Arriving.FinalizeLanding`; bomb-radius ring overlay on `Targeter.TargeterUpdate`.

## Dependencies

- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
- [Vehicle Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3014915404)

## License

(none specified — add a LICENSE file if you want one)
