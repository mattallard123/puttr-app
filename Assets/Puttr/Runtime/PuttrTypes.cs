// The data a Puttr console gives you, and the result of reading one putt.
//
// Coordinates are camera pixels in a 640 x 480 frame, seen from above with the
// Player at the bottom: +x is the Player's right, and y gets SMALLER as the
// ball rolls away from the Player toward the hole.
using System.Collections.Generic;
using UnityEngine;

namespace Puttr
{
    /// <summary>The ball in one reading of the camera: [x, y, radius] in pixels.</summary>
    public readonly struct BallPoint
    {
        public readonly int X, Y, R;
        public BallPoint(int x, int y, int r) { X = x; Y = y; R = r; }
        public override string ToString() => "[" + X + ", " + Y + ", " + R + "]";
    }

    /// <summary>The hole in camera pixels. The console reports the DIAMETER.</summary>
    public readonly struct Hole
    {
        public readonly float X, Y, Diameter;
        public float Radius => Diameter * 0.5f;
        public Hole(float x, float y, float diameter) { X = x; Y = y; Diameter = diameter; }

        /// <summary>Where a correctly set up console usually finds the hole.</summary>
        public static readonly Hole Typical = new Hole(320, 100, 110);

        public override string ToString() => "[" + X + ", " + Y + ", " + Diameter + "]";
    }

    public enum MissSide { None, Left, Right }

    /// <summary>
    /// Where the ball was and how it was moving as it reached the hole, in
    /// inches, ready to hand to a physics engine. See PuttrAnalysis.Launch.
    /// </summary>
    public struct PuttLaunch
    {
        /// <summary>False when the ball never got as far as the front of the hole (a putt left short).</summary>
        public bool Reached;
        /// <summary>
        /// One position per camera frame of the approach, in inches from the
        /// hole center: x to the Player's right, y back toward the Player.
        /// The last one is where to hand the ball to your physics.
        /// For a putt left short this is the whole path, roll back included.
        /// </summary>
        public Vector2[] ApproachInches;
        /// <summary>Speed over the last frames of the approach, inches per second.</summary>
        public float SpeedInPerSec;
        /// <summary>Speed at the last approach frame, after the ramp has slowed the ball a little more.</summary>
        public float HandoffSpeedInPerSec;
        /// <summary>Direction of travel in degrees. 0 is straight up the mat, positive is to the Player's right.</summary>
        public float HeadingDeg;
        /// <summary>Where the fitted line crosses the hole, in inches right of its center.</summary>
        public float LineAtHoleInches;
    }

    /// <summary>Everything about one putt. Mirrors analyzePutt in the JavaScript SDK.</summary>
    public sealed class PuttResult
    {
        public bool Valid;
        /// <summary>When not valid: "no-data" or "not-a-ball".</summary>
        public string Reason;

        public bool Made;

        public bool HasEntry;
        public BallPoint EntryPoint;
        /// <summary>Degrees around the hole from the front. Negative is the Player's left.</summary>
        public int EntryAngle;
        /// <summary>CENTER CUT, LEFT CENTER, RIGHT SIDE, BACK DOOR and so on.</summary>
        public string EntryDescription;
        /// <summary>The entry angle of a straight putt from the tee you passed in.</summary>
        public float ExpectedEntryAngle;
        /// <summary>EntryAngle minus ExpectedEntryAngle.</summary>
        public float OffCenter;

        public MissSide MissSide;
        /// <summary>How far outside the lip the ball passed. 0 for a made putt or a lip-out.</summary>
        public float MissByInches;

        public bool HasSpeed;
        public float SpeedInPerSec;
        public float SpeedFtPerSec => SpeedInPerSec / 12f;
        /// <summary>How much farther up the ramp the ball would have rolled: v * v / 128.</summary>
        public float RampRolloutInches => SpeedInPerSec * SpeedInPerSec / (2f * PuttrAnalysis.RampDecelInPerSec2);

        /// <summary>Distinct camera frames in the path.</summary>
        public int Frames;
        public IReadOnlyList<BallPoint> Path;
        public Hole Hole;
        /// <summary>One point per camera frame, in inches from the hole center (x right, y toward the Player).</summary>
        public Vector2[] PathInches;

        public PuttLaunch Launch;
    }
}
