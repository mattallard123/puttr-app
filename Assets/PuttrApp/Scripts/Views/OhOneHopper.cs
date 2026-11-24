// Oh-One's hopper (Matt, 2026-11-21): a range-ball hopper full of golf balls, one ball a point still to score (301 to
// start, say). A make takes that many balls out: the level falls a few balls a frame, out of the chute at the bottom,
// with the rattle of balls dropping, so the Player sees and hears the number go down.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class Hopper : VisualElement
    {
        readonly VisualElement _art;
        readonly Label _count;
        readonly Color _colour;
        readonly int _capacity;
        readonly List<Vector2> _spots = new List<Vector2>();   // ball centres, bottom first, in the art's 0..1 box
        float _radius;                                          // in the same units (of the art's width)
        Vector2 _laidFor;
        int _shown = -1, _target;
        float _lastRattle;

        // The hopper's outline in 0..1 of the art: a wide mouth, sloping sides, a short chute.
        const float Top = 0.04f, Shoulder = 0.78f, Bottom = 0.9f, MouthHalf = 0.46f, NeckHalf = 0.12f;

        public Hopper(string name, Color colour, int capacity)
        {
            _colour = colour;
            _capacity = Mathf.Max(1, capacity);
            AddToClassList("hopper");
            _art = new VisualElement();
            _art.AddToClassList("hopper-art");
            _art.generateVisualContent += Draw;
            Add(_art);
            _count = Ui.Text(_capacity.ToString(), "hopper-count", Ui.Bold);
            _count.style.color = colour;
            Add(_count);
            Add(Ui.Text(name, "hopper-name", Ui.Bold));
            _art.schedule.Execute(Step).Every(16);
        }

        /// <summary>The balls the Player has left, and whether it is their turn. Fewer than shown drains; more (a new game) fills at once.</summary>
        public void Set(int left, bool now)
        {
            _target = Mathf.Clamp(left, 0, _capacity);
            if (_shown < 0 || _target > _shown) { _shown = _target; _count.text = _shown.ToString(); _art.MarkDirtyRepaint(); }
            EnableInClassList("hopper-now", now);
        }

        void Step()
        {
            if (_shown <= _target) return;
            int gap = _shown - _target;
            _shown -= Mathf.Max(1, Mathf.CeilToInt(gap / 24f));
            _count.text = _shown.ToString();
            _art.MarkDirtyRepaint();
            float now = Time.realtimeSinceStartup;
            if (now - _lastRattle > 0.07f)
            {
                _lastRattle = now;
                PuttrArcade.GameAudio audio = PuttrArcade.Arcade.CurrentAudio;
                if (audio != null) audio.Library("ball_land", 0.18f, 1.35f + Random.value * 0.5f);
            }
        }

        // Half the hopper's width at height y (0 top, 1 bottom), in 0..1 of the art's width.
        static float HalfAt(float y) => y <= Shoulder ? Mathf.Lerp(MouthHalf, NeckHalf, Mathf.InverseLerp(Top, Shoulder, y)) : NeckHalf;

        // Packs the capacity's balls into the body, bottom first, rows offset as balls settle. The radius is the largest
        // that fits them all below the mouth, found by halving.
        void Layout(float aspect)
        {
            _spots.Clear();
            float lo = 0.002f, hi = 0.08f;
            for (int k = 0; k < 18; k++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Pack(mid, aspect, null) >= _capacity) lo = mid; else hi = mid;
            }
            _radius = lo;
            Pack(_radius, aspect, _spots);
        }

        // How many balls of radius r (in widths) fit; fills spots if given. aspect: height over width.
        int Pack(float r, float aspect, List<Vector2> spots)
        {
            int n = 0;
            float rowH = r * 1.75f / aspect;            // rows in 0..1 of the height
            float rY = r / aspect;
            int row = 0;
            for (float y = Shoulder - rY; y >= Top + rY && n < _capacity; y -= rowH, row++)
            {
                float half = Mathf.Min(HalfAt(y - rY), HalfAt(y + rY)) - r * 1.05f;
                if (half < 0f) continue;
                float off = row % 2 == 0 ? 0f : r;
                for (float x = -half + off; x <= half + 1e-4f && n < _capacity; x += r * 2f)
                {
                    spots?.Add(new Vector2(0.5f + x, y));
                    n++;
                }
            }
            return n;
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect box = _art.contentRect;
            if (box.width <= 1f || box.height <= 1f) return;
            Vector2 size = new Vector2(box.width, box.height);
            if (size != _laidFor) { _laidFor = size; Layout(box.height / box.width); }
            Painter2D p = ctx.painter2D;
            Vector2 P(float x, float y) => new Vector2(x * box.width, y * box.height);

            // The body: dark glass with the Player's colour round it.
            p.BeginPath();
            p.MoveTo(P(0.5f - MouthHalf, Top));
            p.LineTo(P(0.5f + MouthHalf, Top));
            p.LineTo(P(0.5f + NeckHalf, Shoulder));
            p.LineTo(P(0.5f + NeckHalf, Bottom));
            p.LineTo(P(0.5f - NeckHalf, Bottom));
            p.LineTo(P(0.5f - NeckHalf, Shoulder));
            p.ClosePath();
            p.fillColor = new Color(1f, 1f, 1f, 0.05f);
            p.Fill();

            // The balls, bottom first: the level falls as they go.
            float r = _radius * box.width;
            int count = Mathf.Min(Mathf.Max(_shown, 0), _spots.Count);
            p.fillColor = new Color(0.96f, 0.96f, 0.94f);
            for (int i = 0; i < count; i++)
            {
                p.BeginPath();
                p.Arc(P(_spots[i].x, _spots[i].y), r * 0.92f, 0f, 360f);
                p.Fill();
            }
            // A dimple shadow on each, so they read as balls and not dots, when there is room for it.
            if (r >= 3f)
            {
                p.fillColor = new Color(0f, 0f, 0f, 0.14f);
                for (int i = 0; i < count; i++)
                {
                    Vector2 c = P(_spots[i].x, _spots[i].y) + new Vector2(r * 0.25f, r * 0.25f);
                    p.BeginPath();
                    p.Arc(c, r * 0.5f, 0f, 360f);
                    p.Fill();
                }
            }
            // A ball on its way out of the chute while it drains.
            if (_shown > _target)
            {
                p.fillColor = new Color(0.96f, 0.96f, 0.94f);
                p.BeginPath();
                p.Arc(P(0.5f, Bottom + 0.04f), r * 0.92f, 0f, 360f);
                p.Fill();
            }

            p.strokeColor = _colour;
            p.lineWidth = 3f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(P(0.5f - MouthHalf - 0.02f, Top - 0.01f));
            p.LineTo(P(0.5f - MouthHalf, Top));
            p.LineTo(P(0.5f - NeckHalf, Shoulder));
            p.LineTo(P(0.5f - NeckHalf, Bottom));
            p.MoveTo(P(0.5f + MouthHalf + 0.02f, Top - 0.01f));
            p.LineTo(P(0.5f + MouthHalf, Top));
            p.LineTo(P(0.5f + NeckHalf, Shoulder));
            p.LineTo(P(0.5f + NeckHalf, Bottom));
            p.Stroke();
        }
    }
}
