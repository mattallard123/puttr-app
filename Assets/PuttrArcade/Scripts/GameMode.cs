// One game in the arcade.
//
// The arcade does everything up to the back of the console: it replays the
// putt on the ramp, drops a holed ball into the cup and rolls it out at the
// head of the hall, or lets a missed ball run up the ramp and off the end. A
// game is what is built in the hall, and what happens to the ball there.
//
// To add a game: subclass this, and add it to the list in Arcade.Awake.
using System.Collections;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public abstract class GameMode
    {
        /// <summary>The arcade this game belongs to: its interface, sound, balls and effects.</summary>
        public Arcade Arcade;
        /// <summary>Everything this game built. Active only while the game is being played.</summary>
        public Transform Root;

        /// <summary>For the home screen: "Lanes".</summary>
        public abstract string Name { get; }
        /// <summary>For the home screen: two or three short lines.</summary>
        public abstract string Blurb { get; }
        /// <summary>Shown as the game starts: one line on how to score.</summary>
        public abstract string Rule { get; }

        /// <summary>Where the camera goes once the ball is in the hall.</summary>
        public abstract Vector3 CameraPosition { get; }
        public abstract Vector3 CameraLook { get; }
        /// <summary>The camera's field of view while it is there, in degrees.</summary>
        public virtual float CameraFov => 38f;

        // The picture on this game's card on the home screen: a close look at the thing the
        // game is about, taken from its own set. By default, the game's own view, drawn in.
        public virtual Vector3 CardFrom => CameraPosition;
        public virtual Vector3 CardLook => CameraLook;
        public virtual float CardFov => CameraFov * 0.55f;
        /// <summary>Set the scene for the card's picture (after Preview), and put it back after.</summary>
        public virtual void PoseForCard() { }
        public virtual void AfterCard() { }

        /// <summary>
        /// A stage game has no use for the Puttr on screen. It takes the
        /// putt's numbers (line, pace, push or pull) and plays them out on a
        /// set of its own: see StageMode. The camera stays on that set.
        /// </summary>
        public virtual bool OwnStage => false;

        /// <summary>How far from the camera a stage's shadows are drawn, in units.</summary>
        public virtual float ShadowDistance => 140f;
        /// <summary>On a stage, what is nearer than BlurFrom is sharp and what is beyond BlurTo is soft.</summary>
        public virtual float BlurFrom => 100000f;
        public virtual float BlurTo => 200000f;

        /// <summary>
        /// A game played on the mat itself: the hole is covered and the ball
        /// never leaves the ramp. The arcade replays the putt as far as the
        /// camera saw it, then hands the ball to RollOnMat. The camera stays
        /// on CameraPosition throughout.
        /// </summary>
        public virtual bool OnMat => false;

        /// <summary>
        /// Carry the ball on from where the camera left it. from is in console
        /// inches (x right, y back toward the Player, the hole at zero);
        /// speed is in/s there. Finish is called next, with the ball.
        /// </summary>
        public virtual IEnumerator RollOnMat(Ball ball, PuttResult putt, Vector2 from, float speed) { yield break; }

        /// <summary>A stage game's whole turn: play the putt out, score it, say so.</summary>
        public virtual IEnumerator Play(PuttResult putt) { yield break; }

        // ── For the home and setup screens ──────────────────────────────────

        /// <summary>The icon's file name in Resources/PuttrArcade/Icons, without "Icon".</summary>
        public virtual string Icon => "Target";
        /// <summary>The color of the icon and its card.</summary>
        public virtual Color Accent => Make.Bright;
        /// <summary>Small labels on the card: "10 frames", "5 kicks".</summary>
        public virtual string[] Tags => new string[0];
        /// <summary>1 easy, 2 medium, 3 hard.</summary>
        public virtual int Difficulty => 2;
        public virtual int MaxPlayers => 4;
        /// <summary>Who a Player alone plays against ("Puttr"), or null where a Player alone
        /// just plays for a score. Shown on the setup screen.</summary>
        public virtual string SoloOpponent => null;
        /// <summary>Three short lines for the setup screen: what the putt does, and how to score.</summary>
        public virtual string[] HowTo => new string[0];

        // ── For the leaderboard ─────────────────────────────────────────────

        /// <summary>This Player's score in the game just finished.</summary>
        public virtual int Score(int player) => 0;
        /// <summary>True where fewer is better (putts taken).</summary>
        public virtual bool LowerIsBetter => false;
        /// <summary>What the score counts: "ft", "putts". Empty for points.</summary>
        public virtual string ScoreUnit => "";

        /// <summary>What a score of n is counted in, for the end card: "points", "feet", "pins".</summary>
        public virtual string ScoreWord(int n) => n == 1 ? "point" : "points";

        /// <summary>
        /// For games played in sides: the rows for the end card and its
        /// headline ("Ana & Ben win"). Null where every Player is for themselves.
        /// </summary>
        public virtual Hud.Line[] Results(out string headline) { headline = null; return null; }

        /// <summary>Left (-1) or right (+1) while the game waits for a putt: arrow keys, the remote, or a swipe.</summary>
        public virtual void Nudge(int direction) { }

        /// <summary>What the place sounds like.</summary>
        public virtual GameAudio.Scene Sound => GameAudio.Scene.Table;

        /// <summary>Lanes only: how far right of its line a holed ball comes out, inches.</summary>
        public virtual float HoledOffsetX => 0f;

        /// <summary>True for a game that is watched from its own place the whole time and takes the
        /// ball there: the putting mat and the hole are never shown. The arcade calls Send in place
        /// of rolling the ball up the mat.</summary>
        public virtual bool SkipMat => false;

        /// <summary>True for a game whose action is in the middle of the screen, where the big
        /// banner would sit on top of it: its banners are drawn small, along the bottom.</summary>
        public virtual bool SmallBanner => false;

        /// <summary>False for a game that has no difficulty setting: it is always played to the
        /// real hole, and setup does not offer Easy, Medium and Hard.</summary>
        public virtual bool HasLevels => true;

        /// <summary>Which Player is putting now (0 is the first). The arcade uses it to apply that
        /// Player's own difficulty.</summary>
        public virtual int Putter => 0;

        /// <summary>True for a game that reads the difficulty itself (Arcade.Level) and wants the
        /// putt as it really was, not drawn in toward the middle.</summary>
        public virtual bool OwnLevels => false;

        /// <summary>Where to putt from, shown with the game's own prompt. Null for a game that
        /// calls the distance or the tee itself.</summary>
        public virtual string Tee => Over ? null : DealtTeeText;

        // ── Where to putt from, dealt fresh for each putt ───────────────────
        // Every game that does not set its own distance deals one before each putt (Arcade
        // calls DealTee): a distance from the range for the putter's difficulty, and one of the
        // three tees. Shown on the big tee sign and called out by the announcer. Cricket, On
        // Fire, Major League and Windmill set their own and say DealsTee false.

        protected static readonly int[,] DealtFeet = { { 3, 6 }, { 5, 9 }, { 8, 11 } };
        protected static readonly string[] DealtTeeWord = { "left", "center", "right" };
        readonly System.Random _teeDice = new System.Random();
        protected int DealtFeetNow, DealtTee;
        // puttr-app-2: a phone held upright. A game that says HasPortrait arranges itself for it
        // (its own camera and layout); any other game is shown as on a wide screen, only smaller.
        public static bool Portrait => Screen.height > Screen.width;
        public virtual bool HasPortrait => false;

        // puttr-app-2: a game that is about the putt itself. The camera stays on the mat and the hole
        // (the game's own CameraPosition) and never follows the ball through the console.
        public virtual bool WatchesMat => false;
        // puttr-app-2: the name beside the logo, when it is not the game's own (a tournament round, League Night).
        public virtual string DisplayName => Name;
        // puttr-app-2: once the putt has reached the hole or missed it, the ball makes no more sound
        // (no roll back down the ramp, no bumps behind the console): only the cup, then the game's call.
        public virtual bool QuietBall => false;
        /// <summary>puttr-app-2: the moment a putt is decided: just after it drops in the cup, or as it misses or stops short.</summary>
        public virtual void Called(PuttResult putt) { }
        // puttr-app-2: the app draws this game's numbers itself, so the arcade's last-putt card and
        // opening banner stay away.
        public virtual bool OwnReadout => false;
        // puttr-app-2: the part of the screen the game is drawn in (0 to 1, from the bottom left).
        // The app puts its own panel in the rest.
        public virtual Rect Viewport => new Rect(0f, 0f, 1f, 1f);

        // puttr-app-2: what the putt being played was dealt, for saving it to the Player's account.
        public virtual int PuttFeet => DealtFeetNow;
        public virtual int PuttTee => DealtTee;

        /// <summary>Pick the next distance and tee, put them up, and say them.</summary>
        public void DealTee(bool aloud = true)
        {
            int level = Arcade != null ? Mathf.Clamp(Arcade.Level, Arcade.Easy, Arcade.Hard) : Arcade.Easy;
            DealtFeetNow = _teeDice.Next(DealtFeet[level, 0], DealtFeet[level, 1] + 1);
            DealtTee = _teeDice.Next(1, 4);
            ShowTeeSign();
            if (aloud && Arcade != null) Arcade.Audio.Voice(VoicePrefix + "_distance_" + DealtFeetNow + "_feet", VoicePrefix + "_tee_" + DealtTeeWord[DealtTee - 1]);
        }

        protected void ShowTeeSign() { if (Arcade != null && Arcade.Hud != null) { Arcade.Hud.SetTeeSign(DealtFeetNow, DealtTee); ShowAim(); } }

        // The ideal putt, top right (AimView): from the dealt tee to where it should cross the
        // hole's centre line, with the band that counts at this difficulty.
        public virtual bool ShowsAim => true;
        /// <summary>Where the perfect putt crosses the hole's centre line, inches right of centre.</summary>
        public virtual float AimTargetInches => 0f;
        /// <summary>How wide the band that counts is: what Easy and Medium widen to, or the cup.</summary>
        public virtual float AimGoalInches => OwnLevels || Arcade == null ? PuttrArcade.Arcade.HoleInches : Arcade.Goal;
        /// <summary>For games where pace is the point: the window of speed at the hole (in/s) that
        /// counts, on a bar from min to max. False for games about line.</summary>
        public virtual bool AimPace(out float low, out float high, out float min, out float max) { low = high = min = max = 0f; return false; }
        protected void ShowAim() { if (Arcade != null && ShowsAim && DealtFeetNow > 0) Arcade.ShowAim(DealtFeetNow, DealtTee, AimTargetInches, AimGoalInches); }
        protected void HideTeeSign() { if (Arcade != null && Arcade.Hud != null) Arcade.Hud.SetTeeSign(0, 0); }

        /// <summary>The dealt distance and tee, for the Tee line above the prompt.</summary>
        protected string DealtTeeText => DealtFeetNow <= 0 ? null : DealtFeetNow + " feet, " + DealtTeeWord[DealtTee - 1] + " tee";

        /// <summary>Whose voice calls the distance and tee: the clips are VoicePrefix_distance_N_feet
        /// and VoicePrefix_tee_left/center/right, one set per game (Tools/generate-tee-vo.js).</summary>
        public virtual string VoicePrefix => "ml";

        /// <summary>False for a game that sets where to putt from itself.</summary>
        public virtual bool DealsTee => true;


        /// <summary>For a SkipMat game: put the ball into play. across is where the putt crossed the
        /// goal, -1 at its left edge to 1 at its right (the goal is as wide as the difficulty makes
        /// it); beyond that it missed the goal. speed is the pace at the hole, in inches a second.</summary>
        public virtual void Send(Ball ball, float across, float speed) { }

        /// <summary>How slow a holed ball may come out of the cup, in/s.</summary>
        public virtual float HoledMinSpeed => 3f;

        /// <summary>Build the set under Root. Called once, at start-up.</summary>
        public abstract void Build();

        /// <summary>Get the set ready to be looked at behind the home screen.</summary>
        public virtual void Preview() { }

        /// <summary>Start a new game for this many Players, and lay out the score table.</summary>
        public abstract void Begin(int players);

        /// <summary>Get the set ready for the next putt (rack pins, clear balls). Runs before every putt.</summary>
        public virtual IEnumerator Prepare() { yield break; }

        /// <summary>What to tell the Player while waiting for a putt.</summary>
        public abstract string Prompt { get; }

        /// <summary>
        /// The ball is in the hall and moving: out of the cup (holed) or off
        /// the end of the ramp. Return when it has done all it is going to do.
        /// </summary>
        public abstract IEnumerator Run(Ball ball, PuttResult putt, bool holed);

        /// <summary>
        /// Score the putt and say so. ball is null when nothing reached the
        /// hall (left short, or rolled back down the ramp). Release the ball
        /// to the arcade here unless the game keeps it in play.
        /// </summary>
        public abstract IEnumerator Finish(Ball ball, PuttResult putt, bool holed);

        public abstract bool Over { get; }
        /// <summary>For the end card: a title, and the lines under it.</summary>
        public abstract void Final(out string title, out string body);

        /// <summary>Called every frame while this game is on.</summary>
        public virtual void Tick() { }

        /// <summary>Leaving the game: put everything away.</summary>
        public virtual void End() { }

        // ── For subclasses ──────────────────────────────────────────────────


        /// <summary>Where a ball rests on a surface at height y: its center.</summary>
        protected static Vector3 Resting(float x, float y, float z) => new Vector3(x, y + LaneSpec.BallRadius, z);
    }
}
