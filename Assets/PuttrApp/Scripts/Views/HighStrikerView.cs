using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class HighStrikerGame
    {
        // HighStrikerScene: the putter's scores so far this game.
        public override void Describe(Dictionary<string, object> d)
        {
            int who = Over && Shots.Count > 0 ? Shots[Shots.Count - 1].Player : P;
            d["mine"] = Shots.Where(s => s.Player == who).Select(s => (float)s.Points).ToArray();
            d["putts"] = List.Count;
        }
    }
}
