using UnityEngine;

namespace Shitboxer.Vehicle
{
    /// <summary>Authoring wrapper so a VehicleSpec can live as a project asset.</summary>
    [CreateAssetMenu(menuName = "Shitboxer/Vehicle Spec", fileName = "VehicleSpec")]
    public class VehicleSpecAsset : ScriptableObject
    {
        public VehicleSpec Spec = new VehicleSpec();
    }
}
