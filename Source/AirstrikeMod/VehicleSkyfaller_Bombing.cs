using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using RimWorld;
using RimWorld.Planet;
using SmashTools;
using UnityEngine;
using Vehicles;
using Vehicles.World;
using Verse;
using Verse.Sound;

namespace AirstrikeMod
{
    // In-map bombing skyfaller. Lerps the vehicle from startCell to endCell over
    // totalTicks, drops ordnance at each bombCells entry as the moving position crosses
    // it, then either advances to the next chained segment (with a brief off-map gap)
    // or routes through ExitMap (same-map: spawn arriving skyfaller; cross-map: build a
    // return AerialVehicleInFlight back to originMapParent).
    public class VehicleSkyfaller_Bombing : VehicleSkyfaller
    {
        // Ticks the vehicle is invisible between chained passes (suppress DrawAt +
        // shadow). Reads as "the plane circled around" without needing turn animation.
        private const int GAP_TICKS_BETWEEN_PASSES = 60;

        public IntVec3 startCell;
        public IntVec3 endCell;
        public List<IntVec3> bombCells;
        public OrdinancePattern pattern = OrdinancePattern.Single;
        public ThingDef ordinance;

        public List<BombingSegment> segments;
        public int segmentIdx;
        public int gapTicksRemaining;

        public List<IntVec3> waypoints;
        public List<bool> waypointIsDrop;
        public float tension = 1f;
        public float turnSmoothness = 1f;
        public float cornerLookaheadCells = 6f;
        public float cornerMinSpeedFactor = 0.5f;
        public float traveled;
        public List<bool> bombFired;

        private List<Vector3> _splinePositions;
        private List<float> _splineCumLengths;
        private List<float> _waypointArcs;
        private List<IntVec3> _dropCells;
        private List<float> _dropArcLengths;
        private float _totalLength;
        private float _cellsPerTick = -1f;
        private bool _splineBuilt;

        public IntVec3 returnCell;
        public Rot4 returnRot;

        // Null = same-map. Non-null = return-flight target for cross-map.
        public MapParent originMapParent;

        public int totalTicks = 240;
        public float visualAltitude = 6f;
        public float scatter = 0f;

        public ThingDef strafingProjectileDef;
        public int leadCells;
        public SoundDef strafingFireSound;
        public int strafingBulletsPerRound = 1;
        public int strafingSpreadCells;
        public int strafingFireOriginOffset = 3;

        public float sortieSpeedMultiplier = 1f;
        public SoundDef bombFireSound;
        public Pawn chosenPilot;
        public SkillDef xpSkill;
        private bool strafingXpGranted;

        protected int ticksRunning;
        protected List<bool> bombsDropped;
        protected float progress;

        private CompEngineFlame _engineFlame;
        private bool _engineFlameLookedUp;

        [Obsolete("Implemented for Xml Deserialization only. Use VehicleSkyfallerMaker instead.")]
        [UsedImplicitly]
        public VehicleSkyfaller_Bombing()
        {
        }

        protected float Progress => Mathf.Clamp01(progress);

        /// <summary>
        /// Lerped along startCell -> endCell at ground altitude. Used for the shadow and
        /// the bomb-trip cross product.
        /// </summary>
        private Vector3 GroundPos
        {
            get
            {
                if (waypoints != null && waypoints.Count >= 2)
                {
                    EnsureSplineBuilt();
                    SampleSplineAtDistance(Mathf.Clamp(traveled, 0f, _totalLength),
                        out var pos, out _);
                    return pos;
                }
                return Vector3.Lerp(startCell.ToVector3Shifted(),
                    endCell.ToVector3Shifted(), Progress);
            }
        }

