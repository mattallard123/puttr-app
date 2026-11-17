// Placeholder app icons, drawn from the Puttr logo.
//
//   Puttr > Make App Icons
//
// fills every icon slot Unity knows for iOS and Apple TV (including the
// layered Apple TV icon and both Top Shelf images) and sets the default icon
// the Mac app uses. The pictures are written to Assets/PuttrArcade/AppIcons.
// Replace them with real artwork of the same sizes, or assign your own in
// Player Settings, when there is some.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace PuttrArcade.EditorTools
{
    public static class AppIcons
    {
        const string Folder = "Assets/PuttrArcade/AppIcons";
        enum Part { Ground, Logo, Flat }

        static Texture2D _mark, _wordmark;
        static readonly Dictionary<string, Texture2D> Made = new Dictionary<string, Texture2D>();

        [MenuItem("Puttr/Make App Icons")]
        public static void Run()
        {
            _mark = Source("LogoMark");
            _wordmark = Source("LogoHeader");
            Made.Clear();
            Directory.CreateDirectory(Folder);

            int slots = 0;
            foreach (NamedBuildTarget target in new[] { NamedBuildTarget.iOS, NamedBuildTarget.tvOS })
            {
                foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(target))
                {
                    PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(target, kind);
                    foreach (PlatformIcon icon in icons)
                    {
                        // A layered icon is a ground and the logo floating over it; anything else is one flat picture.
                        // The first layer is the front one; the last must be opaque.
                        if (icon.maxLayerCount >= 2) icon.SetTextures(Picture(icon.width, icon.height, Part.Logo), Picture(icon.width, icon.height, Part.Ground));
                        else icon.SetTextures(Picture(icon.width, icon.height, Part.Flat));
                        slots++;
                    }
                    PlayerSettings.SetPlatformIcons(target, kind, icons);
                }
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { Picture(1024, 1024, Part.Flat) }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log("Puttr Arcade icons: " + slots + " slots filled from " + Made.Count + " pictures in " + Folder);
        }

        static Texture2D Source(string name)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes("Assets/PuttrArcade/Resources/PuttrArcade/" + name + ".png"));
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        static Texture2D Picture(int w, int h, Part part)
        {
            string name = (part == Part.Ground ? "Ground" : part == Part.Logo ? "Logo" : "Icon") + "-" + w + "x" + h;
            if (Made.TryGetValue(name, out Texture2D made)) return made;

            // Very wide pictures (the Top Shelf) carry the whole wordmark; the rest, the mark alone.
            bool wide = w > h * 2.2f;
            Texture2D logo = wide ? _wordmark : _mark;
            float logoH = wide ? h * 0.3f : h * 0.6f;
            float logoW = logoH * logo.width / logo.height;
            float left = (w - logoW) * 0.5f, bottom = (h - logoH) * 0.5f;

            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = Color.clear;
                    if (part != Part.Logo)
                    {
                        // Near black, with a little green light behind the logo.
                        float dx = (x + 0.5f - w * 0.5f) / (h * 0.9f), dy = (y + 0.5f - h * 0.5f) / (h * 0.9f);
                        float glow = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        c = Color.Lerp(new Color(0.012f, 0.016f, 0.014f, 1f), new Color(0.03f, 0.13f, 0.06f, 1f), glow * glow);
                    }
                    if (part != Part.Ground)
                    {
                        // The logo, averaged over nine samples a pixel so small sizes stay smooth.
                        Color sum = Color.clear;
                        for (int sy = 0; sy < 3; sy++)
                        {
                            for (int sx = 0; sx < 3; sx++)
                            {
                                float u = (x + (sx + 0.5f) / 3f - left) / logoW, v = (y + (sy + 0.5f) / 3f - bottom) / logoH;
                                if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                                Color s = logo.GetPixelBilinear(u, v);
                                sum += new Color(s.r * s.a, s.g * s.a, s.b * s.a, s.a);
                            }
                        }
                        sum /= 9f;
                        if (part == Part.Logo) c = sum.a > 0.001f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
                        else c = new Color(sum.r + c.r * (1f - sum.a), sum.g + c.g * (1f - sum.a), sum.b + c.b * (1f - sum.a), 1f);
                    }
                    px[y * w + x] = c;
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            string path = Folder + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None; // or every icon is squeezed to a power of two
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = part == Part.Logo;
            importer.SaveAndReimport();
            made = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Made[name] = made;
            return made;
        }
    }
}
