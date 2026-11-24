// The Profile tab's drawn pieces (ProfileScreen.cs), flat as docs/design-guidelines.md asks: the edit
// pencil, an achievement crest, the activity calendar and the putts-by-month line. Solid colours from the
// guidelines' table only: no glow, gradient, sheen or shading.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>
    /// A rounded-rectangle path for Painter2D. The gradient textures below are no longer used by the Profile tab;
    /// they stay only until HomeScreen.cs and Pages.cs stop calling them.
    /// </summary>
    static class ProfileTex
    {
        static readonly Dictionary<string, Texture2D> Made = new Dictionary<string, Texture2D>();

        /// <summary>A texture from a function of (u, v), v from the top; kept by name so each is drawn once.</summary>
        public static Texture2D Get(string name, int w, int h, Func<float, float, Color> at)
        {
            if (Made.TryGetValue(name, out Texture2D made) && made != null) return made;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[(h - 1 - y) * w + x] = at((x + 0.5f) / w, (y + 0.5f) / h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Made[name] = tex;
            return tex;
        }

        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }

        /// <summary>Top to bottom, from one colour to another.</summary>
        public static Texture2D Vertical(string top, string bottom) =>
            Get("v" + top + bottom, 4, 64, (u, v) => Color.Lerp(Hex(top), Hex(bottom), v));

        /// <summary>Corner to corner (top left to bottom right), as the mockup's 160 degree gradients.</summary>
        public static Texture2D Diagonal(string from, string to) =>
            Get("d" + from + to, 64, 64, (u, v) => Color.Lerp(Hex(from), Hex(to), Mathf.Clamp01((u * 0.45f + v * 0.55f))));

        /// <summary>A soft pool of colour, brightest at (cx, cy), gone at the given radius (shares of the box).</summary>
        public static Texture2D Glow(string name, Color colour, float cx, float cy, float rx, float ry, float strength) =>
            Get("g" + name, 96, 96, (u, v) =>
            {
                float dx = (u - cx) / rx, dy = (v - cy) / ry;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                return new Color(colour.r, colour.g, colour.b, strength * t * t);
            });

        public static VisualElement Fill(this VisualElement e, Texture2D tex)
        {
            e.style.backgroundImage = new StyleBackground(tex);
            return e;
        }

        /// <summary>A rounded rectangle as a path (Painter2D has none of its own).</summary>
        public static void RoundRect(Painter2D p, Rect r, float radius)
        {
            radius = Mathf.Min(radius, r.width / 2f, r.height / 2f);
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin + radius, r.yMin));
            p.LineTo(new Vector2(r.xMax - radius, r.yMin));
            p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + radius), radius);
            p.LineTo(new Vector2(r.xMax, r.yMax - radius));
            p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - radius, r.yMax), radius);
            p.LineTo(new Vector2(r.xMin + radius, r.yMax));
            p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMax - radius), radius);
            p.LineTo(new Vector2(r.xMin, r.yMin + radius));
            p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + radius, r.yMin), radius);
            p.ClosePath();
        }

    }

    // ── The Player card ──────────────────────────────────────────────────────

    /// <summary>A pencil, drawn as strokes on a 24 unit grid (the icon set has none): the edit button on the photo's corner.</summary>
    public sealed class PencilGlyph : VisualElement
    {
        public PencilGlyph(float size = 16f)
        {
            style.width = style.height = size;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            float s = Mathf.Min(contentRect.width, contentRect.height) / 24f;
            if (s <= 0f) return;
            Painter2D p = context.painter2D;
            p.strokeColor = Color.white;
            p.lineWidth = Mathf.Max(1.5f, 2f * s);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(4f * s, 20f * s)); p.LineTo(new Vector2(4f * s, 16f * s)); p.LineTo(new Vector2(15f * s, 5f * s));
            p.LineTo(new Vector2(19f * s, 9f * s)); p.LineTo(new Vector2(8f * s, 20f * s));
            p.ClosePath();
            p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(12.5f * s, 7.5f * s)); p.LineTo(new Vector2(16.5f * s, 11.5f * s)); p.Stroke();
        }
    }

    // ── Achievements: flat crests ────────────────────────────────────────────

    /// <summary>
    /// An earned achievement's crest: its glyph in white on a surface square (.pf-coin). The tint and offset are
    /// kept for the callers (AchievementsScreen in Pages.cs) and no longer used: a crest has no colour of its own.
    /// </summary>
    public sealed class Coin : VisualElement
    {
        public Coin(string icon, Color tint, float offset)
        {
            AddToClassList("pf-coin");
            // The stroke icon where the set has one (FillTrophy -> Trophy).
            string stroke = icon != null && icon.StartsWith("Fill") ? icon.Substring(4) : icon;
            if (stroke != icon && Resources.Load<Texture2D>("PuttrArcade/Icons/Icon" + stroke) == null) stroke = icon;
            Add(Ui.Icon(stroke, 28, Color.white));
        }
    }

    // ── Last 30 days: the calendar ───────────────────────────────────────────

    /// <summary>
    /// One month at a time, Sunday first, back as far as 90 days: a day with putts is a solid brand green
    /// square with a white number, today has a thin bright border. Tap a day for its putts.
    /// </summary>
    public sealed class DayCalendar : VisualElement
    {
        const float CellH = 44f, Inset = 4f;
        readonly Dictionary<string, int> _days;
        readonly DateTime _today, _earliest;
        DateTime _month;
        readonly Label _title, _back, _on, _picked;
        readonly VisualElement _grid;
        readonly List<(DateTime date, int putts, bool open, Label label)> _cells = new List<(DateTime, int, bool, Label)>();

        public DayCalendar(Dictionary<string, int> days, DateTime today)
        {
            AddToClassList("nc");
            _days = days;
            _today = today.Date;
            _earliest = _today.AddDays(-90);
            _month = new DateTime(_today.Year, _today.Month, 1);

            _back = Ui.Text("‹", "nc-arrow", Ui.Bold).OnTap(() => Step(-1));
            _on = Ui.Text("›", "nc-arrow", Ui.Bold).OnTap(() => Step(1));
            _title = Ui.Text("", "nc-month grow", Ui.Bold);
            Add(Ui.Box("row nc-head", _back, _title, _on));
            VisualElement names = Ui.Box("row nc-names");
            foreach (string name in new[] { "S", "M", "T", "W", "T", "F", "S" }) names.Add(Ui.Text(name, "nc-name", Ui.Bold));
            Add(names);
            _grid = Ui.Box("nc-grid");
            _grid.generateVisualContent += Draw;
            _grid.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Add(_grid);
            _picked = Ui.Text("", "nc-picked", Ui.Medium);
            Add(_picked);
            Build();
        }

        void Step(int by)
        {
            DateTime next = _month.AddMonths(by);
            if (next < new DateTime(_earliest.Year, _earliest.Month, 1) || next > new DateTime(_today.Year, _today.Month, 1)) return;
            _month = next;
            Build();
        }

        static int Offset(DateTime month) => (int)month.DayOfWeek;

        void Build()
        {
            _grid.Clear();
            _cells.Clear();
            _picked.text = " ";
            _title.text = _month.ToString("MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
            _back.style.opacity = _month > new DateTime(_earliest.Year, _earliest.Month, 1) ? 1f : 0.2f;
            _on.style.opacity = _month < new DateTime(_today.Year, _today.Month, 1) ? 1f : 0.2f;
            int length = DateTime.DaysInMonth(_month.Year, _month.Month);
            for (int day = 1; day <= length; day++)
            {
                DateTime date = new DateTime(_month.Year, _month.Month, day);
                _days.TryGetValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out int putts);
                bool open = date >= _earliest && date <= _today;
                Label label = Ui.Text(day.ToString(), putts > 0 && open ? "nc-day nc-day-on" : open ? "nc-day" : "nc-day nc-day-shut", Ui.Bold);
                if (putts > 0 && open)
                {
                    int count = putts;
                    label.OnTap(() => _picked.text = ProfileMath.Number(count) + (count == 1 ? " putt" : " putts") + " on " + date.ToString("MMM d", CultureInfo.InvariantCulture));
                }
                _cells.Add((date, open ? putts : 0, open, label));
                _grid.Add(label);
            }
            int rows = (Offset(_month) + length + 6) / 7;
            _grid.style.height = rows * CellH;
            Place();
            _grid.MarkDirtyRepaint();
        }

        Rect Cell(int index, float width)
        {
            float cw = width / 7f;
            int slot = Offset(_month) + index;
            return new Rect((slot % 7) * cw, (slot / 7) * CellH, cw, CellH);
        }

        void Place()
        {
            float width = _grid.contentRect.width;
            if (float.IsNaN(width) || width <= 0f) return;
            for (int i = 0; i < _cells.Count; i++)
            {
                Rect r = Cell(i, width);
                Label l = _cells[i].label;
                l.style.left = r.x; l.style.top = r.y; l.style.width = r.width; l.style.height = r.height;
            }
            _grid.MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            float width = _grid.contentRect.width;
            if (width <= 0f || _cells.Count == 0) return;
            Painter2D p = context.painter2D;
            for (int i = 0; i < _cells.Count; i++)
            {
                var (date, putts, _, _) = _cells[i];
                bool today = date == _today;
                if (putts <= 0 && !today) continue;
                Rect r = Cell(i, width);
                ProfileTex.RoundRect(p, new Rect(r.x + Inset, r.y + Inset, r.width - Inset * 2f, r.height - Inset * 2f), 4f);
                if (putts > 0) { p.fillColor = Ui.Green; p.Fill(); }
                if (today) { p.lineWidth = 1f; p.strokeColor = Ui.Bright; p.Stroke(); }
            }
        }
    }

    // ── Putts by month: the line ─────────────────────────────────────────────

    /// <summary>
    /// A month a point: a solid bright green line eased through them over a grid, a dot on each point, the count
    /// over each point and the month under it. No fill under the line.
    /// </summary>
    public sealed class Ribbon : VisualElement
    {
        readonly List<(string label, int value)> _points;
        const float Top = 26f, Bottom = 26f, Side = 18f;

        public Ribbon(List<(string label, int value)> points)
        {
            _points = points;
            AddToClassList("pf-ribbon");
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Labels());
        }

        int Most()
        {
            int most = 1;
            foreach (var (_, value) in _points) most = Mathf.Max(most, value);
            return most;
        }

        Vector2 At(int i, Rect r, int most)
        {
            float x = Side + (r.width - Side * 2f) * (_points.Count > 1 ? i / (float)(_points.Count - 1) : 0.5f);
            float y = Top + (r.height - Top - Bottom) * (1f - _points[i].value / (float)most);
            return new Vector2(x, y);
        }

        void Labels()
        {
            Rect r = contentRect;
            if (r.width <= 0f || _points.Count == 0) return;
            Clear();
            int most = Most();
            for (int i = 0; i < _points.Count; i++)
            {
                Vector2 at = At(i, r, most);
                Label value = Ui.Text(ProfileMath.Number(_points[i].value), "pf-ribbon-value", Ui.Bold);
                value.style.left = at.x - 30f; value.style.width = 60f; value.style.top = at.y - 22f;
                Add(value);
                Label month = Ui.Text(_points[i].label.ToUpperInvariant(), "pf-ribbon-month", Ui.Bold);
                month.style.left = at.x - 30f; month.style.width = 60f; month.style.top = r.height - Bottom + 8f;
                Add(month);
            }
        }

        List<Vector2> Curve(Rect r, int most)
        {
            var line = new List<Vector2>();
            Vector2 from = At(0, r, most);
            line.Add(from);
            for (int i = 1; i < _points.Count; i++)
            {
                Vector2 to = At(i, r, most);
                float mid = (from.x + to.x) * 0.5f;
                Vector2 c1 = new Vector2(mid, from.y), c2 = new Vector2(mid, to.y);
                for (int s = 1; s <= 14; s++)
                {
                    float t = s / 14f, u = 1f - t;
                    line.Add(u * u * u * from + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * to);
                }
                from = to;
            }
            return line;
        }

        void Draw(MeshGenerationContext context)
        {
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f || _points.Count == 0) return;
            int most = Most();
            float floor = r.height - Bottom;
            Painter2D p = context.painter2D;
            p.lineWidth = 1f;
            p.strokeColor = Ui.Line;
            for (int g = 0; g <= 3; g++)
            {
                float y = Top + (floor - Top) * g / 3f;
                p.BeginPath(); p.MoveTo(new Vector2(Side, y)); p.LineTo(new Vector2(r.width - Side, y)); p.Stroke();
            }

            List<Vector2> line = Curve(r, most);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.lineWidth = 2f;
            p.strokeColor = Ui.Bright;
            p.BeginPath();
            p.MoveTo(line[0]);
            for (int i = 1; i < line.Count; i++) p.LineTo(line[i]);
            p.Stroke();
            p.fillColor = Ui.Bright;
            for (int i = 0; i < _points.Count; i++)
            {
                p.BeginPath(); p.Arc(At(i, r, most), 4f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            }
        }
    }
}
