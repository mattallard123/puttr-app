using System;
using System.Collections.Generic;

namespace PuttrApp
{
    public sealed partial class AroundTheClockGame
    {
        // ClockScene: the parts of the cup (sectors), each Player's collected parts ("got0".."got3"), the next putter's
        // ("got") and the part they need next ("next", -1 when done).
        public override void Describe(Dictionary<string, object> d)
        {
            d["sectors"] = _sectors;
            if (_got == null) { d["got"] = new bool[_sectors]; d["next"] = 0; return; }
            for (int p = 0; p < Math.Min(Players, _got.Length); p++) d["got" + p] = (bool[])_got[p].Clone();
            d["got"] = (bool[])_got[P].Clone();
            d["next"] = NextSector;
            d["won"] = _won;
        }
    }
}
