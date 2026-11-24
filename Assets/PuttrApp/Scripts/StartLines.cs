// Start lines, as puttr-app's analysis/StartLineScatter draws them: a top-down schematic with the tee at the bottom
// and the hole at the top. Each putt is a ray from the tee at its measured start line, ending in a dot where it
// crossed the hole line (the fitted lateral crossing in inches, + right: RolloutFunctions getStartLine, ported in
// AppPutt.StartLine). The lateral axis is to scale against the hole circle, so a dot inside the circle started on a
// holeable line; the tee-to-hole length is schematic.
//
//   TeeCard        the Analysis "Start lines" card: one compact fan per tee (Left, Center, Right) over all distances,
//                  the median start line and its spread under each (TeeStartLines)
//   Spot           the spot modal's fan for one distance and tee, with "Left / Start line / Right" and the
//                  Tendency and Spread line under it (StartLineScatter, not compact)
//
// The app draws the rays at 22% opacity so density reads as intensity; this project is linear colour with solid
// colours only, so each ray is a solid colour mixed toward the chart's background instead.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class StartLineMath
    {
        public struct Point
        {
            public double CrossingIn, Deg;
            public bool Made;
        }

        /// <summary>Raw user_putts rows to plottable points (getStartLinePoints). Putts whose start-line fit fails are dropped.</summary>
        public static List<Point> Points(IEnumerable<object> rows)
        {
            var points = new List<Point>();
            if (rows == null) return points;
            foreach (object row in rows)
            {
                string path = J.Str(row, "ballPath"), hole = J.Str(row, "holeBounds");
                if (path.Length == 0 || hole.Length == 0) continue;
                int[][] bp;
                double[] hb;
                try { bp = AppPutt.Path(path); hb = AppPutt.Hole(hole); }
                catch (Exception) { continue; }
                if (hb == null) continue;
                if (!AppPutt.StartLine(bp, hb, J.Num(row, "distance"), out double deg, out double crossing)) continue;
                points.Add(new Point { CrossingIn = crossing, Deg = deg, Made = J.Int(row, "made") == 1 });
            }
            return points;
        }

        /// <summary>The rows putted from one tee (1 left, 2 center, 3 right), and optionally one distance.</summary>
        public static List<object> Rows(IEnumerable<object> rows, int tee, int feet = 0)
        {
            var list = new List<object>();
            if (rows == null) return list;
            foreach (object row in rows)
                if (J.Int(row, "tee") == tee && (feet == 0 || J.Int(row, "distance") == feet)) list.Add(row);
            return list;
        }

        /// <summary>The app's median: the upper middle of an even count.</summary>
        public static double? Median(List<Point> points, bool crossing)
        {
            var values = new List<double>();
            foreach (Point p in points) values.Add(crossing ? p.CrossingIn : p.Deg);
            return AppPutt.Median(values);
        }

        /// <summary>
        /// spreadDeg: the median absolute deviation of the start lines from their median, in degrees. Robust to the odd
        /// shank and comparable across tees (tighter is better). Null under two putts.
        /// </summary>
        public static double? Spread(List<Point> points)
        {
            if (points == null || points.Count < 2) return null;
            double med = Median(points, false) ?? 0;
            var off = new List<double>();
            foreach (Point p in points) off.Add(Math.Abs(p.Deg - med));
            return AppPutt.Median(off);
        }

        /// <summary>fmtStartLine: "0.4° left", "1.2° right", or "on line" when it rounds to 0.0.</summary>
        public static string Format(double deg)
        {
            double abs = Math.Abs(deg);
            if (abs < 0.05) return "on line";
            return abs.ToString("0.0", CultureInfo.InvariantCulture) + "° " + (deg > 0 ? "right" : "left");
        }

        public static string Spread1(double spread) => "±" + spread.ToString("0.0", CultureInfo.InvariantCulture) + "°";
    }

    /// <summary>One start-line fan, drawn with Painter2D. Fewer putts than minPutts shows "Not enough putts" instead.</summary>
    public sealed class StartLineFan : VisualElement
    {
        const float XMaxIn = 8f;   // lateral display range, inches either side of the centre
        static readonly Color Back = Ui.Surface2;
        static readonly Color Missed = Ui.Red;
        static readonly Color Surface3 = new Color32(48, 53, 58, 255);

        readonly List<StartLineMath.Point> _points;
        readonly bool _compact, _enough;
        readonly float _aspect;

        /// <param name="aspect">Height as a share of the width (0: the height comes from the style).</param>
        public StartLineFan(List<StartLineMath.Point> points, bool compact, int minPutts, float aspect = 0f)
        {
            _points = points ?? new List<StartLineMath.Point>();
            _compact = compact;
            _enough = _points.Count >= minPutts;
            _aspect = aspect;
            AddToClassList("sl-fan");
            style.backgroundColor = Back;
            if (!_enough)
            {
                Label none = Ui.Text("Not enough putts", compact ? "sl-none sl-none-sm" : "sl-none", Ui.Medium);
                Add(none);
            }
            generateVisualContent += Draw;
            if (aspect > 0f) RegisterCallback<GeometryChangedEvent>(_ => Fit());
        }

        void Fit()
        {
            float w = resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0f) return;
            float want = Mathf.Round(w * _aspect);
            if (Mathf.Abs(resolvedStyle.height - want) > 0.5f) style.height = want;
        }

        void Draw(MeshGenerationContext context)
        {
            if (!_enough) return;
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0f || h <= 0f) return;
            float X(double inches) => (Mathf.Clamp((float)inches, -XMaxIn, XMaxIn) + XMaxIn) / (2f * XMaxIn) * w;

            // The hole to the lateral scale; the crossing line runs through its centre, so a line that would drop lands inside it.
            float holeHalf = (float)(AppPutt.HoleDIn / 2);
            float holeR = (X(holeHalf) - X(-holeHalf)) / 2f;
            float holeY = holeR + (_compact ? 4f : 8f);
            float teeR = _compact ? 3.5f : 5f;
            float teeX = w / 2f, teeY = h - teeR - (_compact ? 4f : 8f);
            float dotR = _compact ? 2f : 3f;

            Painter2D p = context.painter2D;
            p.lineCap = LineCap.Butt;

            // The target line, tee to the hole's centre, dashed.
            p.strokeColor = Surface3;
            p.lineWidth = 1f;
            Vector2 from = new Vector2(teeX, teeY), to = new Vector2(w / 2f, holeY);
            float length = Vector2.Distance(from, to);
            Vector2 dir = length > 0f ? (to - from) / length : Vector2.zero;
            for (float s = 0f; s < length; s += 6f)
            {
                p.BeginPath();
                p.MoveTo(from + dir * s);
                p.LineTo(from + dir * Mathf.Min(length, s + 3f));
                p.Stroke();
            }

            // The hole.
            p.fillColor = Ui.Base;
            p.strokeColor = Ui.Faint;
            p.BeginPath(); p.Arc(new Vector2(w / 2f, holeY), holeR, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill(); p.Stroke();

            // The rays, misses first so the makes sit on top.
            Color madeRay = Color.Lerp(Back, Ui.Green, 0.45f), missRay = Color.Lerp(Back, Missed, 0.35f);
            p.lineWidth = 1f;
            for (int pass = 0; pass < 2; pass++)
                foreach (StartLineMath.Point pt in _points)
                {
                    if (pt.Made != (pass == 1)) continue;
                    p.strokeColor = pt.Made ? madeRay : missRay;
                    p.BeginPath(); p.MoveTo(new Vector2(teeX, teeY)); p.LineTo(new Vector2(X(pt.CrossingIn), holeY)); p.Stroke();
                }

            // Where each crossed the hole line.
            for (int pass = 0; pass < 2; pass++)
                foreach (StartLineMath.Point pt in _points)
                {
                    if (pt.Made != (pass == 1)) continue;
                    p.fillColor = pt.Made ? Ui.Green : Missed;
                    p.BeginPath(); p.Arc(new Vector2(X(pt.CrossingIn), holeY), dotR, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill();
                }

            // The median crossing, ringed.
            if (!_compact && StartLineMath.Median(_points, true) is double med)
            {
                p.strokeColor = Color.white;
                p.lineWidth = 1.5f;
                p.BeginPath(); p.Arc(new Vector2(X(med), holeY), dotR + 3f, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Stroke();
            }

            // The tee, last, so the rays start under its outline.
            p.fillColor = Ui.Base;
            p.strokeColor = Color.white;
            p.lineWidth = 1.5f;
            p.BeginPath(); p.Arc(new Vector2(teeX, teeY), teeR, Angle.Degrees(0), Angle.Degrees(360)); p.ClosePath(); p.Fill(); p.Stroke();
        }
    }

    public static class StartLines
    {
        static readonly string[] Tees = { "Left", "Center", "Right" };

        /// <summary>TeeStartLines: three compact fans side by side, the median start line and spread under each (4 putts or more).</summary>
        public static VisualElement TeeRow(List<object> rows)
        {
            VisualElement row = Ui.Box("sl-row");
            for (int tee = 1; tee <= 3; tee++)
            {
                List<StartLineMath.Point> points = StartLineMath.Points(StartLineMath.Rows(rows, tee));
                bool enough = points.Count >= 4;
                double? med = enough ? StartLineMath.Median(points, false) : null;
                double? spread = enough ? StartLineMath.Spread(points) : null;
                Label line = Ui.Text(med.HasValue ? StartLineMath.Format(med.Value) : "--", "sl-tee-line", Ui.Bold);
                if (!med.HasValue) line.AddToClassList("sl-faint");
                row.Add(Ui.Box(tee == 2 ? "sl-col sl-col-mid" : "sl-col",
                    Ui.Text(Tees[tee - 1], "sl-tee-name", Ui.Medium),
                    new StartLineFan(points, true, 4, 1.1f),
                    line,
                    Ui.Text(spread.HasValue ? StartLineMath.Spread1(spread.Value) + " spread" : " ", "sl-tee-spread", Ui.Medium)));
            }
            return row;
        }

        /// <summary>The Analysis card: "Start lines", the period under it, the Made / Missed key, and the three fans.</summary>
        public static VisualElement TeeCard(List<object> rows, string period)
        {
            VisualElement key = Ui.Box("row", Dot(Ui.Green), Ui.Text("Made", "sl-key", Ui.Medium), Dot(Ui.Red), Ui.Text("Missed", "sl-key sl-key-last", Ui.Medium));
            VisualElement head = Ui.Box("row sl-head", Ui.Box("grow", Ui.Text("Start lines", "g-name", Ui.Bold), Ui.Text(period, "muted", Ui.Regular)), key);
            return Ui.Box("glass", head, TeeRow(rows));
        }

        /// <summary>The spot modal's fan for one distance and tee (5 putts or more), with its axis words and the Tendency line.</summary>
        public static VisualElement Spot(List<object> rows, int feet, int tee)
        {
            List<StartLineMath.Point> points = StartLineMath.Points(StartLineMath.Rows(rows, tee, feet));
            VisualElement box = Ui.Box("sl-spot");
            var fan = new StartLineFan(points, false, 5);
            fan.AddToClassList("sl-fan-spot");
            box.Add(fan);
            if (points.Count < 5) return box;
            box.Add(Ui.Box("row sl-axis", Ui.Text("← Left", "sl-axis-t", Ui.Medium), Ui.Box("grow"), Ui.Text("Start line", "sl-axis-t", Ui.Medium), Ui.Box("grow"), Ui.Text("Right →", "sl-axis-t", Ui.Medium)));
            double med = StartLineMath.Median(points, false) ?? 0;
            double? spread = StartLineMath.Spread(points);
            box.Add(Ui.Text("Tendency: " + StartLineMath.Format(med) + (spread.HasValue ? " · Spread: " + StartLineMath.Spread1(spread.Value) : ""), "sl-tendency", Ui.Medium));
            return box;
        }

        static VisualElement Dot(Color colour)
        {
            VisualElement dot = Ui.Box("sl-dot");
            dot.style.backgroundColor = colour;
            return dot;
        }
    }
}
