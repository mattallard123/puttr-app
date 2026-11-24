// Becoming a Puttr Coach (puttr-app components/coaching/academy, apply, CoachLanding):
//
//   CoachLandingScreen            the pitch and Puttr Pro for Coaches (pro_membership_* through IAPHUB)
//   CoachApplyScreen              six steps, saved as they go (POST /coach/application/save), then submit
//   CoachApplicationStatusScreen  submitted / in review / approved (the go-live path) / waitlisted / not approved
//   CoachAcademyScreen            GET /coach/academy: online (required for online coaches) and studio modules
//   AcademyModuleScreen           the lessons; "Take the quiz" records them read (POST /coach/academy/:key/complete)
//   AcademyQuizScreen             one question at a time, 80% to pass (POST /coach/academy/:key/quiz {answers})
//
// Plus Form, the small form pieces the coaching, lesson and Junior screens share.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Form
    {
        public static TextField Field(string placeholder, string value, int max = 0, bool multiline = false)
        {
            var f = new TextField { value = value ?? "", multiline = multiline };
            if (max > 0) f.maxLength = max;
            f.textEdition.placeholder = placeholder;
            f.textEdition.hidePlaceholderOnFocus = false;
            f.AddToClassList("field");
            if (multiline) { f.AddToClassList("area"); f.verticalScrollerVisibility = ScrollerVisibility.Hidden; }
            return f.Face(Ui.Medium);
        }

        public static Label Label(string text) => Ui.Text(text.ToUpperInvariant(), "form-label", Ui.Bold);

        /// <summary>A row of pills, one of them on (or none: on = -1).</summary>
        public static VisualElement Pills(string[] labels, int on, Action<int> pick)
        {
            VisualElement row = Ui.Box("pills");
            void Draw(int chosen)
            {
                row.Clear();
                for (int i = 0; i < labels.Length; i++)
                {
                    int index = i;
                    row.Add(Ui.Box(i == chosen ? "scope scope-on" : "scope", Ui.Text(labels[i], "scope-text", Ui.Bold)).OnTap(() => { Draw(index); pick(index); }));
                }
            }
            Draw(on);
            return row;
        }

        /// <summary>A tick box and its words; the whole row toggles.</summary>
        public static VisualElement Check(bool on, string text, Action<bool> flip)
        {
            bool state = on;
            Image tick = Ui.Icon("Check", 14, Color.white);
            VisualElement box = Ui.Box(on ? "check-box check-on" : "check-box", tick);
            tick.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            VisualElement row = Ui.Box("check-row", box, Ui.Text(text, "check-text", Ui.Regular));
            row.OnTap(() => { state = !state; box.EnableInClassList("check-on", state); tick.style.display = state ? DisplayStyle.Flex : DisplayStyle.None; flip(state); });
            return row;
        }

        /// <summary>"Step 2 of 6" and its bar, with a back arrow.</summary>
        public static VisualElement Steps(int step, int total, Action back)
        {
            VisualElement top = Ui.Box("top", Ui.Text("‹", "back back-chevron", Ui.Medium).OnTap(back), Ui.Text("Step " + step + " of " + total, "top-title", Ui.Bold), Ui.Box("back-balance"));
            return Ui.Box("", top, Ui.Box("pad", Ui.Progress(step / (float)total)));
        }

        /// <summary>A server error's message, or the fallback.</summary>
        public static string Why(string error, string fallback)
        {
            object e = J.Parse(error ?? "");
            string m = J.Str(e, "message");
            return m.Length > 0 ? m : fallback;
        }

        /// <summary>An ISO date from the API as local time, or null.</summary>
        public static DateTime? When(object row, string key)
        {
            string s = J.Str(row, key);
            return s.Length > 0 && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t) ? t.ToLocalTime() : (DateTime?)null;
        }

        public static string Date(object row, string key, string format = "MMM d") => When(row, key) is DateTime t ? t.ToString(format, CultureInfo.InvariantCulture) : "";

        /// <summary>moment's fromNow: "a few seconds ago", "3 hours ago", "2 days ago".</summary>
        public static string Ago(object row, string key)
        {
            if (!(When(row, key) is DateTime t)) return "";
            TimeSpan d = DateTime.Now - t;
            bool future = d.TotalSeconds < 0;
            d = d.Duration();
            string words = d.TotalSeconds < 45 ? "a few seconds" : d.TotalMinutes < 45 ? Math.Max(1, (int)Math.Round(d.TotalMinutes)) + " minutes" : d.TotalHours < 22 ? Math.Max(1, (int)Math.Round(d.TotalHours)) + " hours"
                : d.TotalDays < 26 ? Math.Max(1, (int)Math.Round(d.TotalDays)) + " days" : d.TotalDays < 320 ? Math.Max(1, (int)Math.Round(d.TotalDays / 30.4)) + " months" : Math.Max(1, (int)Math.Round(d.TotalDays / 365)) + " years";
            words = Regex.Replace(words, "^1 (minute|hour|day|month|year)s", m => (m.Groups[1].Value == "hour" ? "an " : "a ") + m.Groups[1].Value);
            return future ? "in " + words : words + " ago";
        }

        public static string Duration(double seconds) { int s = Mathf.Max(0, (int)Math.Round(seconds)); return (s / 60) + ":" + (s % 60).ToString("00"); }

        /// <summary>A plain 24 point stroke icon at the start of a row (no tinted square behind it).</summary>
        public static VisualElement Glyph(string icon) => Ui.Box("coach-glyph", Ui.Icon(icon, 24, Color.white));

        /// <summary>A small secondary button at the end of a row (Join, Submit, Play).</summary>
        public static VisualElement Mini(string text, Action onTap) => Ui.Button(text, onTap, "btn btn-ghost coach-mini");

        public static VisualElement Bottom(params VisualElement[] children) => Ui.Box("pad", children).Margin(12);

        /// <summary>A photo from the library to a multipart POST; done(the response, or null).</summary>
        public static IEnumerator UploadPhoto(string path, string url, string field, Action<object> done)
        {
            byte[] jpg;
            try { jpg = PhotoPicker.Shrink(File.ReadAllBytes(path), 1600, 90); } catch (Exception) { jpg = null; }
            if (jpg == null) { done(null); yield break; }
            string token = null;
            yield return Auth.Token(t => token = t);
            var form = new List<IMultipartFormSection> { new MultipartFormFileSection(field, jpg, "proof.jpg", "image/jpeg") };
            using (UnityWebRequest req = UnityWebRequest.Post(Config.ApiUrl + url, form))
            {
                if (token != null) req.SetRequestHeader("Authorization", "Bearer " + token);
                req.timeout = 60;
                yield return req.SendWebRequest();
                done(req.result == UnityWebRequest.Result.Success ? J.Parse(req.downloadHandler.text) : null);
            }
        }
    }

    // ── The pitch, and Puttr Pro ─────────────────────────────────────────────

    public sealed class CoachLandingScreen : AppScreen
    {
        public CoachLandingScreen(App app)
        {
            Root = Page.Make(app, "Puttr Coaching", out VisualElement body);
            if (Kid.Blocked(app, Kid.Owner, body)) return;
            body.Add(Ui.Box("pad", Ui.Title("Coach players anywhere", "title"), Ui.Text("Give paid video lessons, run your studio with Puttr data, and grow your players with practice plans.", "sub wrap", Ui.Regular).Margin(8)));
            string[,] features =
            {
                { "VideoCamera", "Paid video lessons", "Players book you, submit a session, and you send a personal video breakdown." },
                { "Cube", "3D sessions", "Body tracking and ball data side by side for every putt." },
                { "ListChecks", "Practice plans", "Assign games and drills and watch your players work them." },
                { "GameController", "Custom games & drills", "Build your own and share them with your players." },
            };
            VisualElement list = Ui.Box("card");
            for (int i = 0; i < features.GetLength(0); i++)
                list.Add(Ui.Box("row", Form.Glyph(features[i, 0]), Ui.Box("grow set-words", Ui.Text(features[i, 1], "body", Ui.Bold), Ui.Text(features[i, 2], "body-2 wrap", Ui.Regular))).Margin(i == 0 ? 0 : 12));
            body.Add(Ui.Box("pad", list));
            body.Add(Ui.Box("pad", Ui.Button("Apply to coach online", () => CoachApplyScreen.Open(app))).Margin(4));
            body.Add(Ui.Section("Puttr Pro for Coaches"));
            body.Add(Ui.Box("pad", new ClubPlans(app, true).Root));
            body.Add(Ui.Box("pad", Ui.Text("$39.99/month after a free trial. Cancel any time in your App Store or Google Play account settings at least 24 hours before the trial ends.", "body-2 wrap", Ui.Regular)));
        }
    }

    // ── Applying ─────────────────────────────────────────────────────────────

    public sealed class CoachApplyScreen : AppScreen
    {
        const int Total = 6;
        static readonly string[] CredTypes = { "PGA_AM", "LPGA", "PGA_INTL", "TPI", "AIMPOINT", "OTHER" };
        static readonly string[] CredLabels = { "PGA of America", "LPGA", "PGA (International)", "TPI Certified", "AimPoint", "Other" };
        static readonly string[] Contexts = { "FACILITY", "ONLINE", "BOTH" };
        static readonly int[] Turnarounds = { 24, 48, 72, 168 };

        readonly App _app;
        readonly VisualElement _top, _body, _foot;
        int _step = 1;
        string _location, _country, _years = "", _players = "", _context, _response = "", _video, _price = "", _capacity = "";
        int _turnaround = 72;
        bool _tos, _saving, _uploading;
        float _progress;
        string _proofBusy;
        readonly List<Dictionary<string, object>> _credentials = new List<Dictionary<string, object>>();

        /// <summary>Resumes a saved draft, or shows where a submitted application stands.</summary>
        public static void Open(App app)
        {
            App.Run(Api.Get("/coach/application", d =>
            {
                object a = J.Get(d, "application");
                if (a != null && J.Str(a, "submittedAt").Length > 0) app.Push(new CoachApplicationStatusScreen(app));
                else app.Push(new CoachApplyScreen(app, a));
            }, _ => app.Push(new CoachApplyScreen(app, null))));
        }

        public CoachApplyScreen(App app, object draft)
        {
            _app = app;
            _top = new VisualElement(); _body = new VisualElement(); _foot = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(_top); scroll.Add(_body); scroll.Add(_foot); scroll.Add(FreePuttScreen.Space(32));
            Root = scroll;
            if (Kid.Blocked(app, Kid.Owner, _body)) return;
            _location = J.Str(app.Profile, "homeTown");
            if (draft != null)
            {
                _step = Mathf.Clamp(J.Int(draft, "step", 1), 1, Total);
                if (J.Str(draft, "location").Length > 0) _location = J.Str(draft, "location");
                _country = J.Str(draft, "country").Length > 0 ? J.Str(draft, "country") : null;
                _years = J.Get(draft, "yearsCoaching") != null ? J.Int(draft, "yearsCoaching").ToString() : "";
                _players = J.Get(draft, "playerCount") != null ? J.Int(draft, "playerCount").ToString() : "";
                _context = J.Str(draft, "teachingContext").Length > 0 ? J.Str(draft, "teachingContext") : null;
                foreach (object c in J.List(J.Get(draft, "credentials"))) if (J.Obj(c) != null) _credentials.Add(new Dictionary<string, object>(J.Obj(c)));
                _response = J.Str(draft, "freeResponse");
                _video = J.Str(draft, "introVideoUrl").Length > 0 ? J.Str(draft, "introVideoUrl") : null;
                _price = J.Get(draft, "intendedPrice") != null ? J.Int(draft, "intendedPrice").ToString() : "";
                _turnaround = J.Int(draft, "intendedTurnaround", 72);
                _capacity = J.Get(draft, "weeklyCapacity") != null ? J.Int(draft, "weeklyCapacity").ToString() : "";
                _tos = J.Str(draft, "tosAcceptedAt").Length > 0;
            }
            Draw();
        }

        int Words => _response.Trim().Length == 0 ? 0 : Regex.Split(_response.Trim(), "\\s+").Length;

        bool Valid()
        {
            switch (_step)
            {
                case 1: return !string.IsNullOrWhiteSpace(_location) && _years.Length > 0 && _context != null;
                case 2: return true; // credentials earn the badge; they do not decide eligibility
                case 3: return _response.Trim().Length > 0 && Words <= 400;
                case 4: return _video != null && !_uploading;
                case 5: return int.TryParse(_price, out int p) && p >= 50;
                default: return _tos;
            }
        }

        static object Num(string s) => s.Length > 0 ? (object)(int.TryParse(s, out int n) ? n : 0) : null;

        Dictionary<string, object> Fields(int step) => new Dictionary<string, object>
        {
            { "step", step }, { "location", _location }, { "country", _country }, { "yearsCoaching", Num(_years) }, { "playerCount", Num(_players) },
            { "teachingContext", _context }, { "credentials", new List<object>(_credentials) }, { "freeResponse", _response }, { "introVideoUrl", _video },
            { "intendedPrice", Num(_price) }, { "intendedTurnaround", _turnaround }, { "weeklyCapacity", Num(_capacity) }, { "tosAccepted", _tos },
        };

        void Draw()
        {
            _top.Clear(); _body.Clear(); _foot.Clear();
            _top.Add(Form.Steps(_step, Total, () => { if (_step > 1) { _step--; Draw(); } else _app.Pop(); }));
            VisualElement p = Ui.Box("pad");
            _body.Add(p);
            switch (_step)
            {
                case 1: Basics(p); break;
                case 2: Credentials(p); break;
                case 3: Response(p); break;
                case 4: Video(p); break;
                case 5: Rate(p); break;
                default: Terms(p); break;
            }
            Foot();
        }

        void Foot()
        {
            _foot.Clear();
            bool last = _step == Total, ok = Valid() && !_saving;
            VisualElement go = Ui.Button(_saving ? "Saving..." : last ? "Submit application" : "Continue", () => { if (Valid() && !_saving) App.Run(last ? Submit() : Advance()); });
            if (!ok) go.AddToClassList("btn-off");
            _foot.Add(Form.Bottom(go));
        }

        void Basics(VisualElement p)
        {
            p.Add(Ui.Title("Tell us about your coaching", "title-20"));
            p.Add(Form.Label("Where are you based?"));
            p.Add(Ui.Box("card row", Ui.Icon("MapPin", 18, _location?.Length > 0 ? Ui.Bright : Ui.Faint), Ui.Text(_location?.Length > 0 ? _location : "Search your city", "body grow", Ui.Medium).Margin(0)).OnTap(() =>
                _app.Push(new CityScreen(_app, _location ?? "", (name, lat, lng) => { _location = name; _country = CityScreen.LastCountry ?? _country; Draw(); }))));
            p.Add(Form.Label("Years coaching"));
            TextField years = Form.Field("e.g. 8", _years, 2);
            years.keyboardType = TouchScreenKeyboardType.NumberPad;
            years.RegisterValueChangedCallback(e => { _years = Regex.Replace(e.newValue, "[^0-9]", ""); Foot(); });
            p.Add(years);
            p.Add(Form.Label("How many players do you currently coach?"));
            TextField players = Form.Field("e.g. 25", _players, 4);
            players.keyboardType = TouchScreenKeyboardType.NumberPad;
            players.RegisterValueChangedCallback(e => _players = Regex.Replace(e.newValue, "[^0-9]", ""));
            p.Add(players);
            p.Add(Form.Label("Where do you teach today?"));
            p.Add(Form.Pills(new[] { "At a facility", "Online", "Both" }, Array.IndexOf(Contexts, _context), i => { _context = Contexts[i]; Foot(); }));
        }

        void Credentials(VisualElement p)
        {
            p.Add(Ui.Title("Credentials", "title-20"));
            p.Add(Ui.Text("Optional, but verified credentials earn the Certified Coach badge. Select any you hold and add a photo of each certificate or membership card.", "sub wrap", Ui.Regular).Margin(8));
            for (int i = 0; i < CredTypes.Length; i++)
            {
                string type = CredTypes[i], label = CredLabels[i];
                Dictionary<string, object> held = _credentials.Find(c => J.Str(c, "type") == type);
                VisualElement card = Ui.Box("card");
                card.Add(Ui.Box("row", Ui.Icon("Certificate", 20, held != null ? Ui.Bright : Ui.Faint), Ui.Text(label, "body grow", Ui.Medium).Margin(0), held != null ? Ui.Icon("CheckCircle", 20, Ui.Bright) : null)
                    .OnTap(() => { if (held != null) _credentials.Remove(held); else _credentials.Add(new Dictionary<string, object> { { "type", type }, { "label", label }, { "proofUrl", null } }); Draw(); }));
                if (held != null)
                {
                    string words = J.Str(held, "proofUrl").Length > 0 ? "Proof added. Tap to replace." : _proofBusy == type ? "Uploading..." : "+ Add a photo of your credential";
                    Label proof = Ui.Text(words, "body-2", Ui.Regular);
                    proof.style.color = J.Str(held, "proofUrl").Length > 0 ? Ui.Bright : Color.white;
                    card.Add(proof.Margin(12).OnTap(() => AddProof(held)));
                }
                p.Add(card.Margin(12));
            }
        }

        void AddProof(Dictionary<string, object> credential)
        {
            PhotoPicker.Pick(path =>
            {
                if (path == null) return;
                _proofBusy = J.Str(credential, "type");
                Draw();
                App.Run(Form.UploadPhoto(path, "/coach/application/uploadProof", "file", r =>
                {
                    _proofBusy = null;
                    if (J.Str(r, "publicUrl").Length > 0) credential["proofUrl"] = J.Str(r, "publicUrl");
                    else Dialog.Ask(Root, "Upload failed", "Could not upload your credential photo. Try again.", "OK", () => { });
                    Draw();
                }));
            });
        }

        void Response(VisualElement p)
        {
            p.Add(Ui.Title("Your putting philosophy", "title-20"));
            p.Add(Ui.Text("Describe how you coach putting and how you would use data like make percentage, pace, and start line in a lesson.", "sub wrap", Ui.Regular).Margin(8));
            TextField text = Form.Field("Write in full sentences, not a resume...", _response, 4000, true);
            Label count = Ui.Text("", "body-2", Ui.Regular);
            void Count() { count.text = Words + " words" + (Words > 400 ? " (maximum 400)" : ""); count.style.color = Words > 400 ? Ui.Red : Ui.Faint; }
            text.RegisterValueChangedCallback(e => { _response = e.newValue; Count(); Foot(); });
            Count();
            p.Add(text.Margin(12));
            p.Add(count);
        }

        void Video(VisualElement p)
        {
            p.Add(Ui.Title("Intro video", "title-20"));
            p.Add(Ui.Text("Record a 60 to 90 second video on your phone: who you are, who you coach, and why putting. Then upload it here. If you are approved, this becomes the intro video players see on your coach profile.", "sub wrap", Ui.Regular).Margin(8));
            if (_uploading)
                p.Add(Ui.Box("card center-box", Ui.Num(Mathf.RoundToInt(_progress * 100) + "%", "num center"), Ui.Text("Uploading your intro...", "body-2 center", Ui.Regular), Ui.Progress(_progress).Margin(8)).Margin(12));
            else if (_video != null)
                p.Add(Ui.Box("", VideoBox.Make(_video, 210).Margin(12), Ui.Text("Replace video", "sec-link center", Ui.Medium).Margin(12).OnTap(PickVideo)));
            else
                p.Add(Ui.Box("dashed", Ui.Icon("VideoCamera", 40, Ui.Faint), Ui.Text("Upload from your camera roll", "body-2", Ui.Bold).Margin(12)).Margin(12).OnTap(PickVideo));
        }

        void PickVideo()
        {
            PhotoPicker.PickVideo(path =>
            {
                if (path == null) return;
                App.Run(Uploads.Length(path, seconds =>
                {
                    if (seconds > 180) { Dialog.Ask(Root, "Video too long", "Keep your intro under 3 minutes. Aim for 60 to 90 seconds.", "OK", () => { }); return; }
                    _uploading = true; _progress = 0; Draw();
                    string name = "applications/intro_" + J.Str(_app.Profile, "userId", Auth.Uid) + "_" + UnityEngine.Random.Range(0, 100000000) + ".mp4";
                    App.Run(Uploads.Video(path, name, f => { _progress = f; if (_step == 4) Draw(); }, url =>
                    {
                        _uploading = false;
                        if (url != null) _video = url; else Dialog.Ask(Root, "Upload failed", "Could not upload your video. Try again.", "OK", () => { });
                        Draw();
                    }));
                }));
            });
        }

        void Rate(VisualElement p)
        {
            p.Add(Ui.Title("Rate and availability", "title-20"));
            p.Add(Form.Label("Lesson price (USD, minimum $50)"));
            TextField price = Form.Field("e.g. 75", _price, 4);
            price.keyboardType = TouchScreenKeyboardType.NumberPad;
            price.RegisterValueChangedCallback(e => { _price = Regex.Replace(e.newValue, "[^0-9]", ""); Foot(); });
            p.Add(price);
            p.Add(Form.Label("Turnaround per lesson"));
            p.Add(Form.Pills(new[] { "24 hrs", "48 hrs", "3 days", "7 days" }, Array.IndexOf(Turnarounds, _turnaround), i => _turnaround = Turnarounds[i]));
            p.Add(Form.Label("Lessons you could take per week"));
            TextField cap = Form.Field("e.g. 10", _capacity, 3);
            cap.keyboardType = TouchScreenKeyboardType.NumberPad;
            cap.RegisterValueChangedCallback(e => _capacity = Regex.Replace(e.newValue, "[^0-9]", ""));
            p.Add(cap);
        }

        void Terms(VisualElement p)
        {
            p.Add(Ui.Title("Almost done", "title-20"));
            p.Add(Ui.Box("card", Ui.Text("What happens next", "body", Ui.Bold),
                Ui.Text("1. Our team reviews your application within 5 business days.\n2. If approved, you complete the Coach Academy: short training modules plus a practice lesson.\n3. Set your profile and payouts, then go live to players.", "sub wrap", Ui.Regular).Margin(8)).Margin(12));
            p.Add(Form.Check(_tos, "I agree to the Puttr Coach Terms of Service and understand that coaches are independent contractors, not Puttr employees.", on => { _tos = on; Foot(); }).Margin(12));
            p.Add(Ui.Text("Read the Puttr Coach Terms of Service", "sec-link", Ui.Medium).OnTap(() => Application.OpenURL("https://puttr.co/coach-terms")));
        }

        IEnumerator Advance()
        {
            _saving = true; Foot();
            string error = null;
            int next = Mathf.Min(_step + 1, Total);
            yield return Api.Post("/coach/application/save", Fields(next), _ => { }, e => error = e ?? "failed");
            _saving = false;
            if (error != null) { Dialog.Ask(Root, "Save failed", Form.Why(error, "Could not save. Check your connection and try again."), "OK", () => { }); Foot(); yield break; }
            _step = next;
            Draw();
        }

        IEnumerator Submit()
        {
            _saving = true; Foot();
            string error = null;
            yield return Api.Post("/coach/application/save", Fields(Total), _ => { }, e => error = e ?? "failed");
            if (error == null) yield return Api.Post("/coach/application/submit", new Dictionary<string, object>(), _ => { }, e => error = e ?? "failed");
            _saving = false;
            if (error != null) { Dialog.Ask(Root, "Submit failed", Form.Why(error, "Could not submit. Check your connection and try again."), "OK", () => { }); Foot(); yield break; }
            _app.Pop();
            _app.Push(new CoachApplicationStatusScreen(_app));
        }
    }

    /// <summary>A video with a play button; tapping plays it in place (VideoPlayer to a texture).</summary>
    public static class VideoBox
    {
        public static VisualElement Make(string url, float height, string thumb = null, double length = 0)
        {
            VisualElement box = Ui.Box("thumb-big");
            box.style.height = height;
            if (!string.IsNullOrEmpty(thumb)) Images.Load(thumb, t => { if (t != null) { box.style.backgroundImage = new StyleBackground(t); box.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; } });
            VisualElement play = Ui.Box("coach-play", Ui.Icon("Play", 22, Color.white));
            box.Add(play);
            if (length > 0) { Label len = Ui.Text(Form.Duration(length), "chip", Ui.Bold); len.style.position = Position.Absolute; len.style.right = 8; len.style.bottom = 8; box.Add(len); }
            box.OnTap(() =>
            {
                if (box.userData is UnityEngine.Video.VideoPlayer playing) { if (playing.isPlaying) playing.Pause(); else playing.Play(); return; }
                play.style.display = DisplayStyle.None;
                var go = new GameObject("Video box");
                var player = go.AddComponent<UnityEngine.Video.VideoPlayer>();
                var rt = new RenderTexture(720, 1280, 0);
                player.source = UnityEngine.Video.VideoSource.Url;
                player.url = url;
                player.renderMode = UnityEngine.Video.VideoRenderMode.RenderTexture;
                player.targetTexture = rt;
                player.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.Direct;
                player.prepareCompleted += v =>
                {
                    if (v.width > 0 && v.height > 0) { rt.Release(); rt.width = (int)v.width; rt.height = (int)v.height; rt.Create(); }
                    box.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
                    box.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                    v.Play();
                };
                player.Prepare();
                box.userData = player;
                box.schedule.Execute(() => Perf.Wake(0.2f)).Every(100).Until(() => box.panel == null);
                box.RegisterCallback<DetachFromPanelEvent>(_ => { UnityEngine.Object.Destroy(go); rt.Release(); });
            });
            return box;
        }
    }

    // ── Where an application stands ──────────────────────────────────────────

    public sealed class CoachApplicationStatusScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;

        public CoachApplicationStatusScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Coach Application", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown()
        {
            object application = null, goLive = null;
            int pending = 2;
            void Done() { if (--pending == 0) Draw(application, goLive); }
            App.Run(Api.Get("/coach/application", d => { application = J.Get(d, "application"); Done(); }, _ => Done()));
            App.Run(Api.Get("/coach/golive", d => { goLive = J.Get(d, "goLive"); Done(); }, _ => Done()));
        }

        VisualElement Step(string state, string title, string body)
        {
            Color c = state == "done" ? Ui.Bright : state == "current" ? Color.white : Ui.Faint;
            string icon = state == "done" ? "CheckCircle" : state == "current" ? "MagnifyingGlass" : "Circle";
            return Ui.Box("timeline-row", Ui.Icon(icon, 22, c), Ui.Box("grow set-words", Ui.Text(title, "body", Ui.Bold), body != null ? Ui.Text(body, "body-2 wrap", Ui.Regular) : null));
        }

        void Draw(object a, object gl)
        {
            _body.Clear();
            if (a == null) { _body.Add(Page.Note("No application yet.")); _body.Add(Ui.Box("pad", Ui.Button("Apply to coach online", () => { _app.Pop(); CoachApplyScreen.Open(_app); }))); return; }
            int st = J.Int(a, "status");
            if (st == 0 || st == 1)
            {
                VisualElement card = Ui.Box("card");
                card.Add(Step("done", "Application submitted " + Form.Date(a, "submittedAt"), "Thanks for applying. Sit tight."));
                card.Add(Step("current", st == 1 ? "Under review now" : "In the review queue", "Our team reviews every application personally. You will hear back within 5 business days by email and push."));
                card.Add(Step("todo", "Coach Academy", "If approved, you complete short training modules and a practice lesson before going live."));
                _body.Add(Ui.Box("pad", card));
                return;
            }
            if (st == 3)
            {
                _body.Add(Ui.Box("pad", Ui.Box("card", Ui.Icon("PauseCircle", 40, Ui.Amber), Ui.Title("You're on the waitlist", "title-20").Margin(12),
                    Ui.Text("Your application looks good. Right now we are matching new coaches to player demand, and we will reach out as soon as a spot opens. No action needed.", "sub wrap", Ui.Regular).Margin(8))));
                return;
            }
            if (st == 4)
            {
                string again = Form.Date(a, "reapplyAfter", "MMMM d, yyyy");
                _body.Add(Ui.Box("pad", Ui.Box("card", Ui.Icon("XCircle", 40, Ui.Red), Ui.Title("Not this time", "title-20").Margin(12),
                    Ui.Text("After review, we are not able to bring you on right now." + (again.Length > 0 ? " You are welcome to apply again after " + again + "." : ""), "sub wrap", Ui.Regular).Margin(8))));
                return;
            }
            // Approved: the path to going live.
            bool live = J.Flag(gl, "live"), academy = J.Flag(gl, "academyComplete"), certPassed = J.Flag(gl, "certPassed"), certPending = J.Flag(gl, "certPending");
            bool profile = J.Flag(gl, "profileComplete"), payouts = J.Flag(gl, "payoutsEnabled");
            object progress = J.Get(gl, "academyProgress");
            _body.Add(Ui.Box("pad", Ui.Box("card", Ui.Title("You're approved", "title-20"), Ui.Text("Welcome to Puttr coaching. Work through the steps below to go live.", "sub wrap", Ui.Regular).Margin(8))));
            VisualElement path = Ui.Box("card");
            path.Add(Ui.Text("Your path to going live", "body", Ui.Bold));
            path.Add(Step("done", "Application approved", null));
            path.Add(Step(academy ? "done" : "current", "Coach Academy", academy ? "Complete" : J.Int(progress, "passed") + " of " + J.Int(progress, "total") + " modules done"));
            path.Add(Step(certPassed ? "done" : academy ? "current" : "todo", "Certification lesson", certPassed ? "Passed" : certPending ? "Delivered. Puttr is grading it." : academy ? "Ready: coach a practice lesson, graded by Puttr" : "Unlocks after you finish the Academy"));
            path.Add(Step(profile ? "done" : "todo", "Complete your coaching profile", profile ? "Headline, bio and price are set" : "Headline, bio and lesson price"));
            path.Add(Step(payouts ? "done" : "todo", "Set up payouts", payouts ? "Stripe connected" : "Connect Stripe to get paid for lessons"));
            _body.Add(Ui.Box("pad", path));
            (string label, Action tap) cta =
                live ? ("Open your lesson inbox", (Action)(() => _app.Push(new LessonInboxScreen(_app))))
                : !academy ? ("Open the Coach Academy", () => _app.Push(new CoachAcademyScreen(_app)))
                : certPending ? ("Certification is being graded", null)
                : !certPassed ? ("Start your certification lesson", () => CoachAcademyScreen.BeginCertification(_app, Root))
                : !profile ? ("Complete your coaching profile", () => _app.Push(new CoachLessonSetupScreen(_app)))
                : !payouts ? ("Set up payouts with Stripe", () => _app.Push(new CoachLessonSetupScreen(_app)))
                : ("Open your lesson inbox", () => _app.Push(new LessonInboxScreen(_app)));
            VisualElement go = Ui.Button(cta.label, cta.tap);
            if (cta.tap == null) go.AddToClassList("btn-off");
            _body.Add(Ui.Box("pad", go));
            if (academy) _body.Add(Ui.Text("Review the Academy", "sec-link center", Ui.Medium).Margin(12).OnTap(() => _app.Push(new CoachAcademyScreen(_app))));
        }
    }

    // ── The Academy ──────────────────────────────────────────────────────────

    public sealed class CoachAcademyScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        public static object Last;

        public CoachAcademyScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Coach Academy", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/coach/academy", d => { Last = d; Draw(d); }, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load the Academy. Check your connection.")); }));

        /// <summary>POST /coach/certification/start: a practice player session lands in the lesson inbox.</summary>
        public static void BeginCertification(App app, VisualElement over)
        {
            App.Run(Api.Post("/coach/certification/start", new Dictionary<string, object>(), r =>
                Dialog.Ask(over, J.Flag(r, "existing") ? "Your certification lesson is waiting" : "Certification lesson ready",
                    "A practice player session is in your lesson inbox. Review it and deliver a lesson exactly like you would for a real player. Puttr grades it, not the player.",
                    "Open my lessons", () => app.Push(new LessonInboxScreen(app))),
                e => Dialog.Ask(over, "Not yet", Form.Why(e, "Could not start certification. Try again."), "OK", () => { })));
        }

        /// <summary>POST /coach/tutorial/start: the practice space, opened in the lesson workspace.</summary>
        public static void OpenPractice(App app, VisualElement over)
        {
            App.Run(Api.Post("/coach/tutorial/start", new Dictionary<string, object>(), r => app.Push(new LessonWorkspaceScreen(app, J.Str(J.Get(r, "booking"), "id"))),
                e => Dialog.Ask(over, "Practice space", Form.Why(e, "Could not open the practice space. Try again."), "OK", () => { })));
        }

        void Draw(object d)
        {
            _body.Clear();
            List<object> modules = J.List(J.Get(J.Get(d, "academy"), "modules"));
            List<object> online = modules.FindAll(m => J.Str(m, "track") == "online"), studio = modules.FindAll(m => J.Str(m, "track") != "online");
            object gl = J.Get(d, "goLive");
            bool onlinePath = gl != null && online.Count > 0;
            List<object> counted = onlinePath ? online : modules;
            int passed = counted.FindAll(m => J.Int(m, "status") == 2).Count, total = counted.Count;
            bool academyDone = total > 0 && passed == total;
            _body.Add(Ui.Box("pad", Ui.Box("card row", Form.Glyph("GraduationCap"), Ui.Box("grow set-words",
                Ui.Text(passed + " of " + total + (onlinePath ? " online coaching" : "") + " modules complete", "body wrap", Ui.Bold),
                onlinePath ? Ui.Text("Finish these to unlock certification. The in-studio modules are optional.", "body-2 wrap", Ui.Regular) : null,
                Ui.Progress(total > 0 ? passed / (float)total : 0).Margin(8)))));
            if (onlinePath && academyDone)
            {
                if (J.Flag(gl, "certPending")) _body.Add(Ui.Box("pad", Ui.Box("card row", Ui.Icon("Medal", 24, Ui.Amber), Ui.Box("grow set-words", Ui.Text("Certification submitted", "body", Ui.Bold), Ui.Text("Puttr is grading it. You will get a push and an email with the result.", "body-2 wrap", Ui.Regular)))));
                else if (J.Flag(gl, "certPassed")) _body.Add(Ui.Box("pad", Ui.Box("card row", Ui.Icon("Medal", 24, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Certified", "body", Ui.Bold), Ui.Text("You can come back here to review any module at any time.", "body-2 wrap", Ui.Regular)))));
                else _body.Add(Ui.Box("pad", Ui.Box("card row", Ui.Icon("Medal", 24, Color.white), Ui.Box("grow set-words", Ui.Text("Start your certification lesson", "body", Ui.Bold), Ui.Text("Modules done. Coach one practice lesson end to end and Puttr grades it.", "body-2 wrap", Ui.Regular)), Ui.Icon("CaretRight", 18, Color.white)).OnTap(() => BeginCertification(_app, Root))));
            }
            if (onlinePath) { Section("Online Coaching · Required", online); Section("In-Studio Coaching · Optional", studio); }
            else Section("In-Studio Coaching", studio.Count > 0 ? studio : modules);
            _body.Add(Ui.Box("pad", Ui.Box("card row", Ui.Icon("Play", 18, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Practice space", "body", Ui.Bold), Ui.Text("A sandbox session to play with the review tools. Nothing you do here reaches a real player.", "body-2 wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint)).OnTap(() => OpenPractice(_app, Root))));
        }

        void Section(string title, List<object> modules)
        {
            if (modules.Count == 0) return;
            _body.Add(Ui.Section(title));
            VisualElement card = Ui.Box("coach-list");
            foreach (object m in modules)
            {
                object module = m;
                int status = J.Int(m, "status");
                string chip = status == 2 ? "Passed" + (J.Get(m, "quizScore") != null ? " · " + J.Int(m, "quizScore") + "%" : "") : status == 1 ? "Take quiz" : "Start";
                Label right = Ui.Text(chip, "chip coach-chip", Ui.Bold);
                right.style.color = status == 2 ? Ui.Bright : status == 1 ? Ui.Amber : Ui.Faint;
                VisualElement row = Ui.Box("set-row", Ui.Num(J.Int(m, "num").ToString(), "num-20").Width(32), Ui.Box("grow set-words", Ui.Text(J.Str(m, "title"), "body", Ui.Bold), Ui.Text(J.Str(m, "summary"), "body-2 wrap", Ui.Regular)), right);
                card.Add(row.OnTap(() => _app.Push(new AcademyModuleScreen(_app, module))));
            }
            _body.Add(Ui.Box("pad", card));
        }
    }

    public sealed class AcademyModuleScreen : AppScreen
    {
        public AcademyModuleScreen(App app, object m)
        {
            Root = Page.Make(app, "Module " + J.Int(m, "num"), out VisualElement body);
            VisualElement p = Ui.Box("pad");
            body.Add(p);
            p.Add(Ui.Title(J.Str(m, "title"), "title-20"));
            if (J.Int(m, "status") == 2) p.Add(Ui.Box("row", Ui.Icon("CheckCircle", 16, Ui.Bright), Ui.Text("Passed" + (J.Get(m, "quizScore") != null ? " · " + J.Int(m, "quizScore") + "%" : ""), "body-2", Ui.Bold).Margin(0)).Margin(8));
            if (J.Str(m, "videoUrl").Length > 0) p.Add(VideoBox.Make(J.Str(m, "videoUrl"), 210).Margin(12));
            foreach (object l in J.List(J.Get(m, "lessons")))
            {
                VisualElement card = Ui.Box("card", Ui.Text(J.Str(l, "heading"), "body wrap", Ui.Bold), Ui.Text(J.Str(l, "body"), "sub wrap", Ui.Regular).Margin(8));
                if (J.Str(l, "imageUrl").Length > 0)
                {
                    VisualElement img = Ui.Box("thumb-big").Margin(12);
                    img.style.height = 180;
                    Images.Load(J.Str(l, "imageUrl"), t => { if (t != null) { img.style.backgroundImage = new StyleBackground(t); img.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit; } });
                    card.Add(img);
                }
                if (J.Str(l, "videoUrl").Length > 0) card.Add(VideoBox.Make(J.Str(l, "videoUrl"), 180).Margin(12));
                string yt = J.Str(l, "youtubeUrl");
                if (yt.Length > 0) card.Add(Ui.Box("row", Ui.Icon("YoutubeLogo", 18, Ui.Red), Ui.Text("Watch on YouTube", "sec-link", Ui.Medium).Margin(0)).Margin(12).OnTap(() => Application.OpenURL(yt)));
                p.Add(card.Margin(12));
            }
            if (J.List(J.Get(m, "quiz")).Count > 0)
                body.Add(Form.Bottom(Ui.Button(J.Int(m, "status") == 2 ? "Retake quiz" : "Take the quiz", () =>
                {
                    // Opening the quiz means the lessons were read.
                    App.Run(Api.Post("/coach/academy/" + J.Str(m, "key") + "/complete", new Dictionary<string, object>(), null, null));
                    app.Push(new AcademyQuizScreen(app, m));
                })));
        }
    }

    public sealed class AcademyQuizScreen : AppScreen
    {
        readonly App _app;
        readonly object _module;
        readonly List<object> _quiz;
        readonly int?[] _answers;
        readonly VisualElement _body;
        int _at;
        bool _sending;

        public AcademyQuizScreen(App app, object module)
        {
            _app = app;
            _module = module;
            _quiz = J.List(J.Get(module, "quiz"));
            _answers = new int?[_quiz.Count];
            _body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(_body);
            Root = scroll;
            Draw();
        }

        void Draw()
        {
            _body.Clear();
            _body.Add(Ui.Box("", Ui.Box("top", Ui.Text("‹", "back back-chevron", Ui.Medium).OnTap(() => { if (_at > 0) { _at--; Draw(); } else _app.Pop(); }), Ui.Text("Question " + (_at + 1) + " of " + _quiz.Count, "top-title", Ui.Bold), Ui.Box("back-balance")),
                Ui.Box("pad", Ui.Progress((_at + 1) / (float)Math.Max(1, _quiz.Count)))));
            object q = _quiz[_at];
            VisualElement p = Ui.Box("pad", Ui.Text(J.Str(q, "q"), "body wrap", Ui.Bold));
            List<object> options = J.List(J.Get(q, "options"));
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                bool on = _answers[_at] == i;
                p.Add(Ui.Box(on ? "card row coach-on" : "card row", Ui.Icon(on ? "RadioButton" : "Circle", 20, on ? Ui.Bright : Ui.Faint), Ui.Text(options[i] as string ?? J.Str(options[i], "text"), "body grow wrap", Ui.Medium).Margin(0))
                    .Margin(12).OnTap(() => { _answers[_at] = index; Draw(); }));
            }
            _body.Add(p);
            bool last = _at == _quiz.Count - 1, chosen = _answers[_at] != null;
            VisualElement go = Ui.Button(_sending ? "Checking..." : last ? "Submit" : "Next", () =>
            {
                if (!chosen || _sending) return;
                if (!last) { _at++; Draw(); return; }
                App.Run(Send());
            });
            if (!chosen) go.AddToClassList("btn-off");
            _body.Add(Form.Bottom(go));
        }

        IEnumerator Send()
        {
            _sending = true; Draw();
            var answers = new List<object>();
            foreach (int? a in _answers) answers.Add(a ?? -1);
            object result = null; string error = null;
            yield return Api.Post("/coach/academy/" + J.Str(_module, "key") + "/quiz", new Dictionary<string, object> { { "answers", answers } }, r => result = r, e => error = e ?? "failed");
            _sending = false;
            if (result == null) { _body.Clear(); _body.Add(Page.Note("We could not save your quiz. Check your connection and try again.")); _body.Add(Form.Bottom(Ui.Button("Try again", () => App.Run(Send())))); yield break; }
            bool passed = J.Flag(result, "passed");
            _body.Clear();
            _body.Add(FreePuttScreen.Space(32));
            _body.Add(Ui.Box("pad center-box", Ui.Icon(passed ? "CheckCircle" : "XCircle", 56, passed ? Ui.Bright : Ui.Amber),
                Ui.Title(passed ? "Module passed" : "Not quite", "title center").Margin(12),
                Ui.Text(J.Int(result, "score") + "%   ·   " + J.Int(result, "correct") + " of " + J.Int(result, "total") + " correct", "body center", Ui.Bold).Margin(8),
                Ui.Text(passed ? "Nice work. Your progress is saved." : "You need 80% to pass. Review the module and give it another go, retakes are free.", "sub center wrap", Ui.Regular).Margin(8)));
            _body.Add(Form.Bottom(Ui.Button(passed ? "Back to Academy" : "Review the module", () =>
            {
                _app.Pop();
                if (passed && _app.Top is AcademyModuleScreen) _app.Pop();
            })));
        }
    }
}
