using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Scene-side guard for the RunDirector singleton: the RunRig saved into the race scene
    /// re-spawns on every reload, so this destroys the duplicate rig when a run is already in
    /// flight. RunDirector.Awake performs the same check itself — this exists so the rig dies
    /// as one unit regardless of component Awake order, and as the editor-visible marker that
    /// a scene is run-mode enabled.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class RunBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (RunDirector.Instance != null && RunDirector.Instance.gameObject != gameObject)
                Destroy(gameObject);
        }
    }
}
