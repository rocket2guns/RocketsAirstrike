using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace AirstrikeMod
{
    public class StrafingRunTargeter : BaseTargeter
    {
        public static StrafingRunTargeter Instance { get; private set; }

        private static float middleMouseDownTime;

        private Map map;
        private int runWidth;
        private int runLength;
        private int maxChain = 1;
        private Action<List<BombingSegment>> action;
        private Func<LocalTargetInfo, bool> targetValidator;
        private Rot4 rotation = Rot4.East;

        private readonly List<BombingSegment> lockedSegments = new();
        private readonly List<IntVec3> footprintBuffer = new(256);

        public override bool IsTargeting => action != null;

        public override void PostInit() => Instance = this;

        public void BeginTargeting(VehiclePawn vehicle, Map map, int runWidth, int runLength,
            int maxChain,
            Action<List<BombingSegment>> action,
            Func<LocalTargetInfo, bool> targetValidator = null,
            Action actionWhenFinished = null,
            Texture2D mouseAttachment = null)
        {
            this.vehicle = vehicle;
            this.map = map;
            this.runWidth = Mathf.Max(1, runWidth);
            this.runLength = Mathf.Max(1, runLength);
            this.maxChain = Mathf.Max(1, maxChain);
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
            targetValidator = null;
            lockedSegments.Clear();
        }

        public override void ProcessInputEvents()
        {
            HandleRotationShortcuts();
            if (Event.current.type is EventType.MouseDown && Event.current.button == 0)
            {
                var cursorCell = UI.MouseCell();
                if (action != null && cursorCell.InBounds(map) && IsValidPlacement(cursorCell))
                {
                    AirstrikeDefOf.ROCKET_InterfaceBeep1.PlayOneShotOnCamera(null);
                    var fireCells = ComputeFireCells(cursorCell, rotation, runLength);
                    var dir = rotation;
                    var roomForMore = lockedSegments.Count + 1 < maxChain;
                    if (Event.current.shift && roomForMore)
                    {
                        lockedSegments.Add(new BombingSegment(fireCells, dir));
                    }
                    else
                    {
                        lockedSegments.Add(new BombingSegment(fireCells, dir));
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

            if (Event.current.type is EventType.MouseDown && Event.current.button == 1)
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

            if (Event.current.button == 2)
            {
                if (Event.current.type is EventType.MouseDown)
                {
                    Event.current.Use();
                    middleMouseDownTime = Time.realtimeSinceStartup;
                }
                if (Event.current.type is EventType.MouseUp &&
                    Time.realtimeSinceStartup - middleMouseDownTime < 0.15f) 
                    rotationDirection = RotationDirection.Clockwise;
            }
            if (KeyBindingDefOf.Designator_RotateRight.KeyDownEvent)
                rotationDirection = RotationDirection.Clockwise;
            if (KeyBindingDefOf.Designator_RotateLeft.KeyDownEvent)
                rotationDirection = RotationDirection.Counterclockwise;

            if (rotationDirection is not RotationDirection.None)
            {
                SoundDefOf.DragSlider.PlayOneShotOnCamera(null);
                rotation.Rotate(rotationDirection);
            }
        }

        public override void TargeterOnGUI()
        {
            CursorLabel.Icon = mouseAttachment;
            CursorLabel.FootprintHalfExtent = ComputeFootprintHalfExtent();
            CursorLabel.FourthLine = (lockedSegments.Count > 0
                ? "ROCKET_RightClickBegin"
                : "ROCKET_RightClickCancel").Translate();
            CursorLabel.Draw();
        }

        private Vector2 ComputeFootprintHalfExtent()
        {
            var halfL = runLength * 0.5f;
            var halfW = runWidth * 0.5f;
            var longAxisIsX = rotation == Rot4.East || rotation == Rot4.West;
            return longAxisIsX
                ? new Vector2(halfL, halfW)
                : new Vector2(halfW, halfL);
        }

        public override void TargeterUpdate()
        {
            for (var i = 0; i < lockedSegments.Count; i++)
            {
                var seg = lockedSegments[i];
                if (seg.bombCells == null || seg.bombCells.Count == 0) continue;
                footprintBuffer.Clear();
                FillFootprint(seg.bombCells[seg.bombCells.Count / 2], seg.flightDir,
                    runWidth, runLength, footprintBuffer);
                GenDraw.DrawFieldEdges(footprintBuffer, LockedColor);
            }
            DrawChainLines();

            var cursorCell = UI.MouseCell();
            if (!cursorCell.InBounds(map))
                return;

            footprintBuffer.Clear();
            FillFootprint(cursorCell, rotation, runWidth, runLength, footprintBuffer);
            var atCapacity = lockedSegments.Count + 1 >= maxChain;
            var color = !IsValidPlacement(cursorCell)
                ? new Color(1f, 0.3f, 0.2f)
                : (Event.current.shift && !atCapacity ? ChainHintColor : Color.white);
            GenDraw.DrawFieldEdges(footprintBuffer, color);

            DrawApproachGhost(cursorCell, color);
        }

        private const float APPROACH_PADDING_CELLS = 2f;
        private const int PING_PONG_TICKS = 100;
        private static float ghostFramesOpen;

        private IntVec3 ComputeApproachCell(IntVec3 cursor)
        {
            var halfFootprintLen = runLength * 0.5f;
            var planeHalf = (vehicle?.VehicleGraphic?.data?.drawSize.y ?? 3f) * 0.5f;
            var offsetCells = Mathf.RoundToInt(halfFootprintLen + planeHalf + APPROACH_PADDING_CELLS);

            var dirVec = rotation.FacingCell;
            return new IntVec3(
                cursor.x - dirVec.x * offsetCells, 0,
                cursor.z - dirVec.z * offsetCells);
        }

        private void DrawApproachGhost(IntVec3 cursor, Color tint)
        {
            var buildDef = vehicle?.VehicleDef?.buildDef;
            if (buildDef?.graphic == null) return;

            ghostFramesOpen++;
            tint.a = Mathf.PingPong(ghostFramesOpen, PING_PONG_TICKS / 1.5f)
                     / PING_PONG_TICKS + 0.25f;

            GhostDrawer.DrawGhostThing(ComputeApproachCell(cursor), rotation,
                buildDef, buildDef.graphic, tint, AltitudeLayer.Blueprint);
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

        private static List<IntVec3> ComputeFireCells(IntVec3 cursor, Rot4 dir, int runLength)
        {
            var halfLLow = runLength / 2;
            var result = new List<IntVec3>(runLength);
            for (var i = 0; i < runLength; i++)
            {
                var along = i - halfLLow;
                result.Add(ApplyOffset(cursor, dir, along, 0));
            }
            return result;
        }

        private static IntVec3 ApplyOffset(IntVec3 cursor, Rot4 dir, int along, int perp)
        {
            if (dir == Rot4.North) return new IntVec3(cursor.x + perp, 0, cursor.z + along);
            if (dir == Rot4.South) return new IntVec3(cursor.x - perp, 0, cursor.z - along);
            if (dir == Rot4.West)  return new IntVec3(cursor.x - along, 0, cursor.z - perp);
            return new IntVec3(cursor.x + along, 0, cursor.z + perp);
        }

        private static void GetBoxBounds(IntVec3 cursor, Rot4 dir, int runWidth, int runLength,
            out int xMin, out int xMax, out int zMin, out int zMax)
        {
            var halfLLow = runLength / 2;
            var halfLHigh = (runLength - 1) / 2;
            var halfWLow = runWidth / 2;
            var halfWHigh = (runWidth - 1) / 2;

            var longAxisIsX = dir == Rot4.East || dir == Rot4.West;
            if (longAxisIsX)
            {
                xMin = cursor.x - halfLLow;
                xMax = cursor.x + halfLHigh;
                zMin = cursor.z - halfWLow;
                zMax = cursor.z + halfWHigh;
            }
            else
            {
                xMin = cursor.x - halfWLow;
                xMax = cursor.x + halfWHigh;
                zMin = cursor.z - halfLLow;
                zMax = cursor.z + halfLHigh;
            }
        }

        private static void FillFootprint(IntVec3 cursor, Rot4 dir, int runWidth, int runLength,
            List<IntVec3> buffer)
        {
            GetBoxBounds(cursor, dir, runWidth, runLength,
                out var xMin, out var xMax, out var zMin, out var zMax);
            for (var x = xMin; x <= xMax; x++)
                for (var z = zMin; z <= zMax; z++)
                    buffer.Add(new IntVec3(x, 0, z));
        }

        private bool IsValidPlacement(IntVec3 cursorCell)
        {
            if (targetValidator != null && !targetValidator(cursorCell))
                return false;
            GetBoxBounds(cursorCell, rotation, runWidth, runLength,
                out var xMin, out var xMax, out var zMin, out var zMax);
            return xMin >= 0 && zMin >= 0 && xMax < map.Size.x && zMax < map.Size.z;
        }
    }
}
