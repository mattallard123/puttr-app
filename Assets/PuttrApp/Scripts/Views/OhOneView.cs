using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class OhOneGame
    {
        // OhOneScene: the start number, what each Player has left, the turn, whether the putter is on a checkout or a
        // double, and the signed entry of the putt just scored (to light its wedge).
        public override void Describe(Dictionary<string, object> d)
        {
            d["start"] = _start;
            d["left"] = _left.Take(Players).ToArray();
            d["putts"] = _putts.Take(Players).ToArray();
            d["inTurn"] = _inTurn;
            d["checkout"] = _left[P] <= Checkout;
            d["double"] = _left[P] <= Checkout && IsDouble(_left[P]);
            d["rel"] = Last != null && Last.V != null ? (float)Last.V.OffLine : 0f;
        }
    }
}
