using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class LeapfrogGame
    {
        // LeapfrogScene: the speed target (inches past), the list of distances, where it is, and the putter's points.
        public override void Describe(Dictionary<string, object> d)
        {
            d["target"] = (float)_target;
            d["feet"] = List.Select(x => x.Feet).ToArray();
            d["index"] = Index;
            int who = Over && Shots.Count > 0 ? Shots[Shots.Count - 1].Player : P;
            d["mine"] = Shots.Where(s => s.Player == who).Select(s => (float)s.Points).ToArray();
        }
    }
}
