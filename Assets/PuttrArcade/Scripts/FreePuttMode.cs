// Free putt: the putt itself is the game. The life-size Puttr, its ramp and its hole, and every
// putt replayed on it from the camera's own frames: a holed putt drops into the cup, a miss
// climbs the ramp and comes back, a putt left short rolls back down. Nothing is behind the console.
//
// It never ends, and it keeps no numbers of its own. The app deals where to putt from (Feet, Tee),
// hears every putt (Arcade.AppPutt), and draws the session beside the picture: below it when the
// phone is upright, to the right when it is on its side (Viewport).
//
// puttr-app-2: this game exists only in the app, not in Puttr Arcade.
using System;
using System.Collections;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class FreePuttMode : GameMode
    {
        public const string Title = "Free Putt";
        /// <summary>Where the Player is putting from, set by the app before each putt.</summary>
        public static int Feet = 6, TeeNow = 2;
        /// <summary>The share of an upright screen the picture takes (from the top), and of a wide one (from the left).</summary>
        public const float UprightShare = 0.52f, WideShare = 0.58f;

        public override string Name => Title;
        /// <summary>What the top bar calls it when the app is using this view for something else: a round of a tournament, League Night.</summary>
        public static string Label;
        public override string DisplayName => string.IsNullOrEmpty(Label) ? Title : Label;
        public override string Blurb => "A driving range for putting.\nEvery putt replayed and measured.";
        public override string Rule => "Putt from anywhere. Every putt is measured.";
        public override string Icon => "Target";
        public override string[] Tags => new[] { "No score", "3 to 11 ft" };
        public override int MaxPlayers => 1;
        public override bool HasLevels => false;   // the putt is shown as it was, never drawn in toward the hole
        public override bool DealsTee => false;
        public override string Tee => null;
        public override bool ShowsAim => false;
        public override int PuttFeet => Feet;
        public override int PuttTee => TeeNow;
        public override bool WatchesMat => true;
        // puttr-app-2: silent under the putt (no room tone): the Lab and the other games played on this view included.
        public override GameAudio.Scene Sound => GameAudio.Scene.Quiet;
        public override bool OwnReadout => true;
        public override bool QuietBall => true;
        /// <summary>Whether a putt counts as made for the call, when the app has its own rule (a back door scores nothing in a round). Null: holed is made.</summary>
        public static Func<PuttResult, bool> Counts;
        /// <summary>puttr-app-2: a rule game plays its own sounds for each putt, so the coin and buzzer stay quiet.</summary>
        public static bool Silent;

        // The cup's own sound comes first (the arcade plays it as the ball drops); then a bright coin
        // for a putt that counts. puttr-app-2: the buzzer is Puttr's back-door sound, so it is kept for a holed
        // putt the round does not count (in the back door); an ordinary miss gets Free putt's own soft miss
        // (the arcade's soft miss until it is recorded).
        public override void Called(PuttResult putt)
        {
            if (Arcade == null || Silent) return;
            bool good = putt.Made && (Counts == null || Counts(putt));
            if (good) Arcade.Audio.Coin();
            else if (putt.Made) Arcade.Audio.Buzzer();
            else if (!Arcade.Audio.TryLibrary("freeputt_miss", 0.6f)) Arcade.Audio.Miss();
        }
        public override bool HasPortrait => true;
        public override string Prompt => "";       // the app's panel says where to putt from

        /// <summary>Set while a rule game's panel is up (RulePanel): wide, its cards lie over the picture, so the mat is
        /// drawn across the whole screen and framed in the part left of them. The app's other panels are solid columns.</summary>
        public static bool Layered;
        public override Rect Viewport => Portrait ? new Rect(0f, 1f - UprightShare, 1f, UprightShare) : Layered ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 0f, WideShare, 1f);
        public override Rect Picture => Portrait || !Layered ? Viewport : Middle;
        /// <summary>The middle of a wide screen: where a game drawn across all of it frames itself.</summary>
        public static Rect Middle => new Rect((1f - MiddleShare) * 0.5f, 0f, MiddleShare, 1f);
        /// <summary>How much of a wide screen's width that middle is: what the cards docked at either side leave clear.</summary>
        public const float MiddleShare = 0.5f;

        // One framing, held through every putt (no following, no zoom): the closest the camera used to come
        // as a ball reached the hole. The arcade sets the lens so the console fills most of the picture's
        // width (Arcade.MatFov); CameraFov is only its fallback. Upright the picture is squarer than wide,
        // so the camera stands a little higher and looks down the last of the mat to the hole.
        public override Vector3 CameraPosition => Portrait ? new Vector3(0f, 18.5f, -33f) : new Vector3(0f, 14.5f, -39f);
        public override Vector3 CameraLook => Portrait ? new Vector3(0f, -0.3f, -0.5f) : new Vector3(0f, 0.2f, 0.5f);
        public override float CameraFov => Portrait ? 30f : 31f;
        // The card stands back from the whole Puttr box (the play camera sits close over the mat), so it is mostly
        // the dark box with the lit mat inside, not a bright green field.
        public override Vector3 CardFrom => new Vector3(0f, 34f, -92f);
        public override Vector3 CardLook => new Vector3(0f, 3f, 2f);
        public override float CardFov => 30f;

        // The arcade's room is dark, lit by the console's hood alone, so a game behind it stands out.
        // Here the mat is the game: a soft light from over the Player's shoulder and a lift to the
        // room's own light, so the carpet, the ball and its line are easy to see. Both go when the
        // game is left (the lights hang from Root, which is switched off with it).
        static readonly Color RoomLight = new Color(0.3f, 0.32f, 0.34f);
        Color _roomBefore;
        bool _lit;

        public override void Build()
        {
            Light key = World.NewLight(Root, "Practice key", LightType.Directional, new Color(1f, 0.97f, 0.92f), 2.1f);
            key.transform.localRotation = Quaternion.Euler(52f, 18f, 0f);
            key.shadows = LightShadows.None; // or the console's hood throws a hard band across the mat
            Light fill = World.NewLight(Root, "Practice fill", LightType.Point, new Color(0.85f, 0.95f, 1f), 260f);
            fill.transform.localPosition = new Vector3(0f, 26f, -34f);
            fill.range = 110f;
            fill.shadows = LightShadows.None;
        }

        public override void Begin(int players)
        {
            if (_lit) return;
            _lit = true;
            _roomBefore = RenderSettings.ambientLight;
            RenderSettings.ambientLight = RoomLight;
        }

        public override void End()
        {
            if (!_lit) return;
            _lit = false;
            RenderSettings.ambientLight = _roomBefore;
        }

        // The ball has come out behind the console, or run off the end of the mat: a moment to see it, then it is put away.
        public override IEnumerator Run(Ball ball, PuttResult putt, bool holed)
        {
            yield return new WaitForSeconds(0.7f);
        }

        public override IEnumerator Finish(Ball ball, PuttResult putt, bool holed)
        {
            if (ball != null && Arcade != null) Arcade.Release(ball);
            yield break;
        }

        public override bool Over => false;
        public override void Final(out string title, out string body) { title = "Free putt"; body = ""; }
    }
}
