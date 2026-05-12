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
        private Action<List<IntVec3>, Rot4> action;
        private Func<LocalTargetInfo, bool> targetValidator;
        private Rot4 rotation = Rot4.East;

        private readonly List<IntVec3> footprintBuffer = new(256);

        public override bool IsTargeting => action != null;

        public override void PostInit() => Instance = this;

        public void BeginTargeting(VehiclePawn vehicle, Map map, int runWidth, int runLength,
            Action<List<IntVec3>, Rot4> action,
            Func<LocalTargetInfo, bool> targetValidator = null,
            Action actionWhenFinished = null,
            Texture2D mouseAttachment = null)
        {
            this.vehicle = vehicle;
            this.map = map;
            this.runWidth = Mathf.Max(1, runWidth);
            this.runLength = Mathf.Max(1, runLength);
            this.action = action;
            this.targetValidator = targetValidator;
            this.actionWhenFinished = actionWhenFinished;
            this.mouseAttachment = mouseAttachment;
            this.rotation = Rot4.East;

            OnStart();
        }

        public override void StopTargeting()
        {
            actionWhenFinished?.Invoke();
            actionWhenFinished = null;
            map = null;
            action = null;
            targetValidator = null;
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
                    var fireCells = ComputeFireCells(cursorCell, rotation, runWidth, runLength);
                    var callback = action;
                    var rot = rotation;
                    StopTargeting();
                    callback(fireCells, rot);
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera(null);
                }
                Event.current.Use();
            }

            if ((Event.current.type == EventType.MouseDown && Event.current.button == 1) ||
                KeyBindingDefOf.Cancel.KeyDownEvent)
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
            CursorLabel.Draw();
        }

        public override void TargeterUpdate()
        {
            var cursorCell = UI.MouseCell();
            if (!cursorCell.InBounds(map))
                return;

            footprintBuffer.Clear();
            FillFootprint(cursorCell, rotation, runWidth, runLength, footprintBuffer);
            var color = IsValidPlacement(cursorCell)
                ? Color.white
                : new Color(1f, 0.3f, 0.2f);
            GenDraw.DrawFieldEdges(footprintBuffer, color);
        }

        private static List<IntVec3> ComputeFireCells(IntVec3 cursor, Rot4 dir,
            int runWidth, int runLength)
        {
            var halfL_low = runLength / 2;
            var halfW_low = runWidth / 2;
            var halfW_high = (runWidth - 1) / 2;
            var result = new List<IntVec3>(runLength);
            for (var i = 0; i < runLength; i++)
            {
                var along = i - halfL_low;
                var perp = Rand.RangeInclusive(-halfW_low, halfW_high);
                result.Add(ApplyOffset(cursor, dir, along, perp));
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
            var halfL_low = runLength / 2;
            var halfL_high = (runLength - 1) / 2;
            var halfW_low = runWidth / 2;
            var halfW_high = (runWidth - 1) / 2;

            var longAxisIsX = dir == Rot4.East || dir == Rot4.West;
            if (longAxisIsX)
            {
                xMin = cursor.x - halfL_low;
                xMax = cursor.x + halfL_high;
                zMin = cursor.z - halfW_low;
                zMax = cursor.z + halfW_high;
            }
            else
            {
                xMin = cursor.x - halfW_low;
                xMax = cursor.x + halfW_high;
                zMin = cursor.z - halfL_low;
                zMax = cursor.z + halfL_high;
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
