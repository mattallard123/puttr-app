// puttr-app's own putt maths, line for line, so a putt scores the same here as in the app and
// in the data the app has saved for years:
//
//   components/common/GameFunctions.js    isBall, wasRollUnder, getMakeProbability, getHoleEntry,
//                                         getMissResult, getMissSide, getTeeEntry,
//                                         getEntryDescription, getPuttGrade
//   components/common/RolloutFunctions.js analyzeRollout, projectRollout, remainingTravel,
//                                         getStartLine, arrivalSpeed, getPuttScoreRollout, fmtRollout
//
// Everything is in doubles with JavaScript's rounding (Math.round rounds halves up), and the
// app's quirks are kept on purpose; each is marked APP QUIRK. Tools/parity runs the JavaScript
// and this file over the same putts and fails on any difference.
//
// A path is int[n][3] {x, y, radius} and a hole double[3] {x, y, diameter}, as the console
// sends them and the API stores them (the hole's third number is its DIAMETER: the app halves it).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PuttrApp
{
    public static class AppPutt
    {
        public const double BallDIn = 1.68, HoleDIn = 4.25, RampDecel = 64, DefaultFps = 38;
        public const double DefaultAlpha = RampDecel / (DefaultFps * DefaultFps);

        /// <summary>JavaScript's Math.round: halves go up (Math.round(-2.5) is -2).</summary>
        public static double JsRound(double x) => Math.Floor(x + 0.5);

        // ── Reading what the API stores ──────────────────────────────────────

        public static int[][] Path(string json)
        {
            var path = new List<int[]>();
            foreach (object point in J.List(J.Parse(json)))
            {
                List<object> p = J.List(point);
                if (p.Count < 3) continue;
                path.Add(new[] { (int)Puttr.PuttrJson.Number(p[0], 0), (int)Puttr.PuttrJson.Number(p[1], 0), (int)Puttr.PuttrJson.Number(p[2], 0) });
            }
            return path.ToArray();
        }

        public static double[] Hole(string json)
        {
            List<object> h = J.List(J.Parse(json));
            return h.Count < 3 ? null : new[] { Puttr.PuttrJson.Number(h[0], 0), Puttr.PuttrJson.Number(h[1], 0), Puttr.PuttrJson.Number(h[2], 0) };
        }

        public static int[][] Path(IReadOnlyList<Puttr.BallPoint> points)
        {
            var path = new int[points.Count][];
            for (int i = 0; i < points.Count; i++) path[i] = new[] { points[i].X, points[i].Y, points[i].R };
            return path;
        }

        public static double[] Hole(Puttr.Hole hole) => new double[] { hole.X, hole.Y, hole.Diameter };

        // ── GameFunctions.js ─────────────────────────────────────────────────

        public static bool IsBall(int[][] bp, double[] hb)
        {
            double lowestY = 0, max = 0;
            foreach (int[] p in bp) { if (p[2] > max) max = p[2]; if (p[1] > lowestY) lowestY = p[1]; }
            double holeBottom = hb[1] + hb[2] / 2;
            if (lowestY < holeBottom) return false;
            if (max > 60) return false;
            if (max < 10) return false;
            return true;
        }

        /// <summary>A path that is all inside the hole (a ball under the hole after a miss): ignored by the games.</summary>
        public static bool WasRollUnder(int[][] bp, double[] hb)
        {
            int c = 0;
            double r = hb[2] / 2 + 5;
            for (int i = bp.Length - 1; i >= 0; i--)
            {
                double a = hb[0] - bp[i][0], b = hb[1] - bp[i][1];
                if (Math.Sqrt(a * a + b * b) < r) c++; else break;
            }
            return c == bp.Length;
        }

        /// <summary>How many of the last ten points are within the hole (plus 10 px). The games score a make at 2 or more; the app saves made at 1 or more.</summary>
        public static int MakeProbability(int[][] bp, double[] hb)
        {
            int c = 0, ct = Math.Min(10, bp.Length);
            double r = hb[2] / 2 + 10;
            for (int i = bp.Length - 1; i >= bp.Length - ct; i--)
            {
                double a = hb[0] - bp[i][0], b = hb[1] - bp[i][1];
                if (Math.Sqrt(a * a + b * b) < r) c++; else break;
            }
            return c;
        }

        /// <summary>
        /// Where the ball crossed the lip: {x, y, degrees}, or degrees 0 with found false when it never did
        /// (the app returns [0,0,0,0], which its callers read as dead on line).
        /// APP QUIRK: atan(a / b) is used, not atan2, so a first point exactly level with the hole's
        /// centre (b = 0) reads as -90 on the right and +90 on the left, the wrong way round. Kept.
        /// </summary>
        public static double HoleEntry(int[][] bp, double[] hb, out bool found)
        {
            double cx = hb[0], cy = hb[1], radius = hb[2] / 2;
            foreach (int[] p in bp)
            {
                double a = cx - p[0], b = cy - p[1];
                if (Math.Sqrt(a * a + b * b) < radius)
                {
                    found = true;
                    bool left = p[0] < cx, back = p[1] < cy;
                    double deg = JsRound(Math.Atan(a / b) * (180 / Math.PI)); // a / 0 is +-Infinity, as in JavaScript
                    if (back && left) deg = -90 - (90 - deg);
                    else if (back) deg = 90 + (90 + deg);
                    return deg;
                }
            }
            found = false;
            return 0;
        }

        /// <summary>The path point nearest the hole's centre.</summary>
        static int[] Nearest(int[][] bp, double[] hb, out double dist)
        {
            int[] best = null;
            double bestSq = double.PositiveInfinity;
            foreach (int[] p in bp)
            {
                double dx = hb[0] - p[0], dy = hb[1] - p[1], sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; best = p; }
            }
            dist = Math.Sqrt(bestSq);
            return best;
        }

        /// <summary>"LEFT" or "RIGHT", always (getMissResult).</summary>
        public static string MissResult(int[][] bp, double[] hb)
        {
            int[] near = Nearest(bp, hb, out _);
            if (near == null) return "LEFT";
            return near[0] > hb[0] ? "RIGHT" : "LEFT";
        }

        /// <summary>"LEFT", "RIGHT", or null when too central to call (getMissSide).</summary>
        public static string MissSide(int[][] bp, double[] hb)
        {
            int[] near = Nearest(bp, hb, out _);
            if (near == null) return null;
            double radius = hb[2] / 2, offset = near[0] - hb[0];
            if (radius == 0 || Math.Abs(offset) < radius * 0.3) return null;
            return offset > 0 ? "RIGHT" : "LEFT";
        }

        public static double TeeEntry(double distance, int tee)
        {
            double a = tee == 1 ? -8 : tee == 3 ? 8 : 0;
            return JsRound(Math.Atan(a / (distance * 12)) * (180 / Math.PI)) * 1.1;
        }

        public static string EntryDescription(double e)
        {
            if (e > -18 && e < 18) return "CENTER CUT";
            if (e > 17 && e < 54) return "RIGHT CENTER";
            if (e > 53 && e < 90) return "RIGHT SIDE";
            if (e < -17 && e > -54) return "LEFT CENTER";
            if (e < -53 && e > -90) return "LEFT SIDE";
            return "BACK DOOR";
        }

        public static string Grade(double score)
        {
            if (score < 20) return "F";
            if (score < 40) return "D" + (score > 35 ? "+" : "");
            if (score < 60) return "C" + (score > 55 ? "+" : "");
            if (score < 80) return "B" + (score > 75 ? "+" : "");
            return "A" + (score > 95 ? "+" : "");
        }

        // ── RolloutFunctions.js ──────────────────────────────────────────────

        /// <summary>APP QUIRK: the "median" is the upper middle of an even count (s[floor(n / 2)]), not the average of the two.</summary>
        public static double? Median(IEnumerable<double> values)
        {
            var s = values.ToList();
            if (s.Count == 0) return null;
            s.Sort();
            return s[s.Count / 2];
        }

        public static string FmtRollout(double? v)
        {
            if (v == null || double.IsNaN(v.Value) || double.IsInfinity(v.Value)) return null;
            double abs = Math.Abs(v.Value);
            if (abs < 0.05) return "dead center";
            string side = v.Value > 0 ? "past" : "short";
            // JavaScript's toFixed(1): .NET's "0.0" rounds halves the same way for the values seen here.
            return abs >= 24 ? (abs / 12).ToString("0.0", CultureInfo.InvariantCulture) + " ft " + side : abs.ToString("0.0", CultureInfo.InvariantCulture) + " in " + side;
        }

        public static double SpeedScore(double? rollout, double target = 0, bool made = false)
        {
            if (rollout == null) return 0;
            double err = rollout.Value - target;
            if (made && err < 0) err = 0;
            double span = err < 0 ? 12 : 36;
            return Math.Max(0, 50 * (1 - Math.Abs(err) / span));
        }

        /// <summary>The 100-point putt score: an entry half and a pace half.</summary>
        public static double PuttScore(double angDiff, bool made, double? rollout, double target = 0)
        {
            double entry = 50, off = Math.Abs(angDiff);
            if (off > 0) entry = entry * Math.Max(0, 1 - off / 90);
            if (!made) entry = 0;
            return entry + SpeedScore(rollout, target, made);
        }

        static readonly double[,] Rho =
        {
            { 1.00, 0.404 }, { 1.25, 0.407 }, { 1.50, 0.407 }, { 1.75, 0.397 },
            { 2.00, 0.400 }, { 2.25, 0.392 }, { 2.50, 0.388 }, { 2.75, 0.382 },
            { 3.00, 0.368 }, { 3.25, 0.358 }, { 3.50, 0.345 }, { 3.75, 0.321 },
            { 4.00, 0.315 }, { 4.25, 0.296 }, { 4.50, 0.275 }, { 4.75, 0.271 }, { 5.00, 0.269 },
        };

        static double RhoAt(double u)
        {
            int n = Rho.GetLength(0);
            if (u <= Rho[0, 0]) return Rho[0, 1];
            for (int i = 1; i < n; i++)
                if (u <= Rho[i, 0]) { double u0 = Rho[i - 1, 0], r0 = Rho[i - 1, 1]; return r0 + (Rho[i, 1] - r0) * (u - u0) / (Rho[i, 0] - u0); }
            return Rho[n - 1, 1];
        }

        public static double PxPerInAt(double dHolePx, double holePxRad) => RhoAt(dHolePx / holePxRad) * holePxRad / (BallDIn / 2);

        const double LipCut = 1.4;

        sealed class Fit { public double A, B, R2; }

        static Fit FitLine(List<double> xs, List<double> ys)
        {
            int n = xs.Count;
            if (n < 3) return null;
            double sx = 0, sy = 0, sxy = 0, sxx = 0;
            for (int i = 0; i < n; i++) { sx += xs[i]; sy += ys[i]; sxy += xs[i] * ys[i]; sxx += xs[i] * xs[i]; }
            double denom = n * sxx - sx * sx;
            if (denom == 0) return null;
            double B = (n * sxy - sx * sy) / denom, A = (sy - B * sx) / n, mean = sy / n, ssRes = 0, ssTot = 0;
            for (int i = 0; i < n; i++) { double p = A + B * xs[i]; ssRes += (ys[i] - p) * (ys[i] - p); ssTot += (ys[i] - mean) * (ys[i] - mean); }
            return new Fit { A = A, B = B, R2 = ssTot != 0 ? 1 - ssRes / ssTot : 0 };
        }

        static Fit RobustFit(List<double> xs, List<double> ys)
        {
            Fit fit = FitLine(xs, ys);
            if (fit == null || xs.Count < 6) return fit;
            var residuals = new List<double>();
            for (int i = 0; i < xs.Count; i++) residuals.Add(ys[i] - (fit.A + fit.B * xs[i]));
            double sigma = Math.Sqrt(residuals.Sum(r => r * r) / residuals.Count);
            if (sigma == 0) return fit;
            var kx = new List<double>(); var ky = new List<double>();
            for (int i = 0; i < xs.Count; i++) if (Math.Abs(residuals[i]) <= 2.5 * sigma) { kx.Add(xs[i]); ky.Add(ys[i]); }
            if (kx.Count >= 4 && kx.Count < xs.Count) { Fit refit = FitLine(kx, ky); if (refit != null) return refit; }
            return fit;
        }

        public sealed class Rollout
        {
            public bool Ok, Reached, Passed, RolledBack;
            public string Reason;
            public double ObservedStop, VLast, VCut, XLast;
            public double? VEntry;
            public List<double> XMids = new List<double>(), V2s = new List<double>();
        }

        sealed class Sample { public double X, Y, RRaw, R, DHolePx, L, DHoleIn; }

        public static Rollout AnalyzeRollout(int[][] bp, double[] hb)
        {
            if (bp == null || bp.Length < 3 || hb == null) return new Rollout { Reason = "no data" };
            double hx = hb[0], hy = hb[1], holePxRad = hb[2] / 2;

            var samples = new List<Sample>();
            foreach (int[] p in bp)
            {
                Sample prev = samples.Count > 0 ? samples[samples.Count - 1] : null;
                if (prev != null && prev.X == p[0] && prev.Y == p[1] && prev.RRaw == p[2]) continue;
                if (p[2] == 0 || p[2] < 4) continue;
                samples.Add(new Sample { X = p[0], Y = p[1], RRaw = p[2] });
            }
            if (samples.Count < 5) return new Rollout { Reason = "too few samples" };

            var rr = samples.Select(s => s.RRaw).ToList();
            for (int i = 0; i < samples.Count; i++)
            {
                var w = new List<double> { rr[Math.Max(0, i - 1)], rr[i], rr[Math.Min(rr.Count - 1, i + 1)] };
                w.Sort();
                samples[i].R = w[1];
            }

            foreach (Sample s in samples) s.DHolePx = Hypot(s.X - hx, s.Y - hy);
            double L = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                Sample s = samples[i];
                if (i > 0)
                {
                    Sample p = samples[i - 1];
                    double dpx = Hypot(s.X - p.X, s.Y - p.Y);
                    L += dpx / ((PxPerInAt(s.DHolePx, holePxRad) + PxPerInAt(p.DHolePx, holePxRad)) / 2);
                }
                s.L = L;
                s.DHoleIn = s.DHolePx / PxPerInAt(s.DHolePx, holePxRad);
            }

            bool reached = false;
            int end = samples.Count;
            double minYSeen = samples[0].Y;
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i].DHolePx < holePxRad * LipCut) { reached = true; end = i; break; }
                minYSeen = Math.Min(minYSeen, samples[i - 1].Y);
                if (samples[i].Y > minYSeen + 3) { end = i; break; }
            }
            var seg = samples.GetRange(0, end);
            if (seg.Count < 4) return new Rollout { Reason = "approach too short", Reached = reached };

            bool rolledBack = false;
            if (reached)
            {
                double cutY = samples[end].Y;
                for (int i = end + 1; i < samples.Count; i++)
                {
                    Sample s = samples[i];
                    if (s.Y > hy && (s.DHolePx > holePxRad * LipCut || s.Y > cutY + holePxRad * 0.5)) { rolledBack = true; break; }
                }
            }

            int minIdx = 0;
            for (int i = 0; i < seg.Count; i++) if (seg[i].DHolePx < seg[minIdx].DHolePx) minIdx = i;
            bool passed = !reached && seg[seg.Count - 1].Y < seg[minIdx].Y - 2;

            double lHole = reached ? samples[end].L + samples[end].DHoleIn : passed ? seg[minIdx].L : seg[minIdx].L + seg[minIdx].DHoleIn;

            var fitSeg = !reached && !passed ? seg.GetRange(0, minIdx + 1) : seg;
            var vx = new List<double>(); var vy = new List<double>();
            for (int i = 1; i < fitSeg.Count; i++)
            {
                Sample a = fitSeg[i - 1], b = fitSeg[i];
                double v = b.L - a.L;
                if (v <= 0) continue;
                vx.Add((a.L + b.L) / 2 - lHole);
                vy.Add(v * v);
            }
            if (vx.Count < 3) return new Rollout { Reason = "too few velocity points", Reached = reached };

            Fit fit = RobustFit(vx, vy);
            if (fit == null) return new Rollout { Reason = "fit failed", Reached = reached };

            bool stillMoving = reached || passed;
            double observedStop = (reached ? samples[end].L : passed ? seg[seg.Count - 1].L : seg[minIdx].L) - lHole;
            double vLast = stillMoving ? Median(vy.Skip(Math.Max(0, vy.Count - 5)).Select(Math.Sqrt)) ?? 0 : 0;
            double xLast = stillMoving ? vx[vx.Count - 1] : observedStop;
            double? vFitSq = fit.B < 0 && fit.R2 >= 0.5 ? fit.A + fit.B * xLast : (double?)null;
            double vCut = stillMoving && vFitSq != null && vFitSq > 0 ? Math.Sqrt(vFitSq.Value) : vLast;

            return new Rollout
            {
                Ok = true, Reached = reached, Passed = passed, RolledBack = rolledBack,
                ObservedStop = observedStop, VLast = vLast, VCut = vCut, XLast = xLast,
                VEntry = Median(vy.Take(3).Select(Math.Sqrt)), XMids = vx, V2s = vy,
            };
        }

        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

        static readonly double[,] Remaining =
        {
            { 0.1, 0.09 }, { 0.2, 0.09 }, { 0.3, 0.20 }, { 0.4, 0.45 }, { 0.5, 0.88 },
            { 0.6, 1.48 }, { 0.7, 2.15 }, { 0.8, 2.84 }, { 0.9, 3.70 }, { 1.0, 4.55 },
            { 1.1, 5.43 }, { 1.2, 6.43 }, { 1.3, 7.9 }, { 1.4, 9.3 },
        };
        const double TailAlpha = 0.10;

        public static double RemainingTravel(double v)
        {
            int n = Remaining.GetLength(0);
            double vTop = Remaining[n - 1, 0], remTop = Remaining[n - 1, 1];
            if (!(v > 0)) return 0;
            if (v <= Remaining[0, 0]) return Remaining[0, 1] * (v / Remaining[0, 0]);
            for (int i = 1; i < n; i++)
                if (v <= Remaining[i, 0]) { double v0 = Remaining[i - 1, 0], r0 = Remaining[i - 1, 1]; return r0 + (Remaining[i, 1] - r0) * (v - v0) / (Remaining[i, 0] - v0); }
            return remTop + (v * v - vTop * vTop) / (2 * TailAlpha);
        }

        /// <summary>Inches past (+) or short (-) of the hole's centre; null when the path cannot be read. made: the game's own verdict, or null.</summary>
        public static double? ProjectRollout(Rollout a, bool? made)
        {
            if (a == null || !a.Ok) return null;
            if (a.VLast == 0) return a.XLast;
            if (a.Reached)
            {
                if (a.RolledBack || made == false) return Math.Min(a.ObservedStop, 0);
                if (a.XMids.Count >= 3) return Median(a.XMids.Select((x, i) => x + a.V2s[i] / (2 * DefaultAlpha)));
                return a.XLast + a.VLast * a.VLast / (2 * DefaultAlpha);
            }
            return a.XLast + RemainingTravel(a.VLast);
        }

        /// <summary>Signed speed reaching the hole, inches per camera frame (arrivalSpeed).</summary>
        public static double? ArrivalSpeed(Rollout a)
        {
            if (a == null || !a.Ok) return null;
            if (a.VLast > 0) return a.VLast;
            return -Math.Sqrt(2 * DefaultAlpha * Math.Max(0, -a.XLast));
        }

        /// <summary>Where the putt would have crossed the hole line (inches, + right) and the start line it implies (degrees).</summary>
        public static bool StartLine(int[][] bp, double[] hb, double distanceFt, out double deg, out double crossingIn)
        {
            deg = crossingIn = 0;
            if (bp == null || bp.Length < 3 || hb == null || hb[2] == 0) return false;
            double hx = hb[0], hy = hb[1], hpr = hb[2] / 2, minYSeen = double.PositiveInfinity;
            var xs = new List<double>(); var ys = new List<double>();
            int[] prev = null;
            foreach (int[] p in bp)
            {
                if (p == null || p.Length < 3) continue;
                if (prev != null && prev[0] == p[0] && prev[1] == p[1] && prev[2] == p[2]) continue;
                prev = p;
                if (p[2] == 0 || p[2] < 4) continue;
                double dHolePx = Hypot(p[0] - hx, p[1] - hy);
                if (dHolePx < hpr * LipCut) break;
                if (xs.Count > 0 && p[1] > minYSeen + 3) break;
                minYSeen = Math.Min(minYSeen, p[1]);
                double ppi = PxPerInAt(dHolePx, hpr);
                xs.Add((p[0] - hx) / ppi);
                ys.Add((p[1] - hy) / ppi);
            }
            int n = xs.Count;
            if (n < 6) return false;
            double sy = 0, sx = 0, sxy = 0, syy = 0;
            for (int i = 0; i < n; i++) { sy += ys[i]; sx += xs[i]; sxy += ys[i] * xs[i]; syy += ys[i] * ys[i]; }
            double denom = n * syy - sy * sy;
            if (denom == 0) return false;
            double m = (n * sxy - sy * sx) / denom;
            crossingIn = (sx - m * sy) / n;
            double D = (distanceFt == 0 ? 6 : distanceFt) * 12;
            deg = Math.Atan(crossingIn / D) * (180 / Math.PI);
            return true;
        }

        // ── A putt, judged as the app's games judge it ───────────────────────

        public sealed class Verdict
        {
            public bool Ball, RollUnder;   // the games ignore a putt that is not a ball, or that rolled under the hole
            public int Count;              // MakeProbability
            public bool Made => Count > 1; // what the games score
            public bool SavedMade => Count > 0; // what the app writes to user_putts.made (APP QUIRK: a 1 is saved as made)
            public double Entry, OffLine; public bool HasEntry;
            public string Description;     // from the entry against the tee's line, as the games do
            public string MissSide;        // conservative: null when too central
            public double? Rollout;
            public double Score;
            public string Grade;
            public double? StartLine;      // degrees, + right of the line (getStartLine)
        }

        public static Verdict Judge(int[][] bp, double[] hb, int distance, int tee)
        {
            var v = new Verdict { Ball = IsBall(bp, hb), RollUnder = WasRollUnder(bp, hb), Count = MakeProbability(bp, hb) };
            v.Entry = HoleEntry(bp, hb, out v.HasEntry);
            double line = TeeEntry(distance, tee);
            v.OffLine = v.Made ? v.Entry - line : 0;
            v.Description = EntryDescription(v.OffLine);
            v.MissSide = v.Made ? null : MissSide(bp, hb);
            v.Rollout = ProjectRollout(AnalyzeRollout(bp, hb), v.Made);
            // The score as Free putt gives it (games/Open.js processBallPath). APP QUIRK: the tee's line is only
            // taken off for a scored make (c > 1), but the angle and the made flag count from c > 0, so a
            // count-1 putt is scored on its raw entry angle (0 when the camera saw no entry).
            double angDiff = v.Count > 0 ? v.Entry - (v.Count > 1 ? line : 0) : 0;
            v.Score = PuttScore(angDiff, v.Count > 0, v.Rollout);
            v.Grade = Grade(v.Score);
            if (StartLine(bp, hb, distance, out double deg, out _)) v.StartLine = deg;
            return v;
        }
    }
}
