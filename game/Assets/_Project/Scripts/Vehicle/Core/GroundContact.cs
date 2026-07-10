using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// World-space ground query result for one wheel, produced by whatever hosts the sim
    /// (a MonoBehaviour raycasting PhysX today, a headless server tomorrow). The core never
    /// touches the physics scene itself.
    /// </summary>
    public struct GroundContact
    {
        public bool Grounded;
        /// <summary>Distance from the attach point to the ground along -SuspensionUp.</summary>
        public float HitDistance;
        public Vector3 HitPoint;
        public Vector3 SurfaceNormal;
        /// <summary>Velocity of the rigidbody at the contact point.</summary>
        public Vector3 PointVelocity;
        /// <summary>World-space suspension direction (chassis up).</summary>
        public Vector3 SuspensionUp;
        /// <summary>World-space rolling direction of the wheel, steering included.</summary>
        public Vector3 WheelForward;
        public Vector3 WheelRight;
        /// <summary>World-space wheel attach point on the chassis.</summary>
        public Vector3 AttachPoint;

        private float _surfaceGripMult;

        /// <summary>
        /// Ground-grip multiplier for the surface this wheel is touching: 1 = full tarmac grip, lower
        /// is slippier (grass/dirt/gravel). The host reads it off a <c>SurfaceZone</c> on the hit
        /// collider; the sim folds it into the tyre friction circle alongside the combat/wear mults.
        /// Backed so an UNSET/default contact reads as 1 — every existing contact (and the headless
        /// server step) is therefore unchanged until a track actually marks a low-grip zone.
        /// </summary>
        public float SurfaceGripMult
        {
            get => _surfaceGripMult > 0f ? _surfaceGripMult : 1f;
            set => _surfaceGripMult = value;
        }
    }

    /// <summary>One world-space force the host must apply to its rigidbody this step.</summary>
    public struct ForceCommand
    {
        public Vector3 Force;
        public Vector3 Position;
    }
}
