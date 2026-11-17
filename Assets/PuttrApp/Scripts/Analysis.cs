// The putting analysis behind the Improve tab, ported from puttr-app so the numbers match:
//
//   the 27-spot grid (3 to 11 ft by three tees), weighted against the PGA Tour   actions/UserActions.js fetchAnalysis
//   hole entry bands and the back door                                            common/GameFunctions.js getEntryDescription
//   where misses go, left or right                                               common/GameFunctions.js getMissResult
//   pace from rollout inches                                                       analysis/PaceMap.js, Speed.js
//   PQS                                                                            members/PuttScore.js scorePutts
//   today's session from the weakest spots                                         coach/mvp/coachLogic.js (simplified: see Session)
//
// Putts arrive as the API stores them: ballPath and holeBounds are JSON strings, rollout a
// decimal string or null. Entry angles and miss sides come from the putt SDK (PuttrAnalysis),
// which is the same geometry the app's GameFunctions uses.
using System;
using System.Collections.Generic;
using System.Globalization;
using Puttr;
using UnityEngine;

namespace PuttrApp
{
    public static class AnalysisMath
    {
        public static readonly int[] Pga = ProfileMath.Pga;
        public static readonly string[] TeeNames = { "Left", "Center", "Right" };

        public sealed class Cell
        {
            public int Feet, Tee, Made, Total;
            public int Percent => Total > 0 ? (int)Math.Round(100.0 * Made / Total) : 0;
            /// <summary>Make % as a share of the PGA Tour's at this distance: 100 is Tour level.</summary>
            public int Weighted => Total > 0 ? (int)Math.Round(Percent / (double)Pga[Feet - 3] * 100.0) : 0;
            public string Name => Feet + " ft " + TeeNames[Tee - 1];
        }

        /// <summary>The 27 spots from GET /profile/analysis/:days rows {total, made (a string), distance, tee}.</summary>
        public static List<Cell> Grid(List<object> rows)
        {
            var cells = new List<Cell>();
            for (int feet = 3; feet <= 11; feet++)
                for (int tee = 1; tee <= 3; tee++)
                    cells.Add(new Cell { Feet = feet, Tee = tee });
            foreach (object row in rows)
            {
                int feet = J.Int(row, "distance"), tee = J.Int(row, "tee");
                if (feet < 3 || feet > 11 || tee < 1 || tee > 3) continue;
                Cell cell = cells[(feet - 3) * 3 + tee - 1];
                cell.Total += J.Int(row, "total");
                cell.Made += J.Int(row, "made");
            }
            return cells;
        }

        public static int Putts(List<Cell> grid) { int n = 0; foreach (Cell c in grid) n += c.Total; return n; }
        public static int MakePercent(List<Cell> grid) { int made = 0, all = 0; foreach (Cell c in grid) { made += c.Made; all += c.Total; } return all > 0 ? (int)Math.Round(100.0 * made / all) : -1; }

        /// <summary>The best and worst spots putted, by the weighted make %.</summary>
        public static void Extremes(List<Cell> grid, out Cell best, out Cell worst)
        {
            best = worst = null;
            foreach (Cell c in grid)
            {
                if (c.Total == 0) continue;
                if (best == null || c.Weighted > best.Weighted) best = c;
                if (worst == null || c.Weighted < worst.Weighted) worst = c;
            }
        }

        /// <summary>Worst to first: every spot putted, weakest first.</summary>
        public static List<Cell> WorstFirst(List<Cell> grid)
        {
            var cells = grid.FindAll(c => c.Total > 0 && c.Weighted > 0);
            cells.Sort((a, b) => a.Weighted.CompareTo(b.Weighted));
            return cells;
        }

        // ── One putt, from the API's row ─────────────────────────────────────

        public sealed class Putt
        {
            public int Feet, Tee;
            public bool Made, HasEntry, HasRollout;
            public int EntryAngle;        // where it crossed the lip, SDK degrees
            public float OffLine;         // entry less the putt's own line from its tee
            public MissSide Side;
            public float Rollout;         // inches past the hole (negative: short)
            public DateTime At;
        }

