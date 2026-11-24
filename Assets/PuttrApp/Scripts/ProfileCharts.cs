// The Profile tab's two drawn cards, made to look as puttr-app's do: the month calendar with
// the days putted on filled in, and putts by month as a curved line with a dot a month; and another Player's
// Putt Quality Score with its trend (members/PuttScore.js: PqsModel, PqsCard, PqsChart).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>
    /// One month at a time, Sunday first, back as far as 90 days: a day with putts is a filled
    /// green circle, today is ringed. Tapping a filled day says how many putts.
    /// </summary>
    public sealed class CalendarView : VisualElement
    {
        readonly Dictionary<string, int> _days;
        readonly DateTime _today, _earliest;
        DateTime _month;
        readonly Label _picked;

        public CalendarView(Dictionary<string, int> days, DateTime today)
        {
            _days = days;
            _today = today.Date;
            _earliest = _today.AddDays(-90);
            _month = new DateTime(_today.Year, _today.Month, 1);
            _picked = Ui.Text("", "muted center", Ui.Medium);
            AddToClassList("calendar");
            Draw();
        }

        void Draw()
        {
            Clear();
            DateTime first = new DateTime(_earliest.Year, _earliest.Month, 1), last = new DateTime(_today.Year, _today.Month, 1);
            Label back = Ui.Text("‹", "calendar-arrow", Ui.Bold), on = Ui.Text("›", "calendar-arrow", Ui.Bold);
            if (_month > first) back.OnTap(() => { _month = _month.AddMonths(-1); Draw(); }); else back.style.opacity = 0.2f;
            if (_month < last) on.OnTap(() => { _month = _month.AddMonths(1); Draw(); }); else on.style.opacity = 0.2f;
            Add(Ui.Box("row calendar-head", back, Ui.Text(_month.ToString("MMMM", CultureInfo.InvariantCulture), "calendar-month grow", Ui.Bold), on));

            VisualElement names = Ui.Box("row");
            foreach (string name in new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }) names.Add(Ui.Box("calendar-cell", Ui.Text(name, "calendar-name", Ui.Medium)));
            Add(names);

            VisualElement week = Ui.Box("row");
            for (int blank = 0; blank < (int)_month.DayOfWeek; blank++) week.Add(Ui.Box("calendar-cell"));
            int length = DateTime.DaysInMonth(_month.Year, _month.Month);
            for (int day = 1; day <= length; day++)
            {
                DateTime date = new DateTime(_month.Year, _month.Month, day);
                _days.TryGetValue(date.ToString("yyyy-MM-dd"), out int putts);
                bool open = date >= _earliest && date <= _today;
                Label number = Ui.Text(day.ToString(), "calendar-number", Ui.Medium);
                VisualElement dot = Ui.Box(putts > 0 && open ? "calendar-day calendar-on" : date == _today ? "calendar-day calendar-today" : "calendar-day", number);
                if (!open) number.style.opacity = 0.2f;
                else if (date == _today && putts == 0) number.style.color = Ui.Bright;
                if (putts > 0 && open)
                {
                    int count = putts;
                    dot.OnTap(() => _picked.text = ProfileMath.Number(count) + (count == 1 ? " putt" : " putts") + " on " + date.ToString("MMM d", CultureInfo.InvariantCulture));
                }
                week.Add(Ui.Box("calendar-cell", dot));
                if (week.childCount == 7) { Add(week); week = Ui.Box("row"); }
            }
            if (week.childCount > 0)
            {
                while (week.childCount < 7) week.Add(Ui.Box("calendar-cell"));
                Add(week);
            }
            _picked.text = "";
            Add(_picked);
        }
    }

    /// <summary>A curved line through a value a month, from zero, with faint grid lines and a dot on each point.</summary>
    public sealed class LineChart : VisualElement
    {
        readonly List<(string label, int value)> _points;
        const float Left = 34f, Bottom = 22f, Top = 10f, Right = 12f;

        public LineChart(List<(string label, int value)> points)
        {
            _points = points;
            AddToClassList("line-chart");
            generateVisualContent += Draw;
            // The words are labels over the drawing: their places are set once the size is known.
            RegisterCallback<GeometryChangedEvent>(_ => Labels());
        }

        int Ceiling()
        {
            int most = 1;
            foreach ((string label, int value) point in _points) most = Mathf.Max(most, point.value);
            // A round number above the tallest, so the four grid lines read as round numbers too.
            int step = most <= 40 ? 10 : most <= 200 ? 50 : most <= 400 ? 100 : most <= 2000 ? 500 : 1000;
            return Mathf.CeilToInt(most / (float)(step * 4)) * step * 4;
        }

        Vector2 At(int index, float width, float height, int ceiling)
        {
            float x = Left + (width - Left - Right) * (_points.Count > 1 ? index / (float)(_points.Count - 1) : 0.5f);
            float y = Top + (height - Top - Bottom) * (1f - _points[index].value / (float)ceiling);
            return new Vector2(x, y);
        }

        void Labels()
        {
            float width = contentRect.width, height = contentRect.height;
            if (width <= 0f || height <= 0f || _points.Count == 0) return;
            Clear();
            int ceiling = Ceiling();
            for (int line = 0; line <= 4; line++)
            {
                Label value = Ui.Text(ProfileMath.Number(ceiling * line / 4), "chart-label", Ui.Regular);
                value.style.left = 0;
                value.style.width = Left - 6f;
                value.style.top = Top + (height - Top - Bottom) * (1f - line / 4f) - 7f;
                value.style.unityTextAlign = TextAnchor.MiddleRight;
                Add(value);
            }
            for (int i = 0; i < _points.Count; i++)
            {
                Label month = Ui.Text(_points[i].label, "chart-label", Ui.Regular);
                month.style.left = At(i, width, height, ceiling).x - 20f;
                month.style.width = 40f;
                month.style.top = height - Bottom + 6f;
                month.style.unityTextAlign = TextAnchor.MiddleCenter;
                Add(month);
            }
        }

        void Draw(MeshGenerationContext context)
        {
            float width = contentRect.width, height = contentRect.height;
            if (width <= 0f || height <= 0f || _points.Count == 0) return;
            int ceiling = Ceiling();
            Painter2D p = context.painter2D;

            p.strokeColor = Ui.Line;
            p.lineWidth = 1f;
            for (int line = 0; line <= 4; line++)
            {
                float y = Top + (height - Top - Bottom) * (1f - line / 4f);
                p.BeginPath(); p.MoveTo(new Vector2(Left, y)); p.LineTo(new Vector2(width - Right, y)); p.Stroke();
            }
            for (int i = 0; i < _points.Count; i++)
            {
                float x = At(i, width, height, ceiling).x;
                p.BeginPath(); p.MoveTo(new Vector2(x, Top)); p.LineTo(new Vector2(x, height - Bottom)); p.Stroke();
            }

            // The line: a curve eased through the points, level at each so it never dips below zero between them.
            p.strokeColor = Ui.Bright;
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            Vector2 from = At(0, width, height, ceiling);
            p.MoveTo(from);
            for (int i = 1; i < _points.Count; i++)
            {
                Vector2 to = At(i, width, height, ceiling);
                float mid = (from.x + to.x) * 0.5f;
                p.BezierCurveTo(new Vector2(mid, from.y), new Vector2(mid, to.y), to);
                from = to;
            }
            p.Stroke();

            for (int i = 0; i < _points.Count; i++)
            {
                Vector2 at = At(i, width, height, ceiling);
                p.fillColor = Ui.Bright;
                p.BeginPath(); p.Arc(at, 4f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            }
        }
    }
    // ── Putt Quality Score (members/PuttScore.js) ────────────────────────────

    /// <summary>
    /// PuttScore.js's model: every putt scored (3 to 11 ft, made putts by how true they came in), one point per practice
    /// day with 5 or more putts, the big number over the last 30 days (else the last practice day), its change, and the
    /// chart's series for a range at a resolution its point count allows (days, then weeks, months, quarters).
    /// The formula is scorePutts: 65 parts make index (make % against the Tour's at each distance) and 35 parts accuracy.
    /// </summary>
    public sealed class PqsModel
    {
        public const int MinPutts = 5, Calibration = 30, HeroDays = 30;
        const int MaxDayPoints = 12, MaxWeekPoints = 16, MaxMonthPoints = 16, MaxLabels = 8;
        public static readonly (string key, int days)[] Ranges = { ("1M", 30), ("3M", 90), ("1Y", 365), ("ALL", 0) };

        public struct Putt { public int Distance, Made; public double Score; public DateTime At; }

        public sealed class Point
        {
            public DateTime Start;
            public int Pqs, MakeIndex, Accuracy, Putts;
            public List<Putt> Raw;
            public string Tip;
        }

        public List<Putt> Putts = new List<Putt>();
        public List<Point> Days = new List<Point>();
        public Point Hero;
        public string HeroLabel, DeltaLabel, DefaultRange = "ALL";
        public int? Delta;
        public bool Calibrating, ShowSelector;
        public DateTime Now;

        /// <summary>scorePutts: null when no putt is at a distance the Tour has a make % for.</summary>
        public static Point Score(List<Putt> putts)
        {
            if (putts == null || putts.Count == 0) return null;
            var attempts = new Dictionary<int, int>();
            var makes = new Dictionary<int, int>();
            double acc = 0; int accN = 0;
            foreach (Putt p in putts)
            {
                attempts[p.Distance] = (attempts.TryGetValue(p.Distance, out int a) ? a : 0) + 1;
                makes[p.Distance] = (makes.TryGetValue(p.Distance, out int m) ? m : 0) + (p.Made == 1 ? 1 : 0);
                if (p.Made == 1 && !double.IsNaN(p.Score) && !double.IsInfinity(p.Score)) { acc += Math.Max(0, Math.Min(100, p.Score)) / 100.0; accN++; }
            }
            double index = 0; int n = 0;
            foreach (KeyValuePair<int, int> d in attempts)
            {
                int at = d.Key - 3;
                double pro = at >= 0 && at < ProfileMath.Pga.Length ? ProfileMath.Pga[at] / 100.0 : 0;
                if (pro <= 0 || d.Value == 0) continue;
                index += makes[d.Key] / (double)d.Value / pro;
                n++;
            }
            if (n == 0) return null;
            double makeIndex = index / n, accuracy = accN > 0 ? acc / accN : 0;
            return new Point
            {
                Pqs = (int)AppPutt.JsRound((0.65 * makeIndex + 0.35 * accuracy) * 100),
                MakeIndex = (int)AppPutt.JsRound(makeIndex * 100),
                Accuracy = (int)AppPutt.JsRound(accuracy * 100),
                Putts = putts.Count,
                Raw = putts,
            };
        }

        /// <summary>The putts grouped by a period's first day, each group scored, oldest first.</summary>
        public static List<Point> Bucket(IEnumerable<Putt> putts, Func<DateTime, DateTime> start)
        {
            var groups = new SortedDictionary<DateTime, List<Putt>>();
            foreach (Putt p in putts)
            {
                DateTime key = start(p.At);
                if (!groups.TryGetValue(key, out List<Putt> list)) groups[key] = list = new List<Putt>();
                list.Add(p);
            }
            var points = new List<Point>();
            foreach (KeyValuePair<DateTime, List<Putt>> g in groups)
            {
                Point s = Score(g.Value);
                if (s == null) continue;
                s.Start = g.Key;
                points.Add(s);
            }
            return points;
        }

        public static DateTime DayOf(DateTime t) => t.Date;
        public static DateTime WeekOf(DateTime t) => t.Date.AddDays(-(((int)t.DayOfWeek + 6) % 7)); // ISO weeks start on Monday
        public static DateTime MonthOf(DateTime t) => new DateTime(t.Year, t.Month, 1);
        public static DateTime QuarterOf(DateTime t) => new DateTime(t.Year, (t.Month - 1) / 3 * 3 + 1, 1);

        /// <summary>
        /// cleanPuttData: putts from 3 to 11 ft with a time, in local time (a practice day is the Player's day). A make
        /// scores 100 x (1 - degrees off its tee's line / 90); a make whose path cannot be read scores 0.
        /// </summary>
        public static List<Putt> Clean(List<object> rows)
        {
            var putts = new List<Putt>();
            foreach (object row in rows)
            {
                if (!(J.Get(row, "distance") is double)) continue;
                int feet = J.Int(row, "distance");
                if (feet < 3 || feet > 11) continue;
                DateTime? at = ProfileMath.Date(row, "created_at");
                if (at == null) continue;
                bool made = J.Flag(row, "made");
                double score = 0;
                if (made)
                {
                    int[][] bp = PuttAssessment.Bp(row); double[] hb = PuttAssessment.Hb(row);
                    if (hb != null)
                    {
                        double he = bp.Length > 0 ? AppPutt.HoleEntry(bp, hb, out _) : 0;
                        double off = Math.Abs(AppPutt.TeeEntry(feet, J.Int(row, "tee", 2)) - he);
                        score = 100 * Math.Max(0, 1 - off / 90);
                    }
                }
                putts.Add(new Putt { Distance = feet, Made = made ? 1 : 0, Score = score, At = at.Value.ToLocalTime() });
            }
            return putts;
        }

        public static PqsModel Of(List<object> rows, DateTime now) => Of(Clean(rows), now);

        public static PqsModel Of(List<Putt> putts, DateTime now)
        {
            var m = new PqsModel { Putts = putts, Now = now };
            m.Days = Bucket(putts, DayOf).FindAll(d => d.Putts >= MinPutts);
            DateTime heroCut = now.AddDays(-HeroDays), prevCut = now.AddDays(-HeroDays * 2);
            List<Putt> last = putts.FindAll(p => p.At > heroCut), prev = putts.FindAll(p => p.At > prevCut && p.At <= heroCut);
            bool window = false;
            if (last.Count >= MinPutts) { m.Hero = Score(last); m.HeroLabel = "Last " + HeroDays + " days"; window = m.Hero != null; }
            if (m.Hero == null && m.Days.Count > 0)
            {
                m.Hero = m.Days[m.Days.Count - 1];
                m.HeroLabel = "Last session · " + m.Hero.Start.ToString("MMM d", CultureInfo.InvariantCulture);
            }
            if (m.Hero != null)
            {
                Point before = window && prev.Count >= MinPutts ? Score(prev) : null;
                if (before != null) { m.Delta = m.Hero.Pqs - before.Pqs; m.DeltaLabel = "vs prior " + HeroDays + " days"; }
                else if (m.Days.Count >= 2) { m.Delta = m.Hero.Pqs - m.Days[0].Pqs; m.DeltaLabel = "since your first session"; }
            }
            m.Calibrating = putts.Count < Calibration;
            // The default range is the smallest holding two practice days; the selector only shows once ranges would differ.
            m.DefaultRange = "ALL";
            foreach (var r in Ranges)
            {
                int count = r.days == 0 ? m.Days.Count : m.Days.Count(d => d.Start > now.AddDays(-r.days));
                if (count >= 2) { m.DefaultRange = r.key; break; }
            }
            m.ShowSelector = m.Days.Count >= 2 && m.Days[0].Start < now.AddDays(-Ranges[0].days);
            return m;
        }

        /// <summary>The chart's points for a range, their axis labels (thinned from the end, so the latest keeps its label) and the level.</summary>
        public List<Point> Series(string range, out List<string> labels, out string level)
        {
            int days = 0;
            foreach (var r in Ranges) if (r.key == range) days = r.days;
            List<Point> inRange = days == 0 ? Days : Days.FindAll(d => d.Start > Now.AddDays(-days));
            var raw = inRange.SelectMany(d => d.Raw).ToList();
            level = "day";
            List<Point> points = inRange;
            if (points.Count > MaxDayPoints) { level = "week"; points = Bucket(raw, WeekOf); }
            if (level == "week" && points.Count > MaxWeekPoints) { level = "month"; points = Bucket(raw, MonthOf); }
            if (level == "month" && points.Count > MaxMonthPoints) { level = "quarter"; points = Bucket(raw, QuarterOf); }
            bool years = points.Select(p => p.Start.Year).Distinct().Count() > 1;
            string lv = level;
            string Axis(Point p)
            {
                if (lv == "day" || lv == "week") return p.Start.ToString(years ? "M/d/yy" : "M/d", CultureInfo.InvariantCulture);
                if (lv == "month") return p.Start.ToString(years ? "MMM \\'yy" : "MMM", CultureInfo.InvariantCulture);
                return "Q" + ((p.Start.Month - 1) / 3 + 1) + " '" + p.Start.ToString("yy", CultureInfo.InvariantCulture);
            }
            string Tip(Point p)
            {
                if (lv == "day") return p.Start.ToString("MMM d", CultureInfo.InvariantCulture);
                if (lv == "week") return "Week of " + p.Start.ToString("MMM d", CultureInfo.InvariantCulture);
                if (lv == "month") return p.Start.ToString("MMM yyyy", CultureInfo.InvariantCulture);
                return "Q" + ((p.Start.Month - 1) / 3 + 1) + " " + p.Start.Year;
            }
            int n = points.Count, step = Math.Max(1, (int)Math.Ceiling(n / (double)MaxLabels));
            labels = new List<string>();
            for (int i = 0; i < n; i++)
            {
                points[i].Tip = Tip(points[i]);
                labels.Add((n - 1 - i) % step == 0 ? Axis(points[i]) : "");
            }
            return points;
        }

        public static string Caption(string level) =>
            level == "day" ? "One point per practice day (" + MinPutts + "+ putts)" : level == "week" ? "One point per week" : level == "month" ? "One point per month" : "One point per quarter";
    }

    /// <summary>PuttScore's card: the big number with its change, Make index and Accuracy, the ranges and the trend line.</summary>
    public sealed class PqsCard
    {
        public readonly VisualElement Root;
        readonly PqsModel _model;
        readonly VisualElement _chart;
        string _range;

        public PqsCard(PqsModel model, VisualElement over)
        {
            _model = model;
            Root = Ui.Box("pf-card pf-card-pad pqs");
            _chart = Ui.Box("pqs-chart-box");
            if (model.Hero == null)
            {
                Root.Add(Ui.Text("Your first score arrives after " + PqsModel.MinPutts + " putts.", "pqs-note", Ui.Medium));
                return;
            }
            Color number = model.Calibrating ? Ui.Faint : Color.white;
            VisualElement big = Ui.Box("pqs-hero-row", Lit(model.Hero.Pqs.ToString(), "pqs-big", number));
            if (model.Delta.HasValue)
            {
                int d = model.Delta.Value;
                Color trend = d > 0 ? Ui.Bright : d < 0 ? Ui.Red : Ui.Faint;
                big.Add(Lit((d < 0 ? "▼ " : "▲ ") + (d > 0 ? "+" : "") + d, "pqs-delta", trend));
            }
            Root.Add(big);
            Root.Add(Ui.Text(model.HeroLabel + (model.Delta.HasValue ? " · " + model.DeltaLabel : ""), "pqs-label", Ui.Medium));
            if (model.Calibrating) Root.Add(Lit("Calibrating · " + model.Putts.Count + "/" + PqsModel.Calibration + " putts", "pqs-label", Ui.Amber));
            Root.Add(Ui.Box("pqs-tiles", Tile("Make index", model.Hero.MakeIndex, Ui.Bright, number, "pqs-tile pqs-tile-left grow"), Tile("Accuracy", model.Hero.Accuracy, Ui.Soft, number, "pqs-tile grow")));
            Root.Add(_chart);
            _range = model.DefaultRange;
            DrawChart();
        }

        static Label Lit(string text, string classes, Color colour) { Label l = Ui.Text(text, classes, Ui.Bold); l.style.color = colour; return l; }

        static VisualElement Tile(string name, int value, Color key, Color number, string classes)
        {
            VisualElement dot = Ui.Box("pqs-dot");
            dot.style.backgroundColor = key;
            return Ui.Box(classes, Ui.Box("pqs-tile-head", dot, Ui.Text(name, "pqs-tile-l", Ui.Medium)), Lit(value.ToString(), "pqs-tile-v", number));
        }

        void DrawChart()
        {
            _chart.Clear();
            if (_model.Days.Count < 2) { _chart.Add(Ui.Text("Play again to see your trend. Each practice day with " + PqsModel.MinPutts + "+ putts adds a point.", "pqs-note", Ui.Medium)); return; }
            if (_model.ShowSelector)
            {
                VisualElement chips = Ui.Box("pqs-ranges");
                foreach (var r in PqsModel.Ranges)
                {
                    string key = r.key;
                    chips.Add(Ui.Box(key == _range ? "pqs-range pqs-range-on" : "pqs-range", Ui.Text(key, "pqs-range-t", Ui.Bold)).OnTap(() => { _range = key; DrawChart(); }));
                }
                _chart.Add(chips);
            }
            List<PqsModel.Point> points = _model.Series(_range, out List<string> labels, out string level);
            if (points.Count < 2) { _chart.Add(Ui.Text("Not enough practice days in this range.", "pqs-note", Ui.Medium)); return; }
            _chart.Add(new PqsChart(points, labels));
            _chart.Add(Ui.Text(PqsModel.Caption(level), "pqs-caption", Ui.Regular));
        }

        /// <summary>The "?" beside the title: what PQS is and how it is worked out.</summary>
        // The one explainer every PQS card opens lives in Pqs.cs.
        public static void Help(VisualElement over) => PqsHelp.Explain(over);
    }

    /// <summary>
    /// The PQS trend: a curved green line through the points (not from zero: the axis spans the values), faint grid lines,
    /// a dot on each point (white when picked), and a tap on a point says its period, PQS and putts.
    /// </summary>
    public sealed class PqsChart : VisualElement
    {
        const float Left = 30f, Bottom = 22f, Top = 12f, Right = 14f;
        readonly List<PqsModel.Point> _points;
        readonly List<string> _labels;
        readonly Label _tip;
        int _picked = -1;
        float _min, _max;

        public PqsChart(List<PqsModel.Point> points, List<string> labels)
        {
            _points = points;
            _labels = labels;
            AddToClassList("pqs-chart");
            _min = points.Min(p => p.Pqs);
            _max = points.Max(p => p.Pqs);
            if (_max - _min < 4) { _min -= 2; _max += 2; }
            _tip = Ui.Text("", "pqs-tip", Ui.Bold);
            _tip.style.display = DisplayStyle.None;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Labels());
            RegisterCallback<PointerDownEvent>(e => Pick(((Vector2)e.localPosition).x));
        }

        Vector2 At(int i, float w, float h)
        {
            float x = Left + (w - Left - Right) * (_points.Count > 1 ? i / (float)(_points.Count - 1) : 0.5f);
            float y = Top + (h - Top - Bottom) * (1f - (_points[i].Pqs - _min) / Mathf.Max(1f, _max - _min));
            return new Vector2(x, y);
        }

        void Pick(float x)
        {
            float w = contentRect.width, h = contentRect.height;
            int best = -1; float near = 28f;
            for (int i = 0; i < _points.Count; i++) { float dx = Mathf.Abs(At(i, w, h).x - x); if (dx < near) { near = dx; best = i; } }
            _picked = best == _picked ? -1 : best;
            Labels();
            MarkDirtyRepaint();
        }

        void Labels()
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0f || h <= 0f) return;
            Clear();
            for (int line = 0; line <= 4; line++)
            {
                Label value = Ui.Text(Mathf.RoundToInt(_min + (_max - _min) * line / 4f).ToString(), "chart-label", Ui.Regular);
                value.style.left = 0;
                value.style.width = Left - 6f;
                value.style.top = Top + (h - Top - Bottom) * (1f - line / 4f) - 7f;
                value.style.unityTextAlign = TextAnchor.MiddleRight;
                Add(value);
            }
            for (int i = 0; i < _points.Count; i++)
            {
                if (string.IsNullOrEmpty(_labels[i])) continue;
                Label day = Ui.Text(_labels[i], "chart-label", Ui.Regular);
                day.style.left = At(i, w, h).x - 26f;
                day.style.width = 52f;
                day.style.top = h - Bottom + 6f;
                day.style.unityTextAlign = TextAnchor.MiddleCenter;
                Add(day);
            }
            if (_picked >= 0)
            {
                PqsModel.Point p = _points[_picked];
                _tip.text = p.Tip + " · PQS " + p.Pqs + "\n<size=11><color=#8B9399>" + ProfileMath.Number(p.Putts) + " putts</color></size>";
                _tip.style.display = DisplayStyle.Flex;
                Vector2 at = At(_picked, w, h);
                _tip.style.left = Mathf.Clamp(at.x - 60f, 4f, Mathf.Max(4f, w - 154f));
                _tip.style.top = Mathf.Max(0f, at.y - 56f);
                Add(_tip);
            }
        }

        void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0f || h <= 0f || _points.Count == 0) return;
            Painter2D p = context.painter2D;
            p.strokeColor = Ui.Line;
            p.lineWidth = 1f;
            for (int line = 0; line <= 4; line++)
            {
                float y = Top + (h - Top - Bottom) * (1f - line / 4f);
                // Dashed, as the app's background lines.
                for (float x = Left; x < w - Right; x += 8f) { p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(Mathf.Min(x + 4f, w - Right), y)); p.Stroke(); }
            }
            p.strokeColor = Ui.Bright;
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            Vector2 from = At(0, w, h);
            p.MoveTo(from);
            for (int i = 1; i < _points.Count; i++)
            {
                Vector2 to = At(i, w, h);
                float mid = (from.x + to.x) * 0.5f;
                p.BezierCurveTo(new Vector2(mid, from.y), new Vector2(mid, to.y), to);
                from = to;
            }
            p.Stroke();
            for (int i = 0; i < _points.Count; i++)
            {
                Vector2 at = At(i, w, h);
                p.fillColor = i == _picked ? Color.white : Ui.Bright;
                p.BeginPath(); p.Arc(at, 4f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            }
        }
    }
}
