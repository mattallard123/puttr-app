using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class MatchPlayGame
    {
        // MatchPlayScene: each hole's result (1 first Player won, -1 second, 0 halved), the hole being played, sudden
        // death, the match count, and whether the first Player made it on this hole.
        public override void Describe(Dictionary<string, object> d)
        {
            d["results"] = _results.ToArray();
            d["hole"] = _hole;
            d["sudden"] = _sudden;
            d["mc"] = _mc;
            d["firstMade"] = _first == true;
        }
    }
}
