// The world, in numbers.
//
// One Unity unit is one inch, and everything is life size: the virtual lane is
// a continuation of the real mat. The origin is the center of the hole, on the
// surface. +X is the Player's right, +Z is away from the Player, +Y is up.
//
// A point from the console lands in this space with
// PuttrAnalysis.ToInchesIntegrated:
//   world.x =  inches.x
//   world.z = -inches.y        (camera y grows toward the Player)
using UnityEngine;

namespace PuttrArcade
{
    public static class LaneSpec
    {
        public const float Gravity = 386.09f;            // in/s^2

        // ── The real thing: mat, ramp, hole ─────────────────────────────────
        //
        // The carpet lies flat, curves up into a 12 degree ramp with no crease,
        // and keeps climbing through the hole to the back of the console,
        // 5 inches past the hole's center.
        public const float RampAngleDeg = 12f;
        public const float MatStartZ = -90f;
        public const float CurveStartZ = -21f;            // the carpet starts to lift
        public const float InclineStartZ = -15f;          // dead straight from here up
        public const float RampEndZ = 5f;                 // the far end of the mat
        public const float MatHalfWidth = 12f;            // the mat is 24 in wide
        public const float HoleRadius = 2.125f;
        public const float BallRadius = 0.84f;            // a golf ball is 1.68 in across

        // Gravity alone slows a rolling ball on a 12 degree slope by
        // (5/7) g sin(12) = 57 in/s^2. The docs measure 64, and the difference
        // is rolling resistance, which we apply everywhere.
        public const float RollingResistance = 6.6f;      // in/s^2

        static readonly float Slope = Mathf.Tan(RampAngleDeg * Mathf.Deg2Rad);
        // The curve is a parabola that meets the ramp at the ramp's own slope.
        static readonly float CurveA = Slope / (2f * (InclineStartZ - CurveStartZ));
        /// <summary>Height of the flat part of the mat (the hole is at 0).</summary>
        public static readonly float MatY = InclineStartZ * Slope - CurveA * (InclineStartZ - CurveStartZ) * (InclineStartZ - CurveStartZ);
        public static readonly float RampEndY = RampEndZ * Slope;

        // ── The lane, which is not real ─────────────────────────────────────
        //
        // It starts under the back edge of the mat. A holed ball drops through
        // the cup and rolls out onto it; a ball that misses but has the pace
        // runs off the end of the ramp and lands on it. Then it runs downhill
        // at the same 12 degrees, giving back the speed the ramp took, and
        // levels out for the pins.
        public const float LaneHeadZ = 5f;
        public const float SlopeEndZ = 13f;
        public const float LaneHalfWidth = 6f;
        public const float GutterWidth = 3f;
        public const float GutterDepth = 0.8f;
        public const float HeadPinZ = 19f;
        public const float LaneEndZ = 29f;
        public const float PitEndZ = 37f;
        public const float PitDepth = 4f;

        /// <summary>The head of the lane: just far enough under the end of the mat for a ball to roll out.</summary>
        public static readonly float LaneHeadY = RampEndY - 2.2f;
        /// <summary>Height of the pin deck.</summary>
        public static readonly float DeckY = LaneHeadY - (SlopeEndZ - LaneHeadZ) * Slope;

        // The pins: regulation shape at about a quarter size, close to what a
        // golf ball is to a bowling ball. They are lighter and livelier than
        // scale would make them, because a putt arrives far slower for its
        // size than a bowling ball does. Puttr > Print Pin Table shows what a
        // change here does. (Fields, not constants, so tools can try values.)
        public static float PinScale = 0.24f;             // of a regulation pin
        public static float PinSpacing = 2.6f;            // between neighbouring pins
        public static float PinMass = 0.12f;              // ball = 1
        public static float PinFriction = 0.24f;
        public static float PinBounce = 0.7f;
        public static float PinHeight => 15f * PinScale;

        // A ball that hits the head pin square leaves a split; the strike is
        // in the pocket beside it. The rack sits a little left so that pocket
        // is in line with the middle of the hole: a center cut putt strikes.
        public static float RackOffsetX = 0f;

        /// <summary>A holed putt always comes out of the cup at no less than this, in/s.</summary>
        public const float HoledMinSpeed = 14f;

        public static float RowSpacing => PinSpacing * 0.8660254f;

        /// <summary>Height of the rolling surface at z.</summary>
        public static float SurfaceY(float z)
        {
            if (z < CurveStartZ) return MatY;
            if (z < InclineStartZ) return MatY + CurveA * (z - CurveStartZ) * (z - CurveStartZ);
            if (z < RampEndZ) return z * Slope;
            if (z < SlopeEndZ) return LaneHeadY - (z - LaneHeadZ) * Slope;
            return DeckY;
        }

        /// <summary>Rise over run of the surface at z.</summary>
        public static float SurfaceSlope(float z)
        {
            if (z < CurveStartZ) return 0f;
            if (z < InclineStartZ) return 2f * CurveA * (z - CurveStartZ);
            if (z < RampEndZ) return Slope;
            if (z < SlopeEndZ) return -Slope;
            return 0f;
        }

        public static Vector3 SurfaceNormal(float z) => new Vector3(0f, 1f, -SurfaceSlope(z)).normalized;

        /// <summary>Console inches (x right, y toward the Player) to a point on the surface.</summary>
        public static Vector3 OnSurface(Vector2 inches, float lift = 0f)
        {
            float z = -inches.y;
            return new Vector3(inches.x, SurfaceY(z), z) + SurfaceNormal(z) * lift;
        }

        /// <summary>Unit vector along the surface for a heading (degrees, positive right) at z.</summary>
        public static Vector3 SurfaceDirection(float headingDeg, float z)
        {
            float h = headingDeg * Mathf.Deg2Rad;
            var flat = new Vector3(Mathf.Sin(h), 0, Mathf.Cos(h));
            Vector3 n = SurfaceNormal(z);
            return (flat - n * Vector3.Dot(flat, n)).normalized;
        }

        /// <summary>Spot for pin 1 to 10: 1 at the front, 7 back left, 10 back right.</summary>
        public static Vector3 PinSpot(int number)
        {
            int row = number <= 1 ? 0 : number <= 3 ? 1 : number <= 6 ? 2 : 3;
            int firstInRow = row * (row + 1) / 2 + 1;
            int k = number - firstInRow;
            return new Vector3(RackOffsetX + (k - row * 0.5f) * PinSpacing, DeckY, HeadPinZ + row * RowSpacing);
        }
    }
}
