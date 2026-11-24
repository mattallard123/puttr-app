// What happens in the app, sent to Puttr: who opens it, on what, which screens and games get used, and
// how a game ends. The Puttr app's utils/Analytics.js, with the same event names and the same store
// (the API's /events, MongoDB puttr_logs.events), so one dashboard reads both apps.
//
// Events gather in a short queue and go out in batches to POST /events/batch, with the Player's token
// when signed in. Nothing here waits on the network or stops play: a batch that does not arrive is kept
// and sent with the next.
//
// On every event: app "puttr2", the install's id, the session's id (a new one after 30 minutes away),
// the build, the platform, the kind of device (tv, tablet, mobile, desktop), its OS and model, and the
// screen that was up. The API adds where the request came from.
//
// A game's start and end are read off the play records as the API takes them (Api.Call tells Analytics.PlayRecord),
// so every way of playing (an arcade game, a round, Tracks, a live game) is counted the same way and a game
// counts as started when its first real putt opens it. Test putts and made-up data send nothing.
//
// Nothing is sent from the editor, with made-up data (-app2Fixture) or with play held (-app2HoldPlay).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PuttrApp
{
    public static class Analytics
    {
        public const string AppName = "puttr2";
        const string DevicePref = "app2.analytics.device";
        const int Batch = 50, Most = 400;
        const float Every = 20f;
        const double IdleSeconds = 30 * 60;

        static readonly List<object> Waiting = new List<object>();
        static Dictionary<string, object> _context;
        static string _session, _screen, _gate;
        static double _sessionBegan, _lastActive;
        static float _nextSend;
        static bool _begun, _sending;

        /// <summary>Nothing is noted or sent: the editor, made-up data, or play held on the device.</summary>
        public static bool Off => Application.isEditor || Fixtures.On || Api.HoldPlay;

        /// <summary>This install's id (made up on first launch) and this session's.</summary>
        public static string DeviceId { get; private set; }
        public static string SessionId => _session;
        /// <summary>The screen that is up, as events carry it.</summary>
        public static string CurrentScreen => _screen;

        static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        /// <summary>Called once the app is up (App.Begin).</summary>
        public static void Begin()
        {
            if (_begun) return;
            _begun = true;
            DeviceId = PlayerPrefs.GetString(DevicePref, "");
            if (DeviceId.Length == 0)
            {
                DeviceId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DevicePref, DeviceId);
                PlayerPrefs.Save();
            }
            _context = Context();
            _nextSend = Time.unscaledTime + 5f;
            NewSession();
            Track("app_opened");
        }

        // ── What this device is ──────────────────────────────────────────────

        /// <summary>"tv", "tablet", "mobile" or "desktop".</summary>
        public static string DeviceClass(bool tv, bool tablet, bool mobilePlatform) => tv ? "tv" : tablet ? "tablet" : mobilePlatform ? "mobile" : "desktop";

        /// <summary>The platform as the Puttr app names it ("ios", "android"), and the TVs and the desktop besides.</summary>
        public static string PlatformName(string tvPlatform, RuntimePlatform platform)
        {
            if (!string.IsNullOrEmpty(tvPlatform)) return tvPlatform;   // "tvos", "androidtv", "web"
            switch (platform)
            {
                case RuntimePlatform.IPhonePlayer: return "ios";
                case RuntimePlatform.Android: return "android";
                case RuntimePlatform.tvOS: return "tvos";
                case RuntimePlatform.WebGLPlayer: return "web";
                case RuntimePlatform.OSXPlayer: case RuntimePlatform.OSXEditor: return "macos";
                case RuntimePlatform.WindowsPlayer: case RuntimePlatform.WindowsEditor: return "windows";
                default: return platform.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// The OS by name, from what the device says of itself (SystemInfo.operatingSystem, deviceModel): "iOS",
        /// "iPadOS", "tvOS", "Android", "Android TV", "Fire OS" (an Amazon device), "Tizen" (the Samsung web build), "macOS".
        /// </summary>
        public static string OsName(string platform, string operatingSystem, string deviceModel)
        {
            string os = operatingSystem ?? "", model = deviceModel ?? "";
            bool amazon = model.StartsWith("Amazon", StringComparison.OrdinalIgnoreCase);
            switch (platform)
            {
                case "tvos": return "tvOS";
                case "androidtv": return amazon ? "Fire OS" : "Android TV";
                case "web": return "Tizen";
                case "ios": return os.StartsWith("iPadOS", StringComparison.OrdinalIgnoreCase) || model.StartsWith("iPad", StringComparison.OrdinalIgnoreCase) ? "iPadOS" : "iOS";
                case "android": return amazon ? "Fire OS" : "Android";
                case "macos": return "macOS";
                case "windows": return "Windows";
                default: return os.Length > 0 ? os.Split(' ')[0] : platform;
            }
        }

        /// <summary>"27.1" from "iOS 27.1", "14" from "Android OS 14 / API-34 (…)", "15.1" from "Mac OS X 15.1.0".</summary>
        public static string OsVersion(string operatingSystem)
        {
            Match m = Regex.Match(operatingSystem ?? "", @"(\d+)(\.\d+)?");
            return m.Success ? m.Value : "";
        }

        /// <summary>The store build number. Unity keeps none at run time, so it is blank unless something sets it; the version is always sent.</summary>
        public static string Build = "";

        static Dictionary<string, object> Context()
        {
            string platform = PlatformName(Tv.Platform, Application.platform);
            return new Dictionary<string, object>
            {
                { "app", AppName },
                { "platform", platform },
                { "device_class", DeviceClass(Tv.On, App.Tablet, Application.isMobilePlatform) },
                { "os", OsName(platform, SystemInfo.operatingSystem, SystemInfo.deviceModel) },
                { "os_version", OsVersion(SystemInfo.operatingSystem) },
                { "device_model", SystemInfo.deviceModel },
                { "locale", System.Globalization.CultureInfo.CurrentCulture.Name },
                { "device_id", DeviceId },
                { "app_version", Application.version },
                { "build_number", Build },
            };
        }

        /// <summary>The device fields a request carries (events, and the feedback card's answer), with this session's id.</summary>
        public static Dictionary<string, object> Device()
        {
            var d = new Dictionary<string, object>(_context ?? (_context = Context()));
            d["session_id"] = _session ?? "";
            return d;
        }

        // ── Sessions ─────────────────────────────────────────────────────────

        static void NewSession()
        {
            _session = Guid.NewGuid().ToString("N");
            _sessionBegan = _lastActive = Now;
        }

        /// <summary>The app went to the back or came to the front (App.OnApplicationPause).</summary>
        public static void Paused(bool paused)
        {
            if (!_begun) return;
            if (paused)
            {
                Track("app_backgrounded", "session_duration_ms", (long)((Now - _sessionBegan) * 1000));
                _lastActive = Now;
                Flush();
                return;
            }
            // Back after a long while: a new session, and an open to start it.
            if (Now - _lastActive > IdleSeconds) { NewSession(); Track("app_opened"); }
            _lastActive = Now;
        }

        // ── Noting things ────────────────────────────────────────────────────

        /// <summary>Note that something happened. fields are name, value, name, value…</summary>
        public static void Track(string name, params object[] fields) => Note(name, null, null, Fields(fields));

        /// <summary>An event that is a step of something ("game", "started"): named activity_phase, and carrying both
        /// on their own so a chart can group by either (the Puttr app's trackActivity).</summary>
        public static void Activity(string activity, string phase, params object[] fields) => Note(activity + "_" + phase, activity, phase, Fields(fields));

        /// <summary>A Club feature a non-member reached: the pair the Puttr app's trackGate sends. The feature is
        /// carried on to the purchase events that follow.</summary>
        public static void Gate(string feature)
        {
            _gate = feature;
            Track("feature_blocked", "feature", feature);
            Track("membership_modal_opened", "trigger_feature", feature);
        }

        /// <summary>A step of buying Puttr Club: "membership_purchase_initiated", "_succeeded" or "_failed".</summary>
        public static void Purchase(string step, string sku, string error = null)
        {
            var fields = new List<object> { "sku", sku };
            if (_gate != null) { fields.Add("trigger_feature"); fields.Add(_gate); }
            if (error != null) { fields.Add("error"); fields.Add(error); }
            Track("membership_purchase_" + step, fields.ToArray());
            if (step != "initiated") _gate = null;
        }

        /// <summary>The name a screen goes by: its class less "Screen" ("TournamentsScreen" is "Tournaments").</summary>
        public static string ScreenName(Type type)
        {
            string name = type != null ? type.Name : "";
            return name.EndsWith("Screen", StringComparison.Ordinal) && name.Length > 6 ? name.Substring(0, name.Length - 6) : name;
        }

        /// <summary>A screen came to the front (App.Swap). The same screen twice in a row is noted once.</summary>
        public static void Viewed(string name)
        {
            if (string.IsNullOrEmpty(name) || name == _screen) return;
            _screen = name;
            Track("screen_viewed");
        }

        static Dictionary<string, object> Fields(object[] fields)
        {
            var properties = new Dictionary<string, object>();
            if (fields != null) for (int i = 0; i + 1 < fields.Length; i += 2) if (fields[i] is string key && fields[i + 1] != null) properties[key] = fields[i + 1];
            return properties;
        }

        static void Note(string name, string activity, string phase, Dictionary<string, object> properties)
        {
            if (!_begun || Off) return;
            _lastActive = Now;
            var e = new Dictionary<string, object>
            {
                { "event", name }, { "at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, { "screen", _screen ?? "" }, { "properties", properties },
            };
            if (activity != null) { e["activity"] = activity; e["phase"] = phase; }
            if (Waiting.Count >= Most) Waiting.RemoveAt(0);   // long offline: the oldest go
            Waiting.Add(e);
        }

        // ── Games: started, completed, abandoned ─────────────────────────────

        sealed class Round { public string GameId, Type, Title, Mode; public int Players; public double Began; }

        static readonly Dictionary<string, Round> Rounds = new Dictionary<string, Round>();
        static string _type, _title, _mode, _lastRound;
        static int _completed;

        /// <summary>Games finished since the app started (the feedback card asks after a few).</summary>
        public static int CompletedThisRun => _completed;
        /// <summary>The game last finished, by name.</summary>
        public static string LastGame { get; private set; }
        /// <summary>Raised when a game is finished and its score taken by the API.</summary>
        public static Action GameCompleted;

        /// <summary>What is about to be played, so the round the API opens next is known by name: its gameType,
        /// its title where there is one, and how it is played ("game", "tournament", "league", "coach", "tracks"…).</summary>
        public static void Playing(string gameType, string title, string mode)
        {
            _type = gameType ?? "";
            _title = title ?? "";
            _mode = string.IsNullOrEmpty(mode) ? "game" : mode;
        }

        /// <summary>The Player picked a game to play (its page or its tile): the top of the funnel.</summary>
        public static void Selected(object game, string source)
        {
            _type = J.Str(game, "gameType").ToUpperInvariant();
            _title = J.Str(game, "title");
            _mode = "game";
            Track("game_selected", "game_id", J.Str(game, "id"), "game_type", _type, "game", _title, "source", source ?? "");
        }

        /// <summary>What a play record's path means: "started", "completed", "abandoned", or null.</summary>
        public static string PhaseOf(string method, string path)
        {
            if (method != "POST" || path == null) return null;
            string p = path.TrimEnd('/').ToLowerInvariant();
            switch (p)
            {
                case "/game/new": case "/tournaments/game/new": case "/coach/session-start": case "/multiplayer/game/create": return "started";
                case "/game/over": case "/game/multiplayer/over": case "/tournaments/game/end": case "/coach/session-complete": case "/multiplayer/game/over": return "completed";
                case "/game/abandon": case "/multiplayer/game/delete": return "abandoned";
                default: return null;
            }
        }

        /// <summary>The API took a call (Api.Call, on a 2xx from the real API only): a play record opens or closes a game.</summary>
        public static void PlayRecord(string method, string path, string body, object answer)
        {
            string phase = PhaseOf(method, path);
            if (phase == null || !_begun || Off) return;
            object sent = body != null ? J.Parse(body) : null;
            string p = path.ToLowerInvariant();
            if (phase == "started")
            {
                string id = J.Str(answer, "id");
                if (id.Length == 0) id = J.Str(answer, "userGameId");
                if (id.Length == 0) id = J.Str(answer, "gameId");
                if (id.Length == 0 || id == "0") return;
                string mode = p.StartsWith("/tournaments/") ? "tournament" : p.StartsWith("/coach/") ? "coach" : p.StartsWith("/multiplayer/") ? "live" : _mode ?? "game";
                var round = new Round { GameId = J.Str(sent, "gameId"), Type = _type ?? "", Title = _title ?? "", Mode = mode, Players = Math.Max(1, J.Int(sent, "players", 1)), Began = Now };
                Rounds[id] = round;
                _lastRound = id;
                Activity("game", "started", "round_id", id, "game_id", round.GameId, "game_type", round.Type, "game", round.Title, "mode", round.Mode, "players", round.Players);
                return;
            }
            // A tournament round and the coach's session close without naming the row: it is the one just opened.
            string roundId = p.StartsWith("/tournaments/") || p.StartsWith("/coach/") ? _lastRound : J.Str(sent, "gameId");
            if (string.IsNullOrEmpty(roundId) || !Rounds.TryGetValue(roundId, out Round r)) return;   // not one this run opened
            Rounds.Remove(roundId);
            var fields = new List<object>
            {
                "round_id", roundId, "game_id", r.GameId, "game_type", r.Type, "game", r.Title, "mode", r.Mode, "players", r.Players,
                "seconds", (int)Math.Round(Now - r.Began),
            };
            if (phase == "completed" && J.Get(sent, "score") != null) { fields.Add("score"); fields.Add(J.Num(sent, "score")); }
            Activity("game", phase, fields.ToArray());
            if (phase != "completed") return;
            _completed++;
            LastGame = r.Title.Length > 0 ? r.Title : r.Type;
            Flush();
            GameCompleted?.Invoke();
        }

        // ── Sending ──────────────────────────────────────────────────────────

        /// <summary>Send what is waiting now, not at the next round.</summary>
        public static void Flush() => _nextSend = 0f;

        /// <summary>Every frame (App.Update): costs nothing while there is nothing to send.</summary>
        public static void Tick()
        {
            if (!_begun || _sending || Waiting.Count == 0 || Time.unscaledTime < _nextSend) return;
            if (Off) { Waiting.Clear(); return; }
            App.Run(Send());
        }

        static IEnumerator Send()
        {
            _sending = true;
            int n = Mathf.Min(Batch, Waiting.Count);
            var body = new Dictionary<string, object> { { "context", Device() }, { "events", Waiting.GetRange(0, n) } };
            string token = null;
            if (Auth.SignedIn) yield return Auth.Token(t => token = t);
            long status = 0;
            yield return Api.Send("POST", Config.ApiUrl + "/events/batch", J.Write(body), token, (s, _) => status = s);
            // Taken, or refused for good (a 4xx will not get better by asking again): let them go.
            // No answer or a 5xx: keep them for the next round.
            if ((status >= 200 && status < 300) || (status >= 400 && status < 500)) Waiting.RemoveRange(0, Mathf.Min(n, Waiting.Count));
            _nextSend = Time.unscaledTime + Every;
            _sending = false;
        }
    }
}
