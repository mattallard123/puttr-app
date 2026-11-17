using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class FullWorkoutGame
    {
        // FullWorkoutScene: the 27 putts in order (distance, tee) and the points each earned (-1 not played yet).
        public override void Describe(Dictionary<string, object> d)
        {
            d["feet"] = List.Select(x => x.Feet).ToArray();
            d["tees"] = List.Select(x => x.Tee).ToArray();
            var pts = new int[List.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = -1;
            List<Shot> mine = Shots.Where(s => s.Player == 0).ToList();
            for (int i = 0; i < mine.Count && i < pts.Length; i++) pts[i] = (int)System.Math.Round(mine[i].Points);
            d["pts"] = pts;
            d["index"] = Index;
        }
    }
}
