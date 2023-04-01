using BepInEx;
using RWCustom;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using UnityEngine;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace ScavBarrel;

using static AbstractPhysicalObject;
using static CreatureTemplate.Type;
using static MoreSlugcats.MoreSlugcatsEnums.CreatureTemplateType;

[BepInPlugin("com.dual.barrel-of-scavs", "Barrel of Scavs", "1.0.1")]
sealed class Plugin : BaseUnityPlugin
{
    sealed class ObjData { public WorldCoordinate startPos; }

    static readonly ConditionalWeakTable<AbstractPhysicalObject, ObjData> objData = new();

    static ObjData Data(AbstractPhysicalObject d) => objData.GetValue(d, _ => new());

    static bool CanDragIntoShortcut(AbstractCreature self, AbstractCreature other)
    {
        if (self.state.dead || self.realizedCreature is Creature c && !c.Consious) {
            return false;
        }

        var ty = self.creatureTemplate.TopAncestor().type;
        if (ty == Fly || ty == Centipede || ty == EggBug || ty == Leech) {
            return false;
        }
        if (ty == Slugcat || ty == PoleMimic || ty == TentaclePlant || ty == MirosBird || ty == Vulture || (ty == SlugNPC && SlugNPC != null)) {
            return true;
        }
        return ty == other.creatureTemplate.TopAncestor().type || self.creatureTemplate.CreatureRelationship(other.creatureTemplate).type == CreatureTemplate.Relationship.Type.Eats;
    }

    static void DropEverything(AbstractCreature crit)
    {
        List<AbstractPhysicalObject> release = new();

        for (int i = crit.stuckObjects.Count - 1; i >= 0; i--) {
            AbstractObjectStick stick = crit.stuckObjects[i];

            if (stick is CreatureGripStick cgs && cgs.A == crit && cgs.B != crit) {
                cgs.Deactivate();
                release.Add(cgs.B);
                if (crit.realizedObject is Creature cr && cr.grasps?[cgs.grasp] != null) {
                    cr.ReleaseGrasp(cgs.grasp);
                }
            }
            else if (stick is not CreatureGripStick) {
                stick.Deactivate();
                if (stick.A != crit)
                    release.Add(stick.A);
                if (stick.B != crit)
                    release.Add(stick.B);
            }
        }

        foreach (var item in release) {
            ResetPos(item);
        }
    }

    static void ResetPos(AbstractPhysicalObject spit)
    {
        foreach (var item in spit.GetAllConnectedObjects()) {
            if (item.realizedObject is not PhysicalObject o) continue;

            WorldCoordinate startPos = Data(spit).startPos;
            Room newRoom = spit.world.GetAbstractRoom(startPos).realizedRoom;
            if (newRoom == null) {
                continue;
            }

            startPos.y = Mathf.Clamp(startPos.y, -1, newRoom.TileHeight);

            spit.pos = startPos;

            Room oldRoom = o.room;
            if (!newRoom.updateList.Contains(o)) {
                newRoom.AddObject(o);
            }
            if (o is Creature c) {
                c.SpitOutOfShortCut(startPos.Tile, newRoom, false);
            }
            if (newRoom != oldRoom) {
                o.NewRoom(newRoom);
            }

            if (o is Weapon w) {
                w.ChangeMode(Weapon.Mode.Free);
            }

            foreach (BodyChunk chunk in o.bodyChunks) {
                chunk.HardSetPosition(newRoom.MiddleOfTile(startPos.Tile) + Custom.RNV());
                chunk.vel = new(0, 1);
            }
        }
    }

    public void OnEnable()
    {
        // TODO test miros

        On.PhysicalObject.Update += PhysicalObject_Update;
        On.Creature.SuckedIntoShortCut += PreventDragging;
        On.AbstractPhysicalObject.IsEnteringDen += DropInDen;
        On.Creature.FlyAwayFromRoom += DropInSky;
    }

    private void PhysicalObject_Update(On.PhysicalObject.orig_Update orig, PhysicalObject self, bool eu)
    {
        if (self is not Creature c || !c.inShortcut) {
            Data(self.abstractPhysicalObject).startPos = self.abstractPhysicalObject.pos;
        }

        orig(self, eu);
    }

    private void PreventDragging(On.Creature.orig_SuckedIntoShortCut orig, Creature self, IntVector2 entrancePos, bool carriedByOther)
    {
        if (self.room?.GetTile(entrancePos)?.Terrain == Room.Tile.TerrainType.ShortcutEntrance) {
            AbstractCreature grabber = self.abstractCreature;

            foreach (var stick in grabber.stuckObjects.ToList().OfType<CreatureGripStick>()) {
                if (stick.A == grabber && stick.B is AbstractCreature grabbed && !CanDragIntoShortcut(grabber, grabbed)) {
                    stick.Deactivate();
                    if (grabber.realizedObject is Creature cr && cr.grasps?[stick.grasp] != null) {
                        cr.ReleaseGrasp(stick.grasp);
                    }
                    Logger.LogDebug($"Preventing {grabber} dragging {grabbed} into shortuct");
                }
            }
        }

        orig(self, entrancePos, carriedByOther);
    }

    private void DropInDen(On.AbstractPhysicalObject.orig_IsEnteringDen orig, AbstractPhysicalObject self, WorldCoordinate den)
    {
        if (self is not AbstractCreature predator) {
            return;
        }

        foreach (var stick in predator.stuckObjects) {
            if (stick is CreatureGripStick cgs && cgs.A == predator && cgs.B is AbstractCreature grabbed && grabbed.stuckObjects.Count > 1) {
                Logger.LogDebug($"Made {grabbed} drop everything while dragged into den");
                DropEverything(grabbed);
            }
        }

        orig(self, den);
    }

    private void DropInSky(On.Creature.orig_FlyAwayFromRoom orig, Creature self, bool carriedByOther)
    {
        if (!carriedByOther && self is Vulture) {
            foreach (var item in self.grasps) {
                if (item?.grabbed is Creature c) {
                    Logger.LogDebug($"Made {c.abstractCreature} drop everything while dragged offscreen");
                    DropEverything(c.abstractCreature);
                }
            }
        }

        orig(self, carriedByOther);
    }
}
