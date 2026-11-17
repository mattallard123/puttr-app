using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class SwitchbackGame
    {
        // SwitchbackScene: the tee of every putt in the list (the road ahead), and the one being played.
        public override void Describe(Dictionary<string, object> d)
        {
            d["tees"] = List.Select(x => x.Tee).ToArray();
            d["index"] = Index;
        }
    }
}
