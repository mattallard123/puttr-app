// puttr-app-2: what the ball does at the hole, worked out ahead of time so it can be played back.
//
// The ramp's collider is solid over the hole (the cup is only drawn), so the last few inches of a putt are
// simulated here instead: a 1.68 in ball on the 12 degree ramp, the 4.25 in hole's rim, the cup's wall and
// the through-hole at its bottom, the way the real Puttr is built. A slow ball topples over the front lip and
// drops; a quick one flies the gap, hits the far wall and rattles down; a quicker one bounces off the far rim
// and hops out; one that catches the edge rides round the rim and spins away. A ball that drops rolls out of
// the back of the cup, under the ramp, into the box.
//
// The result is never decided here: the app's analysis has already said made or missed. Arcade asks for a run
// that agrees with it (Holed / LipOut) and gets the nearest one to the measured putt that does.
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public static class CupPhysics
    {
        public enum End { Dropped, Escaped, Timeout }

        /// <summary>One simulated run, sampled every Step seconds.</summary>
        public sealed class Run
        {
            public readonly List<Vector3> Centers = new List<Vector3>();     // world
            public readonly List<Quaternion> Spins = new List<Quaternion>(); // turned since the start
            public readonly List<float> Speeds = new List<float>();          // for the roll sound, 0 off the carpet
            public readonly List<int> Knocks = new List<int>();              // samples where it hit the rim or the wall hard
            public readonly List<float> KnockSpeeds = new List<float>();
            public End Ended;
            public int DropAt = -1;                                           // the sample where it went below the lip for good
            public Vector3 ExitVelocity;                                      // world, when it Escaped
            public float Seconds => Centers.Count * Step;
        }

        public const float Step = 1f / 600f;
        const float PuttrSlow = 64f; // in/s^2 up the ramp (PuttrAnalysis.RampDecelInPerSec2)
        const float R = LaneSpec.BallRadius, Rh = LaneSpec.HoleRadius, G = LaneSpec.Gravity;
        const float RimBounce = 0.18f, WallBounce = 0.42f, FloorBounce = 0.25f;
        const float FloorH = -2.3f;        // the ball's center on the cup's floor, below the ramp's surface
        const float BackOpenBelow = -1.25f; // the back of the cup is open below this (the through-hole)

        static readonly float Sin = Mathf.Sin(LaneSpec.RampAngleDeg * Mathf.Deg2Rad), Cos = Mathf.Cos(LaneSpec.RampAngleDeg * Mathf.Deg2Rad);
        // The ramp's own axes at the hole: x across, s up the slope, n out of the surface.
        static readonly Vector3 Ex = Vector3.right, Es = new Vector3(0f, Sin, Cos), En = new Vector3(0f, Cos, -Sin);

        public static Vector3 ToWorld(Vector3 local) => Ex * local.x + Es * local.y + En * local.z;
        public static Vector3 ToLocal(Vector3 world) => new Vector3(Vector3.Dot(world, Ex), Vector3.Dot(world, Es), Vector3.Dot(world, En));

        /// <summary>
        /// A holed putt: from the handoff toward the measured entry point (kept within a few degrees of the
        /// measured heading). If that run does not drop, the aim is drawn a little further into the cup and the
        /// pace eased a little at a time until it does. Null if it never does.
        /// </summary>
        public static Run Holed(Vector3 from, Vector2 entryInches, float headingDeg, float speed)
        {
            Vector3 start = ToLocal(from);
            start.z = R;
            var entry = new Vector2(entryInches.x, -entryInches.y / Cos); // console inches (y toward the Player) to x, s
            Vector3 headingLocal = ToLocal(LaneSpec.SurfaceDirection(headingDeg, from.z));
            float heading = Mathf.Atan2(headingLocal.x, headingLocal.y) * Mathf.Rad2Deg;
            // Never so slow it stops short of tipping over the front lip (it was holed, so it got there).
            float least = Mathf.Sqrt(2f * PuttrSlow * Mathf.Max(0.5f, new Vector2(start.x, start.y).magnitude - Rh + 0.9f)) + 1f;
            speed = Mathf.Max(speed, least);
            for (int k = 0; k < 16; k++)
            {
                float reach = Mathf.Min(entry.magnitude, Rh - 0.55f) * Mathf.Pow(0.82f, k);
                Vector2 target = entry.sqrMagnitude > 0.01f ? entry.normalized * reach : Vector2.zero;
                Vector2 to = target - new Vector2(start.x, start.y);
                float aim = to.sqrMagnitude > 0.0001f ? Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg : heading;
                aim = heading + Mathf.Clamp(Mathf.DeltaAngle(heading, aim), -12f, 12f);
                if (k >= 8) aim = Mathf.Atan2(-start.x, -start.y) * Mathf.Rad2Deg; // straight at the middle
                float v = Mathf.Max(least, speed * Mathf.Pow(0.9f, k));
                float r = aim * Mathf.Deg2Rad;
                Run run = Simulate(start, new Vector3(Mathf.Sin(r), Mathf.Cos(r), 0f) * v, true);
                if (run.Ended == End.Dropped) return run;
            }
            return null;
        }

        /// <summary>A lip-out: rolled on its line; if that drops, nudged outward until it stays out.</summary>
        public static Run LipOut(Vector3 from, float headingDeg, float speed, float side)
        {
            Vector3 start = ToLocal(from);
            start.z = R;
            Vector3 world = LaneSpec.SurfaceDirection(headingDeg, from.z);
            Vector3 v = ToLocal(world);
            v.z = 0f;
            v = v.normalized * Mathf.Max(speed, 4f);
            if (side == 0f) side = start.x >= 0f ? 1f : -1f;
            for (int k = 0; k < 24; k++)
            {
                Vector3 s = start + new Vector3(side * 0.07f * k, 0f, 0f);
                Run run = Simulate(s, v, false);
                if (run.Ended == End.Escaped) return run;
            }
            return null;
        }

        /// <summary>Roll the ball from a local start (x, s, height) with a local velocity until it drops out of sight or gets clear of the hole.</summary>
        public static Run Simulate(Vector3 p, Vector3 v, bool canDrop)
        {
            var run = new Run();
            Quaternion spin = Quaternion.identity;
            Vector3 w = Vector3.zero; // angular velocity, world axes
            bool dropped = false;
            float stillFor = 0f;
            int max = Mathf.RoundToInt(5f / Step);
            for (int i = 0; i < max; i++)
            {
                float d = Mathf.Sqrt(p.x * p.x + p.y * p.y);
                bool onCarpet = d >= Rh && p.z >= 0f && p.z <= R + 0.002f; // on top of the carpet, not under it at the back of the cup
                Vector3 a;
                if (onCarpet)
                {
                    // Rolling: (5/7) of the slope's pull, plus the carpet's own drag.
                    p.z = R;
                    if (v.z < 0f) v.z = 0f;
                    a = new Vector3(0f, -(5f / 7f) * G * Sin, 0f);
                    var flat = new Vector2(v.x, v.y);
                    if (flat.sqrMagnitude > 1e-6f) { Vector2 drag = -flat.normalized * LaneSpec.RollingResistance; a.x += drag.x; a.y += drag.y; }
                }
                else a = new Vector3(0f, -G * Sin, -G * Cos);
                v += a * Step;
                p += v * Step;

                // Contacts.
                d = Mathf.Sqrt(p.x * p.x + p.y * p.y);
                Vector2 radial = d > 1e-5f ? new Vector2(p.x, p.y) / d : Vector2.up;
                bool rolling = false;
                Vector3 contactN = Vector3.zero;
                if (p.z >= 0f)
                {
                    if (d >= Rh)
                    {
                        if (p.z < R) { p.z = R; if (v.z < 0f) v.z = -v.z * 0.1f; rolling = true; contactN = new Vector3(0, 0, 1); }
                    }
                    // The rim: a circle, the nearest point of it to the ball's center.
                    var rim = new Vector3(radial.x * Rh, radial.y * Rh, 0f);
                    Vector3 q = p - rim;
                    float m = q.magnitude;
                    if (m < R && m > 1e-5f)
                    {
                        Vector3 n = q / m;
                        p = rim + n * R;
                        float vn = Vector3.Dot(v, n);
                        if (vn < 0f)
                        {
                            if (-vn > 9f) { run.Knocks.Add(run.Centers.Count); run.KnockSpeeds.Add(-vn); }
                            v -= (1f + RimBounce) * vn * n;
                        }
                        rolling = true;
                        contactN = n;
                    }
                }
                else
                {
                    // Inside the cup: its wall, open at the back near the bottom (the through-hole).
                    bool backOpen = p.z < BackOpenBelow && p.y > 0f;
                    if (d > Rh - R && !backOpen)
                    {
                        p.x = radial.x * (Rh - R); p.y = radial.y * (Rh - R);
                        var n = new Vector3(-radial.x, -radial.y, 0f);
                        float vn = Vector3.Dot(v, n);
                        if (vn < 0f)
                        {
                            if (-vn > 8f) { run.Knocks.Add(run.Centers.Count); run.KnockSpeeds.Add(-vn); }
                            v -= (1f + WallBounce) * vn * n;
                            v.z *= 0.9f; // the hit takes some of the fall out of it
                            w *= 0.7f;
                        }
                    }
                    if (backOpen) p.x = Mathf.Clamp(p.x, -(Rh - R), Rh - R); // the chute is no wider than the cup
                    if (p.z < FloorH)
                    {
                        p.z = FloorH;
                        if (v.z < 0f) v.z = -v.z * FloorBounce;
                        // The floor runs down to the back: out it goes.
                        v.y += 160f * Step;
                        v.x *= 0.98f;
                        rolling = true;
                        contactN = new Vector3(0, 0, 1);
                    }
                }

                // Once it is under the lip it stays under: the carpet is over it at the back.
                if (dropped && p.z > -R * 1.05f) { p.z = -R * 1.05f; if (v.z > 0f) v.z = 0f; }

                // Spin (in world axes: the ramp's x, s, n are mirrored): rolling on whatever it touches, or
                // keeping what it had in the air.
                if (rolling) w = Vector3.Cross(ToWorld(contactN), ToWorld(v)) / R;
                Vector3 wWorld = w;
                float angle = wWorld.magnitude * Step * Mathf.Rad2Deg;
                if (angle > 1e-4f) spin = Quaternion.AngleAxis(angle, wWorld.normalized) * spin;

                run.Centers.Add(ToWorld(p));
                run.Spins.Add(spin);
                run.Speeds.Add(onCarpet ? new Vector2(v.x, v.y).magnitude : 0f);

                if (!dropped && p.z < -R * 1.05f) { dropped = true; run.DropAt = run.Centers.Count - 1; }
                // Gone under the ramp at the back of the cup.
                if (dropped && p.y > Rh + R + 0.3f) { run.Ended = End.Dropped; return run; }
                if (!dropped && p.z >= R - 0.01f && d > Rh + R * 0.75f)
                {
                    // Clear of the hole on the carpet, going away from it (or stopped): physics can have it.
                    float away = Vector2.Dot(new Vector2(v.x, v.y), radial);
                    stillFor = v.magnitude < 1.5f ? stillFor + Step : 0f;
                    if (away > 0f || stillFor > 0.15f || p.y * Cos > LaneSpec.RampEndZ - 1f)
                    {
                        run.Ended = End.Escaped;
                        run.ExitVelocity = ToWorld(new Vector3(v.x, v.y, 0f));
                        return run;
                    }
                }
                if (!canDrop && dropped) { run.Ended = End.Dropped; return run; } // a lip-out run that dropped is no use
            }
            run.Ended = dropped ? End.Dropped : End.Timeout;
            return run;
        }
    }
}
