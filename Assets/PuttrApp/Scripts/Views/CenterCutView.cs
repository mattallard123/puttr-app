using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class CenterCutGame
    {
        // CenterCutScene: the tee's line for the putt just played and the next one (the scoring window is the line
        // ±90), and the entry read in words.
        public override void Describe(Dictionary<string, object> d)
        {
            Shot last = Last;
            d["nextLine"] = (float)AppPutt.TeeEntry(Feet, Tee);
            d["window"] = (float)Window;
            d["center"] = (float)CenterDeg;
            if (last == null) return;
            double line = LastLine;
            d["line"] = (float)line;
            if (last.V != null && last.V.Made && last.V.HasEntry) d["side"] = Side(last.V.Entry - line);
        }
    }
}
