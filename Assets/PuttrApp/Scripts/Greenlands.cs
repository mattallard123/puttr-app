// Greenlands: Quests rebuilt as a world map of stages you have to beat (docs/quests-reimagined, decided 2026-11-22).
//
// Eight worlds. Each is six stages, a boss and a secret bonus green. World 1 is open to everyone; worlds 2 to 8 are
// Puttr Club only, and each opens when the boss of the world before it is beaten.
//
//   GreenWorld     a world: its name, what it tests, its boss's reward, its stages
//   GreenStage     one stage: its kind, its goal, its bag of balls, the putts it deals, the test a make must pass
//   StageGame      the rules (a RuleGame, so it plays on GameBridge.PlayRule with its own 3D scene, MeadowScene)
//   GreenProgress  the stars a Player holds, the markers, what is open
//
// The rules use only what the camera reads in the last 2 ft of a putt: the make, the entry, the pace (rollout), and
// when the ball got there. Distance and tee are dealt and trusted; no rule depends on where a ball left from.
//
// Balls are lives: a miss costs one, a make costs nothing. Clear the goal before the bag is empty. Stars: no misses
// three, one miss two, any clear one. An empty bag ends the stage; trying again is free.
//
// What a make may have to pass (GreenStage):
//   Windows    an entry window in degrees, centred on the tee's line (a gate; a boss narrows it with every hit)
//   PaceMin    at least this much rollout, in inches: a firm putt (sand)
//   PaceMax    at most this much rollout: a dying putt (ice)
//   Lamp       the mill's lamp must be green when the ball gets there (the blades are clear)
//   Run        makes must come in a row: a miss starts the run again (a gauntlet)
//
// Saving. Putts are saved as a game's putts (games row GREENLANDS, id 1621, published 0). Stars go to the quest
// routes against one hidden quest a world (quests rows 2001 to 2008, published 0, so neither the legacy app nor
// GET /quests/ lists them):
//   GET  /quests/levels/200N     the world's stage rows and this Player's stars
//   POST /quests/level/finish    the stars, best kept by the API
// World 1's rows came from puttr-api/migrations/add_greenlands_world1.sql; worlds 2 to 8 from
// add_greenlands_worlds_2_8.sql. Where a world's rows are missing its stars are kept on the device only
// (PlayerPrefs, per Player). Greenlands starts clean: stars from the old quests do not carry in.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public enum StageKind { Stage, Gauntlet, Gate, Boss, Bonus }

    public sealed class GreenStage
    {
        public int World = 1;
        public string Code, Name, Rule;
        public StageKind Kind;
        /// <summary>The stage's row in quest_levels (levelNum under the world's hidden quest): 1 to 6, 7 the boss, 8 the bonus green.</summary>
        public int LevelNum;
        public int Goal, Bag;
        /// <summary>The putts dealt, in order, one for each make still needed (the list wraps).</summary>
        public int[] Feet, Tees = { 2 };
        /// <summary>Entry windows in degrees, one a make (the last repeats). Null for none.</summary>
        public double[] Windows;
        public double PaceMin, PaceMax;
        public bool Lamp;
        public float LampOpen = 4f, LampShut = 2.5f;
        /// <summary>Makes must come in a row. Always so for a gauntlet; a boss may ask it too.</summary>
        public bool Run;
        /// <summary>Where the node sits on the map, in the map's own 1000 x 520 space.</summary>
        public Vector2 At;

        public bool InARow => Run || Kind == StageKind.Gauntlet;
        public double Window(int makes) => Windows == null || Windows.Length == 0 ? 0 : Windows[Mathf.Clamp(makes, 0, Windows.Length - 1)];
        public string KindWord => Kind == StageKind.Gauntlet ? "Gauntlet" : Kind == StageKind.Gate ? "Gate" : Kind == StageKind.Boss ? "Boss" : Kind == StageKind.Bonus ? "Bonus green" : "Stage";
        public int NearFeet => Feet.Min();
        public int FarFeet => Feet.Max();
        public string FeetWords => NearFeet == FarFeet ? NearFeet + " ft" : NearFeet + " to " + FarFeet + " ft";
        public string BagWords => Bag == 1 ? "1 ball" : Bag + " balls";
        /// <summary>The test in a few words, for a chip on the stage's card. Null for a plain make.</summary>
        public string TestWords
        {
            get
            {
                var parts = new List<string>();
                if (Windows != null) parts.Add("Inside " + (Windows.Min() / 2).ToString("0") + "° of the line");
                if (PaceMin > 0) parts.Add(PaceMin.ToString("0") + " in. past or more");
                if (PaceMax > 0) parts.Add(PaceMax.ToString("0") + " in. past or less");
                if (Lamp) parts.Add("On the green lamp");
                return parts.Count == 0 ? null : string.Join("   ·   ", parts);
            }
        }
    }

    public sealed class GreenWorld
    {
        public int Num;
        public string Name, Tests;
        /// <summary>What beating the boss hands over, and what it does.</summary>
        public string Reward, RewardNote;
        public List<GreenStage> Stages = new List<GreenStage>();

        public string QuestId => (2000 + Num).ToString();
        /// <summary>Worlds 2 to 8 are for Puttr Club members.</summary>
        public bool Club => Num > 1;
        public GreenStage Boss => Stages.First(s => s.Kind == StageKind.Boss);
        public GreenStage Secret => Stages.First(s => s.Kind == StageKind.Bonus);
        /// <summary>The stage whose three stars open the secret route: the fourth.</summary>
        public GreenStage SecretKey => Stages[3];
        /// <summary>The stages on the main path, in order (the boss last).</summary>
        public List<GreenStage> Path => Stages.Where(s => s.Kind != StageKind.Bonus).ToList();
    }

    public static class Greenlands
    {
        public const string GameType = "GREENLANDS";
        public const string GameId = "1621";
        public const int WorldCount = 8;
        /// <summary>A make this close to the tee's line (degrees) is a center cut: it drops a marker.</summary>
        public const double CenterCut = 5;
        /// <summary>The lamp counts as green for this long after it turns red: the ball was on its way.</summary>
        public const double LampGrace = 1.5;

        // Where the eight nodes sit: three trails, so neighbouring worlds do not look alike. Every trail leaves the
        // top left of the map empty (left of 370, above 350): a world's page puts its legend there (Matt, 2026-11-22),
        // so each one starts low on the left, runs under that corner and climbs away to the right.
        public const float LegendRight = 370f, LegendBottom = 350f;
        static readonly Vector2[][] Trails =
        {
            new[] { new Vector2(120, 440), new Vector2(275, 405), new Vector2(425, 320), new Vector2(545, 185), new Vector2(680, 290), new Vector2(800, 165), new Vector2(905, 290), new Vector2(420, 95) },
            new[] { new Vector2(110, 430), new Vector2(255, 440), new Vector2(400, 360), new Vector2(500, 230), new Vector2(620, 115), new Vector2(745, 250), new Vector2(880, 385), new Vector2(640, 410) },
            new[] { new Vector2(130, 420), new Vector2(290, 440), new Vector2(440, 395), new Vector2(570, 295), new Vector2(700, 400), new Vector2(805, 250), new Vector2(900, 115), new Vector2(570, 130) },
        };

        static List<GreenWorld> _worlds;
        public static List<GreenWorld> Worlds => _worlds ?? (_worlds = Build());

        public static GreenWorld World(int num) => Worlds[Mathf.Clamp(num, 1, WorldCount) - 1];
        public static GreenWorld WorldOf(GreenStage s) => World(s.World);
        public static IEnumerable<GreenStage> All => Worlds.SelectMany(w => w.Stages);
        public static GreenStage ByCode(string code) => All.FirstOrDefault(s => s.Code == code);

        static readonly int[] LCR = { 1, 2, 3 }, LR = { 1, 3 };

        static List<GreenWorld> Build()
        {
            var worlds = new List<GreenWorld>
            {
                new GreenWorld { Num = 1, Name = "Meadow Links", Tests = "Short makes", Reward = "Metronome", RewardNote = "Pace now shows on every Greenlands putt", Stages =
                {
                    new GreenStage { Name = "First Green", Kind = StageKind.Stage, Goal = 3, Bag = 3, Feet = new[] { 3 }, Rule = "Make 3 putts from 3 ft." },
                    new GreenStage { Name = "Three Corners", Kind = StageKind.Stage, Goal = 3, Bag = 3, Feet = new[] { 3, 3, 3 }, Tees = LCR, Rule = "Make one from each tee: left, center, right." },
                    new GreenStage { Name = "Stepping Back", Kind = StageKind.Stage, Goal = 4, Bag = 3, Feet = new[] { 3, 4, 4, 5 }, Rule = "Make 4 putts. Each one is a step further back." },
                    new GreenStage { Name = "The Run", Kind = StageKind.Gauntlet, Goal = 3, Bag = 3, Feet = new[] { 4 }, Rule = "Make 3 in a row from 4 ft. A miss starts the run again." },
                    new GreenStage { Name = "Round the Green", Kind = StageKind.Stage, Goal = 5, Bag = 3, Feet = new[] { 4, 4, 5, 5, 5 }, Tees = new[] { 1, 3, 2, 1, 3 }, Rule = "Make 5 putts. The tee changes after every make." },
                    new GreenStage { Name = "Front Door", Kind = StageKind.Gate, Goal = 3, Bag = 4, Feet = new[] { 4 }, Windows = new[] { 60.0 }, Rule = "Make 3 through the front of the cup. A putt that falls in the side does not count." },
                    new GreenStage { Name = "Three-Putt", Kind = StageKind.Boss, Goal = 3, Bag = 3, Feet = new[] { 3, 4, 5 }, Rule = "Hit him three times. He backs away a foot after each one. Miss three and he has you." },
                    new GreenStage { Name = "Top of the Stick", Kind = StageKind.Bonus, Goal = 1, Bag = 1, Feet = new[] { 5 }, Rule = "One putt from 5 ft. The closer to center it drops, the higher you land." },
                } },
                new GreenWorld { Num = 2, Name = "Windmill Row", Tests = "Timing", Reward = "Miller's crest", RewardNote = "The Grinder's sails, on your map", Stages =
                {
                    new GreenStage { Name = "Mill Lane", Kind = StageKind.Stage, Goal = 3, Bag = 3, Feet = new[] { 4 }, Rule = "Make 3 putts from 4 ft." },
                    new GreenStage { Name = "First Turn", Kind = StageKind.Stage, Goal = 3, Bag = 4, Feet = new[] { 4 }, Lamp = true, LampOpen = 5f, LampShut = 2.5f, Rule = "Make 3 while the lamp is green. On red the blades are in the way." },
                    new GreenStage { Name = "Cross Winds", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 4, 5 }, Tees = LR, Lamp = true, Rule = "Make 4 on the green lamp. The tee swaps sides after every make." },
                    new GreenStage { Name = "Mill Race", Kind = StageKind.Gauntlet, Goal = 3, Bag = 4, Feet = new[] { 5 }, Lamp = true, Rule = "Make 3 in a row through the blades. A miss starts the run again." },
                    new GreenStage { Name = "Quick Blades", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 5 }, Tees = new[] { 2, 1, 3, 2 }, Lamp = true, LampOpen = 3f, LampShut = 3f, Rule = "Make 4. The lamp is green for less time here." },
                    new GreenStage { Name = "The Sluice", Kind = StageKind.Gate, Goal = 3, Bag = 4, Feet = new[] { 5, 6 }, Lamp = true, Windows = new[] { 60.0 }, Rule = "Make 3 through the front of the cup, on the green lamp." },
                    new GreenStage { Name = "The Grinder", Kind = StageKind.Boss, Goal = 4, Bag = 4, Feet = new[] { 4, 5, 5, 6 }, Lamp = true, LampOpen = 3f, LampShut = 3f, Rule = "Hit the mill four times. Only a putt on the green lamp gets past its sails." },
                    new GreenStage { Name = "Top of the Stick", Kind = StageKind.Bonus, Goal = 1, Bag = 1, Feet = new[] { 6 }, Rule = "One putt from 6 ft. The closer to center it drops, the higher you land." },
                } },
                new GreenWorld { Num = 3, Name = "Dune Sea", Tests = "Firm pace", Reward = "Bunker crown", RewardNote = "The Bunker King's crown, on your map", Stages =
                {
                    new GreenStage { Name = "Soft Sand", Kind = StageKind.Stage, Goal = 3, Bag = 3, Feet = new[] { 5 }, Rule = "Make 3 putts from 5 ft." },
                    new GreenStage { Name = "Firm Footing", Kind = StageKind.Stage, Goal = 3, Bag = 4, Feet = new[] { 5 }, PaceMin = 10, Rule = "Make 3 with pace: the ball must be rolling at least 10 in. past the cup." },
                    new GreenStage { Name = "Ridge Walk", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 5, 6 }, Tees = LCR, PaceMin = 10, Rule = "Make 4 firm ones, 10 in. past or more. The tee moves after every make." },
                    new GreenStage { Name = "Caravan", Kind = StageKind.Gauntlet, Goal = 3, Bag = 4, Feet = new[] { 6 }, Rule = "Make 3 in a row from 6 ft. A miss starts the run again." },
                    new GreenStage { Name = "Deep Sand", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 6 }, PaceMin = 14, Rule = "Make 4 through deep sand: 14 in. past or more." },
                    new GreenStage { Name = "The Pass", Kind = StageKind.Gate, Goal = 3, Bag = 4, Feet = new[] { 6, 7 }, Windows = new[] { 50.0 }, Rule = "Make 3 through the front of the cup, inside 25° of the line." },
                    new GreenStage { Name = "The Bunker King", Kind = StageKind.Boss, Goal = 4, Bag = 4, Feet = new[] { 5, 6, 6, 7 }, PaceMin = 12, Rule = "Hit him four times. A soft putt sinks in his sand: 12 in. past or more." },
                    new GreenStage { Name = "Top of the Stick", Kind = StageKind.Bonus, Goal = 1, Bag = 1, Feet = new[] { 7 }, Rule = "One putt from 7 ft. The closer to center it drops, the higher you land." },
                } },
                new GreenWorld { Num = 4, Name = "Frostpine", Tests = "Dying pace", Reward = "Ice crest", RewardNote = "Slick's scarf, on your map", Stages =
                {
                    new GreenStage { Name = "First Frost", Kind = StageKind.Stage, Goal = 3, Bag = 3, Feet = new[] { 5 }, Rule = "Make 3 putts from 5 ft." },
                    new GreenStage { Name = "Thin Ice", Kind = StageKind.Stage, Goal = 3, Bag = 4, Feet = new[] { 5 }, PaceMax = 20, Rule = "Make 3 softly: no more than 20 in. past the cup, or the ice cracks." },
                    new GreenStage { Name = "Snow Line", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 5, 6 }, Tees = LCR, PaceMax = 20, Rule = "Make 4 soft ones, 20 in. past or less. The tee moves after every make." },
                    new GreenStage { Name = "Black Ice", Kind = StageKind.Gauntlet, Goal = 3, Bag = 4, Feet = new[] { 6 }, PaceMax = 24, Rule = "Make 3 in a row, each 24 in. past or less. A miss starts the run again." },
                    new GreenStage { Name = "Glass Lake", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 6 }, PaceMax = 14, Rule = "Make 4 at dying pace: 14 in. past or less." },
                    new GreenStage { Name = "Icicle Gate", Kind = StageKind.Gate, Goal = 3, Bag = 4, Feet = new[] { 6, 7 }, Windows = new[] { 50.0 }, Rule = "Make 3 through the front of the cup, inside 25° of the line." },
                    new GreenStage { Name = "Slick", Kind = StageKind.Boss, Goal = 4, Bag = 4, Feet = new[] { 5, 6, 6, 7 }, PaceMax = 16, Rule = "Hit him four times. A firm putt slides straight past him: 16 in. past or less." },
                    new GreenStage { Name = "Top of the Stick", Kind = StageKind.Bonus, Goal = 1, Bag = 1, Feet = new[] { 7 }, Rule = "One putt from 7 ft. The closer to center it drops, the higher you land." },
                } },
                new GreenWorld { Num = 5, Name = "The Narrows", Tests = "Entry", Reward = "Entry gauge", RewardNote = "Entry in degrees now shows on every Greenlands putt", Stages =
                {
                    new GreenStage { Name = "Canyon Mouth", Kind = StageKind.Stage, Goal = 3, Bag = 3, Feet = new[] { 6 }, Rule = "Make 3 putts from 6 ft." },
                    new GreenStage { Name = "Wide Gate", Kind = StageKind.Gate, Goal = 3, Bag = 4, Feet = new[] { 6 }, Windows = new[] { 60.0 }, Rule = "Make 3 inside 30° of the line." },
                    new GreenStage { Name = "Three Doors", Kind = StageKind.Gate, Goal = 3, Bag = 4, Feet = new[] { 6 }, Tees = LCR, Windows = new[] { 50.0 }, Rule = "Make one from each tee, inside 25° of its line." },
                    new GreenStage { Name = "Tightrope", Kind = StageKind.Gauntlet, Goal = 3, Bag = 4, Feet = new[] { 7 }, Windows = new[] { 60.0 }, Rule = "Make 3 in a row inside 30° of the line. A miss starts the run again." },
                    new GreenStage { Name = "Narrow Gate", Kind = StageKind.Gate, Goal = 4, Bag = 4, Feet = new[] { 7 }, Windows = new[] { 40.0 }, Rule = "Make 4 inside 20° of the line." },
                    new GreenStage { Name = "The Slot", Kind = StageKind.Gate, Goal = 3, Bag = 5, Feet = new[] { 7, 8 }, Windows = new[] { 30.0 }, Rule = "Make 3 inside 15° of the line." },
                    new GreenStage { Name = "The Lip", Kind = StageKind.Boss, Goal = 4, Bag = 5, Feet = new[] { 7 }, Windows = new[] { 60.0, 50.0, 40.0, 30.0 }, Rule = "Hit it four times. Its stone lips close in after every hit: 30°, then 25°, 20° and 15° of the line." },
                    new GreenStage { Name = "Top of the Stick", Kind = StageKind.Bonus, Goal = 1, Bag = 1, Feet = new[] { 8 }, Rule = "One putt from 8 ft. The closer to center it drops, the higher you land." },
                } },
                new GreenWorld { Num = 6, Name = "Ember Caldera", Tests = "Streaks", Reward = "Ember crest", RewardNote = "Hot Hand's flame, on your map", Stages =
                {
                    new GreenStage { Name = "Warm Rock", Kind = StageKind.Stage, Goal = 4, Bag = 3, Feet = new[] { 6 }, Rule = "Make 4 putts from 6 ft." },
                    new GreenStage { Name = "Spark", Kind = StageKind.Gauntlet, Goal = 3, Bag = 3, Feet = new[] { 6 }, Rule = "Make 3 in a row from 6 ft. A miss starts the run again." },
                    new GreenStage { Name = "Lava Steps", Kind = StageKind.Gauntlet, Goal = 3, Bag = 4, Feet = new[] { 6, 7, 7 }, Tees = LCR, Rule = "Make 3 in a row, one from each tee. A miss sends you back to the first." },
                    new GreenStage { Name = "Fire Walk", Kind = StageKind.Gauntlet, Goal = 4, Bag = 4, Feet = new[] { 7 }, Rule = "Make 4 in a row from 7 ft." },
                    new GreenStage { Name = "Ash Field", Kind = StageKind.Stage, Goal = 5, Bag = 3, Feet = new[] { 7 }, Tees = new[] { 1, 3, 2, 3, 1 }, Rule = "Make 5 from 7 ft. The tee changes after every make." },
                    new GreenStage { Name = "The Vent", Kind = StageKind.Gauntlet, Goal = 3, Bag = 4, Feet = new[] { 7, 8, 8 }, Windows = new[] { 60.0 }, Rule = "Make 3 in a row through the front of the cup." },
                    new GreenStage { Name = "Hot Hand", Kind = StageKind.Boss, Goal = 4, Bag = 4, Feet = new[] { 6, 7, 7, 8 }, Run = true, Rule = "Hit him four times in a row. Miss once and his flames are back to full." },
                    new GreenStage { Name = "Top of the Stick", Kind = StageKind.Bonus, Goal = 1, Bag = 1, Feet = new[] { 8 }, Rule = "One putt from 8 ft. The closer to center it drops, the higher you land." },
                } },
                new GreenWorld { Num = 7, Name = "Cloud Nine", Tests = "Long putts", Reward = "Cloud crest", RewardNote = "The Long Shadow's cloak, on your map", Stages =
                {
                    new GreenStage { Name = "Lift Off", Kind = StageKind.Stage, Goal = 2, Bag = 4, Feet = new[] { 9 }, Rule = "Make 2 putts from 9 ft." },
                    new GreenStage { Name = "Thin Air", Kind = StageKind.Stage, Goal = 3, Bag = 5, Feet = new[] { 9 }, Tees = LCR, Rule = "Make one from each tee, all from 9 ft." },
                    new GreenStage { Name = "Updraft", Kind = StageKind.Stage, Goal = 3, Bag = 5, Feet = new[] { 9, 10, 10 }, Rule = "Make 3 putts. They get longer." },
                    new GreenStage { Name = "Skywalk", Kind = StageKind.Gauntlet, Goal = 2, Bag = 5, Feet = new[] { 10 }, Rule = "Make 2 in a row from 10 ft." },
                    new GreenStage { Name = "Stratus", Kind = StageKind.Stage, Goal = 4, Bag = 5, Feet = new[] { 10 }, Tees = new[] { 2, 1, 3, 2 }, Rule = "Make 4 from 10 ft. The tee changes after every make." },
                    new GreenStage { Name = "Summit Gate", Kind = StageKind.Gate, Goal = 2, Bag = 5, Feet = new[] { 11 }, Windows = new[] { 60.0 }, Rule = "Make 2 from 11 ft through the front of the cup." },
                    new GreenStage { Name = "The Long Shadow", Kind = StageKind.Boss, Goal = 3, Bag = 5, Feet = new[] { 9, 10, 11 }, Rule = "Hit it three times. It stretches a foot longer after each one." },
                    new GreenStage { Name = "Top of the Stick", Kind = StageKind.Bonus, Goal = 1, Bag = 1, Feet = new[] { 11 }, Rule = "One putt from 11 ft. The closer to center it drops, the higher you land." },
                } },
                new GreenWorld { Num = 8, Name = "Yips Keep", Tests = "Everything", Reward = "The Golden Cup", RewardNote = "Greenlands is beaten", Stages =
                {
                    new GreenStage { Name = "Drawbridge", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 5, 7, 9, 6 }, Rule = "Make 4 putts. No two from the same distance." },
                    new GreenStage { Name = "Gatehouse", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 6, 7 }, Lamp = true, LampOpen = 3.5f, LampShut = 3f, Rule = "Make 4 while the lamp is green." },
                    new GreenStage { Name = "Cold Hall", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 7 }, PaceMax = 18, Rule = "Make 4 softly: 18 in. past the cup or less." },
                    new GreenStage { Name = "The Stair", Kind = StageKind.Gauntlet, Goal = 4, Bag = 4, Feet = new[] { 6, 7, 8, 9 }, Rule = "Make 4 in a row, each a foot longer. A miss sends you back to the bottom." },
                    new GreenStage { Name = "Armoury", Kind = StageKind.Stage, Goal = 4, Bag = 4, Feet = new[] { 8 }, PaceMin = 12, Rule = "Make 4 with pace: 12 in. past the cup or more." },
                    new GreenStage { Name = "Throne Door", Kind = StageKind.Gate, Goal = 3, Bag = 5, Feet = new[] { 8, 9 }, Windows = new[] { 30.0 }, Rule = "Make 3 inside 15° of the line." },
                    new GreenStage { Name = "The Yips", Kind = StageKind.Boss, Goal = 5, Bag = 5, Feet = new[] { 6, 7, 8, 9, 10 }, Windows = new[] { 60.0, 60.0, 50.0, 40.0, 30.0 }, Rule = "Hit it five times. Every hit is a foot longer and the way in is narrower." },
                    new GreenStage { Name = "Top of the Stick", Kind = StageKind.Bonus, Goal = 1, Bag = 1, Feet = new[] { 10 }, Rule = "One putt from 10 ft. The closer to center it drops, the higher you land." },
                } },
            };
            foreach (GreenWorld w in worlds)
            {
                Vector2[] trail = Trails[(w.Num - 1) % Trails.Length];
                for (int i = 0; i < w.Stages.Count; i++)
                {
                    GreenStage s = w.Stages[i];
                    s.World = w.Num;
                    s.LevelNum = i + 1;
                    s.Code = w.Num + "-" + (s.Kind == StageKind.Boss ? "B" : s.Kind == StageKind.Bonus ? "S" : (i + 1).ToString());
                    s.At = trail[i];
                }
            }
            return worlds;
        }

        /// <summary>The games row the putts are saved under (RoundSaver.UseGame takes its id).</summary>
        public static object Row => new Dictionary<string, object> { { "id", GameId }, { "gameType", GameType }, { "title", "Greenlands" } };

        /// <summary>Plays a stage. Its result comes back through Finished.</summary>
        public static void Play(App app, GreenStage stage)
        {
            if (stage == null || !GreenProgress.Open(stage)) return;
            if (WorldOf(stage).Club && !Club.Member(app.Profile)) { Club.Require(app, "Greenlands", () => { }); return; }
            if (!ConnectGate.Clear(app)) return;
            app.Games.PlayRule(Row, new StageGame(stage));
        }

        /// <summary>GameBridge, when a stage is left over (won or lost): keep the result, then show it.</summary>
        public static void Finished(App app, StageGame game, bool real)
        {
            List<GreenStage> before = All.Where(GreenProgress.Open).ToList();
            GreenWorld world = WorldOf(game.Stage);
            bool hadReward = GreenProgress.HasReward(world.Num);
            int earned = game.Won ? GreenProgress.Record(game.Stage, game.Stars, game.Markers, real) : 0;
            List<GreenStage> opened = All.Where(s => GreenProgress.Open(s) && !before.Contains(s)).ToList();
            app.Push(new StageResultScreen(app, game, earned, opened, !hadReward && GreenProgress.HasReward(world.Num), real));
        }
    }

    /// <summary>What a Player holds in Greenlands: stars a stage, markers, and from those what is open.</summary>
    public static class GreenProgress
    {
        static readonly Dictionary<string, int> Stars = new Dictionary<string, int>();
        static readonly Dictionary<string, string> LevelIds = new Dictionary<string, string>();
        static int _markers;
        static string _loadedFor;

        static string Who => string.IsNullOrEmpty(Auth.Uid) ? "anon" : Auth.Uid;
        static string Key => "greenlands." + Who;
        static string OldKey => "greenlands.w1." + Who;   // world 1 alone, before the other worlds were built

        /// <summary>For tests, and for a Player signing out: forget what is in memory.</summary>
        public static void Reset() { Stars.Clear(); LevelIds.Clear(); _markers = 0; _loadedFor = null; }

        static void Ready()
        {
            if (_loadedFor == Who) return;
            Stars.Clear(); LevelIds.Clear(); _markers = 0;
            _loadedFor = Who;
            Read(PlayerPrefs.GetString(Key, PlayerPrefs.GetString(OldKey, "")));
        }

        /// <summary>"1-1:3,1-2:2|14": the stars, then the markers.</summary>
        public static void Read(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] halves = text.Split('|');
            foreach (string pair in halves[0].Split(','))
            {
                string[] kv = pair.Split(':');
                if (kv.Length == 2 && Greenlands.ByCode(kv[0]) != null && int.TryParse(kv[1], out int n)) Stars[kv[0]] = Mathf.Clamp(n, 0, 3);
            }
            if (halves.Length > 1) int.TryParse(halves[1], out _markers);
        }

        public static string Written() => string.Join(",", Stars.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value)) + "|" + _markers.ToString(CultureInfo.InvariantCulture);

        public static int For(GreenStage s) { Ready(); return s != null && Stars.TryGetValue(s.Code, out int n) ? n : 0; }
        public static bool Cleared(GreenStage s) => For(s) > 0;
        public static int Total { get { Ready(); return Stars.Values.Sum(); } }
        public static int TotalIn(GreenWorld w) => w.Stages.Sum(For);
        public static int Possible => Greenlands.All.Count() * 3;
        public static int Markers { get { Ready(); return _markers; } }
        /// <summary>A world's reward is held once its boss is beaten.</summary>
        public static bool HasReward(int world) => Cleared(Greenlands.World(world).Boss);
        /// <summary>The Metronome (world 1's boss): pace on the read.</summary>
        public static bool HasMetronome => HasReward(1);
        /// <summary>The Entry gauge (world 5's boss): entry in degrees on the read.</summary>
        public static bool HasGauge => HasReward(5);

        /// <summary>A world opens when the boss of the one before it is beaten. (Puttr Club is asked for at the door, not here.)</summary>
        public static bool WorldOpen(int world) => world <= 1 || Cleared(Greenlands.World(world - 1).Boss);
        /// <summary>The furthest world that is open: where the map starts.</summary>
        public static int Furthest { get { int w = 1; while (w < Greenlands.WorldCount && WorldOpen(w + 1)) w++; return w; } }

        /// <summary>A stage opens with a star on the one before it; the boss with all six stages cleared; the secret
        /// green with three stars on the world's fourth stage. Nothing in a world is open until the world is.</summary>
        public static bool Open(GreenStage s)
        {
            if (s == null || !WorldOpen(s.World)) return false;
            GreenWorld w = Greenlands.WorldOf(s);
            if (s.Kind == StageKind.Bonus) return For(w.SecretKey) >= 3;
            List<GreenStage> path = w.Path;
            int at = path.IndexOf(s);
            if (s.Kind == StageKind.Boss) return path.Where(p => p.Kind != StageKind.Boss).All(Cleared);
            return at <= 0 || Cleared(path[at - 1]);
        }

        /// <summary>Why a stage is shut, for its card.</summary>
        public static string LockedWhy(GreenStage s)
        {
            GreenWorld w = Greenlands.WorldOf(s);
            if (!WorldOpen(s.World)) { GreenWorld prior = Greenlands.World(s.World - 1); return "Beat " + prior.Boss.Name + " in " + prior.Name + " to open this world."; }
            if (s.Kind == StageKind.Bonus) return "Three stars on " + w.SecretKey.Code + " " + w.SecretKey.Name + " opens this route.";
            if (s.Kind == StageKind.Boss) return "Clear all six stages to face the boss.";
            List<GreenStage> path = w.Path;
            GreenStage before = path[Mathf.Max(0, path.IndexOf(s) - 1)];
            return "Clear " + before.Code + " " + before.Name + " to open this stage.";
        }

        /// <summary>The stage to play next in a world: the first open one without a star, else its first stage (a shut
        /// world) or its last (a finished one).</summary>
        public static GreenStage NextIn(GreenWorld w) => w.Path.FirstOrDefault(s => Open(s) && !Cleared(s)) ?? (WorldOpen(w.Num) ? w.Path.Last() : w.Path.First());

        /// <summary>The Player's stars for a world from the API over what the device holds: the better of the two.</summary>
        public static IEnumerator Load(GreenWorld world, Action done)
        {
            Ready();
            yield return Api.Get("/quests/levels/" + world.QuestId, data =>
            {
                foreach (object row in J.List(data))
                {
                    int num = J.Int(row, "levelNum");
                    GreenStage s = world.Stages.FirstOrDefault(x => x.LevelNum == num);
                    if (s == null) continue;
                    LevelIds[s.Code] = J.Str(row, "id");
                    int best = Mathf.Clamp(J.Int(row, "result"), 0, 3);
                    if (best > For(s)) Stars[s.Code] = best;
                }
            }, _ => { });
            done?.Invoke();
        }

        /// <summary>Every world, one after another (the totals, and which worlds are open).</summary>
        public static IEnumerator LoadAll(Action done)
        {
            foreach (GreenWorld w in Greenlands.Worlds) yield return Load(w, null);
            done?.Invoke();
        }

        /// <summary>A stage cleared: its stars if they beat the ones held, and its markers. Returns the markers added.
        /// Test putts (real false) change what this run of the app shows and nothing else.</summary>
        public static int Record(GreenStage s, int stars, int markers, bool real)
        {
            Ready();
            stars = Mathf.Clamp(stars, 0, 3);
            bool better = stars > For(s);
            if (better) Stars[s.Code] = stars;
            _markers += Mathf.Max(0, markers);
            if (!real) return markers;
            PlayerPrefs.SetString(Key, Written());
            PlayerPrefs.Save();
            if (better && LevelIds.TryGetValue(s.Code, out string levelId) && !string.IsNullOrEmpty(levelId))
                App.Run(Api.Post("/quests/level/finish", new Dictionary<string, object> { { "questId", Greenlands.WorldOf(s).QuestId }, { "levelId", levelId }, { "result", stars } }, null,
                    why => Perf.Note("greenlands: stars not saved (" + why + ")")));
            return markers;
        }
    }

    /// <summary>One stage's rules.</summary>
    public sealed class StageGame : RuleGame
    {
        /// <summary>Seconds, for the mill's lamp. Tests set their own.</summary>
        public static Func<double> Clock = () => Time.realtimeSinceStartupAsDouble;

        public readonly GreenStage Stage;
        public int Makes { get; private set; }
        public int Misses { get; private set; }
        public int Balls { get; private set; }
        public int Markers { get; private set; }
        public bool Won { get; private set; }
        /// <summary>Why the last putt did not count: "", "missed", "back", "blocked", "wide", "soft", "firm".</summary>
        public string Why { get; private set; } = "";
        int _bonusStars;
        double _lampStart;

        public StageGame(GreenStage stage) { Stage = stage ?? Greenlands.World(1).Stages[0]; Balls = Stage.Bag; }

        protected override string OwnTitle => Stage.Code + " " + Stage.Name;
        public override string GameType => Greenlands.GameType;
        public override string Words => "STARS";
        public override string[] HowTo => new[] { Stage.Rule, "Balls are lives. A miss costs one, a make costs nothing.", "Three stars for no misses, two for one miss, one for any clear." };
        public override bool ShowsRead => false;   // the board below is the whole panel: goal, bag, the call, the read
        public override bool PostsBoard => false;  // a stage has stars, not a leaderboard
        /// <summary>The world's own ambience once it is imported; the meadow's, then the pond's, until then.</summary>
        public override string Bed
        {
            get
            {
                string own = "gl_w" + Stage.World + "_loop";
                return PuttrArcade.GameAudio.Has(own) ? own : PuttrArcade.GameAudio.Has("gl_meadow_loop") ? "gl_meadow_loop" : "pond_loop";
            }
        }
        public override string SoundFor(Shot s) => null;   // the scene plays the stage's sounds as the ball gets there (MeadowScene)

        public bool Lost => !Won && Balls <= 0;
        public override bool Over => Won || Balls <= 0;
        /// <summary>What a clear is worth as things stand: three less the misses, never under one. A bonus green: by its entry.</summary>
        public int StarsLive => Stage.Kind == StageKind.Bonus ? _bonusStars : Mathf.Max(1, 3 - Misses);
        public int Stars => Won ? StarsLive : 0;
        public override int Saved => Stars;
        public override string ScoreText(int p) => Stars.ToString();
        public override string Progress => Stage.Kind == StageKind.Boss ? "HITS " + Makes + " OF " + Stage.Goal : "MAKES " + Makes + " OF " + Stage.Goal;
        public override string Outcome() => Won ? (Stage.Kind == StageKind.Boss ? Stage.Name + " beaten" : "Stage clear") : "Out of balls";

        /// <summary>The entry window the next make must come in through (0 for none).</summary>
        public double WindowNow => Stage.Window(Makes);

        /// <summary>True while the mill's lamp is green, or within the grace after it turned red.</summary>
        public bool LampGreen(double now)
        {
            if (!Stage.Lamp) return true;
            double period = Stage.LampOpen + Stage.LampShut, phase = (now - _lampStart) % period;
            if (phase < 0) phase += period;
            return phase < Stage.LampOpen + Greenlands.LampGrace;
        }

        protected override void Begin() { Makes = 0; Misses = 0; Markers = 0; Balls = Stage.Bag; Won = false; _bonusStars = 0; Why = ""; _lampStart = Clock(); }

        protected override void Deal()
        {
            Feet = Stage.Feet[Makes % Stage.Feet.Length];
            Tee = Stage.Tees[Makes % Stage.Tees.Length];
        }

        protected override Shot Take(AppPutt.Verdict v)
        {
            bool holed = v.Made && !Scoring.BackDoor(v, Feet, Tee);
            double off = Scoring.OffLine(v, Feet, Tee), window = WindowNow;
            // The pace is only held against a putt when the camera could read it.
            double? pace = v.Rollout;
            Why = !v.Made ? "missed" : !holed ? "back"
                : !LampGreen(Clock()) ? "blocked"
                : window > 0 && off > window / 2 ? "wide"
                : Stage.PaceMin > 0 && pace.HasValue && pace.Value < Stage.PaceMin ? "soft"
                : Stage.PaceMax > 0 && pace.HasValue && pace.Value > Stage.PaceMax ? "firm"
                : "";
            bool boss = Stage.Kind == StageKind.Boss;
            if (Why != "")
            {
                Misses++; Balls--;
                bool reset = Stage.InARow && Makes > 0;
                int had = Makes;
                if (Stage.InARow) Makes = 0;
                string left = Balls <= 0 ? "The bag is empty." : Balls == 1 ? "One ball left." : Balls + " balls left.";
                if (Balls <= 0) return Call(0, false, "OUT OF BALLS", boss ? Stage.Name + " wins this one." : "Made " + had + " of " + Stage.Goal + ".");
                string back = reset ? (boss ? "He is back to full. " : "The run starts again. ") : "";
                switch (Why)
                {
                    case "blocked": return Call(0, false, "BLOCKED", "The lamp was red. " + back + left);
                    case "wide": return Call(0, false, "SIDE DOOR", "In, but " + off.ToString("0", CultureInfo.InvariantCulture) + "° off the line. " + back + left);
                    case "soft": return Call(0, false, "TOO SOFT", "In, but only " + pace.Value.ToString("0", CultureInfo.InvariantCulture) + " in. past. " + back + left);
                    case "firm": return Call(0, false, "TOO FIRM", "In, but " + pace.Value.ToString("0", CultureInfo.InvariantCulture) + " in. past. " + back + left);
                    case "back": return Call(0, false, "BACK DOOR", back + left);
                    default: return Call(0, false, reset ? "RUN BROKEN" : "MISSED", back + left);
                }
            }
            Makes++;
            bool centered = v.HasEntry && off <= Greenlands.CenterCut;
            if (centered) Markers++;
            if (Stage.Kind == StageKind.Bonus) _bonusStars = centered ? 3 : off <= 15 ? 2 : 1;
            if (Makes >= Stage.Goal)
            {
                Won = true;
                Score[0] = Stars;
                return Call(Stars, true, boss ? "BEATEN" : "STAGE CLEAR", Stars == 3 ? "Three stars." : Stars == 2 ? "Two stars." : "One star.");
            }
            int to = Stage.Goal - Makes;
            string sub = (centered ? "A marker. " : "") + (boss ? (to == 1 ? "One more hit." : to + " more hits.") : Stage.InARow ? (to == 1 ? "One more in the run." : to + " more in the run.") : (to == 1 ? "One to go." : to + " to go."));
            return Call(1, true, boss ? "HIT" : centered ? "CENTER CUT" : "IN", sub);
        }

        /// <summary>The scene's picture of the stage (MeadowScene reads these).</summary>
        public override void Describe(Dictionary<string, object> data)
        {
            data["world"] = Stage.World;
            data["kind"] = Stage.Kind.ToString();
            data["goal"] = Stage.Goal; data["makes"] = Makes;
            data["bag"] = Stage.Bag; data["balls"] = Balls;
            data["window"] = WindowNow;
            data["paceMin"] = Stage.PaceMin; data["paceMax"] = Stage.PaceMax;
            data["lamp"] = Stage.Lamp; data["lampStart"] = _lampStart; data["lampOpen"] = Stage.LampOpen; data["lampShut"] = Stage.LampShut;
            data["why"] = Why;
            data["won"] = Won; data["lost"] = Lost;
            data["boss"] = Stage.Kind == StageKind.Boss ? Stage.Name : "";
        }

        public override string FinalCall(Shot s) => s.Title;

        /// <summary>The panel: the stage and its rule, the goal, the bag, the stars still live, the last putt.</summary>
        public override VisualElement Board()
        {
            VisualElement box = Ui.Box("");
            VisualElement head = Ui.Box("sheet-card",
                Ui.Text((Stage.KindWord + "   ·   " + Stage.Code).ToUpperInvariant(), "kick", Ui.Bold),
                Ui.Title(Stage.Name, "title-20 gl-stage-name"),
                Ui.Text(Stage.Rule, "sub wrap", Ui.Medium).Margin(2));
            box.Add(head);

            bool boss = Stage.Kind == StageKind.Boss;
            VisualElement goal = Pips(Stage.Goal, i => i < Makes, null, boss ? (Color?)Ui.Red : null);
            VisualElement bag = Ui.Box("gl-balls");
            for (int i = 0; i < Stage.Bag; i++) bag.Add(Ui.Box(i < Balls ? "gl-ball" : "gl-ball gl-ball-out"));
            VisualElement tally = Ui.Box("sheet-card",
                Ui.Box("row gl-line", Ui.Text(boss ? "HITS" : Stage.InARow ? "RUN" : "MAKES", "kick gl-line-word", Ui.Bold), goal),
                Ui.Box("row gl-line", Ui.Text("BALLS", "kick gl-line-word", Ui.Bold), bag));
            if (!Over && Stage.Kind != StageKind.Bonus)
                tally.Add(Ui.Box("row gl-line", Ui.Text("A CLEAR NOW", "kick gl-line-word", Ui.Bold), new StarRow(StarsLive, 16f)));
            box.Add(tally);

            Shot last = Last;
            if (last == null) return box;
            Label call = Ui.Text(last.Title, "rule-call", Ui.Bold);
            call.style.color = last.Good ? (Color)Ui.Bright : (Color)Ui.Red;
            VisualElement read = Ui.Box("sheet-card rule-callcard", call);
            if (!string.IsNullOrEmpty(last.Sub)) read.Add(Ui.Text(last.Sub, "sub wrap center", Ui.Medium));
            AppPutt.Verdict v = last.V;
            // The reads are the bosses' rewards: entry is the Entry gauge's (The Lip), pace the Metronome's (Three-Putt).
            if (GreenProgress.HasGauge && v != null && v.Made && v.HasEntry)
                read.Add(Ui.Box("row gl-line", Ui.Text("ENTRY", "kick gl-line-word", Ui.Bold), Ui.Text(EntryWords(Scoring.OffLine(v, last.Feet, last.Tee), v.OffLine), "body gl-line-v", Ui.Medium)));
            if (GreenProgress.HasMetronome && v != null && v.Rollout.HasValue)
                read.Add(Ui.Box("row gl-line", Ui.Text("PACE", "kick gl-line-word", Ui.Bold), Ui.Text(PaceWords(v.Rollout.Value), "body gl-line-v", Ui.Medium)));
            box.Add(read);
            return box;
        }

        public static string EntryWords(double off, double signed) => off < 0.5 ? "Dead center" : off.ToString("0", CultureInfo.InvariantCulture) + "° " + (signed < 0 ? "left" : "right") + " of the line";
        public static string PaceWords(double rollout) => rollout >= 0 ? rollout.ToString("0", CultureInfo.InvariantCulture) + " in. past the cup" : (-rollout).ToString("0", CultureInfo.InvariantCulture) + " in. short";
    }

    /// <summary>A row of stars, the first few gold.</summary>
    public sealed class StarRow : VisualElement
    {
        static readonly Color Gold = Ui.Amber, Off = new Color32(48, 53, 58, 255);   // amber, surface-3 (docs/design-guidelines.md)
        readonly int _lit, _count;
        readonly float _size;

        public StarRow(int lit, float size, int count = 3)
        {
            _lit = lit; _size = size; _count = count;
            style.width = size * count + size * 0.3f * (count - 1);
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            Painter2D p = context.painter2D;
            for (int i = 0; i < _count; i++)
            {
                Vector2 c = new Vector2(_size * 0.5f + i * _size * 1.3f, _size * 0.53f);
                p.fillColor = i < _lit ? Gold : Off;
                p.BeginPath();
                for (int k = 0; k < 10; k++)
                {
                    float a = (-90f + k * 36f) * Mathf.Deg2Rad, r = (k % 2 == 0 ? 0.5f : 0.21f) * _size;
                    Vector2 at = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (k == 0) p.MoveTo(at); else p.LineTo(at);
                }
                p.ClosePath();
                p.Fill();
            }
        }
    }
}
