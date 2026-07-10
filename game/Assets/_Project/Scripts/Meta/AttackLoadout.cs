using System.Collections.Generic;
using Shitboxer.Race;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Flattens a set of equipped parts into the single AttackProfile a car races with. Pure and
    /// engine-free (like ShopLogic/PayoutTable) so it unit-tests without a scene. Saps stack
    /// additively across parts (two ram-type parts bite twice as hard); the aura takes the widest
    /// radius equipped. Non-attack parts and nulls are ignored.
    /// </summary>
    public static class AttackLoadout
    {
        public static AttackProfile Build(IEnumerable<PartDef> equippedParts)
        {
            AttackProfile profile = AttackProfile.None;
            if (equippedParts == null) return profile;

            foreach (PartDef part in equippedParts)
            {
                if (!part || part.Category != PartCategory.Attack) continue;
                profile.ContactGripSap += part.ContactGripSap;
                profile.ContactPowerSap += part.ContactPowerSap;
                profile.AuraGripSap += part.AuraGripSap;
                if (part.AuraRadiusM > profile.AuraRadiusM) profile.AuraRadiusM = part.AuraRadiusM;
            }
            return profile;
        }
    }
}