        public static Putt Read(object row)
        {
            var putt = new Putt { Feet = J.Int(row, "distance"), Tee = Mathf.Clamp(J.Int(row, "tee", 2), 1, 3), Made = J.Int(row, "made") == 1 };
            DateTime? at = ProfileMath.Date(row, "created_at");
            putt.At = at ?? DateTime.MinValue;
            if (J.Get(row, "rollout") != null) { putt.Rollout = (float)J.Num(row, "rollout"); putt.HasRollout = true; }
            List<BallPoint> path = PuttrAnalysis.ParsePath(J.Str(row, "ballPath"));
            if (path == null || path.Count < 2 || !PuttrAnalysis.TryParseHole(J.Str(row, "holeBounds"), out Hole hole)) return putt;
            if (putt.Made && PuttrAnalysis.TryHoleEntry(path, hole, out _, out int angle))
            {
                putt.HasEntry = true;
                putt.EntryAngle = angle;
                putt.OffLine = angle - PuttrAnalysis.ExpectedEntryAngle(Mathf.Clamp(putt.Feet, 3, 11), putt.Tee);
            }
            if (!putt.Made) putt.Side = PuttrAnalysis.GetMissSide(path, hole);
            return putt;
        }

        public static List<Putt> ReadAll(List<object> rows)
        {
            var putts = new List<Putt>();
            foreach (object row in rows)
            {
                Putt putt = Read(row);
                if (putt.Feet >= 3 && putt.Feet <= 11) putts.Add(putt);
            }
            return putts;
        }

        // ── Hole entry ───────────────────────────────────────────────────────

        public static readonly string[] Bands = { "Left side", "Left center", "Center cut", "Right center", "Right side" };

        /// <summary>The app's bands, measured off each putt's own line: center within 18 degrees, center 18 to 54, side 54 to 90, past 90 the back door.</summary>
        public static int Band(float offLine)
        {
            float a = Mathf.Abs(offLine);
            if (a >= 90f) return -1;
            if (a < 18f) return 2;
            if (a < 54f) return offLine < 0 ? 1 : 3;
            return offLine < 0 ? 0 : 4;
        }

        /// <summary>Holed putts by entry band (five counts, left to right), and how many came in the back door.</summary>
        public static int[] Entries(List<Putt> putts, out int backDoor, out int holed)
        {
            var bands = new int[5];
            backDoor = holed = 0;
            foreach (Putt p in putts)
            {
                if (!p.Made || !p.HasEntry) continue;
                holed++;
                int band = Band(p.OffLine);
                if (band < 0) backDoor++; else bands[band]++;
            }
            return bands;
        }

        public static void Misses(List<Putt> putts, out int left, out int right)
        {
            left = right = 0;
            foreach (Putt p in putts)
            {
                if (p.Made) continue;
                if (p.Side == MissSide.Left) left++;
                else if (p.Side == MissSide.Right) right++;
            }
        }

        /// <summary>Pushes and pulls: misses by side and makes by which side of the line they came in. A right-handed Player's left is a pull.</summary>
        public static void PushPull(List<Putt> putts, bool leftHanded, out int push, out int pull)
        {
            int left = 0, right = 0;
            foreach (Putt p in putts)
            {
                if (!p.Made) { if (p.Side == MissSide.Left) left++; else if (p.Side == MissSide.Right) right++; }
                else if (p.HasEntry) { if (p.OffLine < 0) left++; else if (p.OffLine > 0) right++; }
            }
            pull = leftHanded ? right : left;
            push = leftHanded ? left : right;
        }

        // ── Pace ─────────────────────────────────────────────────────────────

        /// <summary>Putts with a rollout by zone: short (under 0 in), ideal (0 to 12 in past), too firm (over 12 in).</summary>
        public static void Pace(List<Putt> putts, out int shortOnes, out int ideal, out int firm, out float median)
        {
            shortOnes = ideal = firm = 0;
            var values = new List<float>();
            foreach (Putt p in putts)
            {
                if (!p.HasRollout) continue;
                values.Add(p.Rollout);
                if (p.Rollout < 0f) shortOnes++; else if (p.Rollout <= 12f) ideal++; else firm++;
            }
            values.Sort();
            median = values.Count == 0 ? 0f : values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) * 0.5f;
        }

        public static string Rollout(float inches) => inches < 0 ? Mathf.Abs(inches).ToString("0", CultureInfo.InvariantCulture) + " in short" : inches.ToString("0", CultureInfo.InvariantCulture) + " in past";

        /// <summary>Average rollout over a distance band (3-5, 6-8, 9-11); NaN when none.</summary>
        public static float PaceAt(List<Putt> putts, int from, int to)
        {
            float sum = 0f; int n = 0;
            foreach (Putt p in putts) if (p.HasRollout && p.Feet >= from && p.Feet <= to) { sum += p.Rollout; n++; }
            return n == 0 ? float.NaN : sum / n;
        }

        // ── PQS ──────────────────────────────────────────────────────────────

