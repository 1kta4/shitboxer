using UnityEditor;
using UnityEngine;

namespace Shitboxer.Editor
{
    /// <summary>
    /// Bakes the v3 UI's gradient "depth" into PNG sprites, because USS cannot render gradient functions
    /// live (linear-/radial-gradient throw "invalid image texture" at repaint). Each surface gets a tall
    /// 1-D vertical gradient (lit top -> shadow bottom, straight from garage-ps2-v3) that USS references
    /// via `background-image` and stretches to fill — turning the flat plates into shaded ones with no
    /// hand-drawn art. Re-run after tweaking a colour; the PNGs overwrite in place. Sprites land in
    /// Assets/_Project/Scripts/UI/Sprites and are wired up in Tokens/Shitboxer/Garage.uss.
    /// </summary>
    public static class UiSpriteBaker
    {
        private const string UiDir = "Assets/_Project/Scripts/UI";
        private const string Dir = UiDir + "/Sprites";

        [MenuItem("Shitboxer/Bake UI Sprites")]
        public static void Bake()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder(UiDir, "Sprites");

            //          name          top (lit)   bottom (shadow)
            VGradient("screen",     "#1a2130", "#0a0d14"); // the whole garage/end-screen backdrop (no bevel)
            Plate("rail-plate", "#242c3a", "#141a24");             // left rail (beveled 9-slice)
            Plate("offer",      "#232b39", "#1a212c", cut: 14);             // shop offer row (diagonal notch)
            Plate("offer-sel",  "#2f3d55", "#212a3a", glow: true, cut: 14); // selected offer — glow + notch
            Plate("button",     "#e8eff9", "#94a5bd");                      // chrome buttons
            Plate("cta",        "#4f8bff", "#0f2478", glow: true, cut: 14); // NEXT RACE / NEW RUN — glow + notch
            Plate("ghost",      "#2b3548", "#161d28");             // ghost buttons / owned rows / tag
            VGradient("fill",       "#8fc4ff", "#1b46b4"); // GRIP / POWER stat fill (bar, no bevel)
            VGradient("track",      "#1a2230", "#080c14"); // recessed stat track (no bevel)

            // stat-preview ghost: the mock's repeating-linear-gradient diagonal stripes, baked + tiled.
            Stripes("ghost-stripe",      new Color(0.373f, 0.851f, 0.478f, 0.55f), new Color(0.373f, 0.851f, 0.478f, 0.20f));
            Stripes("ghost-stripe-loss", new Color(0.878f, 0.220f, 0.306f, 0.55f), new Color(0.878f, 0.220f, 0.306f, 0.20f));
            Scanlines(); // CRT overlay for the garage / end screens

            AssetDatabase.Refresh();

            // USS resolves url() refs at IMPORT time. The sheets were imported before these sprites
            // existed, so their refs are unresolved and render as the yellow "missing texture"
            // placeholder — re-import the sheets now that the sprites exist so they re-resolve.
            AssetDatabase.ImportAsset(UiDir + "/USS/Shitboxer.uss", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(UiDir + "/USS/Garage.uss", ImportAssetOptions.ForceUpdate);

            Debug.Log($"[Shitboxer] Baked UI gradient sprites into {Dir} and reimported the USS. " +
                      "Reopen the garage to see the depth.");
        }

        private static void VGradient(string name, string topHex, string bottomHex)
        {
            ColorUtility.TryParseHtmlString(topHex, out Color top);
            ColorUtility.TryParseHtmlString(bottomHex, out Color bottom);

            const int w = 4, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                Color c = Color.Lerp(bottom, top, y / (float)(h - 1)); // y=0 bottom of the texture
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, c);
            }
            tex.Apply();

            string path = $"{Dir}/{name}.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;   // plain Texture2D — the type USS background-image wants
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }

        /// <summary>
        /// A 9-slice plate: the vertical gradient plus a 2px raised BEVEL baked into the border (lit
        /// top+left, shadowed bottom+right, light-from-upper-left) and, when <paramref name="glow"/>, a
        /// cobalt glow on the lit edge. USS 9-slices it (-unity-slice-*), so the 2px bevel stays crisp
        /// while the gradient centre stretches. Point-filtered so the bevel edge is a hard 1px line.
        /// </summary>
        private static void Plate(string name, string topHex, string bottomHex, bool glow = false, int cut = 0)
        {
            ColorUtility.TryParseHtmlString(topHex, out Color top);
            ColorUtility.TryParseHtmlString(bottomHex, out Color bottom);
            Color lit = glow ? new Color(0.55f, 0.78f, 1f) : Color.white;
            float litAmount = glow ? 0.60f : 0.30f;

            int s = cut > 0 ? 40 : 32;
            const int bevel = 2;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    Color c = Color.Lerp(bottom, top, y / (float)(s - 1)); // y=0 bottom of the texture
                    bool onLit = y >= s - bevel || x < bevel;             // top or left edge
                    bool onShadow = !onLit && (y < bevel || x >= s - bevel); // bottom or right edge
                    if (onLit) c = Color.Lerp(c, lit, litAmount);
                    else if (onShadow) c = Color.Lerp(c, Color.black, 0.42f);
                    if (cut > 0 && x - y > s - cut) c.a = 0f; // diagonal notch on the bottom-right corner
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();

            string path = $"{Dir}/{name}.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.filterMode = FilterMode.Point;              // crisp bevel edge
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }

        /// <summary>A tileable 135-degree diagonal-stripe sprite (two alphas of one colour) — the stat
        /// preview ghost's repeating-linear-gradient from garage-ps2-v3, which USS can't render live.
        /// Imported with Repeat wrap so USS `background-repeat: repeat` tiles it across the delta segment.</summary>
        private static void Stripes(string name, Color a, Color b)
        {
            const int size = 16, period = 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, (x + y) % (period * 2) < period ? a : b);
            tex.Apply();

            string path = $"{Dir}/{name}.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.filterMode = FilterMode.Point;      // crisp stripe edges
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Repeat;  // tiled by background-repeat
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }

        /// <summary>A tileable CRT scanline overlay — a 1px dark line every 4px. Kept faint by the
        /// overlay's USS opacity; only the menu screens use it (never over the live race).</summary>
        private static void Scanlines()
        {
            const int w = 4, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, y % 4 == 0 ? Color.black : new Color(0f, 0f, 0f, 0f));
            tex.Apply();

            string path = $"{Dir}/scanlines.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.filterMode = FilterMode.Point;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }
    }
}
