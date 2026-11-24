// The forced update gate, as puttr-app's App.tsx: when the store has a newer version than this build, the whole
// app sits behind an "Update required" screen that cannot be closed, with one button to the store page.
//
// The check is the same one the React Native app makes, per platform:
//   iOS      https://itunes.apple.com/lookup?bundleId=co.puttr.ios, results[0].version
//   Android  the Play Store page for co.puttr.android, read the way react-native-version-check's playStore
//            provider reads it (the two patterns below)
// and the same comparison (isOutdated: number by number, never as text, so 2.10.0 is newer than 2.9.0).
//
// It runs at launch and every time the app comes back to the front (App.cs calls Check). It never holds the app
// up: the app starts as usual while the answer is on its way, and a check that fails, times out or cannot be read
// changes nothing. Only a clear "the store is newer" puts the screen up.
//
// Not gated, as in the React Native app: development builds (its __DEV__), and the TV apps (tv/App.js has no
// gate). Also never in the editor, on a desktop build, or with made-up data (Fixtures.On).
using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class UpdateGate
    {
        // The same pages puttr-app opens (App.tsx openStoreLink).
        const string AppStoreUrl = "https://apps.apple.com/us/app/puttrco/id1528616307";
        const string PlayStoreUrl = "https://play.google.com/store/apps/details?id=co.puttr.android";
        const string AppStoreLookup = "https://itunes.apple.com/lookup?bundleId=co.puttr.ios";
        const string PlayStorePage = "https://play.google.com/store/apps/details?id=co.puttr.android&hl=en&gl=US";

        /// <summary>The update screen is up: the store has a newer version than this build.</summary>
        public static bool Required { get; private set; }

        const string CurrentPref = "app2.update.current", CurrentAtPref = "app2.update.currentAt";
        const long RecheckSeconds = 6 * 3600;

        static bool _checking;
        static float _checkedAt = -1000f;
        static GameObject _view;
        static PanelSettings _panel;

        /// <summary>True where the React Native app gates: a phone or tablet build from a store, never a TV.</summary>
        static bool Gated
        {
            get
            {
#if UNITY_EDITOR
                return false;
#elif UNITY_IOS || UNITY_ANDROID
                // A development build's version trails the store's, so the check would flag it (App.tsx: __DEV__).
                return !Debug.isDebugBuild && !Fixtures.On && !Tv.On;
#else
                return false;
#endif
            }
        }

        /// <summary>Ask the store for its version. Called at launch and whenever the app returns to the front.</summary>
        public static void Check()
        {
            if (!Gated || _checking || App.I == null) return;
            // Android reports focus again after every system dialog and the keyboard: one look every few seconds is plenty.
            if (Time.realtimeSinceStartup - _checkedAt < 5f) return;
            // The store said this build was current not long ago: not again for a few hours. (On Android the answer is
            // the whole Play Store page, downloaded and searched; it used to be fetched every time the app came to the
            // front.) While the update screen is up it is asked every time, so updating lets the Player back in.
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!Required && PlayerPrefs.GetString(CurrentPref, "") == Application.version
                && long.TryParse(PlayerPrefs.GetString(CurrentAtPref, "0"), out long at) && now >= at && now - at < RecheckSeconds) return;
            _checkedAt = Time.realtimeSinceStartup;
            _checking = true;
            App.Run(Ask());
        }

        static IEnumerator Ask()
        {
            string latest = null;
            bool answered = false;
#if UNITY_ANDROID
            using (UnityWebRequest req = UnityWebRequest.Get(PlayStorePage))
            {
                // The header react-native-version-check sends. Unity may refuse to set it: the page answers without it.
                try { req.SetRequestHeader("sec-fetch-site", "same-origin"); } catch (Exception) { }
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    answered = true;
                    try { latest = PlayStoreVersion(req.downloadHandler.text); } catch (Exception) { latest = null; }
                }
            }
#else
            using (UnityWebRequest req = UnityWebRequest.Get(AppStoreLookup))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    answered = true;
                    try { latest = J.Str(J.First(J.Get(J.Parse(req.downloadHandler.text), "results")), "version", null); } catch (Exception) { latest = null; }
                }
            }
