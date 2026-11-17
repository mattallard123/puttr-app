using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class LabGame
    {
        // LabScene: the spot, the speed target, each putt's pace and entry out of ten, and the last putt's full read
        // (entry against the tee's line, how it was described, the launch heading) for the scene's data panel.
        public override void Describe(Dictionary<string, object> d)
        {
            d["feet"] = _feet;
            d["tee"] = _tee;
            d["teeName"] = PracticeSession.TeeName(_tee);
            d["target"] = (float)_target;
            d["pace"] = _pace.Select(x => (float)x).ToArray();
            d["entry"] = _entry.Select(x => (float)x).ToArray();
            d["putts"] = Putts;
            d["count"] = Shots.Count;
            d["average"] = Score != null && Score.Length > 0 ? (float)Score[0] : 0f;
            // The best putt so far (out of 20): the average is in the score bar already.
            d["best"] = Shots.Where(x => x.Player == P || Over).Select(x => (float)x.Points).DefaultIfEmpty(0f).Max();
            Shot last = Last;
            if (last?.V == null) return;
            AppPutt.Verdict v = last.V;
            d["total"] = (float)last.Points;
            d["offLine"] = (float)v.OffLine;
            d["entryDesc"] = v.Description ?? "";
            if (v.StartLine.HasValue) d["launch"] = (float)v.StartLine.Value;
        }
    }
}
