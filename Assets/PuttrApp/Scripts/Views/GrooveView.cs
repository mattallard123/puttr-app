using System;
using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class GrooveGame
    {
        // GrooveScene: the pace window for the next putt (target - 2 to target + window, inches past), the round,
        // each Player's streak and misses this round, who is out.
        public override void Describe(Dictionary<string, object> d)
        {
            int n = Math.Max(1, Players);
            d["round"] = Round;
            d["window"] = (float)Windows[Round];
            d["target"] = (float)_target;
            d["streaks"] = _streak.Take(n).ToArray();
            d["misses"] = _misses.Take(n).ToArray();
            d["out"] = _out.Take(n).ToArray();
            d["streak"] = _streak[P];
        }
    }
}
