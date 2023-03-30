using BepInEx;
using System.Linq;
using System.Security.Permissions;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace ScavBarrel;

using static CreatureTemplate.Type;

[BepInPlugin("com.dual.barrel-of-scavs", "Barrel of Scavs", "1.0.0")]
sealed class Plugin : BaseUnityPlugin
{
    static bool CanDragIntoDen(AbstractCreature self, AbstractCreature other)
    {
        var ty = self.creatureTemplate.TopAncestor().type;
        if (ty == Fly || ty == Centipede || ty == EggBug || ty == Leech) {
            return false;
        }
        if (ty == PoleMimic || ty == TentaclePlant || ty == MirosBird || ty == Vulture) {
            return true;
        }
        return self.creatureTemplate.CreatureRelationship(other.creatureTemplate).type == CreatureTemplate.Relationship.Type.Eats;
    }

    public void OnEnable()
    {
        On.AbstractWorldEntity.IsEnteringDen += AbstractWorldEntity_IsEnteringDen;
        On.Creature.FlyAwayFromRoom += Creature_FlyAwayFromRoom;
    }

    private void AbstractWorldEntity_IsEnteringDen(On.AbstractWorldEntity.orig_IsEnteringDen orig, AbstractWorldEntity self, WorldCoordinate den)
    {
        orig(self, den);

        if (self is AbstractPhysicalObject apo && apo.stuckObjects.Any(s => s is AbstractPhysicalObject.CreatureGripStick c && c.B == apo)) {
            if (apo.realizedObject is Creature c) {
                c.LoseAllGrasps();
            }
            apo.LoseAllStuckObjects();
        }
        else if (self is AbstractCreature crit) {
            foreach (var stick in crit.stuckObjects.ToList().OfType<AbstractPhysicalObject.CreatureGripStick>()) {
                if (stick.B is AbstractCreature prey && !CanDragIntoDen(crit, prey)) {
                    stick.Deactivate();
                }
            }
        }
    }

    private void Creature_FlyAwayFromRoom(On.Creature.orig_FlyAwayFromRoom orig, Creature self, bool carriedByOther)
    {
        if (carriedByOther) {
            self.LoseAllGrasps();
            self.abstractCreature?.LoseAllStuckObjects();
        }
        else {
            orig(self, carriedByOther);
        }
    }
}
