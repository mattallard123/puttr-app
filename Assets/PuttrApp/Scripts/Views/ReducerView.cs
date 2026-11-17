using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class ReducerGame
    {
        // ReducerScene: the chosen entry window (degrees, full width), where the list is, and the signed entry of the
        // putt just scored against its tee's line.
        public override void Describe(Dictionary<string, object> d)
        {
            d["window"] = _window;
            d["index"] = Index;
            d["count"] = List.Count;
            d["rel"] = Last != null && Last.V != null ? (float)Last.V.OffLine : 0f;
        }
    }
}
