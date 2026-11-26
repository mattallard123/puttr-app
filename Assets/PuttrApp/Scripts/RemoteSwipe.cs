// A touch-surface remote (the Siri Remote) read as single d-pad steps: one swipe is one step, one way.
//
// The surface arrives twice, as a stick that follows the thumb and as a d-pad that snaps to eight ways (and lands on
// diagonals). The stick decides while it moves. A swipe steps once, on the axis that is further out when it crosses
// the step point; then the whole surface is locked (both axes, not each on its own) until the thumb has been at rest
// for a moment, so a swipe that curves or settles on a diagonal never adds a second step to the side.
//
// Two more, as an Apple TV's own menus read the remote:
//   A flick    a short quick swipe that lifts before it reaches the step point still steps, once, on the lift.
//   The ring   a click on the ring's edge that is held (the d-pad full one way while the thumb has not travelled)
//              repeats after half a second. A thumb resting where its swipe ended has travelled, so it never repeats.
//
// No platform code here: TvPlatform feeds it on an Apple TV, and the tests replay recorded swipes through it
// (the "[remote]" lines a traced build logs, see Tv.Trace).
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PuttrApp
{
    public sealed class RemoteSwipe
    {
        const float StepAt = 0.5f, RestBelow = 0.2f, RestFor = 0.22f, NoReverseFor = 0.35f, BusyFor = 0.4f;
        const float FlickAt = 0.28f, FlickWithin = 0.3f, HoldAfter = 0.5f, HoldEvery = 0.13f, HoldStickBelow = 0.3f;

        bool _locked;
        // The touch under way: when it began, the furthest it has been, and whether it has stepped.
        bool _touching, _stepped;
        float _touchAt;
        Vector2 _peak;
        // A held ring click: which way, since when, and when it last repeated.
        Vector2Int _hold;
        float _holdAt, _holdStepAt;
        float _activeAt = -9f, _lastAt = -9f;
        Vector2Int _lastDir;

        /// <summary>A swipe is under way, or only just ended: nothing else should move focus for it.</summary>
        public bool Busy(float now) => _locked || now - _activeAt < BusyFor;

        /// <summary>One frame of the surface. Returns "left", "right", "up" or "down" on the frame a swipe steps, else null.</summary>
        public string Read(Vector2 stick, Vector2 dpad, float now)
        {
            string step = Swipe(stick, dpad, now);
            string held = Hold(stick, dpad, now, step);
            return step ?? held;
        }

        // A ring click held down: full one way on the d-pad, on one axis, with the thumb where it landed.
        string Hold(Vector2 stick, Vector2 dpad, float now, string stepped)
        {
            bool one = (Mathf.Abs(dpad.x) >= 0.99f) != (Mathf.Abs(dpad.y) >= 0.99f);
            bool still = stick.sqrMagnitude < HoldStickBelow * HoldStickBelow;
            Vector2Int way = one && still ? new Vector2Int(Mathf.Abs(dpad.x) >= 0.99f ? (dpad.x > 0f ? 1 : -1) : 0, Mathf.Abs(dpad.y) >= 0.99f ? (dpad.y > 0f ? 1 : -1) : 0) : Vector2Int.zero;
            if (way != _hold) { _hold = way; _holdAt = now; _holdStepAt = now + HoldAfter - HoldEvery; return null; }
            if (way == Vector2Int.zero || stepped != null || now - _holdAt < HoldAfter || now - _holdStepAt < HoldEvery) return null;
            _holdStepAt = now;
            _lastDir = way; _lastAt = now;
            return Word(way);
        }

        static string Word(Vector2Int dir) => dir.x < 0 ? "left" : dir.x > 0 ? "right" : dir.y > 0 ? "up" : "down";

        string Swipe(Vector2 stick, Vector2 dpad, float now)
        {
            Vector2 d = stick.sqrMagnitude > 0.09f ? stick : dpad;
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), push = Mathf.Max(ax, ay);
            if (push > RestBelow) _activeAt = now;
            // The touch: from the first frame off rest to the first frame back at it.
            bool down = stick != Vector2.zero || dpad != Vector2.zero;
            string flick = null;
            if (down && !_touching) { _touching = true; _stepped = _locked; _touchAt = now; _peak = Vector2.zero; }
            if (down && d.sqrMagnitude > _peak.sqrMagnitude) _peak = d;
            if (!down && _touching)
            {
                _touching = false;
                // Lifted before the step point, having gone far enough fast enough: a flick, one step.
                float px = Mathf.Abs(_peak.x), py = Mathf.Abs(_peak.y);
                if (!_stepped && !_locked && Mathf.Max(px, py) >= FlickAt && now - _touchAt <= FlickWithin && !Mathf.Approximately(px, py))
                {
                    Vector2Int way = px > py ? new Vector2Int(_peak.x > 0f ? 1 : -1, 0) : new Vector2Int(0, _peak.y > 0f ? 1 : -1);
                    if (!(way == -_lastDir && now - _lastAt < NoReverseFor)) { _lastDir = way; _lastAt = now; _activeAt = now; flick = Word(way); }
                }
            }
            if (_locked)
            {
                if (push <= RestBelow && now - _activeAt > RestFor) _locked = false;
                return null;
            }
            if (push < StepAt) return flick;
            _locked = true;
            _stepped = true;
            // A d-pad diagonal says nothing about which way: whatever the stick shows breaks the tie.
            if (Mathf.Approximately(ax, ay) && stick != Vector2.zero) { ax = Mathf.Abs(stick.x); ay = Mathf.Abs(stick.y); if (!Mathf.Approximately(ax, ay)) d = stick; }
            Vector2Int dir = ax >= ay ? new Vector2Int(d.x > 0f ? 1 : -1, 0) : new Vector2Int(0, d.y > 0f ? 1 : -1);
            // A swipe that swings back past the middle is not a step back.
            if (dir == -_lastDir && now - _lastAt < NoReverseFor) return null;
            _lastDir = dir;
            _lastAt = now;
            return Word(dir);
        }

        /// <summary>One frame as the trace writes it: "time,stickX,stickY,dpadX,dpadY".</summary>
        public static string Frame(float now, Vector2 stick, Vector2 dpad) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.##},{2:0.##},{3:0.##},{4:0.##}", now, stick.x, stick.y, dpad.x, dpad.y);

        /// <summary>The steps a recorded swipe gives: frames as Frame writes them, joined by ";" (the part of a "[remote]" line after the "|").</summary>
        public static List<string> Replay(string frames)
        {
            var swipe = new RemoteSwipe();
            var steps = new List<string>();
            foreach (string frame in frames.Split(';'))
            {
                string[] p = frame.Trim().Split(',');
                if (p.Length != 5) continue;
                float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
                string step = swipe.Read(new Vector2(F(1), F(2)), new Vector2(F(3), F(4)), F(0));
                if (step != null) steps.Add(step);
            }
            return steps;
        }
    }
}
