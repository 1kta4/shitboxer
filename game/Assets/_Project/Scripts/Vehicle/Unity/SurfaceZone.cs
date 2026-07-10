using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>
    /// Marks a ground collider as a low-grip surface (grass, dirt, gravel). A track author drops this
    /// on the collider's GameObject — or a parent of it — and <see cref="VehicleController"/> reads it
    /// during each wheel's ground spherecast, feeding <see cref="GripMultiplier"/> into that contact's
    /// <c>SurfaceGripMult</c>. The engine-loop-independent sim then multiplies it into the tyre friction
    /// circle, so cars slither on grass and grip on tarmac. Pure authoring data: no per-step cost and no
    /// coupling into the sim (the host does the scene lookup and hands over a plain float).
    /// 1 = full tarmac grip; lower is slippier. Absence of this component means full grip.
    /// </summary>
    public class SurfaceZone : MonoBehaviour
    {
        [Tooltip("Tyre-grip multiplier for cars on this surface. 1 = full tarmac grip; e.g. 0.6 = slick grass/dirt.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float gripMultiplier = 0.6f;

        /// <summary>
        /// Tyre-grip multiplier a wheel touching this surface should use, clamped to a sane band so a
        /// hand-edited value can never zero out (or amplify) grip. 1 = full tarmac; lower is slippier.
        /// </summary>
        public float GripMultiplier => Mathf.Clamp(gripMultiplier, 0.05f, 1f);
    }
}
