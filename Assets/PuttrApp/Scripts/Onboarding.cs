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
            // -app2NewPlayer with -app2Fixture: the made-up Player as a brand new account (no handedness, no coach).
            if (profile != null && NewPlayer && Fixtures.On) J.Obj(profile)?.Remove("handedness");
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

        /// <summary>-app2NewPlayer: look at the first-run screens with -app2Fixture (onboarding on Home, the coach picker on Improve).</summary>
        public static bool NewPlayer => Arg("-app2NewPlayer");

        /// <summary>A switch on the command line (or PUTTR_ARGS on a simulator), for checking screens from a script.</summary>
        public static bool Arg(string name)
        {
            var args = new List<string>(Environment.GetCommandLineArgs());
            string extra = Environment.GetEnvironmentVariable("PUTTR_ARGS");
            if (!string.IsNullOrEmpty(extra)) args.AddRange(extra.Split(' '));
            return args.Contains(name);
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

        /// <summary>Opens the onboarding guide over Home when the signed-in Player has not done it (and it is not up already).</summary>
        public static void Lead(App app)
        {
            if (!ProfileSetup.NeedsOnboarding(app.Profile) || app.Top is OnboardingScreen) return;
            app.Push(new OnboardingScreen(app));
        }

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
                    page.Add(Ui.Box("center-box", logo, Ui.Title("Welcome to Puttr", "title center").Margin(24), Ui.Text("Let's set up your profile in\nunder a minute", "sub center", Ui.Regular).Margin(8)));
                    page.Add(Ui.Button("Let's go", Next).Margin(32));
                    break;
                case 1:
                    var name = Field("e.g. PuttKing42", _display);
                    Label state = Ui.Text(_checking ? "Checking..." : _nameError.Length > 0 ? _nameError : _nameOk && _display.Length > 0 ? "✓ Available" : "", "body-2", Ui.Medium);
                    state.style.color = _nameError.Length > 0 ? Ui.Red : Ui.Bright;
                    name.RegisterValueChangedCallback(e => CheckName(e.newValue, state));
                    VisualElement go = Ui.Button("Continue", Next);
                    go.SetEnabled(_display.Trim().Length > 0 && _nameOk && !_checking);
                    page.Add(Ui.Title("Pick a display name"));
                    page.Add(Ui.Text("This is how other Players will see you", "sub", Ui.Regular).Margin(8));
                    page.Add(name.Margin(16));
                    page.Add(state);
                    page.Add(go.Margin(16));
                    break;
                case 2:
                    var first = Field("Your first name", _first);
                    VisualElement next = Ui.Button("Continue", Next);
                    next.SetEnabled(_first.Trim().Length > 0);
                    first.RegisterValueChangedCallback(e => { _first = e.newValue; next.SetEnabled(_first.Trim().Length > 0); });
                    page.Add(Ui.Title("What's your first name?"));
                    page.Add(Ui.Text("Used to personalize your experience", "sub", Ui.Regular).Margin(8));
                    page.Add(first.Margin(16));
                    page.Add(next.Margin(16));
                    break;
                case 3:
                    page.Add(Ui.Title("Which hand do you\nputt with?"));
                    page.Add(Ui.Box("row", Choice("Right-handed", _hand == "RIGHTY", () => Pick(ref _hand, "RIGHTY")), FreePuttScreen.Space(0).Width(12), Choice("Left-handed", _hand == "LEFTY", () => Pick(ref _hand, "LEFTY"))).Margin(24));
                    break;
                case 4:
                    page.Add(Ui.Title("What's your putting goal?"));
                    // The goals are rows of one bordered block; the picked one carries a green tick.
                    VisualElement goals = Ui.Box("card onb-list");
                    foreach (var g in Goals) { var goal = g; goals.Add(Rows.Row(goal.icon, goal.words, () => Pick(ref _persona, goal.id), _persona == goal.id ? Ui.Icon("Check", 20, Ui.Bright) : null)); }
                    page.Add(goals.Margin(24));
                    break;
                case 5:
                    page.Add(Ui.Title("What's your handicap?"));
                    page.Add(Ui.Text("Helps your coach set the right difficulty. Pick NONE if you don't keep one.", "sub", Ui.Regular).Margin(8));
                    VisualElement grid = Ui.Box("hcp-grid");
                    var options = new List<string> { "NONE", "PRO", "+5", "+4", "+3", "+2", "+1" };
                    for (int i = 0; i <= 36; i++) options.Add(i.ToString());
                    foreach (string o in options) { string v = o; grid.Add(Ui.Box(v == _handicap ? "scope scope-on hcp" : "scope hcp", Ui.Text(v, "scope-text", Ui.Bold)).OnTap(() => { _handicap = v; Draw(); })); }
                    page.Add(grid.Margin(16));
                    VisualElement cont = Ui.Button("Continue", Next);
                    cont.SetEnabled(!string.IsNullOrEmpty(_handicap));
                    page.Add(cont.Margin(16));
                    break;
                default:
                    page.Add(Ui.Box("center-box", Ui.Icon("Medal", 64, Ui.Bright), Ui.Title("You're all set!", "title center").Margin(16), Ui.Text("Ready to start putting?", "sub center", Ui.Regular).Margin(8)));
                    if (!_showBle)
                    {
                        page.Add(Ui.Button("Connect your Puttr", () => { _showBle = true; Draw(); }).Margin(24));
                        page.Add(Ui.Text("Skip for now", "sec-link center sign-link", Ui.Medium).Margin(12).OnTap(Save));
                    }
                    else
                    {
                        bool on = Green.Console.Online;
                        page.Add(Ui.Box("card", Ui.Text(on ? "Connected to " + Green.Link : "Looking for your Puttr...", "body", Ui.Bold),
                            Ui.Text(on ? "You're ready to putt." : "Turn your Puttr on and keep this phone close.", "body-2", Ui.Regular).Margin(4)).Margin(16));
                        if (!on) page.Add(Ui.Text("Watch setup guide", "sec-link center sign-link", Ui.Medium).Margin(12).OnTap(() => SetupGuide.Play(Root)));
                        page.Add(Ui.Button(_saving ? "Saving..." : on ? "Start putting" : "Continue", Save).Margin(16));
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

        VisualElement Choice(string words, bool on, Action tap) => Ui.Box(on ? "card onboard-choice onboard-choice-on" : "card onboard-choice", Ui.Text(words, "body center", Ui.Bold)).OnTap(tap);

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
