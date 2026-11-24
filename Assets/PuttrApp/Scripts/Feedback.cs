// Feedback: "Rate this game" on every game over, and Settings > Send feedback ("How is Puttr?").
//
// 2026-11-22 (Matt): the card that came up now and then after a finished game ("How is Puttr so far?") is no longer
// raised: every game, quest stage, tournament round, League game and Tracks round asks for its own rating instead
// (GameCard, AskGame below). BackFromGame and Should are kept for the day it is wanted again; nothing calls them.
// What follows describes that card as Settings still opens it.
//
// How the games people play most ask (see docs/analytics.md): at a good moment, never mid-game; one tap
// first (five stars), then one optional follow-up shaped by the answer (what to fix for a low score, what
// they like for a high one), and room to type; easy to wave away; and not again for a
// long while. So:
//
//   When     back in the app after a FINISHED game (never an abandoned one), once the Player has
//            finished MinGames games and had the app a day; at most once a run.
//   Again    90 days after an answer, 30 after "Not now".
//   Always   Settings > Send feedback opens the same card, whenever.
//   A TV     stars with the remote; nothing to type.
//   A Kid    stars; nothing to type (a Kid profile types no free text anywhere).
//
// The answer goes to POST /feedback (MongoDB puttr_logs.app_feedback) with the device, the build and the
// game just played, and shows on the analytics dashboard beside the prompts shown (feedback_prompt_shown,
// feedback_dismissed: Analytics). A rating given and then waved away is still sent, as a rating alone.
// After five stars on an iPhone or iPad the system's own App Store rating sheet is asked for (the system
// decides whether it shows: at most three times a year).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Feedback
    {
        public const int MinGames = 3;
        public const long Day = 86400, AfterAnswer = 90 * Day, AfterNotNow = 30 * Day, AfterInstall = Day;
        const string FirstPref = "app2.feedback.first", AskedPref = "app2.feedback.asked", AnsweredPref = "app2.feedback.answered", GamesPref = "app2.feedback.games";

        static bool _askedThisRun, _gameJustFinished, _open;

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        static long Pref(string key) => long.TryParse(PlayerPrefs.GetString(key, "0"), out long v) ? v : 0;
        static void Pref(string key, long value) { PlayerPrefs.SetString(key, value.ToString()); PlayerPrefs.Save(); }

        /// <summary>The rule itself, apart from the device (for the tests). Times are seconds; 0 is never.</summary>
        public static bool Should(long now, long firstSeen, long lastAsked, long lastAnswered, int gamesFinished, bool askedThisRun)
        {
            if (askedThisRun || gamesFinished < MinGames) return false;
            if (firstSeen <= 0 || now - firstSeen < AfterInstall) return false;
            if (lastAnswered > 0 && now - lastAnswered < AfterAnswer) return false;
            if (lastAsked > 0 && now - lastAsked < AfterNotNow) return false;
            return true;
        }

        /// <summary>The follow-up question for a rating.</summary>
        public static string FollowUp(int rating) => rating >= 5 ? "What do you like most?" : rating == 4 ? "What would make it a 5?" : "What should we fix first?";

        /// <summary>Called once the app is up (App.Begin).</summary>
        public static void Begin(App app)
        {
            if (Pref(FirstPref) == 0) Pref(FirstPref, Now);
            Analytics.GameCompleted += () =>
            {
                PlayerPrefs.SetInt(GamesPref, PlayerPrefs.GetInt(GamesPref, 0) + 1);
                _gameJustFinished = true;
            };
        }

        /// <summary>Back in the app from a game (App.LeaveGame): the moment to ask, when it is time.</summary>
        public static void BackFromGame(App app)
        {
            bool finished = _gameJustFinished;
            _gameJustFinished = false;
            if (!finished || _open || Analytics.Off) return;
            if (!Should(Now, Pref(FirstPref), Pref(AskedPref), Pref(AnsweredPref), PlayerPrefs.GetInt(GamesPref, 0), _askedThisRun)) return;
            App.Run(Soon(app));
        }

        // The results have been read and the screen has settled before the card comes up.
        static IEnumerator Soon(App app)
        {
            yield return new WaitForSecondsRealtime(1.2f);
            if (app.Games != null && app.Games.Busy) yield break;   // straight into another game: not now
            Open(app, "post_game");
        }

        /// <summary>The card. trigger: "post_game" (asked) or "settings" (the Player opened it).</summary>
        public static void Open(App app, string trigger)
        {
            if (_open) return;
            _open = true;
            bool asked = trigger != "settings";
            if (asked) { _askedThisRun = true; Pref(AskedPref, Now); }
            string prompt = asked ? "How is Puttr so far?" : "How is Puttr?";
            Analytics.Track("feedback_prompt_shown", "trigger", trigger);

            int rating = 0;
            bool sent = false;
            var stars = new List<Star>();
            TextField words = null;
            VisualElement dim = null, more = Ui.Box("hidden");
            Label question = Ui.Text("", "body fb-ask", Ui.Medium);

            void Send(string comment)
            {
                if (sent || rating == 0 && string.IsNullOrEmpty(comment)) return;
                sent = true;
                Pref(AnsweredPref, Now);
                var body = Analytics.Device();
                body["rating"] = rating;
                if (!string.IsNullOrEmpty(comment)) body["comment"] = comment;
                body["trigger"] = trigger;
                body["prompt"] = prompt;
                body["context"] = new Dictionary<string, object>
                {
                    { "screen", Analytics.CurrentScreen ?? "" }, { "game", asked ? Analytics.LastGame ?? "" : "" },
                    { "games_finished", PlayerPrefs.GetInt(GamesPref, 0) }, { "member", Club.Member(app.Profile) },
                };
                App.Run(Post(body));
            }

            void Close(bool notNow)
            {
                if (!_open) return;
                _open = false;
                // Stars given and then waved away: the rating still counts.
                if (!sent && rating > 0) Send(null);
                else if (!sent && notNow) Analytics.Track("feedback_dismissed", "trigger", trigger);
                dim.RemoveFromHierarchy();
            }

            VisualElement row = Ui.Box("row fb-stars");
            for (int i = 1; i <= 5; i++)
            {
                int value = i;
                var star = new Star();
                star.OnTap(() =>
                {
                    rating = value;
                    for (int k = 0; k < stars.Count; k++) stars[k].On = k < value;
                    question.text = FollowUp(value);
                    more.RemoveFromClassList("hidden");
                });
                stars.Add(star);
                row.Add(star);
            }

            more.Add(question);
            // Typing: not with a remote, and not on a Kid profile.
            if (!Tv.On && !Kid.IsKid(app.Profile))
            {
                words = new TextField { multiline = true, maxLength = 1000 };
                words.textEdition.placeholder = "Tell us more (optional)";
                words.textEdition.hidePlaceholderOnFocus = false;
                words.textSelection.cursorColor = Ui.Bright;
                words.AddToClassList("field");
                words.AddToClassList("fb-words");
                more.Add(words);
            }
            VisualElement card = null;
            more.Add(Ui.Button("Send", () =>
            {
                int given = rating;
                Send(words != null ? words.value.Trim() : null);
                card.Clear();
                card.Add(Ui.Title("Thank you", "title-20 fb-title"));
                card.Add(Ui.Text("It goes straight to the team building Puttr.", "body-2 fb-line", Ui.Regular));
                card.schedule.Execute(() => { Close(false); if (asked && given >= 5) StoreReview(); }).StartingIn(1600);
                Perf.Wake(2f);
            }).Margin(12));

            card = Ui.Box("modal",
                Ui.Title(prompt, "title-20 fb-title"),
                Ui.Text("Tap a star. Your answer goes straight to the team.", "body-2 fb-line", Ui.Regular),
                row, more,
                Ui.Button(asked ? "Not now" : "Close", () => Close(true), "btn btn-ghost").Margin(8));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => Close(true));
            App.Layer(app.Toasts).Add(dim);
            if (stars.Count > 2) Tv.FocusOn(stars[2]);
            Perf.Wake(2f);
        }

        // ── Rate this game: five stars on every game over (Matt, 2026-11-22) ──

        public const string GamePrompt = "Rate this game";

        /// <summary>The body POST /feedback takes for a game's rating (trigger "game_over").</summary>
        public static Dictionary<string, object> GameBody(string game, int rating, string comment, bool member)
        {
            var body = Analytics.Device();
            body["rating"] = Mathf.Clamp(rating, 1, 5);
            if (!string.IsNullOrEmpty(comment)) body["comment"] = comment;
            body["tags"] = new List<object>();
            body["trigger"] = "game_over";
            body["prompt"] = GamePrompt;
            body["context"] = new Dictionary<string, object> { { "screen", Analytics.CurrentScreen ?? "" }, { "game", game ?? "" }, { "member", member } };
            return body;
        }

        /// <summary>True when a rating under five gets a box to say why: not with a TV remote, not on a Kid profile.</summary>
        public static bool AsksWhy(int rating, bool tv, bool kid) => rating < 5 && !tv && !kid;

        /// <summary>
        /// "Rate this game" for a game over screen: five stars. Five sends at once. Under five opens a box to say
        /// what would make it a five, with Send (a rating left without Send still goes, on its own). Null in host
        /// mode, where whoever is at the Puttr is a guest.
        /// </summary>
        public static VisualElement GameCard(App app, string game)
        {
            if (HostMode.On) return null;
            int rating = 0;
            bool sent = false;
            var stars = new List<Star>();
            TextField words = null;
            VisualElement more = Ui.Box("hidden");
            Label line = Ui.Text("Tap a star.", "body-2 fb-line", Ui.Medium);
            VisualElement row = Ui.Box("row fb-stars fb-game-stars");
            VisualElement card = Ui.Box("glass fb-game", Ui.Caps(GamePrompt, "caps caps-green fb-title"), row, line, more);

            void Send()
            {
                if (sent || rating == 0) return;
                sent = true;
                _askedThisRun = true;   // one question a visit: the app's own card waits for another day
                string comment = words != null ? words.value.Trim() : null;
                App.Run(Post(GameBody(game, rating, comment, Club.Member(app.Profile))));
                Analytics.Track("game_rated", "game", game ?? "", "rating", rating);
                more.AddToClassList("hidden");
                foreach (Star s in stars) s.pickingMode = PickingMode.Ignore;
                line.text = "Thank you. It goes straight to the team.";
            }

            for (int i = 1; i <= 5; i++)
            {
                int value = i;
                var star = new Star();
                star.OnTap(() =>
                {
                    if (sent) return;
                    rating = value;
                    for (int k = 0; k < stars.Count; k++) stars[k].On = k < value;
                    if (!AsksWhy(value, Tv.On, Kid.IsKid(app.Profile))) { Send(); return; }
                    line.text = FollowUp(value);
                    more.RemoveFromClassList("hidden");
                });
                stars.Add(star);
                row.Add(star);
            }
            if (!Tv.On && !Kid.IsKid(app.Profile))
            {
                words = new TextField { multiline = true, maxLength = 1000 };
                words.textEdition.placeholder = "Tell us more (optional)";
                words.textEdition.hidePlaceholderOnFocus = false;
                words.textSelection.cursorColor = Ui.Bright;
                words.AddToClassList("field");
                words.AddToClassList("fb-words");
                more.Add(words);
            }
            more.Add(Ui.Button("Send", Send).Margin(10));
            // Stars given and the screen left without Send: the rating still counts.
            card.RegisterCallback<DetachFromPanelEvent>(_ => Send());
            return card;
        }

        /// <summary>The same card over the page, for a game whose game over is inside the arcade (its own 3D card): shown
        /// once the Player is back in the app from a game that finished.</summary>
        public static void AskGame(App app, string game)
        {
            if (HostMode.On || _open) return;
            VisualElement rate = GameCard(app, game);
            if (rate == null) return;
            _open = true;
            rate.RemoveFromClassList("glass");
            VisualElement dim = null;
            void Close() { if (!_open) return; _open = false; dim.RemoveFromHierarchy(); }
            VisualElement card = Ui.Box("modal", Ui.Title(game ?? "", "title-20 fb-title"), rate, Ui.Button("Close", Close, "btn btn-ghost").Margin(8));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(Close);
            App.Layer(app.Toasts).Add(dim);
            Perf.Wake(2f);
        }

        static IEnumerator Post(Dictionary<string, object> body)
        {
            if (Analytics.Off) yield break;   // the editor, made-up data: the card works, nothing is sent
            string token = null;
            if (Auth.SignedIn) yield return Auth.Token(t => token = t);
            long status = 0;
            yield return Api.Send("POST", Config.ApiUrl + "/feedback", J.Write(body), token, (s, _) => status = s);
            // One more try a little later if it never got there: an answer is worth a second go.
            if (status != 0 && status < 500) yield break;
            yield return new WaitForSecondsRealtime(20f);
            yield return Api.Send("POST", Config.ApiUrl + "/feedback", J.Write(body), token, (s, _) => status = s);
        }

        // The system's own rating sheet (StoreKit). It may or may not show: the system decides.
        static void StoreReview()
        {
#if UNITY_IOS && !UNITY_EDITOR
            UnityEngine.iOS.Device.RequestStoreReview();
#endif
        }

        /// <summary>A star to tap: an outline until chosen, then filled.</summary>
        sealed class Star : VisualElement
        {
            bool _on;
            public bool On { get => _on; set { _on = value; MarkDirtyRepaint(); } }

            public Star()
            {
                AddToClassList("fb-star");
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext context)
            {
                Rect r = contentRect;
                float size = Mathf.Min(r.width, r.height);
                if (size <= 0f) return;
                Vector2 centre = r.center;
                float outer = size * 0.46f, inner = outer * 0.42f;
                Painter2D p = context.painter2D;
                p.BeginPath();
                for (int i = 0; i < 10; i++)
                {
                    float angle = -Mathf.PI / 2f + i * Mathf.PI / 5f, radius = i % 2 == 0 ? outer : inner;
                    var point = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                p.ClosePath();
                if (_on) { p.fillColor = Ui.Amber; p.Fill(); }
                p.strokeColor = _on ? Ui.Amber : Ui.Faint;
                p.lineWidth = Mathf.Max(1.5f, size * 0.05f);
                p.lineJoin = LineJoin.Round;
                p.Stroke();
            }
        }
    }
}
