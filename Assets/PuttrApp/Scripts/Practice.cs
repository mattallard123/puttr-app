// A practice session's numbers, kept apart from the screen so they can be tested: what was
// putted, the streaks, where the misses went, how each distance is going, and whether the goal
// has been met. The same figures puttr-app's Free putt (games/Open.js) shows.
using System;
using System.Collections.Generic;
using System.Linq;
using Puttr;

namespace PuttrApp
{
    public enum PracticeGoal { Free, Streak, Total }

    /// <summary>Open.js SESSION FOCUS: what the session's read leads with. Overall "All stats", Speed "Pace control", Entry "Accuracy".</summary>
    public enum PracticeFocus { Overall, Speed, Entry }

    public sealed class PracticePutt
    {
        public int Feet, Tee;
        public bool Made, Reached = true;
        public MissSide Side;
        public double? Rollout;      // inches past (+) or short (-) of the hole's centre, the app's pace measure
        public string Entry, Grade;  // "Center cut", "B+"
        public double Score;         // the app's 100-point putt score
        public UnityEngine.Vector2[] PathInches;
        // puttr-app-2: what Open.js's session read and result card need.
        public int[][] Path;         // the ball's path in camera pixels, as the console sends it
        public double[] Hole;        // the hole: x, y, diameter in camera pixels
        public double OffLine;       // a make's entry against the tee's line, degrees (+ right); 0 for a miss
        public string EntryWords;    // a make's entry as the app names it: "CENTER CUT", "LEFT SIDE", "BACK DOOR"
        public string MissDir;       // a miss's side as Open.js reads it (getMissResult): "LEFT" or "RIGHT"
        public double? Arrival;      // signed speed reaching the hole, inches per camera frame (arrivalSpeed)

        /// <summary>Open.js / PuttCard's break label: "Left to right", "Straight", "Right to left".</summary>
        public static string BreakLabel(int tee) => tee == 1 ? "Left to right" : tee == 3 ? "Right to left" : "Straight";

        /// <summary>PuttCard's result: a make's entry ("Left center"), else "Missed left".</summary>
        public string ResultText => Made ? Tidy(EntryWords) : string.IsNullOrEmpty(MissDir) ? "Miss" : "Missed " + MissDir.ToLowerInvariant();

        /// <summary>What happened, in a few words.</summary>
        public string Headline =>
            Made ? "Made" + (string.IsNullOrEmpty(Entry) ? "" : "   \u00B7   " + Entry)
            : !Reached ? "Short"
            : Side == MissSide.Left ? "Missed left"
            : Side == MissSide.Right ? "Missed right"
            : "Missed";

        /// <summary>The pace line: "4.1 in past", "dead center", as the app writes it.</summary>
        public string Pace => AppPutt.FmtRollout(Rollout);

        public static PracticePutt From(AppPutt.Verdict v, int feet, int tee) => new PracticePutt
        {
            Feet = feet, Tee = tee, Made = v.Made,
            Reached = v.Made || v.Rollout == null || v.Rollout.Value >= 0,
            Side = v.MissSide == "LEFT" ? MissSide.Left : v.MissSide == "RIGHT" ? MissSide.Right : MissSide.None,
            Rollout = v.Rollout, Grade = v.Grade, Score = v.Score,
            Entry = v.Made ? Tidy(v.Description) : null,
            OffLine = v.Made ? v.OffLine : 0, EntryWords = v.Made ? v.Description : null,
        };

        /// <summary>The same, with the ball's path kept for the result card and the session read.</summary>
        public static PracticePutt From(AppPutt.Verdict v, int feet, int tee, PuttResult raw)
        {
            PracticePutt p = From(v, feet, tee);
            if (raw == null || raw.Path == null || raw.Path.Count == 0) return p;
            p.Path = AppPutt.Path(raw.Path);
            p.Hole = AppPutt.Hole(raw.Hole);
            p.PathInches = raw.PathInches;
            p.Arrival = AppPutt.ArrivalSpeed(AppPutt.AnalyzeRollout(p.Path, p.Hole));
            if (!p.Made) p.MissDir = AppPutt.MissResult(p.Path, p.Hole);
            return p;
        }

