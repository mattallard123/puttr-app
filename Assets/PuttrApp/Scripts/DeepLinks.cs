// Deep and universal links (puttr-app components/common/DeepLinkHandler.js). The same links open the app:
//   https://<host>/w/...                          (api.puttr.co and app.puttr.co)
//   https://app.puttr.co/game|tournament|play|improve|league|versus/...
//   puttr://...
//   https://trk.puttr.co/u/...                    Klaviyo's tracking link: followed to where it goes
// and go where the app sends them. Only for a signed-in Player: a link that arrives first waits.
// iOS: Associated Domains + the puttr scheme (Editor/IosSignIn.cs). Android: intent filters on Unity's
// activity (Plugins/Android/PuttrApp.androidlib/AndroidManifest.xml).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace PuttrApp
{
    public static class DeepLinks
    {
        static App _app;
        static string _pending, _last;
        static float _lastAt = -10f;
        static bool _hooked;

        public static void Init(App app)
        {
            _app = app;
            if (_hooked) return;
            _hooked = true;
            Application.deepLinkActivated += Arrived;
            if (!string.IsNullOrEmpty(Application.absoluteURL)) Arrived(Application.absoluteURL);
        }

        /// <summary>Once signed in and on a tab: a link that came before.</summary>
        public static void Ready() { if (_pending != null) { string url = _pending; _pending = null; App.Run(Handle(url)); } }

        static void Arrived(string url)
        {
            // The same link twice in 3 seconds is one tap (a cold start can deliver it twice).
            if (url == _last && Time.realtimeSinceStartup - _lastAt < 3f) return;
            _last = url;
            _lastAt = Time.realtimeSinceStartup;
            if (_app == null || _app.Profile == null || !Auth.SignedIn) { _pending = url; return; }
            App.Run(Handle(url));
        }

        /// <summary>parseDeepLink: the path's parts, or null for a link the app does not open.</summary>
        public static List<string> Parse(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            Match m = Regex.Match(url, @"^https?://[^/]+/w/(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(url, @"^https?://app\.puttr\.co/((?:game|tournament|play|improve|league|versus)(?:/.*)?)$", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(url, @"^puttr://(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string path = Regex.Replace(m.Groups[1].Value, @"[?#].*$", "");
            var parts = new List<string>();
            foreach (string s in path.Split('/')) if (s.Length > 0) parts.Add(UnityWebRequest.UnEscapeURL(s));
            return parts.Count > 0 ? parts : null;
        }

        static IEnumerator Handle(string url)
        {
            // Klaviyo's tracking links: follow the redirects to the real link.
            if (Regex.IsMatch(url, @"^https?://trk\.puttr\.co/u/", RegexOptions.IgnoreCase))
            {
                string at = url;
                for (int i = 0; i < 6; i++)
                {
                    using (UnityWebRequest req = UnityWebRequest.Get(at))
                    {
                        req.redirectLimit = 0;
                        req.timeout = 10;
                        yield return req.SendWebRequest();
                        string next = req.GetResponseHeader("Location");
                        if (string.IsNullOrEmpty(next)) break;
                        at = next.StartsWith("http") ? next : new Uri(new Uri(at), next).ToString();
                    }
                }
                if (Parse(at) == null) { Application.OpenURL(at); yield break; }
                url = at;
            }
            List<string> p = Parse(url);
            if (p == null) yield break;
            App app = _app;
            string id = p.Count > 1 ? p[1] : "";
            switch (p[0].ToLowerInvariant())
            {
                case "game": if (id.Length > 0) app.Push(new GameViewScreen(app, id)); break;
                case "tournament":
                    if (id.Length == 0) { app.Push(new TournamentsScreen(app)); break; }
                    App.Run(Api.Get("/tournaments/details/" + id, d =>
                    {
                        object row = J.First(d) ?? d;
                        if (J.Get(row, "id") == null && row is Dictionary<string, object> r) r["id"] = id;
                        app.Push(new TournamentScreen(app, row));
                    }, _ => app.Push(new TournamentsScreen(app))));
                    break;
                case "play":
                    if (id.Length == 0) { app.ShowTabs("play"); break; }
                    App.Run(Api.Get("/games/ALL", d =>
                    {
                        object game = J.List(d).Find(g => J.Str(g, "id") == id);
                        if (game != null) app.Push(new GameScreen(app, game)); else app.ShowTabs("play");
                    }, _ => app.ShowTabs("play")));
                    break;
                case "improve": app.ShowTabs("improve"); break;
                case "league": app.Push(new LeagueScreen(app)); break;
                case "versus": app.Push(new VersusInboxScreen(app)); break;
                case "coach": if (id.Length > 0) App.Run(CoachInvite(app, id)); break;
                case "checkin": if (id.Length > 0) App.Run(Checkin(app, id)); break;
                case "tv": if (id.Length > 0) App.Run(TvClaim(app, id)); break;
            }
        }

        // A coach's invite: who it is from, then follow them.
        static IEnumerator CoachInvite(App app, string code)
        {
            object invite = null;
            yield return Api.Get("/coach/invite/resolve/" + UnityWebRequest.EscapeURL(code), d => invite = d, _ => { });
            if (invite == null) { app.Toast("That invite link has expired."); yield break; }
            string coach = J.Str(J.Get(invite, "coach") ?? invite, "displayName", "your coach");
            Dialog.Ask(app.Toasts, "Connect with " + coach + "?", coach + " will be able to see your putting and send you plans.", "Connect",
                () => App.Run(Api.Post("/coach/invite/redeem", new Dictionary<string, object> { { "code", code } }, _ => app.Toast("You are connected with " + coach + "."), _ => app.Toast("Couldn't connect. Try again."))));
        }

        // A studio check-in.
        static IEnumerator Checkin(App app, string token)
        {
            object check = null;
            yield return Api.Get("/coach/checkin/resolve/" + UnityWebRequest.EscapeURL(token), d => check = d, _ => { });
            if (check == null) { app.Toast("That check-in link has expired."); yield break; }
            // A venue's Puttr (Venue Mode): the member takes a seat under their own name.
            if (J.Flag(check, "venue") || J.Get(check, "venue") is bool at && at)
            {
                string venue = J.Str(J.Get(check, "coach"), "displayName", "this venue");
                Dialog.Ask(app.Toasts, "Play as you?", "Take a seat at " + venue + " under your own name. Your putts and games there save to your profile.", "Join",
                    () => App.Run(Api.Post("/coach/checkin/claim", new Dictionary<string, object> { { "token", token } }, _ => app.Toast("You're in. Look up at the screen."), _ => app.Toast("Couldn't join. Try again."))));
                yield break;
            }
            Dialog.Ask(app.Toasts, "Check in?", "Check in to " + J.Str(check, "locationName", J.Str(check, "coachName", "this studio")) + ".", "Check in",
                () => App.Run(Api.Post("/coach/checkin/claim", new Dictionary<string, object> { { "token", token } }, _ => app.Toast("Checked in."), _ => app.Toast("Couldn't check in. Try again."))));
        }

        // Signing in a Puttr TV from its QR code.
        static IEnumerator TvClaim(App app, string code)
        {
            object tv = null;
            yield return Api.Get("/tv/resolve/" + UnityWebRequest.EscapeURL(code), d => tv = d, _ => { });
            if (tv == null) { app.Toast("That TV code has expired. Show a new one on the TV."); yield break; }
            Dialog.Ask(app.Toasts, "Sign in on " + J.Str(tv, "name", "the TV") + "?", "The TV will play as you.", "Sign in",
                () => App.Run(Api.Post("/tv/claim", new Dictionary<string, object> { { "code", code }, { "greenName", "puttr" } }, _ => app.Toast("The TV is signed in."), _ => app.Toast("Couldn't sign in the TV."))));
        }
    }
}
