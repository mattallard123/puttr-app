// The one game over page (docs/design-guidelines.md, Game over): every game ends on it, so they read the same.
//
// In order: the score (or who won), the personal best, PQS and the game's numbers, then Play again, Review game and
// Exit, then the rating; and the putts. On a phone that is one column that scrolls. On a wide screen (a tablet on its
// side) nothing scrolls off the page: the result, the actions and the rating are the left column, and the putts are a
// column of their own on the right that scrolls by itself (Matt, 2026-11-24).
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>One putt on the game over page: its number, where it was from, what the game called it, what it earned.</summary>
    public sealed class GameOverPutt
    {
        public string Number, From, Call, Points;
        public bool Good;
    }

    /// <summary>What a game over page shows. Leave out what a game does not have: the page keeps its order.</summary>
    public sealed class GameOverSpec
    {
        public string Eyebrow;                 // "Final · Center Cut"
        public string Score;                   // the one big number; null when Outcome leads (a game for more than one)
        public string ScoreCaption;            // "points · 6 of 9 holed"
        public string Outcome;                 // "Ava wins", or the round's own line ("3rd of 212")
        public List<(string name, string score, Color colour)> Standings;
        public List<(string value, string caption)> Stats;
        public string GameId;                  // games.id, for the personal best
        public double? ScoreValue;
        public bool LowerIsBetter;
        public string ReviewId;                // the saved game's id: Review game opens it
        public bool XpUnpaid;                  // no one saved game to name (a quest stage, a Tracks round): the XP panel asks for whatever is unpaid
        public List<AnalysisMath.Putt> PqsPutts;   // this game's putts: what it did to PQS. Null for none.
        public List<GameOverPutt> Putts;
        public string PuttsTitle = "Putts";
        public VisualElement Extra;            // a game's own block under the score (a quest's stars and what opened)
        public VisualElement Detail;           // a game's own second column in place of the putts (a Tracks scorecard)
        public string DetailTitle;
        public string RateAs;                  // the name the rating is sent under; null for no rating
        public Action Again;
        public string AgainLabel = "Play again";
        public Action Exit;
        public string Note;                    // "Test putts: nothing was saved."
        /// <summary>A game's own buttons in place of Play again and Exit (a quest: the next stage, replay, the map): the
        /// label, what it does (it leaves the page itself), and whether it is the page's one main button.</summary>
        public List<(string label, Action act, bool main)> Actions;
        /// <summary>A line under the buttons (a TV: "Or hit a putt to play 1-3").</summary>
        public string ActionsHint;
    }

    /// <summary>A game over page as a screen of its own (an arcade game's: the others have their own screen classes).</summary>
    public sealed class GameOverScreen : AppScreen
    {
        public GameOverScreen(VisualElement page) { Root = page; }
    }

    public static class GameOver
    {
        /// <summary>The page. Root is not a scroll view: it lays itself out as one column or two as its width allows.</summary>
        public static VisualElement Build(App app, GameOverSpec s)
        {
            Action exit = s.Exit ?? app.ExitToTab;
            VisualElement root = Ui.Box("screen go");

            // ── The result ──
            VisualElement result = Ui.Box("go-result");
            if (!string.IsNullOrEmpty(s.Eyebrow)) result.Add(Ui.Caps(s.Eyebrow, "caps caps-green go-center"));
            if (!string.IsNullOrEmpty(s.Score))
            {
                result.Add(Ui.Num(s.Score, "num-44 go-score"));
                if (!string.IsNullOrEmpty(s.ScoreCaption)) result.Add(Ui.Caps(s.ScoreCaption, "caps go-center"));
                if (!string.IsNullOrEmpty(s.Outcome)) result.Add(Ui.Text(s.Outcome, "body-2 go-outcome", Ui.Medium));
            }
            else if (!string.IsNullOrEmpty(s.Outcome)) result.Add(Ui.Title(s.Outcome, "title go-title"));
            if (s.Standings != null && s.Standings.Count > 0)
            {
                VisualElement table = Ui.Box("go-block go-gap");
                for (int i = 0; i < s.Standings.Count; i++)
                {
                    VisualElement swatch = Ui.Box("go-swatch");
                    swatch.style.backgroundColor = s.Standings[i].colour;
                    table.Add(Ui.Box(i == 0 ? "go-row go-row-first" : "go-row", Ui.Num((i + 1).ToString(), "go-n"), swatch,
                        Ui.Text(s.Standings[i].name, "body go-grow", Ui.Bold), Ui.Num(s.Standings[i].score, "num-20")));
                }
                result.Add(table);
            }
            // The personal best: a line of its own, in gold when this game set it.
            var best = new VisualElement();
            result.Add(best);
            if (!string.IsNullOrEmpty(s.GameId) && s.ScoreValue.HasValue && !HostMode.On)
                App.Run(Api.Kept("/profile/games/", data => DrawBest(best, GameTags.Bests(J.List(data)), s)));
            if (s.Extra != null) { s.Extra.AddToClassList("go-gap"); result.Add(s.Extra); }
            if (s.Stats != null && s.Stats.Count > 0) result.Add(Stats(s.Stats));
            // ── What it did for the Player: PQS, XP. Under the result in one column; in two, the result goes over
            // the putts on the right and this leads the left column, so the left fits without scrolling (Matt, 2026-11-26). ──
            VisualElement more = Ui.Box("go-result");
            var rating = new VisualElement();
            more.Add(rating);
            if (s.PqsPutts != null && s.PqsPutts.Count > 0 && !HostMode.On) Pqs(rating, s.PqsPutts);
            if (!string.IsNullOrEmpty(s.Note)) more.Add(Ui.Text(s.Note, "body-2 go-outcome", Ui.Medium));
            // XP (Xp.cs): what this saved game paid, under PQS. Filled when the API answers; empty when it paid nothing.
            var xp = new VisualElement();
            more.Add(xp);
            var levelGate = new VisualElement();
            more.Add(levelGate);

            // ── What next: one main button at most ──
            VisualElement actions = Ui.Box("go-actions");
            bool review = !string.IsNullOrEmpty(s.ReviewId) && s.ReviewId != "0" && !Api.IsLocal(s.ReviewId);
            VisualElement Review() => Ui.Button("Review game", () => app.Push(new GameViewScreen(app, s.ReviewId)), "btn btn-ghost go-btn");
            if (s.Actions != null)
            {
                bool main = false;
                for (int i = 0; i < s.Actions.Count; i++)
                {
                    (string label, Action act, bool isMain) = s.Actions[i];
                    bool asMain = isMain && !main;
                    main |= asMain;
                    actions.Add(Ui.Button(label, act, asMain ? "btn go-btn" : "btn btn-ghost go-btn"));
                    if (i == 0 && review) actions.Add(Review());
                }
            }
            else
            {
                if (s.Again != null) actions.Add(Ui.Button(s.AgainLabel, () => { app.Pop(); s.Again(); }, "btn go-btn"));
                if (review) actions.Add(Review());
                actions.Add(Ui.Button("Exit", exit, s.Again != null ? "btn btn-ghost go-btn" : "btn go-btn"));
            }

            // A game that counted towards the Player's own putting (the same test as its PQS line) leads on to Full
            // analysis (Matt, 2026-11-25): one list row under the buttons, never a fourth button.
            if (s.PqsPutts != null && s.PqsPutts.Count > 0 && !HostMode.On)
            {
                VisualElement row = Rows.Row("Target", "See your analysis", () => AnalysisScreen.Open(app));
                row.AddToClassList("set-row-last");
                actions.Add(Ui.Box("glass set-card go-analysis", row));
            }

            if (!string.IsNullOrEmpty(s.ActionsHint)) actions.Add(Ui.Text(s.ActionsHint, "muted center tv-putt-hint", Ui.Medium));

            // A game that carries the Player over a level holds the buttons back behind one Continue, which shows the
            // Level up page first; the buttons are there when it is closed. (Not on a TV: the remote's focus is on them.)
            Xp.Panel(app, xp, s.ReviewId, s.XpUnpaid, level =>
            {
                if (Tv.On) return;
                actions.style.display = DisplayStyle.None;
                levelGate.Clear();
                levelGate.Add(Ui.Button("Continue", () =>
                {
                    levelGate.Clear();
                    actions.style.display = DisplayStyle.Flex;
                    app.Push(new LevelUpScreen(app, level));
                }, "btn go-btn"));
            });

            VisualElement rate = string.IsNullOrEmpty(s.RateAs) ? null : Feedback.GameCard(app, s.RateAs);

            // ── The putts (or the game's own second column) ──
            VisualElement detail = null;
            string detailTitle = null;
            if (s.Detail != null) { detail = s.Detail; detailTitle = s.DetailTitle; }
            else if (s.Putts != null && s.Putts.Count > 0) { detail = PuttList(s.Putts); detailTitle = s.PuttsTitle; }

            VisualElement header = Ui.Header("", exit);
            bool? wide = null;
            void Arrange()
            {
                float w = root.resolvedStyle.width, h = root.resolvedStyle.height;
                if (float.IsNaN(w) || w <= 0f) return;
                bool two = detail != null && w >= 900f && w > h;
                if (wide == two) return;
                wide = two;
                root.Clear();
                root.Add(header);
                if (two)
                {
                    // Left: PQS and XP, the actions and the rating. Right: the score and the game's numbers, held in
                    // place over the putts, which scroll on their own.
                    ScrollView left = Ui.Scroll();
                    left.AddToClassList("go-left");
                    left.Add(Ui.Box("go-pad", more, actions, rate));
                    ScrollView right = Ui.Scroll();
                    right.AddToClassList("go-right");
                    VisualElement head = Section(detailTitle);
                    VisualElement side = Ui.Box("go-side");
                    side.Add(Ui.Box("go-pad go-top", result));
                    if (head != null) side.Add(head);
                    side.Add(right);
                    right.Add(Ui.Box("go-pad", detail));
                    root.Add(Ui.Box("go-cols", left, side));
                }
                else
                {
                    ScrollView scroll = Ui.Scroll();
                    scroll.AddToClassList("go-one");
                    scroll.Add(Ui.Box("go-pad", result, more, actions, rate));
                    if (detail != null)
                    {
                        VisualElement head = Section(detailTitle);
                        if (head != null) scroll.Add(head);
                        scroll.Add(Ui.Box("go-pad go-pad-last", detail));
                    }
                    root.Add(scroll);
                }
            }
            root.RegisterCallback<GeometryChangedEvent>(_ => Arrange());
            root.RegisterCallback<AttachToPanelEvent>(_ => root.schedule.Execute(Arrange));
            return root;
        }

        static VisualElement Section(string title) => string.IsNullOrEmpty(title) ? null : Ui.Section(title);

        /// <summary>The game's numbers: a strip for up to four, rows in one block for more.</summary>
        static VisualElement Stats(List<(string value, string caption)> stats)
        {
            if (stats.Count <= 4)
            {
                VisualElement strip = Ui.StatStrip(stats.ToArray());
                strip.AddToClassList("go-gap");
                return strip;
            }
            VisualElement block = Ui.Box("go-block go-gap");
            for (int i = 0; i < stats.Count; i++)
                block.Add(Ui.Box(i == 0 ? "go-row go-row-first" : "go-row", Ui.Caps(stats[i].caption, "caps go-grow"), Ui.Num(stats[i].value, "num-20")));
            return block;
        }

        static void DrawBest(VisualElement into, Dictionary<string, double> bests, GameOverSpec s)
        {
            into.Clear();
            double score = s.ScoreValue ?? 0;
            bool had = bests.TryGetValue(s.GameId, out double was);
            // The list may already hold this game: a score level with the best is the best.
            bool isBest = score > 0 && (!had || (s.LowerIsBetter ? score <= was : score >= was));
            if (!had && !isBest) return;
            string words = isBest ? "New personal best" : "Your best " + was.ToString("0.##", CultureInfo.InvariantCulture);
            Label line = Ui.Caps(words, "caps go-best");
            line.style.color = isBest ? Ui.Gold : Ui.Faint;
            VisualElement row = Ui.Box("go-bestrow", Ui.Icon("Trophy", 16, isBest ? Ui.Gold : Ui.Faint), line);
            into.Add(row);
        }

        /// <summary>What the game did to PQS: the last 30 days with and without it.</summary>
        static void Pqs(VisualElement into, List<AnalysisMath.Putt> played)
        {
            Frame.LoadPqs((before, putts) =>
            {
                var after = new List<AnalysisMath.Putt>(putts);
                after.AddRange(played);
                // The API may already hold this game's putts: if so, "before" includes them and the change reads 0, which is honest.
                int now = after.Count >= 5 ? AnalysisMath.Pqs(after, out _, out _) : -1;
                if (now < 0) return;
                int change = before >= 0 ? now - before : 0;
                Label delta = Ui.Text(before < 0 ? "Your first PQS" : (change > 0 ? "▲ " : change < 0 ? "▼ " : "") + (change == 0 ? "No change" : Math.Abs(change) + " from this game"), "body-2", Ui.Medium);
                delta.style.color = change > 0 ? Ui.Bright : change < 0 ? Ui.Red : Ui.Faint;
                into.Clear();
                into.Add(Ui.Box("go-block go-gap", Ui.Box("go-row go-row-first", Ui.Box("go-grow", Ui.Caps("PQS · last 30 days"), delta), Ui.Num(now.ToString(), "num"))));
            });
        }

        static VisualElement PuttList(List<GameOverPutt> putts)
        {
            VisualElement list = Ui.Box("go-block");
            for (int i = 0; i < putts.Count; i++)
            {
                GameOverPutt p = putts[i];
                Label call = Ui.Text(p.Call ?? "", "body go-call", Ui.Bold);
                call.style.color = p.Good ? Ui.Bright : Ui.Faint;
                VisualElement right = call;
                if (!string.IsNullOrEmpty(p.Points))
                {
                    Label earned = Ui.Text(p.Points, "body-2 go-call", Ui.Medium);
                    right = Ui.Box("go-right-words", call, earned);
                }
                list.Add(Ui.Box(i == 0 ? "go-row go-row-first" : "go-row", Ui.Num(p.Number ?? (i + 1).ToString(), "go-n"), Ui.Text(p.From ?? "", "body go-grow", Ui.Medium), right));
            }
            return list;
        }

        /// <summary>"6 ft · Center tee".</summary>
        public static string From(int feet, int tee) => feet + " ft   ·   " + PracticeSession.TeeName(tee) + " tee";

        /// <summary>A putt's words when the game has no call of its own: where it went in, or that it missed.</summary>
        public static string CallOf(AppPutt.Verdict v)
        {
            if (v == null) return "";
            if (!v.Made) return "Missed";
            return string.IsNullOrEmpty(v.Description) ? "Made" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(v.Description.ToLowerInvariant());
        }

        /// <summary>A game's putts as PQS reads them.</summary>
        public static AnalysisMath.Putt PqsPutt(int feet, int tee, AppPutt.Verdict v) =>
            new AnalysisMath.Putt { Feet = feet, Tee = tee, Made = v.Made, HasEntry = v.Made && v.HasEntry, OffLine = (float)v.OffLine, At = DateTime.UtcNow };
    }
}
