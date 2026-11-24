// Every game's own sounds (test-reports sound-audit.md, section 3): the fallback that keeps a game sounding as before
// until its recordings are imported, and the table of which slots belong to which game, checked against the code so
// no two games share an identity sound. Nothing here plays a sound: the library lookup is swapped for a list.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PuttrArcade;
using UnityEngine;

namespace PuttrApp.Tests
{
    public class GameSoundsTests
    {
        Func<string, bool> _probe;

        [SetUp]
        public void SwapProbe() { _probe = GameAudio.Probe; GameAudio.ForgetSlots(); }

        [TearDown]
        public void PutProbeBack() { GameAudio.Probe = _probe; GameAudio.ForgetSlots(); }

        void Imported(params string[] slots)
        {
            var have = new HashSet<string>(slots);
            GameAudio.Probe = slot => have.Contains(slot);
            GameAudio.ForgetSlots();
        }

        [Test]
        public void ASlotWithNoRecordingsFallsBackToWhatTheGamePlayedBefore()
        {
            Imported();
            Assert.IsFalse(GameAudio.Has("gears_grind"));
            Assert.AreEqual("rule_miss", GameAudio.Own("gears_grind", "rule_miss"));
            Assert.IsNull(GameAudio.Own("gears_machine_loop", null), "no bed of its own yet: the room's");
            Assert.AreEqual("rule_miss", GameSounds.Own("gears_grind", "rule_miss"));
            Assert.AreEqual("rule_make", GameSounds.SceneOr("leapfrog_make", "rule_make"), "the shared sound until the scene's own is in");
        }

        [Test]
        public void ASlotChangesOverOnceItsFirstTakeIsImported()
        {
            Imported("gears_grind", "gears_machine_loop", "leapfrog_make");
            Assert.IsTrue(GameAudio.Has("gears_grind"));
            Assert.AreEqual("gears_grind", GameAudio.Own("gears_grind", "rule_miss"));
            Assert.AreEqual("gears_machine_loop", GameAudio.Own("gears_machine_loop", null));
            Assert.IsNull(GameSounds.SceneOr("leapfrog_make", "rule_make"), "the scene plays it, so the putt sound steps aside");
            Assert.AreEqual("rule_win", GameAudio.Own("gears_win_not_recorded", "rule_win"), "a neighbour's slot changes nothing");
        }

        [Test]
        public void NoSlotIsNeverASlot()
        {
            Imported("");
            Assert.IsFalse(GameAudio.Has(null));
            Assert.IsFalse(GameAudio.Has(""));
            Assert.AreEqual("game_over", GameAudio.Own(null, "game_over"));
        }

        [Test]
        public void TheLibraryIsAskedOnceASlot()
        {
            int asked = 0;
            GameAudio.Probe = slot => { asked++; return slot == "derby_home"; };
            GameAudio.ForgetSlots();
            for (int i = 0; i < 5; i++) { GameAudio.Has("derby_home"); GameAudio.Has("derby_snort"); }
            Assert.AreEqual(2, asked);
            GameAudio.ForgetSlots();
            GameAudio.Has("derby_home");
            Assert.AreEqual(3, asked, "forgotten, it is asked again");
        }

        // ── Which sounds are whose ──────────────────────────────────────────

        sealed class Game
        {
            public string Name;
            public string[] Files;      // under Assets/
            public string[] New;        // the slots the audit gave it (section 3), each to be recorded for it alone
            public string[] Keeps = new string[0];   // recordings it already had, now its alone
        }

        const string ArcadeDir = "PuttrArcade/Scripts/", SceneDir = "PuttrArcade/Scripts/Scenes/", AppDir = "PuttrApp/Scripts/", Beds = "PuttrApp/Scripts/Views/Beds.cs";

        static Game A(string name, string mode, string[] fresh, params string[] keeps) => new Game { Name = name, Files = new[] { ArcadeDir + mode, ArcadeDir + "GameAudio.cs" }, New = fresh, Keeps = keeps };
        static Game R(string name, string scene, string[] fresh, params string[] keeps) => new Game { Name = name, Files = scene == null ? new[] { Beds } : new[] { SceneDir + scene, Beds }, New = fresh, Keeps = keeps };
        static string[] S(params string[] slots) => slots;

