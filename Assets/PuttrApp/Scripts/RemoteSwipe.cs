// A touch-surface remote (the Siri Remote) read as single d-pad steps: one swipe is one step, one way.
//
// The surface arrives twice, as a stick that follows the thumb and as a d-pad that snaps to eight ways (and lands on
// diagonals). The stick decides while it moves. A swipe steps once, on the axis that is further out when it crosses
// the step point; then the whole surface is locked (both axes, not each on its own) until the thumb has been at rest
// for a moment, so a swipe that curves or settles on a diagonal never adds a second step to the side.
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

        bool _locked;
        float _activeAt = -9f, _lastAt = -9f;
        Vector2Int _lastDir;

        /// <summary>A swipe is under way, or only just ended: nothing else should move focus for it.</summary>
        public bool Busy(float now) => _locked || now - _activeAt < BusyFor;

        /// <summary>One frame of the surface. Returns "left", "right", "up" or "down" on the frame a swipe steps, else null.</summary>
        public string Read(Vector2 stick, Vector2 dpad, float now)
        {
            Vector2 d = stick.sqrMagnitude > 0.09f ? stick : dpad;
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), push = Mathf.Max(ax, ay);
            if (push > RestBelow) _activeAt = now;
            if (_locked)
            {
                if (push <= RestBelow && now - _activeAt > RestFor) _locked = false;
                return null;
            }
            if (push < StepAt) return null;
            _locked = true;
            // A d-pad diagonal says nothing about which way: whatever the stick shows breaks the tie.
            if (Mathf.Approximately(ax, ay) && stick != Vector2.zero) { ax = Mathf.Abs(stick.x); ay = Mathf.Abs(stick.y); if (!Mathf.Approximately(ax, ay)) d = stick; }
            Vector2Int dir = ax >= ay ? new Vector2Int(d.x > 0f ? 1 : -1, 0) : new Vector2Int(0, d.y > 0f ? 1 : -1);
            // A swipe that swings back past the middle is not a step back.
            if (dir == -_lastDir && now - _lastAt < NoReverseFor) return null;
            _lastDir = dir;
            _lastAt = now;
            return dir.x < 0 ? "left" : dir.x > 0 ? "right" : dir.y > 0 ? "up" : "down";
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
