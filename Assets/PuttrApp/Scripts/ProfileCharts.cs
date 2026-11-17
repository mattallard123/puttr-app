// The Profile tab's two drawn cards, made to look as puttr-app's do: the month calendar with
// the days putted on filled in, and putts by month as a curved line with a dot a month.
using System;
using System.Collections.Generic;
using System.Globalization;
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
                else if (date == _today && putts == 0) number.style.color = Ui.Green;
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

            p.strokeColor = new Color32(36, 38, 40, 255);
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
            p.strokeColor = new Color32(45, 140, 60, 255);
            p.lineWidth = 3f;
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
                p.fillColor = new Color32(45, 140, 60, 255);
                p.BeginPath(); p.Arc(at, 5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                p.fillColor = new Color32(122, 212, 70, 255);
                p.BeginPath(); p.Arc(at, 2.2f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            }
        }
    }
}