        // The app says "CENTER CUT"; its screens say "Center cut".
        static string Tidy(string words) => string.IsNullOrEmpty(words) ? words : char.ToUpperInvariant(words[0]) + words.Substring(1).ToLowerInvariant();
    }

    public sealed class PracticeSession
    {
        public const int MinFeet = 3, MaxFeet = 11; // the mat: never deal outside it

        public readonly List<PracticePutt> Putts = new List<PracticePutt>(); // newest last
        /// <summary>The app's verdict on the last putt, for the putt read.</summary>
        public AppPutt.Verdict LastVerdict;
        public PracticeGoal Goal = PracticeGoal.Free;
        public int Target = 10;
        /// <summary>What the session read leads with (Open.js sessionFocus). Starts as the Player last chose it.</summary>
        public PracticeFocus Focus = LastFocus;

        // The focus is remembered on this device, so the next Practice starts the same way.
        const string FocusPref = "app2.practice.focus";
        public static PracticeFocus LastFocus
        {
            get { try { return (PracticeFocus)Math.Max(0, Math.Min(2, UnityEngine.PlayerPrefs.GetInt(FocusPref, 0))); } catch (Exception) { return PracticeFocus.Overall; } }
            set { try { UnityEngine.PlayerPrefs.SetInt(FocusPref, (int)value); } catch (Exception) { /* not remembered: Overall next time */ } }
        }

        /// <summary>Open.js goalTargets: 5, 10, 15 or 20 in a row; 25, 50 or 100 putts.</summary>
        public static int[] TargetsFor(PracticeGoal goal) => goal == PracticeGoal.Streak ? new[] { 5, 10, 15, 20 } : new[] { 25, 50, 100 };

        /// <summary>Where a goal starts when it is picked (Open.js: streak 10, total 50).</summary>
        public static int DefaultTarget(PracticeGoal goal) => goal == PracticeGoal.Streak ? 10 : 50;
        public bool RandomTees;
        public int RangeMin = MinFeet, RangeMax = MaxFeet;
        public int Feet = 6, Tee = 2;

        readonly Random _dice;
        public PracticeSession(int seed = 0) { _dice = seed == 0 ? new Random() : new Random(seed); }

        public int Total => Putts.Count;
        public int Makes { get { int n = 0; foreach (PracticePutt p in Putts) if (p.Made) n++; return n; } }
        public int MakePercent => Total == 0 ? 0 : (int)Math.Round(100.0 * Makes / Total);

        /// <summary>Makes in a row, ending with the last putt.</summary>
        public int Streak
        {
            get { int n = 0; for (int i = Putts.Count - 1; i >= 0 && Putts[i].Made; i--) n++; return n; }
        }

        public int BestStreak
        {
            get { int best = 0, run = 0; foreach (PracticePutt p in Putts) { run = p.Made ? run + 1 : 0; if (run > best) best = run; } return best; }
        }

        /// <summary>Average rollout over the putts that had one (inches past the hole's centre). Null if none.</summary>
        public double? AverageRollout
        {
            get { double sum = 0; int n = 0; foreach (PracticePutt p in Putts) if (p.Rollout != null) { sum += p.Rollout.Value; n++; } return n == 0 ? (double?)null : sum / n; }
        }

        /// <summary>Where the misses went: left, short, right.</summary>
        public void Misses(out int left, out int shortOnes, out int right)
        {
            left = shortOnes = right = 0;
            foreach (PracticePutt p in Putts)
            {
                if (p.Made) continue;
                if (!p.Reached) shortOnes++;
                else if (p.Side == MissSide.Left) left++;
                else if (p.Side == MissSide.Right) right++;
            }
        }

        /// <summary>Makes and attempts at each distance putted, nearest first.</summary>
        public List<(int feet, int makes, int total)> ByDistance()
        {
            var rows = new List<(int, int, int)>();
            for (int feet = MinFeet; feet <= MaxFeet; feet++)
            {
                int makes = 0, total = 0;
                foreach (PracticePutt p in Putts) if (p.Feet == feet) { total++; if (p.Made) makes++; }
                if (total > 0) rows.Add((feet, makes, total));
            }
            return rows;
        }

