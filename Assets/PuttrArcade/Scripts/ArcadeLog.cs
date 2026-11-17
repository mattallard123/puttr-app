// What happens in the arcade, sent to Puttr: which games get played, by how
// many Players, how they end, and each putt's speed and line.
//
// Events gather in a short queue and go out in batches to one endpoint,
// POST /arcade/events on the Puttr API, which stores them in a database of
// the arcade's own (puttr_arcade), apart from the Puttr app's data. Nothing
// here waits on the network or stops play: if a batch does not arrive it is
// kept and sent with the next.
//
// No names and no account: an install is known only by an id made up on its
// first launch.
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PuttrArcade
{
    public sealed class ArcadeLog : MonoBehaviour
    {
        const string Endpoint = "https://api.puttr.co/arcade/events";
        const string Key = "puttr-arcade-1"; // not a secret: it only marks the request as the arcade app's
        const int Batch = 50, Most = 400;
        const float Every = 20f;

        static ArcadeLog _log;
        readonly List<string> _waiting = new List<string>();
        string _install, _session, _app;
        bool _sending;
        float _nextSend;

        /// <summary>Start logging. Called once; without it every Event is dropped (the demo, the editor).</summary>
        public static void Begin(GameObject host)
        {
            if (_log != null) return;
            _log = host.AddComponent<ArcadeLog>();
            _log._install = PlayerPrefs.GetString("puttr.arcade.install", "");
            if (_log._install.Length == 0)
            {
                _log._install = System.Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString("puttr.arcade.install", _log._install);
                PlayerPrefs.Save();
            }
            _log._session = System.Guid.NewGuid().ToString("N");
            var app = new StringBuilder("{");
            Field(app, "version", Application.version);
            Field(app, "unity", Application.unityVersion);
            Field(app, "platform", Application.platform.ToString());
            Field(app, "device", SystemInfo.deviceModel);
            Field(app, "os", SystemInfo.operatingSystem);
            Field(app, "screen", Screen.width + "x" + Screen.height);
            Field(app, "language", Application.systemLanguage.ToString());
            _log._app = app.Append('}').ToString();
            _log._nextSend = Time.unscaledTime + 5f;
        }

        /// <summary>Note that something happened. fields are name, value, name, value…; a value is
        /// a string, a whole or decimal number, true or false, or an array of numbers.</summary>
        public static void Event(string type, params object[] fields)
        {
            if (_log == null) return;
            var json = new StringBuilder("{");
            Field(json, "type", type);
            Field(json, "at", System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            for (int i = 0; i + 1 < fields.Length; i += 2) Field(json, fields[i] as string, fields[i + 1]);
            json.Append('}');
            if (_log._waiting.Count >= Most) _log._waiting.RemoveAt(0); // long offline: the oldest go
            _log._waiting.Add(json.ToString());
        }

        /// <summary>Send what is waiting now, not at the next round (a game has ended; the app is going away).</summary>
        public static void Flush()
        {
            if (_log != null) _log._nextSend = 0f;
        }

        void Update()
        {
            if (_sending || _waiting.Count == 0 || Time.unscaledTime < _nextSend) return;
            StartCoroutine(Send());
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            Event("app_pause");
            Flush();
        }

        IEnumerator Send()
        {
            _sending = true;
            int n = Mathf.Min(Batch, _waiting.Count);
            var body = new StringBuilder("{");
            Field(body, "install", _install);
            Field(body, "session", _session);
            body.Append(",\"app\":").Append(_app).Append(",\"events\":[");
            for (int i = 0; i < n; i++) { if (i > 0) body.Append(','); body.Append(_waiting[i]); }
            body.Append("]}");

            using (var request = new UnityWebRequest(Endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("x-arcade-key", Key);
                request.timeout = 10;
                yield return request.SendWebRequest();
                long code = request.responseCode;
                // Taken, or refused for good (a 4xx will not get better by asking again): let them go.
                // No answer or a 5xx: keep them for the next round.
                if (request.result == UnityWebRequest.Result.Success || (code >= 400 && code < 500))
                    _waiting.RemoveRange(0, Mathf.Min(n, _waiting.Count));
            }
            _nextSend = Time.unscaledTime + Every;
            _sending = false;
        }

        // ── JSON, by hand: a few flat fields do not need a library ──────────

        static void Field(StringBuilder json, string name, object value)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (json.Length > 1) json.Append(',');
            json.Append('"').Append(name).Append("\":");
            Value(json, value);
        }

        static void Value(StringBuilder json, object value)
        {
            switch (value)
            {
                case null: json.Append("null"); break;
                case bool b: json.Append(b ? "true" : "false"); break;
                case int i: json.Append(i); break;
                case long l: json.Append(l); break;
                case float f: json.Append(float.IsNaN(f) || float.IsInfinity(f) ? "null" : System.Math.Round(f, 3).ToString(CultureInfo.InvariantCulture)); break;
                case double d: json.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : System.Math.Round(d, 3).ToString(CultureInfo.InvariantCulture)); break;
                case int[] list:
                    json.Append('[');
                    for (int k = 0; k < list.Length; k++) { if (k > 0) json.Append(','); json.Append(list[k]); }
                    json.Append(']');
                    break;
                default: Quote(json, value.ToString()); break;
            }
        }

        static void Quote(StringBuilder json, string text)
        {
            json.Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') json.Append('\\').Append(c);
                else if (c < ' ') json.Append(' ');
                else json.Append(c);
            }
            json.Append('"');
        }
    }
}
