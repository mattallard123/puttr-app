using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class SpeedDemonGame
    {
        // SpeedDemonScene: where the list is (the scene shows the putt itself from the ShotView).
        public override void Describe(Dictionary<string, object> d)
        {
            d["index"] = Index;
            d["count"] = List.Count;
        }
    }
}
