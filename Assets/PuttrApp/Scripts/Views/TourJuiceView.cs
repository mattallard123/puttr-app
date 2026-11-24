using System;
using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class TourJuiceGame
    {
        // TourJuiceScene (the juice bar): the putt just scored as line and pace (0 to 1 each, as the rules weigh them),
        // the juice (their average, the share of its distance the putt scored), the putts' distances in order, the
        // perfect total, and every Player's juice so far, putt by putt (a miss is 0).
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
            d["list"] = List.Select(x => x.Feet).ToArray();
            d["perfect"] = List.Sum(x => x.Feet);
            var history = new float[Math.Max(1, Players)][];
            for (int p = 0; p < history.Length; p++)
                history[p] = Shots.Where(x => x.Player == p).Select(x => x.Feet > 0 ? (float)Math.Max(0, Math.Min(1, x.Points / x.Feet)) : 0f).ToArray();
            d["history"] = history;
        }
    }
}