        public override Vector3 DrawPos
        {
            get
            {
                var pos = GroundPos;
                pos.y = AltitudeLayer.Skyfaller.AltitudeFor();
                pos.z += visualAltitude;
                return pos;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (gapTicksRemaining > 0) return;

            var pos = DrawPos;
            var extraRotation = 0f;
            if (waypoints != null)
            {
                SnapRotationToHeading(CurrentHeadingDeg(), out var rot, out var residual);
                Rotation = rot;
                extraRotation = residual;
            }
            vehicle.DrawAt(in pos, Rotation, extraRotation);
            DrawShadow();
            if (!_engineFlameLookedUp)
            {
                _engineFlame = vehicle?.GetComp<CompEngineFlame>();
                _engineFlameLookedUp = true;
            }
            _engineFlame?.DrawFlames(pos, Rotation, extraRotation);

            if (waypoints != null && AirstrikeMod.Settings is { debugDrawFlightPath: true })
                DrawDebugFlightPath();
        }

        private void DrawDebugFlightPath()
        {
            EnsureSplineBuilt();
            if (_splinePositions == null || _splinePositions.Count < 2) return;

            var y = AltitudeLayer.MetaOverlays.AltitudeFor();
            for (var i = 1; i < _splinePositions.Count; i++)
            {
                var a = _splinePositions[i - 1]; a.y = y;
                var b = _splinePositions[i]; b.y = y;
                GenDraw.DrawLineBetween(a, b, SimpleColor.White, 0.15f);
            }
            if (waypointIsDrop == null) return;
            for (var i = 0; i < waypointIsDrop.Count; i++)
            {
                if (!waypointIsDrop[i]) continue;
                GenDraw.DrawRadiusRing(waypoints[i], 0.5f);
            }
        }

        private void DrawShadow()
        {
            if (cachedShadowMaterial == null && !string.IsNullOrEmpty(def.skyfaller.shadow))
                cachedShadowMaterial = MaterialPool.MatFrom(def.skyfaller.shadow, ShaderDatabase.Transparent);
            if (cachedShadowMaterial == null) return;
            var shadowSize = vehicle.VehicleGraphic?.data?.drawSize ?? def.skyfaller.shadowSize;
            var shadowTicks = Mathf.RoundToInt(visualAltitude * 10f);
            DrawDropSpotShadow(GroundPos, Rotation, cachedShadowMaterial, shadowSize, shadowTicks);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad) 
                vehicle.EventRegistry[VehicleEventDefOf.AerialVehicleLanding].ExecuteEvents();
        }

        protected override void Tick()
        {
            base.Tick();

            // Between-passes gap: pause motion + drops until the timer expires, then
            // swap in the next segment's flight line and drop list.
            if (gapTicksRemaining > 0)
            {
                gapTicksRemaining--;
                if (gapTicksRemaining == 0)
                    BeginNextSegment();
                return;
            }

            if (waypoints != null)
            {
                SplineTick();
                return;
            }

            ticksRunning++;

            var baseStep = 1f / Math.Max(1, totalTicks);
            progress = Mathf.Min(1f,
                progress + baseStep * Mathf.Max(0.01f, sortieSpeedMultiplier));

            if (Spawned && Map != null && bombCells is { Count: > 0 })
            {
                EnsureDropTracker();
                var currentCell = GroundPos.ToIntVec3();
                var lead = pattern == OrdinancePattern.Strafing ? leadCells : 0;
                var n = bombCells.Count;
                for (var i = 0; i < n; i++)
                {
                    if (bombsDropped[i]) continue;
                    if (PastBombCell(currentCell, bombCells[i], lead))
                    {
                        FireAtCell(bombCells[i]);
                        bombsDropped[i] = true;
                    }
                }
            }
            if (progress >= 1f)
            {
                // If more chained passes remain, start the gap; otherwise leave the map.
                if (segments != null && segmentIdx + 1 < segments.Count)
                    gapTicksRemaining = GAP_TICKS_BETWEEN_PASSES;
                else
                    ExitMap();
            }
        }

        private void BeginNextSegment()
        {
            segmentIdx++;
            if (segments == null || segmentIdx >= segments.Count) return;

            var next = segments[segmentIdx];
            if (next?.bombCells == null || next.bombCells.Count == 0)
            {
                // Defensive: empty segment, skip past it (or exit if it was the last).
                if (segmentIdx + 1 < segments.Count) gapTicksRemaining = 1;
                else ExitMap();
                return;
            }

            ArrivalAction_BombMap.ChooseFlightLine(Map, next.bombCells[0], next.flightDir,
                out var newStart, out var newEnd);
            startCell = newStart;
            endCell = newEnd;
            bombCells = next.bombCells;
            Rotation = next.flightDir;
            progress = 0f;
            ticksRunning = 0;
            bombsDropped = null;
            totalTicks = ArrivalAction_BombMap.ComputeBuzzTicks(newStart, newEnd, vehicle);
        }

        private const float SPLINE_BUZZ_TIME_CONSTANT = 30f;
        private const int SAMPLES_PER_SEGMENT = 32;
        private const float CR_ALPHA = 0.5f;

