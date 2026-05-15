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
        public float tangentScale = 1f;
        public float cornerLookaheadCells = 6f;
        public float cornerMinSpeedFactor = 0.5f;
        public float traveled;
        public List<bool> bombFired;

        private List<Vector3> _splinePositions;
        private List<float> _splineCumLengths;
        private List<float> _waypointArcs;
        private List<Vector3> _waypointTangents;
        private List<IntVec3> _dropCells;
        private List<float> _dropArcLengths;
        private float _totalLength;
        private float _cellsPerTick = -1f;
        private bool _splineBuilt;

        public IntVec3 returnCell;
        public Rot4 returnRot;

        // Null = same-map. Non-null = return-flight target for cross-map.
        public MapParent originMapParent;

        public bool inPlaceMode;
        public int hoverLandingTicks = 90;

        public int totalTicks = 240;
        public float visualAltitude = 6f;
        public float scatter = 0f;

        public ThingDef strafingProjectileDef;
        public int leadCells;
        public SoundDef strafingFireSound;
        public int strafingBulletsPerRound = 1;
        public int strafingSpreadCells;
        public int strafingFireOriginOffset = 3;
        public int strafingRunWidth = 1;

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

        private CompRotorSpinUp _rotorSpinUp;
        private bool _rotorSpinUpLookedUp;

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
                if (waypoints is { Count: >= 2 })
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
            float spriteHeadingDeg;
            if (waypoints != null)
            {
                var tan = ComputeFlightTangent();
                var screenCwDeg = -Mathf.Atan2(tan.z, tan.x) * Mathf.Rad2Deg;
                spriteHeadingDeg = screenCwDeg + 90f;
                SnapRotationToHeading(screenCwDeg, VehicleNegatesWestResidual(),
                    out var rot, out var residual);
                Rotation = rot;
                extraRotation = residual;
            }
            else
            {
                spriteHeadingDeg = Rotation.AsAngle;
            }
            vehicle.DrawAt(in pos, Rotation, extraRotation);
            DrawShadow(spriteHeadingDeg);
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

        private void DrawShadow(float headingDeg)
        {
            if (cachedShadowMaterial == null && !string.IsNullOrEmpty(def.skyfaller.shadow))
                cachedShadowMaterial = MaterialPool.MatFrom(def.skyfaller.shadow, ShaderDatabase.Transparent);
            if (cachedShadowMaterial == null) return;
            var size = vehicle.VehicleGraphic?.data?.drawSize ?? def.skyfaller.shadowSize;
            AirstrikeShadow.Draw(GroundPos, size, headingDeg, visualAltitude, cachedShadowMaterial);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            if (!respawningAfterLoad && vehicle == null)
            {
                Log.Warning("[Rockets.Airstrike] ROCKET_BombingPass spawned without a sortie (likely dev mode). Destroying.");
                Destroy(DestroyMode.Vanish);
                return;
            }
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
                vehicle.EventRegistry[VehicleEventDefOf.AerialVehicleLanding].ExecuteEvents();
        }

        protected override void Tick()
        {
            TickRotorSpin();
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

            SyncPositionToGroundPos();

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

        private void SplineTick()
        {
            EnsureSplineBuilt();
            if (_totalLength <= 0f) { ExitMap(); return; }
            EnsureCellsPerTick();

            traveled = Mathf.Min(_totalLength,
                traveled + _cellsPerTick * ComputeCornerSpeedFactor());

            SyncPositionToGroundPos();

            if (Spawned && Map != null && _dropCells != null && bombFired != null)
            {
                var lead = pattern is OrdinancePattern.Strafing ? leadCells : 0;
                var n = Math.Min(_dropCells.Count, bombFired.Count);
                for (var i = 0; i < n; i++)
                {
                    if (bombFired[i]) continue;
                    if (traveled < _dropArcLengths[i] - lead) continue;
                    FireAtCell(_dropCells[i]);
                    bombFired[i] = true;
                }
            }

            if (traveled >= _totalLength) ExitMap();
        }

        private void TickRotorSpin()
        {
            if (!_rotorSpinUpLookedUp)
            {
                _rotorSpinUp = vehicle?.GetComp<CompRotorSpinUp>();
                _rotorSpinUpLookedUp = true;
            }
            if (_rotorSpinUp == null) return;
            vehicle.DrawTracker?.overlayRenderer?.SetAcceleration(_rotorSpinUp.TargetRate);
        }

        private void SyncPositionToGroundPos()
        {
            if (!Spawned || Map == null) return;
            var cell = GroundPos.ToIntVec3();
            if (cell.InBounds(Map) && cell != Position) Position = cell;
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

        /*
        Okay buckle up. Here we walk segments in sync with the waypoint layout (prefix 1 for entry, 2 for
        in-place anchor+forward). Single-drop segments map to 1 waypoint; multi-drop
        line segments map to 2 waypoints (first + last drop), with intermediates
        placed by arc-length fraction across the (geometrically straight) section
        between those two waypoints.
         */
        private void BuildDrops()
        {
            _dropCells = new List<IntVec3>();
            _dropArcLengths = new List<float>();
            if (segments == null || _waypointArcs == null) return;

            var wpIdx = inPlaceMode ? 2 : 1;
            for (var s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                var cells = seg?.bombCells;
                if (cells == null || cells.Count == 0) continue;

                if (cells.Count == 1)
                {
                    if (wpIdx < _waypointArcs.Count)
                    {
                        _dropCells.Add(cells[0]);
                        _dropArcLengths.Add(_waypointArcs[wpIdx]);
                    }
                    wpIdx += 1;
                    continue;
                }

                if (wpIdx + 1 >= _waypointArcs.Count) break;
                var firstArc = _waypointArcs[wpIdx];
                var lastArc = _waypointArcs[wpIdx + 1];
                var first = cells[0];
                var last = cells[cells.Count - 1];
                var dx = last.x - first.x;
                var dz = last.z - first.z;
                var totalDist = Mathf.Sqrt(dx * dx + dz * dz);
                if (totalDist <= 0.0001f)
                {
                    _dropCells.Add(first);
                    _dropArcLengths.Add(firstArc);
                }
                else
                {
                    for (var k = 0; k < cells.Count; k++)
                    {
                        var c = cells[k];
                        var cdx = c.x - first.x;
                        var cdz = c.z - first.z;
                        var f = Mathf.Sqrt(cdx * cdx + cdz * cdz) / totalDist;
                        _dropCells.Add(c);
                        _dropArcLengths.Add(Mathf.Lerp(firstArc, lastArc, f));
                    }
                }
                wpIdx += 2;
            }
        }

        private void BuildSplineSamples()
        {
            ComputeWaypointTangents();

            _splinePositions = new List<Vector3>(waypoints.Count * SAMPLES_PER_SEGMENT);
            _splineCumLengths = new List<float>(waypoints.Count * SAMPLES_PER_SEGMENT);
            _waypointArcs = new List<float>(waypoints.Count);
            _totalLength = 0f;

            var numSegments = waypoints.Count - 1;
            var cumLen = 0f;
            Vector3 lastPos = waypoints[0].ToVector3Shifted();
            var added = false;

            for (var seg = 0; seg < numSegments; seg++)
            {
                var p0 = waypoints[seg].ToVector3Shifted();
                var p1 = waypoints[seg + 1].ToVector3Shifted();
                var m0 = _waypointTangents[seg];
                var m1 = _waypointTangents[seg + 1];

                var samples = seg == numSegments - 1 ? SAMPLES_PER_SEGMENT + 1 : SAMPLES_PER_SEGMENT;
                for (var i = 0; i < samples; i++)
                {
                    var t = (float)i / SAMPLES_PER_SEGMENT;
                    var pos = Hermite(p0, p1, m0, m1, t);
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

        private static Vector3 Hermite(Vector3 p0, Vector3 p1, Vector3 m0, Vector3 m1, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            var h00 = 2f * t3 - 3f * t2 + 1f;
            var h10 = t3 - 2f * t2 + t;
            var h01 = -2f * t3 + 3f * t2;
            var h11 = t3 - t2;
            return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
        }

        // Tangent rule: drops belonging to a multi-cell line segment get the segment's
        // line direction (so the curve enters/exits the line colinearly; line interior
        // becomes geometrically straight). Everything else (entry/exit, single drops,
        // in-place anchor + forward/behind nodes) uses centered finite difference on
        // neighbor positions. Magnitude = average chord of incident edges, scaled by
        // tangentScale. Endpoints mirror one neighbor for the missing side.
        private void ComputeWaypointTangents()
        {
            var n = waypoints.Count;
            _waypointTangents = new List<Vector3>(n);
            var positions = new Vector3[n];
            for (var i = 0; i < n; i++) positions[i] = waypoints[i].ToVector3Shifted();

            var forcedDir = new Vector3[n];
            var forced = new bool[n];
            AssignLineForcedDirections(forcedDir, forced);

            for (var i = 0; i < n; i++)
            {
                Vector3 prev = i == 0
                    ? positions[i] - (positions[i + 1] - positions[i])
                    : positions[i - 1];
                Vector3 next = i == n - 1
                    ? positions[i] + (positions[i] - positions[i - 1])
                    : positions[i + 1];
                var chord = 0.5f * ((positions[i] - prev).magnitude
                                    + (next - positions[i]).magnitude);

                Vector3 dir;
                if (forced[i])
                {
                    dir = forcedDir[i];
                }
                else
                {
                    var centered = next - prev;
                    dir = centered.sqrMagnitude > 0.0001f
                        ? centered.normalized
                        : Vector3.right;
                }
                _waypointTangents.Add(dir * chord * tangentScale);
            }
        }

        // Walks segments in sync with the waypoint layout (prefix 1 for entry, 2 for
        // in-place anchor+forward). Multi-drop line segments contribute 2 waypoints
        // (first + last drop); both get forced line direction. Single-drop segments
        // contribute 1 waypoint with no forced direction (neighbor geometry decides).
        private void AssignLineForcedDirections(Vector3[] forcedDir, bool[] forced)
        {
            if (segments == null) return;
            var idx = inPlaceMode ? 2 : 1;
            for (var s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];
                var cellCount = seg?.bombCells?.Count ?? 0;
                if (cellCount == 0) continue;
                if (cellCount == 1)
                {
                    idx += 1;
                    continue;
                }
                var lineDir = ComputeLineDir(seg);
                if (idx < waypoints.Count) { forcedDir[idx] = lineDir; forced[idx] = true; }
                if (idx + 1 < waypoints.Count) { forcedDir[idx + 1] = lineDir; forced[idx + 1] = true; }
                idx += 2;
            }
        }

        private static Vector3 ComputeLineDir(BombingSegment seg)
        {
            var cells = seg.bombCells;
            var first = cells[0];
            var last = cells[cells.Count - 1];
            var dx = last.x - first.x;
            var dz = last.z - first.z;
            if (dx == 0 && dz == 0)
            {
                var f = seg.flightDir.FacingCell;
                var fv = new Vector3(f.x, 0f, f.z);
                return fv.sqrMagnitude > 0.0001f ? fv.normalized : Vector3.right;
            }
            return new Vector3(dx, 0f, dz).normalized;
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

        private bool VehicleNegatesWestResidual()
        {
            if (vehicle?.VehicleDef?.graphicData?.Graphic is not Graphic_Rgb g) return false;
            return g.WestFlipped && !g.EastRotated;
        }

        private static void SnapRotationToHeading(float screenCwDeg, bool negateWestResidual,
            out Rot4 rot, out float residualDeg)
        {
            var d = screenCwDeg;
            while (d > 180f) d -= 360f;
            while (d <= -180f) d += 360f;
            if (d >= -45f && d < 45f) { rot = Rot4.East;  residualDeg = d; }
            else if (d >= 45f && d < 135f) { rot = Rot4.South; residualDeg = d - 90f; }
            else if (d >= -135f && d < -45f) { rot = Rot4.North; residualDeg = d + 90f; }
            else
            {
                rot = Rot4.West;
                var diff = d - 180f;
                while (diff > 180f) diff -= 360f;
                while (diff <= -180f) diff += 360f;
                residualDeg = negateWestResidual ? -diff : diff;
            }
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
            // Run-width perp randomization is per-round (per fire cell), not per bullet:
            // the rectangle's cells were generated axis-only so this is what gives the
            // cell its perpendicular scatter across the width.
            var roundCell = ApplyRunWidthPerp(cell);
            var bullets = Math.Max(1, strafingBulletsPerRound);
            for (var i = 0; i < bullets; i++)
            {
                var spreadCell = ApplySpread(roundCell);
                var intendedTarget = PickIntendedTarget(roundCell, spreadCell);
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
            var flight = ComputeFlightTangent();
            var dx = Math.Sign(flight.x);
            var dz = Math.Sign(flight.z);
            if (dx == 0 && dz == 0) dx = 1;
            var offset = Math.Max(1, strafingFireOriginOffset);
            return new IntVec3(target.x - dx * offset, target.y, target.z - dz * offset);
        }

        // Spline mode: local tangent at the plane's current arc length. Legacy mode:
        // start->end vector. Used by ComputeFireOrigin and ApplyRunWidthPerp so that in
        // chained spline strafing, bullet origin and width-spread orient to the current
        // segment's heading rather than the overall polyline's start->end.
        private Vector3 ComputeFlightTangent()
        {
            if (waypoints != null && _splinePositions != null && _splinePositions.Count >= 2)
            {
                SampleSplineAtDistance(Mathf.Clamp(traveled, 0f, _totalLength),
                    out _, out var tan);
                if (tan.sqrMagnitude > 0.0001f) return tan.normalized;
            }
            var dx = endCell.x - startCell.x;
            var dz = endCell.z - startCell.z;
            if (dx == 0 && dz == 0) return new Vector3(1f, 0f, 0f);
            return new Vector3(dx, 0f, dz).normalized;
        }

        private IntVec3 ApplyRunWidthPerp(IntVec3 cell)
        {
            if (strafingRunWidth <= 1) return cell;
            var flight = ComputeFlightTangent();
            // 90deg CCW rotation in (x,z) plane.
            var perpX = -flight.z;
            var perpZ = flight.x;
            var halfW_low = strafingRunWidth / 2;
            var halfW_high = (strafingRunWidth - 1) / 2;
            var t = Rand.RangeInclusive(-halfW_low, halfW_high);
            var dx = Mathf.RoundToInt(perpX * t);
            var dz = Mathf.RoundToInt(perpZ * t);
            var offset = new IntVec3(cell.x + dx, cell.y, cell.z + dz);
            return offset.InBounds(Map) ? offset : cell;
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
            ThingDef arrivingDef;
            Rot4 landingRot;
            if (inPlaceMode)
            {
                arrivingDef = AirstrikeDefOf.ROCKET_HoverLanding;
                landingRot = returnRot;
            }
            else
            {
                arrivingDef = vehicle.CompVehicleLauncher.Props.skyfallerIncoming;
                landingRot = vehicle.CompVehicleLauncher.launchProtocol.LandingProperties?.forcedRotation
                              ?? returnRot;
            }
            if (arrivingDef == null)
            {
                Log.Warning("[Rockets.Airstrike] Vehicle has no incoming skyfaller def; landing vehicle directly.");
                GenSpawn.Spawn(vehicle, returnCell, map, returnRot);
                Destroy();
                return;
            }
            var arriving = (VehicleSkyfaller_Arriving)
                VehicleSkyfallerMaker.MakeSkyfaller(arrivingDef, vehicle);
            arriving.rotatePostLanding = returnRot;
            if (arriving is VehicleSkyfaller_HoverLanding hover)
            {
                hover.visualAltitude = visualAltitude;
                hover.descentTicks = hoverLandingTicks;
            }
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
            Scribe_Values.Look(ref strafingRunWidth, nameof(strafingRunWidth), 1);
            Scribe_Values.Look(ref sortieSpeedMultiplier, nameof(sortieSpeedMultiplier), 1f);
            Scribe_Defs.Look(ref bombFireSound, nameof(bombFireSound));
            Scribe_References.Look(ref chosenPilot, nameof(chosenPilot));
            Scribe_Defs.Look(ref xpSkill, nameof(xpSkill));
            Scribe_Values.Look(ref strafingXpGranted, nameof(strafingXpGranted));
            Scribe_Values.Look(ref progress, nameof(progress));
            Scribe_Collections.Look(ref waypoints, nameof(waypoints), LookMode.Value);
            Scribe_Collections.Look(ref waypointIsDrop, nameof(waypointIsDrop), LookMode.Value);
            Scribe_Values.Look(ref tangentScale, nameof(tangentScale), 1f);
            Scribe_Values.Look(ref cornerLookaheadCells, nameof(cornerLookaheadCells), 6f);
            Scribe_Values.Look(ref cornerMinSpeedFactor, nameof(cornerMinSpeedFactor), 0.55f);
            Scribe_Values.Look(ref traveled, nameof(traveled));
            Scribe_Collections.Look(ref bombFired, nameof(bombFired), LookMode.Value);
            Scribe_Values.Look(ref inPlaceMode, nameof(inPlaceMode));
            Scribe_Values.Look(ref hoverLandingTicks, nameof(hoverLandingTicks), 90);
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
