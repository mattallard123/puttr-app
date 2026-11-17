using System;
using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class DoorsGame
    {
        // DoorsScene: the doors (their bounds in degrees, so count = bounds + 1), the door called for the next putt.
        public override void Describe(Dictionary<string, object> d)
        {
            d["level"] = _level;
            d["bounds"] = Bounds[_level].Select(b => (float)b).ToArray();
            d["labels"] = Labels[_level];
            d["called"] = _calls.Count > 0 ? _calls[Math.Min(Index, _calls.Count - 1)] : 0;
        }
    }
}
