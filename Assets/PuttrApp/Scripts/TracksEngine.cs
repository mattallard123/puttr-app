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

        /// <summary>The leaderboard segment: set*10 + level (11..33), so a front nine at Amateur never meets 18 at Major.</summary>
        public static int LeaderboardCode(string holeSet, string level) => (Array.IndexOf(new[] { "front", "back", "all" }, holeSet) + 1) * 10 + (Array.FindIndex(Levels, l => l.Id == level) + 1);
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
