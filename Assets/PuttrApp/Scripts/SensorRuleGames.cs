// The Vertex sensor games (puttr-app components/games/Pendulum.js, Waltz.js, Pure.js, CarbonCopy.js, SquareUp.js,
// Stretch.js, Lockdown.js, TheYips.js): rule games that score the STROKE. Every constant is the app's.
//
// Each putt is paired with its Vertex stroke before it is scored (GameBridge arms the game with SensorStrokes.Take):
// a putt with no fresh stroke does not count and the Player is told to take it again. The putt is saved with the raw
// burst as sensorData (SensorSave.js), and the game's score at the end as the app does: higher is better.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>The eight games in the rule catalog, keyed by gameType (ids 1601 to 1607 and 1489, SensorGames.cs).</summary>
    public static class SensorCatalog
    {
        public static readonly string[] Types = { "PENDULUM", "WALTZ", "PURE", "CARBONCOPY", "SQUAREUP", "STRETCH", "LOCKDOWN", "THEYIPS" };

        public static void Register()
        {
            RuleCatalog.Add("PENDULUM", _ => new PendulumGame());
            RuleCatalog.Add("WALTZ", _ => new WaltzGame());
            RuleCatalog.Add("PURE", _ => new PureGame());
            RuleCatalog.Add("CARBONCOPY", _ => new CarbonCopyGame());
            RuleCatalog.Add("SQUAREUP", _ => new SquareUpGame());
            RuleCatalog.Add("STRETCH", _ => new StretchGame());
            RuleCatalog.Add("LOCKDOWN", _ => new LockdownGame());
            RuleCatalog.Add("THEYIPS", _ => new TheYipsGame());
        }
    }

    /// <summary>A game scored on the Vertex stroke behind each putt. Played alone, as in the app.</summary>
    public abstract class SensorGame : RuleGame
    {
        /// <summary>The stroke for the putt about to be scored (Arm), then the last putt's stroke.</summary>
        public SensorStroke Pending, Stroke;
        protected readonly System.Random Dice = new System.Random();

        protected SensorGame() { SensorStrokes.Listen(); }

        public override string[] HowTo => SensorBriefings.Lines(GameType);

        /// <summary>Pair the putt that just finished with its stroke. False: no stroke, the putt does not count.</summary>
        public bool Arm(bool testing) { Pending = SensorStrokes.Take(testing); if (Pending != null && !Accepts(Pending)) Pending = null; return Pending != null; }
        /// <summary>A stroke this game can score (Waltz needs a rhythm).</summary>
        protected virtual bool Accepts(SensorStroke s) => true;
        /// <summary>The raw burst to save with the putt (none for a made-up stroke).</summary>
        public string SensorData => Pending != null && !Pending.Sim ? Pending.Raw : null;
        public static string NoStroke => Vertex.Connected ? "No stroke detected. Vertex missed that one: take the putt again." : "No stroke detected. Connect your Vertex sensor: this game scores your stroke.";

        protected sealed override Shot Take(AppPutt.Verdict v)
        {
            SensorStroke s = Pending ?? SensorStroke.Make(SensorStrokes.Dice);
            Pending = null;
            Stroke = s;
            return Judge(v, s);
        }
        protected abstract Shot Judge(AppPutt.Verdict v, SensorStroke s);

        // 3 to 11 ft on Medium; the game page's Easy deals 3 to 6 ft and Hard 8 to 11 ft (the games that deal this way
        // say so with DifficultyNotes).
        protected void DealRandom() { Feet = Dice.Next(FeetFrom(), FeetTo() + 1); Tee = Dice.Next(1, 4); }

        // ── Setup: the sensor, then the game's own choices ──

        public override VisualElement Options()
        {
            VisualElement box = Ui.Box("");
            box.Add(SensorCard());
            GameOptions(box);
            return box;
        }
        protected virtual void GameOptions(VisualElement box) { }

        static VisualElement SensorCard()
        {
            bool on = Vertex.Connected;
            string name = Vertex.Current != null && !string.IsNullOrEmpty(Vertex.Current.Name) ? Vertex.Current.Name : "Vertex";
            Label state = Ui.Text(on ? name + " connected" : "Vertex not connected", "g-name", Ui.Bold);
            state.style.color = on ? (Color)Ui.Bright : Ui.Amber;
            string words = on ? "Every putt is paired with its stroke." : Vertex.Supported
                ? "This game scores your stroke, so it needs the Vertex sensor on your putter."
                : "Test putts here play with practice strokes. On a phone, connect the Vertex on your putter.";
            VisualElement card = Ui.Box("glass", Form.Label("Vertex sensor"), state, Ui.Text(words, "muted wrap", Ui.Regular));
            if (!on && Vertex.Supported) card.Add(Ui.Button("Connect Vertex", () => App.I.Push(new VertexScreen(App.I)), "btn btn-ghost").Margin(8));
            return card.Margin(8);
        }

        protected static VisualElement DistanceTee(Func<int> feet, Action<int> setFeet, Func<int> tee, Action<int> setTee)
        {
            VisualElement box = Ui.Box("");
            box.Add(Form.Label("Distance and tee"));
            box.Add(Frame.SpotRow(feet(), tee(), (f, t) => { setFeet(f); setTee(t); }));
            return box;
        }

        protected static VisualElement Tiles(params (string value, string label)[] tiles)
        {
            VisualElement row = Ui.Box("stat-tiles");
            foreach ((string value, string label) t in tiles) row.Add(Ui.Box("glass stat-tile", Ui.Text(t.value, "stat-value", Ui.Bold), Ui.Text(t.label, "caps", Ui.Bold)));
            return row;
        }

        public override string SoundFor(Shot s) => Over ? "rule_win" : s.Title.Contains("×") || s.Title.StartsWith("PURE") || s.Title == "LEVEL CLEARED" ? "rule_bonus" : s.Good ? "rule_make" : "rule_miss";
    }

    // ── Pendulum: land the stroke's tempo on the beat ──

    public sealed partial class PendulumGame : SensorGame
    {
        public const int Putts = 15, MaxMult = 5;
        public const double Pocket = 3, Zero = 10;
        public static readonly int[] Presets = { 60, 66, 72, 78 };
        public int Target = Mathf.Clamp(PlayerPrefs.GetInt("app2.pendulum.bpm", 72), 50, 100);
        int _streak, _best, _pocket;
        readonly List<double> _tempos = new List<double>();

        protected override string OwnTitle => "Pendulum";
        public override string GameType => "PENDULUM";
        public override string Icon => "Timer";
        public override Color Accent => new Color32(0x56, 0x87, 0xFF, 0xFF);
        public int Mult => Math.Max(1, Math.Min(MaxMult, _streak));

        protected override void GameOptions(VisualElement box)
        {
            void Set(int bpm) { Target = Mathf.Clamp(bpm, 50, 100); PlayerPrefs.SetInt("app2.pendulum.bpm", Target); }
            box.Add(Form.Label("Target tempo"));
            box.Add(FreePuttScreen.Stepper(() => Target + " BPM   ·   " + SensorMath.N(SensorMath.BpmToSeconds(Target), "0.00") + " s stroke", d => Set(Target + d)));
            box.Add(Form.Pills(Presets.Select(b => b + "").ToArray(), Array.IndexOf(Presets, Target), i => Set(Presets[i])));
        }

        protected override void Begin() { _streak = _best = _pocket = 0; _tempos.Clear(); }
        public override string[] DifficultyNotes => GameLevels.RandomNotes;
        protected override void Deal() => DealRandom();
        public override bool Over => Shots.Count >= Putts;
        public override string Progress => "PUTT " + Math.Min(Shots.Count + 1, Putts) + " OF " + Putts;

        protected override Shot Judge(AppPutt.Verdict v, SensorStroke s)
        {
            double tempo = s.Tempo, diff = Math.Abs(tempo - Target);
            bool pocket = diff <= Pocket;
            double close = pocket ? 1 : diff >= Zero ? 0 : (Zero - diff) / (Zero - Pocket);
            _streak = pocket ? _streak + 1 : 0;
            _best = Math.Max(_best, _streak);
            if (pocket) _pocket++;
            _tempos.Add(tempo);
            int pts = (int)Math.Round(Feet * close * 10 * Mult * (v.Made ? 1.5 : 1), MidpointRounding.AwayFromZero);
            Score[0] += pts;
            LastClose = close;
            string stroke = SensorMath.N(SensorMath.BpmToSeconds(tempo), "0.00") + " s", beat = SensorMath.N(SensorMath.BpmToSeconds(Target), "0.00") + " s";
            string why = pocket ? "A " + stroke + " stroke, right on the " + beat + " beat"
                : tempo > Target ? "A " + stroke + " stroke against the " + beat + " beat: take a little longer from takeaway to impact"
                : "A " + stroke + " stroke against the " + beat + " beat: get from takeaway to impact a little sooner";
            string title = pocket ? "IN THE POCKET ×" + Mult : tempo > Target ? "TOO QUICK" : "TOO SLOW";
            return Call(pts, pocket, title, "+" + pts + "   ·   " + why + (v.Made ? ". Made it, +50%" : ""));
        }
        public double LastClose;

        public override VisualElement Board() => Shots.Count == 0 ? null : Card("YOUR TEMPO", Tiles(
            (SensorMath.N(SensorMath.Mean(_tempos)), "AVERAGE BPM"), (_pocket + "/" + Shots.Count, "IN THE POCKET"), ("×" + Math.Max(1, Math.Min(MaxMult, _best)), "BEST STREAK")));
    }

    // ── Waltz: back, top, strike at the ratio ──

    public sealed partial class WaltzGame : SensorGame
    {
        public const int Putts = 15, PerRound = 3, WaltzAt = 3;
        public static readonly int[] RoundBpm = { 72, 66, 78, 69, 75 };
        public const double InTime = 0.2, ZeroAt = 0.7, MakeBonus = 1.25, MinRest = 1.2;
        public static readonly double[] Presets = { 1.5, 1.8, 2.0, 2.5 };
        public double Target = Math.Round(Mathf.Clamp(PlayerPrefs.GetFloat("app2.waltz.ratio", 2f), 1f, 3f) * 10) / 10;
        int _streak, _best, _inTime;
        readonly List<double> _ratios = new List<double>();

        protected override string OwnTitle => "Waltz";
        public override string GameType => "WALTZ";
        public override string Icon => "MusicNotes";
        public override Color Accent => new Color32(0xFF, 0x8A, 0x65, 0xFF);
        public int Streak => _streak;
        /// <summary>The click speed (whole-stroke tempo) for the putt now: it changes every 3 putts.</summary>
        public int ClickBpm => RoundBpm[Math.Min(RoundBpm.Length - 1, Math.Min(Shots.Count, Putts - 1) / PerRound)];

        protected override void GameOptions(VisualElement box)
        {
            void Set(double r) { Target = Math.Round(Math.Min(3, Math.Max(1, r)) * 10) / 10; PlayerPrefs.SetFloat("app2.waltz.ratio", (float)Target); }
            box.Add(Form.Label("Target rhythm"));
            box.Add(FreePuttScreen.Stepper(() => SensorMath.Ratio(Target) + "   ·   back : through", d => Set(Target + d * 0.1)));
            box.Add(Form.Pills(Presets.Select(r => SensorMath.Ratio(r)).ToArray(), Array.IndexOf(Presets, Target), i => Set(Presets[i])));
        }

        protected override bool Accepts(SensorStroke s) => s.Rhythm > 0;
        protected override void Begin() { _streak = _best = _inTime = 0; _ratios.Clear(); }
        public override string[] DifficultyNotes => GameLevels.RandomNotes;
        protected override void Deal() => DealRandom();
        public override bool Over => Shots.Count >= Putts;
        public override string Progress => "PUTT " + Math.Min(Shots.Count + 1, Putts) + " OF " + Putts;

        protected override Shot Judge(AppPutt.Verdict v, SensorStroke s)
        {
            double r = s.Rhythm, diff = Math.Abs(r - Target);
            bool inTime = diff <= InTime;
            double close = inTime ? 1 : diff >= ZeroAt ? 0 : (ZeroAt - diff) / (ZeroAt - InTime);
            _streak = inTime ? _streak + 1 : 0;
            _best = Math.Max(_best, _streak);
            if (inTime) _inTime++;
            _ratios.Add(r);
            bool waltz = _streak >= WaltzAt;
            int pts = (int)Math.Round(100 * close * (waltz ? 2 : 1) * (v.Made ? MakeBonus : 1), MidpointRounding.AwayFromZero);
            Score[0] += pts;
            LastClose = close;
            string split = "Back " + SensorMath.N(s.Back, "0.00") + " s, through " + SensorMath.N(s.Forward, "0.00") + " s";
            string why = inTime ? split + ", right on your " + SensorMath.Ratio(Target)
                : r < Target ? split + ". Give the backstroke until the second click"
                : split + ". Reach the top on the second click, then let the putter go";
            string title = inTime ? (waltz ? "WALTZ ×2" : "IN TIME") : r < Target ? "QUICK BACK" : "SLOW BACK";
            return Call(pts, inTime, title, "+" + pts + "   ·   " + why + (v.Made ? ". Made it, +25%" : ""));
        }
        public double LastClose;

        public override VisualElement Board() => Shots.Count == 0 ? null : Card("YOUR RHYTHM", Tiles(
            (SensorMath.Ratio(SensorMath.Mean(_ratios)), "AVERAGE"), (_inTime + "/" + Shots.Count, "IN TIME"), (_best + "", "BEST RUN")));
    }

    // ── Pure: the stroke is the score, the hole a multiplier ──

    public sealed partial class PureGame : SensorGame
    {
        public const int Putts = 15, StrikeMin = 75;
        int _strikes, _made;
        readonly List<int> _purity = new List<int>();
        public (int face, int rhythm, int lie, int purity) Q;
        public bool LastStrike;

        protected override string OwnTitle => "Pure";
        public override string GameType => "PURE";
        public override string Icon => "Diamond";
        public override Color Accent => new Color32(0xC7, 0xCF, 0xDA, 0xFF);

        protected override void Begin() { _strikes = _made = 0; _purity.Clear(); Q = default; LastStrike = false; }
        public override string[] DifficultyNotes => GameLevels.RandomNotes;
        protected override void Deal() => DealRandom();
        public override bool Over => Shots.Count >= Putts;
        public override string Progress => "PUTT " + Math.Min(Shots.Count + 1, Putts) + " OF " + Putts;

        protected override Shot Judge(AppPutt.Verdict v, SensorStroke s)
        {
            Q = SensorMath.Quality(s);
            bool strike = Q.face >= StrikeMin && Q.rhythm >= StrikeMin && Q.lie >= StrikeMin;
            LastStrike = strike;
            int pts = Q.purity * (v.Made ? 2 : 1);
            Score[0] += pts;
            _purity.Add(Q.purity);
            if (strike) _strikes++;
            if (v.Made) _made++;
            string title = strike ? (v.Made ? "PURE STRIKE, MADE ×2" : "PURE STRIKE") : v.Made ? "MADE ×2" : "MISS";
            string weakest = Q.face <= Q.rhythm && Q.face <= Q.lie ? "the face" : Q.rhythm <= Q.lie ? "the rhythm" : "the lie";
            // The purity is the scene's readout: only a make's doubled total is new here.
            string sub = (strike ? "All three in the green" : "Most points lost on " + weakest) + (v.Made ? ". Made it: doubled to +" + pts : strike ? ". The stroke still pays" : "");
            return Call(pts, strike || v.Made, title, sub);
        }

        public override VisualElement Board() => Shots.Count == 0 ? null : Card("YOUR STROKES", Tiles(
            (Math.Round(_purity.Average()) + "", "AVERAGE PURITY"), (_strikes + "", "PURE STRIKES"), (_made + "/" + Shots.Count, "MADE")));
    }

    // ── Carbon Copy: putt 1 is the signature, clone it ──

    public sealed partial class CarbonCopyGame : SensorGame
    {
        public const int Copies = 14, CopyMin = 90, PerRound = 3;
        public static readonly double[] Tol = { 1.0, 0.85, 0.7, 0.55, 0.45 };
        /// <summary>The four metrics: key, name, base tolerance (length scored in mm, shown in inches).</summary>
        public static readonly (string key, string name, string label, double std)[] Metrics =
        {
            ("tempo", "tempo", "TEMPO", 6), ("backStrokeLength", "length", "LENGTH", 60), ("rhythm", "rhythm", "RHYTHM", 0.35), ("faceChange", "face change", "FACE", 1.2),
        };
        int _feet = 6, _tee = 2, _copies, _carbon;
        public double[] Signature;
        public int[] Match = new int[4];
        public int LastMatch;
        readonly List<int> _matches = new List<int>();

        protected override string OwnTitle => "Carbon Copy";
        public override string GameType => "CARBONCOPY";
        public override string Icon => "Copy";
        public override Color Accent => new Color32(0x4C, 0xC1, 0x62, 0xFF);
        public static double TolFor(int copyIndex) => Tol[Math.Min(Tol.Length - 1, copyIndex / PerRound)];
        public int CopiesDone => _copies;

        protected override void GameOptions(VisualElement box) => box.Add(DistanceTee(() => _feet, f => _feet = f, () => _tee, t => _tee = t));
        protected override void Begin() { _copies = _carbon = 0; Signature = null; Match = new int[4]; LastMatch = 0; _matches.Clear(); }
        protected override void Deal() { Feet = _feet; Tee = _tee; }
        public override bool Over => _copies >= Copies;
        public override string Progress => Signature == null ? "SIGNATURE STROKE" : "COPY " + Math.Min(_copies + 1, Copies) + " OF " + Copies;

        protected override Shot Judge(AppPutt.Verdict v, SensorStroke s)
        {
            if (Signature == null)
            {
                Signature = Metrics.Select(m => s[m.key]).ToArray();
                return Call(0, true, "SIGNATURE RECORDED", "Now clone that stroke fourteen times.");
            }
            double tol = TolFor(_copies);
            for (int i = 0; i < Metrics.Length; i++) Match[i] = SensorMath.Bell(s[Metrics[i].key], Signature[i], Metrics[i].std * tol);
            int match = (int)Math.Round(Match.Average(), MidpointRounding.AwayFromZero);
            bool copy = match >= CopyMin;
            int pts = match * (copy ? 2 : 1);
            _copies++;
            if (copy) _carbon++;
            LastMatch = match;
            _matches.Add(match);
            Score[0] += pts;
            int worst = Array.IndexOf(Match, Match.Min());
            string sub = (copy ? "A clone, doubled to +" + pts : char.ToUpperInvariant(Metrics[worst].name[0]) + Metrics[worst].name.Substring(1) + " drifted most") + (v.Made ? ". Made it too" : "");
            return Call(pts, copy, copy ? "CARBON COPY ×2" : match + "% MATCH", sub);
        }

        public override VisualElement Board() => _copies == 0 ? null : Card("YOUR COPIES", Tiles(
            (Math.Round(_matches.Average()) + "%", "AVERAGE MATCH"), (_carbon + "", "CARBON COPIES"), (Math.Round(TolFor(Math.Min(_copies, Copies - 1)) * 100) + "%", "TOLERANCE NOW")));
    }

    // ── Square Up: keep the face inside a shrinking gate ──

    public sealed partial class SquareUpGame : SensorGame
    {
        public const int Calibrate = 3, PerLevel = 3, StartBalls = 3;
        public const double StartGate = 2.0, Step = 0.25, Floor = 0.5;
        int _feet = 6, _tee = 2;
        public bool Zero;                       // "True zero"; else "My normal" (the first 3 putts find the center)
        public int Level = 1, InLevel, Balls = StartBalls, Survived;
        public double? Center;
        public readonly List<double> Calib = new List<double>();
        public double LastGate, LastOff;
        public bool LastInside, Cleared;

        protected override string OwnTitle => "Square Up";
        public override string GameType => "SQUAREUP";
        public override string Icon => "Square";
        public override Color Accent => new Color32(0xF5, 0x9E, 0x0B, 0xFF);
        public override string Words => "SURVIVED";
        public static double GateFor(int level) => Math.Max(Floor, StartGate - Step * (level - 1));
        public double Gate => GateFor(Level);

        protected override void GameOptions(VisualElement box)
        {
            box.Add(DistanceTee(() => _feet, f => _feet = f, () => _tee, t => _tee = t));
            box.Add(Form.Label("Gate center"));
            box.Add(Form.Pills(new[] { "My normal", "True zero" }, Zero ? 1 : 0, i => Zero = i == 1));
            box.Add(Ui.Text("My normal: your first 3 putts find your usual face change. True zero: the gate is centered on 0°.", "muted wrap", Ui.Regular));
        }
        protected override void Begin()
        {
            Level = 1; InLevel = 0; Balls = StartBalls; Survived = 0; Calib.Clear(); Center = Zero ? 0 : (double?)null;
            LastGate = LastOff = 0; LastInside = Cleared = false;
        }
        protected override void Deal() { Feet = _feet; Tee = _tee; }
        public override bool Over => Balls <= 0;
        public override string Progress => Center == null ? "CALIBRATING " + (Calib.Count + 1) + " OF " + Calibrate : "LEVEL " + Level;
        public override string ScoreText(int p) => Survived.ToString();
        public override int Saved => Survived;

        protected override Shot Judge(AppPutt.Verdict v, SensorStroke s)
        {
            double face = s.Face;
            Cleared = false;
            if (Center == null)
            {
                Calib.Add(face);
                if (Calib.Count < Calibrate) return Call(0, true, "CALIBRATING " + Calib.Count + "/" + Calibrate, "Putt normally, no balls at risk yet.");
                List<double> sorted = Calib.OrderBy(x => x).ToList();
                Center = Math.Round(sorted[sorted.Count / 2], 1);
                return Call(0, true, "GATE SET", "Your normal face change is " + SensorMath.Deg(Center.Value) + ". The gate is centered there. Now repeat it.");
            }
            double gate = Gate, off = face - Center.Value;
            bool inside = Math.Abs(off) <= gate;
            LastGate = gate; LastOff = off; LastInside = inside;
            string vs = Center.Value == 0 ? "" : " (" + SensorMath.Deg(off) + " from your normal)";
            if (inside)
            {
                Survived++;
                Score[0] = Survived;
                if (++InLevel >= PerLevel)
                {
                    Level++; InLevel = 0; Cleared = true;
                    return Call(1, true, "LEVEL CLEARED", "Three survived: the gate narrows");
                }
                return Call(1, true, "SQUARE ENOUGH", "Inside the gate" + vs);
            }
            Balls--;
            return Call(0, false, off > 0 ? "TOO CLOSED" : "TOO OPEN", "Outside the gate" + vs + ". " + (Balls > 0 ? Balls + (Balls == 1 ? " ball" : " balls") + " left." : "Out of balls."));
        }

        public override VisualElement Board() => Card("THE GATE", Tiles(
            (Center == null ? "Finding" : SensorMath.Deg(Center.Value), Zero ? "TRUE ZERO" : "YOUR NORMAL"), (InLevel + "/" + PerLevel, "TO CLEAR LEVEL"), (Survived + "", "PUTTS SURVIVED")));
    }

    // ── Stretch: longer putt, longer stroke, same tempo ──

    public sealed partial class StretchGame : SensorGame
    {
        public const int Putts = 12, LockAfter = 3;
        public const double TempoBand = 5, LengthPts = 10, TempoPts = 10, PaceMax = 10, PaceZero = 12;
        public const double BandBase = 8, BandPerFt = 0.5, BandHalf = 2, RulerMin = 4, RulerMax = 20;
        int _tee = 2, _elastic;
        public double? Lock;
        readonly List<double> _tempos = new List<double>();
        public double LastLength, LastLo, LastHi, LastRollout = double.NaN;
        public int LastTempoOk = -1, LastPace;
        public bool LastLengthOk, LastElastic;

        protected override string OwnTitle => "Stretch";
        public override string GameType => "STRETCH";
        public override string Icon => "Ruler";
        public override Color Accent => new Color32(0x00, 0xCC, 0xFF, 0xFF);
        public static (double lo, double hi, double center) BandFor(int feet) { double c = BandBase + BandPerFt * feet; return (c - BandHalf, c + BandHalf, c); }

        protected override void GameOptions(VisualElement box)
        {
            box.Add(Form.Label("Tee"));
            box.Add(Frame.TeePicker(_tee, t => _tee = t));
        }
        protected override void Begin() { _elastic = 0; Lock = null; _tempos.Clear(); _lengths.Clear(); LastRollout = double.NaN; LastTempoOk = -1; }
        // Short (3 to 6 ft), then long (8 to 11 ft), all on the tee picked.
        protected override void Deal() { Feet = Shots.Count % 2 == 0 ? Dice.Next(3, 7) : Dice.Next(8, 12); Tee = _tee; }
        public override bool Over => Shots.Count >= Putts;
        public override string Progress => "PUTT " + Math.Min(Shots.Count + 1, Putts) + " OF " + Putts + "   ·   " + (Shots.Count % 2 == 0 ? "SHORT" : "LONG");

        protected override Shot Judge(AppPutt.Verdict v, SensorStroke s)
        {
            (double lo, double hi, double _) = BandFor(Feet);
            double length = s.LengthIn, tempo = s.Tempo;
            bool lengthOk = length >= lo && length <= hi;
            _tempos.Add(tempo);
            if (Lock == null && _tempos.Count >= LockAfter) Lock = Math.Round(_tempos.Take(LockAfter).Average(), MidpointRounding.AwayFromZero);
            int tempoOk = Lock == null ? -1 : Math.Abs(tempo - Lock.Value) <= TempoBand ? 1 : 0;
            int pace = v.Rollout == null ? (int)(PaceMax / 2) : (int)Math.Round(PaceMax * Math.Max(0, 1 - Math.Abs(v.Rollout.Value) / PaceZero), MidpointRounding.AwayFromZero);
            bool elastic = lengthOk && tempoOk == 1 && v.Rollout != null && Math.Abs(v.Rollout.Value) <= PaceZero;
            double pts = (lengthOk ? LengthPts : 0) + (tempoOk == 1 ? TempoPts : 0) + pace;
            if (elastic) { pts *= 2; _elastic++; }
            Score[0] += pts;
            _lengths.Add((float)length);
            LastLength = length; LastLo = lo; LastHi = hi; LastTempoOk = tempoOk; LastPace = pace; LastLengthOk = lengthOk; LastElastic = elastic;
            LastRollout = v.Rollout ?? double.NaN;
            var bits = new List<string>();
            bits.Add(lengthOk ? "Backstroke in the band" : "Backstroke " + (length < lo ? "too short" : "too long") + " for " + Feet + " ft");
            if (tempoOk >= 0) bits.Add(tempoOk == 1 ? "tempo held" : "tempo drifted from your " + Lock + " BPM");
            bits.Add("pace " + pace + " of 10" + (v.Rollout != null ? " (" + AppPutt.FmtRollout(v.Rollout) + ")" : ""));
            return Call(pts, lengthOk, elastic ? "ELASTIC ×2" : lengthOk ? "IN THE BAND" : "OUT OF BAND", "+" + pts + "   ·   " + string.Join(", ", bits));
        }

        public override VisualElement Board() => Card("TEMPO LOCK", Tiles(
            (Lock == null ? "Putt " + Math.Min(Shots.Count + 1, LockAfter) + "/" + LockAfter : Lock + " ±" + TempoBand, "LOCKED BPM"), (_elastic + "", "ELASTIC")));
    }

    // ── Lockdown: six spokes inside a tightening band ──

    public sealed partial class LockdownGame : SensorGame
    {
        public const int Calibrate = 3, SpokePts = 5, EndAfter = 3;
        public const double BandStart = 1.0, Tighten = 0.95, Relax = 1.05, BandMin = 0.5, BandMax = 1.25;
        public static readonly (string key, string label, double dev)[] Spokes =
        {
            ("tempo", "TEMPO", 4), ("clubHeadSpeed", "SPEED", 0.4), ("backStrokeTime", "BACK T", 0.06),
            ("forwardStrokeTime", "FWD T", 0.04), ("backStrokeLength", "LENGTH", 50), ("backStrokeRotation", "ROT", 1.2),
        };
        int _feet = 8, _tee = 2;
        public readonly List<SensorStroke> Calib = new List<SensorStroke>();
        public double[] Centers;
        public double Band = BandStart, Tightest = BandStart, BandBefore = BandStart;
        public int Combo, BreakRun, Lockdowns;
        public double[] Ratios;
        public bool LastLock;

        protected override string OwnTitle => "Lockdown";
        public override string GameType => "LOCKDOWN";
        public override string Icon => "Hexagon";
        public override Color Accent => new Color32(0xB5, 0x7B, 0xFF, 0xFF);

        protected override void GameOptions(VisualElement box) => box.Add(DistanceTee(() => _feet, f => _feet = f, () => _tee, t => _tee = t));
        protected override void Begin() { Calib.Clear(); Centers = null; Band = Tightest = BandBefore = BandStart; Combo = BreakRun = Lockdowns = 0; Ratios = null; LastLock = false; }
        protected override void Deal() { Feet = _feet; Tee = _tee; }
        public override bool Over => BreakRun >= EndAfter;
        public override string Progress => Centers == null ? "CALIBRATING " + (Calib.Count + 1) + " OF " + Calibrate : "STROKE " + (Shots.Count + 1);

        protected override Shot Judge(AppPutt.Verdict v, SensorStroke s)
        {
            if (Centers == null)
            {
                Calib.Add(s);
                if (Calib.Count < Calibrate) return Call(0, true, "CALIBRATING " + Calib.Count + "/" + Calibrate, "Putt naturally. Your band comes from these strokes.");
                Centers = Spokes.Select(k => Calib.Average(c => c[k.key])).ToArray();
                return Call(0, true, "BAND SET", "Your ring is drawn. Now stay inside it.");
            }
            BandBefore = Band;
            Ratios = Spokes.Select((k, i) => { double allow = k.dev * Band; return allow > 0 ? Math.Abs(s[k.key] - Centers[i]) / allow : 0; }).ToArray();
            int inCount = Ratios.Count(r => r <= 1);
            bool lockdown = inCount == Spokes.Length;
            LastLock = lockdown;
            if (lockdown) { Combo++; BreakRun = 0; Band = Math.Max(BandMin, Band * Tighten); Lockdowns++; }
            else { Combo = 0; BreakRun++; Band = Math.Min(BandMax, Band * Relax); }
            Tightest = Math.Min(Tightest, Band);
            int pts = inCount * SpokePts * Math.Max(1, Combo);
            Score[0] += pts;
            if (lockdown) return Call(pts, true, "LOCKDOWN ×" + Combo, "+" + pts + "   ·   All six inside: the band tightens");
            string broke = string.Join(", ", Spokes.Where((k, i) => Ratios[i] > 1).Select(k => k.label.ToLowerInvariant()));
            return Call(pts, false, inCount + "/" + Spokes.Length + " SPOKES", "+" + pts + "   ·   " + char.ToUpperInvariant(broke[0]) + broke.Substring(1) + " broke the ring. The band relaxes");
        }

        public override VisualElement Board() => Centers == null ? null : Card("THE RING", Tiles(
            (Lockdowns + "", "LOCKDOWNS"), (Math.Round(Tightest * 100) + "%", "TIGHTEST BAND"), (BreakRun + "/" + EndAfter, "BREAKS IN A ROW")));
    }

    // ── The Yips: a boss battle against the twitch ──

    public sealed partial class TheYipsGame : SensorGame
    {
        public const int BossMax = 250, PlayerMax = 100, Stage3Purity = 50, ComboBonus = 2;
        public static readonly double[] StrikeFace = { 2.0, 1.5, 1.2 };
        public const double RhythmLo = 1.4, RhythmHi = 3.0;
        public int BossHp = BossMax, PlayerHp = PlayerMax, Combo, Damage, LastDmg, LastTaken, LastStage = 1, LastPurity;
        public bool LastCrit, LastTwitch, LastCollapse;

        protected override string OwnTitle => "The Yips";
        public override string GameType => "THEYIPS";
        public override string Icon => "Skull";
        public override Color Accent => new Color32(0xFF, 0x33, 0x66, 0xFF);
        public override string Words => "DAMAGE";
        public static int StageFor(int hp) => hp > BossMax * 0.66 ? 1 : hp > BossMax * 0.33 ? 2 : 3;
        public int Stage => StageFor(BossHp);
        public bool Won => BossHp <= 0;

        protected override void Begin() { BossHp = BossMax; PlayerHp = PlayerMax; Combo = Damage = LastDmg = LastTaken = LastPurity = 0; LastStage = 1; LastCrit = LastTwitch = LastCollapse = false; }
        public override string[] DifficultyNotes => GameLevels.RandomNotes;
        protected override void Deal() => DealRandom();
        public override bool Over => BossHp <= 0 || PlayerHp <= 0;
        public override string Progress => "STAGE " + Stage;
        public override string ScoreText(int p) => Damage.ToString();
        /// <summary>A win saves all the boss's HP plus the HP kept (a cleaner win ranks higher); a loss, the damage done.</summary>
        public override int Saved => BossHp <= 0 ? BossMax + PlayerHp : BossMax - BossHp;
        public override string Outcome() => Won ? "You beat the Yips" : PlayerHp <= 0 ? "The Yips got you" : base.Outcome();

        protected override Shot Judge(AppPutt.Verdict v, SensorStroke s)
        {
            var q = SensorMath.Quality(s);
            int stage = Stage;
            bool twitch = Math.Abs(s.Face) > StrikeFace[stage - 1];
            bool collapse = s.Rhythm < RhythmLo || s.Rhythm > RhythmHi;
            int taken = 0;
            if (twitch) taken += Math.Min(20, (int)Math.Round(4 + Math.Abs(s.Face) * 3, MidpointRounding.AwayFromZero));
            if (collapse) taken += 8;
            int dmg = (int)Math.Round(q.purity / 100.0 * Feet * 2, MidpointRounding.AwayFromZero);
            if (stage == 3 && q.purity < Stage3Purity) dmg = 0;
            bool crit = v.Made && dmg > 0;
            if (crit) dmg *= 2;
            if (dmg > 0) dmg += ComboBonus * Combo;
            Combo = dmg > 0 && taken == 0 ? Combo + 1 : 0;
            BossHp = Math.Max(0, BossHp - dmg);
            PlayerHp = Math.Max(0, PlayerHp - taken);
            Damage += dmg;
            Score[0] = Damage;
            LastDmg = dmg; LastTaken = taken; LastStage = stage; LastPurity = q.purity; LastCrit = crit; LastTwitch = twitch; LastCollapse = collapse;
            string title = dmg > 0 && taken > 0 ? "TRADED BLOWS" : dmg > 0 ? (crit ? "CRIT ×2, " + dmg + " DAMAGE" : dmg + " DAMAGE") : taken > 0 ? "THE YIPS STRIKE" : "GLANCING BLOW";
            var bits = new List<string>();
            if (taken > 0) bits.Add("You take " + taken + (twitch ? ", face " + SensorMath.Deg(s.Face) : "") + (collapse ? ", rhythm collapsed" : ""));
            if (dmg == 0 && taken == 0) bits.Add(stage == 3 ? "Stage 3 only respects smooth strokes" : "Weak stroke, no damage");
            if (Combo > 1) bits.Add("combo ×" + Combo);
            if (BossHp <= 0) bits.Add("The Yips are beaten");
            else if (PlayerHp <= 0) bits.Add("Out of HP");
            return Call(dmg, dmg > 0, title, string.Join(". ", bits));
        }

        public override VisualElement Board() => Shots.Count == 0 ? null : Card("STROKE CHECK", Tiles(
            (LastPurity + "", "PURITY"), ("±" + SensorMath.N(StrikeFace[Stage - 1]) + "°", "STRIKE WINDOW"), (SensorMath.Ratio(Stroke != null ? Stroke.Rhythm : 0), "RHYTHM")));
    }
}
