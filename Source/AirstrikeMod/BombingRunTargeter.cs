using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace AirstrikeMod
{
    // Custom rectangular targeter for Bombing Run. VF auto-discovers BaseTargeter
    // subclasses via reflection in Vehicles.Targeters' static ctor; a parameterless
    // ctor and storing the singleton in PostInit is the entire registration.
    public class BombingRunTargeter : BaseTargeter
    {
        public static BombingRunTargeter Instance { get; private set; }

        private static float middleMouseDownTime;

        private Map map;
        private ThingDef ordinance;
        private int dropCount = 5;
        private int maxChain = 1;
        private float spacingMultiplier = CompProperties_AirstrikeBombingRun.DEFAULT_SPACING_MULTIPLIER;
        private Action<List<BombingSegment>> action;
        private Func<LocalTargetInfo, bool> targetValidator;
        private Rot4 rotation = Rot4.East;

        private readonly List<BombingSegment> lockedSegments = new();
        // Reused across frames; sized for the largest reasonable footprint.
        private readonly List<IntVec3> footprintBuffer = new(256);

        public override bool IsTargeting => action != null;

        public override void PostInit() => Instance = this;

        public void BeginTargeting(VehiclePawn vehicle, Map map, ThingDef ordinance,
            int dropCount,
            int maxChain,
            Action<List<BombingSegment>> action,
            Func<LocalTargetInfo, bool> targetValidator = null,
            Action actionWhenFinished = null,
            Texture2D mouseAttachment = null,
            float spacingMultiplier = CompProperties_AirstrikeBombingRun.DEFAULT_SPACING_MULTIPLIER)
        {
            this.vehicle = vehicle;
            this.map = map;
            this.ordinance = ordinance;
            this.dropCount = Mathf.Max(1, dropCount);
            this.maxChain = Mathf.Max(1, maxChain);
            this.spacingMultiplier = Mathf.Max(0f, spacingMultiplier);
            this.action = action;
            this.targetValidator = targetValidator;
            this.actionWhenFinished = actionWhenFinished;
            this.mouseAttachment = mouseAttachment;
            this.rotation = Rot4.East;
            this.lockedSegments.Clear();

            OnStart();
        }

        public override void StopTargeting()
        {
            actionWhenFinished?.Invoke();
            actionWhenFinished = null;
            map = null;
            action = null;
            ordinance = null;
            targetValidator = null;
            lockedSegments.Clear();
        }

        public override void ProcessInputEvents()
        {
            HandleRotationShortcuts();

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                var cursorCell = UI.MouseCell();
                if (action != null && cursorCell.InBounds(map) && IsValidPlacement(cursorCell))
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    var drops = ComputeDropCells(cursorCell, rotation, ordinance, dropCount);
                    var dir = rotation;
                    // total committed
                    var roomForMore = lockedSegments.Count + 1 < maxChain;
                    if (Event.current.shift && roomForMore)
                    {
                        lockedSegments.Add(new BombingSegment(drops, dir));
                    }
                    else
                    {
                        // final segment
                        lockedSegments.Add(new BombingSegment(drops, dir));
                        var chain = new List<BombingSegment>(lockedSegments);
                        var callback = action;
                        StopTargeting();
                        callback(chain);
                    }
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera(null);
                }
                Event.current.Use();
            }

            // Right-click commits the locked chain if any targets have been placed
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                if (lockedSegments.Count > 0)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    var chain = new List<BombingSegment>(lockedSegments);
                    var callback = action;
                    StopTargeting();
                    callback(chain);
                }
                else
                {
                    SoundDefOf.CancelMode.PlayOneShotOnCamera(null);
                    StopTargeting();
                }
                Event.current.Use();
            }

            if (KeyBindingDefOf.Cancel.KeyDownEvent)
            {
                SoundDefOf.CancelMode.PlayOneShotOnCamera(null);
                StopTargeting();
                Event.current.Use();
            }
        }

        private void HandleRotationShortcuts()
        {
            var rotationDirection = RotationDirection.None;

            // Middle-click tap-and-release rotates clockwise (matches LandingTargeter).
            if (Event.current.button == 2)
            {
                if (Event.current.type == EventType.MouseDown)
                {
                    Event.current.Use();
                    middleMouseDownTime = Time.realtimeSinceStartup;
                }
                if (Event.current.type == EventType.MouseUp &&
                    Time.realtimeSinceStartup - middleMouseDownTime < 0.15f)
                {
                    rotationDirection = RotationDirection.Clockwise;
                }
            }
            if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
                rotationDirection = RotationDirection.Clockwise;
            if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
                rotationDirection = RotationDirection.Counterclockwise;

            if (rotationDirection != RotationDirection.None)
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera(null);
                rotation.Rotate(rotationDirection);
            }
        }

        public override void TargeterOnGUI()
        {
            GenUI.DrawMouseAttachment(mouseAttachment);
            CursorLabel.FourthLine = (lockedSegments.Count > 0
                ? "ROCKET_RightClickBegin"
                : "ROCKET_RightClickCancel").Translate();
            CursorLabel.Draw();
        }

        public override void TargeterUpdate()
        {
            // locked targets: faded rectangles + connecting lines
            for (var i = 0; i < lockedSegments.Count; i++)
            {
                var seg = lockedSegments[i];
                if (seg.bombCells == null || seg.bombCells.Count == 0) continue;
                footprintBuffer.Clear();
                FillFootprint(seg.bombCells[seg.bombCells.Count / 2], seg.flightDir,
                    ordinance, dropCount, footprintBuffer);
                GenDraw.DrawFieldEdges(footprintBuffer, LockedColor);
            }
            DrawChainLines();

            // live cursor footprint.
            var cursorCell = UI.MouseCell();
            if (!cursorCell.InBounds(map) || ordinance == null)
                return;

            footprintBuffer.Clear();
            FillFootprint(cursorCell, rotation, ordinance, dropCount, footprintBuffer);
            var atCapacity = lockedSegments.Count + 1 >= maxChain;
            var color = !IsValidPlacement(cursorCell)
                ? new Color(1f, 0.3f, 0.2f)
                : (Event.current.shift && !atCapacity ? ChainHintColor : Color.white);
            GenDraw.DrawFieldEdges(footprintBuffer, color);
        }

        private static readonly Color LockedColor = new(1f, 1f, 1f, 0.45f);
        private static readonly Color ChainHintColor = new(0.6f, 1f, 0.6f);
        private static readonly Color ChainLineColor = new(1f, 1f, 1f, 0.6f);

        private void DrawChainLines()
        {
            if (lockedSegments.Count == 0) return;
            var prev = SegmentAnchor(lockedSegments[0]);
            for (var i = 1; i < lockedSegments.Count; i++)
            {
                var next = SegmentAnchor(lockedSegments[i]);
                DrawLine(prev, next, ChainLineColor);
                prev = next;
            }
            // Last locked → live cursor.
            DrawLine(prev, UI.MouseCell().ToVector3Shifted(), ChainLineColor);
        }

        private static Vector3 SegmentAnchor(BombingSegment seg)
        {
            if (seg.bombCells == null || seg.bombCells.Count == 0) return Vector3.zero;
            return seg.bombCells[seg.bombCells.Count / 2].ToVector3Shifted();
        }

        private static void DrawLine(Vector3 a, Vector3 b, Color color)
        {
            a.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            b.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            GenDraw.DrawLineBetween(a, b, SimpleColor.White, 0.2f);
        }

        private int Spacing(ThingDef ord)
        {
            var radius = ord?.projectileWhenLoaded?.projectile?.explosionRadius ?? 0f;
            return Mathf.Max(1, Mathf.FloorToInt(radius * spacingMultiplier));
        }

        private List<IntVec3> ComputeDropCells(IntVec3 cursor, Rot4 dir, ThingDef ord,
            int dropCount)
        {
            var spacing = Spacing(ord);
            var half = (dropCount - 1) / 2;
            var cells = new List<IntVec3>(dropCount);

            for (var i = 0; i < dropCount; i++)
            {
                var offset = (i - half) * spacing;
                if (dir == Rot4.North)
                    cells.Add(new IntVec3(cursor.x, 0, cursor.z + offset));
                else if (dir == Rot4.South)
                    cells.Add(new IntVec3(cursor.x, 0, cursor.z - offset));
                else if (dir == Rot4.West)
                    cells.Add(new IntVec3(cursor.x - offset, 0, cursor.z));
                else
                    cells.Add(new IntVec3(cursor.x + offset, 0, cursor.z));
            }
            return cells;
        }

        private void GetBoxBounds(IntVec3 cursor, Rot4 dir, ThingDef ord,
            int dropCount, out int xMin, out int xMax, out int zMin, out int zMax)
        {
            var spacing = Spacing(ord);
            var width = spacing;
            var length = spacing * dropCount;
            var halfW = width / 2;
            var halfL = length / 2;

            var longAxisIsX = dir == Rot4.East || dir == Rot4.West;
            if (longAxisIsX)
            {
                xMin = cursor.x - halfL;
                xMax = cursor.x + halfL - 1;
                zMin = cursor.z - halfW;
                zMax = cursor.z + halfW - 1;
            }
            else
            {
                xMin = cursor.x - halfW;
                xMax = cursor.x + halfW - 1;
                zMin = cursor.z - halfL;
                zMax = cursor.z + halfL - 1;
            }
        }

        private void FillFootprint(IntVec3 cursor, Rot4 dir, ThingDef ord,
            int dropCount, List<IntVec3> buffer)
        {
            GetBoxBounds(cursor, dir, ord, dropCount,
                out var xMin, out var xMax, out var zMin, out var zMax);
            for (var x = xMin; x <= xMax; x++)
                for (var z = zMin; z <= zMax; z++)
                    buffer.Add(new IntVec3(x, 0, z));
        }

        private bool IsValidPlacement(IntVec3 cursorCell)
        {
            if (targetValidator != null && !targetValidator(cursorCell))
                return false;
            GetBoxBounds(cursorCell, rotation, ordinance, dropCount,
                out var xMin, out var xMax, out var zMin, out var zMax);
            return xMin >= 0 && zMin >= 0 && xMax < map.Size.x && zMax < map.Size.z;
        }
    }
}