        // freeputt_goal is not here: it is played by GameBridge once a Free putt goal is reached (see the sound report).
        static readonly Game[] Games =
        {
            // Arcade
            A("Cricket", "CricketMode.cs", S("cricket_dart_throw", "cricket_dart_hit", "cricket_bounce_out", "cricket_mark", "cricket_treble", "cricket_arena_loop")),
            A("Derby", "DerbyMode.cs", S("derby_bugle", "derby_hooves", "derby_home", "derby_snort", "derby_crowd_loop"), "horse_neigh"),
            A("Ten Pin", "LanesMode.cs", S("tenpin_pin_clack", "tenpin_ball_roll_loop", "tenpin_spare", "tenpin_gutter", "tenpin_alley_loop"), "strike"),
            A("On Fire", "OnFireMode.cs", S("onfire_ignite", "onfire_streak", "onfire_burnout"), "fire_small_loop", "fire_big_loop", "fire_whoosh", "fire_out"),
            A("Major League", "MajorLeagueMode.cs", S("ballpark_loop", "bat_swing", "organ", "majorleague_final"), "bat_crack", "mitt", "crowd_loop", "crowd_roar_loop", "cheer", "groan"),
            A("Putt Star", "PuttStarMode.cs", S("puttstar_co2", "puttstar_pyro", "puttstar_booed_off"), "concert_loop", "concert_cheer", "concert_boo", "rock_riff", "boom", "rock_screech"),
            new Game { Name = "Beer Pong", Files = new[] { ArcadeDir + "BeerPongMode.cs", Beds }, New = S("beerpong_rim_spin", "beerpong_miss", "beerpong_win"), Keeps = S("cup_plop", "pingpong_bounce") },
            A("Balloon Pop", "BalloonPopMode.cs", S("balloonpop_jackpot", "balloonpop_score", "balloonpop_no_pop", "balloonpop_sink"), "balloon_pop"),
            A("Archery", "ArcheryMode.cs", S("arrow_miss", "archery_bullseye", "archery_ring", "archery_miss"), "arrow_shot", "arrow_hit", "range_loop"),
            A("Dead Weight", "DeadWeightMode.cs", S("deadweight_release", "deadweight_clack", "deadweight_button", "deadweight_out")),   // the rink bed and the slide loop are not played (Matt, 2026-11-23: they droned like a truck)
            A("Send It", "SendItMode.cs", S("sendit_arena_loop", "sendit_landing", "sendit_crash_groan"), "engine_loop", "land", "crash"),
            A("Slingshot", "SlingshotMode.cs", S("slingshot_relay_lock", "slingshot_pulled_in", "slingshot_adrift", "slingshot_win"), "launch", "space_loop", "space_shimmer_loop", "probe_loop"),
            A("Spot Kick", "SpotKickMode.cs", S("spotkick_stadium_loop", "spotkick_goal", "spotkick_save", "spotkick_miss_groan", "spotkick_whistle"), "kick", "net", "post"),
            A("Tug of War", "TugOfWarMode.cs", S("tugofwar_big_pull", "tugofwar_mud_splat", "tugofwar_win", "tugofwar_field_loop")),
            A("Turf War", "TurfWarMode.cs", S("turfwar_throw", "turfwar_splat", "turfwar_big_splat", "turfwar_dribble", "turfwar_studio_loop")),
            A("Under the Cup", "UnderTheCupMode.cs", S("underthecup_pin", "underthecup_pocket", "underthecup_jackpot", "underthecup_zero", "underthecup_machine_loop")),
            A("Putt-ball", "PuttBallMode.cs", S("puttball_arcade_loop", "puttball_roll", "puttball_hop", "puttball_drop", "puttball_ring", "puttball_hundred", "puttball_gutter")),
            A("Windmill", "WindmillMode.cs", S("windmill_blade", "windmill_splash", "windmill_par", "windmill_ace", "windmill_park_loop")),
            new Game { Name = "Free putt", Files = new[] { ArcadeDir + "FreePuttMode.cs" }, New = S("freeputt_miss"), Keeps = S("coin") },
            new Game { Name = "Tracks", Files = new[] { AppDir + "TracksWorld.cs" }, New = S("tracks_roar"), Keeps = S("course_loop", "golf_drive", "golf_iron", "bunker", "splash", "golf_clap") },

            // Rule games
            R("Tour Juice", "TourJuiceScene.cs", S("tourjuice_pour", "tourjuice_fizzle", "tourjuice_make", "tourjuice_tour_quality", "tourjuice_miss"), "riser"),
            R("Speed Demon", null, S("speeddemon_make", "speeddemon_miss", "speeddemon_strip_loop", "speeddemon_win"), "engine_rev"),
            R("Metal Play", "MetalScene.cs", S("metal_bellows", "metal_miss", "metal_forge_loop", "metal_win"), "anvil", "sizzle"),
            R("The Lab", "LabScene.cs", S("lab_make", "lab_miss", "lab_scan"), "lab_beep"),
            R("Full Workout", "FullWorkoutScene.cs", S("fullworkout_make", "fullworkout_big", "fullworkout_miss", "fullworkout_gym_loop", "fullworkout_win")),
            R("Compare", "CompareScene.cs", S("compare_pick", "compare_make", "compare_miss", "compare_result", "compare_bay_loop")),
            R("Dead Center", "DeadCenterScene.cs", S("deadcenter_scope", "deadcenter_bullseye", "deadcenter_make", "deadcenter_miss")),
            R("Assessment", "AssessmentScene.cs", S("assessment_grade", "assessment_make", "assessment_miss", "assessment_station", "assessment_floor_loop")),
            R("Horse", "HorseScene.cs", S("horse_rim", "horse_letter", "horse_cheer", "horse_gym_loop", "horse_win"), "swish", "court_bounce"),
            R("Oh-One", "OhOneScene.cs", S("ohone_throw", "ohone_bounce_out", "ohone_checkout"), "dart_thud"),
            R("Reducer", "ReducerScene.cs", S("reducer_pass", "reducer_alarm", "reducer_shrink"), "laser_zap"),
            R("Match Play", "MatchPlayScene.cs", S("matchplay_hole_won", "matchplay_hole_lost", "matchplay_halved", "matchplay_board", "matchplay_win"), "seaside_loop"),
            R("High Striker", "HighStrikerScene.cs", S("highstriker_puck", "highstriker_dud", "highstriker_win"), "hammer_hit", "bell_ding", "carnival_loop"),
            R("Leapfrog", "LeapfrogScene.cs", S("leapfrog_lilypad", "leapfrog_make", "leapfrog_win"), "frog_croak", "water_plop", "pond_loop"),
            R("Doors", "DoorsScene.cs", S("doors_make", "doors_miss"), "door_creak", "door_slam", "stone_grind", "gong"),
            R("High Side", "HighSideScene.cs", S("highside_make", "highside_low_side", "highside_win"), "wind_loop"),
            R("Switchback", "SwitchbackScene.cs", S("switchback_make", "switchback_miss", "switchback_drive_loop", "switchback_win"), "synth_whoosh"),
            R("Gears", "GearsScene.cs", S("gears_make", "gears_grind", "gears_machine_loop"), "gear_shift", "ratchet"),
            R("Groove", "GrooveScene.cs", S("groove_make", "groove_scratch", "groove_stage_loop", "groove_win"), "drum_hit"),
            R("Around the Clock", "ClockScene.cs", S("clock_make", "clock_miss", "clock_hour", "clock_study_loop"), "clock_chime"),
            R("Center Cut", "CenterCutScene.cs", S("centercut_clap", "centercut_bell", "centercut_bed_loop"), "rule_make", "rule_miss", "rule_bonus", "rule_win"),

            // Vertex sensor games (putter_strike and putter_swing are the putter's physics, shared on purpose)
            R("Pendulum", "PendulumScene.cs", S("pendulum_hall_loop", "pendulum_beat", "pendulum_make", "pendulum_miss", "pendulum_multiplier")),
            R("Waltz", "WaltzScene.cs", S("waltz_ballroom_loop", "waltz_beat", "waltz_swell", "waltz_make", "waltz_miss")),
            R("Pure", "PureScene.cs", S("pure_cavern_loop", "pure_fill", "pure_perfect", "pure_make", "pure_miss")),
            R("Carbon Copy", "CarbonCopyScene.cs", S("carboncopy_scan", "carboncopy_copy", "carboncopy_match", "carboncopy_miss", "carboncopy_office_loop")),
            R("Square Up", "SquareUpScene.cs", S("squareup_beam", "squareup_block", "squareup_level", "squareup_miss", "squareup_arena_loop"), "gate"),
            R("Stretch", "StretchScene.cs", S("stretch_studio_loop", "stretch_band", "stretch_ok", "stretch_short")),
            R("Lockdown", "LockdownScene.cs", S("lockdown_vault_loop", "lockdown_bolt", "lockdown_corner", "lockdown_alarm", "lockdown_break", "lockdown_open")),
            R("The Yips", "TheYipsScene.cs", S("theyips_lair_loop", "theyips_hit", "theyips_crit", "theyips_block", "theyips_stage", "theyips_defeat", "theyips_victory")),
        };

