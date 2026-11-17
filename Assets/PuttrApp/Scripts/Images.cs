// Pictures from the network (Player photos), kept in memory and on disk so a list scrolled twice
// downloads nothing the second time.
//
// SPIKE: decoding happens on the main thread and nothing is ever evicted. Both are fine for a
// feed of a few dozen avatars and both need replacing before a real feed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Images
    {
        static readonly Dictionary<string, Texture2D> _memory = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, List<Action<Texture2D>>> _waiting = new Dictionary<string, List<Action<Texture2D>>>();

        static string Folder => Path.Combine(Application.temporaryCachePath, "img");

        /// <summary>Shows the picture at url as the element's background once it is here.</summary>
        public static void Into(VisualElement element, string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("http")) return;
            Load(url, texture =>
            {
                if (texture == null) return;
                element.style.backgroundImage = new StyleBackground(texture);
                element.AddToClassList("has-image");
                Perf.Wake(0.3f);
            });
        }

        public static void Load(string url, Action<Texture2D> done)
        {
            if (_memory.TryGetValue(url, out Texture2D have)) { done(have); return; }
            if (_waiting.TryGetValue(url, out List<Action<Texture2D>> queue)) { queue.Add(done); return; }
            _waiting[url] = new List<Action<Texture2D>> { done };
            App.Run(Fetch(url));
        }

        static IEnumerator Fetch(string url)
        {
            string file = Path.Combine(Folder, Hash(url));
            byte[] bytes = null;
            bool fromDisk = false;
            try { if (File.Exists(file)) { bytes = File.ReadAllBytes(file); fromDisk = true; } }
            catch (Exception) { bytes = null; }

            if (bytes == null)
            {
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.timeout = 20;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success) bytes = req.downloadHandler.data;
                }
                if (bytes != null)
                {
                    try { Directory.CreateDirectory(Folder); File.WriteAllBytes(file, bytes); }
                    catch (Exception) { /* the cache is a convenience */ }
                }
            }

            Texture2D texture = null;
            if (bytes != null && bytes.Length > 0)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                if (!texture.LoadImage(bytes, true)) { UnityEngine.Object.Destroy(texture); texture = null; }
            }
            if (texture != null)
            {
                _memory[url] = texture;
                Perf.ImagesLoaded++;
                if (fromDisk) Perf.ImagesFromDisk++;
            }
            List<Action<Texture2D>> queue = _waiting[url];
            _waiting.Remove(url);
            foreach (Action<Texture2D> callback in queue) callback(texture);
        }

        static string Hash(string text)
        {
            using (MD5 md5 = MD5.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in md5.ComputeHash(Encoding.UTF8.GetBytes(text))) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
