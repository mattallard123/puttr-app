// Putts to play with when there is no console in the room.
//
// Recorded: the eight real putts from sdk/samples.js, recorded with the hole
// at [320, 100, 110]. The simulator replays the same eight.
//
// Synthesize: makes up a path for any line and speed, using the documented
// ramp deceleration and pixel scale. The recordings all arrive at 3.2 to 3.7
// feet per second, so this is the only way to try a putt that dies at the
// lip, one that is left short and rolls back, or one hit far too hard.
using System.Collections.Generic;
using UnityEngine;

namespace Puttr
{
    public static class PuttrSamples
    {
        public sealed class Sample
        {
            public readonly string Id, Label;
            public readonly List<BallPoint> Path;
            public Sample(string id, string label, int[] flat)
            {
                Id = id;
                Label = label;
                Path = new List<BallPoint>(flat.Length / 3);
                for (int i = 0; i + 2 < flat.Length; i += 3) Path.Add(new BallPoint(flat[i], flat[i + 1], flat[i + 2]));
            }
        }

        public static readonly Hole SampleHole = new Hole(320, 100, 110);

        public static readonly Sample[] Recorded =
        {
            new Sample("center", "Center cut", new[] { 321,326,13,321,326,13,321,326,13,321,310,19,321,310,19,322,288,21,322,288,21,320,264,23,320,264,23,320,264,23,320,239,25,320,239,25,321,214,26,321,214,26,321,214,26,321,189,27,321,189,27,320,164,27,320,164,27,320,164,27,320,141,27,320,141,27,320,119,26,320,119,26,320,119,26,320,100,23,320,100,23,320,85,20,320,85,20,320,85,20,320,70,17,320,70,17,320,59,11,320,59,11 }),
            new Sample("left-side", "Left center", new[] { 300,330,15,300,330,15,299,313,21,299,313,21,299,313,21,298,290,22,298,290,22,296,266,23,296,266,23,296,266,23,295,241,25,295,241,25,295,241,25,295,215,26,295,215,26,293,190,27,293,190,27,292,165,27,292,165,27,291,142,27,291,142,27,291,142,27,292,120,26,292,120,26,294,101,24,294,101,24,294,101,24,296,87,20,296,87,20,300,73,18,300,73,18,302,63,10,302,63,10,302,63,10 }),
            new Sample("right-side", "Right center", new[] { 328,332,12,328,332,12,328,332,12,329,315,19,329,315,19,331,292,21,331,292,21,331,292,21,333,266,22,333,266,22,333,266,22,334,239,25,334,239,25,336,212,26,336,212,26,336,212,26,337,184,26,337,184,26,337,184,26,339,156,27,339,156,27,341,130,27,341,130,27,341,130,27,341,106,25,341,106,25,341,88,22,341,88,22,341,88,22,338,74,18,338,74,18,338,74,18,338,62,10,338,62,10 }),
            new Sample("left-edge", "Left edge", new[] { 265,325,3,265,325,3,265,325,3,267,300,11,267,300,11,265,275,13,265,275,13,264,247,14,264,247,14,264,247,14,264,220,18,264,220,18,264,220,18,263,193,19,263,193,19,263,193,19,266,165,22,266,165,22,268,142,23,268,142,23,268,142,23,271,116,24,271,116,24,271,116,24,277,95,26,277,95,26,289,78,22,289,78,22,289,78,22,302,68,19,302,68,19,302,68,19,313,67,11,313,67,11 }),
            new Sample("right-edge", "Right edge", new[] { 341,329,14,341,329,14,341,329,14,343,311,21,343,311,21,343,311,21,345,286,23,345,286,23,348,258,26,348,258,26,348,258,26,350,229,28,350,229,28,353,199,29,353,199,29,353,199,29,354,169,29,354,169,29,356,140,29,356,140,29,356,140,29,357,112,29,357,112,29,357,112,29,351,89,27,351,89,27,337,74,24,337,74,24,337,74,24,325,66,18,325,66,18,318,61,7,318,61,7,318,61,7 }),
            new Sample("miss-right", "Miss right", new[] { 362,333,7,362,333,7,362,333,7,364,320,18,364,320,18,368,297,22,368,297,22,368,297,22,370,270,24,370,270,24,373,242,26,373,242,26,373,242,26,374,213,27,374,213,27,374,213,27,376,183,29,376,183,29,377,153,30,377,153,30,377,124,30,377,124,30,377,124,30,375,97,30,375,97,30,369,74,29,369,74,29,360,53,30,360,53,30,353,40,26,353,40,26,353,40,26,347,32,21,347,32,21 }),
            new Sample("wide-left", "Wide left", new[] { 271,331,10,271,331,10,264,312,20,264,312,20,264,312,20,261,283,22,261,283,22,261,283,22,253,253,24,253,253,24,253,253,24,247,221,27,247,221,27,247,221,27,241,187,28,241,187,28,236,151,29,236,151,29,236,151,29,232,118,30,232,118,30,232,118,30,229,86,29,229,86,29,226,55,29,226,55,29,226,55,29,226,55,29,222,36,22,222,36,22,215,29,9,215,29,9,215,29,9 }),
            new Sample("wide-right", "Wide right", new[] { 387,331,11,387,331,11,387,331,11,389,314,21,389,314,21,395,286,23,395,286,23,395,286,23,400,258,26,400,258,26,400,258,26,402,227,28,402,227,28,407,196,30,407,196,30,408,164,31,408,164,31,408,164,31,409,132,31,409,132,31,410,102,31,410,102,31,410,102,31,409,74,30,409,74,30,406,51,28,406,51,28,407,35,21,407,35,21,407,35,21 }),
        };

