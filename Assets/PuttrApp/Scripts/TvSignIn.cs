// Signing a TV in, as puttr-tv's SignInScreen: a /tv/session code drawn as a QR. The Player scans it with their
// phone, the Puttr app approves, and the poll hands back a sign-in token (and the phone's green, which the TV then
// looks for on the wifi). After a moment of "Welcome", the Play tab.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class TvSignInScreen : AppScreen
    {
        const string PrefCode = "app2.tv.code";
        readonly App _app;
        readonly VisualElement _right;
        readonly Label _green, _greenSub;
        Coroutine _flow;

        /// <summary>The accepted code: this TV's session, retired on sign out.</summary>
        public static string Code { get => PlayerPrefs.GetString(PrefCode, ""); set => PlayerPrefs.SetString(PrefCode, value ?? ""); }

        public TvSignInScreen(App app)
        {
            _app = app;
            var logo = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoHeader"), scaleMode = ScaleMode.ScaleToFit };
            logo.AddToClassList("tv-signin-logo");
            _green = Ui.Text("", "sub", Ui.Medium);
            _greenSub = Ui.Text("", "muted", Ui.Regular);
            VisualElement left = Ui.Box("tv-signin-left",
                logo,
                Ui.Text("Sign in with your phone", "tv-signin-title", Ui.Bold),
                Step(1, "Open the camera on your phone"),
                Step(2, "Scan the code"),
                Step(3, "Approve on the Puttr app"),
                Ui.Box("row tv-signin-green", Ui.Icon("WifiHigh", 22, Ui.Bright), Ui.Box("grow", _green, _greenSub)));
            _right = Ui.Box("tv-signin-right");
            Root = Ui.Box("tv-signin", left, _right);
            Root.schedule.Execute(ShowGreen).Every(1000);
            ShowGreen();
            _flow = App.Run(Flow());
        }

        static VisualElement Step(int n, string words) =>
            Ui.Box("row tv-signin-step", Ui.Box("tv-signin-num", Ui.Text(n.ToString(), "tv-signin-num-t", Ui.Bold)), Ui.Text(words, "tv-signin-step-t", Ui.Medium));

        // The green, over Bluetooth first and then the wifi (Green.Console starts looking as soon as it is asked for).
        void ShowGreen()
        {
            bool radio = Puttr.PuttrBluetooth.Supported;   // not the web (Samsung) build
            string waiting = radio ? "Turn your Puttr on near this TV" : "Have your Puttr on the same wifi as this TV";
            if (Fixtures.On) { _green.text = waiting; return; }
            Puttr.PuttrConsole c = Green.Console;
            bool on = c.Online;
            _green.text = on ? (c.Bluetooth.Connected ? "Puttr connected over Bluetooth" : "Puttr connected on the wifi") : waiting;
            _greenSub.text = on ? "" : radio ? "Too far for Bluetooth? Put it on this wifi: Puttr app, Profile, Settings, Connect Puttr to Wifi"
                                             : "On your Puttr app: Profile, Settings, Connect Puttr to Wifi";
            _greenSub.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public override void Hidden() { if (_flow != null) _app.StopCoroutine(_flow); _flow = null; }

        IEnumerator Flow()
        {
            while (true)
            {
                _right.Clear();
                _right.Add(Spinner.Big());
                string device = SystemInfo.deviceUniqueIdentifier;
                var body = new Dictionary<string, object> { { "deviceId", device }, { "deviceName", SystemInfo.deviceName }, { "platform", Tv.Platform } };
                long status = 0;
                string text = null;
                yield return Api.Send("POST", Config.ApiUrl + "/tv/session", J.Write(body), Config.TvToken, (s, t) => { status = s; text = t; });
                object made = J.Parse(text);
                string code = J.Str(made, "code"), link = J.Str(made, "link");
                if (status != 200 || code.Length == 0 || link.Length == 0)
                {
                    _right.Clear();
                    _right.Add(Ui.Text(status == 0 ? "No internet. Check the TV's connection." : "Could not reach Puttr. Trying again.", "sub center wrap", Ui.Medium));
                    yield return new WaitForSecondsRealtime(5f);
                    continue;
                }
                ShowCode(link, J.Str(made, "userCode"));
                float interval = Mathf.Clamp((float)J.Num(made, "interval", 3), 2f, 10f);
                float until = Time.realtimeSinceStartup + (float)J.Num(made, "expiresIn", 600);
                bool restart = false;
                while (!restart && Time.realtimeSinceStartup < until)
                {
                    yield return new WaitForSecondsRealtime(interval);
                    yield return Api.Send("GET", Config.ApiUrl + "/tv/session/" + code + "?deviceId=" + UnityWebRequest.EscapeURL(device), null, Config.TvToken, (s, t) => { status = s; text = t; });
                    object poll = J.Parse(text);
                    string state = J.Str(poll, "status"), token = J.Str(poll, "accessToken");
                    if (state == "ACCEPTED" && token.Length > 0)
                    {
                        object player = J.Get(poll, "player");
                        Welcome(player, "Signing in");
                        string problem = null;
                        yield return Auth.SignInWithCustomToken(token, p => problem = p);
                        if (problem != null) { _app.Toast("Sign-in did not complete. Scan the new code to try again."); restart = true; break; }
                        Code = code;
                        string green = J.Str(poll, "greenName");
                        if (green.Length > 0) Tv.GreenName = green;   // the phone's green: looked for by this name from here on
                        Welcome(player, null);
                        yield return new WaitForSecondsRealtime(1.8f);
                        _flow = null;
                        Perf.Mark("signed in (TV)");
                        _app.ShowTabs("play");
                        yield break;
                    }
                    // ACCEPTED with no token: another TV took it. Expired or signed out: a fresh code.
                    if (state == "EXPIRED" || state == "SIGNED_OUT" || state == "ACCEPTED") restart = true;
                }
            }
        }

        void ShowCode(string link, string userCode)
        {
            _right.Clear();
            var qr = new Image { image = QrCode.Texture(link, 10), scaleMode = ScaleMode.ScaleToFit };
            qr.AddToClassList("tv-qr");
            _right.Add(Ui.Box("tv-qr-card", qr));
            if (userCode.Length > 0) _right.Add(Ui.Text("Or open " + ShortLink(link) + " and enter " + userCode, "muted center wrap", Ui.Regular).Margin(14));
        }

        static string ShortLink(string link)
        {
            string s = link.Replace("https://", "").Replace("http://", "");
            int slash = s.IndexOf('/');
            return slash > 0 ? s.Substring(0, slash) : s;
        }

        void Welcome(object player, string busy)
        {
            _right.Clear();
            string name = J.Str(player, "displayName", J.Str(player, "name", "Player"));
            VisualElement avatar = Ui.Avatar(name, J.Str(player, "photoURL"), true);
            avatar.AddToClassList("tv-welcome-avatar");
            _right.Add(avatar);
            _right.Add(Ui.Text(busy ?? "Welcome, " + name, "tv-signin-title center", Ui.Bold));
            if (busy == null) _right.Add(Ui.Text("SIGNED IN", "kick center", Ui.Bold).Margin(8));
            else _right.Add(Spinner.Small());
        }
    }

    /// <summary>The TV's Play tab corner: the green's status, and who is signed in (switch profile, sign out).</summary>
    public static class TvAccount
    {
        public static VisualElement Green(App app)
        {
            VisualElement dot = Ui.Box("dot");
            Label words = Ui.Text("Looking for your Puttr", "", Ui.Medium);
            VisualElement pill = Ui.Box("pill tv-pill", dot, words).OnTap(() => ConnectSheet.Open(app, dot));
            void Show()
            {
                string link = Fixtures.Connected ? "on" : Fixtures.On ? null : PuttrApp.Green.Console.Online ? "on" : null;
                dot.EnableInClassList("dot-on", link != null);
                words.text = link != null ? "Puttr connected" : "Looking for your Puttr";
            }
            pill.schedule.Execute(Show).Every(1000);
            Show();
            return pill;
        }

        public static VisualElement Button(App app)
        {
            VisualElement slot = Ui.Box("tv-account");
            void Draw()
            {
                slot.Clear();
                object p = app.Profile;
                string name = PlayerPicker.Short(p);
                VisualElement face = Profiles.Face(p ?? new Dictionary<string, object> { { "name", "?" } }, 40);
                slot.Add(Ui.Box("row tv-account-btn", face, Ui.Text(p != null ? name : "", "feed-name", Ui.Bold)).OnTap(() => Sheet(app, slot)));
            }
            Draw();
            app.TvProfileRead += Draw;
            slot.RegisterCallback<DetachFromPanelEvent>(_ => app.TvProfileRead -= Draw);
            return slot;
        }

        static void Sheet(App app, VisualElement over)
        {
            VisualElement dim = null;
            object p = app.Profile;
            VisualElement card = Ui.Box("modal tv-sheet", Profiles.Face(p ?? new Dictionary<string, object>(), 72), Ui.Text(PlayerPicker.Short(p), "h2 center", Ui.Bold).Margin(10));
            card.Add(Ui.Button("Switch profile", () => { dim.RemoveFromHierarchy(); Profiles.Sheet(app, over); }, "btn btn-ghost").Margin(14));
            card.Add(Ui.Button("Sign out of this TV", () => { dim.RemoveFromHierarchy(); SignOut(app); }, "btn btn-ghost").Margin(10));
            card.Add(Ui.Button("Close", () => dim.RemoveFromHierarchy(), "btn btn-ghost").Margin(10));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
        }

        static void SignOut(App app)
        {
            string code = TvSignInScreen.Code;
            if (code.Length > 0)
                App.Run(Api.Send("POST", Config.ApiUrl + "/tv/signout", J.Write(new Dictionary<string, object> { { "code", code } }), Config.TvToken, (s, t) => { }));
            TvSignInScreen.Code = "";
            app.SignedOut();
        }
    }
}