        private void SplineTick()
        {
            EnsureSplineBuilt();
            if (_totalLength <= 0f) { ExitMap(); return; }
            EnsureCellsPerTick();

            traveled = Mathf.Min(_totalLength,
                traveled + _cellsPerTick * ComputeCornerSpeedFactor());

            if (Spawned && Map != null && _dropCells != null && bombFired != null)
            {
                var n = Math.Min(_dropCells.Count, bombFired.Count);
                for (var i = 0; i < n; i++)
                {
                    if (bombFired[i]) continue;
                    if (traveled < _dropArcLengths[i]) continue;
                    FireAtCell(_dropCells[i]);
                    bombFired[i] = true;
                }
            }

            if (traveled >= _totalLength) ExitMap();
        }

        private void EnsureCellsPerTick()
        {
            if (_cellsPerTick > 0f) return;
            var flightSpeed = vehicle.CompVehicleLauncher?.FlightSpeed ?? 10f;
            if (flightSpeed <= 0f) flightSpeed = 10f;
            _cellsPerTick = flightSpeed / SPLINE_BUZZ_TIME_CONSTANT
                            * Mathf.Max(0.01f, sortieSpeedMultiplier);
        }

        private float ComputeCornerSpeedFactor()
        {
            if (cornerMinSpeedFactor >= 1f || cornerLookaheadCells <= 0f) return 1f;
            if (_splinePositions == null || _splinePositions.Count < 2) return 1f;

            SampleSplineAtDistance(traveled, out _, out var here);
            var ahead = Mathf.Min(traveled + cornerLookaheadCells, _totalLength);
            SampleSplineAtDistance(ahead, out _, out var aheadTangent);

            if (here.sqrMagnitude < 0.0001f || aheadTangent.sqrMagnitude < 0.0001f) return 1f;
            var angleDeg = Vector3.Angle(here, aheadTangent);
            var t = Mathf.Clamp01(angleDeg / 90f);
            return Mathf.Lerp(1f, cornerMinSpeedFactor, t);
        }

        private void EnsureSplineBuilt()
        {
            if (_splineBuilt) return;
            if (waypoints == null || waypoints.Count < 2) return;
            _splineBuilt = true;
            BuildSplineSamples();
            BuildDrops();
            if (bombFired == null || bombFired.Count != _dropCells.Count)
            {
                bombFired = new List<bool>(_dropCells.Count);
                for (var i = 0; i < _dropCells.Count; i++) bombFired.Add(false);
            }
        }

        private void BuildDrops()
        {
            _dropCells = new List<IntVec3>();
            _dropArcLengths = new List<float>();
            if (waypointIsDrop == null || _waypointArcs == null) return;
            var n = Math.Min(waypointIsDrop.Count, _waypointArcs.Count);
            for (var i = 0; i < n; i++)
            {
                if (!waypointIsDrop[i]) continue;
                _dropCells.Add(waypoints[i]);
                _dropArcLengths.Add(_waypointArcs[i]);
            }
        }

        private void BuildSplineSamples()
        {
            _splinePositions = new List<Vector3>(waypoints.Count * SAMPLES_PER_SEGMENT);
            _splineCumLengths = new List<float>(waypoints.Count * SAMPLES_PER_SEGMENT);
            _waypointArcs = new List<float>(waypoints.Count);
            _totalLength = 0f;

            var ctrl = new List<Vector3>(waypoints.Count + 2);
            var first = waypoints[0].ToVector3Shifted();
            var second = waypoints[1].ToVector3Shifted();
            ctrl.Add(first + (first - second));
            for (var i = 0; i < waypoints.Count; i++) ctrl.Add(waypoints[i].ToVector3Shifted());
            var last = waypoints[waypoints.Count - 1].ToVector3Shifted();
            var penult = waypoints[waypoints.Count - 2].ToVector3Shifted();
            ctrl.Add(last + (last - penult));

            var numSegments = ctrl.Count - 3;
            var cumLen = 0f;
            Vector3 lastPos = ctrl[1];
            var added = false;

            for (var seg = 0; seg < numSegments; seg++)
            {
                var p0 = ctrl[seg];
                var p1 = ctrl[seg + 1];
                var p2 = ctrl[seg + 2];
                var p3 = ctrl[seg + 3];

                var samples = seg == numSegments - 1 ? SAMPLES_PER_SEGMENT + 1 : SAMPLES_PER_SEGMENT;
                for (var i = 0; i < samples; i++)
                {
                    var t = (float)i / SAMPLES_PER_SEGMENT;
                    var pos = HermiteCR(p0, p1, p2, p3, t, tension, turnSmoothness);
                    if (!added)
                    {
                        added = true;
                        cumLen = 0f;
                    }
                    else
                    {
                        cumLen += Vector3.Distance(lastPos, pos);
                    }
                    _splinePositions.Add(pos);
                    _splineCumLengths.Add(cumLen);
                    lastPos = pos;

                    if (i == 0) _waypointArcs.Add(cumLen);
                    else if (seg == numSegments - 1 && i == samples - 1) _waypointArcs.Add(cumLen);
                }
            }
            _totalLength = cumLen;
        }

