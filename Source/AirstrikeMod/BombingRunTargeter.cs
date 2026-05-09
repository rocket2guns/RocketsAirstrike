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
        public const int DropCount = 5;

        public static BombingRunTargeter Instance { get; private set; }

        private static float middleMouseDownTime;

        private Map map;
        private OrdinanceDef ordinance;
        private Action<List<IntVec3>, Rot4> action;
        private Func<LocalTargetInfo, bool> targetValidator;
        private Rot4 rotation = Rot4.East;

        // Reused across frames; sized for the largest reasonable footprint.
        private readonly List<IntVec3> footprintBuffer = new(256);

        public override bool IsTargeting => action != null;

        public override void PostInit() => Instance = this;

        public void BeginTargeting(VehiclePawn vehicle, Map map, OrdinanceDef ordinance,
            Action<List<IntVec3>, Rot4> action,
            Func<LocalTargetInfo, bool> targetValidator = null,
            Action actionWhenFinished = null,
            Texture2D mouseAttachment = null)
        {
            this.vehicle = vehicle;
            this.map = map;
            this.ordinance = ordinance;
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
            ordinance = null;
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
                    var drops = ComputeDropCells(cursorCell, rotation, ordinance);
                    var callback = action;
                    var rot = rotation;
                    StopTargeting();
                    callback(drops, rot);
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
            CursorLabel.Draw();
        }

        public override void TargeterUpdate()
        {
            var cursorCell = UI.MouseCell();
            if (!cursorCell.InBounds(map) || ordinance == null)
                return;

            footprintBuffer.Clear();
            FillFootprint(cursorCell, rotation, ordinance, footprintBuffer);
            var color = IsValidPlacement(cursorCell)
                ? Color.white
                : new Color(1f, 0.3f, 0.2f);
            GenDraw.DrawFieldEdges(footprintBuffer, color);
        }

        private static int Spacing(OrdinanceDef ord) =>
            Mathf.Max(1, Mathf.RoundToInt(ord.radius * 2f));

        private static List<IntVec3> ComputeDropCells(IntVec3 cursor, Rot4 dir, OrdinanceDef ord)
        {
            var spacing = Spacing(ord);
            var half = (DropCount - 1) / 2;
            var cells = new List<IntVec3>(DropCount);

            for (var i = 0; i < DropCount; i++)
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

        private static void GetBoxBounds(IntVec3 cursor, Rot4 dir, OrdinanceDef ord,
            out int xMin, out int xMax, out int zMin, out int zMax)
        {
            var spacing = Spacing(ord);
            var width = spacing;
            var length = spacing * DropCount;
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

        private static void FillFootprint(IntVec3 cursor, Rot4 dir, OrdinanceDef ord,
            List<IntVec3> buffer)
        {
            GetBoxBounds(cursor, dir, ord, out var xMin, out var xMax, out var zMin, out var zMax);
            for (var x = xMin; x <= xMax; x++)
                for (var z = zMin; z <= zMax; z++)
                    buffer.Add(new IntVec3(x, 0, z));
        }

        private bool IsValidPlacement(IntVec3 cursorCell)
        {
            if (targetValidator != null && !targetValidator(cursorCell))
                return false;
            GetBoxBounds(cursorCell, rotation, ordinance,
                out var xMin, out var xMax, out var zMin, out var zMax);
            return xMin >= 0 && zMin >= 0 && xMax < map.Size.x && zMax < map.Size.z;
        }
    }
}
