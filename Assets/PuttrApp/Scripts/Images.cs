// Pictures from the network (Player photos), kept in memory and on disk so a list scrolled twice
// downloads nothing the second time.
//
// Six downloads at a time (a Top 100 board used to start a hundred at once), a picture that failed is not
// asked for again for a minute. Still to do: decoding is on the main thread, and nothing kept in memory is
// ever let go (a picture on screen cannot simply be destroyed, so that needs the screens to say what they hold).
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

        /// <summary>Aspect-fill for a picture shown as a background (scale-and-crop, centred): an avatar's
        /// photo is never stretched to its circle. UI Toolkit's default is stretch-to-fill.</summary>
        public static T Cover<T>(T element) where T : VisualElement
        {
            element.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(BackgroundSizeType.Cover));
            element.style.backgroundPositionX = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
            element.style.backgroundPositionY = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
            element.style.backgroundRepeat = new StyleBackgroundRepeat(new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat));
            return element;
        }

        const int AtOnce = 6;
        const float RetryAfter = 60f;
        static readonly Queue<string> _queued = new Queue<string>();
        static readonly Dictionary<string, float> _failed = new Dictionary<string, float>();
        static int _running;

        public static void Load(string url, Action<Texture2D> done)
        {
            if (_memory.TryGetValue(url, out Texture2D have) && have != null) { done(have); return; }
            if (_failed.TryGetValue(url, out float at) && Time.realtimeSinceStartup - at < RetryAfter) { done(null); return; }
            if (_waiting.TryGetValue(url, out List<Action<Texture2D>> queue)) { queue.Add(done); return; }
            _waiting[url] = new List<Action<Texture2D>> { done };
            _queued.Enqueue(url);
            Pump();
        }

        static void Pump()
        {
            while (_running < AtOnce && _queued.Count > 0)
            {
                _running++;
                App.Run(Fetch(_queued.Dequeue()));
            }
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
                _failed.Remove(url);
                Perf.ImagesLoaded++;
                if (fromDisk) Perf.ImagesFromDisk++;
            }
            else _failed[url] = Time.realtimeSinceStartup;
            _running--;
            List<Action<Texture2D>> queue = _waiting[url];
            _waiting.Remove(url);
            foreach (Action<Texture2D> callback in queue) callback(texture);
            Pump();
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
