// The shared game frame from the Puttr 2 sports UI (puttr-app/docs/puttr2-sports-ui, phone 3 to 5):
// the same few pieces around every game, so each game only brings its own rules and scene.
//
//   Matchup: a stage before the game: the game, its length, the Player's card with their PQS.
//   Score bug: one compact bar: who is playing, the score, how far through.
//   Tee sign: where to putt from, large, over the scene.
//   Putt read: after each putt, what happened and three graded reads: Line, Pace, Entry.
//   Final: the result, stat lines, what it did to PQS, and the next choice.
//
// The grades are a first proposal (PuttGrades) built on the app's own measures: the start line
// (getStartLine), the rollout (projectRollout) and the entry band (getEntryDescription).
// XP and levels are in the design but the API does not award them yet, so nothing shows them.
using System;
using System.Collections.Generic;
using System.Globalization;
using PuttrArcade;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>The words and colours of a putt read. A proposal: the bands are not from the app.</summary>
    public static class PuttGrades
    {
        public enum Tone { Best, Good, Fair, Poor, None }

        /// <summary>Start line in degrees, + right: Perfect within half a degree, Great within one, Good within two.</summary>
        public static (string text, string grade, Tone tone) Line(double? degrees)
        {
            if (degrees == null) return ("Not read", "", Tone.None);
            double d = degrees.Value, a = Math.Abs(d);
            string text = a < 0.05 ? "On line" : a.ToString("0.0", CultureInfo.InvariantCulture) + "° " + (d > 0 ? "right" : "left");
            if (a < 0.5) return (text, "Perfect", Tone.Best);
            if (a < 1.0) return (text, "Great", Tone.Best);
            if (a < 2.0) return (text, "Good", Tone.Good);
            return (text, "Off line", Tone.Poor);
        }

        /// <summary>
        /// Rollout in inches past (+) or short (-) of the hole's centre, as the app measures it.
        /// Ideal is 0 to 12 in past, the analysis' own band; a make short of centre is the app's measuring noise and reads ideal.
        /// </summary>
        public static (string text, string grade, Tone tone) Pace(double? rollout, bool made)
        {
            if (rollout == null) return ("Not read", "", Tone.None);
            double r = rollout.Value;
            string text = AppPutt.FmtRollout(r);
            if (r < 0 && !made) return (text, "Short", Tone.Poor);
            if (r <= 12 || made && r < 0) return (text, "Ideal", Tone.Best);
            if (r <= 24) return (text, "Firm", Tone.Fair);
            return (text, "Too firm", Tone.Poor);
        }

        /// <summary>Where it went in (or by): center cut is perfect, the center bands good, the sides lucky.</summary>
        public static (string text, string grade, Tone tone) Entry(AppPutt.Verdict v, double score)
        {
            if (!v.Made) return (v.MissSide == "LEFT" ? "Missed left" : v.MissSide == "RIGHT" ? "Missed right" : "Missed", "", Tone.None);
            switch (v.Description)
            {
                case "CENTER CUT": return ("Center cut", "Perfect", Tone.Best);
                case "LEFT CENTER": return ("Left center", "Good", Tone.Good);
                case "RIGHT CENTER": return ("Right center", "Good", Tone.Good);
                case "LEFT SIDE": return ("Left side", "Lucky", Tone.Fair);
                case "RIGHT SIDE": return ("Right side", "Lucky", Tone.Fair);
                default: return ("Back door", score > 0 ? "Lucky" : "Does not count", Tone.Poor);
            }
        }

        public static Color Colour(Tone tone) =>
            tone == Tone.Best ? Ui.Bright : tone == Tone.Good ? (Color)new Color32(0x9E, 0xD8, 0x7A, 0xFF) : tone == Tone.Fair ? Ui.Amber : tone == Tone.Poor ? Ui.Red : Ui.Faint;
    }

    public static class Frame
    {
        /// <summary>The score bug: a Player chip with their score, and how far through the game is.</summary>
        public static VisualElement ScoreBug(string name, string photo, string score, string progress)
        {
            VisualElement avatar = Ui.Avatar(name, photo);
            avatar.AddToClassList("bug-avatar");
            return Ui.Box("bug",
                Ui.Box("row bug-player", avatar, Ui.Text(name, "bug-name", Ui.Bold), Ui.Text(score, "bug-score", Ui.Bold)),
                Ui.Box("bug-progress", Ui.Text(progress, "bug-round", Ui.Bold)));
        }

        /// <summary>The tee sign: "PUTT FROM", the distance large, the tee under it.</summary>
        public static VisualElement TeeSign(int feet, int tee) =>
            Ui.Box("tee-sign", Ui.Text("PUTT FROM", "kick center", Ui.Bold), Ui.Text(feet + " FEET", "tee-feet", Ui.Bold), Ui.Text(PracticeSession.TeeName(tee) + " tee", "sub center", Ui.Medium));

        /// <summary>The putt read: the result and its points, then Line, Pace and Entry, each with its grade.</summary>
        public static VisualElement PuttRead(AppPutt.Verdict v, double points, bool scored, string pointsWord = "POINTS")
        {
            Label head = Ui.Text(v.Made && points > 0 || v.Made && !scored ? "MADE" : v.Made ? "BACK DOOR" : "MISSED", "read-head", Ui.Bold);
            head.style.color = v.Made && (points > 0 || !scored) ? Ui.Bright : Ui.Red;
            VisualElement top = Ui.Box("row", Ui.Box("grow", Ui.Text("PUTT READ", "kick", Ui.Bold), head));
            if (scored) top.Add(Ui.Box("read-points", Ui.Text((points > 0 ? "+" : "") + RoundSession.Points(points), "read-number", Ui.Bold), Ui.Text(pointsWord, "kick center", Ui.Bold)));
            VisualElement card = Ui.Box("sheet-card read", top);
            void Line((string text, string grade, PuttGrades.Tone tone) read, string label)
            {
                Label grade = Ui.Text(read.grade, "read-grade", Ui.Bold);
                grade.style.color = PuttGrades.Colour(read.tone);
                card.Add(Ui.Box("row read-line", Ui.Text(label, "read-label", Ui.Bold), Ui.Text(read.text, "read-text grow", Ui.Medium), grade));
            }
            Line(PuttGrades.Line(v.StartLine), "LINE");
            Line(PuttGrades.Pace(v.Rollout, v.Made), "PACE");
            Line(PuttGrades.Entry(v, points), v.Made ? "ENTRY" : "RESULT");
            return card;
        }

        /// <summary>A Player as a card: photo, name, a line under it, and their PQS large.</summary>
        public static VisualElement PlayerCard(object profile, int pqs, string line)
        {
            string name = J.Str(profile, "displayName", "You");
            VisualElement avatar = Ui.Avatar(name, J.Str(profile, "photoURL"), true);
            if (Club.Member(profile)) avatar.AddToClassList("avatar-member");
            VisualElement words = Ui.Box("grow next-words", Ui.Text(name.ToUpperInvariant(), "tile-name", Ui.Bold), Ui.Text(line, "muted", Ui.Regular));
            VisualElement rating = Ui.Box("card-rating", Ui.Text(pqs >= 0 ? pqs.ToString() : "–", "score-mid", Ui.Bold), Ui.Text("PQS", "kick center", Ui.Bold));
            return Ui.Box("glass row player-card", avatar, words, rating);
        }

        /// <summary>The Player's PQS over the last 30 days, and the putts it came from (to work out the change a game makes).</summary>
        public static void LoadPqs(Action<int, List<AnalysisMath.Putt>> done)
        {
            App.Run(Api.Get("/profile/analysis/putts/30", data =>
            {
                List<AnalysisMath.Putt> putts = AnalysisMath.ReadAll(J.List(data));
                done(putts.Count >= 5 ? AnalysisMath.Pqs(putts, out _, out _) : -1, putts);
            }, _ => done(-1, new List<AnalysisMath.Putt>())));
        }
    }

    /// <summary>The matchup stage: what is about to be played, and by whom. Start begins the game.</summary>
    public sealed class MatchupScreen : AppScreen
    {
        static string Rules(RoundSession s)
        {
            string streak = s.StopOnMiss ? " One miss ends the level." : "";
            switch (s.Rule)
            {
                case RoundRule.CenterCut: return "A holed putt scores its distance, less the share it comes in off its line. The back door scores nothing.";
                case RoundRule.Make: return "Every holed putt counts." + streak;
                case RoundRule.Precision: return "A putt counts only if it drops inside its entry window." + streak;
                default: return "A holed putt scores its distance in feet. The back door scores nothing.";
            }
        }

        public MatchupScreen(App app, RoundSession session, string kicker, string title, Action start)
        {
            int feetFrom = 11, feetTo = 3;
            foreach (TournamentMath.Dealt p in session.Putts) { feetFrom = Math.Min(feetFrom, p.Feet); feetTo = Math.Max(feetTo, p.Feet); }
            var card = new VisualElement();
            VisualElement rules = Ui.Box("glass",
                Ui.Text(session.Putts.Count + " putts   ·   " + feetFrom + " to " + feetTo + " feet   ·   a new tee each putt", "sub", Ui.Medium),
                Ui.Text(Rules(session), "muted", Ui.Regular).Margin(6));
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad",
                Ui.Text((string.Equals(kicker, title, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(kicker) ? "" : kicker.ToUpperInvariant() + "   ·   ") + "ABOUT " + Mathf.Max(2, Mathf.CeilToInt(session.Putts.Count / 3f)) + " MIN", "kick center", Ui.Bold),
                Ui.Text(title.ToUpperInvariant(), "display center wrap", Ui.Bold),
                card.Margin(16), rules.Margin(12),
                Ui.Button("Start", () => { app.Pop(); start(); })));
            Root = scroll;
            Frame.LoadPqs((pqs, _) =>
            {
                object profile = app.Profile;
                card.Clear();
                card.Add(Frame.PlayerCard(profile, pqs, pqs >= 0 ? "Last 30 days" : "PQS after your first 5 putts"));
            });
            card.Add(Frame.PlayerCard(app.Profile, -1, ""));
        }
    }

    /// <summary>After a round: the result, stat lines, the change to PQS, and what next.</summary>
    public sealed class FinalScreen : AppScreen
    {
        public FinalScreen(App app, CompetitionRound round, string title, Action again)
        {
            RoundSession s = round.Session;
            int made = 0, best = 0, run = 0, centre = 0, lines = 0;
            double lineSum = 0;
            var rollouts = new List<double>();
            foreach (AppPutt.Verdict v in s.Seen)
            {
                if (v.Made) { made++; run++; best = Math.Max(best, run); if (v.Description == "CENTER CUT") centre++; } else run = 0;
                if (v.StartLine != null) { lineSum += Math.Abs(v.StartLine.Value); lines++; }
                if (v.Rollout != null) rollouts.Add(v.Rollout.Value);
            }
            double? pace = AppPutt.Median(rollouts);
            VisualElement stats = Ui.Box("glass");
            void Stat(string label, string value)
            {
                stats.Add(Ui.Box("board-row", Ui.Text(label, "read-label grow", Ui.Bold), Ui.Text(value, "feed-score", Ui.Bold)));
            }
            Stat("PUTTS MADE", made + " of " + s.Seen.Count);
            if (lines > 0) Stat("AVERAGE LINE MISS", (lineSum / lines).ToString("0.0", CultureInfo.InvariantCulture) + "°");
            if (pace != null) Stat("TYPICAL PACE", AppPutt.FmtRollout(pace));
            Stat("BEST RUN", best.ToString());
            Stat("CENTER CUTS", centre.ToString());

            var rating = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            VisualElement buttons = Ui.Box("pad");
            if (again != null) buttons.Add(Ui.Button("Play again", () => { app.Pop(); again(); }));
            buttons.Add(Ui.Button("Exit", app.ExitToTab, again != null ? "btn btn-ghost" : "btn"));
            scroll.Add(Ui.Box("pad",
                Ui.Text("FINAL   ·   " + title.ToUpperInvariant(), "kick center", Ui.Bold),
                Ui.Text(RoundSession.Points(s.Total), "score-big", Ui.Bold),
                Ui.Text("of " + s.Possible + " possible", "sub center", Ui.Regular),
                Ui.Text(round.Outcome(), "sub center", Ui.Regular).Margin(8),
                stats.Margin(16), rating.Margin(12)));
            scroll.Add(buttons);
            Root = scroll;

            // What the round did to PQS: the last 30 days with and without it. A round played on test putts never counts.
            if (round.How == CompetitionRound.Mode.Local) return;
            Frame.LoadPqs((before, putts) =>
            {
                var after = new List<AnalysisMath.Putt>(putts);
                for (int i = 0; i < s.Seen.Count; i++)
                {
                    AppPutt.Verdict v = s.Seen[i];
                    after.Add(new AnalysisMath.Putt { Feet = s.Putts[i].Feet, Tee = s.Putts[i].Tee, Made = v.Made, HasEntry = v.Made && v.HasEntry, OffLine = (float)v.OffLine, At = DateTime.UtcNow });
                }
                // The API may already hold this round's putts: if so, "before" includes them and the change reads 0, which is honest.
                int now = after.Count >= 5 ? AnalysisMath.Pqs(after, out _, out _) : -1;
                if (now < 0) return;
                int change = before >= 0 ? now - before : 0;
                Label delta = Ui.Text(before < 0 ? "Your first PQS" : (change > 0 ? "▲ " : change < 0 ? "▼ " : "") + (change == 0 ? "No change" : Math.Abs(change) + " from this game"), "sub", Ui.Medium);
                delta.style.color = change > 0 ? Ui.Bright : change < 0 ? Ui.Red : Ui.Faint;
                rating.Add(Ui.Box("glass row", Ui.Box("card-rating", Ui.Text(now.ToString(), "score-mid", Ui.Bold), Ui.Text("PQS", "kick center", Ui.Bold)), Ui.Box("grow next-words", Ui.Text("LAST 30 DAYS", "kick", Ui.Bold), delta)));
            });
        }
    }
}
