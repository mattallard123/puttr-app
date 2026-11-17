// Putt analysis: a C# port of the putt rules in sdk/puttr.js, the same rules
// the Puttr app uses. Every function matches its JavaScript counterpart on the
// recorded sample putts (see Assets/Puttr/Tests).
//
// One addition for game engines: Launch() turns the camera's view of the
// approach into a position, heading and speed you can hand to a rigidbody.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Puttr
{
    public static class PuttrAnalysis
    {
        public const float BallDiameterIn = 1.68f;
        public const float HoleDiameterIn = 4.25f;
        /// <summary>Typical camera frame rate. Paths carry no timestamps.</summary>
        public const float DefaultFps = 38f;
        /// <summary>After any path, ignore further paths for this long.</summary>
        public const float RepeatGateSeconds = 2.5f;
        /// <summary>How hard the ramp slows the ball, inches per second squared.</summary>
        public const float RampDecelInPerSec2 = 64f;
        public const float RampAngleDeg = 12f;

        // ── Parsing ─────────────────────────────────────────────────────────

        /// <summary>
        /// Parse a ball path: the JSON string the WiFi socket sends,
        /// "[[340, 299, 13], [340, 283, 19], ...]". Returns null when it
        /// cannot be parsed.
        /// </summary>
        public static List<BallPoint> ParsePath(string raw)
        {
            if (!PuttrJson.TryParse(raw, out object value)) return null;
            return ParsePath(value);
        }

        public static List<BallPoint> ParsePath(object value)
        {
            if (value is string s) return ParsePath(s);
            if (!(value is List<object> list)) return null;
            var path = new List<BallPoint>(list.Count);
            foreach (object item in list)
            {
                if (!(item is List<object> p) || p.Count < 2) continue;
                double r = p.Count > 2 ? PuttrJson.Number(p[2], 0) : 0;
                path.Add(new BallPoint(RoundJs(PuttrJson.Number(p[0], 0)), RoundJs(PuttrJson.Number(p[1], 0)), RoundJs(r)));
            }
            return path;
        }

        /// <summary>
        /// Parse the hole. WiFi sends {"x":320,"y":100,"diameter":110};
        /// Bluetooth sends "[320, 100, 110]". Both work.
        /// </summary>
        public static bool TryParseHole(string raw, out Hole hole)
        {
            hole = default;
            if (!PuttrJson.TryParse(raw, out object value)) return false;
            double x, y, d;
            if (value is List<object> a && a.Count >= 3)
            {
                x = PuttrJson.Number(a[0]); y = PuttrJson.Number(a[1]); d = PuttrJson.Number(a[2]);
            }
            else if (value is Dictionary<string, object> o)
            {
                x = o.TryGetValue("x", out object ox) ? PuttrJson.Number(ox) : double.NaN;
                y = o.TryGetValue("y", out object oy) ? PuttrJson.Number(oy) : double.NaN;
                d = o.TryGetValue("diameter", out object od) ? PuttrJson.Number(od) : double.NaN;
            }
            else return false;
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(d)) return false;
            hole = new Hole((float)x, (float)y, (float)d);
            return true;
        }

        /// <summary>
        /// True when the hole is where a correctly set up console reports it.
        /// Outside this range the mat is probably rolled up or the camera is
        /// blocked, and putt results should not be trusted.
        /// </summary>
        public static bool IsHoleCalibrated(Hole h)
        {
            return h.X >= 270 && h.X <= 370 && h.Y >= 30 && h.Y <= 170 && h.Diameter >= 80 && h.Diameter <= 130;
        }

        // ── Putt rules ──────────────────────────────────────────────────────

        static double Dist(BallPoint p, Hole h)
        {
            double dx = p.X - h.X, dy = p.Y - h.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Consecutive identical points are the console reading one camera
        /// frame more than once. Removing them leaves one point per frame,
        /// which is the only usable clock.
        /// </summary>
        public static List<BallPoint> DistinctFrames(IReadOnlyList<BallPoint> path)
        {
            var frames = new List<BallPoint>();
            foreach (BallPoint p in path)
            {
                if (frames.Count > 0)
                {
                    BallPoint prev = frames[frames.Count - 1];
                    if (prev.X == p.X && prev.Y == p.Y && prev.R == p.R) continue;
                }
                frames.Add(p);
            }
            return frames;
        }

        /// <summary>
        /// Is this a golf ball rolling up the ramp? Rejects hands, shoes,
        /// putter heads and reflections: the blob must be ball sized and must
        /// have been seen on the Player's side of the hole.
        /// </summary>
        public static bool IsBall(IReadOnlyList<BallPoint> path, Hole h)
        {
            if (path == null || path.Count == 0) return false;
            int maxRadius = 0, nearestToPlayer = 0;
            foreach (BallPoint p in path)
            {
                if (p.R > maxRadius) maxRadius = p.R;
                if (p.Y > nearestToPlayer) nearestToPlayer = p.Y;
            }
            if (nearestToPlayer < h.Y + h.Radius) return false;
            return maxRadius >= 10 && maxRadius <= 60;
        }

        /// <summary>
        /// How many of the last (up to 10) points are inside the hole,
        /// counting back from the end. Repeated points count: do not remove
        /// them first.
        /// </summary>
        public static int PointsInHoleAtEnd(IReadOnlyList<BallPoint> path, Hole h)
        {
            if (path == null) return 0;
            int limit = Math.Min(10, path.Count), count = 0;
            for (int i = path.Count - 1; i >= path.Count - limit; i--)
            {
                if (Dist(path[i], h) < h.Radius + 10) count++;
                else break;
            }
            return count;
        }

        /// <summary>Made when the path ends in the hole.</summary>
        public static bool IsMade(IReadOnlyList<BallPoint> path, Hole h) => PointsInHoleAtEnd(path, h) > 1;

        /// <summary>
        /// Where the ball crossed the lip: the first point inside the hole.
        /// The angle is degrees around the hole from the front: 0 dead center,
        /// +90 the right edge, -90 the left edge, beyond that the back door.
        /// </summary>
        public static bool TryHoleEntry(IReadOnlyList<BallPoint> path, Hole h, out BallPoint point, out int angle)
        {
            foreach (BallPoint p in path)
            {
                if (Dist(p, h) < h.Radius)
                {
                    point = p;
                    angle = RoundJs(Math.Atan2(p.X - h.X, p.Y - h.Y) * (180.0 / Math.PI));
                    return true;
                }
            }
            point = default;
            angle = 0;
            return false;
        }

        public static string EntryDescription(int angle)
        {
            int a = Math.Abs(angle);
            if (a < 18) return "CENTER CUT";
            if (a >= 90) return "BACK DOOR";
            string side = angle > 0 ? "RIGHT" : "LEFT";
            return a < 54 ? side + " CENTER" : side + " SIDE";
        }

        /// <summary>
        /// The entry angle of a dead straight putt. distanceFt is 3 to 11; tee
        /// is 1 (left), 2 (center) or 3 (right). Side tees are 8 inches off
        /// the center line.
        /// </summary>
        public static float ExpectedEntryAngle(float distanceFt, int tee)
        {
            float offset = tee == 1 ? -8f : tee == 3 ? 8f : 0f;
            return RoundJs(Math.Atan(offset / (distanceFt * 12.0)) * (180.0 / Math.PI)) * 1.1f;
        }

        /// <summary>The path point nearest the hole center, and its distance in pixels.</summary>
        public static bool TryClosestApproach(IReadOnlyList<BallPoint> path, Hole h, out BallPoint point, out float distance)
        {
            point = default;
            distance = float.PositiveInfinity;
            if (path == null || path.Count == 0) return false;
            foreach (BallPoint p in path)
            {
                float d = (float)Dist(p, h);
                if (d < distance) { distance = d; point = p; }
            }
            return true;
        }

        /// <summary>
        /// Which side a miss went by on. None when the closest point is too
        /// central to call (within 30% of the hole radius).
        /// </summary>
        public static MissSide GetMissSide(IReadOnlyList<BallPoint> path, Hole h)
        {
            if (!TryClosestApproach(path, h, out BallPoint near, out _)) return MissSide.None;
            float offset = near.X - h.X;
            if (Mathf.Abs(offset) < h.Radius * 0.3f) return MissSide.None;
            return offset > 0 ? MissSide.Right : MissSide.Left;
        }

        // ── Pixels to inches ────────────────────────────────────────────────
        //
        // The camera looks down a ramp, so an inch covers more pixels near the
        // hole than at the bottom of the frame. Each row is [distance from the
        // hole center in hole radii, apparent ball radius in hole radii].
        static readonly float[,] ScaleCurve =
        {
            { 1.00f, 0.404f }, { 1.25f, 0.407f }, { 1.50f, 0.407f }, { 1.75f, 0.397f },
            { 2.00f, 0.400f }, { 2.25f, 0.392f }, { 2.50f, 0.388f }, { 2.75f, 0.382f },
            { 3.00f, 0.368f }, { 3.25f, 0.358f }, { 3.50f, 0.345f }, { 3.75f, 0.321f },
            { 4.00f, 0.315f }, { 4.25f, 0.296f }, { 4.50f, 0.275f }, { 4.75f, 0.271f }, { 5.00f, 0.269f },
        };

        /// <summary>Apparent ball radius, in hole radii, at u hole radii from the hole center.</summary>
        public static float ScaleAt(float u)
        {
            int n = ScaleCurve.GetLength(0);
            if (u <= ScaleCurve[0, 0]) return ScaleCurve[0, 1];
            for (int i = 1; i < n; i++)
            {
                if (u <= ScaleCurve[i, 0])
                {
                    float u0 = ScaleCurve[i - 1, 0], r0 = ScaleCurve[i - 1, 1];
                    return r0 + (ScaleCurve[i, 1] - r0) * (u - u0) / (ScaleCurve[i, 0] - u0);
                }
            }
            return ScaleCurve[n - 1, 1];
        }

        /// <summary>Pixels per inch at a pixel distance from the hole center.</summary>
        public static float PixelsPerInch(float distanceFromHolePx, float holeRadiusPx)
        {
            return ScaleAt(distanceFromHolePx / holeRadiusPx) * holeRadiusPx / (BallDiameterIn * 0.5f);
        }

        /// <summary>
        /// One camera point in inches from the hole center: x to the Player's
        /// right, y back toward the Player.
        /// </summary>
        public static Vector2 ToInches(float px, float py, Hole h)
        {
            float dx = px - h.X, dy = py - h.Y;
            float ppi = PixelsPerInch(Mathf.Sqrt(dx * dx + dy * dy), h.Radius);
            return new Vector2(dx / ppi, dy / ppi);
        }

        public static Vector2 ToInches(BallPoint p, Hole h) => ToInches(p.X, p.Y, h);

        /// <summary>The inverse of ToInches: where a point in inches appears in the camera frame.</summary>
        public static Vector2 ToPixels(Vector2 inches, Hole h)
        {
            float ppi = PixelsPerInch(h.Radius, h.Radius);
            Vector2 px = default;
            for (int i = 0; i < 12; i++)
            {
                px = new Vector2(inches.x * ppi, inches.y * ppi);
                ppi = PixelsPerInch(px.magnitude, h.Radius);
            }
            return new Vector2(h.X + px.x, h.Y + px.y);
        }

        // ToInches divides a point's whole offset by the scale where the point
        // is. Across the ramp that is right: the scale at a point is the scale
        // for sideways distances there. Along the ramp it is not, because the
        // scale changes on the way: a point at the bottom of the frame comes
        // out about a quarter too far from the hole, and speeds worked out
        // from positions like that come out too high and falling too fast.
        // To place a ball in a 3D world, add the scale up along the ramp:
        //
        //   inches(d) = integral from 0 to d of dpx / pixelsPerInch(px)
        //
        // In hole radii this is the same for every console, so it is one table.
        const float RadialStep = 0.01f;
        static float[] _radial;

        static float[] RadialTable()
        {
            if (_radial != null) return _radial;
            var table = new float[801]; // u from 0 to 8 hole radii
            for (int i = 1; i < table.Length; i++)
            {
                float u = (i - 0.5f) * RadialStep;
                table[i] = table[i - 1] + BallDiameterIn * 0.5f * RadialStep / ScaleAt(u);
            }
            return _radial = table;
        }

        /// <summary>True distance in inches for a pixel distance from the hole center.</summary>
        public static float RadialInches(float distanceFromHolePx, float holeRadiusPx)
        {
            float[] table = RadialTable();
            float f = Mathf.Clamp(distanceFromHolePx / holeRadiusPx / RadialStep, 0, table.Length - 1.001f);
            int i = (int)f;
            return Mathf.Lerp(table[i], table[i + 1], f - i);
        }

        /// <summary>
        /// A camera point in inches from the hole center, for placing the ball
        /// in a 3D scene: x by the scale at the point, y with the changing
        /// scale added up along the ramp. Steps between points placed this way
        /// agree with ApproachSpeed.
        /// </summary>
        public static Vector2 ToInchesIntegrated(float px, float py, Hole h)
        {
            float dx = px - h.X, dy = py - h.Y;
            float ppi = PixelsPerInch(Mathf.Sqrt(dx * dx + dy * dy), h.Radius);
            return new Vector2(dx / ppi, Mathf.Sign(dy) * RadialInches(Mathf.Abs(dy), h.Radius));
        }

        public static Vector2 ToInchesIntegrated(BallPoint p, Hole h) => ToInchesIntegrated(p.X, p.Y, h);

        /// <summary>The inverse of ToInchesIntegrated.</summary>
        public static Vector2 ToPixelsIntegrated(Vector2 inches, Hole h)
        {
            // Along the ramp: look the distance up in the table, backwards.
            float[] table = RadialTable();
            float s = Mathf.Abs(inches.y);
            int lo = 0, hi = table.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (table[mid] < s) lo = mid; else hi = mid;
            }
            float dy = (lo + Mathf.InverseLerp(table[lo], table[hi], s)) * RadialStep * h.Radius * Mathf.Sign(inches.y);
            // Across it: the scale depends on the answer, so close in on it.
            float dx = inches.x * PixelsPerInch(Mathf.Abs(dy), h.Radius);
            for (int i = 0; i < 8; i++) dx = inches.x * PixelsPerInch(Mathf.Sqrt(dx * dx + dy * dy), h.Radius);
            return new Vector2(h.X + dx, h.Y + dy);
        }

        // ── Speed ───────────────────────────────────────────────────────────

        /// <summary>
        /// Speed as the ball approaches the hole, in inches per second, or
        /// null for a very short path. Assumes fps camera frames per second.
        /// </summary>
        public static float? ApproachSpeed(IReadOnlyList<BallPoint> path, Hole h, float fps = DefaultFps)
        {
            if (path == null) return null;
            List<BallPoint> frames = DistinctFrames(path);
            frames.RemoveAll(p => p.R < 4);
            int end = frames.Count;
            int farthest = frames.Count > 0 ? frames[0].Y : 0;
            for (int i = 1; i < frames.Count; i++)
            {
                if (Dist(frames[i], h) < h.Radius * 1.4) { end = i; break; }
                farthest = Math.Min(farthest, frames[i - 1].Y);
                if (frames[i].Y > farthest + 3) { end = i; break; }
            }
            if (end < 4) return null;
            return MedianStep(frames, end, h) * fps;
        }

        // Median of the last five steps between frames [0, end), in inches.
        static float MedianStep(List<BallPoint> frames, int end, Hole h)
        {
            var steps = new List<float>();
            for (int i = 1; i < end; i++)
            {
                BallPoint a = frames[i - 1], b = frames[i];
                float ppi = (PixelsPerInch((float)Dist(a, h), h.Radius) + PixelsPerInch((float)Dist(b, h), h.Radius)) * 0.5f;
                float dx = b.X - a.X, dy = b.Y - a.Y;
                steps.Add(Mathf.Sqrt(dx * dx + dy * dy) / ppi);
            }
            if (steps.Count == 0) return 0;
            List<float> tail = steps.GetRange(Math.Max(0, steps.Count - 5), Math.Min(5, steps.Count));
            tail.Sort();
            return tail[tail.Count / 2];
        }

        // ── Launch: from camera frames to a physics engine ──────────────────

        /// <summary>
        /// Where the ball was and how it was moving when it reached the front
        /// of the hole.
        ///
        /// The approach is every camera frame before the ball gets within 1.4
        /// hole radii of the hole's row, which is where the hole starts to
        /// change what the ball does. Positions use ToInchesIntegrated. Speed
        /// is the median of the last five steps between them (the recipe
        /// ApproachSpeed uses); heading and line come from a straight line
        /// fitted through the approach.
        /// </summary>
        public static PuttLaunch Launch(IReadOnlyList<BallPoint> path, Hole h, float fps = DefaultFps)
        {
            var launch = new PuttLaunch { ApproachInches = Array.Empty<Vector2>() };
            if (path == null || path.Count == 0) return launch;

            List<BallPoint> frames = DistinctFrames(path);
            frames.RemoveAll(p => p.R < 4);
            if (frames.Count == 0) return launch;

            float line = h.Y + h.Radius * 1.4f;
            int end = frames.Count;
            int farthest = frames[0].Y;
            bool reached = frames[0].Y < line;
            if (reached) end = 1;
            for (int i = 1; i < frames.Count && !reached; i++)
            {
                if (frames[i].Y < line) { end = i; reached = true; break; }
                farthest = Math.Min(farthest, frames[i - 1].Y);
                if (frames[i].Y > farthest + 3) break; // rolling back down the ramp
            }
            launch.Reached = reached;

            if (!reached)
            {
                // Left short: keep the whole path so it can be replayed, roll back and all.
                launch.ApproachInches = frames.ConvertAll(p => ToInchesIntegrated(p, h)).ToArray();
                launch.LineAtHoleInches = launch.ApproachInches[launch.ApproachInches.Length - 1].x;
                return launch;
            }

            var pts = new Vector2[end];
            for (int i = 0; i < end; i++) pts[i] = ToInchesIntegrated(frames[i], h);
            launch.ApproachInches = pts;
            launch.SpeedInPerSec = MedianOfLast(pts, 5) * fps;
            // The median step is about two and a half frames old by the last
            // frame, and the ramp has been slowing the ball all that time.
            float framesOld = Mathf.Min(5, end - 1) * 0.5f;
            launch.HandoffSpeedInPerSec = Mathf.Max(0f, launch.SpeedInPerSec - RampDecelInPerSec2 * framesOld / fps);

            // Least squares line x = a + b * y through the approach.
            Vector2 last = pts[end - 1];
            launch.LineAtHoleInches = last.x;
            if (end >= 3)
            {
                float my = 0, mx = 0;
                foreach (Vector2 p in pts) { mx += p.x; my += p.y; }
                mx /= end; my /= end;
                float sxy = 0, syy = 0;
                foreach (Vector2 p in pts) { sxy += (p.x - mx) * (p.y - my); syy += (p.y - my) * (p.y - my); }
                if (syy > 1e-4f)
                {
                    float b = sxy / syy;
                    // y shrinks as the ball rolls forward, so drifting right means x rises as y falls.
                    launch.HeadingDeg = Mathf.Clamp(Mathf.Atan(-b) * Mathf.Rad2Deg, -20f, 20f);
                    launch.LineAtHoleInches = mx - b * my;
                }
            }
            return launch;
        }

        // Median length of the last `count` steps between points.
        static float MedianOfLast(Vector2[] pts, int count)
        {
            var steps = new List<float>();
            for (int i = Mathf.Max(1, pts.Length - count); i < pts.Length; i++) steps.Add(Vector2.Distance(pts[i - 1], pts[i]));
            if (steps.Count == 0) return 0f;
            steps.Sort();
            return steps[steps.Count / 2];
        }

        // ── All together ────────────────────────────────────────────────────

        /// <summary>
        /// Everything about one putt. distanceFt and tee are optional: the
        /// console cannot see where the Player is standing, so pass them when
        /// your app knows.
        /// </summary>
        public static PuttResult Analyze(IReadOnlyList<BallPoint> path, Hole hole, float distanceFt = 0, int tee = 2, float fps = DefaultFps)
        {
            if (path == null || path.Count == 0) return new PuttResult { Valid = false, Reason = "no-data" };
            if (!IsBall(path, hole)) return new PuttResult { Valid = false, Reason = "not-a-ball", Path = path, Hole = hole };

            var result = new PuttResult { Valid = true, Path = path, Hole = hole };
            result.Made = IsMade(path, hole);

            if (result.Made && TryHoleEntry(path, hole, out BallPoint entry, out int angle))
            {
                result.HasEntry = true;
                result.EntryPoint = entry;
                result.EntryAngle = angle;
                result.EntryDescription = EntryDescription(angle);
                if (distanceFt > 0)
                {
                    result.ExpectedEntryAngle = ExpectedEntryAngle(distanceFt, tee);
                    result.OffCenter = angle - result.ExpectedEntryAngle;
                }
            }

            float? speed = ApproachSpeed(path, hole, fps);
            result.HasSpeed = speed.HasValue;
            result.SpeedInPerSec = speed ?? 0;

            if (!result.Made)
            {
                result.MissSide = GetMissSide(path, hole);
                TryClosestApproach(path, hole, out _, out float nearPx);
                result.MissByInches = Mathf.Max(0, nearPx / PixelsPerInch(nearPx, hole.Radius) - HoleDiameterIn * 0.5f);
            }

            List<BallPoint> frames = DistinctFrames(path);
            result.Frames = frames.Count;
            result.PathInches = frames.ConvertAll(p => ToInches(p, hole)).ToArray();
            result.Launch = Launch(path, hole, fps);
            return result;
        }

        // JavaScript's Math.round: halves round up, not to even.
        static int RoundJs(double v) => (int)Math.Floor(v + 0.5);
    }
}
