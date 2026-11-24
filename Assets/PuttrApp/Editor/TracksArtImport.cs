// How the Tracks scans (Resources/PuttrApp/Tracks, baked by Tools/tracks-art/bake.sh) are imported, by file name, so
// the bake can rewrite the files freely and no .meta has to be kept right by hand.
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuttrApp.EditorTools
{
    sealed class TracksArtImport : AssetPostprocessor
    {
        const string Folder = "Assets/PuttrApp/Resources/PuttrApp/Tracks/";

        public override uint GetVersion() => 2;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var t = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            t.textureType = TextureImporterType.Default;
            t.npotScale = TextureImporterNPOTScale.None;
            t.textureCompression = TextureImporterCompression.Compressed;
            t.mipmapEnabled = true;
            t.sRGBTexture = true;
            t.alphaSource = TextureImporterAlphaSource.None;
            t.alphaIsTransparency = false;
            t.wrapMode = TextureWrapMode.Repeat;
            t.anisoLevel = 8;
            t.maxTextureSize = 1024;
            if (name == "Sky")
            {
                // No mips: the shader looks it up by direction, and a mip chain would draw a seam where the panorama wraps.
                t.mipmapEnabled = false;
                t.wrapModeU = TextureWrapMode.Repeat; t.wrapModeV = TextureWrapMode.Clamp;
                t.anisoLevel = 1;
                t.maxTextureSize = 4096;
            }
            else if (name == "Trees")
            {
                // Cut out by alpha: the smaller mips keep as much of each tree covered as the full picture has.
                t.alphaSource = TextureImporterAlphaSource.FromInput;
                t.mipMapsPreserveCoverage = true;
                t.alphaTestReferenceValue = 0.4f;
                t.wrapMode = TextureWrapMode.Clamp;
                t.maxTextureSize = 2048;
            }
            else if (name == "TreesNormal")
            {
                t.sRGBTexture = false;   // rgb = n * 0.5 + 0.5, read as it is
                t.wrapMode = TextureWrapMode.Clamp;
                t.maxTextureSize = 2048;
            }
            else if (name.EndsWith("Normal")) t.textureType = TextureImporterType.NormalMap;
        }

        /// <summary>Tools/app-build.sh tracksart: import the folder again (after a bake, or a change to the rules above).</summary>
        public static void Reimport()
        {
            AssetDatabase.Refresh();
            foreach (string guid in AssetDatabase.FindAssets("", new[] { Folder.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Debug.Log("Puttr 2: Tracks art imported");
        }
    }
}
