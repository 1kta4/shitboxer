using System;
using UnityEngine;

namespace Shitboxer.Race
{
    /// <summary>
    /// The resolved attack loadout a car carries into a race — the sum of its equipped Attack
    /// parts (doc 03), flattened to plain numbers so VehicleCombat can resolve it without
    /// touching the Meta assembly. RunDirector builds one from the player's equipped parts and
    /// pushes it onto the player car; bots carry <see cref="None"/> until AI attack tuning lands.
    ///
    /// Two families, both from doc 03:
    ///   • On-contact  — a qualifying car-to-car hit saps the OTHER car's grip/power. Lingers a
    ///     beat (ContactRecoverPerS) so a good shunt is felt.
    ///   • Proximity aura — every step, rivals within radius and behind you lose grip; it clears
    ///     fast once they escape (AuraRecoverPerS), so the aura is about holding station, not a
    ///     lasting debuff.
    /// </summary>
    [Serializable]
    public struct AttackProfile
    {
        [Header("On-contact (Ram Bars, Spike Plates)")]
        [Tooltip("Grip fraction stripped from a car you hit hard enough. 0.3 = -30%.")]
        [Range(0f, 0.9f)] public float ContactGripSap;
        [Tooltip("Engine-torque fraction stripped from a car you hit. 0.3 = -30%.")]
        [Range(0f, 0.9f)] public float ContactPowerSap;
        [Tooltip("Minimum collision impulse (N·s) that counts as a hit — gentle rubs do nothing.")]
        public float MinImpactImpulse;
        [Tooltip("How fast a contact victim recovers, per second. Lower = longer sting.")]
        public float ContactRecoverPerS;

        [Header("Proximity aura (Disruptor Field)")]
        [Tooltip("Aura radius in metres; 0 = no aura.")]
        public float AuraRadiusM;
        [Tooltip("Grip fraction stripped each step from rivals behind you inside the aura. 0.2 = -20%.")]
        [Range(0f, 0.9f)] public float AuraGripSap;
        [Tooltip("How fast a car recovers once it leaves the aura, per second. Higher = snappier.")]
        public float AuraRecoverPerS;

        public bool HasContact => ContactGripSap > 0f || ContactPowerSap > 0f;
        public bool HasAura => AuraRadiusM > 0f && AuraGripSap > 0f;
        public bool IsActive => HasContact || HasAura;

        /// <summary>An inert profile with sane thresholds: the car can be hit and sapped, but attacks no one.</summary>
        public static AttackProfile None => new AttackProfile
        {
            MinImpactImpulse = 2000f,
            ContactRecoverPerS = 0.8f,
            AuraRecoverPerS = 3f,
        };
    }
}