        /// <summary>How far toward the goal, 0 to 1. Free practice has no goal: 0.</summary>
        public float Progress =>
            Goal == PracticeGoal.Total ? Math.Min(1f, Total / (float)Math.Max(1, Target))
            : Goal == PracticeGoal.Streak ? Math.Min(1f, Math.Max(Streak, GoalMet ? Target : 0) / (float)Math.Max(1, Target))
            : 0f;

        bool _streakReached;
        public bool GoalMet => Goal == PracticeGoal.Total ? Total >= Target : Goal == PracticeGoal.Streak && _streakReached;

        /// <summary>Adds a putt. True if this is the putt that met the goal.</summary>
        public bool Add(PracticePutt putt)
        {
            bool before = GoalMet;
            Putts.Add(putt);
            if (Goal == PracticeGoal.Streak && Streak >= Target) _streakReached = true;
            if (RandomTees) Deal();
            return !before && GoalMet;
        }

        /// <summary>Random tees: a distance inside the range and a tee, never the same spot twice running.</summary>
        public void Deal()
        {
            int min = Math.Max(MinFeet, Math.Min(RangeMin, RangeMax)), max = Math.Min(MaxFeet, Math.Max(RangeMin, RangeMax));
            int feet, tee, tries = 0;
            do
            {
                feet = _dice.Next(min, max + 1);
                tee = _dice.Next(1, 4);
            } while (feet == Feet && tee == Tee && ++tries < 20);
            Feet = feet;
            Tee = tee;
        }

        public static string TeeName(int tee) => tee == 1 ? "Left" : tee == 3 ? "Right" : "Center";

        /// <summary>Open.js sessionStats: the figures the session read shows. Null before the first putt.</summary>
        public PracticeStats Stats() => PracticeStats.Of(Putts);
    }

    /// <summary>Open.js sessionStats, figure for figure (the putts newest last here, newest first there).</summary>
    public sealed class PracticeStats
    {
        /// <summary>PGA Tour make percentages from 3 to 11 ft (Open.js PGA_BENCHMARKS).</summary>
        public static readonly int[] PgaBenchmarks = { 99, 95, 90, 85, 80, 75, 70, 65, 61 };

        public int Total, Makes, Misses, MakePercent;
        public int AvgScore, BestScore;
        public string AvgGrade, BestGrade;
        public int CurrentStreak, LongestMakeStreak;
        public bool StreakOfMakes;
        public double? AvgSpeed;
        public double MinSpeed, MaxSpeed, SpeedSpread;   // spread: inches per second
        public string SpeedConsistency = "";             // "Very consistent", "Consistent", "Inconsistent"
        public string Tendency = "Neutral";              // "Pulling left", "Pushing right", "Neutral"
        public int MissLeft, MissRight, TotalMisses;
        public int CenterCuts, LeftEntries, RightEntries, Backdoors;
        public int AvgEntryAngle;
        public readonly List<(int feet, int makes, int total, int pct, int? pga)> Distances = new List<(int, int, int, int, int?)>();
        public readonly List<(int tee, string label, int makes, int total, int pct)> Breaks = new List<(int, string, int, int, int)>();

