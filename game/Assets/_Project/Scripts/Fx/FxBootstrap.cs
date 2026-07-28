using Shitboxer.Race;
using Shitboxer.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shitboxer.Fx
{
    /// <summary>
    /// Self-installer for the juice layer. Fx is referenced by NOBODY (FxAssemblyGuardTests) — not
    /// even the scene builders — so nothing can serialize its components into a scene. Instead this
    /// hook watches scene loads at runtime and, wherever a <see cref="RaceManager"/> exists, spawns an
    /// FxRig with a bound <see cref="RaceFxController"/> and makes sure the camera can actually hear
    /// (the builders' camera has no AudioListener — audio didn't exist when they were written).
    /// A headless server build excludes this assembly and therefore all of it, by construction.
    /// </summary>
    internal static class FxBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += (_, _) => TryRig();
            TryRig(); // the first scene has already loaded by the time this runs
        }

        private static void TryRig()
        {
            var race = Object.FindFirstObjectByType<RaceManager>();
            if (race == null) return;                       // menu / non-race scene — nothing to voice
            if (Object.FindFirstObjectByType<RaceFxController>() != null) return; // already rigged

            VehicleController player = FindPlayer();
            if (player == null) return;

            EnsureListener();

            var rig = new GameObject("FxRig");
            rig.AddComponent<RaceFxController>().Bind(race, player);
            rig.AddComponent<RaceVisualFx>().Bind(player);
        }

        /// <summary>The player is the car nobody drives FOR: every bot carries a BotDriver, the human
        /// car never does. Same convention the scene builders wire the chase camera by.</summary>
        private static VehicleController FindPlayer()
        {
            foreach (VehicleController car in Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
                if (car.GetComponent<BotDriver>() == null)
                    return car;
            return null;
        }

        private static void EnsureListener()
        {
            if (Object.FindFirstObjectByType<AudioListener>() != null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            cam.gameObject.AddComponent<AudioListener>();
        }
    }
}