#endif
            _checking = false;
            // No answer (offline, slow, an error page): leave things as they are. An answer with no version in it
            // counts as "not outdated", as isOutdated does for a missing version.
            if (!answered) yield break;
            bool outdated = IsOutdated(Application.version, latest);
            if (!outdated)
            {
                PlayerPrefs.SetString(CurrentPref, Application.version);
                PlayerPrefs.SetString(CurrentAtPref, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            }
            Show(outdated);
        }

        /// <summary>The version on a Play Store page: the old layout's "Current Version" first, then the new layout's
        /// [[["2.8.6"]] (react-native-version-check, providers/playStore.js).</summary>
        public static string PlayStoreVersion(string page)
        {
            if (string.IsNullOrEmpty(page)) return null;
            Match old = Regex.Match(page, @"Current Version.+?>([\d.-]+)</span>");
            if (old.Success) return old.Groups[1].Value.Trim();
            Match now = Regex.Match(page, @"\[\[\[""([\d\-.]+?)""\]\]");
            return now.Success ? now.Groups[1].Value.Trim() : null;
        }

        /// <summary>App.tsx isOutdated: true when the store's version is newer than this one, compared part by part as numbers.</summary>
        public static bool IsOutdated(string current, string latest)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(latest)) return false;
            string[] c = current.Split('.'), l = latest.Split('.');
            int length = Math.Max(c.Length, l.Length);
            for (int i = 0; i < length; i++)
            {
                int cv = i < c.Length ? LeadingNumber(c[i]) : 0;
                int lv = i < l.Length ? LeadingNumber(l[i]) : 0;
                if (lv > cv) return true;
                if (lv < cv) return false;
            }
            return false;
        }

        /// <summary>JavaScript's parseInt(text, 10) || 0: the digits a part starts with, or 0 when it starts with none.</summary>
        static int LeadingNumber(string part)
        {
            part = part.Trim();
            int i = 0, sign = 1;
            if (part.Length > 0 && (part[0] == '-' || part[0] == '+')) { if (part[0] == '-') sign = -1; i = 1; }
            long n = 0;
            for (; i < part.Length && part[i] >= '0' && part[i] <= '9' && n < 100000000; i++) n = n * 10 + (part[i] - '0');
            return (int)(sign * n);
        }

        static void Show(bool required)
        {
            Required = required;
            if (!required)
            {
                if (_view != null) UnityEngine.Object.Destroy(_view);
                if (_panel != null) UnityEngine.Object.Destroy(_panel);
                _view = null;
                _panel = null;
                return;
            }
            if (_view != null || App.I == null) return;

            // A panel of its own, above everything: the app's screens and sheets, and a game's own interface, which
            // draws over the app's panel. The React Native gate replaces the whole app, a game in progress included.
            _panel = UnityEngine.Object.Instantiate(Resources.Load<PanelSettings>("PuttrApp/Panel"));
            _panel.sortingOrder = 1000;
            _view = new GameObject("Update required");
            _view.transform.SetParent(App.I.transform, false);
            _view.SetActive(false);
            var document = _view.AddComponent<UIDocument>();
            document.panelSettings = _panel;
            _view.SetActive(true);

            // The same words as App.tsx, drawn as the design guidelines have it: the page colour, a title, a sentence
            // and the one main button. The panel is this screen's own, so it takes the app's style sheet itself.
            VisualElement root = document.rootVisualElement;
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/App");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.style.backgroundColor = Ui.Base;
            root.style.color = Color.white;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.paddingLeft = 32; root.style.paddingRight = 32; root.style.paddingTop = 32; root.style.paddingBottom = 32;
            root.pickingMode = PickingMode.Position;   // nothing underneath takes a tap

            Label title = Ui.Title("Update required", "title center");
            title.style.marginBottom = 12;

            Label body = Ui.Text("A new version of Puttr is available. Please update to keep playing.", "body center", Ui.Regular);
            body.style.color = Ui.Soft;
            body.style.maxWidth = 420;
            body.style.marginBottom = 24;

            VisualElement button = Ui.Button("Update now", OpenStore);
            button.style.marginTop = 0;
            button.style.width = Length.Percent(100);
            button.style.maxWidth = 420;

            root.Add(title);
            root.Add(body);
            root.Add(button);
            Perf.Wake();
        }

        static void OpenStore()
        {
            Perf.Wake();
#if UNITY_ANDROID
            Application.OpenURL(PlayStoreUrl);
#else
            Application.OpenURL(AppStoreUrl);
#endif
        }
    }
}