        public static PracticeStats Of(IReadOnlyList<PracticePutt> putts)
        {
            if (putts == null || putts.Count == 0) return null;
            var s = new PracticeStats { Total = putts.Count };
            // The current run: makes or misses, ending with the last putt.
            int last = putts.Count - 1;
            s.StreakOfMakes = putts[last].Made;
            for (int i = last; i >= 0 && putts[i].Made == s.StreakOfMakes; i--) s.CurrentStreak++;

            double totalScore = 0, bestScore = 0;
            s.BestGrade = "F";
            int run = 0;
            var speeds = new List<double>();
            var arrivals = new List<double>();
            var angles = new List<double>();
            var byFeet = new SortedDictionary<int, int[]>();
            var byTee = new Dictionary<int, int[]> { { 1, new int[2] }, { 2, new int[2] }, { 3, new int[2] } };
            foreach (PracticePutt p in putts)
            {
                if (p.Made) { s.Makes++; run++; s.LongestMakeStreak = Math.Max(s.LongestMakeStreak, run); } else { s.Misses++; run = 0; }
                totalScore += p.Score;
                if (p.Score > bestScore) { bestScore = p.Score; s.BestGrade = AppPutt.Grade(p.Score); }
                if (p.Rollout != null) speeds.Add(p.Rollout.Value);
                if (p.Arrival != null) arrivals.Add(p.Arrival.Value);
                if (p.Made)
                {
                    string words = p.EntryWords ?? AppPutt.EntryDescription(p.OffLine);
                    if (words.Contains("CENTER CUT")) s.CenterCuts++;
                    else if (words.Contains("LEFT")) s.LeftEntries++;
                    else if (words.Contains("RIGHT")) s.RightEntries++;
                    if (words.Contains("BACK DOOR") || words.Contains("BACKDOOR")) s.Backdoors++;
                    angles.Add(Math.Abs(p.OffLine));
                }
                else if (p.MissDir == "LEFT") s.MissLeft++;
                else if (p.MissDir == "RIGHT") s.MissRight++;
                if (!byFeet.TryGetValue(p.Feet, out int[] d)) byFeet[p.Feet] = d = new int[2];
                d[1]++; if (p.Made) d[0]++;
                if (byTee.TryGetValue(p.Tee, out int[] b)) { b[1]++; if (p.Made) b[0]++; }
            }
            s.MakePercent = (int)AppPutt.JsRound(100.0 * s.Makes / s.Total);
            s.AvgScore = (int)AppPutt.JsRound(totalScore / s.Total);
            s.AvgGrade = AppPutt.Grade(s.AvgScore);
            s.BestScore = (int)AppPutt.JsRound(bestScore);

            if (speeds.Count > 0)
            {
                double sum = 0; foreach (double v in speeds) sum += v;
                s.AvgSpeed = sum / speeds.Count;
                s.MinSpeed = speeds.Min(); s.MaxSpeed = speeds.Max();
                string label = PuttAssessment.Consistency(arrivals).words;
                s.SpeedConsistency = label == "very consistent" ? "Very consistent" : label == "not consistent" ? "Inconsistent" : "Consistent";
                s.SpeedSpread = Iqr(arrivals) * AppPutt.DefaultFps;
            }

            s.TotalMisses = s.MissLeft + s.MissRight;
            if (s.TotalMisses >= 3)
            {
                double leftPct = 100.0 * s.MissLeft / s.TotalMisses;
                s.Tendency = leftPct > 65 ? "Pulling left" : leftPct < 35 ? "Pushing right" : "Neutral";
            }
            if (angles.Count > 0) { double a = 0; foreach (double v in angles) a += v; s.AvgEntryAngle = (int)AppPutt.JsRound(a / angles.Count); }

            foreach (KeyValuePair<int, int[]> kv in byFeet)
            {
                int idx = kv.Key - 3;
                s.Distances.Add((kv.Key, kv.Value[0], kv.Value[1], (int)AppPutt.JsRound(100.0 * kv.Value[0] / kv.Value[1]), idx >= 0 && idx < PgaBenchmarks.Length ? PgaBenchmarks[idx] : (int?)null));
            }
            for (int tee = 1; tee <= 3; tee++)
            {
                int[] b = byTee[tee];
                if (b[1] == 0) continue;
                s.Breaks.Add((tee, tee == 1 ? "Left break" : tee == 2 ? "Straight" : "Right break", b[0], b[1], (int)AppPutt.JsRound(100.0 * b[0] / b[1])));
            }
            return s;
        }

        /// <summary>RolloutFunctions.js iqr: the spread of the middle half (linear quantiles), 0 under two values.</summary>
        public static double Iqr(List<double> values)
        {
            if (values == null || values.Count < 2) return 0;
            var v = new List<double>(values);
            v.Sort();
            double Q(double q) { double pos = (v.Count - 1) * q; int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos); return v[lo] + (v[hi] - v[lo]) * (pos - lo); }
            return Q(0.75) - Q(0.25);
        }
    }
}