        private static Vector3 HermiteCR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            float t, float tension, float turnSmoothness)
        {
            var d01 = Mathf.Pow(Mathf.Max(0.0001f, Vector3.Distance(p0, p1)), CR_ALPHA);
            var d12 = Mathf.Pow(Mathf.Max(0.0001f, Vector3.Distance(p1, p2)), CR_ALPHA);
            var d23 = Mathf.Pow(Mathf.Max(0.0001f, Vector3.Distance(p2, p3)), CR_ALPHA);

            var m1 = ((p1 - p0) / d01 - (p2 - p0) / (d01 + d12) + (p2 - p1) / d12) * d12
                     * turnSmoothness;
            var m2 = ((p2 - p1) / d12 - (p3 - p1) / (d12 + d23) + (p3 - p2) / d23) * d12
                     * turnSmoothness;

            var t2 = t * t;
            var t3 = t2 * t;
            var h00 = 2f * t3 - 3f * t2 + 1f;
            var h10 = t3 - 2f * t2 + t;
            var h01 = -2f * t3 + 3f * t2;
            var h11 = t3 - t2;
            var c = h00 * p1 + h10 * m1 + h01 * p2 + h11 * m2;

            var linear = Vector3.Lerp(p1, p2, t);
            return Vector3.LerpUnclamped(linear, c, tension);
        }

