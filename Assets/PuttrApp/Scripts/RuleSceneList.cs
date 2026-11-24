// Which rule game plays in which 3D scene (PuttrArcade/Scripts/Scenes/<Name>Scene.cs). Looked up by name, so a
// game whose scene isn't built yet just plays on the plain mat.
using System;
using System.Collections.Generic;
using PuttrArcade;

namespace PuttrApp
{
    public static class RuleSceneList
    {
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "HORSE", "HorseScene" }, { "HIGHSTRIKER", "HighStrikerScene" },
            { "SPEEDDEMON", "SpeedDemonScene" }, { "FULLWORKOUT", "FullWorkoutScene" }, { "METAL", "MetalScene" }, { "FITTING", "CompareScene" },
            { "DEADCENTER", "DeadCenterScene" }, { "ASSESSMENT", "AssessmentScene" },
            { "OHONE", "OhOneScene" }, { "REDUCER", "ReducerScene" }, { "MATCH", "MatchPlayScene" }, { "LEAPFROG", "LeapfrogScene" },
            { "TOURJUICE", "TourJuiceScene" }, { "LAB", "LabScene" },
            { "CENTERCUT", "CenterCutScene" },
            { "GREENLANDS", "MeadowScene" },   // Greenlands world 1, every stage (Greenlands.cs)
            { "DOORS", "DoorsScene" }, { "HIGHSIDE", "HighSideScene" }, { "SWITCHBACK", "SwitchbackScene" }, { "GEARS", "GearsScene" },
            { "GROOVE", "GrooveScene" }, { "CLOCK", "ClockScene" }, { "AROUNDTHECLOCK", "ClockScene" },
            { "PENDULUM", "PendulumScene" }, { "WALTZ", "WaltzScene" }, { "PURE", "PureScene" }, { "CARBONCOPY", "CarbonCopyScene" }, { "SQUAREUP", "SquareUpScene" }, { "STRETCH", "StretchScene" }, { "LOCKDOWN", "LockdownScene" }, { "THEYIPS", "TheYipsScene" },
        };

        public static void Register(Dictionary<string, Func<RuleScene>> s)
        {
            foreach (KeyValuePair<string, string> n in Names)
            {
                Type t = typeof(RuleScene).Assembly.GetType("PuttrArcade." + n.Value);
                if (t != null) s[n.Key] = () => (RuleScene)Activator.CreateInstance(t);
            }
        }
    }
}
