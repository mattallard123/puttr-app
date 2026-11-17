using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class HighSideGame
    {
        // HighSideScene: the tally (the tee for the lean is NextTee).
        public override void Describe(Dictionary<string, object> d)
        {
            d["makes"] = _makes;
            d["high"] = _high;
            d["low"] = _low;
        }
    }
}
