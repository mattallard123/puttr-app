// Rule games, third batch: the pace and entry games (puttr-app Leapfrog.js, Doors.js, HighSide.js, Switchback.js,
// Gears.js, Groove.js, AroundTheClock.js). Constants are the app's.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class RuleCatalogC
    {
        public static void Register()
        {
            RuleCatalog.Add("LEAPFROG", _ => new LeapfrogGame());
            RuleCatalog.Add("DOORS", _ => new DoorsGame());
            RuleCatalog.Add("HIGHSIDE", _ => new HighSideGame());
            RuleCatalog.Add("SWITCHBACK", _ => new SwitchbackGame());
            RuleCatalog.Add("GEARS", _ => new GearsGame());
            RuleCatalog.Add("GROOVE", _ => new GrooveGame());
            RuleCatalog.Add("CLOCK", _ => new AroundTheClockGame());
            RuleCatalog.Add("AROUNDTHECLOCK", _ => new AroundTheClockGame());
        }

        public static VisualElement SpeedChoice(Action<double> pick)
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Your speed"));
            double[] t = { 0, 3, 6 };
            box.Add(Form.Pills(new[] { "Die it", "Firm (3 in)", "Aggressive (6 in)" }, 0, i => pick(t[i])));
            return box;
        }
    }

    // ── Leapfrog: short, long, short, long. Pace is the score ──

    public sealed partial class LeapfrogGame : ListGame
    {
        double _target;
        public override string Title => "Leapfrog";
        public override string GameType => "LEAPFROG";
        public override int MaxPlayers => 4;
        public override string Icon => "Frog";
        public override string[] HowTo => new[]
        {
            "Twelve putts that jump between short (3 to 6 ft) and long (8 to 11 ft).",
            "Pace scores: 10 within 3 inches of your speed, 6 within 7, 2 otherwise.",
            "A make doubles it. A miss still scores its pace.",
        };
        public override VisualElement Options() => RuleCatalogC.SpeedChoice(t => _target = t);
        protected override List<TournamentMath.Dealt> MakeList()
        {
            var lows = new[] { 3, 4, 5, 6, 3, 4 }.OrderBy(_ => Dice.Next()).ToList();
            var highs = new[] { 11, 10, 9, 8, 11, 10 }.OrderBy(_ => Dice.Next()).ToList();
            var list = new List<TournamentMath.Dealt>();
            for (int i = 0; i < 6; i++) { list.Add(new TournamentMath.Dealt { Feet = lows[i], Tee = Dice.Next(1, 4) }); list.Add(new TournamentMath.Dealt { Feet = highs[i], Tee = Dice.Next(1, 4) }); }
            return list;
        }
        protected override Shot Take(AppPutt.Verdict v)
        {
            int pts; string word;
            if (v.Rollout == null) { pts = 2; word = "NO READ"; }
            else
            {
                double err = v.Rollout.Value - _target, abs = Math.Abs(err);
                if (abs <= 3) { pts = 10; word = "PERFECT PACE"; }
                else if (abs <= 7) { pts = 6; word = err > 0 ? "LITTLE FIRM" : "LITTLE SOFT"; }
                else { pts = 2; word = err > 0 ? "WAY TOO FIRM" : "WAY TOO SOFT"; }
            }
            if (v.Made) pts *= 2;
            Score[P] += pts;
            return Call(pts, pts >= 10, word, (v.Made ? "Made: doubled to " + pts : "Miss: pace still counts, " + pts) + (v.Rollout != null ? "   ·   " + AppPutt.FmtRollout(v.Rollout) : ""));
        }
    }

    // ── Doors: enter through the door that is called ──

    public sealed partial class DoorsGame : ListGame
    {
        static readonly string[][] Labels = { new[] { "LEFT", "CENTER", "RIGHT" }, new[] { "FAR LEFT", "LEFT", "CENTER", "RIGHT", "FAR RIGHT" }, new[] { "LEFT 3", "LEFT 2", "LEFT 1", "CENTER", "RIGHT 1", "RIGHT 2", "RIGHT 3" } };
        static readonly double[][] Bounds = { new[] { -18.0, 18 }, new[] { -54.0, -18, 18, 54 }, new[] { -64.3, -38.6, -12.9, 12.9, 38.6, 64.3 } };
        int _level;
        readonly List<int> _calls = new List<int>();
        public override string Title => "Doors";
        public override string GameType => "DOORS";
        public override int MaxPlayers => 4;
        public override string Icon => "Door";
        public override string[] HowTo => new[]
        {
            "Twelve putts. Each one calls a door: the part of the cup it must fall through.",
            "The called door is 10 points. Any other door is 2. A miss or the back of the cup is nothing.",
            "Hard has five doors, Diabolical seven.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Doors"));
            box.Add(Form.Pills(new[] { "Standard (3)", "Hard (5)", "Diabolical (7)" }, 0, i => _level = i));
            return box;
        }
        double Centre(int door)
        {
            double[] b = Bounds[_level];
            double lo = door == 0 ? -90 : b[door - 1], hi = door == b.Length ? 90 : b[door];
            return (lo + hi) / 2;
        }
        protected override List<TournamentMath.Dealt> MakeList()
        {
            int n = Labels[_level].Length;
            _calls.Clear();
            _calls.AddRange(Enumerable.Range(0, 12).Select(i => i % n).OrderBy(_ => Dice.Next()));
            return _calls.Select(d => new TournamentMath.Dealt { Feet = Dice.Next(3, 12), Tee = Centre(d) < -45 ? 1 : Centre(d) > 45 ? 3 : 2 }).ToList();
        }
        int DoorFor(double a) => Bounds[_level].Count(b => a > b);
        public override string SoundFor(Shot s) => Over ? "rule_win" : s.Points >= 10 ? "door_creak" : s.Points > 0 ? "door_slam" : "rule_miss";
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (!v.Made) return Call(0, false, "MISS");
            if (!v.HasEntry || Math.Abs(v.Entry) > 90) return Call(0, false, "BACK DOOR");
            int door = DoorFor(v.Entry), called = _calls[Index];
            if (door == called) { Score[P] += 10; return Call(10, true, Labels[_level][called] + "!", "Through the called door"); }
            Score[P] += 2;
            return Call(2, false, "WRONG DOOR", "In through " + Labels[_level][door].ToLowerInvariant() + ", called " + Labels[_level][called].ToLowerInvariant());
        }
        public override VisualElement Board()
        {
            if (Over) return null;
            int called = _calls[Math.Min(Index, _calls.Count - 1)];
            return Card("ENTER THROUGH", Ui.Text(Labels[_level][called], "rule-big", Ui.Bold), Pips(Labels[_level].Length, i => i == called, i => Labels[_level][i].Split(' ')[0].Substring(0, 1) + (Labels[_level][i].Contains(" ") ? Labels[_level][i].Split(' ')[1].Substring(0, 1) : "")));
        }
    }

    // ── High Side: miss above the hole, never below ──

    public sealed partial class HighSideGame : ListGame
    {
        const int LowMiss = -3;
        int _makes, _high, _low;
        public override string Title => "High Side";
        public override string GameType => "HIGHSIDE";
        public override int MaxPlayers => 4;
        public override string Icon => "TrendUp";
        public override string[] HowTo => new[]
        {
            "Twelve putts from the side tees. From the left tee the high side is left; from the right, right.",
            "A make from the high side scores its distance. A make on the low side scores nothing.",
            "A miss on the high side is free. A miss on the low side costs 3.",
        };
        protected override List<TournamentMath.Dealt> MakeList()
        {
            var tees = Enumerable.Repeat(1, 6).Concat(Enumerable.Repeat(3, 6)).OrderBy(_ => Dice.Next()).ToList();
            return tees.Select(t => new TournamentMath.Dealt { Feet = Dice.Next(3, 12), Tee = t }).ToList();
        }
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (v.Made)
            {
                bool low = Tee == 1 ? v.Entry >= 18 : v.Entry <= -18;
                if (low) { _low++; return Call(0, false, "LOW SIDE", "Made it, but from below."); }
                _makes++; Score[P] += Feet;
                return Call(Feet, true, "+" + Feet, "Made from the high side");
            }
            string side = v.MissSide ?? (v.StartLine.HasValue ? (v.StartLine.Value < 0 ? "LEFT" : "RIGHT") : null);
            bool highMiss = side == null || (Tee == 1 ? side == "LEFT" : side == "RIGHT");
            if (highMiss) { _high++; return Call(0, true, "GOOD MISS", "High side"); }
            _low++; Score[P] += LowMiss;
            return Call(LowMiss, false, "NEVER LOW", "Missed low: -3");
        }
        public override VisualElement Board() => Card("HIGH SIDE: " + (Tee == 1 ? "LEFT" : "RIGHT"), Ui.Box("stat-tiles",
            Ui.Box("glass stat-tile", Ui.Text(_makes.ToString(), "stat-value", Ui.Bold), Ui.Text("MAKES", "stat-label", Ui.Bold)),
            Ui.Box("glass stat-tile", Ui.Text(_high.ToString(), "stat-value", Ui.Bold), Ui.Text("HIGH MISSES", "stat-label", Ui.Bold)),
            Ui.Box("glass stat-tile", Ui.Text(_low.ToString(), "stat-value", Ui.Bold), Ui.Text("LOW SIDE", "stat-label", Ui.Bold))));
    }

    // ── Switchback: the break switches every putt ──

    public sealed partial class SwitchbackGame : ListGame
    {
        public override string Title => "Switchback";
        public override string GameType => "SWITCHBACK";
        public override int MaxPlayers => 4;
        public override string Icon => "ArrowsLeftRight";
        public override string[] HowTo => new[]
        {
            "Twelve putts that switch tees every time: left, right, left, right.",
            "A make scores its distance less the share it was off the line.",
            "Back-door makes score nothing.",
        };
        protected override List<TournamentMath.Dealt> MakeList()
        {
            int start = Dice.Next(2) == 0 ? 1 : 3;
            return Enumerable.Range(0, 12).Select(i => new TournamentMath.Dealt { Feet = Dice.Next(3, 12), Tee = i % 2 == 0 ? start : 4 - start }).ToList();
        }
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (!v.Made) return Call(0, false, "MISS");
            double off = Scoring.OffLine(v, Feet, Tee);
            if (off > 90) return Call(0, false, "BACK DOOR");
            double pts = Math.Round(Feet * (1 - off / 90) * 10) / 10;
            Score[P] += pts;
            return Call(pts, off <= 18, "+" + Scoring.N1(pts), Math.Round(off) + "° off the line");
        }
        public override string ScoreText(int p) => Scoring.N1(Score[p]);
        public override VisualElement Board() => Over ? null : Card(Tee == 1 ? "LEFT TO RIGHT" : "RIGHT TO LEFT", Ui.Text("Switches every putt", "muted", Ui.Regular), base.Board());
    }

    // ── Gears: pace that climbs a gear at a time ──

    public sealed partial class GearsGame : RuleGame
    {
        const int Putts = 15, Top = 6, Shift = 25, Bonus = 10;
        int _feet = 6, _tee = 2, _round, _won = -1;
        int[] _gear = { 1, 1, 1, 1 }, _best = { 1, 1, 1, 1 };
        public override string Title => "Gears";
        public override string GameType => "GEARS";
        public override int MaxPlayers => 4;
        public override string Icon => "GearSix";
        public override string[] HowTo => new[]
        {
            "Fifteen putts from one spot. Each gear wants a little more pace: gear 1 is 2 in short to 4 in past, each gear 3 in more.",
            "In the band: shift up for 25. Out of it: stall down a gear. Holing it doesn't matter.",
            "Reach top gear to win, with 10 for every putt left.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Spot"));
            VisualElement picker = Ui.Box("");
            void Draw() { picker.Clear(); picker.Add(RuleCatalogB.SpotPicker(_feet, _tee, (f, t) => { _feet = f; _tee = t; Draw(); })); }
            Draw();
            box.Add(picker);
            return box;
        }
        protected override void Begin() { _gear = new[] { 1, 1, 1, 1 }; _best = new[] { 1, 1, 1, 1 }; _round = 0; _won = -1; }
        protected override void Deal() { Feet = _feet; Tee = _tee; }
        static (double lo, double hi) Band(int g) => (-2 + (g - 1) * 3, 4 + (g - 1) * 3);
        protected override void Next() { P = (P + 1) % Players; if (P == 0) _round++; }
        public override bool Over => _won >= 0 || _round >= Putts;
        public override string Progress => "PUTT " + Math.Min(_round + 1, Putts) + " OF " + Putts;
        protected override Shot Take(AppPutt.Verdict v)
        {
            var (lo, hi) = Band(_gear[P]);
            if (v.Rollout != null && v.Rollout.Value >= lo && v.Rollout.Value <= hi)
            {
                _gear[P]++;
                _best[P] = Math.Max(_best[P], _gear[P]);
                int pts = Shift;
                if (_gear[P] >= Top) { pts += (Putts - (_round + 1)) * Bonus; _won = P; Score[P] += pts; return Call(pts, true, "TOP GEAR!", "+" + pts); }
                Score[P] += pts;
                return Call(pts, true, "SHIFT UP", "Gear " + _gear[P] + "   ·   " + AppPutt.FmtRollout(v.Rollout));
            }
            _gear[P] = Math.Max(1, _gear[P] - 1);
            return Call(0, false, "STALL", (v.Rollout == null ? "No read" : AppPutt.FmtRollout(v.Rollout)) + ": wanted " + lo + " to " + hi + " in");
        }
        public override int Winner => _won >= 0 ? _won : base.Winner;
        public override string SoundFor(Shot s) => Over ? "rule_win" : s.Good ? "gear_shift" : "rule_miss";
        public override VisualElement Board()
        {
            var (lo, hi) = Band(Math.Min(_gear[P], 5));
            return Card("GEAR " + _gear[P] + "   ·   PACE " + (lo < 0 ? lo + "" : "+" + lo) + " TO +" + hi + " IN", Pips(Top, i => i < _gear[P], i => (i + 1).ToString()));
        }
    }

    // ── Groove: stay in a pace window that keeps shrinking ──

    public sealed partial class GrooveGame : RuleGame
    {
        static readonly double[] Windows = { 9, 7, 5, 3.5, 2.5 };
        const int Putts = 15, PerRound = 3, MaxMisses = 3;
        int _feet = 8, _tee = 2, _index;
        double _target;
        int[] _streak = new int[4], _misses = new int[4];
        bool[] _out = new bool[4];
        int _roundSeen = -1;
        public override string Title => "Groove";
        public override string GameType => "GROOVE";
        public override int MaxPlayers => 4;
        public override string Icon => "Waveform";
        public override string[] HowTo => new[]
        {
            "Fifteen putts from one spot, five rounds of three. Get it to the hole without running past the window.",
            "The window shrinks every round, from 9 in past to 2.5 in. In the window: round x streak points.",
            "Three misses in a round and you're out.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Spot"));
            VisualElement picker = Ui.Box("");
            void Draw() { picker.Clear(); picker.Add(RuleCatalogB.SpotPicker(_feet, _tee, (f, t) => { _feet = f; _tee = t; Draw(); })); }
            Draw();
            box.Add(picker);
            box.Add(RuleCatalogC.SpeedChoice(t => _target = t));
            return box;
        }
        int Round => Math.Min(4, _index / PerRound);
        protected override void Begin() { _streak = new int[4]; _misses = new int[4]; _out = new bool[4]; _index = 0; _roundSeen = 0; }
        protected override void Deal() { Feet = _feet; Tee = _tee; }
        protected override void Next()
        {
            int start = P;
            do { P = (P + 1) % Players; if (P == 0) _index++; } while (_out[P] && P != start);
            if (Round != _roundSeen) { _roundSeen = Round; _misses = new int[4]; }
        }
        int Alive => Enumerable.Range(0, Players).Count(p => !_out[p]);
        public override bool Over => _index >= Putts || (Players == 1 ? _out[0] : Alive <= 1);
        public override string Progress => "ROUND " + (Round + 1) + " OF 5";
        protected override Shot Take(AppPutt.Verdict v)
        {
            double w = Windows[Round];
            bool inside = v.Rollout != null && v.Rollout.Value >= _target - 2 && v.Rollout.Value <= _target + w;
            if (inside)
            {
                _streak[P]++;
                int pts = (Round + 1) * _streak[P];
                Score[P] += pts;
                return Call(pts, true, "IN THE WINDOW", "+" + pts + "   ·   streak " + _streak[P]);
            }
            _streak[P] = 0;
            _misses[P]++;
            if (_misses[P] >= MaxMisses) { _out[P] = true; return Call(0, false, "BUSTED OUT", "Third miss this round"); }
            return Call(0, false, "OUT", (v.Rollout == null ? "No read" : AppPutt.FmtRollout(v.Rollout)) + ": streak reset");
        }
        public override int Winner => Players > 1 && Alive == 1 ? Enumerable.Range(0, Players).First(p => !_out[p]) : base.Winner;
        public override VisualElement Board() => Card("WINDOW: TO THE HOLE, MAX +" + Scoring.N1(Windows[Round]) + " IN", Ui.Text(_streak[P].ToString(), "rule-big", Ui.Bold), Ui.Text("PACE STREAK   ·   " + _misses[P] + " of 3 misses this round", "muted center", Ui.Regular));
    }

    // ── Around the Clock: collect every part of the cup ──

    public sealed partial class AroundTheClockGame : RuleGame
    {
        const int Rounds = 15, Points = 10, BonusPer = 5, Distance = 6;
        int _sectors = 5, _round, _won = -1;
        bool[][] _got;
        public override string Title => "Around the Clock";
        public override string GameType => "CLOCK";
        public override int MaxPlayers => 4;
        public override string Icon => "Clock";
        public override string[] HowTo => new[]
        {
            "Six-foot putts. Hole one through every part of the cup, left edge to right edge.",
            "A new part is 10 points. The tee moves to set up the part you need next.",
            "Collect them all to win, with 5 for every round left.",
        };
        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Parts of the cup"));
            int[] n = { 5, 7, 9 };
            box.Add(Form.Pills(new[] { "Standard (5)", "Hard (7)", "Diabolical (9)" }, 0, i => _sectors = n[i]));
            return box;
        }
        protected override void Begin() { _got = Enumerable.Range(0, 4).Select(_ => new bool[_sectors]).ToArray(); _round = 0; _won = -1; }
        int SectorFor(double a) => Mathf.Clamp((int)Math.Floor((a + 90) / (180.0 / _sectors)), 0, _sectors - 1);
        double Centre(int s) => -90 + (s + 0.5) * 180.0 / _sectors;
        int NextSector => Array.FindIndex(_got[P], g => !g);
        protected override void Deal() { Feet = Distance; int s = Math.Max(0, NextSector); Tee = Centre(s) < -45 ? 1 : Centre(s) > 45 ? 3 : 2; }
        protected override void Next() { P = (P + 1) % Players; if (P == 0) _round++; }
        public override bool Over => _won >= 0 || _round >= Rounds;
        public override string Progress => "ROUND " + Math.Min(_round + 1, Rounds) + " OF " + Rounds;
        protected override Shot Take(AppPutt.Verdict v)
        {
            if (!v.Made) return Call(0, false, "MISS");
            if (!v.HasEntry || Math.Abs(v.Entry) > 90) return Call(0, false, "BACK DOOR");
            int s = SectorFor(v.Entry);
            if (_got[P][s]) return Call(0, false, "AGAIN", "Already have that part");
            _got[P][s] = true;
            int pts = Points;
            if (_got[P].All(g => g)) { pts += (Rounds - (_round + 1)) * BonusPer; _won = P; }
            Score[P] += pts;
            return Call(pts, true, _won == P ? "CLOCK DONE!" : "+" + pts, _got[P].Count(g => g) + " of " + _sectors);
        }
        public override int Winner => _won >= 0 ? _won : base.Winner;
        public override string SoundFor(Shot s) => _won >= 0 ? "clock_chime" : Over ? "rule_win" : s.Good ? "rule_make" : "rule_miss";
        public override VisualElement Board() => Card("COLLECTED " + _got[P].Count(g => g) + " OF " + _sectors + "   ·   NEXT: " + (NextSector >= 0 ? (NextSector + 1).ToString() : "-"), Pips(_sectors, i => _got[P][i], i => (i + 1).ToString()));
    }
}
