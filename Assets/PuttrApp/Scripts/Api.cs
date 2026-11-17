// HTTP to api.puttr.co with the signed-in Player's Firebase id token, as puttr-app's utils/api.js
// does it. Coroutines, because every caller is on the main thread and wants to touch UI after.
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PuttrApp
{
    public static class Api
    {
        public const int TimeoutSeconds = 15;

        /// <summary>One request. done(status, body): status 0 means it never reached the server.</summary>
        public static IEnumerator Send(string method, string url, string body, string bearer, Action<long, string> done, string contentType = "application/json")
        {
            float began = Time.realtimeSinceStartup;
            using (var req = new UnityWebRequest(url, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.SetRequestHeader("Content-Type", contentType);
                }
                if (!string.IsNullOrEmpty(bearer)) req.SetRequestHeader("Authorization", "Bearer " + bearer);
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();
                long status = req.result == UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode;
                Perf.Request(method, url, status, (Time.realtimeSinceStartup - began) * 1000f);
                done?.Invoke(status, req.downloadHandler != null ? req.downloadHandler.text : "");
            }
        }

        /// <summary>GET a path as the signed-in Player. ok gets the parsed JSON.</summary>
        public static IEnumerator Get(string path, Action<object> ok, Action<string> fail = null) => Call("GET", path, null, true, ok, fail);

        /// <summary>POST a JSON body. The game-save routes take no token, as in the app; pass auth false for those.</summary>
        public static IEnumerator Post(string path, object body, Action<object> ok, Action<string> fail = null, bool auth = true) => Call("POST", path, J.Write(body), auth, ok, fail);

        /// <summary>Any verb with an optional JSON body (PUT, PATCH, DELETE: the putter routes).</summary>
        public static IEnumerator Do(string method, string path, object body, Action<object> ok, Action<string> fail = null) => Call(method, path, body == null ? null : J.Write(body), true, ok, fail);

        /// <summary>GET without a token (the routes the app calls with plain axios).</summary>
        public static IEnumerator Open(string path, Action<object> ok, Action<string> fail = null) => Call("GET", path, null, false, ok, fail);

        // ── Play records are not written yet (Matt, 2026-11-17) ──────────────────
        // While this rebuild is in testing, nothing it plays is written to production: no games, putts, scores,
        // leaderboard rows, Versus, live games, Tracks rounds or Lab sessions. Those calls are answered here instead:
        // a call that opens a record gets a local id (so the screens carry on), the rest a plain "ok". Turn on with
        // -app2WritePlay (test accounts only) or by setting WritePlay when this build goes live.
        public static bool WritePlay = Array.IndexOf(Environment.GetCommandLineArgs(), "-app2WritePlay") >= 0;

        static readonly string[] PlayRecords =
        {
            "/game/new", "/game/savePutt", "/game/save", "/game/over", "/game/abandon", "/game/multiplayer",
            "/club/leaderboard/add", "/tournaments/game/", "/league/score/submit", "/versus/", "/multiplayer/",
            "/tracks/round", "/lab/session", "/lab/recording",
        };

        /// <summary>True when this call would write a play record that this build keeps off production.</summary>
        public static bool Held(string method, string path)
        {
            if (WritePlay || method == "GET") return false;
            foreach (string prefix in PlayRecords) if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static int _localIds;

        static IEnumerator Call(string method, string path, string body, bool auth, Action<object> ok, Action<string> fail)
        {
            if (Held(method, path))
            {
                Debug.Log("Puttr 2 api: " + method + " " + path + " held (play records are not written in this build)");
                yield return null;
                // An opened record gets a local id; anything else just succeeds.
                bool opens = path.StartsWith("/game/new") || path.StartsWith("/tournaments/game/new") || path.StartsWith("/tracks/round") && method == "POST"
                    || path.StartsWith("/lab/session/start") || path.StartsWith("/multiplayer/game/create") || path.StartsWith("/versus/create");
                string id = "local-" + (++_localIds);
                ok?.Invoke(opens ? J.Parse("{\"id\":\"" + id + "\",\"gameId\":\"" + id + "\",\"sessionId\":\"" + id + "\",\"insertId\":\"" + id + "\"}") : J.Parse("{\"ok\":true}"));
                yield break;
            }
            if (Fixtures.On)
            {
                // Made-up answers arrive after a moment, as real ones do (-app2FixtureDelay seconds, default 0.15).
                yield return new WaitForSecondsRealtime(Fixtures.Delay * (0.6f + 0.1f * (path.Length % 7)));
                ok?.Invoke(Fixtures.For(path));
                yield break;
            }
            string token = null;
            if (auth)
            {
                yield return Auth.Token(t => token = t);
                if (token == null) { fail?.Invoke("signed_out"); yield break; }
            }
            long status = 0;
            string text = null;
            yield return Send(method, Config.ApiUrl + path, body, token, (s, t) => { status = s; text = t; });
            if (status >= 200 && status < 300) ok?.Invoke(J.Parse(text));
            else
            {
                Debug.LogWarning("Puttr 2 api: " + method + " " + path + " failed, status " + status);
                fail?.Invoke(status == 0 ? "offline" : "http_" + status);
            }
        }
    }
}
