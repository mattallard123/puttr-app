// Tracks: the rules, as puttr-app's components/games/tracks/engine.js. Every mat putt plays one golf shot.
// The putt's length picks the club, the line (inches left or right of the cup as the ball passes it) sets how
// straight it flies, and the rollout (inches past the cup, negative short) sets how far.
//
// Units: course coordinates are yards in the course document's own frame (x, y as stored; the 3D world maps
// y to z). Green putts are feet; mat measurements are inches.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PuttrApp
{
    public static class TracksEngine
    {
        public const double PaceTargetIn = 0, HoleRadiusIn = 2.125, MatPxPerIn = 475.0 / 24;
        public const double PitchMaxYds = 80, GimmeFt = 2.5;
        public const int MaxPuttFt = 11, MinPuttFt = 3, LagDivisor = 4, MaxHoleScore = 12, WaterPenalty = 1;

        public sealed class Club { public string Id, Name, Short; public int Ft; public double Yds; public bool Big, Pitch, Putt, Lag, Tap; public double LeaveFt, Ratio = 1; public Club With(int ft) { var c = (Club)MemberwiseClone(); c.Ft = ft; return c; } }

        public static readonly Club[] Clubs =
        {
            new Club { Id = "DR", Name = "Driver", Short = "DR", Ft = 11, Yds = 300, Big = true },
            new Club { Id = "3W", Name = "3 Wood", Short = "3W", Ft = 10, Yds = 225 },
            new Club { Id = "5I", Name = "5 Iron", Short = "5i", Ft = 9, Yds = 195 },
            new Club { Id = "6I", Name = "6 Iron", Short = "6i", Ft = 8, Yds = 175 },
            new Club { Id = "7I", Name = "7 Iron", Short = "7i", Ft = 7, Yds = 158 },
            new Club { Id = "8I", Name = "8 Iron", Short = "8i", Ft = 6, Yds = 142 },
            new Club { Id = "9I", Name = "9 Iron", Short = "9i", Ft = 5, Yds = 125 },
            new Club { Id = "PW", Name = "Pitching Wedge", Short = "PW", Ft = 4, Yds = 105 },
            new Club { Id = "SW", Name = "Sand Wedge", Short = "SW", Ft = 3, Yds = 80 },
        };

        public sealed class Level { public string Id, Name; public Color Colour; public double Line, Depth, Entry, LagEntry; public string Blurb; }
        public static readonly Level[] Levels =
        {
            new Level { Id = "amateur", Name = "Amateur", Colour = new Color32(0x56, 0x87, 0xFF, 255), Line = 1.0, Depth = 0.05, Entry = 180, LagEntry = 10, Blurb = "Forgiving. Misses cost a few yards. Any front-door make counts." },
            new Level { Id = "pro", Name = "Pro", Colour = new Color32(0x7A, 0xD4, 0x46, 255), Line = 2.0, Depth = 0.10, Entry = 45, LagEntry = 5, Blurb = "A 3 in pull with a driver lands 18 yds left. Putts must enter the front 45 degrees." },
            new Level { Id = "major", Name = "Major", Colour = new Color32(0xFF, 0x52, 0x52, 255), Line = 3.0, Depth = 0.20, Entry = 30, LagEntry = 3, Blurb = "The cup is the fairway: miss it and you miss the fairway. Putts must enter the front 30 degrees." },
        };
        public static Level LevelOf(string id) => Levels.FirstOrDefault(l => l.Id == id) ?? Levels[1];
        const double ProDepth = 0.10;

        public sealed class Lie { public string Name; public int PenaltyFt; public double Error; public double MaxYds; }
        public static readonly Dictionary<string, Lie> Lies = new Dictionary<string, Lie>
        {
            { "tee", new Lie { Name = "Tee", PenaltyFt = 0, Error = 1, MaxYds = 999 } }, { "fairway", new Lie { Name = "Fairway", PenaltyFt = 0, Error = 1, MaxYds = 999 } },
            { "fringe", new Lie { Name = "Fringe", PenaltyFt = 0, Error = 1, MaxYds = 999 } }, { "green", new Lie { Name = "Green", PenaltyFt = 0, Error = 1, MaxYds = 999 } },
            { "rough", new Lie { Name = "Rough", PenaltyFt = 1, Error = 1.35, MaxYds = 999 } }, { "gorse", new Lie { Name = "Gorse", PenaltyFt = 2, Error = 1.6, MaxYds = 160 } },
            { "tree", new Lie { Name = "Trees", PenaltyFt = 2, Error = 1.6, MaxYds = 160 } }, { "azalea", new Lie { Name = "Azaleas", PenaltyFt = 1, Error = 1.4, MaxYds = 175 } },
            { "sand", new Lie { Name = "Sand", PenaltyFt = 2, Error = 1.6, MaxYds = 160 } }, { "water", new Lie { Name = "Water", PenaltyFt = 0, Error = 1, MaxYds = 999 } },
            { "road", new Lie { Name = "Road", PenaltyFt = 1, Error = 1.5, MaxYds = 160 } },
        };
        public static Lie LieOf(string key) => key != null && Lies.TryGetValue(key, out Lie l) ? l : Lies["rough"];

        public static int? PuttFor(Club club, string lie)
        {
            Lie l = LieOf(lie);
            if (club.Yds > l.MaxYds) return null;
            int ft = club.Ft + l.PenaltyFt;
            return ft > MaxPuttFt ? (int?)null : ft;
        }

        public static List<Club> Playable(string lie) => Clubs.Where(c => PuttFor(c, lie) != null).ToList();

        /// <summary>The caddie's call: a pitch under 80 yds, else the shortest club that reaches.</summary>
        public static Club Suggest(double distYds, string lie)
        {
            Lie l = LieOf(lie);
            if (distYds < PitchMaxYds) return new Club { Id = "PI", Name = "Pitch", Short = "Pitch", Ft = Math.Min(MaxPuttFt, MinPuttFt + l.PenaltyFt), Yds = Math.Round(distYds), Pitch = true };
            List<Club> list = Playable(lie);
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].Yds >= distYds) return list[i].With(PuttFor(list[i], lie) ?? list[i].Ft);
            return list[0].With(PuttFor(list[0], lie) ?? list[0].Ft);
        }

        /// <summary>The putt on the green for a leave: its own length to 11 ft, then a quarter-length lag whose miss scales back up.</summary>
        public static Club GreenPutt(double ft)
        {
            if (ft <= GimmeFt) return new Club { Id = "TAP", Name = "Tap in", Short = "Tap in", Putt = true, Tap = true };
            bool lag = ft > MaxPuttFt;
            int mat = Mathf.Clamp((int)Math.Round(lag ? ft / LagDivisor : ft), MinPuttFt, MaxPuttFt);
            return new Club { Id = "PT", Name = lag ? "Lag putt" : "Putt", Short = lag ? "Lag" : "Putt", Ft = mat, Putt = true, Lag = lag, LeaveFt = ft, Ratio = lag ? ft / mat : 1 };
        }

        public static double LagTolerance(string level, double leaveFt) => LevelOf(level).LagEntry * 20 / Math.Max(20, leaveFt);

        /// <summary>Where the ball went: inches left (-) or right (+) of the cup as it crossed the hole's line, or its nearest point.</summary>
        public static double ReadLineIn(int[][] bp, double[] hb)
        {
            if (bp == null || bp.Length < 2 || hb == null) return 0;
            double hx = hb[0], hy = hb[1];
            double? x = null;
            for (int i = 1; i < bp.Length; i++)
            {
                int[] a = bp[i - 1], b = bp[i];
                if ((a[1] >= hy && b[1] <= hy) || (a[1] <= hy && b[1] >= hy)) { double t = a[1] == b[1] ? 0 : (hy - a[1]) / (b[1] - a[1]); x = a[0] + (b[0] - a[0]) * t; break; }
            }
            if (x == null) { double best = double.MaxValue; foreach (int[] p in bp) { double d = (p[0] - hx) * (p[0] - hx) + (p[1] - hy) * (p[1] - hy); if (d < best) { best = d; x = p[0]; } } }
            return Math.Max(-12, Math.Min(12, ((x ?? hx) - hx) / MatPxPerIn));
        }

        /// <summary>A shot's error: yards sideways, yards long (+) or short (-), and the carry it was played to.</summary>
        public static (double lateral, double depth, double carry) ShotError(Club club, string lie, string level, double lineIn, double? rolloutIn, double? carryYds = null)
        {
            Lie l = LieOf(lie); Level lv = LevelOf(level);
            double carry = carryYds ?? club.Yds, roll = rolloutIn ?? PaceTargetIn;
            double lateral = lineIn * (carry / 100) * lv.Line * l.Error;
            if (club.Big)
            {
                double speedLoss = Math.Abs(roll - PaceTargetIn) / 12 * carry * lv.Depth * 2 * l.Error, lineLoss = Math.Abs(lineIn) * 4 * lv.Line * l.Error;
                return (lateral, -Math.Min(carry * 0.45, speedLoss + lineLoss), carry);
            }
            return (lateral, (roll - PaceTargetIn) / 12 * carry * lv.Depth * l.Error, carry);
        }

        /// <summary>A putt on the green: inches across and past (+) the hole after the roll, and whether it dropped.</summary>
        public static (bool made, double xin, double yin) GreenResult(double leaveFt, double lineIn, double? rolloutIn, string level, double ratio)
        {
            Level lv = LevelOf(level);
            double r = ratio > 0 ? ratio : (leaveFt > MaxPuttFt ? leaveFt / MaxPuttFt : 1), roll = rolloutIn ?? PaceTargetIn;
            double xin = lineIn * r, yin = (roll - PaceTargetIn) * r * (lv.Depth / ProDepth) + PaceTargetIn;
            return (Math.Abs(xin) <= HoleRadiusIn && yin >= 0, xin, yin);
        }

        public static string DescribePutt(double lineIn, double? rolloutIn)
        {
            string l = Math.Abs(lineIn) < 0.25 ? "Dead center" : lineIn < 0 ? "Pulled " + Math.Abs(lineIn).ToString("0.0") + " in" : "Pushed " + lineIn.ToString("0.0") + " in";
            if (rolloutIn == null) return l + ", pace unread";
            int d = (int)Math.Round(rolloutIn.Value - PaceTargetIn);
            return l + ", " + (Math.Abs(d) < 2 ? "perfect speed" : d < 0 ? -d + " in short" : d + " in long");
        }

        public static string DescribeShot(double lateral, double depth)
        {
            var parts = new List<string>();
            if (Math.Abs(lateral) >= 1) parts.Add(Math.Round(Math.Abs(lateral)) + " yds " + (lateral < 0 ? "left" : "right"));
            if (Math.Abs(depth) >= 1) parts.Add(Math.Round(Math.Abs(depth)) + " yds " + (depth < 0 ? "short" : "long"));
            return parts.Count > 0 ? string.Join(", ", parts) : "Flush";
        }

        public static string ScoreName(int strokes, int par)
        {
            int d = strokes - par;
            if (strokes == 1) return "Ace";
            if (d <= -3) return "Albatross";
            switch (d) { case -2: return "Eagle"; case -1: return "Birdie"; case 0: return "Par"; case 1: return "Bogey"; case 2: return "Double"; case 3: return "Triple"; default: return "+" + d; }
        }

        public static string ToPar(int n) => n == 0 ? "E" : n > 0 ? "+" + n : n.ToString();

        // ── The words on the shot interface (Tracks.js renderCaddie, renderDistanceBig, renderDistanceChip) ──
        // All of it is text worked out from the shot's facts, kept here so it can be tested without a course.

        static readonly System.Globalization.CultureInfo Plain = System.Globalization.CultureInfo.InvariantCulture;
        /// <summary>JavaScript's Math.round (halves go up), which the app's card text is written with.</summary>
        static long JsRound(double v) => (long)Math.Floor(v + 0.5);

        public static string TeeWords(int tee) => tee == 1 ? "left tee" : tee == 3 ? "right tee" : "center tee";
        public static string BreakWords(int tee) => tee == 1 ? "breaks left to right" : tee == 3 ? "breaks right to left" : "straight putt";

        /// <summary>The app's one-line hint under the putt: what the putt has to do, in a few words.</summary>
        public static string Hint(Club club, int tee)
        {
            if (club == null) return "";
            if (club.Tap) return "Gimme inside 2.5 ft. Tapping in.";
            if (!club.Putt) return "perfect speed";
            return (club.Lag ? "lag it close" : "make it") + (tee == 1 || tee == 3 ? ", " + BreakWords(tee) : "");
        }

        /// <summary>Why the putt is not the plain one, shown in amber as the app does: "left tee", "+1 ft rough". Nothing on the green.</summary>
        public static List<string> Adjustments(Club club, string lie, int tee)
        {
            var list = new List<string>();
            if (club == null || club.Putt || club.Tap) return list;
            if (tee == 1 || tee == 3) list.Add(TeeWords(tee));
            Lie l = LieOf(lie);
            if (l.PenaltyFt > 0) list.Add("+" + l.PenaltyFt + " ft " + l.Name.ToLowerInvariant());
            return list;
        }

        /// <summary>What is left: yards to the pin, or on the green feet to the hole (one decimal under 10 ft).</summary>
        public static string DistanceLeft(bool onGreen, double distYds)
        {
            if (!onGreen) return JsRound(distYds) + " YDS TO THE PIN";
            double ft = distYds * 3;
            return (ft < 10 ? ft.ToString("0.0", Plain) : JsRound(ft).ToString()) + " FT TO THE HOLE";
        }

        /// <summary>The colour of a lie's pill, as the app's lieChip: rough amber, sand its own, trouble red, azaleas pink, else green.</summary>
        public static string LieTone(string lie)
        {
            switch (lie)
            {
                case "rough": return "amber";
                case "sand": return "sand";
                case "azalea": return "pink";
                case "gorse": case "tree": case "water": case "road": return "red";
                default: return "green";
            }
        }

        // ── The shot card (tracks/PuttReadout.js ShotResultCard): the same words, worked out the same way ──

        /// <summary>Where a shot finished, as the card names it; null for a lie the card has no name for.</summary>
        public static string CardLie(string lie)
        {
            switch (lie)
            {
                case "fairway": return "Fairway"; case "green": return "On the green"; case "fringe": return "Fringe"; case "rough": return "Rough";
                case "sand": return "Bunker"; case "water": return "Water"; case "tee": return "Tee"; case "road": return "Road";
                case "tree": return "Trees"; case "gorse": return "Gorse"; case "azalea": return "Azaleas";
                default: return null;
            }
        }

        /// <summary>"good" for the fairway and the green, "bad" for trouble, "plain" for the rest.</summary>
        public static string CardLieTone(string lie)
        {
            if (lie == "fairway" || lie == "green") return "good";
            return lie == "water" || lie == "sand" || lie == "gorse" || lie == "tree" || lie == "azalea" || lie == "road" ? "bad" : "plain";
        }

        /// <summary>LINE: "Dead center" inside a yard, else the yards left or right.</summary>
        public static string CardLine(double lateralYds, out bool ok)
        {
            ok = Math.Abs(lateralYds) < 1;
            return ok ? "Dead center" : JsRound(Math.Abs(lateralYds)) + " yds " + (lateralYds < 0 ? "left" : "right");
        }

        /// <summary>Under LINE: the mat's own reading, "1.2 in left on the mat".</summary>
        public static string CardLineMat(double lineIn) =>
            Math.Abs(lineIn).ToString("0.0", Plain) + " in " + (lineIn < 0 ? "left " : lineIn > 0 ? "right " : "") + "on the mat";

        /// <summary>SPEED: "Perfect" inside a yard, else the yards short or long.</summary>
        public static string CardSpeed(double depthYds, out bool ok)
        {
            ok = Math.Abs(depthYds) < 1;
            return ok ? "Perfect" : JsRound(Math.Abs(depthYds)) + " yds " + (depthYds < 0 ? "short" : "long");
        }

        /// <summary>Under SPEED: the rollout on the mat, "+6 in past" or "-3 in short".</summary>
        public static string CardSpeedMat(double rollIn) => (rollIn > 0 ? "+" : "") + JsRound(rollIn) + " in " + (rollIn < 0 ? "short" : "past");

        // ── The booth (tracks/audio.js categories, called where Tracks.js calls commentate) ──

        /// <summary>A shot from the course: the tee shot by its lie, a shot at the green by where it finished, a lay-up by the grass it found.
        /// chip: a short pitch from beside the green (the app's chip from the green view).</summary>
        public static string CourseCall(string lie, bool teeShot, bool aimedGreen, bool chip, double leaveFt)
        {
            if (lie == "green") return chip ? (leaveFt <= 5 ? "chip_tight" : "chip_on") : leaveFt <= 8 ? "approach_tight" : "approach_on";
            if (lie == "water") return teeShot ? "tee_water" : "approach_water";
            if (lie == "sand") return teeShot ? "tee_sand" : "approach_sand";
            bool trees = lie == "gorse" || lie == "tree" || lie == "azalea" || lie == "scrub";
            if (teeShot) return lie == "fairway" ? "tee_fairway" : trees ? "tee_trees" : "tee_rough";
            if (aimedGreen) return "approach_miss";
            return lie == "fairway" ? "shot_fairway" : "shot_rough";
        }

        /// <summary>A putt on the green; null when the booth leaves it to the score call (a make from inside 15 ft).</summary>
        public static string PuttCall(bool made, double fromFt, bool lip, bool lag, double leftFt, double yin)
        {
            if (made) return fromFt >= 15 ? "putt_made_long" : null;
            if (lip) return "putt_lip";
            if (lag && leftFt <= 3.5) return "lag_good";
            return yin < -2 ? "putt_miss_short" : yin > 24 ? "putt_miss_long" : "putt_miss_line";
        }

        public static string ScoreCall(bool pickedUp, int strokes, int par) => pickedUp ? "score_pickup" : strokes <= par ? "score_good" : "score_bad";
        public static string RoundCall(int toPar) => toPar < 0 ? "round_under" : toPar == 0 ? "round_even" : "round_over";

        // ── Trouble by the target (what the commentary and the shot card know of the hole) ──

        /// <summary>
        /// The trouble by where the shot is meant to finish: water first, then sand, the nearest of it within 24 yds.
        /// side is "left", "right", "both", "short" or "long" of the target as the shot flies. A putt left of the cup
        /// flies left, a putt short of it lands short, so the side says which miss to keep away from.
        /// </summary>
        public static bool Trouble(Func<Vector2, string> lieAt, Vector2 from, Vector2 target, out string lie, out string side)
        {
            lie = null; side = null;
            Vector2 dir = target - from;
            if (lieAt == null || dir.sqrMagnitude < 1f) return false;
            dir.Normalize();
            Vector2 right = new Vector2(dir.y, -dir.x);   // the same right as a pushed putt's yards in the round
            foreach (string bad in new[] { "water", "sand" })
                foreach (float r in new[] { 8f, 16f, 24f })
                {
                    bool l = lieAt(target - right * r) == bad, rt = lieAt(target + right * r) == bad;
                    string found = l && rt ? "both" : l ? "left" : rt ? "right" : lieAt(target - dir * r) == bad ? "short" : lieAt(target + dir * r) == bad ? "long" : null;
                    if (found == null) continue;
                    lie = bad; side = found;
                    return true;
                }
            return false;
        }

        /// <summary>The leaderboard segment: set*10 + level (11..33), so a front nine at Amateur never meets 18 at Major.</summary>
        public static int LeaderboardCode(string holeSet, string level) => (Array.IndexOf(new[] { "front", "back", "all" }, holeSet) + 1) * 10 + (Array.FindIndex(Levels, l => l.Id == level) + 1);

        /// <summary>Whose turn it is, on the banner over the shot: "Ava's shot", or "Your shot" for a seat with no name.</summary>
        public static string TurnWords(string name) => string.IsNullOrEmpty(name) || name == "You" ? "Your shot" : name + "'s shot";

        /// <summary>A Player's name on the scorecard's label column: the first five letters, in capitals like HOLE and PAR.</summary>
        public static string CardName(string name)
        {
            string n = (name ?? "").Trim().ToUpperInvariant();
            return n.Length == 0 ? "?" : n.Length > 5 ? n.Substring(0, 5) : n;
        }
    }

    /// <summary>
    /// One Player in a Tracks round (puttr-app Tracks.js newRound's player): the scorecard and the round's stats, and
    /// where their ball lies on the hole being played. Each Player has their own ball, lie and strokes; the club is
    /// the caddie's call for whoever is on the tee, worked out again at every turn as the app's callCaddie does.
    /// </summary>
    public sealed class TracksPlayer
    {
        public readonly int Seat;
        public readonly string Name, UserId;
        public readonly Color Colour;
        public readonly int[] Strokes, Putts;
        public readonly bool?[] Fairways;
        public readonly bool[] Gir, Holed;
        public readonly List<double> Drives = new List<double>(), ApproachFt = new List<double>();
        public int Penalties, SandHoles, SandSaves, Scrambles;
        public double LongestPuttFt;
        /// <summary>This Player's user_tracks row (POST /tracks/round), once the API has made it.</summary>
        public string RoundId;

        // The hole being played.
        public Vector2 Ball;
        public string Lie = "tee";
        public bool OnGreen, HoleSand, HoledOut;
        /// <summary>Picked up at the stroke cap on the hole being played (the hole-done card says so).</summary>
        public bool Picked;
        public int ShotNo;

        /// <param name="userId">A profile's user id, RoundSaver.Guest for a guest; the signed-in Player's seat is 0.</param>
        public TracksPlayer(int seat, string name, string userId, Color colour, int holes)
        {
            Seat = seat; Name = name; UserId = userId; Colour = colour;
            Strokes = new int[holes]; Putts = new int[holes]; Fairways = new bool?[holes]; Gir = new bool[holes]; Holed = new bool[holes];
        }

        public int Total => Strokes.Sum();

        /// <summary>Strokes to par over the holes this Player has finished (the scorecard's TO PAR while the round is on).</summary>
        public int ToParSoFar(IList<int> pars)
        {
            int s = 0, p = 0;
            for (int i = 0; i < Strokes.Length && i < pars.Count; i++) if (Holed[i]) { s += Strokes[i]; p += pars[i]; }
            return s - p;
        }

        /// <summary>A guest has no account: the round is on no one's stats and no board.</summary>
        public bool Guest => string.IsNullOrEmpty(UserId) || UserId == "0";
    }

    /// <summary>
    /// The Players of a Tracks round and whose turn it is (Tracks.js nextTurn). Golf's order: on every hole everyone tees
    /// off in order, then whoever is farthest from the hole plays; a Player who has holed out sits out until the next tee.
    /// The tee order starts with the Player picked to go first and keeps the order they were listed in.
    /// </summary>
    public sealed class TracksRound
    {
        public readonly List<TracksPlayer> Players = new List<TracksPlayer>();
        public readonly int First;
        /// <summary>The seat whose turn it is.</summary>
        public int Cur { get; private set; }
        /// <summary>The hole being played (an index into the round's holes).</summary>
        public int Hole { get; private set; }

        public TracksRound(IList<string> names, IList<string> userIds, IList<Color> colours, int holes, int first = 0)
        {
            int n = Math.Max(1, Math.Min(4, names != null ? names.Count : 1));
            for (int i = 0; i < n; i++)
            {
                string name = names != null && i < names.Count ? names[i] : "Player " + (i + 1);
                string id = userIds != null && i < userIds.Count ? userIds[i] : null;
                Color c = colours != null && i < colours.Count ? colours[i] : Color.white;
                Players.Add(new TracksPlayer(i, name, id, c, holes));
            }
            First = Math.Max(0, Math.Min(n - 1, first));
            Cur = First;
        }

        public TracksPlayer Now => Players[Cur];
        public bool Multi => Players.Count > 1;

        /// <summary>The seats in tee order: the first Player, then round the list.</summary>
        public IEnumerable<int> TeeOrder => Enumerable.Range(0, Players.Count).Select(i => (First + i) % Players.Count);

        /// <summary>A new hole: every ball on the tee, nobody has played, and the first Player has the honour.</summary>
        public void StartHole(int hole, Vector2 tee)
        {
            Hole = hole;
            foreach (TracksPlayer p in Players) { p.Ball = tee; p.Lie = "tee"; p.OnGreen = false; p.HoleSand = false; p.HoledOut = false; p.Picked = false; p.ShotNo = 0; }
            Cur = First;
        }

        /// <summary>Has this Player hit their tee shot on the hole being played?</summary>
        public bool Teed(TracksPlayer p) => Hole < p.Strokes.Length && p.Strokes[Hole] > 0;

        /// <summary>
        /// Moves the turn on: the next Player still to tee off, in tee order; once everyone has, whoever is farthest from
        /// the pin (a tie goes to the earlier in tee order), which can be the Player who has just played. False when
        /// everyone has holed out.
        /// </summary>
        public bool NextTurn(Vector2 pin)
        {
            TracksPlayer pick = null;
            foreach (int i in TeeOrder) { TracksPlayer p = Players[i]; if (!p.HoledOut && !Teed(p)) { pick = p; break; } }
            if (pick == null)
                foreach (int i in TeeOrder)
                {
                    TracksPlayer p = Players[i];
                    if (p.HoledOut) continue;
                    if (pick == null || Vector2.Distance(p.Ball, pin) > Vector2.Distance(pick.Ball, pin) + 0.01f) pick = p;
                }
            if (pick == null) return false;
            Cur = pick.Seat;
            return true;
        }

        public bool AllHoledOut => Players.All(p => p.HoledOut);

        /// <summary>Everyone, lowest total first (ties in tee order).</summary>
        public List<TracksPlayer> Ranking()
        {
            List<int> order = TeeOrder.ToList();
            return Players.OrderBy(p => p.Total).ThenBy(p => order.IndexOf(p.Seat)).ToList();
        }

        /// <summary>A Player's place: 1 plus how many took fewer strokes, so a tie shares the place.</summary>
        public int Place(TracksPlayer p) => 1 + Players.Count(q => q.Total < p.Total);

        /// <summary>The result's headline (Tracks.js renderGameOver): "Ava wins", or "Tied" when the lowest total is shared.</summary>
        public string Outcome()
        {
            List<TracksPlayer> r = Ranking();
            if (r.Count > 1 && r[1].Total == r[0].Total) return "Tied";
            return r[0].Name + " wins";
        }
    }

    /// <summary>A course document from GET /tracks/course/:slug: holes in course yards and the OSM shapes.</summary>
    public sealed class TracksCourse
    {
        public sealed class Hole { public int N, Par, Yds; public string Name; public Vector2[] Line; public Vector2 Tee, Pin; public float GreenR; public float[] Cum; public float Len; }
        public string Slug, Name, Location;
        public int Version = 1;
        public readonly List<Hole> Holes = new List<Hole>();
        public readonly Dictionary<string, List<Vector2[]>> Features = new Dictionary<string, List<Vector2[]>>();

        static Vector2 P(object p) { List<object> a = J.List(p); return a.Count >= 2 ? new Vector2((float)Puttr.PuttrJson.Number(a[0], 0), (float)Puttr.PuttrJson.Number(a[1], 0)) : Vector2.zero; }

        public static TracksCourse Read(object doc)
        {
            var c = new TracksCourse { Slug = J.Str(doc, "slug", J.Str(doc, "id")), Name = J.Str(doc, "name"), Location = J.Str(doc, "location"), Version = J.Int(doc, "version", 1) };
            object body = J.Get(doc, "course") ?? J.Get(doc, "data") ?? doc;
            if (J.Get(body, "holes") == null && J.Str(doc, "doc").Length > 0) body = J.Parse(J.Str(doc, "doc"));
            foreach (object h in J.List(J.Get(body, "holes")))
            {
                Vector2[] line = J.List(J.Get(h, "line")).Select(P).ToArray();
                if (line.Length < 2) continue;
                var hole = new Hole { N = J.Int(h, "n"), Name = J.Str(h, "name"), Par = J.Int(h, "par", 4), Yds = J.Int(h, "yds"), Line = line, Tee = J.Get(h, "tee") != null ? P(J.Get(h, "tee")) : line[0], Pin = J.Get(h, "pin") != null ? P(J.Get(h, "pin")) : line[line.Length - 1], GreenR = (float)J.Num(h, "greenR", 15) };
                hole.Cum = new float[line.Length];
                for (int i = 1; i < line.Length; i++) hole.Cum[i] = hole.Cum[i - 1] + Vector2.Distance(line[i - 1], line[i]);
                hole.Len = hole.Cum[line.Length - 1];
                c.Holes.Add(hole);
            }
            foreach (KeyValuePair<string, object> f in J.Obj(J.Get(body, "features")) ?? new Dictionary<string, object>())
                c.Features[f.Key] = J.List(f.Value).Select(poly => J.List(poly).Select(P).ToArray()).Where(a => a.Length >= 2).ToList();
            if (J.Get(body, "boundary") != null && !c.Features.ContainsKey("boundary"))
                c.Features["boundary"] = J.List(J.Get(body, "boundary")).Select(poly => J.List(poly).Select(P).ToArray()).Where(a => a.Length >= 3).ToList();
            return c;
        }

        /// <summary>A tile-only course from the catalog's small `art` outline (fairways, greens, water, boundary; no holes).</summary>
        public static TracksCourse FromArt(object meta)
        {
            object art = J.Get(meta, "art");
            if (art is string s && s.Length > 0) art = J.Parse(s);
            var c = new TracksCourse { Slug = J.Str(meta, "slug"), Name = J.Str(meta, "name"), Location = J.Str(meta, "location"), Version = J.Int(meta, "version", 1) };
            foreach (KeyValuePair<string, object> f in J.Obj(art) ?? new Dictionary<string, object>())
                c.Features[f.Key] = J.List(f.Value).Select(poly => J.List(poly).Select(P).ToArray()).Where(a => a.Length >= 2).ToList();
            return c;
        }

        public List<Vector2[]> F(string key) => Features.TryGetValue(key, out List<Vector2[]> l) ? l : new List<Vector2[]>();

        static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y + 1e-9f) + poly[i].x) inside = !inside;
            return inside;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b) { Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)); return Vector2.Distance(p, a + ab * t); }

        bool Hit(Vector2 p, string key) { foreach (Vector2[] poly in F(key)) if (poly.Length >= 3 && Inside(p, poly)) return true; return false; }
        bool Near(Vector2 p, string key, float d) { foreach (Vector2[] poly in F(key)) for (int i = 1; i < poly.Length; i++) if (SegDist(p, poly[i - 1], poly[i]) <= d) return true; return false; }

        /// <summary>classifyPolyExact: water, bunker, green (and a 3 yd fringe), tee, fairway, road, trees, gorse, else rough.</summary>
        public string LieAt(Vector2 p)
        {
            if (Hit(p, "water")) return "water";
            if (Hit(p, "bunker") || Hit(p, "sand")) return "sand";
            if (Hit(p, "green")) return "green";
            if (Near(p, "green", 3)) return "fringe";
            if (Hit(p, "tee")) return "tee";
            if (Hit(p, "fairway")) return "fairway";
            if (Near(p, "road", 3)) return "road";
            if (Hit(p, "wood")) return "tree";
            if (Hit(p, "scrub")) return "gorse";
            return "rough";
        }

        public static Vector2 PointAt(Hole h, float s)
        {
            s = Mathf.Clamp(s, 0, h.Len);
            for (int i = 1; i < h.Line.Length; i++) if (s <= h.Cum[i]) { float seg = Mathf.Max(1e-4f, h.Cum[i] - h.Cum[i - 1]); return Vector2.Lerp(h.Line[i - 1], h.Line[i], (s - h.Cum[i - 1]) / seg); }
            return h.Pin;
        }

        public static (float d, float s) Closest(Hole h, Vector2 p)
        {
            float best = float.MaxValue, bs = 0;
            for (int i = 1; i < h.Line.Length; i++)
            {
                Vector2 a = h.Line[i - 1], ab = h.Line[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best) { best = d; bs = h.Cum[i - 1] + t * ab.magnitude; }
            }
            return (best, bs);
        }
    }
}
