using System;
using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class GearsGame
    {
        // GearsScene: each Player's gear, the next putter's gear and its pace band (inches past the cup), the top gear.
        public override void Describe(Dictionary<string, object> d)
        {
            d["gears"] = _gear.Take(Math.Max(1, Players)).ToArray();
            int g = Math.Min(_gear[P], Top - 1);
            var (lo, hi) = Band(g);
            d["gear"] = _gear[P];
            d["lo"] = (float)lo;
            d["hi"] = (float)hi;
            d["top"] = Top;
            d["won"] = _won;
        }
    }
}
