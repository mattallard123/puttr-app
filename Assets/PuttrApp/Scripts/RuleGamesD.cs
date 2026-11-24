// Rule games, fourth batch: Center Cut (puttr-app components/games/CenterCut.js). Constants are the app's.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PuttrApp
{
    // ── Center Cut: a make scores its distance, less the share it came in off the tee's line ──

    public sealed partial class CenterCutGame : ListGame
    {
        /// <summary>Half the scoring window round the tee's line (CenterCut.js processBallPath: tot = 180 / 2).</summary>
        public const double Window = 90;
        /// <summary>getEntryDescription's center cut: within 18 degrees of the line.</summary>
        public const double CenterDeg = 18;

        protected override string OwnTitle => "Center Cut";
        public override string GameType => "CENTERCUT";
        public override int MaxPlayers => 4;
        public override string Icon => "Crosshair";
        public override string[] HowTo => new[]
        {
            "Nine putts, one from every distance from 3 to 11 feet, a random tee each.",
            "A make scores its distance, less the share it comes in off the tee's line: dead center scores it all, 90 degrees off scores nothing.",
            "Misses and back-door makes score nothing. 63 is perfect.",
        };

        // createRandomPutts: 3 to 11 feet, each once, a random tee 1 to 3, shuffled. No Easy / Medium / Hard (Matt, 2026-11-21).
        protected override List<TournamentMath.Dealt> MakeList() => Scoring.NineFor(GameLevels.Medium, Dice);

        protected override Shot Take(AppPutt.Verdict v)
        {
            // The line the ball actually came in on (a straight putt enters at minus its heading), so a center cut reads
            // center whatever the tee: the same as the tee's line for a putt from the dealt tee, and right for a putt
            // simulator that rolls from its own spot. Without a launch read, the tee's line (CenterCut.js getTeeEntry).
            double line = Raw != null && Raw.Launch.Reached ? -Raw.Launch.HeadingDeg : AppPutt.TeeEntry(Feet, Tee);
            LastLine = line;
            if (!v.Made) return Mark(Call(0, false, "MISS", "0 of " + Feet), "Miss");
            // The window is that line ±90, both ends open (he[2] > left_bound && he[2] < right_bound).
            double off = v.HasEntry ? v.Entry - line : 0;
            if (Math.Abs(off) >= Window) return Mark(Call(0, false, "BACK DOOR", "In the back of the cup scores nothing."), "Back door");
            double score = Feet * (1 - Math.Abs(off) / Window);
            Score[P] += score;
            string word = Quality(score, Feet), where = Side(off);
            return Mark(Call(score, true, word, Scoring.N1(score) + " of " + Feet + "   ·   " + (where.ToUpperInvariant() == word ? "" : where + ", ") + Off(off)), where);
        }

        // Where each putt went in, against the line it was judged on, for the final screen ("Left center").
        readonly Dictionary<Shot, string> _where = new Dictionary<Shot, string>();
        Shot Mark(Shot s, string where) { if (Shots.Count == 0) _where.Clear(); _where[s] = where; return s; }

        public override string FinalCall(Shot s) => _where.TryGetValue(s, out string w) ? w : base.FinalCall(s);
        public override string FinalPoints(Shot s) => Scoring.N1(s.Points) + " of " + s.Feet;

        /// <summary>The line the last putt was judged against (the scene turns its window to it).</summary>
        public double LastLine;

        /// <summary>getScoreQuality: the share of the distance the make kept.</summary>
        public static string Quality(double score, int feet)
        {
            double ratio = feet > 0 ? score / feet : 0;
            return ratio > 0.9 ? "CENTER CUT" : ratio > 0.7 ? "NICE PUTT" : ratio > 0.4 ? "ON TARGET" : "EDGE ENTRY";
        }

        /// <summary>Where it went in (getEntryDescription against the tee's line): "Center cut", "Left center".</summary>
        public static string Side(double off)
        {
            // Center Cut says "edge" for the outer part of the window (Matt: left center, right center, left edge).
            string where = AppPutt.EntryDescription(off).Replace("SIDE", "EDGE");
            return where.Substring(0, 1) + where.Substring(1).ToLowerInvariant();
        }
        static string Off(double off)
        {
            int deg = (int)Math.Round(Math.Abs(off));
            return deg == 0 ? "dead on the line" : deg + "° " + (off < 0 ? "left" : "right") + " of the line";
        }

        // Scoreboard.js shows the running total to two places.
        public override string ScoreText(int p) => Score[p].ToString("0.00", CultureInfo.InvariantCulture);
        // The scene plays each putt's sounds in time with its replay (the roll, the drop, the cheer); only the win is the game's.
        public override string SoundFor(Shot s) => Over ? "rule_win" : null;
    }
}
