using System.Collections.Generic;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// The full catalogue of parts a run's shop can roll from. Built/refreshed by
    /// MetaAssetsBuilder; consumed by RunDirector → ShopLogic.
    /// </summary>
    [CreateAssetMenu(menuName = "Shitboxer/Part Pool", fileName = "PartPool")]
    public class PartPool : ScriptableObject
    {
        public List<PartDef> Parts = new List<PartDef>();
    }
}
