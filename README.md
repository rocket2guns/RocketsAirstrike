# Rocket's Airstrike

RimWorld 1.6 mod. Adds airstrike gizmos to aerial vehicles built on [Vehicle Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3014915404).

The vehicle takes off, flies a pass over the chosen target, drops ordnance or strafes with its cannon, then lands back at a chosen cell on the same map (or returns from a cross-map sortie).

## Strike modes

Three gizmos, one ordnance pool:

- **Precision Strike**. Single drop on one cell. Player picks the target cell.
- **Bombing Run**. Five drops along a player-rotated rectangle. Player picks the centre cell and rotates the footprint.
- **Strafing Run**. Cannon line. Fires a configured number of rounds along a strafe lane (separate cannon ammo, not the bomb pool).

A separate **Select ordinance** gizmo picks the loaded shell type for Precision/Bombing. Selection is per-vehicle and save-persisted.

Cargo is consumed atomically when the targeting is confirmed. Cancelling during targeting wastes nothing. A Bombing Run that can't pay all five shells is refused outright.

## Cross-map strikes

When more than one map is loaded, the gizmo opens a FloatMenu of loaded maps before targeting. Pick the destination, target on that map, then pick the landing cell on the vehicle's home map. Cross-map sorties fly real round-trip world flights and the fuel cost reflects that.

## Supported vehicles

The mod ships with **ROCKET_Falcon**, a buildable jet aircraft gated behind a Jet Aircraft research project.

Optional [Vanilla Vehicles Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=3014906877) patches add the gizmos to:

- **VVE_Mosquito** (helicopter, vertical takeoff)
- **VVE_Warbird** (plane, runway takeoff)

Patches are mod-gated via `PatchOperationFindMod`, so they no-op if VVE isn't loaded. Add more vehicles by editing `1.6/Patches/Vehicle_Patches.xml`. Each target needs `Vehicles.CompProperties_VehicleLauncher` and one or more of the `CompProperties_AirstrikePrecision` / `_BombingRun` / `_StrafingRun` comps.

There is also an opt-in LTS Ammunition compat patch at `1.6/Patches/LTS_Ammunition_Patch.xml` (mod-gated).

## Items shipped by the mod

- **High Explosive Bomb** (`Bomb_HighExplosive`). Larger payload than mortar shells, intended for bombing runs.
- **30mm Round** (`Round_30mm`). Cannon ammunition for strafing runs.
- Bomb-category ThingCategoryDef so the items shelf together.

## Records

Pilots accumulate three records, visible in their Records tab:

- **sorties flown**. Incremented once per launch, regardless of mode.
- **ordinance dropped**. Incremented by the number of bombs released. Strafing runs do not contribute.
- **time on sortie**. Wall-clock ticks the pilot spent in a sortie-flagged vehicle.

## Using the gizmos

Click a strike gizmo. Two-stage targeting:

1. Target stage. Crosshair (Precision), rotatable rectangle (Bombing Run), or strafe lane (Strafing Run). A radius ring or footprint outline previews the impact.
2. Landing stage. Pick where the vehicle lands once the strike is done. Runway vehicles get the standard runway-clearance check.

Disable conditions match the vanilla Launch gizmo (runway clear, fuel, operators, not under roof, not moving, not rotated, etc.). Hovering shows the takeoff-restriction overlay for runway vehicles.

The vehicle ends a sortie facing its pre-launch direction. The landing approach itself comes in from the opposite heading; the orientation is restored when the landing animation completes.

## Settings

Mod settings expose:

- **Airstrike fuel cost** (as a percentage of one world-tile flight).
- **Hide unavailable ordinance** (skip zero-cargo ordnance types in the picker instead of greying them out).

Per-ordnance overrides (in `OrdinanceDef`): `damageDef`, `radius`, `damage`, `preExplosionSpawn*`, `postExplosionSpawn*`, `fuelCostOverride`. Stats inherit from the source projectile (`thingDef.projectileWhenLoaded`) by default, so for vanilla mortar shells nothing extra needs to be set.

Per-vehicle overrides live in `CompProperties_AirstrikeBase` and its subclasses (Precision / BombingRun / StrafingRun) on the vehicle def.

## Project layout

```
About/                              Mod metadata. Hard-requires Vehicle Framework + Harmony.
1.6/
  Defs/
    OrdinanceDefs.xml               Ordnance defs (shell-based and HE bomb).
    Skyfallers.xml                  Bombing skyfaller + falling bomb projectile.
    SoundDefs.xml                   Mod sounds.
    RecordDefs/                     Pilot record defs (sorties, ordnance, time).
    ResearchDefs/                   Jet Aircraft research project.
    ThingCategoryDefs/              Bombs category.
    ThingDefs_Items/                HE bomb, 30mm round.
    VehicleDefs/Falcon/             ROCKET_Falcon vehicle.
  Patches/
    Vehicle_Patches.xml             Per-vehicle CompAirstrike injections (mod-gated).
    LTS_Ammunition_Patch.xml        LTS Ammunition compat (mod-gated).
  Assemblies/                       Released DLL.
Source/
  AirstrikeMod/                     C# source (csproj + cs files).
  Assemblies/                       Build output (gitignored).
Languages/English/Keyed/            Translatable strings.
Textures/                           Vehicle, item, and UI textures.
```

## Building from source

1. Open `Source/AirstrikeMod/AirstrikeMod.csproj` in Rider or Visual Studio.
2. The csproj references RimWorld and Vehicle Framework DLLs via `HintPath`. Adjust the `<RimWorldDir>` and `<VehicleFrameworkDir>` properties at the top of the csproj if your installs aren't at the default Steam locations.
3. Build. Output goes to `Source/Assemblies/AirstrikeMod.dll`.
4. Copy the DLL to `1.6/Assemblies/` for release. (Or change `OutputPath` in the csproj to deploy directly.)

Target framework: `net48`, language version `latest`.

## Architecture (high-level)

- `CompAirstrikeBase` (and the Precision / BombingRun / StrafingRun subclasses) exposes the gizmos, runs the FloatMenu for multi-map UX, and runs the two-stage targeter. It mirrors VF's vanilla Launch viability checks.
- `ArrivalAction_BombMap` fires on world-flight arrival (degenerate same-tile flight for same-map strikes, real flight for cross-map) and synchronously spawns the bombing skyfaller before destroying the `AerialVehicleInFlight`.
- `VehicleSkyfaller_Bombing` extends `VehicleSkyfaller` directly (not `_FlyOver`, which is incomplete in VF). Owns its own animation, lerps through the bomb cells, drops each bomb on a side-of-line cross-product trip, then either spawns a vanilla `VehicleSkyfaller_Arriving` (same-map) or starts a return world flight via `AerialVehicleInFlight.Create` (cross-map).
- `ProjectileSkyfaller_AirstrikeBomb` animates each falling bomb and runs the configured explosion at impact.
- `CompEngineFlame` draws engine-flame quads during takeoff, landing, and the buzz.
- Harmony patches: takeoff/landing tick speed doubling for the airstrike round-trip, post-landing rotation restore and cleanup on `VehicleSkyfaller_Arriving.FinalizeLanding`, bomb-radius ring overlay on `Targeter.TargeterUpdate`, and engine-flame draw piggybacking on `LaunchProtocol.Draw`.

See `CLAUDE.md` (repo root and `Source/AirstrikeMod/`) for the architectural rationale and the catalogue of VF quirks worked around.

## Dependencies

- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
- [Vehicle Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3014915404)

## License

(none specified, add a LICENSE file if you want one)