        static string Source(string file) => File.ReadAllText(Path.Combine(Application.dataPath, file));
        static bool Names(string source, string slot) => source.Contains("\"" + slot + "\"");

        [Test]
        public void NoTwoGamesShareAnIdentitySound()
        {
            var owner = new Dictionary<string, string>();
            foreach (Game g in Games)
                foreach (string slot in g.New.Concat(g.Keeps))
                {
                    Assert.IsFalse(owner.TryGetValue(slot, out string other) && other != g.Name, slot + " belongs to both " + other + " and " + g.Name);
                    owner[slot] = g.Name;
                }
        }

        [Test]
        public void TheAuditsCountOfNewSlotsIsWired()
        {
            // 78 arcade, 85 rule, 42 sensor, Free putt's miss and Tracks' roar (freeputt_goal waits on GameBridge).
            Assert.AreEqual(78 + 85 + 42 + 1 + 1 - 11 /* Balloon Pop, Tour Juice, Lab, Beer Pong, Dead Center, Reducer, Doors and Dead Weight beds, Dead Weight's slide, Tug of War's crackling heave and slip, dropped */ + 7 /* Putt Ball */, Games.Sum(g => g.New.Length));
            Assert.AreEqual(Games.Sum(g => g.New.Length), Games.SelectMany(g => g.New).Distinct().Count(), "a slot listed twice");
        }