        /// <summary>
        /// Make up a ball path.
        /// lineInches: where the ball crosses the hole, inches right of center.
        /// speedInPerSec: speed about 3 inches before the hole center. 40 is a
        /// normal putt and 20 dies just past the hole. A negative number is a
        /// putt left short: -14 stops an inch and a half before that point and
        /// rolls back.
        /// headingDeg: direction of travel, positive to the Player's right.
        /// </summary>
        public static List<BallPoint> Synthesize(float lineInches, float speedInPerSec, float headingDeg, Hole hole, float fps = PuttrAnalysis.DefaultFps)
        {
            const float decel = PuttrAnalysis.RampDecelInPerSec2;
            const float startY = 9.4f;    // where a ball comes into view, inches before the hole
            const float lipY = 3f;        // where speedInPerSec is measured
            float dt = 1f / fps;
            float tan = Mathf.Tan(headingDeg * Mathf.Deg2Rad);

            // Speed at the bottom of the view that arrives at lipY with the asked speed.
            float v = Mathf.Sqrt(Mathf.Max(0.01f, speedInPerSec * Mathf.Abs(speedInPerSec) + 2f * decel * (startY - lipY)));
            bool willDrop = Mathf.Abs(lineInches) < 1.7f && speedInPerSec > 0f && speedInPerSec < 64f;

            var frames = new List<Vector3>(); // x, y in inches, z = radius scale
            float y = startY;
            bool rollingBack = false;
            for (int i = 0; i < 80; i++)
            {
                float x = lineInches - y * tan;
                frames.Add(new Vector3(x, y, i == 0 ? 0.5f : 1f));

                if (!rollingBack)
                {
                    float next = v - decel * dt;
                    if (next <= 0) { rollingBack = true; v = 0; }
                    else { y -= (v + next) * 0.5f * dt; v = next; }
                }
                else
                {
                    v += decel * dt;
                    y += v * dt;
                    if (y > startY + 0.5f) break;
                }

                if (!rollingBack && willDrop && Mathf.Sqrt(x * x + y * y) < 1.2f)
                {
                    // Dropping: a few more frames drifting to the back of the cup, shrinking.
                    float[] shrink = { 0.85f, 0.7f, 0.55f, 0.35f };
                    for (int k = 0; k < shrink.Length; k++)
                    {
                        y -= 0.62f - k * 0.08f;
                        frames.Add(new Vector3(lineInches * (1f - 0.2f * k) - y * tan, y, shrink[k]));
                    }
                    break;
                }
                if (y < -2.9f) break; // out of the top of the frame
            }

            // To pixels. The console reads faster than the camera delivers, so
            // each frame is repeated two or three times.
            var path = new List<BallPoint>();
            for (int i = 0; i < frames.Count; i++)
            {
                Vector2 px = PuttrAnalysis.ToPixelsIntegrated(new Vector2(frames[i].x, frames[i].y), hole);
                float u = Mathf.Sqrt((px.x - hole.X) * (px.x - hole.X) + (px.y - hole.Y) * (px.y - hole.Y)) / hole.Radius;
                int r = Mathf.Max(3, Mathf.RoundToInt(PuttrAnalysis.ScaleAt(u) * hole.Radius * frames[i].z));
                var point = new BallPoint(Mathf.RoundToInt(px.x), Mathf.RoundToInt(px.y), r);
                int repeats = i % 3 == 1 ? 2 : 3;
                for (int k = 0; k < repeats; k++) path.Add(point);
            }
            return path;
        }

        /// <summary>Something that is not a putt: a hand passing over the hole.</summary>
        public static List<BallPoint> Hand(Hole hole)
        {
            var path = new List<BallPoint>();
            for (int i = 0; i < 9; i++)
            {
                var p = new BallPoint((int)hole.X - 160 + i * 40, (int)hole.Y + 30 - i * 4, 70 + (i % 3) * 6);
                path.Add(p);
                path.Add(p);
            }
            return path;
        }
    }
}