        private void SampleSplineAtDistance(float dist, out Vector3 pos, out Vector3 tangent)
        {
            if (_splinePositions == null || _splinePositions.Count == 0)
            {
                pos = Vector3.zero; tangent = Vector3.right; return;
            }
            int lo = 0, hi = _splineCumLengths.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (_splineCumLengths[mid] < dist) lo = mid + 1;
                else hi = mid;
            }
            if (lo == 0)
            {
                pos = _splinePositions[0];
                tangent = _splinePositions.Count >= 2
                    ? _splinePositions[1] - _splinePositions[0]
                    : Vector3.right;
            }
            else
            {
                var d0 = _splineCumLengths[lo - 1];
                var d1 = _splineCumLengths[lo];
                var t = d1 > d0 ? (dist - d0) / (d1 - d0) : 0f;
                pos = Vector3.Lerp(_splinePositions[lo - 1], _splinePositions[lo], t);
                tangent = _splinePositions[lo] - _splinePositions[lo - 1];
            }
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;
        }

        private float CurrentHeadingDeg()
        {
            EnsureSplineBuilt();
            SampleSplineAtDistance(Mathf.Clamp(traveled, 0f, _totalLength), out _, out var tangent);
            return -Mathf.Atan2(tangent.z, tangent.x) * Mathf.Rad2Deg;
        }

        private static void SnapRotationToHeading(float screenCwDeg, out Rot4 rot,
            out float residualDeg)
        {
            var d = screenCwDeg;
            while (d > 180f) d -= 360f;
            while (d <= -180f) d += 360f;
            if (d >= -45f && d < 45f) { rot = Rot4.East;  residualDeg = d; }
            else if (d >= 45f)         { rot = Rot4.South; residualDeg = d - 90f; }
            else                        { rot = Rot4.North; residualDeg = d + 90f; }
        }

        private void EnsureDropTracker()
        {
            if (bombsDropped == null || bombsDropped.Count != bombCells.Count)
            {
                bombsDropped = new List<bool>(bombCells.Count);
                for (var i = 0; i < bombCells.Count; i++)
                    bombsDropped.Add(false);
            }
        }

        private bool PastBombCell(IntVec3 currentCell, IntVec3 cell, int leadCells = 0)
        {
            var dx = endCell.x - startCell.x;
            var dz = endCell.z - startCell.z;
            if (dx == 0 && dz == 0) return true;

            var bombDot = (long)(cell.x - startCell.x) * dx + (long)(cell.z - startCell.z) * dz;
            var currentDot = (long)(currentCell.x - startCell.x) * dx + (long)(currentCell.z - startCell.z) * dz;
            var axisLen = Math.Abs(dx) + Math.Abs(dz);
            return currentDot >= bombDot - (long)leadCells * axisLen;
        }

        private static ThingDef _fallingBombDef;
        private static ThingDef FallingBombDef =>
            _fallingBombDef ??= DefDatabase<ThingDef>.GetNamedSilentFail("ROCKET_FallingBomb");

        private void FireAtCell(IntVec3 cell)
        {
            if (pattern == OrdinancePattern.Strafing)
            {
                FireStrafingBullet(cell);
                return;
            }
            DropBomb(cell);
        }

        private const int BOMB_FALL_TICKS = 30;

        private void PlayBombFireSound()
        {
            if (bombFireSound == null || Map == null) return;
            var planeCell = GroundPos.ToIntVec3();
            if (!planeCell.InBounds(Map)) planeCell = Position;
            bombFireSound.PlayOneShot(new TargetInfo(planeCell, Map));
        }

        private void GrantMunitionXP()
        {
            if (chosenPilot == null) return;
            chosenPilot.records.AddTo(AirstrikeDefOf.RocketsAirstrike_OrdinanceDropped, 1);
            if (xpSkill != null)
                chosenPilot.skills?.Learn(xpSkill, CompAirstrikeBase.XP_PER_MUNITION);
        }

        private const float STRAFING_XP_PER_SORTIE = 50f;

        private void GrantStrafingSortieXP()
        {
            if (strafingXpGranted) return;
            strafingXpGranted = true;
            if (chosenPilot == null || xpSkill == null) return;
            chosenPilot.skills?.Learn(xpSkill, STRAFING_XP_PER_SORTIE);
        }

        private void DropBomb(IntVec3 cell)
        {
            if (ordinance == null) return;
            GrantMunitionXP();
            PlayBombFireSound();
            var impactCell = ApplyScatter(cell);

            var def = FallingBombDef;
            var projectileDef = ordinance.projectileWhenLoaded;
            if (def == null || projectileDef == null)
            {
                DetonateInline(impactCell);
                return;
            }

            var origin = DrawPos;
            var destination = impactCell.ToVector3Shifted();
            var magnitude = (origin - destination).magnitude;

            var bomb = (ProjectileSkyfaller_AirstrikeBomb)ThingMaker.MakeThing(def);
            bomb.caster = vehicle;
            bomb.ordinance = ordinance;
            bomb.projectileDef = projectileDef;
            bomb.origin = origin;
            bomb.destination = destination;
            bomb.speedTilesPerTick = Mathf.Max(magnitude / BOMB_FALL_TICKS, 0.0001f);
            GenSpawn.Spawn(bomb, impactCell, Map);
        }

        private void FireStrafingBullet(IntVec3 cell)
        {
            if (strafingProjectileDef == null) return;
            if (!cell.InBounds(Map)) return;

            GrantStrafingSortieXP();
            var bullets = Math.Max(1, strafingBulletsPerRound);
            for (var i = 0; i < bullets; i++)
            {
                var spreadCell = ApplySpread(cell);
                var intendedTarget = PickIntendedTarget(cell, spreadCell);
                var landCell = intendedTarget.Cell;
                var originCell = ComputeFireOrigin(landCell);
                if (!originCell.InBounds(Map)) originCell = landCell;

                var origin = originCell.ToVector3Shifted();
                origin.y = AltitudeLayer.Projectile.AltitudeFor();

                var projectile = (Projectile)GenSpawn.Spawn(strafingProjectileDef, originCell, Map);
                projectile.Launch(
                    launcher: vehicle,
                    origin: origin,
                    usedTarget: intendedTarget,
                    intendedTarget: intendedTarget,
                    hitFlags: ProjectileHitFlags.IntendedTarget
                              | ProjectileHitFlags.NonTargetWorld
                              | ProjectileHitFlags.NonTargetPawns,
                    preventFriendlyFire: false,
                    equipment: null,
                    targetCoverDef: null);
            }

            strafingFireSound?.PlayOneShot(new TargetInfo(cell, Map));
        }

        // Reservoir-sample a standing pawn within spreadCells of roundCell. Falls back
        // to bulletCell when no pawn is nearby so empty parts of the rectangle still
        // receive visual rounds.
        private LocalTargetInfo PickIntendedTarget(IntVec3 roundCell, IntVec3 bulletCell)
        {
            var radius = Math.Max(0, strafingSpreadCells);
            Pawn chosen = null;
            var count = 0;
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var c = new IntVec3(roundCell.x + dx, roundCell.y, roundCell.z + dz);
                    if (!c.InBounds(Map)) continue;
                    var things = c.GetThingList(Map);
                    for (var i = 0; i < things.Count; i++)
                    {
                        if (things[i] is Pawn p && p.Spawned && p != vehicle)
                        {
                            count++;
                            if (Rand.Chance(1f / count)) chosen = p;
                        }
                    }
                }
            }
            return chosen != null ? new LocalTargetInfo(chosen) : new LocalTargetInfo(bulletCell);
        }

        private IntVec3 ComputeFireOrigin(IntVec3 target)
        {
            var dx = Math.Sign(endCell.x - startCell.x);
            var dz = Math.Sign(endCell.z - startCell.z);
            var offset = Math.Max(1, strafingFireOriginOffset);
            return new IntVec3(target.x - dx * offset, target.y, target.z - dz * offset);
        }

        private IntVec3 ApplySpread(IntVec3 cell)
        {
            if (strafingSpreadCells <= 0) return cell;
            var dx = Rand.RangeInclusive(-strafingSpreadCells, strafingSpreadCells);
            var dz = Rand.RangeInclusive(-strafingSpreadCells, strafingSpreadCells);
            var offset = new IntVec3(cell.x + dx, cell.y, cell.z + dz);
            return offset.InBounds(Map) ? offset : cell;
        }

        /// <summary>
        /// Defensive fallback when the falling-bomb visual can't be set up.
        /// </summary>
        private void DetonateInline(IntVec3 cell)
        {
            ExplosionFx.Trigger(ordinance?.projectileWhenLoaded,
                ordinance?.projectileWhenLoaded?.projectile,
                cell, Map, vehicle);
        }

        /// <summary>
        /// Linear distance distribution biases toward the target.
        /// </summary>
        private IntVec3 ApplyScatter(IntVec3 cell)
        {
            if (scatter <= 0f) return cell;
            var angle = Rand.Range(0f, 360f) * Mathf.Deg2Rad;
            var distance = Rand.Range(0f, scatter);
            var dx = Mathf.RoundToInt(Mathf.Cos(angle) * distance);
            var dz = Mathf.RoundToInt(Mathf.Sin(angle) * distance);
            var offset = new IntVec3(cell.x + dx, cell.y, cell.z + dz);
            return offset.InBounds(Map) ? offset : cell;
        }

        protected virtual void ExitMap()
        {
            var map = Map;
            if (map == null)
            {
                Destroy();
                return;
            }
            var crossMap = originMapParent != null && originMapParent != map.Parent;
            if (crossMap)
            {
                ExitMapToWorldFlight(map);
                return;
            }
            ExitMapSameMap(map);
        }

        // Same-map: spawn a VehicleSkyfaller_Arriving directly. No return world-flight.
        private void ExitMapSameMap(Map map)
        {
            var arrivingDef = vehicle.CompVehicleLauncher.Props.skyfallerIncoming;
            if (arrivingDef == null)
            {
                Log.Warning("[Rockets.Airstrike] Vehicle has no skyfallerIncoming def; landing vehicle directly.");
                GenSpawn.Spawn(vehicle, returnCell, map, returnRot);
                Destroy();
                return;
            }
            var arriving = (VehicleSkyfaller_Arriving)
                VehicleSkyfallerMaker.MakeSkyfaller(arrivingDef, vehicle);
            arriving.rotatePostLanding = returnRot;
            var landingRot = vehicle.CompVehicleLauncher.launchProtocol.LandingProperties?.forcedRotation
                              ?? returnRot;
            GenSpawn.Spawn(arriving, returnCell, map, landingRot);
            Destroy();
            vehicle.SetSustainerTarget(arriving);
            vehicle.EventRegistry[VehicleEventDefOf.AerialVehicleLanding].ExecuteEvents();
        }

        // Cross-map: build a fresh AerialVehicleInFlight bound for the origin tile.
        private void ExitMapToWorldFlight(Map map)
        {
            if (originMapParent == null)
            {
                Log.Error("[Rockets.Airstrike] Cross-map exit requested but originMapParent is null.");
                ExitMapSameMap(map);
                return;
            }

            var aerial = AerialVehicleInFlight.Create(vehicle, map.Tile);
            var arrival = new ArrivalAction_LandToCell(vehicle, originMapParent, returnCell, returnRot);
            aerial.OrderFlyToTiles(
                new List<FlightNode> { new FlightNode(originMapParent.Tile) },
                arrival);

            Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startCell, nameof(startCell));
            Scribe_Values.Look(ref endCell, nameof(endCell));
            Scribe_Collections.Look(ref bombCells, nameof(bombCells), LookMode.Value);
            Scribe_Collections.Look(ref segments, nameof(segments), LookMode.Deep);
            Scribe_Values.Look(ref segmentIdx, nameof(segmentIdx));
            Scribe_Values.Look(ref gapTicksRemaining, nameof(gapTicksRemaining));
            Scribe_Values.Look(ref pattern, nameof(pattern), OrdinancePattern.Single);
            Scribe_Defs.Look(ref ordinance, nameof(ordinance));
            Scribe_Values.Look(ref returnCell, nameof(returnCell));
            Scribe_Values.Look(ref returnRot, nameof(returnRot));
            Scribe_References.Look(ref originMapParent, nameof(originMapParent));
            Scribe_Values.Look(ref totalTicks, nameof(totalTicks), 240);
            Scribe_Values.Look(ref scatter, nameof(scatter));
            Scribe_Values.Look(ref ticksRunning, nameof(ticksRunning));
            Scribe_Collections.Look(ref bombsDropped, nameof(bombsDropped), LookMode.Value);
            Scribe_Values.Look(ref visualAltitude, nameof(visualAltitude), 6f);
            Scribe_Defs.Look(ref strafingProjectileDef, nameof(strafingProjectileDef));
            Scribe_Values.Look(ref leadCells, nameof(leadCells));
            Scribe_Defs.Look(ref strafingFireSound, nameof(strafingFireSound));
            Scribe_Values.Look(ref strafingBulletsPerRound, nameof(strafingBulletsPerRound), 1);
            Scribe_Values.Look(ref strafingSpreadCells, nameof(strafingSpreadCells));
            Scribe_Values.Look(ref strafingFireOriginOffset, nameof(strafingFireOriginOffset), 3);
            Scribe_Values.Look(ref sortieSpeedMultiplier, nameof(sortieSpeedMultiplier), 1f);
            Scribe_Defs.Look(ref bombFireSound, nameof(bombFireSound));
            Scribe_References.Look(ref chosenPilot, nameof(chosenPilot));
            Scribe_Defs.Look(ref xpSkill, nameof(xpSkill));
            Scribe_Values.Look(ref strafingXpGranted, nameof(strafingXpGranted));
            Scribe_Values.Look(ref progress, nameof(progress));
            Scribe_Collections.Look(ref waypoints, nameof(waypoints), LookMode.Value);
            Scribe_Collections.Look(ref waypointIsDrop, nameof(waypointIsDrop), LookMode.Value);
            Scribe_Values.Look(ref tension, nameof(tension), 1f);
            Scribe_Values.Look(ref turnSmoothness, nameof(turnSmoothness), 1f);
            Scribe_Values.Look(ref cornerLookaheadCells, nameof(cornerLookaheadCells), 6f);
            Scribe_Values.Look(ref cornerMinSpeedFactor, nameof(cornerMinSpeedFactor), 0.55f);
            Scribe_Values.Look(ref traveled, nameof(traveled));
            Scribe_Collections.Look(ref bombFired, nameof(bombFired), LookMode.Value);
        }
    }

    // Caravan fallback if the destination map disappears between launch and arrival.
    internal class ArrivalAction_LandInMap_NoOp : VehicleArrivalAction
    {
        public override bool DestroyOnArrival => true;

        public override void Arrived(GlobalTargetInfo target)
        {
            base.Arrived(target);
            AerialVehicle?.SwitchToCaravan();
        }
    }
}
