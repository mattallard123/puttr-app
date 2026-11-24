// The coach's drawing tools over a Player's video (puttr-app coaching/dashboard/VideoDrawing.js): a layer the size of
// the video that takes freehand strokes, straight lines, boxes and ovals in the app's green, and a tool bar of five
// round buttons: pencil, line, box, oval (the green dot, the app's default tool) and the red eraser, which clears
// everything drawn. The app asks for a pause before drawing; here the coach may draw over the video paused or playing
// (Matt, 2026-11-18).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class VideoDraw : VisualElement
    {
        public enum Tool { Draw, Line, Square, Circle }

        sealed class Mark { public Tool Tool; public readonly List<Vector2> Points = new List<Vector2>(); }

        readonly List<Mark> _marks = new List<Mark>();
        Mark _current;
        Tool _tool = Tool.Circle;
        VisualElement _bar;

        public VideoDraw()
        {
            AddToClassList("lab-draw");
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent += Paint;
            RegisterCallback<PointerDownEvent>(Down);
            RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(Up);
            RegisterCallback<PointerCancelEvent>(_ => { _current = null; MarkDirtyRepaint(); });
        }

        public Tool Current => _tool;

        /// <summary>Clears every mark (the eraser).</summary>
        public void Erase() { _marks.Clear(); _current = null; MarkDirtyRepaint(); }

        void Down(PointerDownEvent e)
        {
            this.CapturePointer(e.pointerId);
            _current = new Mark { Tool = _tool };
            _current.Points.Add(e.localPosition);
            if (_tool != Tool.Draw) _current.Points.Add(e.localPosition);
            e.StopPropagation();
            MarkDirtyRepaint();
        }

        void Move(PointerMoveEvent e)
        {
            if (_current == null || !this.HasPointerCapture(e.pointerId)) return;
            Vector2 p = e.localPosition;
            if (_tool == Tool.Draw) { if (Vector2.Distance(_current.Points[_current.Points.Count - 1], p) >= 1.5f) _current.Points.Add(p); }
            else _current.Points[1] = p;
            Perf.Wake(0.3f);
            MarkDirtyRepaint();
        }

        void Up(PointerUpEvent e)
        {
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            if (_current != null && (_current.Points.Count > 1)) _marks.Add(_current);
            _current = null;
            MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext ctx)
        {
            Painter2D p = ctx.painter2D;
            p.strokeColor = Ui.Green;
            p.lineWidth = 2.5f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            foreach (Mark m in _marks) Shape(p, m);
            if (_current != null) Shape(p, _current);
        }

        static void Shape(Painter2D p, Mark m)
        {
            List<Vector2> pts = m.Points;
            if (pts.Count < 2) return;
            Vector2 a = pts[0], b = pts[pts.Count - 1];
            p.BeginPath();
            switch (m.Tool)
            {
                case Tool.Draw:
                    p.MoveTo(a);
                    for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]);
                    break;
                case Tool.Line:
                    p.MoveTo(a); p.LineTo(b);
                    break;
                case Tool.Square:
                    p.MoveTo(a); p.LineTo(new Vector2(b.x, a.y)); p.LineTo(b); p.LineTo(new Vector2(a.x, b.y)); p.ClosePath();
                    break;
                case Tool.Circle:
                    // The app's ellipse: centred where the drag began, its radii to where it is now.
                    float rx = Mathf.Abs(b.x - a.x), ry = Mathf.Abs(b.y - a.y);
                    for (int i = 0; i <= 48; i++)
                    {
                        float t = i / 48f * Mathf.PI * 2;
                        Vector2 q = a + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry);
                        if (i == 0) p.MoveTo(q); else p.LineTo(q);
                    }
                    break;
            }
            p.Stroke();
        }

        /// <summary>The tool bar (a dark bar of five square buttons) to place over the top of the video.</summary>
        public VisualElement Bar()
        {
            _bar = Ui.Box("row lab-draw-bar");
            DrawBar();
            return _bar;
        }

        void DrawBar()
        {
            if (_bar == null) return;
            _bar.Clear();
            void Add(Tool tool, string icon)
            {
                bool on = _tool == tool;
                // The tool in hand is the picked state: white with a black icon, as a chip that is on.
                VisualElement b = LabUi.Round(icon, 44, on ? Color.white : Ui.Surface2, () => { _tool = tool; DrawBar(); }, 18, on ? Color.black : Color.white);
                b.style.marginLeft = b.style.marginRight = 4;
                _bar.Add(b);
            }
            Add(Tool.Draw, "PencilSimple");
            Add(Tool.Line, "LineSegment");
            Add(Tool.Square, "BoundingBox");
            Add(Tool.Circle, "Circle");
            // Erase is the destructive one: the red of .btn-red.
            VisualElement erase = LabUi.Round("Eraser", 44, new Color32(140, 34, 34, 255), Erase, 18);
            erase.style.marginLeft = erase.style.marginRight = 4;
            _bar.Add(erase);
        }
    }
}