        /// <summary>
        /// PQS, as the app scores it: 65 parts make index (make % against the Tour's, averaged over
        /// the distances putted) and 35 parts accuracy (how true the holed putts came in).
        /// </summary>
        public static int Pqs(List<Putt> putts, out float makeIndex, out float accuracy)
        {
            makeIndex = accuracy = 0f;
            var made = new int[9]; var all = new int[9];
            float trueness = 0f; int holed = 0;
            foreach (Putt p in putts)
            {
                all[p.Feet - 3]++;
                if (!p.Made) continue;
                made[p.Feet - 3]++;
                holed++;
                // A make the camera saw no entry for is taken as on its line.
                trueness += p.HasEntry ? Mathf.Max(0f, 1f - Mathf.Abs(p.OffLine) / 90f) : 1f;
            }
            int distances = 0; float index = 0f;
            for (int i = 0; i < 9; i++)
            {
                if (all[i] == 0) continue;
                distances++;
                index += (made[i] / (float)all[i] * 100f) / Pga[i];
            }
            if (distances == 0) return -1;
            makeIndex = index / distances;
            accuracy = holed > 0 ? trueness / holed : 0f;
            return Mathf.RoundToInt((0.65f * makeIndex + 0.35f * accuracy) * 100f);
        }

        /// <summary>PQS for each day with five or more putts, oldest first.</summary>
        public static List<(DateTime day, int pqs)> PqsByDay(List<Putt> putts)
        {
            var days = new SortedDictionary<DateTime, List<Putt>>();
            foreach (Putt p in putts)
            {
                if (p.At == DateTime.MinValue) continue;
                DateTime day = p.At.ToLocalTime().Date;
                if (!days.TryGetValue(day, out List<Putt> list)) days[day] = list = new List<Putt>();
                list.Add(p);
            }
            var points = new List<(DateTime, int)>();
            foreach (KeyValuePair<DateTime, List<Putt>> day in days)
                if (day.Value.Count >= 5) points.Add((day.Key, Pqs(day.Value, out _, out _)));
            return points;
        }

        // ── Today's session ──────────────────────────────────────────────────

        /// <summary>
        /// A practice session built on the weakest spots: each spot's make % is pulled toward the
        /// Player's own average until it has enough putts behind it (the app's shrink, K = 10), the
        /// three weakest with three or more putts are dealt in turn, and a quarter of the session
        /// goes to spots barely putted. With no history it maps the mat.
        ///
        /// Simpler than the app's coachLogic (no named focus types, no two-day rule, no ramp up or
        /// down mid-session): it is the same idea, and its choice can be checked against the grid.
        /// </summary>
        public static List<TournamentMath.Dealt> Session(List<Cell> grid, int count, System.Random dice, out string title, out string why)
        {
            count = Math.Max(3, count);
            int made = 0, all = 0;
            foreach (Cell c in grid) { made += c.Made; all += c.Total; }
            float average = all > 0 ? made / (float)all : 0.5f;
            var weak = grid.FindAll(c => c.Total >= 3);
            weak.Sort((a, b) => Shrunk(a, average).CompareTo(Shrunk(b, average)));
            var fresh = grid.FindAll(c => c.Total < 10);
            var putts = new List<TournamentMath.Dealt>();
            if (weak.Count == 0)
            {
                title = "Map your green";
                why = "A few putts from each part of the mat, so I can see where you are strong.";
                int[] feet = { 3, 5, 7, 9, 11, 4, 6, 8, 10 };
                for (int i = 0; i < count; i++) putts.Add(new TournamentMath.Dealt { Feet = feet[i % feet.Length], Tee = 1 + i % 3 });
                return putts;
            }
            Cell worst = weak[0];
            title = "Lock in " + worst.Feet + " ft " + (worst.Tee == 2 ? "straight" : worst.Tee == 1 ? "from the left" : "from the right");
            why = "Your softest spot is " + worst.Name.ToLowerInvariant() + ": you are making " + worst.Percent + "% there. Today works on it, with your next weakest spots mixed in.";
            int top = Math.Min(3, weak.Count);
            for (int i = 0; i < count; i++)
            {
                Cell pick = fresh.Count > 0 && i % 4 == 3 ? fresh[dice.Next(fresh.Count)] : weak[i % top];
                putts.Add(new TournamentMath.Dealt { Feet = pick.Feet, Tee = pick.Tee });
            }
            return putts;
        }

        static float Shrunk(Cell c, float average) => (c.Made + 10f * average) / (c.Total + 10f);
    }
}
