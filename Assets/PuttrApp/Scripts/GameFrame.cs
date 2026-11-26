// The shared game frame from the Puttr 2 sports UI (puttr-app/docs/puttr2-sports-ui, phone 3 to 5):
// the same few pieces around every game, so each game only brings its own rules and scene.
//
//   Matchup: a stage before the game: the game, its length, the Player's card with their PQS.
//   Score bug: one compact bar: who is playing, the score, how far through.
//   Tee sign: where to putt from, over the scene: the distance large, and the three tees with the ball on one (TeeMarker).
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
            tone == Tone.Best ? Ui.Bright : tone == Tone.Good ? (Color)Ui.Bright : tone == Tone.Fair ? Ui.Amber : tone == Tone.Poor ? Ui.Red : Ui.Faint;
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

        /// <summary>True where the tee is the band under the game's picture (a phone), not the large sign over the scene.</summary>
        public static bool TeeInBand => Sized(TeeSize.Auto) == TeeSize.Phone;

        /// <summary>
        /// Where to putt from on a phone (Matt, 2026-11-18): a band across the screen just under the game's picture,
        /// centered: the three tees as circles, left to right as the Player sees the mat, the one to putt from solid,
        /// and the distance large. Nothing sits over the scene or the ball's replay.
        /// </summary>
        public static VisualElement TeeBand(int feet, int tee)
        {
            int on = Mathf.Clamp(tee, 1, 3);
            VisualElement band = Ui.Box("tee-band");
            band.pickingMode = PickingMode.Ignore;
            for (int t = 1; t <= 3; t++) band.Add(Ui.Box(t == on ? "tee-dot tee-dot-on" : "tee-dot"));
            if (feet > 0) band.Add(Ui.Box("tee-band-ft", Ui.Text(feet.ToString(), "tee-band-num", Ui.Bold), Ui.Text("FT", "tee-band-unit", Ui.Bold)));
            return band;
        }

        /// <summary>How large the tee marker is drawn. Auto picks by device: a phone, a tablet, or a TV read from across the room.</summary>
        public enum TeeSize { Auto, Phone, Tablet, Tv }

        static TeeSize Sized(TeeSize size) => size != TeeSize.Auto ? size : Tv.On ? TeeSize.Tv : App.Tablet ? TeeSize.Tablet : TeeSize.Phone;

        // A game panel sits on the stage, outside the app's own root, so the .tablet and .wide classes never reach
        // it: the size is a class on the piece itself.
        static string SizeClass(string name, TeeSize size)
        {
            TeeSize s = Sized(size);
            return s == TeeSize.Tv ? name + " " + name + "-tv" : s == TeeSize.Tablet ? name + " " + name + "-tablet" : name;
        }

        /// <summary>
        /// The one tee marker, wherever a game says where to putt from: the distance as a large number with FEET, and
        /// the three tees in a row as the Player sees the mat (left, center, right), a golf ball on the one to putt
        /// from and the other two empty rings. The picture is the instruction; no "Left tee" in words.
        /// On a solid dark plate, so it reads over a 3D scene. Number / circle sizes in panel units: phone 46 / 30,
        /// tablet 72 / 46, TV 100 / 62 (of a 720 unit high screen).
        /// </summary>
        /// <param name="feet">The distance; 0 or less draws the three tees alone.</param>
        /// <param name="tee">1 left, 2 center, 3 right.</param>
        /// <param name="caption">Adds the tee's name small under the circles, for a screen that wants the word too.</param>
        public static VisualElement TeeMarker(int feet, int tee, TeeSize size = TeeSize.Auto, bool caption = false)
        {
            VisualElement marker = Ui.Box(SizeClass("tee-marker", size));
            marker.pickingMode = PickingMode.Ignore;
            if (feet > 0) marker.Add(Ui.Box("tee-marker-top", Ui.Text(feet.ToString(), "tee-num", Ui.Bold), Ui.Text("FEET", "tee-unit", Ui.Bold)));
            marker.Add(TeeSpots(tee, null));
            if (caption) marker.Add(Ui.Text(PracticeSession.TeeName(tee).ToUpperInvariant() + " TEE", "tee-caption", Ui.Bold));
            return marker;
        }

        // The three circles. With pick, each is a button (the tee picker).
        static VisualElement TeeSpots(int tee, Action<int> pick)
        {
            int on = Mathf.Clamp(tee, 1, 3);
            VisualElement spots = Ui.Box("tee-spots");
            for (int t = 1; t <= 3; t++)
            {
                int which = t;
                VisualElement spot = t == on ? Ui.Box("tee-spot tee-spot-on", Ui.Box("tee-ball")) : Ui.Box("tee-spot");
                if (pick != null) spot.OnTap(() => pick(which));
                spots.Add(spot);
            }
            return spots;
        }

        /// <summary>Choosing a tee by the same picture: the three circles as buttons, the ball on the chosen one.</summary>
        public static VisualElement TeePicker(int tee, Action<int> pick, TeeSize size = TeeSize.Auto)
        {
            VisualElement picker = Ui.Box(SizeClass("tee-marker", size) + " tee-picker");
            // The ball moves to the tapped circle here, so a screen that does not redraw itself still shows the choice.
            void Draw(int on)
            {
                picker.Clear();
                picker.Add(TeeSpots(on, t => { Draw(t); pick?.Invoke(t); }));
            }
            Draw(tee);
            return picker;
        }

        /// <summary>
        /// Where to putt from, on a setup screen (puttr-app Open.js): one row with the distance and the tee's way, and
        /// tapping it opens the mat's 27 spots (TeeSheet). In place of every distance as a pill and the tees under them.
        /// </summary>
        public static VisualElement SpotRow(int feet, int tee, Action<int, int> pick)
        {
            VisualElement row = Ui.Box("glass row spot-row");
            Label distance = Ui.Text("", "spot-row-ft", Ui.Bold), way = Ui.Text("", "spot-row-way", Ui.Regular);
            void Show(int f, int t) { distance.text = f + " ft"; way.text = TeeWay(t); }
            Show(feet, tee);
            row.Add(Ui.Box("grow", distance, way));
            row.Add(Ui.Box("spot-row-icon", Ui.Icon("Target", 24, Ui.Faint)));
            // The row shows the new spot itself, so a screen that does not redraw still shows the choice.
            row.OnTap(() => TeeSheet(feet, tee, (f, t) => { feet = f; tee = t; Show(f, t); pick?.Invoke(f, t); }, App.Layer(row)));
            return row;
        }

        /// <summary>The way a tee breaks, as the app words it: left to right, straight, right to left.</summary>
        public static string TeeWay(int tee) => tee == 1 ? "Left to right" : tee == 3 ? "Right to left" : "Straight";

        /// <summary>
        /// "Select tee position" (puttr-app common/Green.js): the mat's 27 spots, 3 feet nearest the hole at the top to
        /// 11 at the bottom, three tees across, the distance under each row, the Puttr mark printed on 11 feet's center
        /// spot as on the mat. Tapping a spot picks it and closes the sheet.
        /// </summary>
        /// <param name="layer">Where the sheet goes: App.Layer on a screen, the game's overlay in a game.</param>
        public static void TeeSheet(int feet, int tee, Action<int, int> pick, VisualElement layer)
        {
            VisualElement dim = null;
            void Close() => dim?.RemoveFromHierarchy();
            VisualElement grid = Ui.Box("spot-grid");
            for (int f = PracticeSession.MinFeet; f <= PracticeSession.MaxFeet; f++)
            {
                VisualElement spots = Ui.Box("spot-line");
                for (int t = 1; t <= 3; t++)
                {
                    int pf = f, pt = t;
                    bool on = f == feet && t == tee, mark = f == PracticeSession.MaxFeet && t == 2;
                    VisualElement spot = Ui.Box(on ? "spot spot-on" : mark ? "spot spot-mark" : "spot");
                    if (on) spot.Add(Ui.Box("spot-ball"));
                    else if (mark)
                    {
                        var logo = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite"), scaleMode = ScaleMode.ScaleToFit };
                        logo.AddToClassList("spot-logo");
                        logo.pickingMode = PickingMode.Ignore;
                        spot.Add(logo);
                    }
                    spots.Add(spot.OnTap(() => { Close(); pick?.Invoke(pf, pt); }));
                }
                grid.Add(spots);
                grid.Add(Ui.Text(f.ToString(), "spot-ft", Ui.Regular));
            }
            VisualElement head = Ui.Box("row spot-head", Ui.Title("Select tee position", "title-20 grow"),
                Ui.IconSquare("X", Close));
            VisualElement card = Ui.Box("modal spot-sheet", head, grid);
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(Close);
            dim.Face(Ui.Medium);
            layer.Add(dim);
            // On a TV the remote starts on the spot that is picked, not the close button.
            if (Tv.On) card.schedule.Execute(() => Tv.FocusOn(card.Q(className: "spot-on") ?? card.Q(className: "spot"))).StartingIn(50);
        }

        /// <summary>The tee sign: the tee marker in its place over the scene, top left under the score bug.</summary>
        public static VisualElement TeeSign(int feet, int tee)
        {
            VisualElement sign = Ui.Box("tee-sign", TeeMarker(feet, tee));
            sign.pickingMode = PickingMode.Ignore;
            return sign;
        }

        /// <summary>
        /// How far through a list of putts: one segment a putt, green for a putt that scored, red for one that did
        /// not, white for the one being played. A list too long for segments to read is one bar.
        /// </summary>
        public static VisualElement PuttProgress(IList<double> scores, int count)
        {
            int played = scores != null ? scores.Count : 0;
            if (count > 40 || count <= 0) return Ui.Progress(count > 0 ? played / (float)count : 0f);
            VisualElement row = Ui.Box("putt-segs");
            for (int i = 0; i < count; i++)
                row.Add(Ui.Box(i < played ? (scores[i] > 0 ? "putt-seg putt-seg-made" : "putt-seg putt-seg-missed") : i == played ? "putt-seg putt-seg-now" : "putt-seg"));
            return row;
        }

        /// <summary>The putt read: the result and its points, then Line, Pace and Entry, each with its grade.
        /// backDoor: whether the make came in the back of the cup, where the caller knows (a rule game: Scoring.BackDoor).
        /// Without it a make that scored nothing reads as one, which is wrong for a game where most makes score 0 (Dead
        /// Center: a made 27° push is a make, not a back door).</summary>
        public static VisualElement PuttRead(AppPutt.Verdict v, double points, bool scored, string pointsWord = "POINTS", bool? backDoor = null)
        {
            bool back = v.Made && (backDoor ?? scored && points <= 0);
            Label head = Ui.Text(!v.Made ? "MISSED" : back ? "BACK DOOR" : "MADE", "read-head", Ui.Bold);
            head.style.color = v.Made && !back ? Ui.Bright : Ui.Red;
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
            VisualElement words = Ui.Box("grow next-words", Ui.Title(name, "title-20"), Ui.Text(line, "muted", Ui.Regular));
            VisualElement rating = Ui.Box("card-rating", Ui.Num(pqs >= 0 ? pqs.ToString() : "–", "num-44 center"), Ui.Text("PQS", "kick center", Ui.Bold));
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
                Ui.Text(Rules(session), "muted", Ui.Regular).Margin(8));
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.Pop));
            scroll.Add(Ui.Box("pad",
                Ui.Text((string.Equals(kicker, title, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(kicker) ? "" : kicker.ToUpperInvariant() + "   ·   ") + "ABOUT " + Mathf.Max(2, Mathf.CeilToInt(session.Putts.Count / 3f)) + " MIN", "kick center", Ui.Bold),
                Ui.Title(title, "title center wrap"),
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

    /// <summary>After a round: the result, stat lines, the change to PQS, and what next (the shared game over page).</summary>
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
            var stats = new List<(string, string)> { (made + " of " + s.Seen.Count, "Putts made") };
            if (lines > 0) stats.Add(((lineSum / lines).ToString("0.0", CultureInfo.InvariantCulture) + "°", "Average line miss"));
            if (pace != null) stats.Add((AppPutt.FmtRollout(pace), "Typical pace"));
            stats.Add((best.ToString(), "Best run"));
            stats.Add((centre.ToString(), "Center cuts"));

            // A round played on test putts is saved nowhere: no game to review, and it never counts for PQS.
            bool saved = round.How != CompetitionRound.Mode.Local;
            var spec = new GameOverSpec
            {
                Eyebrow = "Final   ·   " + title,
                Score = RoundSession.Points(s.Total),
                ScoreCaption = "of " + s.Possible + " possible",
                Outcome = round.Outcome(),
                Stats = stats,
                ReviewId = saved ? round.UserGameId : null,
                RateAs = title,
                Again = again,
                Putts = new List<GameOverPutt>(),
                PqsPutts = saved ? new List<AnalysisMath.Putt>() : null,
            };
            // A catalogue game played as itself has a best of its own; a tournament or coach round does not.
            if (round.What == CompetitionRound.Kind.Game && round.Round != null) { spec.GameId = J.Str(round.Round, "id"); spec.ScoreValue = s.Total; }
            for (int i = 0; i < s.Seen.Count && i < s.Putts.Count; i++)
            {
                AppPutt.Verdict v = s.Seen[i];
                double points = i < s.Scores.Count ? s.Scores[i] : 0;
                spec.Putts.Add(new GameOverPutt { Number = (i + 1).ToString(), From = GameOver.From(s.Putts[i].Feet, s.Putts[i].Tee), Call = GameOver.CallOf(v), Points = points > 0 ? "+" + RoundSession.Points(points) : null, Good = points > 0 });
                if (saved) spec.PqsPutts.Add(GameOver.PqsPutt(s.Putts[i].Feet, s.Putts[i].Tee, v));
            }
            Root = GameOver.Build(app, spec);
        }
    }
}
