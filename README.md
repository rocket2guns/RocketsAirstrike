# Rocket's Airstrike

A RimWorld 1.6 mod for [Vehicle Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3014915404). Adds airstrike gizmos to flying vehicles: drop bombs on a point, carpet-bomb in a line, or strafe with cannons. The aircraft taxis, takes off, flies the strike, then returns and lands - same map or across the world.

## What you get in-game

### Three strike modes

- **Precision Strike** - one bomb on one cell. Use when you only need to delete one target.
- **Bombing Run** - a line of bombs along a rotatable rectangle. Spacing is driven by the selected bomb's explosion radius.
- **Strafing Run** - cannon fire down a long, thin rectangle. Uses a separate cannon ammo type (steel by default), not the bomb pool.

Each mode is a separate gizmo on the vehicle. A fourth **Select ordinance** gizmo picks which shell/bomb type the Precision and Bombing Run gizmos drop.

### Multi-target chains (Shift+click)

For Bombing Run and Strafing Run, **hold Shift while clicking** to add another rectangle to the run. Right-click to commit the chain (or cancel if nothing's locked).

When you chain two or more targets, the aircraft flies a smooth curved path through every rectangle in one continuous pass - no second takeoff, no landing in between. The plane banks naturally between rectangles. Chain length is capped by how many sortie-worths of ordnance you have in cargo.

A faded ghost of the aircraft is shown on the approach side of the cursor so you can see where the plane will be coming from.

### Pilot skill matters

Each strike comp has a required skill (Intellectual for bombs, Shooting for cannons) and a minimum level. The vehicle won't launch the strike until at least one assigned pilot meets the level. Higher skill levels narrow the bomb scatter and earn XP per drop (or once per strafing sortie). The tooltip on each gizmo shows the targeting accuracy rating in colour.

### Cross-map strikes

If you have more than one map loaded, clicking a strike gizmo opens a menu to pick which map you're striking. Pick the destination, then target on that map. The aircraft does a real round-trip world flight and is billed real fuel. Cross-map sorties also let you pick where the aircraft lands on the home map afterwards.

### Settings

- **General** tab: fuel cost percentage for an airstrike sortie.
- **Debug** tab: draw the spline flight path in white for tuning.

## Vehicles that get the gizmos

**Ships in this mod**

- **Falcon** (`ROCKET_Falcon`) - jet aircraft, gated behind the Jet Aircraft research project (or Combat Vehicles when Vanilla Vehicles Expanded is loaded). Precision, Bombing Run, and Strafing Run.

**Mod-gated compatibility patches** (no-op if the host mod isn't loaded)

- **Vanilla Vehicles Expanded**: adds Precision + Strafing Run to the **Warbird**.
- **LTS Ammunition (Simple Ammo Pack)**: swaps the Falcon's strafing ammo from steel to `AmmoIndustrial`.

You can hand-edit `1.6/Patches/Vehicle_Patches.xml` to add the gizmos to any other aerial vehicle.

## New items

- **Explosive / High-Explosive / Incendiary / Antigrain bomb** (`ROCKET_Bomb_*`) - large air-dropped bombs, craftable at the machining table. Drop in Bombs category.
- The Bombing Run and Precision Strike can also drop any vanilla mortar shell from cargo.

## Pilot records

Pilots earn three records visible in the Records tab:

- **sorties flown** - once per launch, regardless of mode.
- **ordinance dropped** - one per bomb (chosen pilot only, not the whole crew).
- **time on sortie** - wall-clock ticks spent in an airstrike-flagged sortie.

---

## For modders: adding airstrike to your own vehicle

The mod exposes three comps that you bolt onto a `Vehicles.VehicleDef`. The vehicle must already have a `Vehicles.CompProperties_VehicleLauncher` (i.e. it's a Vehicle Framework aerial vehicle).

### Quickest path: patch an existing vehicle

```xml
<Operation Class="PatchOperationFindMod">
    <mods><li>YourHostMod</li></mods>
    <match Class="PatchOperationAdd">
        <xpath>Defs/Vehicles.VehicleDef[defName="YourVehicle"]/comps</xpath>
        <value>
            <li Class="AirstrikeMod.CompProperties_AirstrikePrecision">
                <skyfallerBombing>ROCKET_BombingPass</skyfallerBombing>
                <bombFireSound>ROCKET_HardpointLatchRelease</bombFireSound>
                <requiredSkill>Intellectual</requiredSkill>
                <requiredSkillLevel>4</requiredSkillLevel>
                <scatter>1</scatter>
                <skillScatter>4</skillScatter>
                <ordinance>
                    <li>Shell_HighExplosive</li>
                    <li>ROCKET_Bomb_Explosive</li>
                </ordinance>
            </li>
        </value>
    </match>
</Operation>
```

Mod-gate with `PatchOperationFindMod` so your patch is silent when the host mod (or this mod) isn't loaded.

### The three comp classes

| Class | Gizmo | Pattern |
|---|---|---|
| `AirstrikeMod.CompProperties_AirstrikePrecision` | Precision Strike | Single bomb on the cursor cell |
| `AirstrikeMod.CompProperties_AirstrikeBombingRun` | Bombing Run | Line of `dropCount` bombs |
| `AirstrikeMod.CompProperties_AirstrikeStrafingRun` | Strafing Run | Cannon fire across a rectangle |

You can add any subset. A vehicle with all three gets all three gizmos.

### Shared fields (all three comps)

These come from `CompProperties_AirstrikeBase`:

| Field | Default | Meaning |
|---|---|---|
| `skyfallerBombing` | required | Skyfaller def used for the buzz. Always `ROCKET_BombingPass` unless you've defined your own. |
| `ordinance` | (list) | Allowed ThingDefs the player can pick in the ordnance selector. Precision and Bombing only. Strafing ignores this. |
| `scatter` | `0` | Floor on per-bomb random offset in cells. 0 = always lands exactly. |
| `skillScatter` | `0` | Additional scatter cells added at zero pilot skill; falls to 0 at max skill. Total scatter is capped at `flyAltitude` and at 16 cells. |
| `flyAltitude` | `6` | Draw altitude of the plane during the buzz. Also caps total scatter. Lower for low-altitude strafers. |
| `sortieSpeedMultiplier` | `1` | Multiplies the plane's `FlightSpeed` for the whole sortie. `0.5` = slow movie pass; `1.5` = streaking attack. |
| `bombFireSound` | `null` | One-shot sound at the plane's position per bomb dropped. Strafing has its own `fireSound` field. |
| `requiredSkill` | `null` | `SkillDef` gating launch and driving accuracy. Null = anyone can use, no XP awarded. |
| `requiredSkillLevel` | `0` | Minimum level the best pilot must have in `requiredSkill`. Ignored if `requiredSkill` is null. |

### `CompProperties_AirstrikeBombingRun` extras

| Field | Default | Meaning |
|---|---|---|
| `dropCount` | `5` | Bombs per sortie. Required-shells count. Drives footprint length. |
| `spacingMultiplier` | `1.8` | Bomb-to-bomb spacing as a multiple of the selected bomb's explosion radius. |

### `CompProperties_AirstrikeStrafingRun` extras

| Field | Default | Meaning |
|---|---|---|
| `projectileDef` | required | Vanilla bullet projectile to spawn per shot (e.g. `Bullet_AutocannonTurret`). |
| `ammoDef` | `null` | Cargo item consumed for rounds. Null = free (no ammo cost). |
| `ammoPerRound` | `1` | Units of `ammoDef` per round fired. |
| `fireSound` | `null` | One-shot sound at each fired cell. |
| `runLength` | `40` | Cells along the flight axis. Drives sortie ammo cost (`runLength × ammoPerRound`). |
| `runWidth` | `3` | Cells perpendicular to flight axis. Each fire-cell gets a random perpendicular offset within this width. |
| `leadCells` | `8` | Fires this many cells ahead of the plane's current position (so the bullets visibly fall from the plane). |
| `bulletsPerRound` | `4` | Bullets spawned per fired cell. |
| `spreadCells` | `1` | Per-bullet biaxial scatter on top of the run-width perpendicular offset. |
| `fireOriginOffset` | `3` | Cells between bullet spawn point and its target cell (gives the bullet visible travel time). |

### Sounds and visuals provided by the mod

Reference these directly from your patches - they're shipped by Rocket's Airstrike.

| Def | What it's for |
|---|---|
| `ROCKET_BombingPass` | The standard buzz skyfaller. Use as `<skyfallerBombing>`. |
| `ROCKET_HardpointLatchRelease` | Per-bomb release thunk. Positional, pitch-varied. |
| `ROCKET_InterfaceBeep1` | UI beep on confirmed target placement (used internally by all targeters). |

### Engine flames (optional, cosmetic)

Add `AirstrikeMod.CompProperties_EngineFlame` to draw animated exhaust flames during takeoff, landing, and the buzz. See the Falcon's vehicle def for a complete example - you set the flame `drawSize`, `pivot`, `rotationDeg`, and per-cardinal-direction offset lists.

### Multi-comp on one vehicle

The "Select ordinance" picker and the `ShowAllOrdinance` toggle are stored on the *primary* comp (first-found) of the vehicle. All comps share the same selected ordnance and visibility setting at runtime, so you can attach Precision + BombingRun and they'll always operate on the same bomb type.

### Save compatibility note

If you rename or remove a `CompProperties_Airstrike*` from a vehicle after a save was created with the previous setup, the comp's stored fields (`selectedOrdinance`, `showAllOrdinance`) will simply default on load. In-flight sorties survive save/load via the skyfaller's own Scribe entries.

---

## Project layout

```
About/                          Mod metadata; depends on VF + Harmony.
1.6/
  Defs/
    OrdinanceDefs.xml           Per-shell explosion-customisation defs.
    Skyfallers.xml              ROCKET_BombingPass + ROCKET_FallingBomb.
    SoundDefs.xml               Mod sounds (engine, hardpoint, beep).
    Stats/                      ROCKET_TargetingAbility (vestigial; skill-driven now).
    RecordDefs/                 Pilot record defs.
    ResearchDefs/               ROCKET_JetAircraft, ROCKET_StealthAircraft.
    ThingCategoryDefs/          Bombs category.
    ThingDefs_Items/            HE / Incendiary / Antigrain bombs.
    VehicleDefs/Falcon/         ROCKET_Falcon + buildable.
  Patches/
    Vehicle_Patches.xml         VVE Warbird & Falcon upgrades (mod-gated).
    LTS_Ammunition_Patch.xml    LTS Ammunition compat (mod-gated).
  Assemblies/                   Shipped DLL.
Source/AirstrikeMod/            C# source (csproj + .cs files).
Languages/English/Keyed/        Translatable strings.
Textures/                       Vehicle, item, and UI textures.
Sounds/                         WAV files referenced by SoundDefs.
```

## Building from source

1. Open `Source/AirstrikeMod/AirstrikeMod.csproj` in Rider or Visual Studio.
2. The csproj points at RimWorld and Vehicle Framework DLLs via `<RimWorldDir>` and `<VehicleFrameworkDir>` properties at the top - edit those if your installs aren't at the default Steam locations.
3. Build. Output goes to `Source/Assemblies/AirstrikeMod.dll`.
4. Copy the DLL to `1.6/Assemblies/` for release.

Target framework `net48`, language version `latest`.

## Dependencies

- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
- [Vehicle Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3014915404)
