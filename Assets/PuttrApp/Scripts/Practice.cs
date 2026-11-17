// A practice session's numbers, kept apart from the screen so they can be tested: what was
// putted, the streaks, where the misses went, how each distance is going, and whether the goal
// has been met. The same figures puttr-app's Free putt (games/Open.js) shows.
using System;
using System.Collections.Generic;
using Puttr;

namespace PuttrApp
{
    public enum PracticeGoal { Free, Streak, Total }

    public sealed class PracticePutt
    {
        public int Feet, Tee;
        public bool Made, Reached = true;
        public MissSide Side;
        public double? Rollout;      // inches past (+) or short (-) of the hole's centre, the app's pace measure
        public string Entry, Grade;  // "Center cut", "B+"
        public double Score;         // the app's 100-point putt score
        public UnityEngine.Vector2[] PathInches;

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
        };

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
    }
}
