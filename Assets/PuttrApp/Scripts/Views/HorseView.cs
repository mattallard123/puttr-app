using System.Collections.Generic;
using System.Linq;

namespace PuttrApp
{
    public sealed partial class HorseGame
    {
        // HorseScene: letters per Player, who calls, whether the shot is being called, swish, who is out.
        public override void Describe(Dictionary<string, object> d)
        {
            d["letters"] = _letters.Take(Players).ToArray();
            d["caller"] = _caller;
            d["calling"] = _calling;
            d["swish"] = _swish;
            d["pick"] = _calling && P != Bot;   // the caller may tap a spot (not Puttr)
        }

        // Taps on the court's mat call the spot.
        void Listen() => PuttrArcade.HorseScene.SpotPicked = CallSpot;
    }
}
