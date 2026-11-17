using System;
using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class TourJuiceGame
    {
        // TourJuiceScene: the putt just scored as line and pace (0 to 1 each, as the rules weigh them), the juice
        // (their average), and the putter's juice so far.
        public override void Describe(Dictionary<string, object> d)
        {
            float line = 0f, pace = 0f;
            Shot s = Last;
            if (s != null && s.V != null && s.V.Made && !Scoring.BackDoor(s.V, s.Feet, s.Tee))
            {
                line = (float)Math.Max(0, 1 - Scoring.OffLine(s.V, s.Feet, s.Tee) / 90);
                pace = s.V.Rollout == null ? 0.5f : (float)(Scoring.Speed(s.V.Rollout, 0, true) / 50);
            }
            d["line"] = line;
            d["pace"] = pace;
            d["juice"] = (line + pace) * 0.5f;
            d["index"] = Index;
            d["count"] = List.Count;
        }
    }
}
