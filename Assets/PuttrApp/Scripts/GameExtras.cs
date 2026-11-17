// What GameView.js adds below a game's header, and the top-100 board with its Recent | All Time toggle:
//
//   PuttAssessment     getPuttAssessment: make %, left/right/high/low tallies, pace consistency, push/pull
//   MatPlot            PuttMatFunctions puttToMat + PuttMat: each putt's path drawn on the 24 x 144 in mat
//   MatSummary         PuttMatSummary: made, by distance, pace, start line, make % by break, beside the mat
//   VertexSummary      VertexSessionSummary: accuracy and consistency bell scores, and what separates makes from misses
//   Chipping           the chipping layout: hero, miss zones, by surface, the chips
//   GameLeaderboardScreen  GET /club/leaderboard/PUTTR/:gameId/:distance/?hours=24 (Recent) or all time
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class PuttAssessment
    {
        public int PercentMade, Left, Right, High, Low, PushPullPercent;
        public string PushPull = "none", SpeedConsistency = "consistent", SpeedDispersion = "0.0";

        public static int[][] Bp(object row) => AppPutt.Path(J.Str(row, "ballPath"));
        public static double[] Hb(object row) => AppPutt.Hole(J.Str(row, "holeBounds"));

        /// <summary>paceConsistency: IQR of arrival speeds (linear quantiles); under 3 values reads "consistent".</summary>
        public static (string words, double iqr) Consistency(List<double> v)
        {
            if (v.Count < 3) return ("consistent", 0);
            var s = v.OrderBy(x => x).ToList();
            double Q(double q) { double pos = (s.Count - 1) * q; int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos); return s[lo] + (s[hi] - s[lo]) * (pos - lo); }
            double iqr = Q(0.75) - Q(0.25);
            return (iqr < 0.18 ? "very consistent" : iqr >= 0.50 ? "not consistent" : "consistent", iqr);
        }

        public static PuttAssessment Of(List<object> putts, string handedness)
        {
            var a = new PuttAssessment();
            int made = 0;
            var speeds = new List<double>();
            foreach (object p in putts)
            {
                int[][] bp = Bp(p); double[] hb = Hb(p);
                bool m = J.Int(p, "made") == 1;
                if (m) made++;
                if (bp.Length < 2 || hb == null) continue;
                double? arrival = AppPutt.ArrivalSpeed(AppPutt.AnalyzeRollout(bp, hb));
                if (arrival.HasValue) speeds.Add(arrival.Value);
                int tee = J.Int(p, "tee", 2);
                if (!m) { if (AppPutt.MissResult(bp, hb) == "RIGHT") a.Right++; else a.Left++; continue; }
                double entry = AppPutt.HoleEntry(bp, hb, out bool _);
                double diff = entry - AppPutt.TeeEntry(J.Num(p, "distance"), tee);
                if (diff > 0) a.Right++; else if (diff < 0) a.Left++;
                if (tee == 1) { if (diff < 0) a.High++; else if (diff > 0) a.Low++; }
                if (tee == 3) { if (diff > 0) a.High++; else if (diff < 0) a.Low++; }
            }
            a.PercentMade = putts.Count > 0 ? (int)AppPutt.JsRound(made * 100.0 / putts.Count) : 0;
            var consistency = Consistency(speeds);
            a.SpeedConsistency = consistency.words;
            double iqr = consistency.iqr;
            a.SpeedDispersion = (iqr * 38).ToString("0.0", CultureInfo.InvariantCulture);
            bool lefty = (handedness ?? "").ToUpperInvariant() == "LEFTY";
            if (a.Left != a.Right && ((handedness ?? "").ToUpperInvariant() == "RIGHTY" || lefty))
            {
                bool leftWins = a.Left > a.Right;
                a.PushPull = leftWins ^ lefty ? "pull" : "push";
                a.PushPullPercent = (int)AppPutt.JsRound(Math.Max(a.Left, a.Right) * 100.0 / (a.Left + a.Right));
            }
            return a;
        }
    }

    // ── The mat ──────────────────────────────────────────────────────────────

    public static class MatMath
    {
        public const double W = 24, H = 144, HoleFromTop = 5;
        public sealed class Track { public bool Made; public int Tee, Feet; public List<Vector2> Pts = new List<Vector2>(); }

        /// <summary>puttToMat: the path in mat inches from the hole (+x right, +y toward the tee), cut at the cup or the roll back.</summary>
        public static Track ToMat(object row)
        {
            int[][] bp = PuttAssessment.Bp(row); double[] hb = PuttAssessment.Hb(row);
            if (bp.Length < 2 || hb == null || hb[2] <= 0) return null;
            bool made = J.Int(row, "made") == 1;
            double hx = hb[0], hy = hb[1], hpr = hb[2] / 2, holeR = hpr / AppPutt.PxPerInAt(hpr, hpr);
            var t = new Track { Made = made, Tee = J.Int(row, "tee", 2), Feet = J.Int(row, "distance") };
            double minY = double.MaxValue;
            int[] last = null;
            foreach (int[] p in bp)
            {
                if (last != null && last[0] == p[0] && last[1] == p[1]) continue;
                if (p.Length > 2 && p[2] < 4) continue;
                last = p;
                double d = Math.Sqrt((p[0] - hx) * (p[0] - hx) + (p[1] - hy) * (p[1] - hy));
                double ppi = AppPutt.PxPerInAt(d, hpr);
                var pt = new Vector2((float)((p[0] - hx) / ppi), (float)((p[1] - hy) / ppi));
                if (made && d < hpr && t.Pts.Count > 0)
                {
                    Vector2 a = t.Pts[t.Pts.Count - 1], dv = pt - a;
                    double A = dv.sqrMagnitude, B = 2 * Vector2.Dot(a, dv), C = a.sqrMagnitude - holeR * holeR, disc = B * B - 4 * A * C;
                    double k = A > 0 && disc >= 0 ? Math.Max(0, Math.Min(1, (-B - Math.Sqrt(disc)) / (2 * A))) : 1;
                    t.Pts.Add(a + dv * (float)k);
                    break;
                }
                if (!made && pt.y > minY + 3) break;
                minY = Math.Min(minY, pt.y);
                t.Pts.Add(pt);
            }
            return t.Pts.Count >= 2 ? t : null;
        }

        public static Vector2? TeeAt(int feet, int tee) => feet < 1 || feet * 12 > 139 ? (Vector2?)null : new Vector2(((tee == 1 || tee == 3 ? tee : 2) - 2) * 8, feet * 12);
    }

    /// <summary>PuttMat: the mat, distance rows, tees, the hole, and each putt's line (green made, red missed).</summary>
    public sealed class MatPlot : VisualElement
    {
        readonly List<MatMath.Track> _tracks;
        static readonly Color MatFill = new Color32(0x10, 0x30, 0x1C, 255), MatEdge = new Color32(0x2A, 0x6A, 0x3F, 255), Ramp = new Color32(0x18, 0x43, 0x26, 255);

        public MatPlot(List<MatMath.Track> tracks, float height)
        {
            _tracks = tracks;
            style.height = height;
            style.width = height * (float)(MatMath.W / MatMath.H);
            generateVisualContent += Paint;
        }

        void Paint(MeshGenerationContext ctx)
        {
            float s = contentRect.height / (float)MatMath.H, cx = contentRect.width / 2, holeY = (float)MatMath.HoleFromTop * s;
            Vector2 P(Vector2 m) => new Vector2(cx + m.x * s, holeY + m.y * s);
            Painter2D g = ctx.painter2D;
            void Rect(float y0, float y1, Color fill) { g.fillColor = fill; g.BeginPath(); g.MoveTo(new Vector2(0, y0)); g.LineTo(new Vector2(contentRect.width, y0)); g.LineTo(new Vector2(contentRect.width, y1)); g.LineTo(new Vector2(0, y1)); g.ClosePath(); g.Fill(); }
            Rect(0, contentRect.height, MatFill);
            Rect(0, holeY + 8 * s, Ramp);
            g.lineWidth = 1;
            for (int ft = 3; ft <= 11; ft++) { g.strokeColor = new Color(1, 1, 1, 0.07f); g.BeginPath(); g.MoveTo(P(new Vector2(-12, ft * 12))); g.LineTo(P(new Vector2(12, ft * 12))); g.Stroke(); }
            g.strokeColor = MatEdge; g.BeginPath(); g.MoveTo(Vector2.zero); g.LineTo(new Vector2(contentRect.width, 0)); g.LineTo(new Vector2(contentRect.width, contentRect.height)); g.LineTo(new Vector2(0, contentRect.height)); g.ClosePath(); g.Stroke();
            foreach (int x in new[] { -8, 0, 8 }) for (int ft = 3; ft <= 11; ft++) Dot(g, P(new Vector2(x, ft * 12)), 2, new Color(1, 1, 1, 0.3f));
            Dot(g, P(Vector2.zero), (float)(4.25 / 2) * s, new Color32(0x02, 0x05, 0x03, 255));
            foreach (MatMath.Track t in _tracks)
            {
                Color c = t.Made ? (Color)Ui.Bright : Ui.Red;
                Vector2? tee = MatMath.TeeAt(t.Feet, t.Tee);
                if (tee.HasValue) { g.strokeColor = new Color(c.r, c.g, c.b, 0.5f); g.lineWidth = 1.2f; g.BeginPath(); g.MoveTo(P(tee.Value)); g.LineTo(P(t.Pts[0])); g.Stroke(); }
                g.strokeColor = c; g.lineWidth = 1.8f; g.BeginPath(); g.MoveTo(P(t.Pts[0]));
                for (int i = 1; i < t.Pts.Count; i++) g.LineTo(P(t.Pts[i]));
                g.Stroke();
                Vector2 end = P(t.Pts[t.Pts.Count - 1]);
                if (t.Made) Dot(g, end, 2, c);
                else { g.lineWidth = 1.6f; g.BeginPath(); g.MoveTo(end + new Vector2(-3, -3)); g.LineTo(end + new Vector2(3, 3)); g.MoveTo(end + new Vector2(3, -3)); g.LineTo(end + new Vector2(-3, 3)); g.Stroke(); }
            }
            foreach (MatMath.Track t in _tracks) if (MatMath.TeeAt(t.Feet, t.Tee) is Vector2 tee) Dot(g, P(tee), 2.6f, Color.white);
        }

        static void Dot(Painter2D g, Vector2 at, float r, Color c) { g.fillColor = c; g.BeginPath(); g.Arc(at, r, 0, 360); g.Fill(); }
    }

    public static class MatSummary
    {
        /// <summary>PuttMatSummary: the numbers on each side of the mat (stacked under it on a phone).</summary>
        public static VisualElement Build(List<object> putts, double score, float height)
        {
            var tracks = new List<MatMath.Track>();
            foreach (object p in putts) { MatMath.Track t = MatMath.ToMat(p); if (t != null) tracks.Add(t); }
            int made = putts.Count(p => J.Int(p, "made") == 1), total = putts.Count;
            VisualElement left = Ui.Box("", Ui.Text("MADE", "stat-label", Ui.Bold), Ui.Text(made + "/" + total, "display wrap", Ui.Bold), Green((total > 0 ? Mathf.RoundToInt(made * 100f / total) : 0) + "% made"));
            if (score > 0) left.Add(Ui.Text("Score " + score.ToString("0.#", CultureInfo.InvariantCulture), "muted", Ui.Regular));
            for (int ft = 3; ft <= 11; ft++)
            {
                var at = putts.FindAll(p => J.Int(p, "distance") == ft);
                if (at.Count == 0) continue;
                int m = at.Count(p => J.Int(p, "made") == 1);
                string pips = at.Count <= 4 ? string.Concat(at.Select(p => J.Int(p, "made") == 1 ? "●" : "✕")) : m + "/" + at.Count;
                left.Add(Ui.Text(pips + "  " + ft + " ft", "muted", Ui.Regular));
            }
            var rolls = new List<double>(); var speeds = new List<double>(); var lines = new List<double>(); var crossings = new List<double>();
            foreach (object p in putts)
            {
                int[][] bp = PuttAssessment.Bp(p); double[] hb = PuttAssessment.Hb(p);
                if (J.Get(p, "rollout") != null) rolls.Add(J.Num(p, "rollout"));
                if (bp.Length < 2 || hb == null) continue;
                AppPutt.Rollout a = AppPutt.AnalyzeRollout(bp, hb);
                if (J.Get(p, "rollout") == null && AppPutt.ProjectRollout(a, J.Int(p, "made") == 0 ? false : (bool?)null) is double r) rolls.Add(r);
                if (AppPutt.ArrivalSpeed(a) is double v) speeds.Add(v);
                if (AppPutt.StartLine(bp, hb, J.Num(p, "distance"), out double deg, out double cross)) { lines.Add(deg); crossings.Add(Math.Abs(cross)); }
            }
            double? Median(List<double> v) { if (v.Count == 0) return null; var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
            string pace = Median(rolls) is double mr ? AppPutt.FmtRollout(mr) : "No read";
            string line = Median(lines) is double ml ? (Math.Abs(ml) < 0.05 ? "On line" : Math.Abs(ml).ToString("0.0", CultureInfo.InvariantCulture) + "° " + (ml > 0 ? "right" : "left")) : "No read";
            VisualElement right = Ui.Box("", Ui.Text("PACE", "stat-label", Ui.Bold), Ui.Text(Cap(pace), "feed-name wrap", Ui.Bold), Ui.Text("typical finish", "muted", Ui.Regular),
                Ui.Text(Cap(PuttAssessment.Consistency(speeds).words), "feed-name", Ui.Bold).Margin(4), Ui.Text("putt to putt", "muted", Ui.Regular),
                Ui.Text("ACCURACY", "stat-label", Ui.Bold).Margin(10), Ui.Text(line, "feed-name", Ui.Bold), Ui.Text("start line bias", "muted", Ui.Regular),
                Ui.Text(crossings.Count > 0 ? crossings.Average().ToString("0.0", CultureInfo.InvariantCulture) + " in" : "No read", "feed-name", Ui.Bold).Margin(4), Ui.Text("avg from center", "muted", Ui.Regular),
                Ui.Text("MAKE % BY BREAK", "stat-label", Ui.Bold).Margin(10));
            string[] breaks = { "Left to right", "Straight", "Right to left" };
            for (int tee = 1; tee <= 3; tee++)
            {
                var at = putts.FindAll(p => J.Int(p, "tee", 2) == tee);
                if (at.Count == 0) continue;
                int pct = Mathf.RoundToInt(at.Count(p => J.Int(p, "made") == 1) * 100f / at.Count);
                right.Add(Ui.Text(breaks[tee - 1] + "  " + pct + "%", "muted", Ui.Regular));
                right.Add(Ui.Progress(pct / 100f));
            }
            left.style.width = right.style.width = 124;
            VisualElement mat = new MatPlot(tracks, height);
            mat.style.marginLeft = mat.style.marginRight = 12;
            return Ui.Box("row", left, mat, right);
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        static Label Green(string s) { Label l = Ui.Text(s, "feed-name", Ui.Bold); l.style.color = Ui.Bright; return l; }

        public static bool HasMatPutts(List<object> putts) => putts.Any(p => MatMath.ToMat(p) != null);

        /// <summary>The bottom sheet: "Putt scatter chart" over the screen.</summary>
        public static void Show(VisualElement over, List<object> putts, double score)
        {
            VisualElement dim = null;
            float h = Mathf.Max(400, Mathf.Min(640, (over.panel?.visualTree.layout.height ?? 800) - 220));
            ScrollView sideways = Ui.Scroll(true);
            sideways.Add(Build(putts, score, h));
            VisualElement card = Ui.Box("modal", Ui.Box("row", Ui.Text("Putt scatter chart", "h2 grow", Ui.Bold), Ui.Text("✕", "h2 wrap", Ui.Bold).OnTap(() => dim.RemoveFromHierarchy())), sideways.Margin(10));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
        }
    }

    // ── Vertex ───────────────────────────────────────────────────────────────

    public static class VertexSummary
    {
        public static VertexPutt Read(object row)
        {
            List<object> packets = J.List(J.Parse(J.Str(row, "sensorData")));
            if (packets.Count < 6) return null;
            var bytes = new byte[6][];
            for (int i = 0; i < 6; i++) bytes[i] = J.List(packets[i]).Select(x => (byte)Mathf.Clamp((int)Puttr.PuttrJson.Number(x, 0), 0, 255)).ToArray();
            try { return VertexPutt.Read(bytes); } catch (Exception) { return null; }
        }

        static int Bell(double v, double mean, double sd) => (int)Math.Round(Math.Exp(-0.5 * Math.Pow((v - mean) / sd, 2)) * 100);
        static Color Colour(int s) => s >= 75 ? (Color)new Color32(0x2D, 0x8C, 0x3C, 255) : s >= 40 ? Ui.Amber : Ui.Red;

        public static VisualElement Build(List<object> putts)
        {
            var rows = new List<(VertexPutt v, bool made)>();
            foreach (object p in putts) if (Read(p) is VertexPutt v) rows.Add((v, J.Int(p, "made") == 1));
            if (rows.Count < 2) return null;
            VisualElement card = Ui.Box("glass", Ui.Box("row", Ui.Text("Stroke Analysis", "feed-name grow", Ui.Bold), Ui.Text(rows.Count + " putts", "muted", Ui.Regular)));
            void Row(string label, string value, string sub, int score)
            {
                Label pct = Ui.Text(score + "%", "feed-name", Ui.Bold);
                pct.style.color = Colour(score);
                VisualElement bar = Ui.Progress(Mathf.Max(0.03f, score / 100f));
                bar.Q(className: "bar-fill").style.backgroundColor = Colour(score);
                card.Add(Ui.Box("", Ui.Box("row", Ui.Text(label, "sub grow", Ui.Regular), Ui.Text(value, "feed-name", Ui.Bold), Ui.Text("   " + sub + "   ", "muted", Ui.Regular), pct), bar).Margin(10));
            }
            card.Add(Form.Label("Accuracy"));
            void Accuracy(string label, Func<VertexPutt, double> f, double mean, double sd, string unit)
            {
                var vals = rows.Select(r => f(r.v)).ToList();
                Row(label, vals.Average().ToString("0.0", CultureInfo.InvariantCulture) + unit, "ideal " + mean.ToString("0.#", CultureInfo.InvariantCulture) + unit, (int)Math.Round(vals.Average(x => Bell(x, mean, sd))));
            }
            Accuracy("Face change", r => r.FaceChange, 0, 1.4, "°");
            Accuracy("Rhythm", r => r.Rhythm, 2.0, 0.7, "");
            Accuracy("Lie angle", r => r.Lie, 0, 2.0, "°");
            card.Add(Form.Label("Consistency"));
            void Consistent(string label, Func<VertexPutt, double> f, double sd, string unit)
            {
                var vals = rows.Select(r => f(r.v)).ToList();
                double mean = vals.Average(), pop = Math.Sqrt(vals.Average(x => (x - mean) * (x - mean)));
                Row(label, mean.ToString("0.00", CultureInfo.InvariantCulture) + " " + unit, "σ " + pop.ToString("0.00", CultureInfo.InvariantCulture), (int)Math.Round(vals.Average(x => Bell(x, mean, sd))));
            }
            Consistent("Tempo", r => r.Tempo, 5, "bpm");
            Consistent("Club head speed", r => r.Speed, 0.5, "m/s");
            Consistent("Backstroke time", r => r.BackTime, 0.05, "s");
            Consistent("Forward stroke time", r => r.ForwardTime, 0.05, "s");
            Consistent("Backstroke length", r => r.BackLength, 50, "mm");
            Consistent("Backstroke rotation", r => r.BackRotation, 1.0, "°");
            Consistent("Forward rotation", r => r.ForwardRotation, 1.0, "°");
            var makes = rows.Where(r => r.made).ToList(); var misses = rows.Where(r => !r.made).ToList();
            if (rows.Count >= 3 && makes.Count > 0 && misses.Count > 0)
            {
                var diffs = new List<(string label, double diff, string make, string miss)>();
                void Diff(string label, Func<VertexPutt, double> f, string fmt)
                {
                    double a = makes.Average(r => f(r.v)), b = misses.Average(r => f(r.v));
                    if (Math.Abs(a - b) > 0.1) diffs.Add((label, Math.Abs(a - b), a.ToString(fmt, CultureInfo.InvariantCulture), b.ToString(fmt, CultureInfo.InvariantCulture)));
                }
                Diff("Face Change", r => Math.Abs(r.FaceChange), "0.0");
                Diff("Rhythm", r => r.Rhythm, "0.00");
                Diff("Lie Angle", r => Math.Abs(r.Lie), "0.0");
                Diff("Speed (m/s)", r => r.Speed, "0.00");
                if (diffs.Count > 0)
                {
                    card.Add(Form.Label("Make vs Miss"));
                    foreach (var d in diffs.OrderByDescending(x => x.diff).Take(3)) card.Add(Ui.Box("row", Ui.Text(d.label, "sub grow", Ui.Regular), Ui.Text("Make " + d.make + "  ·  Miss " + d.miss, "muted", Ui.Regular)));
                    card.Add(Ui.Text(diffs.OrderByDescending(x => x.diff).First().label + " is your biggest make/miss differentiator", "muted wrap", Ui.Regular).Margin(8));
                }
            }
            return card;
        }
    }

    // ── Chipping ─────────────────────────────────────────────────────────────

    public static class Chipping
    {
        static readonly Dictionary<string, string> Zones = new Dictionary<string, string> { { "SHORT", "Short on mat" }, { "OVER", "Past the box" }, { "LEFT", "Wide left" }, { "RIGHT", "Wide right" }, { "SHORT_LEFT", "Short and left" }, { "SHORT_RIGHT", "Short and right" } };

        public static void Build(VisualElement body, List<object> chips)
        {
            int made = chips.Count(c => J.Int(c, "made") == 1), total = chips.Count;
            int manual = chips.Count(c => J.Str(c, "source") == "MANUAL");
            body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Text((total > 0 ? Mathf.RoundToInt(made * 100f / total) : 0) + "%", "code", Ui.Bold), Ui.Text(made + " of " + total + " chips made", "muted", Ui.Regular),
                Ui.Text((total - manual) + " in / around hole · " + manual + " short or off mat", "muted", Ui.Regular).Margin(6))));
            var missed = chips.Where(c => J.Int(c, "made") != 1 && J.Str(c, "missZone").Length > 0).GroupBy(c => J.Str(c, "missZone")).OrderByDescending(g => g.Count()).ToList();
            int misses = chips.Count(c => J.Int(c, "made") != 1);
            if (missed.Count > 0)
            {
                body.Add(Ui.Section("Miss zones"));
                VisualElement card = Ui.Box("glass set-card");
                foreach (var g in missed) card.Add(Ui.Box("set-row", Ui.Text(Zones.TryGetValue(g.Key, out string l) ? l : g.Key, "sub grow", Ui.Regular), Ui.Text(g.Count().ToString(), "feed-name", Ui.Bold), Ui.Text("  " + (misses > 0 ? Mathf.RoundToInt(g.Count() * 100f / misses) : 0) + "%", "muted", Ui.Regular)));
                body.Add(Ui.Box("pad", card));
            }
            body.Add(Ui.Section("By surface"));
            VisualElement surf = Ui.Box("glass set-card");
            foreach (string s in new[] { "FAIRWAY", "ROUGH" })
            {
                var on = chips.Where(c => J.Str(c, "surface").ToUpperInvariant() == s).ToList();
                if (on.Count == 0) continue;
                int m = on.Count(c => J.Int(c, "made") == 1);
                surf.Add(Ui.Box("set-row", Ui.Text(s == "FAIRWAY" ? "Fairway" : "Rough", "sub grow", Ui.Regular), Ui.Text(m + "/" + on.Count, "feed-name", Ui.Bold), Ui.Text("  " + Mathf.RoundToInt(m * 100f / on.Count) + "%", "muted", Ui.Regular)));
            }
            if (surf.childCount > 0) body.Add(Ui.Box("pad", surf));
            body.Add(Ui.Section("Chips"));
            if (total == 0) { body.Add(Page.Note("No chips recorded")); return; }
            VisualElement list = Ui.Box("glass set-card");
            for (int i = 0; i < chips.Count; i++)
            {
                object c = chips[i];
                bool m = J.Int(c, "made") == 1;
                Label r = Ui.Text(m ? "Made" : Zones.TryGetValue(J.Str(c, "missZone"), out string z) ? z : "Missed", "sub", Ui.Bold);
                r.style.color = m ? Ui.Bright : Ui.Red;
                list.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Ui.Box("grow", Ui.Text((J.Int(c, "distance") > 0 ? J.Int(c, "distance") + " ft · " : "") + (J.Str(c, "surface").Length > 0 ? J.Str(c, "surface").ToLowerInvariant() : "chip"), "feed-name", Ui.Medium), r)));
            }
            body.Add(Ui.Box("pad", list));
        }
    }

    // ── The top 100 ──────────────────────────────────────────────────────────

    public sealed class GameLeaderboardScreen : AppScreen
    {
        readonly App _app;
        readonly string _gameId;
        readonly object _game;
        readonly VisualElement _card;
        bool _recent = true;

        public GameLeaderboardScreen(App app, string gameId, string title, object game = null)
        {
            _app = app;
            _gameId = gameId;
            _game = game;
            Root = Page.Make(app, title, out VisualElement body);
            string label = J.Str(game, "leaderboardLabel");
            int distance = J.Int(game, "leaderboardDistance");
            body.Add(Ui.Box("pad", Ui.Text("Top 100", "muted", Ui.Medium), Ui.Text("Leaderboard" + (label.Length > 0 ? " (" + label + ")" : distance > 0 ? " (" + distance + " ft)" : ""), "h2 wrap", Ui.Bold)));
            body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "Recent", "All time" }, 0, i => { _recent = i == 0; Load(); })));
            _card = Ui.Box("glass set-card");
            body.Add(Ui.Box("pad", _card));
            Load();
        }

        void Load()
        {
            _card.Clear();
            _card.Add(Spinner.Small().Margin(12));
            var query = new List<string>();
            if (_recent) query.Add("hours=24");
            if (J.Str(_game, "leaderboardOrder") == "asc") query.Add("order=asc");
            string path = "/club/leaderboard/PUTTR/" + _gameId + "/" + J.Int(_game, "leaderboardDistance") + (query.Count > 0 ? "/?" + string.Join("&", query) : "");
            bool asked = _recent;
            App.Run(Api.Open(path, data => { if (asked == _recent) Fill(J.List(data)); }, _ => { _card.Clear(); _card.Add(Ui.Text("Could not load the leaderboard.", "sub", Ui.Regular)); }));
        }

        /// <summary>userName is publicPlayer as JSON; a sanitised copy is parsed (control and bidi characters stripped).</summary>
        public static object Player(object row)
        {
            string raw = J.Str(row, "userName");
            if (raw.Length == 0 || raw == "undefined") return null;
            raw = raw.Replace(' ', ' ');
            raw = System.Text.RegularExpressions.Regex.Replace(raw, "[\u0000-\u001F\u007F-\u009F​-‏‪-‮⁦-⁩﻿]", "");
            return J.Parse(raw) ?? new Dictionary<string, object> { { "displayName", raw } };
        }

        void Fill(List<object> rows) => Rows(_card, rows, 100, _recent);

        /// <summary>The board's rows: place (gold, silver, bronze), photo or brand-coloured letter, name, score; you highlighted.</summary>
        public static void Rows(VisualElement into, List<object> rows, int max, bool recent = false)
        {
            into.Clear();
            App app = App.I;
            string me = J.Str(app != null ? app.Profile : null, "userId", Auth.Uid);
            int place = 0;
            foreach (object row in rows)
            {
                object p = Player(row);
                if (p == null) continue;
                if (++place > max) break;
                string name = J.Str(p, "displayName", "Guest"), userId = J.Str(row, "userId");
                Label at = Ui.Text(place.ToString(), "board-place", Ui.Bold);
                if (place <= 3) at.style.color = place == 1 ? (Color)new Color32(0xF5, 0x9E, 0x0B, 255) : place == 2 ? (Color)new Color32(0xC0, 0xC0, 0xC0, 255) : (Color)new Color32(0xCD, 0x7F, 0x32, 255);
                double score = J.Num(row, "score");
                VisualElement r = Ui.Box("board-row", at, Ui.Avatar(name, J.Str(p, "photoURL")), Ui.Box("grow", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(J.Str(p, "homeTown"), "muted", Ui.Regular)),
                    Ui.Text(score % 1 == 0 ? score.ToString("0", CultureInfo.InvariantCulture) : score.ToString("0.0", CultureInfo.InvariantCulture), "feed-score", Ui.Bold));
                if (userId == me) r.style.backgroundColor = (Color)new Color32(0x16, 0x2E, 0x1B, 255);
                bool guest = string.IsNullOrEmpty(userId) || userId == "0";
                VisualElement over = into;
                r.OnTap(() => { if (app == null) return; if (guest) Dialog.Ask(over, "Guest Player", "This player was a guest and doesn't have a Puttr profile.", "OK", () => { }); else ProfileScreen.Open(app, userId, p); });
                into.Add(r);
            }
            if (place == 0) into.Add(Ui.Text(recent ? "No scores in the last 24 hours. Try All time." : "No scores yet. Yours could be the first.", "sub", Ui.Regular).Margin(12));
        }
    }
}
