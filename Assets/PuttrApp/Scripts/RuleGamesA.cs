// Rule games, first batch: the scored putt-list games (puttr-app TourJuice.js, SpeedDemon.js, Metal.js, Lab.js,
// FullWorkout.js + fullworkout/scoring.js, Fitting.js). Constants are the app's.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>Which game plays a catalog row. Null when this build has no rules for it.</summary>
    public static class RuleCatalog
    {
        static readonly Dictionary<string, Func<object, RuleGame>> Games = new Dictionary<string, Func<object, RuleGame>>
        {
            { "TOURJUICE", _ => new TourJuiceGame() },
            { "CENTERCUT", _ => new CenterCutGame() },
            { "SPEEDDEMON", _ => new SpeedDemonGame() },
            { "METAL", _ => new MetalGame() },
            { "LAB", _ => new LabGame() },
            { "FULLWORKOUT", _ => new FullWorkoutGame() },
            { "FITTING", row => new CompareGame(row) },
            // A Greenlands stage by its code (row["stage"], "1-4"); the first stage for a bare row (a script, the card pass).
            { Greenlands.GameType, row => new StageGame(Greenlands.ByCode(J.Str(row, "stage")) ?? Greenlands.World(1).Stages[0]) },
        };

        static RuleCatalog() { RuleCatalogB.Register(); RuleCatalogC.Register(); SensorCatalog.Register(); }
        public static void Add(string type, Func<object, RuleGame> make) => Games[type] = make;
        public static bool Has(object game) => Games.ContainsKey(J.Str(game, "gameType").ToUpperInvariant());
        public static RuleGame Make(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            if (!Games.TryGetValue(type, out var make)) return null;
            RuleGame made = make(game);
            // The row's own title is the name the Player picked the game by (a renamed row, a custom game on this
            // gameType). A row whose title is only its type again (a script's play:TYPE row) has no name of its own.
            string title = J.Str(game, "title").Trim();
            if (made != null && title.Length > 0 && title.ToUpperInvariant() != type) made.RowTitle = title;
            return made;
        }
        public static IEnumerable<string> Types => Games.Keys;
    }

    /// <summary>The app's shared scoring pieces (common/RolloutFunctions.js, GameFunctions.js).</summary>
    public static class Scoring
    {
        /// <summary>speedScoreFromRollout: 0 to 50. Short reaches zero at 12 in, past at 36 in; a make is never marked down for short.</summary>
        public static double Speed(double? rollout, double target, bool made)
        {
            if (rollout == null) return 0;
            double err = rollout.Value - target;
            if (made && err < 0) err = 0;
            double span = err < 0 ? 12 : 36;
            return Math.Max(0, 50 * (1 - Math.Abs(err) / span));
        }

        /// <summary>A make that came in through the back of the cup (outside the tee's line ±90°) does not count.</summary>
        public static bool BackDoor(AppPutt.Verdict v, int feet, int tee) => v.Made && v.HasEntry && Math.Abs(v.Entry - AppPutt.TeeEntry(feet, tee)) >= 90;
        public static bool Counts(AppPutt.Verdict v, int feet, int tee) => v.Made && !BackDoor(v, feet, tee);
        public static double OffLine(AppPutt.Verdict v, int feet, int tee) => v.HasEntry ? Math.Abs(v.Entry - AppPutt.TeeEntry(feet, tee)) : 0;
        public static string N1(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>3 to 11 feet, each once, shuffled; a random tee each (TourJuice.js createRandomPutts).</summary>
        public static List<TournamentMath.Dealt> NineDistances(System.Random dice)
        {
            List<int> feet = Enumerable.Range(3, 9).OrderBy(_ => dice.Next()).ToList();
            return feet.Select(f => new TournamentMath.Dealt { Feet = f, Tee = dice.Next(1, 4) }).ToList();
        }

        /// <summary>The nine putts of a level (GameLevels): Medium is NineDistances, the game as the app deals it. Easy is
        /// nine from 3 to 6 ft and Hard nine from 8 to 11 ft, each distance in the range as often as nine putts allow.</summary>
        public static List<TournamentMath.Dealt> NineFor(int level, System.Random dice)
        {
            if (GameLevels.Clamp(level) == GameLevels.Medium) return NineDistances(dice);
            return NineFeet(level).OrderBy(_ => dice.Next()).Select(f => new TournamentMath.Dealt { Feet = f, Tee = dice.Next(1, 4) }).ToList();
        }

        /// <summary>Nine distances for a level, shortest first: 3 to 11 once each on Medium, else round the level's range.</summary>
        public static List<int> NineFeet(int level)
        {
            (int lo, int hi) = GameLevels.Band(level);
            return Enumerable.Range(0, 9).Select(i => lo + i % (hi - lo + 1)).OrderBy(f => f).ToList();
        }
    }

    /// <summary>A game over a fixed list of putts that every Player plays in turn (putt 1 for each, then putt 2...).</summary>
    public abstract class ListGame : RuleGame
    {
        // Each Player's own list when they play at their own levels (Levels), else one list for all.
        List<TournamentMath.Dealt> _list = new List<TournamentMath.Dealt>();
        List<TournamentMath.Dealt>[] _lists;
        protected List<TournamentMath.Dealt> List
        {
            get => _lists != null ? _lists[Math.Min(Math.Max(P, 0), _lists.Length - 1)] : _list;
            set { _list = value; _lists = null; }   // one list for everyone (a League round's fixed putts)
        }
        protected readonly System.Random Dice = new System.Random();
        protected int Index;          // the putt in the list being played
        protected int[] Played;       // putts played by each Player
        protected abstract List<TournamentMath.Dealt> MakeList();
        protected override void Begin()
        {
            _lists = null;
            if (Levels != null && Levels.Length > 1 && Levels.Distinct().Count() > 1)
            {
                int was = P;
                _lists = new List<TournamentMath.Dealt>[Players];
                for (int p = 0; p < Players; p++) { P = p; _lists[p] = MakeList(); }
                P = was;
                _list = _lists[0];
            }
            else _list = MakeList();
            Index = 0; Played = new int[Players];
        }
        protected override void Deal() { TournamentMath.Dealt d = List[Math.Min(Index, List.Count - 1)]; Feet = d.Feet; Tee = d.Tee; }
        protected override void Next()
        {
            Played[P]++;
            P = (P + 1) % Players;
            if (P == 0) Index++;
        }
        public override bool Over => Played != null && Played.All(n => n >= List.Count) || Shots.Count >= List.Count * Players;
        public override string Progress => "PUTT " + Math.Min(Index + 1, List.Count) + " OF " + List.Count;
        public override VisualElement Board()
        {
            // The list, with this Player's results on it.
            VisualElement row = Ui.Box("rule-pips");
            for (int i = 0; i < List.Count; i++)
            {
                Shot s = Shots.Where(x => x.Player == P).Skip(i).FirstOrDefault();
                string cls = s == null ? (i == Index ? "rule-pip rule-pip-now" : "rule-pip") : s.Good ? "rule-pip rule-pip-on" : "rule-pip rule-pip-miss";
                row.Add(Ui.Text(List[i].Feet.ToString(), cls, Ui.Bold));
            }
            return Card("THE PUTTS (FEET)", row);
        }
    }

    // ── Tour Juice: line and pace, a make worth up to its distance ──

    public sealed partial class TourJuiceGame : ListGame
    {
        protected override string OwnTitle => "Tour Juice";
        public override string GameType => "TOURJUICE";
        public override int MaxPlayers => 4;
        public override string Icon => "Lightning";
        public override string[] HowTo => new[]
        {
            "Nine putts, one from every distance from 3 to 11 feet.",
            "A make scores up to its distance: half for a centered entry, half for pace that finishes at the hole.",
            "Misses and back-door makes score nothing. 63 is perfect.",
        };
        // No Easy / Medium / Hard (Matt, 2026-11-21): always the nine from 3 to 11 ft.
        protected override List<TournamentMath.Dealt> MakeList() => Scoring.NineFor(GameLevels.Medium, Dice);
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (!v.Made) return Call(0, false, "MISS");
            if (Scoring.BackDoor(v, Feet, Tee)) return Call(0, false, "BACK DOOR", "Putts in the back of the cup don't count.");
            double entry = Feet * (1 - Scoring.OffLine(v, Feet, Tee) / 90);
            double speed = v.Rollout == null ? Feet * 0.5 : Feet * Scoring.Speed(v.Rollout, 0, true) / 50;
            double score = (entry + speed) / 2;
            Score[P] += score;
            double ratio = score / Feet;
            string word = ratio > 0.9 ? "TOUR QUALITY" : ratio > 0.7 ? "SOLID PUTT" : ratio > 0.4 ? "ON TARGET" : "ROOM TO IMPROVE";
            return Call(score, true, word, Scoring.N1(score) + " of " + Feet + "   ·   accuracy " + Scoring.N1(entry / 2) + ", pace " + Scoring.N1(speed / 2) + (v.Rollout != null ? " (" + AppPutt.FmtRollout(v.Rollout) + ")" : ""));
        }
        public override string ScoreText(int p) => Scoring.N1(Score[p]);
    }

    // ── Speed Demon: pace only ──

    public sealed partial class SpeedDemonGame : ListGame
    {
        protected override string OwnTitle => "Speed Demon";
        public override string GameType => "SPEEDDEMON";
        public override int MaxPlayers => 4;
        public override string Icon => "Gauge";
        public override string[] HowTo => new[]
        {
            "Nine putts, 3 to 11 feet, each once.",
            "A make scores up to its distance for pace: perfect when it dies at the hole, nothing at 3 feet past.",
            "Misses and back-door makes score nothing.",
        };
        // No Easy / Medium / Hard (Matt, 2026-11-21): always the nine from 3 to 11 ft.
        protected override List<TournamentMath.Dealt> MakeList() => Scoring.NineFor(GameLevels.Medium, Dice);
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (!v.Made) return Call(0, false, "MISS");
            if (Scoring.BackDoor(v, Feet, Tee)) return Call(0, false, "BACK DOOR", "Putts in the back of the cup don't count.");
            double score = v.Rollout == null ? Feet * 0.5 : Feet * Scoring.Speed(v.Rollout, 0, true) / 50;
            Score[P] += score;
            string word = PaceWord(v.Rollout, true);
            return Call(score, true, word, Scoring.N1(score) + " of " + Feet + (v.Rollout != null ? "   ·   " + AppPutt.FmtRollout(v.Rollout) : ""));
        }
        public override string ScoreText(int p) => Scoring.N1(Score[p]);

        /// <summary>The call for a make's pace, in the putt read's own bands (PuttGrades.Pace: Ideal to 12 in past, Firm to 24, then
        /// too firm), so the headline never says too fast over a read that says Ideal (Matt, 2026-11-21).</summary>
        public static string PaceWord(double? rollout, bool made)
        {
            if (rollout == null) return "MADE IT";
            switch (PuttGrades.Pace(rollout, made).grade)
            {
                case "Ideal": return rollout.Value <= 6 ? "PERFECT SPEED" : "GREAT SPEED";
                case "Firm": return "A LITTLE FIRM";
                case "Short": return "TOO SLOW";
                default: return "TOO FAST";
            }
        }
    }

    // ── Metal: a make scores its distance ──

    public sealed partial class MetalGame : ListGame
    {
        static readonly int[] Counts = { 3, 9, 18, 27, 36, 45, 54, 63, 72 };
        int _count = 9;
        protected override string OwnTitle => "Metal Play";
        public override string GameType => "METAL";
        public override int MaxPlayers => 4;
        public override string Words => "FEET";
        public override string Icon => "Lightning";
        public override string[] HowTo => new[]
        {
            "Random distances in your range, a random tee each.",
            "Every make scores its distance in feet.",
            "Misses and back-door makes score nothing.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Putts"));
            box.Add(Form.Pills(Counts.Select(c => c.ToString()).ToArray(), Array.IndexOf(Counts, _count), i => _count = Counts[i]));
            return box;
        }
        // No Easy / Medium / Hard (Matt, 2026-11-21): every putt from 3 to 11 ft.
        protected override List<TournamentMath.Dealt> MakeList() => Enumerable.Range(0, _count).Select(_ => new TournamentMath.Dealt { Feet = Dice.Next(FeetFrom(), FeetTo() + 1), Tee = Dice.Next(1, 4) }).ToList();
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (!v.Made) return Call(0, false, "MISS");
            if (Scoring.BackDoor(v, Feet, Tee)) return Call(0, false, "BACK DOOR", "Putts in the back of the cup don't count.");
            Score[P] += Feet;
            return Call(Feet, true, "+" + Feet + " FT");
        }
        public override VisualElement Board() => _count <= 18 ? base.Board() : null;
    }

    // ── The Lab: seven putts from one spot, pace and entry out of ten each ──

    public sealed partial class LabGame : RuleGame
    {
        const int Putts = 7;
        int _feet = 5, _tee = 2;
        double _target;
        readonly List<double> _pace = new List<double>(), _entry = new List<double>();
        protected override string OwnTitle => "The Lab";
        public override string GameType => "LAB";
        public override string Icon => "Flask";
        public override string Words => "OUT OF 20";
        public override string[] HowTo => new[]
        {
            "Seven putts from one spot you choose.",
            "Each putt scores up to 10 for pace and up to 10 for a centered entry.",
            "Your score is your average out of 20.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Distance and tee"));
            box.Add(Frame.SpotRow(_feet, _tee, (f, t) => { _feet = f; _tee = t; }));
            box.Add(Form.Label("Your speed"));
            double[] targets = { 0, 3, 6 };
            box.Add(Form.Pills(new[] { "Die it", "Firm", "Aggressive" }, 0, i => _target = targets[i]));
            return box;
        }
        protected override void Begin() { _pace.Clear(); _entry.Clear(); }
        protected override void Deal() { Feet = _feet; Tee = _tee; }
        public override bool Over => Shots.Count >= Putts;
        public override string Progress => "PUTT " + Math.Min(Shots.Count + 1, Putts) + " OF " + Putts;
        protected override Shot Take(AppPutt.Verdict v)
        {
            double pace = Scoring.Speed(v.Rollout, _target, v.Made) / 5;
            double entry = v.Made ? Math.Max(0, 10 - Math.Round(Scoring.OffLine(v, Feet, Tee) / 10)) : 0;
            _pace.Add(pace); _entry.Add(entry);
            Score[0] = (_pace.Sum() + _entry.Sum()) / _pace.Count;
            string word = !v.Made ? "MISS" : entry >= 10 ? "PERFECT" : v.Entry - AppPutt.TeeEntry(Feet, Tee) < 0 ? "TOO FAR LEFT" : "TOO FAR RIGHT";
            return Call(pace + entry, v.Made, word, "Pace " + Scoring.N1(pace) + (v.Rollout != null ? " (" + AppPutt.FmtRollout(v.Rollout) + ")" : " (no read)") + "   ·   Entry " + Scoring.N1(entry));
        }
        public override string ScoreText(int p) => Score[p].ToString("0.00", CultureInfo.InvariantCulture);
        public override int Saved => (int)Math.Round(Score[0]); // the app sends the average; the API column is an int
        public override VisualElement Board()
        {
            if (_pace.Count == 0) return null;
            return Card("AVERAGES", Ui.Box("stat-tiles",
                Ui.Box("glass stat-tile", Ui.Text(Scoring.N1(_pace.Average()), "stat-value", Ui.Bold), Ui.Text("PACE", "caps", Ui.Bold)),
                Ui.Box("glass stat-tile", Ui.Text(Scoring.N1(_entry.Average()), "stat-value", Ui.Bold), Ui.Text("ENTRY", "caps", Ui.Bold))));
        }
    }

    // ── Full Workout: 27 putts, every distance from every tee ──

    public sealed partial class FullWorkoutGame : ListGame
    {
        const double CenterCutDeg = 18, PaceMaxPast = 3;
        protected override string OwnTitle => "Full Workout";
        public override string GameType => "FULLWORKOUT";
        public override string Icon => "Barbell";
        public override string[] HowTo => new[]
        {
            "27 putts: every distance from 3 to 11 feet, from all three tees. Short, then mid, then long.",
            "A make is 3 points, +1 for a center cut, +1 for pace no more than 3 inches past.",
            "135 is perfect. Aim for 100.",
        };
        protected override List<TournamentMath.Dealt> MakeList()
        {
            var all = new List<TournamentMath.Dealt>();
            foreach (int[] seg in new[] { new[] { 3, 5 }, new[] { 6, 8 }, new[] { 9, 11 } })
            {
                var part = new List<TournamentMath.Dealt>();
                for (int f = seg[0]; f <= seg[1]; f++) for (int t = 1; t <= 3; t++) part.Add(new TournamentMath.Dealt { Feet = f, Tee = t });
                all.AddRange(part.OrderBy(_ => Dice.Next()));
            }
            return all;
        }
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (!v.Made) return Call(0, false, "MISS");
            bool centre = v.HasEntry && Math.Abs(v.Entry - AppPutt.TeeEntry(Feet, Tee)) < CenterCutDeg;
            bool pace = v.Rollout != null && v.Rollout.Value <= PaceMaxPast;
            int pts = 3 + (centre ? 1 : 0) + (pace ? 1 : 0);
            Score[P] += pts;
            return Call(pts, true, "+" + pts, "Made" + (centre ? "   ·   center cut" : "") + (pace ? "   ·   pace" : ""));
        }
        public override string Progress
        {
            get
            {
                int i = Math.Min(Index, 26);
                string seg = i < 9 ? "SHORT 3-5 FT" : i < 18 ? "MID 6-8 FT" : "LONG 9-11 FT";
                return seg + "   ·   " + (i + 1) + " OF 27";
            }
        }
        public override VisualElement Board()
        {
            VisualElement tiles = Ui.Box("stat-tiles");
            string[] names = { "SHORT", "MID", "LONG" };
            for (int k = 0; k < 3; k++)
            {
                double pts = Shots.Skip(k * 9).Take(9).Sum(s => s.Points);
                tiles.Add(Ui.Box("glass stat-tile", Ui.Text(pts + "/45", "stat-value", Ui.Bold), Ui.Text(names[k], "caps", Ui.Bold)));
            }
            return Card("TARGET 100   ·   MAX 135", tiles);
        }
    }

    // ── Compare (Fitting): putters side by side over the same putts ──

    public sealed partial class CompareGame : RuleGame
    {
        readonly List<TournamentMath.Dealt> _list;
        readonly List<double>[] _pace = new List<double>[4], _entry = new List<double>[4];
        public CompareGame(object row)
        {
            _list = TournamentMath.Putts(row);
            if (_list.Count == 0) for (int f = 3; f <= 11; f += 2) _list.Add(new TournamentMath.Dealt { Feet = f, Tee = 2 });
            Names = new List<string> { "Putter 1", "Putter 2" };
        }
        protected override string OwnTitle => "Compare";
        public override string GameType => "FITTING";
        public override int MinPlayers => 2;
        public override int MaxPlayers => 4;
        public override string Icon => "Scales";
        public override string Words => "OUT OF 20";
        public override string[] HowTo => new[]
        {
            "Add two to four putters. You putt the same " + _list.Count + " putts with each one, one putter after another.",
            "Every putt scores up to 10 for a centered entry and up to 10 for pace.",
            "The putter with the best average comes out on top.",
        };
        protected override void Begin() { for (int i = 0; i < 4; i++) { _pace[i] = new List<double>(); _entry[i] = new List<double>(); } }
        int Done(int p) => Shots.Count(s => s.Player == p);
        protected override void Deal() { TournamentMath.Dealt d = _list[Math.Min(Done(P), _list.Count - 1)]; Feet = d.Feet; Tee = d.Tee; }
        protected override void Next() { if (Done(P) >= _list.Count) P = Math.Min(P + 1, Players - 1); }
        public override bool Over => Shots.Count >= _list.Count * Players;
        public override string Progress => "PUTT " + Math.Min(Done(P) + 1, _list.Count) + " OF " + _list.Count;
        protected override Shot Take(AppPutt.Verdict v)
        {
            double pace = v.Rollout == null ? 5 : Scoring.Speed(v.Rollout, 0, v.Made) / 5;
            double entry = v.Made ? Math.Max(0, 10 - Math.Round(Scoring.OffLine(v, Feet, Tee) / 10)) : 0;
            _pace[P].Add(pace); _entry[P].Add(entry);
            Score[P] = _pace[P].Average() + _entry[P].Average();
            return Call(pace + entry, v.Made, v.Made ? "MADE" : "MISS", "Accuracy " + Scoring.N1(entry) + "   ·   pace " + Scoring.N1(pace));
        }
        public override string ScoreText(int p) => Score[p].ToString("0.0", CultureInfo.InvariantCulture);
        public override string Outcome() { int w = Winner; return w < 0 ? "A tie" : Wording.Does(Names[w], "is", "are") + " best overall"; }
    }
}
