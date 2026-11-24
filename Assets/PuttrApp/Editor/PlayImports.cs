// How the game cards (Resources/PuttrApp/Cards) and the arcade's sounds (Resources/PuttrArcade/Audio) are imported,
// by folder and file name, so new cards and sounds dropped in come out right with no .meta to set by hand.
//
// Before this (2026-11-19) the 50 cards were imported uncompressed (77.6 MB of the build, about 2.2 MB each on the GPU)
// and all 1,130 sounds at Vorbis quality 100, decompressed whole the first time each was played (141.5 MB of the
// build; a 22 second loop is 4 MB of memory and a stall on the main thread while it unpacks).
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuttrApp.EditorTools
{
    sealed class PlayImports : AssetPostprocessor
    {
        const string Cards = "Assets/PuttrApp/Resources/PuttrApp/Cards/";
        const string Audio = "Assets/PuttrArcade/Resources/PuttrArcade/Audio/";

        public override uint GetVersion() => 1;

        // A card: the high-quality block format (ASTC 4x4 on phones and TVs, BC7 on a desktop), which is a quarter of
        // the memory and close to the eye to the original. Still no mipmaps and no resizing: they are drawn near
        // their own size.
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Cards)) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.npotScale = TextureImporterNPOTScale.None;
            t.mipmapEnabled = false;
            t.alphaSource = TextureImporterAlphaSource.None;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Audio)) return;
            var a = (AudioImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            bool voice = assetPath.StartsWith(Audio + "Voice/");
            bool loop = name.Contains("_loop");
            long bytes = 0;
            try { bytes = new FileInfo(assetPath).Length; } catch (System.Exception) { }
            // About a second or less: a .wav is 176 KB a second in stereo, an .mp3 here about 16 KB.
            bool brief = bytes > 0 && bytes < (assetPath.EndsWith(".wav") ? 200 * 1024 : 20 * 1024);

            AudioImporterSampleSettings s = a.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = false;   // nothing is read until a game first asks for the sound
            if (loop)
            {
                // A game's ambience, 22 seconds long: read from the file as it plays (a TV browser has no streaming and
                // keeps it packed in memory instead, which Unity does by itself).
                s.loadType = AudioClipLoadType.Streaming;
                s.quality = 0.45f;
            }
            else if (brief)
            {
                // A knock, a click: unpacked once (a few milliseconds), then free to play as often as it likes.
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.quality = 0.5f;
            }
            else
            {
                // Everything longer, and every spoken line: kept packed, unpacked as it plays.
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.quality = 0.5f;
            }
            a.defaultSampleSettings = s;
            a.forceToMono = voice;   // a voice is one channel; the effects keep their stereo
            a.loadInBackground = !loop && !brief;
        }
    }
}
