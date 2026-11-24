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
            // Full rate while a request is out: at the idle rate each step of the coroutine waits a slow frame.
            Perf.Wake(2f);
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

        // ── Answers kept between launches ────────────────────────────────────
        // A GET whose last answer is kept on the device (per Player): ok gets the kept answer at once, and again
        // with the live one when it differs. A screen read this way is on screen before the network answers. fail
        // is only called when there is nothing kept to show, or the Player is signed out.

        static string KeptFile(string path)
        {
            string uid = Auth.Uid;
            if (string.IsNullOrEmpty(uid)) return null;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in md5.ComputeHash(Encoding.UTF8.GetBytes(uid + " " + path))) sb.Append(b.ToString("x2"));
                return System.IO.Path.Combine(Application.persistentDataPath, "api", sb + ".json");
            }
        }

        /// <summary>GET a path, showing the last answer first. ok may be called twice: kept, then live.</summary>
        public static IEnumerator Kept(string path, Action<object> ok, Action<string> fail = null)
        {
            if (Fixtures.On) { yield return Get(path, ok, fail); yield break; }
            string file = KeptFile(path), kept = null;
            try { if (file != null && System.IO.File.Exists(file)) kept = System.IO.File.ReadAllText(file); }
            catch (Exception) { kept = null; }
            if (!string.IsNullOrEmpty(kept))
            {
                object was = J.Parse(kept);
                if (was != null) ok?.Invoke(was); else kept = null;
            }
            else kept = null;
            string fresh = null;
            yield return Call("GET", path, null, true,
                data => { if (kept == null || fresh != kept) ok?.Invoke(data); },
                why => { if (kept == null || why == "signed_out") fail?.Invoke(why); },
                false, text => fresh = text);
            if (file == null || string.IsNullOrEmpty(fresh) || fresh == kept) yield break;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                System.IO.File.WriteAllText(file, fresh);
            }
            catch (Exception) { /* a convenience: the next launch simply waits for the network */ }
        }

        /// <summary>POST a JSON body. The game-save routes take no token, as in the app; pass auth false for those.</summary>
        public static IEnumerator Post(string path, object body, Action<object> ok, Action<string> fail = null, bool auth = true, bool live = false) => Call("POST", path, J.Write(body), auth, ok, fail, live);

        /// <summary>Any verb with an optional JSON body (PUT, PATCH, DELETE: the putter routes).</summary>
        public static IEnumerator Do(string method, string path, object body, Action<object> ok, Action<string> fail = null) => Call(method, path, body == null ? null : J.Write(body), true, ok, fail);

        /// <summary>GET without a token (the routes the app calls with plain axios).</summary>
        public static IEnumerator Open(string path, Action<object> ok, Action<string> fail = null) => Call("GET", path, null, false, ok, fail);

        // ── Play records are written to production, as the Puttr app writes them ──
        // From 2026-11-17 this rebuild held everything it played on the device while it was tested. On 2026-11-18 Matt
        // asked for the app to "submit games, putts, tournaments, leagues etc to the API like in production", so play
        // records are written by default now: games, putts, scores, leaderboard rows, Versus, live games, Tracks rounds,
        // quests and the coach's session. The hold is kept for development only: launched with -app2HoldPlay, the calls
        // in the list below are answered on the device instead (a call that opens a record gets a local id so the
        // screens carry on, the rest a plain "ok"), and the screens say the round was not saved.
        public static bool HoldPlay = Array.IndexOf(Environment.GetCommandLineArgs(), "-app2HoldPlay") >= 0;

        /// <summary>The other side of HoldPlay: true (the default) when play records go to the API.</summary>
        public static bool WritePlay { get => !HoldPlay; set => HoldPlay = !value; }

        static readonly string[] PlayRecords =
        {
            "/game/new", "/game/savePutt", "/game/save", "/game/over", "/game/abandon", "/game/multiplayer",
            "/club/leaderboard/add", "/tournaments/game/", "/league/score/submit", "/versus/", "/multiplayer/",
            "/tracks/round",
            // Never held: /lab/session (start, putt, end) and /lab/recording (the lab's cloud recorder). A 3D session is
            // filmed, saved and processed for real even under -app2HoldPlay (Matt, 2026-11-18: "make 3D sessions record and save").
            // A quest level's putts and stars, and the coach's daily session: play like the rest.
            "/quests/savePutt", "/quests/level/finish", "/coach/session-",
            // A round's recording: tying it to the (held, local) game and the cloud recorder. The camera still runs.
            "/video/saveGame", "/video/agora/startRecording", "/video/agora/stopRecording",
        };

        /// <summary>True when this call would write a play record and -app2HoldPlay keeps it on the device. False in normal use.</summary>
        public static bool Held(string method, string path) => IsHeld(HoldPlay, method, path);

        /// <summary>The rule itself, apart from the launch flag (for the tests).</summary>
        public static bool IsHeld(bool holdPlay, string method, string path)
        {
            if (!holdPlay || method == "GET" || path == null) return false;
            foreach (string prefix in PlayRecords) if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Would a write to this path be held now? Only under -app2HoldPlay: the screens then say the round was not
        /// saved, and otherwise say what the Puttr app says.</summary>
        public static bool Holds(string path) => Held("POST", path);

        /// <summary>A game played now would not be written (-app2HoldPlay): its round, putts and score are all held.</summary>
        public static bool HoldsPlay => Holds("/game/new");

        /// <summary>An id the hold made up for a record it opened ("local-3"): nothing with this id exists on the server.</summary>
        public static bool IsLocal(string id) => !string.IsNullOrEmpty(id) && id.StartsWith("local-", StringComparison.Ordinal);

        /// <summary>What a screen says in place of "saved" while play is held (-app2HoldPlay only).</summary>
        public const string NotSaved = "Held on this device: not saved";

        /// <summary>"Held on this device: 12 putts were not saved." (-app2HoldPlay only.)</summary>
        public static string PuttsNotSaved(int putts) => "Held on this device: " + putts + (putts == 1 ? " putt was" : " putts were") + " not saved.";

        /// <summary>The body of the last failed call (an API error such as {error, message}), or null.</summary>
        public static object LastFailure;

        static int _localIds;

        static IEnumerator Call(string method, string path, string body, bool auth, Action<object> ok, Action<string> fail, bool live = false, Action<string> raw = null)
        {
            // live: a tournament round or League Night game the Player entered, which goes to production even under
            // -app2HoldPlay (Matt, 2026-11-18: tested in a real tournament).
            if (!live && Held(method, path))
            {
                Debug.Log("Puttr 2 api: " + method + " " + path + " held (-app2HoldPlay)");
                yield return null;
                // An opened record gets a local id; anything else just succeeds.
                bool opens = path.StartsWith("/game/new") || path.StartsWith("/tournaments/game/new") || path.StartsWith("/tracks/round") && method == "POST"
                    || path.StartsWith("/multiplayer/game/create") || path.StartsWith("/versus/create")
                    || path.StartsWith("/coach/session-start");   // answers {userGameId, gameId}
                string id = "local-" + (++_localIds);
                ok?.Invoke(opens ? J.Parse("{\"id\":\"" + id + "\",\"gameId\":\"" + id + "\",\"userGameId\":\"" + id + "\",\"sessionId\":\"" + id + "\",\"insertId\":\"" + id + "\"}") : J.Parse("{\"ok\":true}"));
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
            if (status >= 200 && status < 300)
            {
                object answer = J.Parse(text);
                Analytics.PlayRecord(method, path, body, answer);   // a play record opening or closing a game
                raw?.Invoke(text);
                ok?.Invoke(answer);
            }
            else
            {
                Debug.LogWarning("Puttr 2 api: " + method + " " + path + " failed, status " + status);
                LastFailure = J.Parse(text ?? "");
                fail?.Invoke(status == 0 ? "offline" : "http_" + status);
            }
        }
    }
}
