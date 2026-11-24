namespace PuttrApp
{
    // Each rule and Vertex game's own sounds: the ambient loop under its 3D world, and the sound for each putt it calls
    // (played by GameBridge as the putt is scored; the 3D scene plays the rest). Soundly library slots,
    // Audio/Library/<slot>_N.wav, filled by Tools/import-sounds.py from SoundDrop/<slot>/.
    //
    // No two games share a sound: each has slots of its own (test-reports sound-audit.md, section 3). They are being
    // recorded one slot at a time, so every slot names what the game played before as its fallback (GameSounds.Own):
    // the game keeps sounding as it did until its own recordings are imported, then changes over by itself.
    //
    // As a game ends, its putt sound is its own win, not the shared rule_win.

    /// <summary>Picks a game's own slot, or the old shared one until the game's own is imported.</summary>
    public static class GameSounds
    {
        public static bool Has(string slot) => PuttrArcade.GameAudio.Has(slot);
        public static string Own(string slot, string fallback) => PuttrArcade.GameAudio.Own(slot, fallback);

        /// <summary>The usual putt sound: the game's win once it is over, else its make or its miss (each falling back to the
        /// rule games' shared one). A null make or miss leaves that putt to the scene.</summary>
        public static string Putt(RuleGame g, Shot s, string win, string make, string miss)
            => g.Over ? Own(win, "rule_win") : s.Good ? (make == null ? null : Own(make, "rule_make")) : (miss == null ? null : Own(miss, "rule_miss"));

        /// <summary>For a putt the game's scene answers with its own sound once that is recorded: nothing here then, the shared
        /// sound until it is.</summary>
        public static string SceneOr(string sceneSlot, string fallback) => Has(sceneSlot) ? null : fallback;
    }

    // ── Rule games ──────────────────────────────────────────────────────────

    public sealed partial class TourJuiceGame
    {
        // No bed (Matt, 2026-11-21): Tour Juice plays with no background sound.
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("tourjuice_tour_quality", "rule_win")
            : s.Title == "TOUR QUALITY" ? GameSounds.Own("tourjuice_tour_quality", "rule_bonus")
            : s.Good ? GameSounds.Own("tourjuice_make", "rule_make") : GameSounds.Own("tourjuice_miss", "rule_miss");
    }

    public sealed partial class SpeedDemonGame
    {
        public override string Bed => GameSounds.Own("speeddemon_strip_loop", null);
        public override string SoundFor(Shot s) => GameSounds.Putt(this, s, "speeddemon_win", "speeddemon_make", "speeddemon_miss");
    }

    public sealed partial class MetalGame
    {
        public override string Bed => GameSounds.Own("metal_forge_loop", null);
        // A make plays no sound of its own, just the voice (Matt, 2026-11-21: the guitar riff on a make is gone).
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("metal_win", "rule_win") : s.Good ? null : GameSounds.Own("metal_miss", "rule_miss");
    }

    public sealed partial class LabGame
    {
        // No bed (Matt, 2026-11-21): the Lab plays with no background sound.
        public override string SoundFor(Shot s) => GameSounds.Putt(this, s, "lab_make", "lab_make", "lab_miss");
    }

    public sealed partial class FullWorkoutGame
    {
        public override string Bed => GameSounds.Own("fullworkout_gym_loop", null);
        public override string SoundFor(Shot s) => GameSounds.Putt(this, s, "fullworkout_win", "fullworkout_make", "fullworkout_miss");
    }

    public sealed partial class CompareGame
    {
        public override string Bed => GameSounds.Own("compare_bay_loop", null);
        public override string SoundFor(Shot s) => GameSounds.Putt(this, s, "compare_result", "compare_make", "compare_miss");
    }

    // Center Cut keeps rule_make, rule_miss, rule_bonus and rule_win (its SoundFor is in RuleGamesD.cs; the scene plays the putt).
    public sealed partial class CenterCutGame
    {
        public override string Bed => GameSounds.Own("centercut_bed_loop", null);
    }

    public sealed partial class DeadCenterGame
    {
        // No bed (Matt, 2026-11-21): Dead Center plays with no background music.
        public override string SoundFor(Shot s) => GameSounds.Putt(this, s, "deadcenter_bullseye", "deadcenter_make", "deadcenter_miss");
    }

    public sealed partial class AssessmentGame
    {
        public override string Bed => GameSounds.Own("assessment_floor_loop", null);
        public override string SoundFor(Shot s) => GameSounds.Putt(this, s, "assessment_station", "assessment_make", "assessment_miss");
    }

    // HORSE: the scene plays the swish, the rim and the gym's cheer; a letter is the shot-clock buzzer.
    public sealed partial class HorseGame
    {
        public override string Bed => GameSounds.Own("horse_gym_loop", null);
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("horse_win", "rule_win")
            : s.Title == "LETTER" ? GameSounds.Own("horse_letter", "horse_neigh")
            : s.Good ? GameSounds.SceneOr("horse_cheer", "rule_make") : GameSounds.SceneOr("horse_rim", "rule_miss");
    }

    // Oh-One: the dart's thud is the scene's; the checkout is the game's win (once only: the scene no longer plays rule_win).
    public sealed partial class OhOneGame
    {
        // No bed (Matt, 2026-11-21): Oh-One plays with no background sound.
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("ohone_checkout", "rule_win")
            : s.Good ? GameSounds.SceneOr("ohone_throw", "rule_make") : GameSounds.Own("ohone_bounce_out", "rule_miss");
    }

    // Reducer: the scene plays the gate's pass and the laser trip alarm.
    public sealed partial class ReducerGame
    {
        // No bed (Matt, 2026-11-23): Reducer plays with no background sound.
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("reducer_pass", "rule_win")
            : s.Good ? GameSounds.SceneOr("reducer_pass", "rule_make") : GameSounds.SceneOr("reducer_alarm", "rule_miss");
    }

    // Match Play: the gallery reacts to a hole won, lost or halved (Points is 1 when the first Player won the hole, -1 when
    // the second did; a hole is decided on the second Player's putt).
    public sealed partial class MatchPlayGame
    {
        public override string Bed => "seaside_loop";
        public override string SoundFor(Shot s)
        {
            if (Over) return GameSounds.Own(Winner == 1 ? "matchplay_hole_lost" : "matchplay_win", "golf_clap");
            if (s.Player != 1) return s.Good ? "rule_make" : "rule_miss";
            if (s.Points > 0) return GameSounds.Own("matchplay_hole_won", "golf_clap");
            if (s.Points < 0) return GameSounds.Own("matchplay_hole_lost", "golf_clap");
            return GameSounds.Own("matchplay_halved", s.Good ? "rule_make" : "rule_miss");
        }
    }

    // High Striker: the bell over 80 is its own; the puck's slide and the dud are the scene's and the game's.
    public sealed partial class HighStrikerGame
    {
        public override string Bed => "carnival_loop";
        public override string SoundFor(Shot s)
        {
            string putt = s.Points > 80 ? "bell_ding" : s.Points > 0 ? GameSounds.SceneOr("highstriker_puck", "rule_make") : GameSounds.Own("highstriker_dud", "rule_miss");
            return Over ? GameSounds.Own("highstriker_win", putt) : putt;
        }
    }

    // Beer Pong (the rule game): the cup and the ball are its own already.
    public sealed partial class BeerPongGame
    {
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("beerpong_win", "rule_win")
            : s.Title == "CUP!" || s.Title == "BALLS BACK" || s.Good ? "cup_plop" : "pingpong_bounce";
    }

    // Leapfrog: the scene plays the frog, the lily pad and the water.
    public sealed partial class LeapfrogGame
    {
        public override string Bed => "pond_loop";
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("leapfrog_win", "rule_win")
            : s.Good ? GameSounds.SceneOr("leapfrog_make", "rule_make") : GameSounds.SceneOr("leapfrog_lilypad", "rule_miss");
    }

    // Doors: the creak and the slam of a door that opened are its own, and so is the temple gong it ends on.
    public sealed partial class DoorsGame
    {
        // No bed (Matt, 2026-11-23): Doors plays with no background sound.
        public override string SoundFor(Shot s) => Over ? "gong" : s.Points >= 10 ? "door_creak" : s.Points > 0 ? "door_slam" : GameSounds.Own("doors_miss", "rule_miss");
    }

    public sealed partial class HighSideGame
    {
        public override string Bed => "wind_loop";
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("highside_win", "rule_win")
            : s.Good ? GameSounds.SceneOr("highside_make", "rule_make") : GameSounds.Own("highside_low_side", "rule_miss");
    }

    public sealed partial class SwitchbackGame
    {
        public override string Bed => GameSounds.Own("switchback_drive_loop", null);
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("switchback_win", "rule_win")
            : s.Good || s.Points > 0 ? GameSounds.SceneOr("switchback_make", "rule_make") : GameSounds.Own("switchback_miss", "rule_miss");   // a putt holed off the line still scored: never the miss sound
    }

    // Gears: the gear shift on a good putt is its own; the scene plays the clunk in.
    public sealed partial class GearsGame
    {
        public override string Bed => GameSounds.Own("gears_machine_loop", null);
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("gears_make", "rule_win") : s.Good ? "gear_shift" : GameSounds.Own("gears_grind", "rule_miss");
    }

    public sealed partial class GrooveGame
    {
        public override string Bed => GameSounds.Own("groove_stage_loop", null);
        public override string SoundFor(Shot s) => GameSounds.Putt(this, s, "groove_win", "groove_make", "groove_scratch");
    }

    // Around the Clock: each hour reached chimes once; the clock's own chime ends it.
    public sealed partial class AroundTheClockGame
    {
        public override string Bed => GameSounds.Own("clock_study_loop", null);
        public override string SoundFor(Shot s) => _won >= 0 ? "clock_chime" : Over ? GameSounds.Own("clock_chime", "rule_win")
            : s.Good ? GameSounds.Own("clock_hour", "rule_make") : GameSounds.Own("clock_miss", "rule_miss");
    }

    // ── Vertex sensor games ─────────────────────────────────────────────────
    // The putter on the ball in every replay is the shared putter_strike / putter_swing (physics, allowed to be shared).

    public sealed partial class PendulumGame
    {
        public override string Bed => GameSounds.Own("pendulum_hall_loop", "room_loop");
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("pendulum_make", "rule_win")
            : s.Title.Contains("×") ? GameSounds.Own("pendulum_multiplier", "rule_bonus")
            : s.Good ? GameSounds.Own("pendulum_make", "rule_make") : GameSounds.Own("pendulum_miss", "rule_miss");
    }

    public sealed partial class WaltzGame
    {
        public override string Bed => GameSounds.Own("waltz_ballroom_loop", "room_loop");
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("waltz_swell", "rule_win")
            : s.Title.Contains("×") ? GameSounds.Own("waltz_make", "rule_bonus")
            : s.Good ? GameSounds.Own("waltz_make", "rule_make") : GameSounds.Own("waltz_miss", "rule_miss");
    }

    // Pure: a PURE strike is the scene's singing bowl.
    public sealed partial class PureGame
    {
        public override string Bed => GameSounds.Own("pure_cavern_loop", "space_shimmer_loop");
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("pure_perfect", "rule_win")
            : s.Title.Contains("×") || s.Title.StartsWith("PURE") ? GameSounds.SceneOr("pure_perfect", "rule_bonus")
            : s.Good ? GameSounds.Own("pure_make", "rule_make") : GameSounds.Own("pure_miss", "rule_miss");
    }

    // Carbon Copy: the scene stamps a match.
    public sealed partial class CarbonCopyGame
    {
        public override string Bed => GameSounds.Own("carboncopy_office_loop", "probe_loop");
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("carboncopy_match", "rule_win")
            : s.Good ? GameSounds.SceneOr("carboncopy_match", s.Title.Contains("×") || s.Title.StartsWith("PURE") || s.Title == "LEVEL CLEARED" ? "rule_bonus" : "rule_make")
            : GameSounds.Own("carboncopy_miss", "rule_miss");
    }

    // Square Up: calibration putts are quiet; the scene plays the beam and the struck gate.
    public sealed partial class SquareUpGame
    {
        public override string Bed => GameSounds.Own("squareup_arena_loop", "wind_loop");
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("squareup_level", "rule_win")
            : s.Title == "LEVEL CLEARED" || s.Title == "GATE SET" ? GameSounds.Own("squareup_level", "rule_bonus")
            : s.Title.StartsWith("CALIB") ? null
            : s.Good ? GameSounds.SceneOr("squareup_beam", "rule_make") : GameSounds.SceneOr("squareup_miss", "rule_miss");
    }

    public sealed partial class StretchGame
    {
        public override string Bed => GameSounds.Own("stretch_studio_loop", "range_loop");
        public override string SoundFor(Shot s) => Over ? GameSounds.Own("stretch_ok", "rule_win")
            : s.Title.Contains("×") || s.Title.StartsWith("PURE") || s.Title == "LEVEL CLEARED" ? GameSounds.Own("stretch_ok", "rule_bonus")
            : s.Good ? GameSounds.SceneOr("stretch_ok", "rule_make") : GameSounds.SceneOr("stretch_short", "rule_miss");
    }

    // Lockdown ends when a run of breaks reaches the limit: the scene sounds the vault alarm.
    public sealed partial class LockdownGame
    {
        public override string Bed => GameSounds.Own("lockdown_vault_loop", "probe_loop");
        public override string SoundFor(Shot s)
        {
            if (Over) return GameSounds.SceneOr("lockdown_alarm", "rule_win");
            if (s.Title.StartsWith("CALIB")) return null;
            // A locked spoke is the scene's bolt, a break its rattle.
            return GameSounds.SceneOr("lockdown_bolt", base.SoundFor(s));
        }
    }

    // The Yips: the scene plays every hit, block, stage and the end; until those are recorded, the old calls.
    public sealed partial class TheYipsGame
    {
        public override string Bed => GameSounds.Own("theyips_lair_loop", "fire_small_loop");
        public override string SoundFor(Shot s)
        {
            if (Over) return Won ? GameSounds.SceneOr("theyips_victory", "rule_win") : GameSounds.SceneOr("theyips_defeat", "game_over");
            return GameSounds.SceneOr("theyips_hit", LastTaken > 0 ? "rule_miss" : s.Good ? (LastCrit ? "rule_bonus" : "rule_make") : "rule_miss");
        }
    }
}
