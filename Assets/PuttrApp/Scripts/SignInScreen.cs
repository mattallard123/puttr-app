// Signing in and signing up, as puttr-app's IntroScreen and EmailAuth sheet: "Better putting starts
// here.", Continue with Google, Continue with Apple (iOS), Continue with email (Sign in | Sign up,
// Forgot password?). A first sign-in creates the Puttr profile (ProfileSetup), then onboarding runs.
// Desktop builds, with no Apple or Google sheet, can also sign in with a code the Puttr app approves.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class SignInScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _codeCard, _body;
        bool _busy;
        Coroutine _polling;

        public SignInScreen(App app)
        {
            _app = app;
            _codeCard = Ui.Box("glass hidden");
            _codeCard.style.marginTop = 14;
            _body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(_body);
            Root = scroll;
            Draw();
        }

        void Draw()
        {
            _body.Clear();
            if (_busy) { _body.Add(Ui.Box("pad center-box", Ui.Text("Signing in...", "sub center", Ui.Medium).Margin(200))); return; }
            var logo = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoHeader"), scaleMode = ScaleMode.ScaleToFit };
            logo.AddToClassList("intro-logo");
            Label second = Ui.Text("starts here.", "intro-hero", Ui.Bold);
            second.style.color = Ui.Bright;
            VisualElement chips = Ui.Box("row intro-chips",
                Ui.Box("intro-chip", Ui.Icon("Target", 16, Ui.Bright), Ui.Text("Track putts", "muted", Ui.Medium)),
                Ui.Box("intro-chip", Ui.Icon("Lightning", 16, Ui.Bright), Ui.Text("AI coaching", "muted", Ui.Medium)),
                Ui.Box("intro-chip", Ui.Icon("Trophy", 16, Ui.Bright), Ui.Text("Compete", "muted", Ui.Medium)));
            _body.Add(Ui.Box("pad center-box intro",
                logo,
                Ui.Text("Better putting", "intro-hero", Ui.Bold), second,
                chips,
                SocialSignIn.Supported ? Ui.Button("G   Continue with Google", () => Social(SocialSignIn.Google), "btn btn-white intro-btn") : null,
                SocialSignIn.AppleSupported ? Ui.Button("Continue with Apple", () => Social(SocialSignIn.Apple), "btn btn-white intro-btn") : null,
                Ui.Button("Continue with email", () => EmailSheet.Open(_app, _body, Done), "btn btn-ghost intro-btn"),
                SocialSignIn.Supported ? null : Ui.Button("Sign in with the Puttr app", StartCode, "btn btn-ghost intro-btn"),
                _codeCard,
                Ui.Text("By continuing, you agree to our Terms of Service\nand Privacy Policy", "muted center", Ui.Regular).Margin(24)));
        }

        void Social(Action<Action<string>> start)
        {
            if (_busy) return;
            _busy = true;
            Draw();
            start(problem =>
            {
                _busy = false;
                Draw();
                if (problem == "cancelled") return;
                if (problem != null) { _app.Toast(problem); return; }
                Done();
            });
        }

        void Done()
        {
            if (_polling != null) _app.StopCoroutine(_polling);
            Perf.Mark("signed in");
            _app.ShowTabs("home"); // Home makes the profile on a first sign-in, then runs onboarding
        }

        // ── A code the Puttr app claims (desktop only) ───────────────────────

        void StartCode()
        {
            if (_polling != null) return;
            _polling = App.Run(CodeFlow());
        }

        IEnumerator CodeFlow()
        {
            _codeCard.Clear();
            _codeCard.RemoveFromClassList("hidden");
            _codeCard.Add(Ui.Text("Getting a code...", "sub center", Ui.Regular));
            string device = SystemInfo.deviceUniqueIdentifier;
            var body = new Dictionary<string, object> { { "deviceId", device }, { "deviceName", "Puttr (desktop)" }, { "platform", "unity" } };
            long status = 0;
            string text = null;
            yield return Api.Send("POST", Config.ApiUrl + "/tv/session", J.Write(body), Config.TvToken, (s, t) => { status = s; text = t; });
            object made = J.Parse(text);
            string code = J.Str(made, "code"), link = J.Str(made, "link");
            if (status != 200 || code.Length == 0)
            {
                _codeCard.Clear();
                _codeCard.Add(Ui.Text(status == 0 ? "No connection. Check your WiFi and try again." : "Could not get a code. Try again in a moment.", "sub center", Ui.Regular));
                _polling = null;
                yield break;
            }
            _codeCard.Clear();
            _codeCard.Add(Ui.Text("YOUR CODE", "kick center", Ui.Bold));
            _codeCard.Add(Ui.Text(J.Str(made, "userCode"), "code", Ui.Bold));
            _codeCard.Add(Ui.Text("Open the link from a phone that has the Puttr app, and approve.", "sub center", Ui.Regular));
            _codeCard.Add(Ui.Text(link, "muted center", Ui.Regular));
            float interval = Mathf.Clamp((float)J.Num(made, "interval", 3), 2f, 10f);
            float until = Time.realtimeSinceStartup + (float)J.Num(made, "expiresIn", 600);
            while (Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(interval);
                yield return Api.Send("GET", Config.ApiUrl + "/tv/session/" + code + "?deviceId=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(device), null, Config.TvToken, (s, t) => { status = s; text = t; });
                object poll = J.Parse(text);
                string state = J.Str(poll, "status"), token = J.Str(poll, "accessToken");
                if (state == "ACCEPTED" && token.Length > 0)
                {
                    string problem = null;
                    yield return Auth.SignInWithCustomToken(token, p => problem = p);
                    _polling = null;
                    if (problem != null) { _app.Toast(problem); yield break; }
                    Done();
                    yield break;
                }
                if (state == "EXPIRED" || state == "SIGNED_OUT") break;
            }
            _codeCard.Clear();
            _codeCard.Add(Ui.Text("That code ran out. Tap the button above for a new one.", "sub center", Ui.Regular));
            _polling = null;
        }
    }

    /// <summary>EmailAuth: a sheet with Sign in | Sign up, the password shown or hidden, and a reset link.</summary>
    public static class EmailSheet
    {
        public static void Open(App app, VisualElement over, Action done)
        {
            bool signUp = false, show = false, busy = false;
            VisualElement dim = null, card = Ui.Box("modal email-sheet");
            var email = Field("Email", false);
            email.keyboardType = TouchScreenKeyboardType.EmailAddress;
            var password = Field("Password", true);
            var confirm = Field("Confirm password", true);
            Label error = Ui.Text("", "sub", Ui.Medium), sent = Ui.Text("", "sub", Ui.Medium);
            error.style.color = Ui.Red;
            sent.style.color = Ui.Bright;
            void Draw()
            {
                card.Clear();
                password.isPasswordField = confirm.isPasswordField = !show;
                card.Add(FreePuttScreen.Choice(new[] { "Sign in", "Sign up" }, signUp ? 1 : 0, i => { signUp = i == 1; error.text = sent.text = ""; Draw(); }));
                card.Add(email.Margin(12));
                card.Add(password);
                if (signUp) card.Add(confirm);
                card.Add(Ui.Text(show ? "Hide password" : "Show password", "sec-link", Ui.Medium).OnTap(() => { show = !show; Draw(); }));
                card.Add(error.Margin(6));
                card.Add(sent);
                VisualElement go = Ui.Button(busy ? "..." : signUp ? "Create account" : "Sign in", Submit);
                go.SetEnabled(!busy);
                card.Add(go.Margin(10));
                if (!signUp) card.Add(Ui.Text("Forgot password?", "sec-link center", Ui.Medium).Margin(10).OnTap(Reset));
            }
            void Submit()
            {
                if (busy) return;
                string e = email.value.Trim(), p = password.value;
                error.text = sent.text = "";
                if (e.Length == 0 || p.Length == 0) { error.text = signUp ? "Please enter an email and password" : "Please enter your email and password"; return; }
                if (signUp && p != confirm.value) { error.text = "Passwords must match"; return; }
                busy = true;
                Draw();
                App.Run((signUp ? Auth.SignUpWithPassword(e, p, Finish) : Auth.SignInWithPassword(e, p, Finish)));
            }
            void Finish(string problem)
            {
                busy = false;
                if (problem != null) { error.text = signUp ? problem : "Sign in failed. Check your email and password."; Draw(); return; }
                dim.RemoveFromHierarchy();
                done();
            }
            void Reset()
            {
                string e = email.value.Trim();
                if (e.Length == 0) { error.text = "Enter your email above to reset your password"; return; }
                App.Run(Auth.SendPasswordReset(e, problem => { if (problem == null) { error.text = ""; sent.text = "Password reset email sent"; } else error.text = problem; }));
            }
            Draw();
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim sheet-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
        }

        static TextField Field(string placeholder, bool secret)
        {
            var field = new TextField { isPasswordField = secret };
            field.textEdition.placeholder = placeholder;
            field.textEdition.hidePlaceholderOnFocus = false;
            field.AddToClassList("field");
            return field.Face(Ui.Medium);
        }
    }
}
