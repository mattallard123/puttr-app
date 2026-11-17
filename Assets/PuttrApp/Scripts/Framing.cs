// The framing check while a 3D session films: is the Player's whole body in the picture?
//
// FramingMath is puttr-app's coach/framing/framingLogic.js classifyFraming, unchanged.
// FramingCheck is its useFramingCheck / useFramingVoice loop: a still about once a second, a
// fall back only after two readings in a row, "framed" after two seconds of the whole body, and
// the coach saying what to change. Framing never stops a putt counting.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrApp
{
    public enum FramingStatus { None, Partial, Full }

    public static class FramingMath
    {
        static readonly string[] Head = { "nose", "left_eye", "right_eye", "left_ear", "right_ear" };
        static readonly (string key, string[] points)[] Groups =
        {
            ("head", Head),
            ("shoulders", new[] { "left_shoulder", "right_shoulder" }),
            ("hips", new[] { "left_hip", "right_hip" }),
            ("knees", new[] { "left_knee", "right_knee" }),
            ("feet", new[] { "left_ankle", "right_ankle" }),
        };
        const double MinInFrame = 0.5, EdgeMargin = 0.03;
        public const string NoPlayer = "No player detected. Step into the frame";

        static object Point(object landmarks, string name) => J.Get(landmarks, name);
        static double X(object p) => J.Num(p, "x");
        static double Y(object p) => J.Num(p, "y");
        static double In(object p) => J.Get(p, "inFrame") != null ? J.Num(p, "inFrame") : 0;

        static bool PointOk(object p, string group)
        {
            if (p == null || In(p) < MinInFrame) return false;
            double bottom = group == "feet" ? 1 - 0.005 : 1 - EdgeMargin;
            return X(p) >= EdgeMargin && X(p) <= 1 - EdgeMargin && Y(p) >= EdgeMargin && Y(p) <= bottom;
        }

        static bool Plausible(object lm)
        {
            double? headY = null;
            foreach (string name in Head)
            {
                object p = Point(lm, name);
                if (p == null || In(p) < MinInFrame) continue;
                if (headY == null || Y(p) < headY) headY = Y(p);
            }
            if (headY == null) return false;
            double Avg(string a, string b) => (Y(Point(lm, a)) + Y(Point(lm, b))) / 2;
            double shoulder = Avg("left_shoulder", "right_shoulder"), hip = Avg("left_hip", "right_hip"), knee = Avg("left_knee", "right_knee"), ankle = Avg("left_ankle", "right_ankle");
            double top = Math.Min(headY.Value, shoulder), bottom = Math.Max(hip, Math.Max(knee, ankle));
            return shoulder < bottom - 0.1 && bottom - top >= 0.3;
        }

        static string Hint(List<string> missing, object lm)
        {
            if (missing.Contains("feet")) return "Feet are cut off. Tilt the phone down or step back";
            if (missing.Contains("head")) return "Head is cut off. Tilt the phone up or step back";
            foreach (string name in new[] { "left_shoulder", "right_shoulder", "left_hip", "right_hip" })
            {
                object p = Point(lm, name);
                if (p != null && (X(p) < EdgeMargin || X(p) > 1 - EdgeMargin)) return "Move to the center of the frame";
            }
            return "Step back so your whole body is visible";
        }

        /// <summary>The app's classifyFraming: pose is the detector's {landmarks: {name: {x, y, inFrame}}}, or null.</summary>
        public static (FramingStatus status, string hint) Classify(object pose)
        {
            object lm = J.Get(pose, "landmarks");
            if (lm == null) return (FramingStatus.None, NoPlayer);
            var missing = new List<string>();
            foreach ((string key, string[] points) g in Groups)
            {
                bool ok = false;
                foreach (string name in g.points) if (PointOk(Point(lm, name), g.key)) ok = true;
                if (!ok) missing.Add(g.key);
            }
            if (missing.Count == 0) return Plausible(lm) ? (FramingStatus.Full, null) : (FramingStatus.None, NoPlayer);
            if (missing.Count >= Groups.Length - 1) return (FramingStatus.None, NoPlayer);
            return (FramingStatus.Partial, Hint(missing, lm));
        }
    }

    /// <summary>The loop around the classifier: stills, holding, drift, and what the coach says.</summary>
    public sealed class FramingCheck
    {
        const float Before = 0.9f, After = 2f, HoldToConfirm = 2f, PuttGrace = 15f, StanceMemory = 12f, RepeatHint = 8f;

        public FramingStatus Status { get; private set; } = FramingStatus.None;
        public string Hint { get; private set; } = "Step back so your whole body is visible";
        public bool Confirmed { get; private set; }
        public event Action Changed;

        float _nextShot, _fullSince = -1f, _lastPutt = -100f, _lastBody = -100f, _lastSaid = -100f;
        int _downgrades, _partials, _nones;
        string _said;
        bool _holdSaid;

        public void Reset()
        {
            Confirmed = false;
            Status = FramingStatus.None;
            _fullSince = -1f;
            _downgrades = _partials = _nones = 0;
            _holdSaid = false;
            Changed?.Invoke();
        }

        public void PuttSeen() => _lastPutt = Time.realtimeSinceStartup;

        /// <summary>Once a frame: asks for a still when one is due.</summary>
        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextShot) return;
            _nextShot = now + (Confirmed ? After : Before);
            Capture.One.Snapshot();
        }

        /// <summary>The detector's answer for the latest still.</summary>
        public void Seen(object pose)
        {
            float now = Time.realtimeSinceStartup;
            (FramingStatus status, string hint) = FramingMath.Classify(pose);
            if (status != FramingStatus.None) _lastBody = now;

            if (Confirmed)
            {
                // Drift: two partial readings or three empty ones in a row open the guide again, but not just after a putt (the head is down).
                if (status == FramingStatus.Partial) { _partials++; _nones = 0; }
                else if (status == FramingStatus.None) { _nones++; _partials = 0; }
                else { _partials = _nones = 0; }
                if (now - _lastPutt > PuttGrace && (_partials >= 2 || _nones >= 3)) { Reset(); Show(status, hint); }
                return;
            }

            // A step down (full to partial, partial to none) shows only on the second reading in a row.
            if (status < Status) { _downgrades++; if (_downgrades < 2) return; }
            _downgrades = 0;
            if (status == FramingStatus.None && now - _lastBody < StanceMemory && _lastBody > 0)
                hint = "I lose you when your head is down. Stand tall for a moment so I can find you";
            Show(status, hint);

            if (status == FramingStatus.Full)
            {
                if (_fullSince < 0) _fullSince = now;
                if (!_holdSaid) { _holdSaid = true; CoachVoice.Say("Hold still."); }
                if (now - _fullSince >= HoldToConfirm)
                {
                    Confirmed = true;
                    Changed?.Invoke();
                    CoachVoice.Say("Perfect, I can see all of you. Let's putt.");
                }
            }
            else _fullSince = -1f;
        }

        void Show(FramingStatus status, string hint)
        {
            bool changed = status != Status || hint != Hint;
            Status = status;
            Hint = hint;
            float now = Time.realtimeSinceStartup;
            // The coach says a new hint at once, and the same one again every 8 seconds, never over itself.
            if (hint != null && !CoachVoice.Speaking && (hint != _said || now - _lastSaid > RepeatHint))
            {
                _said = hint;
                _lastSaid = now;
                CoachVoice.Say(hint);
            }
            if (changed) Changed?.Invoke();
        }

        public Color Colour => Confirmed || Status == FramingStatus.Full ? Ui.Bright : Status == FramingStatus.Partial ? Ui.Amber : Ui.Red;
    }
}
