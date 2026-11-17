using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class DeadCenterGame
    {
        // DeadCenterScene: found it yet, and whether the spot moves every putt.
        public override void Describe(Dictionary<string, object> d)
        {
            d["won"] = _won;
            d["random"] = _random;
            d["putts"] = Shots.Count;
        }
    }
}