        [Test]
        public void SlotNamesAreTheSoundDropFolderNames()
        {
            var snake = new Regex("^[a-z0-9]+(_[a-z0-9]+)*$");
            foreach (string slot in Games.SelectMany(g => g.New)) Assert.IsTrue(snake.IsMatch(slot), slot + " is not a SoundDrop folder name");
        }

        [Test]
        public void EveryGamePlaysEachOfItsNewSlots()
        {
            foreach (Game g in Games)
            {
                string code = string.Concat(g.Files.Select(Source));
                foreach (string slot in g.New) Assert.IsTrue(Names(code, slot), g.Name + " never plays " + slot);
            }
        }

        [Test]
        public void NoGamesOwnCodePlaysAnotherGamesNewSlot()
        {
            // A file that is one game's alone (its mode or its scene; not the shared GameAudio.cs or Beds.cs).
            var uses = Games.SelectMany(g => g.Files).GroupBy(f => f).ToDictionary(x => x.Key, x => x.Count());
            foreach (Game g in Games)
                foreach (string file in g.Files.Where(f => uses[f] == 1))
                {
                    string code = Source(file);
                    foreach (Game other in Games.Where(o => o != g))
                        foreach (string slot in other.New)
                            Assert.IsFalse(Names(code, slot), file + " (" + g.Name + ") plays " + other.Name + "'s " + slot);
                }
        }
    }
}
