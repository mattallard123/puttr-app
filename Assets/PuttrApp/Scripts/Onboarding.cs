// A new Player's first minute, as puttr-app:
//   ProfileSetup      a first sign-in makes the profile row: POST /profile/createSocial (Apple, Google:
//                     the account's name and photo) or /profile/create (email), then reads it back
//                     (Navigator.js waits for it the same way).
//   OnboardingScreen  OnboardingGuide, shown while the profile has no handedness: Welcome, display name
//                     (checked free), first name, which hand, putting goal, handicap, then connect your
//                     Puttr (or skip). POST /profile/save {displayName, firstName, handedness, persona, realHandicap}.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class ProfileSetup
    {
        /// <summary>The profile, made first if this account has none. done(null) if it could not be read or made.</summary>
        public static IEnumerator Ensure(Action<object> done)
        {
            object profile = null;
            bool failed = false;
            yield return Api.Get("/profile/", d => profile = J.First(d), _ => failed = true);
            if (profile != null || failed) { done(profile); yield break; }

            object claims = null;
            yield return Auth.Claims(c => claims = c);
            string provider = J.Str(J.Get(claims, "firebase"), "sign_in_provider", "password");
            string email = J.Str(claims, "email");
            if (provider == "google.com" || provider == "apple.com")
                yield return Api.Post("/profile/createSocial", new Dictionary<string, object>
                {
                    { "userId", Auth.Uid }, { "email", email }, { "displayName", J.Get(claims, "name") }, { "photoURL", J.Get(claims, "picture") },
                    { "accountType", provider }, { "source", "APP" },
                }, null, null, auth: false);
            else
                yield return Api.Post("/profile/create", new Dictionary<string, object> { { "userId", Auth.Uid }, { "email", email }, { "accountType", "firebase" }, { "source", "APP" } }, null, null, auth: false);

            // The row can take a moment to be readable: try for up to ten seconds.
            for (int i = 0; i < 5 && profile == null; i++)
            {
                if (i > 0) yield return new WaitForSecondsRealtime(2f);
                yield return Api.Get("/profile/", d => profile = J.First(d), _ => { });
            }
            done(profile);
        }

        public static bool NeedsOnboarding(object profile) => profile != null && J.Str(profile, "handedness").Length == 0 && !Kid.Managed(profile);
    }

    public sealed class OnboardingScreen : AppScreen
    {
        const int Steps = 6;
        static readonly (string id, string words, string icon)[] Goals = { ("GRINDER", "Improve my putting", "Target"), ("GAME NIGHTER", "Play with friends & family", "UsersThree"), ("E-SPORTER", "Compete online", "Trophy") };

        readonly App _app;
        readonly VisualElement _body;
        int _step;
        string _display, _first = "", _hand, _persona, _handicap;
        bool _nameOk = true, _checking, _saving, _showBle;
        int _asked;
        string _nameError = "";

        public OnboardingScreen(App app)
        {
            _app = app;
            _display = J.Str(app.Profile, "displayName");
            _first = J.Str(app.Profile, "firstName");
            _body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(_body);
            Root = scroll;
            Draw();
        }

        void Next() { _step = Mathf.Min(Steps, _step + 1); Draw(); }

        void Draw()
        {
            _body.Clear();
            VisualElement top = Ui.Box("row onboard-top");
            if (_step >= 1 && _step <= 5) top.Add(Ui.Text("‹", "back back-chevron", Ui.Medium).OnTap(() => { _step--; Draw(); }));
            if (_step > 0) top.Add(Ui.Box("grow", Ui.Progress(_step / (float)Steps)).Margin(0));
            _body.Add(Ui.Box("pad", top));
            VisualElement page = Ui.Box("pad onboard");
            _body.Add(page);
            switch (_step)
            {
                case 0:
                    var logo = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoHeader"), scaleMode = ScaleMode.ScaleToFit };
                    logo.AddToClassList("intro-logo");
                    page.Add(Ui.Box("center-box", logo, Ui.Text("Welcome to Puttr", "h1 center", Ui.Bold).Margin(20), Ui.Text("Let's set up your profile in\nunder a minute", "sub center", Ui.Regular).Margin(8)));
                    page.Add(Ui.Button("Let's go", Next).Margin(30));
                    break;
                case 1:
                    var name = Field("e.g. PuttKing42", _display);
                    Label state = Ui.Text(_checking ? "Checking..." : _nameError.Length > 0 ? _nameError : _nameOk && _display.Length > 0 ? "✓ Available" : "", "sub", Ui.Medium);
                    state.style.color = _nameError.Length > 0 ? Ui.Red : Ui.Bright;
                    name.RegisterValueChangedCallback(e => CheckName(e.newValue, state));
                    VisualElement go = Ui.Button("Continue", Next);
                    go.SetEnabled(_display.Trim().Length > 0 && _nameOk && !_checking);
                    page.Add(Ui.Text("Pick a display name", "h1", Ui.Bold));
                    page.Add(Ui.Text("This is how other players will see you", "sub", Ui.Regular).Margin(6));
                    page.Add(name.Margin(18));
                    page.Add(state);
                    page.Add(go.Margin(18));
                    break;
                case 2:
                    var first = Field("Your first name", _first);
                    VisualElement next = Ui.Button("Continue", Next);
                    next.SetEnabled(_first.Trim().Length > 0);
                    first.RegisterValueChangedCallback(e => { _first = e.newValue; next.SetEnabled(_first.Trim().Length > 0); });
                    page.Add(Ui.Text("What's your first name?", "h1", Ui.Bold));
                    page.Add(Ui.Text("Used to personalize your experience", "sub", Ui.Regular).Margin(6));
                    page.Add(first.Margin(18));
                    page.Add(next.Margin(18));
                    break;
                case 3:
                    page.Add(Ui.Text("Which hand do you\nputt with?", "h1", Ui.Bold));
                    page.Add(Ui.Box("row", Choice("Right-handed", _hand == "RIGHTY", () => Pick(ref _hand, "RIGHTY")), FreePuttScreen.Space(0).Width(10), Choice("Left-handed", _hand == "LEFTY", () => Pick(ref _hand, "LEFTY"))).Margin(20));
                    break;
                case 4:
                    page.Add(Ui.Text("What's your putting goal?", "h1", Ui.Bold));
                    foreach (var g in Goals) { var goal = g; page.Add(Rows.Row(goal.icon, goal.words, () => Pick(ref _persona, goal.id), _persona == goal.id ? Ui.Text("✓", "feed-name", Ui.Bold) : null).Margin(10)); }
                    break;
                case 5:
                    page.Add(Ui.Text("What's your handicap?", "h1", Ui.Bold));
                    page.Add(Ui.Text("Helps your coach set the right difficulty. Pick NONE if you don't keep one.", "sub", Ui.Regular).Margin(6));
                    VisualElement grid = Ui.Box("hcp-grid");
                    var options = new List<string> { "NONE", "PRO", "+5", "+4", "+3", "+2", "+1" };
                    for (int i = 0; i <= 36; i++) options.Add(i.ToString());
                    foreach (string o in options) { string v = o; grid.Add(Ui.Box(v == _handicap ? "scope scope-on hcp" : "scope hcp", Ui.Text(v, "scope-text", Ui.Bold)).OnTap(() => { _handicap = v; Draw(); })); }
                    page.Add(grid.Margin(14));
                    VisualElement cont = Ui.Button("Continue", Next);
                    cont.SetEnabled(!string.IsNullOrEmpty(_handicap));
                    page.Add(cont.Margin(14));
                    break;
                default:
                    page.Add(Ui.Box("center-box", Ui.Icon("Medal", 64, Ui.Bright), Ui.Text("You're all set!", "h1 center", Ui.Bold).Margin(14), Ui.Text("Ready to start putting?", "sub center", Ui.Regular).Margin(6)));
                    if (!_showBle)
                    {
                        page.Add(Ui.Button("Connect your Puttr", () => { _showBle = true; Draw(); }).Margin(24));
                        page.Add(Ui.Text("Skip for now", "sec-link center", Ui.Medium).Margin(14).OnTap(Save));
                    }
                    else
                    {
                        bool on = Green.Console.Online;
                        page.Add(Ui.Box("glass", Ui.Text(on ? "Connected to " + Green.Link : "Looking for your Puttr...", "feed-name", Ui.Bold),
                            Ui.Text(on ? "You're ready to putt." : "Turn your Puttr on and keep this phone close.", "sub", Ui.Regular)).Margin(18));
                        if (!on) page.Add(Ui.Text("Watch setup guide", "sec-link center", Ui.Medium).Margin(12).OnTap(() => SetupGuide.Play(Root)));
                        page.Add(Ui.Button(_saving ? "Saving..." : on ? "Start putting" : "Continue", Save).Margin(18));
                        page.schedule.Execute(() => { if (_step == Steps && _showBle && Green.Console.Online != on) Draw(); }).Every(1000);
                    }
                    break;
            }
        }

        // Tapping a hand or a goal picks it and moves on half a second later, as the app.
        void Pick(ref string field, string value)
        {
            field = value;
            Draw();
            int at = _step;
            _body.schedule.Execute(() => { if (_step == at) Next(); }).StartingIn(500);
        }

        VisualElement Choice(string words, bool on, Action tap) => Ui.Box(on ? "glass grow onboard-choice choice-on" : "glass grow onboard-choice", Ui.Text(words, "feed-name center", Ui.Bold)).OnTap(tap);

        static TextField Field(string placeholder, string value)
        {
            var f = new TextField { value = value ?? "" };
            f.textEdition.placeholder = placeholder;
            f.textEdition.hidePlaceholderOnFocus = false;
            f.AddToClassList("field");
            return f.Face(Ui.Medium);
        }

        // Letters, numbers and spaces, and nobody else's: checked half a second after typing stops.
        void CheckName(string value, Label state)
        {
            _display = value;
            int me = ++_asked;
            if (!Regex.IsMatch(value, "^[a-zA-Z0-9 ]*$")) { _nameOk = false; _nameError = "Letters, numbers, and spaces only"; state.text = _nameError; state.style.color = Ui.Red; return; }
            _nameError = "";
            _checking = true;
            state.text = "Checking...";
            state.style.color = Ui.Faint;
            _body.schedule.Execute(() =>
            {
                if (me != _asked) return;
                App.Run(Api.Open("/profile/displayName/" + UnityEngine.Networking.UnityWebRequest.EscapeURL(value.Trim()), d =>
                {
                    if (me != _asked) return;
                    object taken = J.First(d);
                    _checking = false;
                    _nameOk = taken == null || J.Str(taken, "userId") == Auth.Uid;
                    _nameError = _nameOk ? "" : "Already taken";
                    Draw();
                }, _ => { if (me != _asked) return; _checking = false; _nameOk = true; Draw(); }));
            }).StartingIn(500);
        }

        void Save()
        {
            if (_saving) return;
            _saving = true;
            Draw();
            // The handicap goes as text, so 0 is saved (the app sent the number 0, which the API dropped).
            var body = new Dictionary<string, object> { { "displayName", _display.Trim() }, { "firstName", _first.Trim() }, { "handedness", _hand ?? "RIGHTY" }, { "persona", _persona ?? "GRINDER" }, { "realHandicap", _handicap ?? "NONE" } };
            App.Run(Api.Post("/profile/save", body, _ => App.Run(Api.Get("/profile/", d =>
            {
                _app.Profile = J.First(d);
                _app.Pop();
                _app.ShowTabs("home");
            }, __ => { _saving = false; Draw(); })), _ => { _saving = false; _app.Toast("Couldn't save. Check your connection."); Draw(); }));
        }
    }
}
