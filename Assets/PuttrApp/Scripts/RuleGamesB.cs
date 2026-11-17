// Rule games, second batch (puttr-app DeadCenter.js, Assessment.js, Horse.js, OhOne.js + ohone/, Reducer.js, Match.js,
// HighStriker.js, BeerPong.js). Constants are the app's.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class RuleCatalogB
    {
        public static void Register()
        {
            RuleCatalog.Add("DEADCENTER", _ => new DeadCenterGame());
            RuleCatalog.Add("ASSESSMENT", _ => new AssessmentGame());
            RuleCatalog.Add("HORSE", _ => new HorseGame());
            RuleCatalog.Add("OHONE", _ => new OhOneGame());
            RuleCatalog.Add("REDUCER", _ => new ReducerGame());
            RuleCatalog.Add("MATCH", _ => new MatchPlayGame());
            RuleCatalog.Add("HIGHSTRIKER", _ => new HighStrikerGame());
            RuleCatalog.Add("BEERPONG", _ => new BeerPongGame());
        }

        /// <summary>A 3 x 9 grid of every spot on the mat: pick a distance and a tee.</summary>
        public static VisualElement SpotPicker(int feet, int tee, Action<int, int> pick)
        {
            VisualElement grid = Ui.Box("");
            for (int t = 1; t <= 3; t++)
            {
                VisualElement row = Ui.Box("rule-pips");
                row.Add(Ui.Text(PracticeSession.TeeName(t).Substring(0, 1), "rule-pip rule-pip-label", Ui.Bold));
                for (int f = 3; f <= 11; f++)
                {
                    int ff = f, tt = t;
                    row.Add(Ui.Text(f.ToString(), f == feet && t == tee ? "rule-pip rule-pip-on" : "rule-pip", Ui.Bold).OnTap(() => pick(ff, tt)));
                }
                grid.Add(row);
            }
            return grid;
        }
    }

    // ── Dead Center: hole one within a degree of the perfect line. Fewest putts wins ──

    public sealed partial class DeadCenterGame : RuleGame
    {
        const double Tolerance = 1.0;
        int _feet = 6, _tee = 2;
        bool _random, _won;
        public override string Title => "Dead Center";
        public override string GameType => "DEADCENTER";
        public override bool LowerIsBetter => true;
        public override string Words => "PUTTS";
        public override string Icon => "Crosshair";
        public override string[] HowTo => new[]
        {
            "Find the center: hole a putt within 1 degree of the perfect line.",
            "Every putt counts, made or missed. Fewest putts wins.",
            "Play one spot, or a new random spot after every putt.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Spot"));
            VisualElement picker = Ui.Box("");
            void Draw() { picker.Clear(); if (!_random) picker.Add(RuleCatalogB.SpotPicker(_feet, _tee, (f, t) => { _feet = f; _tee = t; Draw(); })); }
            box.Add(Form.Pills(new[] { "Pick a spot", "Random putts" }, 0, i => { _random = i == 1; Draw(); }));
            box.Add(picker);
            Draw();
            return box;
        }
        protected override void Deal() { if (_random) { Feet = Roll(3, 11); Tee = Roll(1, 3); } else { Feet = _feet; Tee = _tee; } }
        public override bool Over => _won;
        public override string Progress => "PUTT " + (Shots.Count + 1);
        protected override Shot Take(AppPutt.Verdict v)
        {
            Score[0] = Shots.Count + 1;
            if (!v.HasEntry) return Call(0, false, "MISS");
            double angle = Math.Round((v.Entry - AppPutt.TeeEntry(Feet, Tee)) * 10) / 10, abs = Math.Abs(angle);
            string side = angle < 0 ? "PULL · LEFT" : angle > 0 ? "PUSH · RIGHT" : "";
            if (v.Made && abs <= Tolerance) { _won = true; return Call(0, true, abs == 0 ? "DEAD CENTER" : "PURE", Scoring.N1(abs) + "° off. Found it in " + (Shots.Count + 1) + "."); }
            string tier = abs <= 3 ? "ON LINE" : abs <= 6 ? "DRIFTING" : "OFF LINE";
            return Call(0, false, tier, Scoring.N1(abs) + "° " + side.ToLowerInvariant() + (v.Made ? "" : "   ·   missed"));
        }
        public override string ScoreText(int p) => Shots.Count.ToString();
        public override string Outcome() => _won ? "Found the center in " + Shots.Count : "Not found yet";
    }

    // ── Assessment: twelve set putts, read by the coach ──

    public sealed partial class AssessmentGame : RuleGame
    {
        static readonly int[,] Set = { { 4, 1 }, { 4, 3 }, { 5, 2 }, { 6, 1 }, { 6, 3 }, { 7, 2 }, { 8, 1 }, { 8, 3 }, { 9, 2 }, { 10, 1 }, { 10, 3 }, { 11, 2 } };
        List<int> _order;
        public override string Title => "Assessment";
        public override string GameType => "ASSESSMENT";
        public override string Words => "MADE";
        public override string Icon => "ClipboardText";
        public override string[] HowTo => new[]
        {
            "Twelve set putts from 4 to 11 feet, every break.",
            "Each putt is graded on entry and pace. The game counts your makes.",
            "At the end you get a read on your stroke: line, pace and misses.",
        };
        protected override void Begin() { var dice = new System.Random(); _order = Enumerable.Range(0, 12).OrderBy(_ => dice.Next()).ToList(); }
        protected override void Deal() { int i = _order[Math.Min(Shots.Count, 11)]; Feet = Set[i, 0]; Tee = Set[i, 1]; }
        public override bool Over => Shots.Count >= 12;
        public override string Progress => "PUTT " + Math.Min(Shots.Count + 1, 12) + " OF 12";
        public override int Saved => 0; // the app saves 0: the putts are what matter
        protected override Shot Take(AppPutt.Verdict v)
        {
            double entry = v.Made ? 50 * Math.Max(0, 1 - Scoring.OffLine(v, Feet, Tee) / 90) : 0;
            double score = entry + Scoring.Speed(v.Rollout, 0, v.Made);
            if (v.Made) Score[0]++;
            string grade = score >= 80 ? (score > 95 ? "A+" : "A") : score >= 60 ? (score > 75 ? "B+" : "B") : score >= 40 ? (score > 55 ? "C+" : "C") : score >= 20 ? (score > 35 ? "D+" : "D") : "F";
            return Call(score, v.Made, grade, (v.Made ? "Made" : "Missed") + "   ·   " + Math.Round(score) + " of 100   ·   pace " + AppPutt.FmtRollout(v.Rollout));
        }
        public override string ScoreText(int p) => Score[0] + " of " + Shots.Count;
        public override string Outcome() => Score[0] + " of 12 made";
    }

    // ── HORSE: call a shot, the others match it or take a letter ──

    public sealed partial class HorseGame : RuleGame
    {
        const double SwishHalf = 15;
        int[] _letters = new int[4];
        int _caller;
        bool _calling = true, _swish;
        public HorseGame() { Names = new List<string> { "You", "Player 2" }; }
        public override string Title => "HORSE";
        public override string GameType => "HORSE";
        public override int MinPlayers => 2;
        public override int MaxPlayers => 4;
        public override string Icon => "Horse";
        public override string Words => "LETTERS";
        public override bool LowerIsBetter => true;
        public override string[] HowTo => new[]
        {
            "Two to four players. The caller picks a spot and putts it.",
            "Make it, and everyone else must match it or take a letter. Call a swish to make them hit the center.",
            "Spell H-O-R-S-E and you're out. Last one standing wins.",
        };
        protected override void Begin() { _letters = new int[4]; _caller = First; _calling = true; Feet = 6; Tee = 2; Listen(); }
        /// <summary>The caller picks the spot (a tap on the court in the scene): feet 3 to 11, tee 1 to 3.</summary>
        public void CallSpot(int feet, int tee)
        {
            if (!_calling || Over || P == Bot) return;
            feet = Math.Max(3, Math.Min(11, feet)); tee = Math.Max(1, Math.Min(3, tee));
            if (feet == Feet && tee == Tee) return;
            Feet = feet; Tee = tee;
            Changed?.Invoke();
        }
        // Puttr calling the shot picks its own spot.
        public override void BotPrepare(System.Random dice)
        {
            if (!_calling) return;
            Feet = dice.Next(3, 12); Tee = dice.Next(1, 4);
            Changed?.Invoke();
        }
        protected override void Deal() { }
        bool Out(int p) => _letters[p] >= 5;
        int NextIn(int from) { int p = from; do p = (p + 1) % Players; while (Out(p) && p != from); return p; }
        public override bool Over => Players > 1 && Enumerable.Range(0, Players).Count(p => !Out(p)) <= 1;
        public override string Progress => _calling ? "CALL THE SHOT" : _swish ? "MATCH IT · SWISH" : "MATCH IT";
        protected override void Next() { }
        protected override Shot Take(AppPutt.Verdict v)
        {
            bool front = Scoring.Counts(v, Feet, Tee);
            bool swishOk = !_swish || Scoring.OffLine(v, Feet, Tee) <= SwishHalf;
            bool make = front && swishOk;
            string why = !v.Made ? "MISS" : !front ? "BACK DOOR" : !swishOk ? "NO SWISH" : "MAKE";
            if (_calling)
            {
                if (make) { _calling = false; P = NextIn(P); return Call(0, true, "SHOT SET", Names[_caller] + " made it. " + Names[P] + " must match."); }
                _caller = NextIn(_caller); P = _caller; _swish = false;
                return Call(0, false, why, Names[_caller] + " calls the next shot.");
            }
            string sub;
            if (!make)
            {
                _letters[P]++;
                Score[P] = _letters[P];
                sub = Names[P] + " takes " + "HORSE".Substring(_letters[P] - 1, 1) + (Out(P) ? " and is out." : ".");
            }
            else sub = Names[P] + " matched it.";
            int nextP = NextIn(P);
            if (nextP == _caller || Out(_caller) && nextP == NextIn(_caller)) { _calling = true; _swish = false; _caller = Out(_caller) ? NextIn(_caller) : _caller; P = _caller; sub += " " + Names[P] + " calls the next shot."; }
            else P = nextP;
            return Call(make ? 0 : 1, make, make ? "MATCHED" : "LETTER", sub);
        }
        public override string ScoreText(int p) => _letters[p] == 0 ? "–" : "HORSE".Substring(0, Math.Min(5, _letters[p]));
        public override int Winner { get { var left = Enumerable.Range(0, Players).Where(p => !Out(p)).ToList(); return left.Count == 1 ? left[0] : -1; } }
        public override string SoundFor(Shot s) => Over ? "rule_win" : s.Title == "LETTER" ? "horse_neigh" : s.Good ? "rule_make" : "rule_miss";
        public override VisualElement Board()
        {
            if (Over) return null;
            if (_calling)
            {
                // The spot is picked on the court (HorseScene shows it): the panel keeps only the swish choice.
                return Card("THE CALL", Form.Pills(new[] { "Any make", "Call swish" }, _swish ? 1 : 0, i => { _swish = i == 1; Changed?.Invoke(); }).Margin(8));
            }
            return Card("THE SHOT", Ui.Text(Feet + " ft   ·   " + PracticeSession.TeeName(Tee) + " tee" + (_swish ? "   ·   swish (center 30°)" : ""), "feed-name", Ui.Bold));
        }
    }

    // ── 01: count down to zero, finish on a double ──

    public sealed partial class OhOneGame : RuleGame
    {
        static readonly int[] Starts = { 101, 201, 301, 401, 501 };
        const int PerTurn = 3, Checkout = 40, MinDouble = 6;
        int _start = 301;
        int[] _left = new int[4], _putts = new int[4];
        int _inTurn, _shared = 6, _sharedTee = 2, _done = -1;
        readonly List<string> _recent = new List<string>();
        public override string Title => "01";
        public override string GameType => "OHONE";
        public override int MaxPlayers => 4;
        public override bool LowerIsBetter => true;
        public override string Words => "PUTTS";
        public override string Icon => "Target";
        public override string[] HowTo => new[]
        {
            "Count down from 301 to exactly zero, three putts a turn.",
            "A make scores its distance times the entry: x4 within 15°, x3 within 30°, x2 within 45°.",
            "Under 40 you must finish on a double: a center cut from half your number. Fewest putts wins.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Start from"));
            box.Add(Form.Pills(Starts.Select(s => s.ToString()).ToArray(), Array.IndexOf(Starts, _start), i => _start = Starts[i]));
            return box;
        }
        protected override void Begin() { for (int i = 0; i < 4; i++) { _left[i] = _start; _putts[i] = 0; } _inTurn = 0; _done = -1; NewShared(); }
        static bool IsDouble(int n) => n >= MinDouble && n <= 22 && n % 2 == 0;
        static bool Impossible(int n) => n > 0 && n <= Checkout && (n < 6 || n == 7);
        void NewShared()
        {
            string key;
            int guard = 0;
            do { _shared = Roll(3, 11); _sharedTee = Roll(1, 3); key = _shared + "-" + _sharedTee; } while ((_recent.Contains(key)) && guard++ < 30);
            _recent.Add(key); if (_recent.Count > 5) _recent.RemoveAt(0);
        }
        protected override void Deal()
        {
            int left = _left[P];
            if (left <= Checkout && IsDouble(left)) { Feet = left / 2; Tee = 2; return; }
            if (left <= Checkout)
            {
                // Setup: the longest single that leaves a double, else one that keeps it even.
                for (int f = 11; f >= 3; f--) if (IsDouble(left - f)) { Feet = f; Tee = Roll(1, 3); return; }
                for (int f = 11; f >= 3; f--) if (left - f >= 6 && (left - f) % 2 == 0) { Feet = f; Tee = Roll(1, 3); return; }
                Feet = Math.Min(11, Math.Max(3, left - 6)); Tee = Roll(1, 3); return;
            }
            Feet = _shared; Tee = _sharedTee;
        }
        protected override void Next()
        {
            _inTurn++;
            if (_inTurn < PerTurn) return;
            _inTurn = 0;
            int was = P;
            P = (P + 1) % Players;
            if (P <= was) NewShared();
        }
        public override bool Over => _done >= 0;
        public override string Progress => _left[P] <= Checkout && IsDouble(_left[P]) ? "DOUBLE OUT" : _left[P] <= Checkout ? "SETUP" : "PUTT " + (_inTurn + 1) + " OF 3";
        protected override Shot Take(AppPutt.Verdict v)
        {
            _putts[P]++;
            Score[P] = _putts[P];
            int left = _left[P];
            if (!v.Made) return Call(0, false, "MISS", left + " to go");
            if (Scoring.BackDoor(v, Feet, Tee)) return Call(0, false, "BACK DOOR", "Back-door putts don't count.");
            double off = Scoring.OffLine(v, Feet, Tee);
            bool doubleOut = left <= Checkout && IsDouble(left) && Feet == left / 2;
            int pts;
            string label;
            if (left <= Checkout)
            {
                bool dbl = off <= 15;
                if (doubleOut && dbl) { _left[P] = 0; _done = P; return Call(left, true, "OH ONE!", "Finished in " + _putts[P] + " putts."); }
                pts = dbl ? Feet * 2 : Feet;
                label = dbl ? "DOUBLE" : "SINGLE";
                if (dbl && (left - pts < MinDouble && left - pts != 0 || Impossible(left - pts))) { pts = Feet; label = "SINGLE"; }
                if (doubleOut && !dbl && left - pts < MinDouble) return Call(0, false, "NOT PRECISE", "Need a double to finish.");
            }
            else
            {
                int mult = off <= 15 ? 4 : off <= 30 ? 3 : off <= 45 ? 2 : 1;
                pts = Feet * mult;
                label = "x" + mult;
            }
            int after = left - pts;
            if (pts > left || after < 0 || Impossible(after) || after == 0 && !doubleOut) return Call(0, false, "BUST", "No legal checkout. Still " + left + ".");
            _left[P] = after;
            return Call(pts, true, label + "   +" + pts, after + " to go");
        }
        public override string ScoreText(int p) => _left[p].ToString();
        public override int Saved => _putts[0];
        public override int Winner => _done;
        public override string Outcome() => _done < 0 ? "Not finished" : Players == 1 ? "Finished in " + _putts[0] + " putts" : Names[_done] + " wins in " + _putts[_done];
        public override VisualElement Board() => Card("REMAINING", Ui.Text(_left[P].ToString(), "rule-big", Ui.Bold),
            Ui.Text(_left[P] <= Checkout && IsDouble(_left[P]) ? "Center cut from " + (_left[P] / 2) + " ft to finish" : _left[P] <= Checkout ? "Set up a double" : _putts[P] + " putts taken", "muted center", Ui.Regular));
    }

    // ── Reducer: a make scores its distance, through a window you choose ──

    public sealed partial class ReducerGame : ListGame
    {
        static readonly int[] Windows = { 120, 90, 60, 30 };
        int _window = 120;
        public override string Title => "Reducer";
        public override string GameType => "REDUCER";
        public override int MaxPlayers => 4;
        public override string Words => "FEET";
        public override string Icon => "Funnel";
        public override string[] HowTo => new[]
        {
            "Nine random putts. A make scores its distance in feet.",
            "It only counts if it enters through your window: pick 120°, 90°, 60° or 30°.",
            "The narrower the window, the purer the line has to be.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Entry window"));
            box.Add(Form.Pills(Windows.Select(w => w + "°").ToArray(), 0, i => _window = Windows[i]));
            return box;
        }
        protected override List<TournamentMath.Dealt> MakeList() => Enumerable.Range(0, 9).Select(_ => new TournamentMath.Dealt { Feet = Dice.Next(3, 12), Tee = Dice.Next(1, 4) }).ToList();
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (!v.Made) return Call(0, false, "MISS");
            double off = Scoring.OffLine(v, Feet, Tee);
            if (off >= _window / 2.0) return Call(0, false, off >= 90 ? "BACK DOOR" : "OUTSIDE", Scoring.N1(off) + "° off: outside your " + _window + "° window.");
            Score[P] += Feet;
            return Call(Feet, true, "+" + Feet + " FT", Scoring.N1(off) + "° off the line");
        }
    }

    // ── Match Play: two players, nine holes ──

    public sealed partial class MatchPlayGame : RuleGame
    {
        readonly List<TournamentMath.Dealt> _holes = new List<TournamentMath.Dealt>();
        int _hole, _mc;
        bool _centerCut, _sudden, _over;
        bool? _first; double _firstOff;
        readonly List<int> _results = new List<int>();
        public MatchPlayGame() { Names = new List<string> { "You", "Player 2" }; }
        public override string Title => "Match Play";
        public override string GameType => "MATCH";
        public override int MinPlayers => 2;
        public override int MaxPlayers => 2;
        public override string Words => "HOLES";
        public override string Icon => "Flag";
        public override string[] HowTo => new[]
        {
            "Two players, nine holes, one putt each per hole.",
            "Make it while the other misses and you win the hole. Both or neither and it's halved.",
            "Center cut rule: when both make, the truer entry wins. All square after nine is sudden death.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("When both make"));
            box.Add(Form.Pills(new[] { "Halved", "Center cut wins" }, 0, i => _centerCut = i == 1));
            return box;
        }
        protected override void Begin()
        {
            if (Players < 2) Names.Add("Player 2");
            Score = new double[2];
            _holes.Clear();
            for (int i = 0; i < 9; i++) _holes.Add(new TournamentMath.Dealt { Feet = Roll(3, 11), Tee = Roll(1, 3) });
            _hole = 0; _mc = 0; _sudden = false; _over = false; _first = null; _results.Clear();
        }
        protected override void Deal() { TournamentMath.Dealt d = _holes[_hole % 9]; Feet = d.Feet; Tee = d.Tee; }
        public override bool Over => _over;
        public override string Progress => _sudden ? "SUDDEN DEATH" : "HOLE " + (_hole + 1) + " OF 9";
        protected override Shot Take(AppPutt.Verdict v)
        {
            bool make = Scoring.Counts(v, Feet, Tee);
            double off = Scoring.OffLine(v, Feet, Tee);
            if (P == 0) { _first = make; _firstOff = off; return Call(make ? 1 : 0, make, make ? "MADE" : "MISSED", Names[1] + " to putt."); }
            int won = _first == true && !make ? 1 : _first != true && make ? -1 : 0;
            if (won == 0 && _centerCut && _first == true && make) won = _firstOff < off ? 1 : off < _firstOff ? -1 : 0;
            _mc += won;
            _results.Add(won);
            _hole++;
            int left = 9 - _hole;
            string status = Status();
            if (_sudden ? _mc != 0 : Math.Abs(_mc) > left || left == 0 && _mc != 0) _over = true;
            else if (!_sudden && left == 0) { _sudden = true; _hole = 0; }
            Score[0] = Math.Max(0, _mc); Score[1] = Math.Max(0, -_mc);
            string head = won == 1 ? Names[0].ToUpperInvariant() + " WINS THE HOLE" : won == -1 ? Names[1].ToUpperInvariant() + " WINS THE HOLE" : "HALVED";
            return Call(won, won != 0, head, _over ? Result() : status);
        }
        protected override void Next() => P = P == 0 ? 1 : 0;
        string Status()
        {
            if (_mc == 0) return "All square";
            int left = 9 - _hole;
            string lead = Names[_mc > 0 ? 0 : 1];
            return Math.Abs(_mc) == left ? lead + " dormie" : lead + " " + Math.Abs(_mc) + " up";
        }
        string Result()
        {
            string lead = Names[_mc > 0 ? 0 : 1];
            if (_sudden) return lead + " wins on extra holes";
            int left = 9 - _hole;
            return left > 0 ? lead + " wins " + Math.Abs(_mc) + " & " + left : lead + " wins " + Math.Abs(_mc) + " up";
        }
        public override int Winner => _mc > 0 ? 0 : _mc < 0 ? 1 : -1;
        public override string SoundFor(Shot s) => Over ? "golf_clap" : s.Points != 0 && s.Player == 1 ? "golf_clap" : s.Good ? "rule_make" : "rule_miss";
        public override string Bed => "seaside_loop";
        public override int Saved => _mc > 0 ? 1 : 0;
        public override string ScoreText(int p) => _mc == 0 ? "AS" : (p == 0) == (_mc > 0) ? Math.Abs(_mc) + " UP" : "–";
        public override string Outcome() => _over ? Result() : Status();
        public override VisualElement Board() => Card("MATCH   ·   " + Status().ToUpperInvariant(), Pips(9, i => i < _results.Count && _results[i] != 0, i => i < _results.Count ? (_results[i] == 1 ? Names[0].Substring(0, 1) : _results[i] == -1 ? Names[1].Substring(0, 1) : "½") : (i + 1).ToString()));
    }

    // ── High Striker: send it as far up the mat as you dare, short of the hole ──

    public sealed partial class HighStrikerGame : ListGame
    {
        public override string Title => "High Striker";
        public override string GameType => "HIGHSTRIKER";
        public override int MaxPlayers => 4;
        public override string Icon => "Bell";
        public override string[] HowTo => new[]
        {
            "Nine putts, 3 to 11 feet, all from the center tee.",
            "Hit it as close to the front of the cup as you can without reaching it. The closer, the higher the bell.",
            "Reach the hole and it scores nothing. 100 a putt is perfect.",
        };
        protected override List<TournamentMath.Dealt> MakeList() => Enumerable.Range(3, 9).Select(f => new TournamentMath.Dealt { Feet = f, Tee = 2 }).ToList();
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (Raw == null || Raw.Path == null || Raw.Path.Count == 0 || Raw.Hole.Diameter <= 0) return Call(0, false, "NO READ");
            int[][] bp = AppPutt.Path(Raw.Path);
            double[] hb = AppPutt.Hole(Raw.Hole);
            double holeBottom = hb[1] + hb[2] / 2;
            double peak = bp.Min(p => p[1]);
            if (peak <= holeBottom) return Call(0, false, "TOO FAR", "It reached the hole.");
            double score = Math.Max(0, 100 - (peak - holeBottom) / 2);
            Score[P] += score;
            return Call(score, score > 0, score > 80 ? "DING!" : Math.Round(score).ToString(), Math.Round(score) + " of 100");
        }
        public override string ScoreText(int p) => Math.Round(Score[p]).ToString();
        public override int Saved => (int)Math.Round(Score[0]);
        public override string SoundFor(Shot s) => s.Points > 80 ? "bell_ding" : s.Points > 0 ? "rule_make" : "rule_miss";
        public override string Bed => "carnival_loop";
    }

    // ── Beer Pong: two players, ten cups each, two putts a turn ──

    public sealed partial class BeerPongGame : RuleGame
    {
        readonly List<TournamentMath.Dealt> _cups = new List<TournamentMath.Dealt>();
        bool[][] _sunk = { new bool[10], new bool[10] };
        int _inTurn, _first = 0, _pick = -1;
        bool _ballsBack, _rebuttal = true, _master, _inRebuttal, _overtime, _over, _turnAllMade = true;
        int _winner = -1;
        public BeerPongGame() { Names = new List<string> { "You", "Player 2" }; }
        public override string Title => "Beer Pong";
        public override string GameType => "BEERPONG";
        public override int MinPlayers => 2;
        public override int MaxPlayers => 2;
        public override string Icon => "Beer";
        public override string Words => "CUPS";
        public override string[] HowTo => new[]
        {
            "Two players, ten cups each, two putts a turn. Each cup is its own putt.",
            "Sink all ten first. Clear them shooting first and your opponent gets a rebuttal.",
            "Pong Master narrows the entry window with every cup you sink.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Rules"));
            box.Add(Form.Pills(new[] { "Rebuttal on", "Rebuttal off" }, 0, i => _rebuttal = i == 0));
            box.Add(Form.Pills(new[] { "Balls back off", "Balls back on" }, 0, i => _ballsBack = i == 1).Margin(6));
            box.Add(Form.Pills(new[] { "Normal", "Pong Master" }, 0, i => _master = i == 1).Margin(6));
            return box;
        }
        protected override void Begin()
        {
            if (Players < 2) Names.Add("Player 2");
            Score = new double[2];
            _cups.Clear();
            for (int i = 0; i < 10; i++) _cups.Add(new TournamentMath.Dealt { Feet = Roll(3, 11), Tee = Roll(1, 3) });
            _sunk = new[] { new bool[10], new bool[10] };
            _inTurn = 0; _first = 0; _over = false; _overtime = false; _inRebuttal = false; _winner = -1; _pick = -1;
        }
        int Target => _pick >= 0 && !_sunk[P][_pick] ? _pick : Array.FindIndex(_sunk[P], s => !s);
        protected override void Deal() { int c = Math.Max(0, Target); Feet = _cups[c].Feet; Tee = _cups[c].Tee; }
        int Count(int p) => _sunk[p].Count(s => s);
        double Window => _master ? Math.Max(30, 165 - 15 * Count(P)) : 180;
        public override bool Over => _over;
        public override string Progress => _overtime ? "OVERTIME" : _inRebuttal ? "REBUTTAL" : "PUTT " + (_inTurn + 1) + " OF 2";
        protected override void Next() { }
        protected override Shot Take(AppPutt.Verdict v)
        {
            int cup = Math.Max(0, Target);
            double off = Scoring.OffLine(v, Feet, Tee);
            bool make = v.Made && off < Window / 2;
            string head = make ? "CUP!" : v.Made ? "BACK DOOR" : "MISS";
            if (make) { _sunk[P][cup] = true; Score[P] = Count(P); _pick = -1; }
            if (_inRebuttal)
            {
                if (!make) { _over = true; _winner = 1 - P; return Call(0, false, head, "Rebuttal over. " + Names[_winner] + " wins."); }
                if (Count(P) == 10) { Overtime(); return Call(1, true, "OVERTIME", "Rebuttal cleared it. Three cups each."); }
                return Call(1, true, head, "Keep shooting.");
            }
            if (Count(P) == 10)
            {
                if (_rebuttal && P == _first) { _inRebuttal = true; P = 1 - P; _inTurn = 0; return Call(1, true, "TABLE CLEARED", Names[P] + " gets a rebuttal."); }
                _over = true; _winner = P; return Call(1, true, "TABLE CLEARED", Names[P] + " wins.");
            }
            _turnAllMade &= make;
            _inTurn++;
            if (_inTurn >= 2)
            {
                bool back = _ballsBack && _turnAllMade;
                _inTurn = 0; _turnAllMade = true;
                if (!back) P = 1 - P;
                return Call(make ? 1 : 0, make, back ? "BALLS BACK" : head, back ? "Two for two: shoot again." : Names[P] + "'s turn.");
            }
            return Call(make ? 1 : 0, make, head, "");
        }
        void Overtime()
        {
            for (int p = 0; p < 2; p++) for (int i = 0; i < 10; i++) _sunk[p][i] = i < 7;
            Score[0] = Score[1] = 7;
            _overtime = true; _inRebuttal = false; _inTurn = 0;
            _first = P; // the one who shot second goes first
        }
        public override int Winner => _winner;
        public override string ScoreText(int p) => Count(p) + "/10";
        public override string SoundFor(Shot s) => Over ? "rule_win" : s.Title == "CUP!" || s.Title == "BALLS BACK" || s.Good ? "cup_plop" : "pingpong_bounce";
        public override VisualElement Board()
        {
            if (_over) return null;
            // The cups this Player is shooting at, 4-3-2-1. Tap one to aim at it.
            VisualElement tri = Ui.Box("rule-cups");
            int k = 0;
            foreach (int rowN in new[] { 4, 3, 2, 1 })
            {
                VisualElement row = Ui.Box("rule-cuprow");
                for (int j = 0; j < rowN; j++, k++)
                {
                    int cup = k;
                    string cls = _sunk[P][cup] ? "rule-cup rule-cup-sunk" : cup == Target ? "rule-cup rule-cup-now" : "rule-cup";
                    VisualElement c = Ui.Box(cls, Ui.Text(_sunk[P][cup] ? "" : _cups[cup].Feet.ToString(), "rule-cup-t", Ui.Bold));
                    if (!_sunk[P][cup]) c.OnTap(() => { _pick = cup; Redeal(); });
                    row.Add(c);
                }
                tri.Add(row);
            }
            return Card(Names[P].ToUpperInvariant() + (_master ? "   ·   WINDOW " + Window + "°" : "") + "   ·   TAP A CUP", tri);
        }
    }
}
