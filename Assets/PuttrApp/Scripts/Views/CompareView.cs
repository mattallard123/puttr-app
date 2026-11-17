using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class CompareGame
    {
        // CompareScene: putts played by each putter, and how many each plays.
        public override void Describe(Dictionary<string, object> d)
        {
            d["done"] = Enumerable.Range(0, Players).Select(Done).ToArray();
            d["count"] = _list.Count;
        }
    }
}
