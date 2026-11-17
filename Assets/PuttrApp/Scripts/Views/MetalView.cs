using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class MetalGame
    {
        // MetalScene: each Player's ingots (the feet of every make that counted, in order) and back door makes.
        public override void Describe(Dictionary<string, object> d)
        {
            for (int p = 0; p < Players; p++)
            {
                int who = p;
                d["made" + p] = Shots.Where(s => s.Player == who && s.Good).Select(s => s.Feet).ToArray();
                d["doors" + p] = Shots.Count(s => s.Player == who && s.Title == "BACK DOOR");
            }
            d["count"] = List.Count;
        }
    }
}
