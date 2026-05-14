using System;
using System.Collections.Generic;
using RimWorld;
using SmashTools;
using UnityEngine;
using Vehicles;
using Verse;
using Verse.Sound;

namespace AirstrikeMod
{
    // Single-cell targeter for Single Strike
    public class SingleStrikeTargeter : BaseTargeter
    {
        public static SingleStrikeTargeter Instance { get; private set; }

        private Map map;
        private ThingDef ordinance;
        private float radius = 3f;
        private int maxChain = 1;
        private Action<List<BombingSegment>> action;
        private Func<LocalTargetInfo, bool> targetValidator;

        private readonly List<IntVec3> lockedTargets = new();

        public override bool IsTargeting => action != null;

        public override void PostInit() => Instance = this;

        public void BeginTargeting(VehiclePawn vehicle, Map map, ThingDef ordinance,
            int maxChain,
            Action<List<BombingSegment>> action,
            Func<LocalTargetInfo, bool> targetValidator = null,
            Action actionWhenFinished = null,
            Texture2D mouseAttachment = null)
        {
            this.vehicle = vehicle;
            this.map = map;
            this.ordinance = ordinance;
            this.radius = ordinance?.projectileWhenLoaded?.projectile?.explosionRadius ?? 3f;
            this.maxChain = Mathf.Max(1, maxChain);
            this.action = action;
            this.targetValidator = targetValidator;
            this.actionWhenFinished = actionWhenFinished;
            this.mouseAttachment = mouseAttachment;
            this.lockedTargets.Clear();

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
            lockedTargets.Clear();
        }

        public override void ProcessInputEvents()
        {
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                var cursorCell = UI.MouseCell();
                if (action != null && cursorCell.InBounds(map) && IsValidPlacement(cursorCell))
                {
                    AirstrikeDefOf.ROCKET_InterfaceBeep1.PlayOneShotOnCamera(null);
                    var roomForMore = lockedTargets.Count + 1 < maxChain;
                    if (Event.current.shift && roomForMore)
                    {
                        lockedTargets.Add(cursorCell);
                    }
                    else
                    {
                        lockedTargets.Add(cursorCell);
                        var chain = BuildChain();
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

            // Right-click commits the locked chain if any targets have been placed,
            // otherwise cancels the targeter (matches ESC). Locked targets are never
            // discarded by right-click.
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                if (lockedTargets.Count > 0)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    var chain = BuildChain();
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

        private List<BombingSegment> BuildChain()
        {
            var dir = vehicle?.Rotation.Opposite ?? Rot4.East;
            var segments = new List<BombingSegment>(lockedTargets.Count);
            for (var i = 0; i < lockedTargets.Count; i++)
                segments.Add(new BombingSegment(
                    new List<IntVec3> { lockedTargets[i] }, dir));
            return segments;
        }

        public override void TargeterOnGUI()
        {
            CursorLabel.Icon = mouseAttachment;
            CursorLabel.FootprintHalfExtent = new Vector2(radius, radius);
            CursorLabel.FourthLine = (lockedTargets.Count > 0
                ? "ROCKET_RightClickBegin"
                : "ROCKET_RightClickCancel").Translate();
            CursorLabel.Draw();
        }

        public override void TargeterUpdate()
        {
            for (var i = 0; i < lockedTargets.Count; i++) 
                GenDraw.DrawRadiusRing(lockedTargets[i], radius);
            DrawChainLines();
            var cursorCell = UI.MouseCell();
            if (!cursorCell.InBounds(map)) return;
            GenDraw.DrawRadiusRing(cursorCell, radius);
        }

        private static readonly Color ChainLineColor = new(1f, 1f, 1f, 0.6f);

        private void DrawChainLines()
        {
            if (lockedTargets.Count == 0) return;
            var prev = lockedTargets[0].ToVector3Shifted();
            for (var i = 1; i < lockedTargets.Count; i++)
            {
                var next = lockedTargets[i].ToVector3Shifted();
                GenDraw.DrawLineBetween(prev, next, SimpleColor.White, 0.2f);
                prev = next;
            }
            var cursor = UI.MouseCell();
            if (cursor.InBounds(map))
                GenDraw.DrawLineBetween(prev, cursor.ToVector3Shifted(), SimpleColor.White, 0.2f);
        }

        private bool IsValidPlacement(IntVec3 cursorCell)
        {
            if (targetValidator != null && !targetValidator(cursorCell))
                return false;
            return cursorCell.InBounds(map);
        }
    }
}
