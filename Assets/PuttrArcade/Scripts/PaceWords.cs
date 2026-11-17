// Pace, said the way a golfer says it: how far the putt would have finished
// from the hole. "2 in short", "Dies in", "8 in past", "2.5 ft past".
//
// Nobody thinks in feet per second. The number here is the Puttr app's own
// measure of pace, rollout: where the ball would have stopped had the hole not
// been there, in inches from the hole's center. A putt still moving at the
// hole is carried on at the app's reference deceleration (64 in/s², the ramp),
// which is the scale Players of the app already know. A putt that never got
// there is measured where it stopped.
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public static class PaceWords
    {
        const float Decel = PuttrAnalysis.RampDecelInPerSec2;

        /// <summary>How far past the hole a putt arriving at this speed (inches a second) would finish.</summary>
        public static float Past(float inchesPerSecond) => inchesPerSecond <= 0f ? 0f : inchesPerSecond * inchesPerSecond / (2f * Decel);

        /// <summary>A length: "8 in", "2.5 ft".</summary>
        public static string Length(float inches)
        {
            inches = Mathf.Abs(inches);
            if (inches >= 24f) return (inches / 12f).ToString("0.#") + " ft";
            return Mathf.Max(1, Mathf.RoundToInt(inches)) + " in";
        }

        /// <summary>A pace to aim for, from a speed: "Dies in", "8 in past".</summary>
        public static string Finish(float inchesPerSecond)
        {
            float past = Past(inchesPerSecond);
            return past < 1.5f ? "Dies in" : Length(past) + " past";
        }

        /// <summary>The pace of a putt that was hit.</summary>
        public static string Of(PuttResult putt)
        {
            if (putt == null) return "—";
            PuttLaunch launch = putt.Launch;
            if (!launch.Reached)
            {
                // Left short: how far from the hole's center it got.
                float nearest = float.MaxValue;
                if (putt.PathInches != null)
                    foreach (Vector2 p in putt.PathInches) nearest = Mathf.Min(nearest, p.magnitude);
                return nearest < 60f ? Length(nearest) + " short" : "Short";
            }
            float speed = launch.HandoffSpeedInPerSec > 0f ? launch.HandoffSpeedInPerSec : launch.SpeedInPerSec;
            return Finish(speed);
        }
    }
}
