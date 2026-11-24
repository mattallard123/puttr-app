// Coach settings, the sheet behind the gear beside Chat on Improve (puttr-app coach/mvp/CoachSettings): the coach (four
// personas, each introducing themselves when picked while the voice is on), Coach voice, the daily goal and the reminder
// time. Edits stay on the sheet until Save; closing it cancels. Save sends what the first run sends:
//   POST /coach/settings {personaId, voiceOn, dailyGoal, reminderTime, reminderUtcMinutes}
//   POST /streaks/goal/  {dailyGoal}   (the Home goal ring reads the streak row)
// then the coach's voice switches at once and CoachVoice.SettingsChanged tells the screens that show the goal.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class CoachSettingsMath
    {
        /// <summary>"6:30 PM" (the app's moment 'h:mm A') to hour and minute; 6:30 PM when it cannot be read.</summary>
        public static void ParseTime(string text, out int hour, out int minute)
        {
            hour = 18; minute = 30;
            if (string.IsNullOrEmpty(text)) return;
            if (DateTime.TryParseExact(text.Trim().ToUpperInvariant(), new[] { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t))
            {
                hour = t.Hour; minute = t.Minute;
            }
        }

        public static string FormatTime(int hour, int minute) => DateTime.Today.AddHours(hour).AddMinutes(minute).ToString("h:mm tt", CultureInfo.InvariantCulture);

        /// <summary>The persona's colour (CoachPersonas accent), from the guidelines' table: Maya green, Theo white, Riley red, Jax amber. The sheet itself marks the picked coach in white, not in this colour.</summary>
        public static Color Accent(string id) =>
            id == "theo" ? Color.white : id == "riley" ? Ui.Red : id == "jax" ? Ui.Amber : Ui.Bright;
    }

    public static class CoachSettingsSheet
    {
        static readonly int[] Goals = { 10, 20, 30, 50 };

        /// <param name="settings">GET /coach/settings as last read (null: the defaults).</param>
        /// <param name="saved">Runs with what was saved, once the sheet has closed.</param>
        public static void Open(App app, VisualElement over, object settings, Action<Dictionary<string, object>> saved)
        {
            Coaches.Persona before = CoachVoice.Persona;
            string persona = J.Str(settings, "personaId", before != null ? before.Id : "maya");
            bool voice = J.Get(settings, "voiceOn") == null || J.Flag(settings, "voiceOn");
            int goal = J.Int(settings, "dailyGoal", 10);
            if (goal <= 0) goal = 10;
            CoachSettingsMath.ParseTime(J.Str(settings, "reminderTime"), out int hour, out int minute);
            bool picking = false;

            VisualElement dim = null;
            VisualElement sheet = Ui.Box("putter-sheet cs-sheet");
            sheet.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            void Close(bool keep)
            {
                CoachVoice.Hush();
                // A coach heard but not saved: the voice goes back to the Player's coach.
                if (!keep) CoachVoice.Persona = before;
                dim?.RemoveFromHierarchy();
            }

            void Draw()
            {
                sheet.Clear();
                sheet.Add(Ui.Box("row cs-head", Ui.Title("Coach settings", "title-20 grow"), Ui.Box("putter-sheet-close", Ui.Icon("X", 16, Ui.Faint)).OnTap(() => Close(false))));
                ScrollView scroll = Ui.Scroll();
                scroll.AddToClassList("cs-scroll");
                sheet.Add(scroll);

                // The coach.
                scroll.Add(Form.Label("Coach"));
                VisualElement coaches = Ui.Box("cs-coaches");
                foreach (Coaches.Persona p in Coaches.All)
                {
                    Coaches.Persona pick = p;
                    bool on = p.Id == persona;
                    VisualElement face = Coaches.Avatar(p);
                    face.AddToClassList("cs-face");
                    Label style = Ui.Text(FirstRunMath.Personality.TryGetValue(p.Id, out string words) ? words.ToUpperInvariant() : p.Style.ToUpperInvariant(), "cs-style", Ui.Bold);
                    VisualElement card = Ui.Box(on ? "cs-coach cs-coach-on" : "cs-coach", face, Ui.Text(p.Name, on ? "cs-name cs-name-on" : "cs-name", on ? Ui.Bold : Ui.Medium), style);
                    coaches.Add(card.OnTap(() =>
                    {
                        persona = pick.Id;
                        // Heard in their own voice when the voice is on (speakCoachIntro).
                        if (voice && FirstRunMath.Intro.TryGetValue(pick.Id, out string intro)) CoachVoice.Intro(pick, intro);
                        Draw();
                    }));
                }
                scroll.Add(coaches);

                // The voice.
                VisualElement knob = Ui.Box("switch-knob");
                VisualElement toggle = Ui.Box(voice ? "switch switch-on" : "switch", knob);
                scroll.Add(Ui.Box("card row cs-card", Ui.Box("grow", Ui.Text("Coach voice", "body", Ui.Bold), Ui.Text("Spoken tips during sessions and setup", "body-2", Ui.Regular)), toggle)
                    .OnTap(() => { voice = !voice; if (!voice) CoachVoice.Hush(); Draw(); }));

                // The daily goal.
                scroll.Add(Form.Label("Daily goal (putts)"));
                VisualElement pills = Form.Pills(new[] { "10", "20", "30", "50" }, Array.IndexOf(Goals, goal), i => goal = Goals[i]);
                pills.AddToClassList("cs-goals");
                scroll.Add(pills);

                // The reminder.
                scroll.Add(Form.Label("Remind me at"));
                Label time = Ui.Text(CoachSettingsMath.FormatTime(hour, minute), "body grow", Ui.Bold);
                scroll.Add(Ui.Box("card row cs-card", time, Ui.Text(picking ? "done" : "tap to change", "body-2", Ui.Medium), Ui.Icon("CaretRight", 14, Ui.Faint))
                    .OnTap(() => { picking = !picking; Draw(); }));
                if (picking)
                {
                    // In place of the phone's time wheel: the hour and the minutes (five at a time).
                    VisualElement hours = FreePuttScreen.Stepper(() => DateTime.Today.AddHours(hour).ToString("h tt", CultureInfo.InvariantCulture), d => { hour = (hour + d + 24) % 24; time.text = CoachSettingsMath.FormatTime(hour, minute); });
                    VisualElement minutes = FreePuttScreen.Stepper(() => ":" + minute.ToString("00", CultureInfo.InvariantCulture), d => { minute = ((minute / 5) * 5 + d * 5 + 60) % 60; time.text = CoachSettingsMath.FormatTime(hour, minute); });
                    minutes.style.marginLeft = 8;
                    scroll.Add(Ui.Box("row cs-picker", hours, minutes));
                }

                sheet.Add(Ui.Button("Save", Save, "btn cs-save"));
            }

            void Save()
            {
                string reminder = CoachSettingsMath.FormatTime(hour, minute);
                var partial = new Dictionary<string, object>
                {
                    { "personaId", persona }, { "voiceOn", voice }, { "dailyGoal", goal }, { "reminderTime", reminder },
                    // Minutes after UTC midnight when the local reminder fires, so the server's cron needs no time zone.
                    { "reminderUtcMinutes", FirstRunMath.UtcMinutes(hour, minute) },
                };
                // The voice and the coach change on the phone at once, as the app's local cache does.
                CoachVoice.Load(partial);
                Close(true);
                saved?.Invoke(partial);
                App.Run(Send(app, partial, goal));
            }

            Draw();
            dim = Ui.Box("putter-sheet-dim", sheet).OnBackdropTap(() => Close(false));
            StyleSheet css = Resources.Load<StyleSheet>("PuttrApp/Improve");
            if (css != null) dim.styleSheets.Add(css);
            float inset = App.I != null ? App.I.SafeInsets().y : 0f;
            sheet.style.paddingBottom = 24f + inset;
            sheet.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            App.Layer(over).Add(dim);
            // Slide up from the bottom, as the app's sheet does.
            sheet.style.translate = new Translate(0, Length.Percent(100));
            sheet.schedule.Execute(() => { sheet.style.translate = new Translate(0, 0); Perf.Wake(0.5f); }).ExecuteLater(20);
            Perf.Wake(0.6f);
        }

        static System.Collections.IEnumerator Send(App app, Dictionary<string, object> partial, int goal)
        {
            bool failed = false;
            yield return Api.Post("/coach/settings", partial, null, _ => failed = true);
            yield return Api.Post("/streaks/goal/", new Dictionary<string, object> { { "dailyGoal", goal } }, null, _ => failed = true);
            if (failed) app.Toast("Couldn't save your coach settings. Check your connection and try again.");
            CoachVoice.Changed();
        }
    }
}
