// Uploading a video to Google Cloud Storage the way puttr-app's VideoUploadFunctions.js does:
//   POST /coaching/createUploadUrl {fileName}   -> {signedResumableStartUrl, startHeaders}
//   POST the signed URL with startHeaders       -> the session URL (Location header)
//   PUT 2 MiB chunks with Content-Range, picking up from the Range the server last confirmed;
//   up to 50 retries, backing off 1 s to 30 s.
// The file lands at https://storage.googleapis.com/coaching.puttr.co/<fileName>.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace PuttrApp
{
    public static class Uploads
    {
        public const string Bucket = "https://storage.googleapis.com/coaching.puttr.co/";
        const int Chunk = 2 * 1024 * 1024; // a multiple of 256 KiB, as GCS requires

        /// <summary>progress 0 to 1 as it goes; done(publicUrl) or done(null) on failure.</summary>
        public static IEnumerator Video(string path, string fileName, Action<float> progress, Action<string> done)
        {
            long size;
            try { size = new FileInfo(path).Length; } catch (Exception) { done(null); yield break; }
            object start = null;
            yield return Api.Post("/coaching/createUploadUrl", new Dictionary<string, object> { { "fileName", fileName } }, d => start = d, _ => { });
            string startUrl = J.Str(start, "signedResumableStartUrl");
            if (startUrl.Length == 0) { done(null); yield break; }

            string session = null;
            using (var req = new UnityWebRequest(startUrl, "POST") { uploadHandler = new UploadHandlerRaw(new byte[0]), downloadHandler = new DownloadHandlerBuffer() })
            {
                foreach (KeyValuePair<string, object> h in J.Obj(J.Get(start, "startHeaders")) ?? new Dictionary<string, object>())
                    if (h.Key != "Content-Length") req.SetRequestHeader(h.Key, h.Value as string ?? Convert.ToString(h.Value));
                yield return req.SendWebRequest();
                session = req.GetResponseHeader("Location");
            }
            if (string.IsNullOrEmpty(session)) { done(null); yield break; }

            long sent = 0;
            int failures = 0;
            float wait = 1f;
            using (FileStream file = File.OpenRead(path))
            {
                while (sent < size)
                {
                    int length = (int)Math.Min(Chunk, size - sent);
                    var buffer = new byte[length];
                    file.Seek(sent, SeekOrigin.Begin);
                    int read = 0;
                    while (read < length) read += file.Read(buffer, read, length - read);
                    long status;
                    string range;
                    using (var put = new UnityWebRequest(session, "PUT") { uploadHandler = new UploadHandlerRaw(buffer), downloadHandler = new DownloadHandlerBuffer() })
                    {
                        put.SetRequestHeader("Content-Range", "bytes " + sent + "-" + (sent + length - 1) + "/" + size);
                        put.timeout = 120;
                        yield return put.SendWebRequest();
                        status = put.responseCode;
                        range = put.GetResponseHeader("Range");
                    }
                    if (status == 200 || status == 201) { sent = size; break; }
                    if (status == 308)
                    {
                        // The server says how much it has: "bytes=0-1234".
                        sent = !string.IsNullOrEmpty(range) && long.TryParse(range.Substring(range.LastIndexOf('-') + 1), out long last) ? last + 1 : sent + length;
                        failures = 0; wait = 1f;
                        progress?.Invoke(size > 0 ? sent / (float)size : 1f);
                        continue;
                    }
                    if (++failures > 50) { done(null); yield break; }
                    yield return new WaitForSecondsRealtime(wait);
                    wait = Mathf.Min(30f, wait * 2f);
                }
            }
            progress?.Invoke(1f);
            done(Bucket + fileName);
        }

        /// <summary>A local video's length in seconds (or -1), read by preparing it.</summary>
        public static IEnumerator Length(string path, Action<double> done)
        {
            var go = new GameObject("Video length");
            var player = go.AddComponent<VideoPlayer>();
            player.source = VideoSource.Url;
            player.url = "file://" + path;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.renderMode = VideoRenderMode.APIOnly;
            bool ready = false, failed = false;
            player.prepareCompleted += _ => ready = true;
            player.errorReceived += (_, __) => failed = true;
            player.Prepare();
            float until = Time.realtimeSinceStartup + 10f;
            while (!ready && !failed && Time.realtimeSinceStartup < until) yield return null;
            double seconds = ready ? player.length : -1;
            UnityEngine.Object.Destroy(go);
            done(seconds);
        }

        public static string Random() => UnityEngine.Random.Range(100000, 999999).ToString();
    }
}
