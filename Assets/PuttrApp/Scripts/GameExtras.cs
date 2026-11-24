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
            VisualElement left = Ui.Box("", Ui.Text("MADE", "caps", Ui.Bold), Ui.Num(made + "/" + total), Green((total > 0 ? Mathf.RoundToInt(made * 100f / total) : 0) + "% made"));
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
            VisualElement right = Ui.Box("", Ui.Text("PACE", "caps", Ui.Bold), Ui.Text(Cap(pace), "g-name wrap", Ui.Bold), Ui.Text("typical finish", "muted", Ui.Regular),
                Ui.Text(Cap(PuttAssessment.Consistency(speeds).words), "g-name", Ui.Bold).Margin(4), Ui.Text("putt to putt", "muted", Ui.Regular),
                Ui.Text("ACCURACY", "caps", Ui.Bold).Margin(12), Ui.Text(line, "g-name", Ui.Bold), Ui.Text("start line bias", "muted", Ui.Regular),
                Ui.Text(crossings.Count > 0 ? crossings.Average().ToString("0.0", CultureInfo.InvariantCulture) + " in" : "No read", "g-name", Ui.Bold).Margin(4), Ui.Text("avg from center", "muted", Ui.Regular),
                Ui.Text("MAKE % BY BREAK", "caps", Ui.Bold).Margin(12));
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
        static Label Green(string s) { Label l = Ui.Text(s, "g-name", Ui.Bold); l.style.color = Ui.Bright; return l; }

        public static bool HasMatPutts(List<object> putts) => putts.Any(p => MatMath.ToMat(p) != null);

        /// <summary>The bottom sheet: "Putt scatter chart" over the screen.</summary>
        public static void Show(VisualElement over, List<object> putts, double score)
        {
            VisualElement dim = null;
            float h = Mathf.Max(400, Mathf.Min(640, (over.panel?.visualTree.layout.height ?? 800) - 220));
            ScrollView sideways = Ui.Scroll(true);
            sideways.Add(Build(putts, score, h));
            VisualElement card = Ui.Box("modal", Ui.Box("row", Ui.Title("Putt scatter chart", "title-20 grow"), Ui.IconSquare("X", () => dim.RemoveFromHierarchy())), sideways.Margin(12));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
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
        static Color Colour(int s) => s >= 75 ? (Color)Ui.Green : s >= 40 ? Ui.Amber : Ui.Red;
        // The same bands for the number beside the bar: bright green, since brand green is a fill and too dim to read as text.
        static Color TextColour(int s) => s >= 75 ? (Color)Ui.Bright : s >= 40 ? Ui.Amber : Ui.Red;

        public static VisualElement Build(List<object> putts)
        {
            var rows = new List<(VertexPutt v, bool made)>();
            foreach (object p in putts) if (Read(p) is VertexPutt v) rows.Add((v, J.Int(p, "made") == 1));
            if (rows.Count < 2) return null;
            VisualElement card = Ui.Box("glass", Ui.Box("row", Ui.Text("Stroke Analysis", "g-name grow", Ui.Bold), Ui.Text(rows.Count + " putts", "muted", Ui.Regular)));
            void Row(string label, string value, string sub, int score)
            {
                Label pct = Ui.Text(score + "%", "g-name", Ui.Bold);
                pct.style.color = TextColour(score);
                VisualElement bar = Ui.Progress(Mathf.Max(0.03f, score / 100f));
                bar.Q(className: "bar-fill").style.backgroundColor = Colour(score);
                card.Add(Ui.Box("", Ui.Box("row", Ui.Text(label, "sub grow", Ui.Regular), Ui.Text(value, "g-name", Ui.Bold), Ui.Text("   " + sub + "   ", "muted", Ui.Regular), pct), bar).Margin(12));
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
            body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Num((total > 0 ? Mathf.RoundToInt(made * 100f / total) : 0) + "%", "num-44 center"), Ui.Text(made + " of " + total + " chips made", "muted", Ui.Regular),
                Ui.Text((total - manual) + " in / around hole · " + manual + " short or off mat", "muted", Ui.Regular).Margin(8))));
            var missed = chips.Where(c => J.Int(c, "made") != 1 && J.Str(c, "missZone").Length > 0).GroupBy(c => J.Str(c, "missZone")).OrderByDescending(g => g.Count()).ToList();
            int misses = chips.Count(c => J.Int(c, "made") != 1);
            if (missed.Count > 0)
            {
                body.Add(Ui.Section("Miss zones"));
                VisualElement card = Ui.Box("glass set-card");
                foreach (var g in missed) card.Add(Ui.Box("set-row", Ui.Text(Zones.TryGetValue(g.Key, out string l) ? l : g.Key, "sub grow", Ui.Regular), Ui.Text(g.Count().ToString(), "g-name", Ui.Bold), Ui.Text("  " + (misses > 0 ? Mathf.RoundToInt(g.Count() * 100f / misses) : 0) + "%", "muted", Ui.Regular)));
                body.Add(Ui.Box("pad", card));
            }
            body.Add(Ui.Section("By surface"));
            VisualElement surf = Ui.Box("glass set-card");
            foreach (string s in new[] { "FAIRWAY", "ROUGH" })
            {
                var on = chips.Where(c => J.Str(c, "surface").ToUpperInvariant() == s).ToList();
                if (on.Count == 0) continue;
                int m = on.Count(c => J.Int(c, "made") == 1);
                surf.Add(Ui.Box("set-row", Ui.Text(s == "FAIRWAY" ? "Fairway" : "Rough", "sub grow", Ui.Regular), Ui.Text(m + "/" + on.Count, "g-name", Ui.Bold), Ui.Text("  " + Mathf.RoundToInt(m * 100f / on.Count) + "%", "muted", Ui.Regular)));
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
                list.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Ui.Box("grow", Ui.Text((J.Int(c, "distance") > 0 ? J.Int(c, "distance") + " ft · " : "") + (J.Str(c, "surface").Length > 0 ? J.Str(c, "surface").ToLowerInvariant() : "chip"), "g-name", Ui.Medium), r)));
            }
            body.Add(Ui.Box("pad", list));
        }
    }

    // ── One putt: its card, its detail and its ball path (common/PuttCard.js, PuttReplay.js) ──

    /// <summary>
    /// Everything a putt's card shows, worked out once as computePuttDisplay does, so the card, its detail and the
    /// Versus putt by putt all read the same numbers. The grade is the app's: AppPutt.PuttScore (getPuttScoreRollout,
    /// 50 for the entry, 50 for the pace) banded by AppPutt.Grade (getPuttGrade).
    /// </summary>
    public sealed class PuttDisplay
    {
        public static readonly Color Ideal = Ui.Green, Short = Ui.Blue, Firm = Ui.Amber;

        /// <summary>A fill's colour as text or an icon: brand green is too dim to read, so it becomes the bright green.</summary>
        public static Color AsText(Color fill) => fill == Ideal ? Ui.Bright : fill;

        public int[][] Bp;
        public double[] Hb;
        public bool Made, Tracked;
        public int Distance, Tee, RoundedScore;
        public double? Rollout;
        public double AngDiff, Score;
        public string PaceText, Grade, BreakLabel, ResultText;
        public Color GradeColour, ResultColour, PaceColour, Accent;
        public float PacePct;

        public static PuttDisplay Of(object putt)
        {
            var d = new PuttDisplay { Bp = PuttAssessment.Bp(putt), Hb = PuttAssessment.Hb(putt) };
            d.Made = J.Int(putt, "made") == 1;
            d.Tracked = d.Bp.Length >= 2 && d.Hb != null;
            d.Distance = J.Int(putt, "distance");
            d.Tee = J.Int(putt, "tee", 2);
            // Pace: the server's stored rollout (a DECIMAL, so it may come as a string), else projected from the path.
            object stored = J.Get(putt, "rollout");
            bool hasStored = stored is double || (stored is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
            d.Rollout = hasStored ? J.Num(putt, "rollout")
                : d.Tracked ? AppPutt.ProjectRollout(AppPutt.AnalyzeRollout(d.Bp, d.Hb), d.Made ? (bool?)null : false) : null;
            d.PaceText = AppPutt.FmtRollout(d.Rollout) ?? "no read";
            // The entry against the tee's line (getHoleEntry - getTeeEntry). A putt the camera never saw cross the lip
            // reads 0 for its entry, as the app's [0,0,0,0] does.
            double entry = d.Bp.Length > 0 && d.Hb != null ? AppPutt.HoleEntry(d.Bp, d.Hb, out _) : 0;
            d.AngDiff = entry - AppPutt.TeeEntry(d.Distance, d.Tee);
            d.Score = AppPutt.PuttScore(d.AngDiff, d.Made, d.Rollout);
            d.Grade = AppPutt.Grade(d.Score);
            d.RoundedScore = (int)AppPutt.JsRound(d.Score);
            d.BreakLabel = d.Tee == 1 ? "Left to right" : d.Tee == 3 ? "Right to left" : "Straight";
            if (d.Made)
            {
                string zone = AppPutt.EntryDescription(d.AngDiff);
                d.ResultText = zone.Substring(0, 1) + zone.Substring(1).ToLowerInvariant();
                d.ResultColour = Ui.Bright;
            }
            else
            {
                d.ResultText = "Missed " + (d.Tracked ? AppPutt.MissResult(d.Bp, d.Hb) : "LEFT").ToLowerInvariant();
                d.ResultColour = Ui.Red;
            }
            d.GradeColour = d.RoundedScore < 50 ? Ui.Red : d.RoundedScore < 70 ? Color.white : Ui.Bright;
            // The pace bar runs from 15 in short to 30 in past: ideal (-2 to 12 in) green, short blue, firm orange.
            bool ideal = d.Rollout.HasValue && d.Rollout.Value >= -2 && d.Rollout.Value <= 12;
            d.PaceColour = ideal ? Ideal : d.Rollout.HasValue && d.Rollout.Value < -2 ? Short : Firm;
            d.PacePct = d.Rollout.HasValue ? Mathf.Clamp((float)((d.Rollout.Value + 15) / 45), 0.05f, 1f) : 0.05f;
            d.Accent = d.Made ? Ideal : Ui.Red;
            return d;
        }

        /// <summary>"Center cut · 1.0°" for a make, "Missed right" for a miss.</summary>
        public string EntryLine => ResultText + (Made ? " · " + Math.Abs(AngDiff).ToString("0.0", CultureInfo.InvariantCulture) + "°" : "");

        /// <summary>The detail's line under the entry: dead center within 5 degrees, else how far and which side.</summary>
        public string EntrySub => Math.Abs(AngDiff) < 5 ? "Dead center" : Math.Abs(AngDiff).ToString("0.0", CultureInfo.InvariantCulture) + "° " + (AngDiff > 0 ? "right" : "left") + " of center";
    }

    public static class PuttCards
    {
        static StyleSheet _sheet;
        /// <summary>Profile.uss holds the putt card's styles; a screen that shows the cards adds it to its root.</summary>
        public static StyleSheet Sheet => _sheet != null ? _sheet : _sheet = Resources.Load<StyleSheet>("PuttrApp/Profile");

        static Label Coloured(string text, string classes, Font font, Color colour)
        {
            Label l = Ui.Text(text, classes, font);
            l.style.color = colour;
            return l;
        }

        static VisualElement Badge(int index, Color colour, string classes)
        {
            VisualElement b = Ui.Box(classes, Ui.Text((index + 1).ToString(), "pc-num-t", Ui.Bold));
            b.style.backgroundColor = colour;
            return b;
        }

        /// <summary>
        /// The putt's card: the made or missed edge, the grade over its score, the number badge with the distance and
        /// break, the pace bar with its value, and the entry line. A tap opens the detail (or onTap when given).
        /// compact: the narrower card two of which sit side by side (Versus putt by putt).
        /// </summary>
        public static VisualElement Card(object putt, int index, VisualElement over, Action onTap = null, bool compact = false, Action watch = null)
        {
            PuttDisplay d = PuttDisplay.Of(putt);
            VisualElement edge = Ui.Box("pc-edge");
            edge.style.backgroundColor = d.Accent;
            // The score under the grade in the grade's colour, dimmed (solid, as the app's 0.6 opacity over the card).
            Color dim = Color.Lerp(Ui.Surface, d.GradeColour, 0.6f);
            VisualElement grade = Ui.Box("pc-grade", Coloured(d.Grade, "pc-grade-t", Ui.Bold, d.GradeColour), Coloured(d.RoundedScore.ToString(), "pc-score-t", Ui.Regular, dim));

            VisualElement head = Ui.Box("pc-head", Badge(index, d.Accent, "pc-num"), Ui.Text(d.Distance + "ft", "pc-dist", Ui.Bold), Ui.Text(d.BreakLabel, "pc-break", Ui.Regular));
            VisualElement fill = Ui.Box("pc-bar-fill");
            fill.style.backgroundColor = d.PaceColour;
            fill.style.width = Length.Percent(d.PacePct * 100f);
            VisualElement pace = Ui.Box("pc-pace",
                Ui.Box("pc-pace-row", Ui.Text("Pace", "pc-pace-l grow", Ui.Medium), Coloured(d.PaceText, "pc-pace-v", Ui.Bold, PuttDisplay.AsText(d.PaceColour))),
                Ui.Box("pc-bar", fill));
            VisualElement mark;
            if (d.Made) { mark = Ui.Box("pc-dot"); mark.style.backgroundColor = d.ResultColour; }
            else mark = Ui.Icon("X", compact ? 8 : 10, Ui.Red);
            mark.AddToClassList("pc-mark");
            VisualElement result = Ui.Box("pc-result", mark, Coloured(d.EntryLine, "pc-result-t", Ui.Medium, d.ResultColour));

            VisualElement card = Ui.Box(compact ? "pc-card pc-compact" : "pc-card", edge, grade, Ui.Box("pc-main", head, pace, result));
            card.OnTap(() => { if (onTap != null) onTap(); else Open(over, putt, index, watch); });
            return card;
        }

        /// <summary>The detail over the screen (PuttCard's modal): the ball path replayed on the green, then the grade, Pace and Entry.</summary>
        public static void Open(VisualElement over, object putt, int index, Action watch = null)
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal pc-modal", Detail(putt, index));
            if (watch != null) card.Add(Ui.Button("Watch this putt", () => { dim.RemoveFromHierarchy(); watch(); }, "btn btn-ghost").Margin(16));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            if (Sheet != null) dim.styleSheets.Add(Sheet);
            App.Layer(over).Add(dim);
        }

        /// <summary>PuttResultDetail: the path (replayed once, with a button to play it again) and the stats panel.</summary>
        public static VisualElement Detail(object putt, int index)
        {
            PuttDisplay d = PuttDisplay.Of(putt);
            var path = new PuttPathView(d);
            // The replay button (the app's ArrowClockwise; the Arcade's icon set has Play for it).
            VisualElement again = Ui.Box("pc-again", Ui.Icon("Play", 13, Ui.Soft));
            again.style.display = DisplayStyle.None;
            again.OnTap(() => { again.style.display = DisplayStyle.None; path.Replay(); });
            path.Done = () => again.style.display = DisplayStyle.Flex;
            VisualElement picture = Ui.Box("pc-picture", path, again);

            VisualElement head = Ui.Box("pc-detail-head", Badge(index, d.Accent, "pc-num pc-num-big"), Ui.Text(d.Distance + "ft · " + d.BreakLabel, "pc-detail-title grow", Ui.Bold), Coloured(d.Grade, "pc-detail-grade", Ui.Bold, d.GradeColour));
            VisualElement paceTile = Ui.Box("pc-tile pc-tile-left grow", Ui.Box("pc-tile-head", Ui.Icon("Lightning", 12, PuttDisplay.AsText(d.PaceColour)), Ui.Text("Pace", "pc-tile-l", Ui.Medium)), Coloured(d.PaceText, "pc-tile-v", Ui.Bold, PuttDisplay.AsText(d.PaceColour)));
            VisualElement entryTile = Ui.Box("pc-tile grow", Ui.Box("pc-tile-head", Ui.Icon("Target", 12, d.ResultColour), Ui.Text("Entry", "pc-tile-l", Ui.Medium)), Coloured(d.ResultText, "pc-tile-v", Ui.Bold, d.ResultColour));
            if (d.Made) entryTile.Add(Ui.Text(d.EntrySub, "pc-tile-sub", Ui.Regular));
            return Ui.Box("pc-detail", picture, head, Ui.Box("pc-tiles", paceTile, entryTile));
        }
    }

    /// <summary>
    /// The ball's path from above on a dark green grid (PuttCard's EnhancedPath, in computePathGeometry's frame: the
    /// camera's 475 x 350, the hole moved to x 320, the picture starting 82.5 in from the left). On opening, the ball
    /// rolls the recorded path at the camera's own pace (PuttReplay), then the still view settles in. The project draws
    /// in linear colour, so every see-through colour of the app is mixed with the green here and drawn solid.
    /// </summary>
    public sealed class PuttPathView : VisualElement
    {
        static readonly Color Bg = new Color32(10, 30, 15, 255);
        readonly PuttDisplay _d;
        float _started, _duration;
        bool _playing;
        public Action Done;

        public PuttPathView(PuttDisplay d)
        {
            _d = d;
            AddToClassList("pc-path");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            // The picture keeps the camera's shape: its height follows its width.
            RegisterCallback<GeometryChangedEvent>(e =>
            {
                float h = e.newRect.width * 350f / 475f;
                if (Mathf.Abs(resolvedStyle.height - h) > 0.5f) style.height = h;
            });
            RegisterCallback<AttachToPanelEvent>(_ => Replay());
            schedule.Execute(Tick).Every(16);
        }

        static Color Mix(Color over, float alpha) => Color.Lerp(Bg, over, alpha);

        /// <summary>Rolls the putt again from the start.</summary>
        public void Replay()
        {
            int n = _d.Bp.Length;
            if (n < 2) { _playing = false; MarkDirtyRepaint(); Done?.Invoke(); return; }
            _duration = Mathf.Min(1.8f, Mathf.Max(0.7f, n * 0.03f));
            _started = Time.realtimeSinceStartup;
            _playing = true;
            Perf.Wake(_duration + 0.5f);
            MarkDirtyRepaint();
        }

        void Tick()
        {
            if (!_playing) return;
            Perf.Wake(0.3f);
            if (Time.realtimeSinceStartup - _started >= _duration) { _playing = false; Done?.Invoke(); }
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            float gw = contentRect.width, gh = contentRect.height;
            if (gw <= 0 || gh <= 0) return;
            Painter2D p = context.painter2D;
            float mult = gw / 475f, xAdjust = 82.5f * mult;
            double holeXCam = 320, holeYCam = 100, holeRCam = 60, shift = 0;
            if (_d.Hb != null) { shift = 320 - _d.Hb[0]; holeYCam = _d.Hb[1]; holeRCam = _d.Hb[2] / 2; }
            var hole = new Vector2((float)holeXCam * mult - xAdjust, (float)holeYCam * mult);
            float holeRad = (float)holeRCam * mult;
            var pts = new Vector2[_d.Bp.Length];
            var radii = new float[_d.Bp.Length];
            for (int i = 0; i < _d.Bp.Length; i++)
            {
                int[] b = _d.Bp[i];
                pts[i] = new Vector2((float)((b[0] + shift) * mult) - xAdjust, b[1] * mult);
                radii[i] = (b.Length > 2 ? b[2] : 10) * mult;
            }
            Color glow = _d.Made ? PuttDisplay.Ideal : Ui.Red;

            p.fillColor = Bg;
            p.BeginPath(); p.MoveTo(Vector2.zero); p.LineTo(new Vector2(gw, 0)); p.LineTo(new Vector2(gw, gh)); p.LineTo(new Vector2(0, gh)); p.ClosePath(); p.Fill();
            p.strokeColor = Mix(Color.white, 0.04f);
            p.lineWidth = 1f;
            foreach (float k in new[] { 0.25f, 0.5f, 0.75f })
            {
                p.BeginPath(); p.MoveTo(new Vector2(0, gh * k)); p.LineTo(new Vector2(gw, gh * k)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(gw * k, 0)); p.LineTo(new Vector2(gw * k, gh)); p.Stroke();
            }
            p.strokeColor = Mix(Color.white, 0.1f);
            p.lineWidth = 1f;
            p.BeginPath(); p.Arc(hole, holeRad + 2, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.fillColor = Color.Lerp(Mix(glow, 0.3f), Color.black, 0.6f);
            Circle(p, hole, holeRad);
            p.strokeColor = Mix(Color.white, 0.25f);
            p.lineWidth = 1.5f;
            p.BeginPath(); p.Arc(hole, holeRad, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();

            int n = pts.Length;
            if (n == 0) return;
            // How far the ball has rolled: the whole path once the replay is over.
            float progress = _playing ? Mathf.Clamp01((Time.realtimeSinceStartup - _started) / Mathf.Max(0.01f, _duration)) : 1f;
            float at = progress * (n - 1);
            int last = Mathf.Min(Mathf.FloorToInt(at), n - 1);
            float frac = at - last;
            Vector2 ball = last < n - 1 ? Vector2.Lerp(pts[last], pts[last + 1], frac) : pts[n - 1];

            // The path, one solid colour (no gradient: docs/design-guidelines.md).
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            for (int i = 1; i <= last + (frac > 0 && last < n - 1 ? 1 : 0); i++)
            {
                Vector2 a = pts[i - 1], b = i <= last ? pts[i] : ball;
                p.strokeColor = Ui.Soft;
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
            }

            if (_playing)
            {
                float r = last < n - 1 ? Mathf.Lerp(radii[last], radii[last + 1], frac) : radii[n - 1];
                p.fillColor = Color.white;
                Circle(p, ball, Mathf.Max(3.5f, r * 0.45f));
                return;
            }

            // The still view: trail dots, the start, and where it went in (or finished).
            for (int i = 3, k = 0; i < n - 1; i += 3, k++)
            {
                p.fillColor = Mix(Color.white, 0.1f + k / (n / 3f) * 0.4f);
                Circle(p, pts[i], 2f);
            }
            p.fillColor = Mix(Color.white, 0.15f); Circle(p, pts[0], 6f);
            p.fillColor = Mix(Color.white, 0.5f); Circle(p, pts[0], 3f);
            if (_d.Made)
            {
                Vector2? edge = EntryEdge(pts, hole, holeRad);
                if (edge.HasValue)
                {
                    p.fillColor = PuttDisplay.Ideal;
                    Circle(p, edge.Value, 5f);
                }
            }
            else
            {
                Vector2 end = pts[n - 1];
                p.fillColor = Mix(Ui.Red, 0.8f);
                Circle(p, end, 5f);
                p.strokeColor = Ui.Red;
                p.lineWidth = 2f;
                p.BeginPath(); p.MoveTo(end + new Vector2(-3, -3)); p.LineTo(end + new Vector2(3, 3)); p.Stroke();
                p.BeginPath(); p.MoveTo(end + new Vector2(3, -3)); p.LineTo(end + new Vector2(-3, 3)); p.Stroke();
            }
        }

        static void Circle(Painter2D p, Vector2 c, float r)
        {
            if (r <= 0) return;
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
        }

        /// <summary>Where the path first crosses the hole's edge, between the last point outside and the first inside.</summary>
        static Vector2? EntryEdge(Vector2[] pts, Vector2 hole, float r)
        {
            for (int i = 1; i < pts.Length; i++)
            {
                if ((pts[i] - hole).magnitude > r) continue;
                Vector2 prev = pts[i - 1], v = pts[i] - prev, o = prev - hole;
                float a = Vector2.Dot(v, v), b = 2 * Vector2.Dot(o, v), c = Vector2.Dot(o, o) - r * r;
                float disc = b * b - 4 * a * c;
                if (disc >= 0 && a > 0)
                {
                    float t = (-b - Mathf.Sqrt(disc)) / (2 * a);
                    return t >= 0 && t <= 1 ? prev + v * t : pts[i];
                }
                return pts[i];
            }
            return null;
        }
    }
}
