using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class AssessmentGame
    {
        // AssessmentScene: the twelve stations (distance, tee) in set order, the grade at each so far, the station
        // that's up and the one just played.
        public override void Describe(Dictionary<string, object> d)
        {
            var feet = new int[12]; var tees = new int[12]; var grades = new string[12];
            for (int i = 0; i < 12; i++) { feet[i] = Set[i, 0]; tees[i] = Set[i, 1]; grades[i] = ""; }
            int done = Shots.Count;
            if (_order != null)
                for (int k = 0; k < done && k < 12; k++) grades[_order[k]] = Shots[k].Title ?? "";
            d["stationFeet"] = feet;
            d["stationTee"] = tees;
            d["grades"] = grades;
            d["now"] = _order != null && done < 12 ? _order[done] : -1;
            d["last"] = _order != null && done > 0 ? _order[System.Math.Min(done, 12) - 1] : -1;
        }
    }
}
