using System;
using System.IO;
using UnityEngine;

namespace Shitboxer.Meta
{
    /// <summary>
    /// Player-facing settings (sound / video / gameplay), saved as JSON beside the meta profile and
    /// applied to the engine on load and on change. Mirrors MetaProgress's save/load shape. The gameplay
    /// flags (screen shake, damage flash) are read by the juice layer once it's enabled.
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        public const string FileName = "shitboxer_settings.json";

        // sound (0..1)
        public float masterVolume = 1f;
        public float musicVolume = 0.8f;
        public float sfxVolume = 1f;

        // video
        public bool fullscreen = true;
        public bool vsync = true;
        public int qualityLevel = -1; // -1 = leave the project default alone

        // gameplay
        public bool screenShake = true;
        public bool damageFlash = true;

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Push the settings into the engine. Cheap and idempotent — call on load and on change.</summary>
        public void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(masterVolume);
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            if (qualityLevel >= 0 && qualityLevel < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(qualityLevel, true);
            if (Screen.fullScreen != fullscreen) Screen.fullScreen = fullscreen;
        }

        public static GameSettings Load() => Load(DefaultPath);

        public static GameSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var s = JsonUtility.FromJson<GameSettings>(File.ReadAllText(path));
                    if (s != null) return s;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Shitboxer] Settings load failed: {e.Message}"); }
            return new GameSettings();
        }

        public static void Save(GameSettings s) => Save(s, DefaultPath);

        public static void Save(GameSettings s, string path)
        {
            try { File.WriteAllText(path, JsonUtility.ToJson(s, true)); }
            catch (Exception e) { Debug.LogWarning($"[Shitboxer] Settings save failed: {e.Message}"); }
        }
    }
}
