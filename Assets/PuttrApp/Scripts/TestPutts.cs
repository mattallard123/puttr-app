// Putting without a Puttr. There are no on-screen test buttons (Matt, 2026-11-18: they showed in every game and in
// the store captures). Testers and made-up data putt from a keyboard instead: the simulator's, a Mac's, or one paired
// with a device. In a game: Space a random putt, 1 to 6 the six recorded putts (Wide L to R Edge, PuttrSamples.Recorded),
// Z short, C firm, V long, X a hand (Arcade.HandleKeys). In Tracks: Space a putt, M made, F firm, L lip, N miss,
// S short (TracksPlayScreen). Keyboard putts are test putts and never saved (RoundSaver.KeyboardPuttNow).
//
//   TestPutts   who may test-putt, which rounds take a test putt, and the recorded paths.
//   PuttStrip   the strip every in-game panel carries above its cards: why the last putt did not count, and
//               "Puttr not connected" with a Connect button when there is no Puttr and no test putts.
//
// The paths are the app's own: PuttrSamples.Recorded holds the same mined putts as TestButtons.js, recorded against
// the hole at [320, 100, 110]. The legacy app does not change a path for the distance: the dealt distance and tee go
// to the analysis (PuttrConsole.distanceFt and tee, set when the game deals), and so they do here. The path is only
// moved and sized onto the hole the connected Puttr reports, so its entry reads the same on any green.
using System;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class TestPutts
    {
        /// <summary>The recorded putts, left to right as the Player sees the hole (keys 1 to 6 in a game).</summary>
        public static readonly string[] Labels = { "Wide L", "L Edge", "L Side", "Center", "R Side", "R Edge" };

        // Which recorded putt (PuttrSamples.Recorded, by id) each button plays.
        static readonly string[] SampleIds = { "wide-left", "left-edge", "left-side", "center", "right-side", "right-edge" };

        /// <summary>Who may test-putt at all: a tester, or anyone looking at made-up data.</summary>
        public static bool Tester(object profile) => Fixtures.On || J.Flag(profile, "isTester");

        /// <summary>Whether this Player's keyboard test putts are taken (testers and made-up data).</summary>
        public static bool Available(object profile) => Tester(profile);

        /// <summary>
        /// Whether a round takes a test putt while a real Puttr is connected. A round on test putts already (Local)
        /// and a lesson drill save nothing, so always. A tournament round or a League Night game never: they are
        /// played once and go on a board. Anything else (the coach session, a drill, a catalog game, a quest level)
        /// takes it for a tester with the buttons on, and becomes a test round (CompetitionRound.MarkTest).
        /// </summary>
        public static bool RoundTakes(bool available, CompetitionRound.Kind what, CompetitionRound.Mode how)
        {
            if (how == CompetitionRound.Mode.Local || what == CompetitionRound.Kind.Drill) return true;
            if (what == CompetitionRound.Kind.Tournament || what == CompetitionRound.Kind.League) return false;
            return available;
        }

        /// <summary>A recorded path moved and sized from the hole it was recorded against onto another.</summary>
        public static List<BallPoint> Fit(IReadOnlyList<BallPoint> path, Hole from, Hole to)
        {
            float k = from.Diameter > 0f && to.Diameter > 0f ? to.Diameter / from.Diameter : 1f;
            var fitted = new List<BallPoint>(path.Count);
            foreach (BallPoint p in path)
                fitted.Add(new BallPoint(Mathf.RoundToInt(to.X + (p.X - from.X) * k), Mathf.RoundToInt(to.Y + (p.Y - from.Y) * k), Mathf.Max(1, Mathf.RoundToInt(p.R * k))));
            return fitted;
        }

        /// <summary>The recorded putt behind a button, as camera frames against PuttrSamples.SampleHole. Null if there is none.</summary>
        public static IReadOnlyList<BallPoint> Recorded(int button)
        {
            if (button < 0 || button >= SampleIds.Length) return null;
            foreach (PuttrSamples.Sample s in PuttrSamples.Recorded) if (s.Id == SampleIds[button]) return s.Path;
            return null;
        }
    }

    /// <summary>
    /// What an in-game panel says about putts that are not arriving or not counting. One per panel, above its cards;
    /// it keeps itself up to date (a Puttr connects), and takes no room while it has nothing to show.
    /// </summary>
    public sealed class PuttStrip
    {
        public readonly VisualElement Root;
        readonly App _app;
        readonly Func<bool> _takesTestPutts;
        readonly bool _floating;
        readonly Label _notice;
        readonly VisualElement _offline;
        static PuttStrip _shown;

        /// <summary>A floating strip in a panel that has a sheet (a live match): it keeps to the picture, clear of the sheet.</summary>
        public bool BesideSheet;

        /// <summary>No Puttr is connected (and this is not made-up data, which needs none).</summary>
        public static bool Disconnected => !Fixtures.On && (Green.Peek == null || !Green.Peek.Online);

        /// <summary>True when the panel should say "Puttr not connected": none is, and the Player has no test putts to use instead.</summary>
        public static bool SaysOffline(object profile) => Disconnected && !TestPutts.Available(profile);

        /// <param name="takesTestPutts">Whether the game takes test putts now (a tournament round does not); null for always.</param>
        /// <param name="floating">Over an arcade game rather than in a sheet: only the notice, on a dark plate.</param>
        public PuttStrip(App app, Func<bool> takesTestPutts = null, bool floating = false)
        {
            _app = app;
            _takesTestPutts = takesTestPutts;
            _floating = floating;
            Root = Ui.Box(floating ? "putt-strip putt-strip-float" : "putt-strip");
            if (floating) Root.pickingMode = PickingMode.Ignore;

            _notice = Ui.Text("", "putt-notice hidden", Ui.Bold);
            _notice.pickingMode = PickingMode.Ignore;

            VisualElement dot = Ui.Box("putt-offline-dot");
            _offline = Ui.Box("putt-offline", dot, Ui.Text("Puttr not connected", "g-name grow", Ui.Bold),
                Ui.Box("btn-small", Ui.Text("Connect", "btn-text", Ui.Bold)).OnTap(() => ConnectSheet.Open(_app, Root)));

            Root.Add(_notice);
            if (!floating) Root.Add(_offline);
            Root.RegisterCallback<AttachToPanelEvent>(_ => { _shown = this; Update(); });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => { if (_shown == this) _shown = null; });
            Root.schedule.Execute(Update).Every(400);
            Update();
        }

        void Update()
        {
            bool tests = TestPutts.Available(_app.Profile) && (_takesTestPutts == null || _takesTestPutts());
            bool offline = !_floating && !tests && SaysOffline(_app.Profile);
            _offline.style.display = offline ? DisplayStyle.Flex : DisplayStyle.None;
            if (!_floating) return;
            bool wide = Screen.width > Screen.height;
            if (BesideSheet)
            {
                // Over the picture, just clear of the sheet: under it upright, to its left on a screen on its side.
                Root.style.right = wide ? Length.Percent((1f - PuttrArcade.FreePuttMode.WideShare) * 100f) : 0;
                Root.style.bottom = Length.Percent(wide ? 6f : (1f - PuttrArcade.FreePuttMode.UprightShare) * 100f + 2f);
            }
            // Clear of the arcade's own cards along the bottom: the prompt and tee line, and upright the last putt and
            // the tee band above them.
            else Root.style.bottom = Length.Percent(wide ? 17f : 30f);
        }

        /// <summary>
        /// Says, for a few seconds, why a putt did not count. The app's toast is hidden while a game has the screen,
        /// so it goes in the panel's strip; with no game panel up it falls back to the toast.
        /// </summary>
        public static void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            PuttStrip strip = _shown;
            if (strip == null || strip.Root.panel == null) { if (App.I != null) App.I.Toast(text); return; }
            Perf.Wake(4f);
            Label notice = strip._notice;
            notice.text = text;
            notice.RemoveFromClassList("hidden");
            notice.schedule.Execute(() => { if (notice.text == text) notice.AddToClassList("hidden"); }).StartingIn(3600);
        }
    }
}
