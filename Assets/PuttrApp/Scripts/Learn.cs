// Train & learn (puttr-app ImproveScreen.js "LearnScreen", improve/LessonView, LessonDrill): putting
// lessons (GET /improve/lessons/, category LEARN) first, then warm-ups, putting stretches, strength and
// mobility (GET /improve/workouts/, category BODY), each a video with an optional drill on the 3D ramp.
// The first two in each section are free; the rest are Puttr Club. Drills are saved nowhere, as in the app.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Library
    {
        public const string VideoRoot = "https://storage.googleapis.com/improve.puttr.co/";
        // LearnScreen sections, in order: key, title, words, from workouts (BODY) or lessons (LEARN).
        // The putting lessons lead, then the body work (Matt, 2026-11-17).
        public static readonly (string key, string title, string words, bool body, string icon, Color colour)[] Sections =
        {
            ("BASICS", "Putting basics", "Basic fundamentals of putting", false, "GraduationCap", Color.white),
            ("ADVANCED", "Advanced putting", "Advanced concepts to improve your putting", false, "Lightning", Color.white),
            ("WARMUP", "Warm up", "Get warmed up before your practice session", true, "FillFire", Color.white),
            ("PUTTING", "Putting stretches", "Train to putt at the highest level", true, "Target", Color.white),
            ("MOBILITY", "Strength & mobility", "Focus on parts of your body that help with golf", true, "Lightning", Color.white),
        };

        public static List<object> Lessons, Workouts;

        public static void Load(Action done)
        {
            int waiting = 2;
            void Then() { if (--waiting == 0) done(); }
            App.Run(Api.Get("/improve/lessons/", data => { Lessons = J.List(data); Then(); }, _ => { Lessons = Lessons ?? new List<object>(); Then(); }));
            App.Run(Api.Get("/improve/workouts/", data => { Workouts = J.List(data); Then(); }, _ => { Workouts = Workouts ?? new List<object>(); Then(); }));
        }

        public static List<object> In(string key)
        {
            var list = new List<object>();
            foreach (object l in (Lessons ?? new List<object>())) if (J.Str(l, "subCategory") == key) list.Add(l);
            foreach (object l in (Workouts ?? new List<object>())) if (J.Str(l, "subCategory") == key) list.Add(l);
            list.Sort((a, b) => J.Int(a, "orderNum").CompareTo(J.Int(b, "orderNum")));
            return list;
        }

        /// <summary>The Improve tab's six tiles (CoachHome libraryCard), two to a row.</summary>
        public static VisualElement Tiles(App app)
        {
            VisualElement grid = Ui.Box("lib-grid");
            foreach (var s in Sections)
            {
                string key = s.key;
                grid.Add(Ui.Box("card lib-tile", Form.Glyph(s.icon), Ui.Text(s.title, "lib-title", Ui.Bold)).OnTap(() => app.Push(new LearnScreen(app, key))));
            }
            grid.Add(Ui.Box("card lib-tile", Form.Glyph("GraduationCap"), Ui.Text("Browse all", "lib-title", Ui.Bold)).OnTap(() => app.Push(new LearnScreen(app, null))));
            return grid;
        }

        public static bool Locked(App app, int index) => index >= 2 && !Club.Member(app.Profile);
    }

    public sealed class LearnScreen : AppScreen
    {
        readonly App _app;
        readonly string _focus;
        readonly VisualElement _body;
        readonly ScrollView _scroll;

        public LearnScreen(App app, string focus)
        {
            _app = app;
            _focus = focus;
            _scroll = Page.Make(app, "Train & learn", out _body);
            Root = _scroll;
            _body.Add(Ui.Box("pad", Ui.Text("Putting lessons, warm-ups, stretches and mobility.", "sub", Ui.Regular)));
            _body.Add(Page.Loading());
            Library.Load(Draw);
        }

        void Draw()
        {
            _body.Clear();
            _body.Add(Ui.Box("pad", Ui.Text("Putting lessons, warm-ups, stretches and mobility.", "sub", Ui.Regular)));
            VisualElement focused = null;
            foreach (var s in Library.Sections)
            {
                List<object> lessons = Library.In(s.key);
                VisualElement section = Ui.Box("");
                section.Add(Ui.Section(s.title));
                section.Add(Ui.Box("pad", Ui.Text(s.words, "body-2", Ui.Regular)));
                ScrollView row = Ui.Scroll(true);
                row.style.marginTop = 8;
                if (lessons.Count == 0) row.Add(Ui.Box("pad", Ui.Text("Nothing here yet.", "body-2", Ui.Regular)));
                for (int i = 0; i < lessons.Count; i++)
                {
                    object lesson = lessons[i];
                    int index = i;
                    // The thumbnail in a bordered window, and everything said about it under the picture, never over it.
                    VisualElement thumb = Ui.Box("window learn-thumb");
                    Images.Into(thumb, J.Str(lesson, "thumbnail"));
                    VisualElement tags = Ui.Box("chips");
                    object meta = J.Parse(J.Str(lesson, "meta", "{}"));
                    if (J.Flag(meta, "equipmentRequired")) tags.Add(Ui.Text("Equipment", "chip", Ui.Bold));
                    if (Library.Locked(_app, i)) tags.Add(Ui.Text("Puttr Club", "chip chip-amber", Ui.Bold));
                    VisualElement card = Ui.Box("learn-card", thumb, Ui.Text(J.Str(lesson, "title"), "body learn-name", Ui.Bold), Ui.Text(J.Str(lesson, "description"), "body-2 lesson-desc", Ui.Regular), tags.childCount > 0 ? tags : null);
                    card.OnTap(() => { if (Library.Locked(_app, index)) Club.Require(_app, "Lessons", () => { }); else _app.Push(new LessonScreen(_app, lesson)); });
                    row.Add(card);
                }
                if (row.contentContainer.childCount > 0) row.contentContainer[0].style.marginLeft = 16;
                row.Add(FreePuttScreen.Space(0).Width(16));
                section.Add(row);
                _body.Add(section);
                if (s.key == _focus) focused = section;
            }
            if (focused != null) _scroll.schedule.Execute(() => _scroll.ScrollTo(focused)).StartingIn(250);
        }
    }

    /// <summary>A lesson: its video, the drill it recommends, what is up next, and a rating.</summary>
    public sealed class LessonScreen : AppScreen
    {
        readonly App _app;
        readonly object _lesson;

        public LessonScreen(App app, object lesson)
        {
            _app = app;
            _lesson = lesson;
            Root = Page.Make(app, J.Str(lesson, "title", "Lesson"), out VisualElement body);
            string path = J.Str(lesson, Screen.width > Screen.height && J.Str(lesson, "videoPathWide").Length > 0 ? "videoPathWide" : "videoPath");
            VisualElement hero = Ui.Box("window lesson-hero", Ui.Box("lesson-play", Ui.Icon("Play", 22, Color.white)));
            Images.Into(hero, J.Str(lesson, "thumbnail"));
            if (path.Length > 0) hero.OnTap(() => SetupGuide.Play(Root, path.StartsWith("http") ? path : Library.VideoRoot + path));
            body.Add(Ui.Box("pad", hero, Ui.Text(J.Str(lesson, "description"), "sub", Ui.Regular).Margin(12)));

            // The recommended drill, played on the 3D ramp.
            string drillType = J.Str(lesson, "drillType"), drillWords = J.Str(lesson, "drillDescription");
            if (drillType.Length > 0 || drillWords.Length > 0)
            {
                VisualElement card = Ui.Box("card", Ui.Text("RECOMMENDED DRILL", "caps caps-green", Ui.Bold), Ui.Text(drillWords, "sub", Ui.Regular).Margin(4));
                if (drillType.ToUpperInvariant() == "TEMPO") card.Add(Ui.Box("", Ui.Text("Metronome", "body", Ui.Bold), Metronome.Pill()).Margin(12));
                if (drillType.ToUpperInvariant() == "VIDEO") card.Add(Ui.Button("Camera setup", () => app.Push(new CameraSetupScreen(app)), "btn btn-ghost").Margin(12));
                if (drillType.Length > 0 && J.Str(lesson, "drillPutts").Length > 2) card.Add(Ui.Button("Start drill", () => app.Games.PlayLessonDrill(lesson)));
                body.Add(Ui.Box("pad", card).Margin(12));
            }

            // Up next in the same series, or the end of it.
            object next = Next(lesson);
            body.Add(Ui.Section(next != null ? "Up next" : "Series complete"));
            if (next != null)
            {
                bool locked = J.Int(lesson, "orderNum") >= 1 && !Club.Member(app.Profile);
                VisualElement row = Ui.Box("card list-row", Ui.Box("grow", Ui.Text(J.Str(next, "title"), "body", Ui.Bold), Ui.Text(J.Str(next, "description"), "body-2 lesson-desc", Ui.Regular)),
                    locked ? Ui.Text("Club", "chip chip-amber", Ui.Bold) : Ui.Icon("CaretRight", 16, Ui.Faint));
                row.OnTap(() => { if (locked) Club.Require(app, "Lessons", () => { }); else { app.Pop(); app.Push(new LessonScreen(app, next)); } });
                body.Add(row);
            }
            else body.Add(Ui.Box("pad", Ui.Button("Back to lessons", app.Pop, "btn btn-ghost")));

            if (!J.Flag(app.Profile, "isKid")) body.Add(Rating());
        }

        static object Next(object lesson)
        {
            List<object> series = Library.In(J.Str(lesson, "subCategory"));
            int order = J.Int(lesson, "orderNum");
            foreach (object l in series) if (J.Int(l, "orderNum") > order) return l;
            return null;
        }

        VisualElement Rating()
        {
            VisualElement box = Ui.Box("");
            box.Add(Ui.Section("How was this lesson?"));
            VisualElement row = Ui.Box("row");
            Label said = Ui.Text("", "sub", Ui.Regular);
            for (int i = 1; i <= 5; i++)
            {
                int stars = i;
                VisualElement star = Ui.Box("rate-star", Ui.Num(stars.ToString(), "num-20"));
                star.OnTap(() =>
                {
                    for (int k = 0; k < row.childCount; k++) row[k].EnableInClassList("rate-on", k < stars);
                    App.Run(Api.Post("/lessons/review", new Dictionary<string, object>
                    {
                        { "lessonId", J.Str(_lesson, "id") }, { "title", J.Str(_lesson, "title") }, { "stars", stars }, { "reviewText", "" },
                        { "platform", Application.platform == RuntimePlatform.Android ? "android" : "ios" },
                    }, _ => said.text = "Thanks for your feedback!", _ => said.text = "Could not send that. Try again."));
                });
                row.Add(star);
            }
            box.Add(Ui.Box("pad", row, said.Margin(8)));
            return box;
        }
    }
}
