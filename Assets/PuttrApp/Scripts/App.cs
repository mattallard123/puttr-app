// Puttr 2: the Phase 0 spike of puttr-app in Unity.
//
// The app shell is UI Toolkit: sign-in, four tabs, and Home built from the live API. One game
// (On Fire, from the Puttr Arcade snapshot in Assets/PuttrArcade) is played from it and saved
// to the Player's account: see GameBridge.
//
// Nothing is in the scene. This component makes itself when the app starts, and builds the
// arcade only when a game is first played, so the shell's start time is the shell's alone.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public abstract class AppScreen
    {
        public VisualElement Root;
        /// <summary>Called every time the screen comes to the front.</summary>
        public virtual void Shown() { }
        /// <summary>Called when another screen takes the front (or a tab is shown over it).</summary>
        public virtual void Hidden() { }
    }

    public sealed class App : MonoBehaviour
    {
        public static App I { get; private set; }

        static readonly string[] TabKeys = { "home", "play", "improve", "profile" };
        static readonly string[] TabNames = { "Home", "Play", "Improve", "Profile" };

        UIDocument _document;
        PanelSettings _panel;
        Camera _camera;
        VisualElement _stage, _app, _host, _tabBar;
        Label _toast, _perf;
        readonly Dictionary<string, AppScreen> _screens = new Dictionary<string, AppScreen>();
        readonly List<VisualElement> _tabs = new List<VisualElement>();
        string _tab;
        bool _firstLayout, _perfShown;
        string _shotPath;
        float _shotAt;

        public GameBridge Games { get; private set; }
        /// <summary>The signed-in Player's profile row, once Home has read it.</summary>
        public object Profile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // The arcade's own scene, opened on its own in the editor, runs without the shell.
            if (FindFirstObjectByType<PuttrArcade.Arcade>() != null) return;
            var go = new GameObject("Puttr App");
            DontDestroyOnLoad(go);
            go.AddComponent<App>();
        }

        /// <summary>A text field has the focus (Perf keeps the app at full rate while the Player types).</summary>
        public bool Typing => _stage?.focusController?.focusedElement is VisualElement f && (f is TextField || f.GetFirstAncestorOfType<TextField>() != null);

        public static Coroutine Run(IEnumerator routine) => I != null ? I.StartCoroutine(routine) : null;

        void Awake()
        {
            // -puttrShots: the arcade writes every game's card, the rule games' own 3D worlds included.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-puttrShots") >= 0) PuttrArcade.Arcade.SceneCards = RuleViews.CardScenes();
            I = this;
            // The Puttr mark and a bar, from the first frame: the same loader the arcade uses, and the
            // same picture as the native launch screen, so the hand-over from it cannot be seen.
            PuttrArcade.Loader.AppSplash = true;
            PuttrArcade.Loader.Show();
            StartCoroutine(NativeSplashOff());
            SoundOnSilent();
            Perf.Mark("app awake");
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            // With the simulator's PUTTR_ARGS too (launch arguments do not reach an app on a simulator), so a scripted
            // remote check (-app2TvKeys) runs there.
            var tvArgs = new List<string>(Environment.GetCommandLineArgs());
            string simArgs = Environment.GetEnvironmentVariable("PUTTR_ARGS");
            if (!string.IsNullOrEmpty(simArgs)) tvArgs.AddRange(simArgs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            Tv.Detect(tvArgs);
            Portrait(true);
            ReadArguments();
            if (Tv.On && _startTab == null) _startTab = "play";   // a TV is the Play tab
            if (Tv.On)
            {
                TvPlatform.Init();
                // The first screen: Menu / Back there leaves the app (tvOS, Samsung, Android TV).
                TvPlatform.AtRoot = () => _stack.Count == 0 && !(Games?.Busy ?? false) && (_tab == null || (_screens.TryGetValue("play", out AppScreen p) && p is PlayScreen ps && ps.AtLocal));
            }
            Ui.Init();
            BuildCamera();
            BuildEventSystem();
            BuildDocument();
            Games = new GameBridge(this);
            DeepLinks.Init(this);
            StartCoroutine(Begin());
        }

        // The first frame is the loader alone. Who is signed in is read after it is drawn: on a phone that is the
        // Firebase SDK starting and a Keychain read, which used to hold the launch screen up.
        IEnumerator Begin()
        {
            yield return null;
            Auth.Restore();
            Perf.Mark("session read");
            Loading(0.3f);
            if (Auth.SignedIn || Fixtures.On)
            {
                ShowTabs("home"); // Home fills the rest of the bar as its data arrives
                if (_tab != "home") Loaded(); // started somewhere else (-app2Tab): nothing to wait for
            }
            else { ShowSignIn(); Loaded(); }
            StartCoroutine(LoadedAnyway());
            UpdateGate.Check();   // never waits: the app starts as usual while the store answers
            Analytics.Begin();
            Feedback.Begin(this);
            ConnectGate.Begin(this);   // the hole warning when a Puttr connects with a bad read
        }

        // ── The loader on first launch ───────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrSplash_Hide();
#else
        static void PuttrSplash_Hide() { }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrAudio_Play();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrAudio_Restore();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string PuttrAudio_State();
#else
        static void PuttrAudio_Play() { }
        static void PuttrAudio_Restore() { }
        static string PuttrAudio_State() => "";
#endif

        // The sound clock is watched: on a phone it can stop (the session taken by something else and never given
        // back), and then nothing is heard and whatever waits for a sound to end waits for ever (the coach's review sat
        // on its first line: Matt, 2026-11-24). Still for two seconds with the app in front, it is started again, and
        // the log says what the phone's sound was set to.
        double _soundClock;
        float _soundAt, _soundStill;
        int _soundRestarts;
        bool _soundLogged;

        // Something to hear with, always. The only listeners are on the game camera and the Tracks world, both off
        // outside a game: with none, a sound plays to its end and nothing is heard (the coach's review was silent on
        // every screen that is not a game: Matt, 2026-11-24). The app's own listens whenever no other one does.
        AudioListener _ears;
        float _earsAt;

        void Ears()
        {
            float now = Time.realtimeSinceStartup;
            if (_ears != null && now - _earsAt < 0.25f) return;
            _earsAt = now;
            if (_ears == null) { _ears = gameObject.AddComponent<AudioListener>(); _ears.enabled = false; }
            bool other = false;
            foreach (AudioListener l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l != _ears && l.isActiveAndEnabled) { other = true; break; }
            if (_ears.enabled == other) _ears.enabled = !other;
        }

        void SoundWatch()
        {
            Ears();
            float now = Time.realtimeSinceStartup;
            if (now - _soundAt < 1f) return;
            double clock = AudioSettings.dspTime;
            if (!_soundLogged && now > 3f) { _soundLogged = true; Perf.Note("sound: " + PuttrAudio_State()); }
            if (_soundAt > 0f && clock == _soundClock && Application.isFocused)
            {
                _soundStill += now - _soundAt;
                if (_soundStill >= 2f && _soundRestarts < 5)
                {
                    _soundStill = 0f;
                    _soundRestarts++;
                    Perf.Note("sound: clock stopped (" + PuttrAudio_State() + "), restart " + _soundRestarts);
                    PuttrAudio_Restore();
                    // A second stop: the sound engine itself is set up again.
                    if (_soundRestarts >= 2) AudioSettings.Reset(AudioSettings.GetConfiguration());
                }
            }
            else _soundStill = 0f;
            _soundClock = clock;
            _soundAt = now;
        }

        /// <summary>Game sounds play with the phone on silent (Plugins/iOS/PuttrAudio.m). Set again whenever the app
        /// comes back to the front and after the camera stops, as both can put the session back.</summary>
        public static void SoundOnSilent() => PuttrAudio_Play();

        // Back at the front: the store is asked again whether this build is too old (UpdateGate, as App.tsx on 'active').
        void OnApplicationFocus(bool focused) { if (focused) { SoundOnSilent(); UpdateGate.Check(); } else Perf.Flush(); }
        void OnApplicationPause(bool paused) { if (paused) Perf.Flush(); Analytics.Paused(paused); }
        void OnApplicationQuit() => Perf.Flush();

        // The launch screen's spinner (Plugins/iOS/PuttrSplash.m) turns until the engine is up; once the first frame,
        // with the loader and its bar, is drawn, it goes.
        IEnumerator NativeSplashOff()
        {
            yield return new WaitForEndOfFrame();
            PuttrSplash_Hide();
        }

        bool _loaded;

        /// <summary>How far the first screen is from ready, 0 to 1.</summary>
        public void Loading(float done)
        {
            if (_loaded) return;
            PuttrArcade.Loader.Progress(done);
            Perf.Wake(2f);
        }

        /// <summary>The first screen is ready: fill the bar and fade the loader away.</summary>
        public void Loaded()
        {
            if (_loaded) return;
            _loaded = true;
            Perf.Wake(2f);
            PuttrArcade.Loader.Hide();
            Perf.Mark("loader gone");
        }

        // A slow or dead connection must not leave the Player looking at a bar: show what there is.
        IEnumerator LoadedAnyway()
        {
            yield return new WaitForSecondsRealtime(6f);
            Loaded();
        }

        void ReadArguments()
        {
            var args = new List<string>(Environment.GetCommandLineArgs());
            // Launch arguments do not reach an app on a simulator, but its environment does.
            string extra = Environment.GetEnvironmentVariable("PUTTR_ARGS");
            if (!string.IsNullOrEmpty(extra)) args.AddRange(extra.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "-app2Fixture") Fixtures.On = true;
                // -app2Host: this run starts in host mode (the attract screen), without the switch being kept.
                if (args[i] == "-app2Host") HostMode.Force();
                // -app2HostVenue: the same, in Venue Mode (the lounge), whatever the kit says.
                if (args[i] == "-app2HostVenue") { HostMode.Force(); BrandKit.ForceVenue = true; }
                // -app2Kit file.json: the worlds dressed in a brand kit from a file (BrandKit.LoadFile), for checking the scenes without the API.
                if (args[i] == "-app2Kit" && i + 1 < args.Count && !BrandKit.LoadFile(args[i + 1])) Debug.LogWarning("Puttr 2: could not read the kit at " + args[i + 1]);
                if (args[i] == "-app2NotMember") Fixtures.NotMember = true;
                if (args[i] == "-app2XpUp") Fixtures.XpUp = true;
                if (args[i] == "-app2TestPutts") ConnectGate.UseTestPutts();
                if (args[i] == "-app2Connected") { Fixtures.Connected = true; PuttrArcade.Arcade.EmbeddedConnected = true; }
                if (args[i] == "-app2FixtureDelay" && i + 1 < args.Count) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Fixtures.Delay);
                if (args[i] == "-app2Tab" && i + 1 < args.Count) _startTab = args[i + 1];
                if (args[i] == "-app2SignIn") _forceSignIn = true;
                // -app2FreePutt: straight into Free putt once the app is up (for checking it from a script).
                if (args[i] == "-app2FreePutt") StartCoroutine(StraightIntoFreePutt());
                // -app2Open league|join|tournament|tournaments|social|find|games|challenge|round|leagueround: a page straight away (for checking it from a script).
                if (args[i] == "-app2Open" && i + 1 < args.Count) StartCoroutine(OpenPage(args[i + 1]));
                // -app2Scroll 900: scroll the screen that far down three seconds in (to photograph what is below the fold).
                if (args[i] == "-app2Scroll" && i + 1 < args.Count && float.TryParse(args[i + 1], out float down)) StartCoroutine(DevScroll(down));
                // -app2ExportPutts file.json: once signed in, your own putts of the last year (GET /profile/analysis/putts/365)
                // are written to the file, for the parity check (Tools/parity). Nobody else's: the API reads the signed-in Player's.
                if (args[i] == "-app2ExportPutts" && i + 1 < args.Count) StartCoroutine(ExportPutts(args[i + 1]));
                if (args[i] == "-app2Phone") _forcePhone = true; // lay a wide desktop window out as a phone on its side
                if (args[i] == "-app2TracksAuto") TracksPlayScreen.AutoPlay = true;
                // -app2AutoPutt 6: a random test putt every that many seconds in whatever game is being played (rule games
                // take no demo putts; this walks them from a script, as pressing Space would).
                if (args[i] == "-app2AutoPutt" && i + 1 < args.Count && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float every) && !_autoPutting) { _autoPutting = true; StartCoroutine(AutoPutt(Mathf.Max(1f, every))); }
                if (args[i] == "-app2Tablet") _forceTablet = true; // lay a desktop window out as a tablet, a unit a pixel
                // -app2Shot file.png: a screenshot five seconds in, then quit (for checking layout from a script).
                if (args[i] == "-app2Shot" && i + 1 < args.Count) { _shotPath = args[i + 1]; if (_shotAt <= 0f) _shotAt = 5f; }
                if (args[i] == "-app2ShotAt" && i + 1 < args.Count) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _shotAt);
                // -app2Click x,y,seconds: a click at that point (from the top left, in pixels) that long after launch.
                // It goes in through the Input System, so it tests the same path a finger takes.
                if (args[i] == "-app2Click" && i + 1 < args.Count)
                {
                    string[] parts = args[i + 1].Split(',');
                    if (parts.Length == 3 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y) && float.TryParse(parts[2], out float at)) StartCoroutine(DevClick(x, y, at));
                }
            }
        }

        string _startTab;
        bool _forceSignIn, _forcePhone;

        // The arguments are read more than once in a run: one auto-putter only, or its putts land on each other.
        bool _autoPutting;
        IEnumerator AutoPutt(float every)
        {
            yield return new WaitForSecondsRealtime(8f);
            while (true) { Games?.TestPutt(); yield return new WaitForSecondsRealtime(every); }
        }

        /// <summary>-app2Open tracksplay: straight onto the first tee (sample data or the live course).</summary>
        IEnumerator TracksDemo()
        {
            object doc = null;
            yield return Api.Get("/tracks/course/st-andys-old", d => doc = d, null);
            if (doc == null) yield break;
            TracksCourse course = TracksCourse.Read(doc);
            if (string.IsNullOrEmpty(course.Slug)) course.Slug = "st-andys-old";
            Push(new TracksPlayScreen(this, course, "front", "pro"));
        }

        IEnumerator DevScroll(float down)
        {
            yield return new WaitForSecondsRealtime(3f);
            ScrollView scroll = _host.Q<ScrollView>();
            if (scroll != null) scroll.scrollOffset = new Vector2(0f, down);
            Perf.Wake(3f);
        }

        IEnumerator OpenPage(string page)
        {
            yield return new WaitForSecondsRealtime(1.2f);
            object tournament = null;
            if (page == "tournament" || page == "round") Compete.Load(list => tournament = list.Count > 0 ? list[0] : null);
            object challenges = null;
            if (page == "challenge") StartCoroutine(Api.Get("/challenges/userChallenges", data => challenges = data));
            yield return new WaitForSecondsRealtime(0.8f);
            // play:TYPE (setup) or start:TYPE (straight in): any game by its gameType, for checking it from a script.
            // playquests, playquests:3 (that world's map), playquestsall:3 (every stage cleared, in memory only): the
            // Play tab's Quests, for checking the Greenlands maps from a script.
            if (page.StartsWith("playquests"))
            {
                int colon = page.IndexOf(':');
                if (page.StartsWith("playquestsall")) foreach (GreenStage each in Greenlands.All) GreenProgress.Record(each, 2, 0, false);
                if (colon > 0 && int.TryParse(page.Substring(colon + 1), out int world)) GreenlandsScreen.OpenWorld = world;
                PlayScreen.OpenAt = "Quests"; _screens.Remove("play"); ShowTabs("play");
                yield break;
            }
            // stage:1-4: a Greenlands stage straight in, whether or not it is open yet.
            if (page.StartsWith("stage:")) { GreenStage stage = Greenlands.ByCode(page.Substring(6).ToUpperInvariant()); if (stage != null) { if (!Fixtures.On) yield return GreenProgress.Load(Greenlands.WorldOf(stage), null);   // the API's level ids, so the stars are saved as they are from the map
                    Games.PlayRule(Greenlands.Row, new StageGame(stage)); } yield break; }
            if (page.StartsWith("play:") || page.StartsWith("start:") || page.StartsWith("vs:"))
            {
                string type = page.Substring(page.IndexOf(':') + 1).ToUpperInvariant();
                // start:TYPE:n: n Players (1 to 4), for an arcade game that needs more than one (Tug of War's two teams).
                int players = 1;
                int colon = type.IndexOf(':');
                if (colon > 0) { int.TryParse(type.Substring(colon + 1), out players); type = type.Substring(0, colon); players = Mathf.Clamp(players, 1, 4); }
                var row = new Dictionary<string, object> { { "gameType", type }, { "title", type } };
                // Fixture runs: a catalog-like row, with a leaderboard except Beer Pong (as the live catalog).
                if (Fixtures.On) { row["id"] = "9001"; row["hasLeaderboard"] = type == "BEERPONG" || type == "OPEN" ? 0.0 : 1.0; row["description"] = "A fixture game for checking the layout."; }
                // Its real name where one is known (the fixture catalog, the arcade and sensor extras), so a capture's
                // header reads "On Fire", not "ARCADESTREAK".
                var known = new List<object>();
                if (Fixtures.On && Fixtures.For("/games/ALL") is List<object> catalog) known.AddRange(catalog);
                known.AddRange(ArcadeExtras.All);
                known.AddRange(SensorGames.All);
                object named = known.Find(g => J.Str(g, "gameType").ToUpperInvariant() == type);
                if (named != null) row["title"] = J.Str(named, "title", type);
                else foreach (var pair in GameBridge.ArcadeGames) if (pair.Value == type) { row["title"] = pair.Key; break; }
                RuleGame rules = RuleCatalog.Make(row);
                if (rules != null && page.StartsWith("start:")) { Games.PlayRule(row, rules); yield break; }
                // An arcade game straight in, with made-up Players (store captures run these with -puttrDemo).
                if (rules == null && page.StartsWith("start:") && GameBridge.ArcadeGames.ContainsValue(type))
                {
                    PuttrArcade.Arcade.EmbeddedPuttr = false;
                    var names = new List<string> { "Sam", "Ava", "Marcus", "Priya" }.GetRange(0, players);
                    Games.PlayWith(J.Str(row, "title", type), names);
                    yield break;
                }
                // vs:TYPE for an arcade game that takes Puttr as the other Player (Dead Weight).
                if (rules == null && page.StartsWith("vs:") && GameBridge.ArcadeGames.ContainsValue(type)) { PuttrArcade.Arcade.EmbeddedPuttr = true; Games.PlayWith(J.Str(row, "title", type), new List<string> { "Sam" }); yield break; }
                // vs:TYPE: you against Puttr, Puttr putting first (checks Puttr's turns).
                if (rules != null && page.StartsWith("vs:")) { rules.Names = new List<string> { "Sam", "Puttr" }; rules.Bot = 1; rules.First = 1; Games.PlayRule(row, rules); yield break; }
                if (!Games.Open(row)) Toast("Nothing plays " + type + " yet.");
                yield break;
            }
            switch (page)
            {
                case "league": Push(new LeagueScreen(this)); break;
                case "join": Push(new LeagueJoinScreen(this)); break;
                case "tournaments": Push(new TournamentsScreen(this)); break;
                case "tournament": if (tournament != null) Push(new TournamentScreen(this, tournament)); break;
                case "social": Push(new SocialScreen(this, false)); break;
                case "find": Push(new FindPlayersScreen(this)); break;
                case "games": Push(new GamesScreen(this)); break;
                case "settings": Push(new SettingsScreen(this)); break;
                case "levels": Push(new LevelsScreen(this)); break;      // XP: the Levels sheet
                case "levelup": Push(new LevelUpScreen(this, 15)); break;   // an ordinary level
                case "rankup": Push(new LevelUpScreen(this, 20)); break;    // a new rank (Scratch)
                case "host": case "hostevent": Host.Open(this, BrandKit.Event); break;   // the Settings rows
                case "hostvenue": Host.Open(this, BrandKit.Venue); break;
                case "brandkit": Push(new BrandKitScreen(this)); break;
                case "brandgames": Push(new BrandGamesScreen(this)); break;
                case "hostpicker": case "hostattract": ShowHost(false); break;   // with -app2Host: the host's home
                case "staff": StaffPad.Ask(this); break;
                case "feedback": Feedback.Open(this, "post_game"); break;
                case "member": ProfileScreen.Open(this, "a"); break;
                case "onboarding": Push(new OnboardingScreen(this)); break;
                case "creator": Push(new CreatorScreen(this)); break;
                case "newgame": { var c = new CreatorScreen(this); Push(c); CreatorScreen.NewGame(this, c.Root); } break;
                case "editgame": Push(new EditGameScreen(this, J.Parse("{\"id\":5001,\"title\":\"Lag ladder\",\"description\":\"Long putts, all three tees.\",\"gameType\":\"STANDARD\",\"puttData\":\"[{\\\"distance\\\":9,\\\"tee\\\":1},{\\\"distance\\\":10,\\\"tee\\\":2},{\\\"distance\\\":11,\\\"tee\\\":3}]\"}"))); break;
                case "coachchat": Push(new CoachChatScreen(this, null, false)); break;
                case "caddie": Push(new CaddieScreen(this)); break;
                case "coach": Push(new CoachScreen(this)); break;
                case "coachlanding": Push(new CoachLandingScreen(this)); break;
                case "coachapply": Push(new CoachApplyScreen(this, null)); break;
                case "coachstatus": Push(new CoachApplicationStatusScreen(this)); break;
                case "academy": Push(new CoachAcademyScreen(this)); break;
                case "findcoach": Push(new FindCoachScreen(this)); break;
                case "lessoncoach": Push(new LessonCoachScreen(this, "c1")); break;
                case "mylessons": Push(new MyLessonsScreen(this)); break;
                case "bookedlesson": Push(new BookedLessonScreen(this, "701")); break;
                case "plan": Push(new PlanDetailScreen(this, "301")); break;
                case "coachvideos": Push(new CoachVideosScreen(this)); break;
                case "inbox": Push(new LessonInboxScreen(this)); break;
                case "roster": Push(new PlayerRosterScreen(this)); break;
                case "monitor": Push(new PlayerMonitorScreen(this, "301")); break;
                case "assignplan": Push(new AssignPlanScreen(this, "701", null, "Ava Chen", null)); break;
                case "lessonsetup": Push(new CoachLessonSetupScreen(this)); break;
                case "playerreview": Push(new CoachingDashboardScreen(this)); break;
                case "videolibrary": Push(new CoachingVideosScreen(this)); break;
                case "firstrun": Push(new CoachFirstRunScreen(this, null)); break;
                case "junior": Push(new JuniorLandingScreen(this)); break;
                case "juniorapply": Push(new JuniorApplyScreen(this)); break;
                case "juniorwait": Push(new JuniorParentWaitlistScreen(this, null)); break;
                case "createtournament": Push(new CreateTournamentScreen(this)); break;
                case "camerasetup": Push(new CameraSetupScreen(this)); break;
                case "connect": ConnectSheet.Open(this, _stage); break;
                case "gate": ConnectGate.Preview(this); break;
                case "beerpong": Games.PlayWith("Beer Pong", new List<string> { "Sam", "Ava" }); break;
                case "beerpongsolo": Games.PlayWith("Beer Pong", new List<string> { "Sam" }); break;
                case "tracks": Push(new TracksCoursesScreen(this)); break;
                case "tracksplay": App.Run(TracksDemo()); break;
                case "onfire": Push(new GameScreen(this, J.Parse("{\"id\":101,\"title\":\"On Fire\",\"gameType\":\"ARCADESTREAK\",\"description\":\"How many putts can you make in a row?\",\"multiPlayer\":0}"))); break;
                case "leaderboard": Push(new GameLeaderboardScreen(this, "102", "Center Cut")); break;
                case "ghost": Push(new GhostOpponentScreen(this, J.Parse("{\"id\":102,\"title\":\"Center Cut\",\"gameType\":\"CENTERCUT\",\"isOnline\":1}"))); break;
                case "clubpay": Push(new ClubScreen(this, null)); break;
                case "admin": Push(new AdminScreen(this)); break;
                case "adminuser": Push(new AdminUserScreen(this, "a")); break;
                case "adminapp": Push(new AdminApplicationScreen(this, "31")); break;
                case "admincert": Push(new AdminCertGradeScreen(this, J.Parse("{\"id\":801,\"coachName\":\"Tom Ellery\",\"deliveredAt\":\"2026-11-15T00:00:00.000Z\",\"lessonVideoLength\":312}"))); break;
                case "adminjunior": Push(new AdminJuniorScreen(this, "41")); break;
                case "quests": Push(new GreenlandsScreen(this)); break;

                case "questlevels": Push(new QuestLevelsScreen(this, J.Parse("{\"id\":1,\"title\":\"First Steps\",\"description\":\"Short putts to build confidence.\"}"))); break;
                case "learn": Push(new LearnScreen(this, "BASICS")); break;
                case "lesson": Library.Load(() => Push(new LessonScreen(this, Library.In("BASICS")[0]))); break;
                case "versus": Push(new VersusInboxScreen(this)); break;
                case "versuspick": Push(new VersusPickerScreen(this)); break;
                case "versusresult": Push(new VersusResultsScreen(this, "499")); break;
                case "live": Push(new LiveLobbyScreen(this)); break;
                case "livesetup": Push(new LiveSetupScreen(this)); break;
                case "profiles": Push(new ProfilesScreen(this)); break;
                case "addplayer": Push(new AddPlayerScreen(this)); break;
                case "character": Push(new CharacterScreen(this, J.Parse("{\"userId\":\"k1\",\"name\":\"Mia\",\"avatar\":{\"animal\":\"fox\",\"color\":4}}"), true)); break;
                case "gameview": Push(new GameViewScreen(this, "9001")); break;
                case "challenges": Push(new ChallengesScreen(this)); break;
                case "achievements": Push(new AchievementsScreen(this)); break;
                case "putters": Push(new PuttersScreen(this)); break;
                case "editputter": Push(new EditPutterScreen(this, null)); break;
                case "editprofile": Push(new EditProfileScreen(this)); break;
                case "membership": MembershipScreen.Open(this); break;
                case "feed": Push(new FeedScreen(this, false)); break;
                case "team": Push(new LeagueTeamScreen(this, League.TeamOf("mavericks"), "s4-2026", 2)); break;
                case "seasons": Push(new LeagueSeasonsScreen(this)); break;
                case "finale": Push(new LeagueFinaleScreen(this, "s3-2026")); break;
                case "notifications": Push(new NotificationsScreen(this)); break;
                case "notificationprefs": Push(new NotificationPrefsScreen(this)); break;
                case "level": Push(new LevelScreen(this)); break;
                case "wifi": Push(new WifiScreen(this)); break;
                case "vertex": Push(new VertexScreen(this)); break;
                case "lab": Push(new LabIntroScreen(this)); break;
                // labguide: the intro with its setup video already playing (for checking the in-app player from a script).
                case "labguide": { var intro = new LabIntroScreen(this); Push(intro); intro.Root.schedule.Execute(intro.Guide).StartingIn(800); break; }
                case "coachedreview": StartCoroutine(Api.Get("/lab/sessions", data => { if (J.List(data).Count > 0) Push(new CoachedReviewScreen(this, J.List(data)[0], null)); })); break;
                case "labsession": StartCoroutine(Api.Get("/lab/sessions", data => { if (J.List(data).Count > 0) Push(new LabSessionScreen(this, J.List(data)[0])); })); break;
                case "analysis": Club.Require(this, "Full analysis", () => Push(new AnalysisScreen(this))); break;
                case "club": Push(new ClubScreen(this, null)); break;
                case "challenge": if (J.List(challenges).Count > 0) Push(new ChallengeScreen(this, J.List(challenges)[0])); break;
                case "round":
                    if (tournament != null)
                        StartCoroutine(Api.Get("/tournaments/details/" + J.Str(tournament, "id"), data => Games.PlayRound(tournament, J.List(J.Get(J.First(data), "rounds"))[0], 1)));
                    break;
                case "leagueround":
                    League.Load((team, season, mine) => { if (season != null) Games.PlayLeague(season, League.CurrentWeek(season, System.DateTime.Now), team); });
                    break;
            }
        }

        IEnumerator ExportPutts(string file)
        {
            while (!Auth.SignedIn) yield return new WaitForSecondsRealtime(1f); // sign in on screen first
            Toast("Exporting your putts...");
            string text = null;
            yield return Auth.Token(t => text = t);
            long status = 0;
            yield return Api.Send("GET", Config.ApiUrl + "/profile/analysis/putts/365", null, text, (s, body) => { status = s; text = body; });
            if (status != 200) { Toast("Export failed (" + status + ")."); yield break; }
            System.IO.File.WriteAllText(file, text);
            Toast(J.List(J.Parse(text)).Count + " putts written. You can close the app.");
            Perf.Note("export: " + file);
        }

        IEnumerator StraightIntoFreePutt()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            Games.PlayPractice(new PracticeSession(3) { Goal = PracticeGoal.Total, Target = 10 });
        }

        static IEnumerator DevClick(float x, float y, float at)
        {
            yield return new WaitForSecondsRealtime(at);
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) { Debug.Log("Puttr 2 dev click: no mouse"); yield break; }
            var point = new Vector2(x, Screen.height - y);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null; yield return null;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, true));
            yield return null; yield return null;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, false));
            Debug.Log("Puttr 2 dev click: " + x + "," + y);
        }

        // Everything follows the device between portrait and landscape, the app's screens as well as the
        // games (Matt, 2026-11-16: no portrait lock). Upside down only on a tablet, as iPadOS allows.
        static void Portrait(bool portrait)
        {
            if (!Application.isMobilePlatform || Tv.On) return;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortraitUpsideDown = Tablet;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }

        void BuildCamera()
        {
            // Something has to clear the screen behind the UI. It draws nothing else.
            var go = new GameObject("Shell Camera");
            go.transform.SetParent(transform, false);
            _camera = go.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Ui.Ink;
            _camera.cullingMask = 0;
            _camera.depth = -10f; // under a game's camera: it fills whatever part of the screen the game does not draw
        }

        void BuildEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.transform.SetParent(transform, false);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        void BuildDocument()
        {
            _panel = Instantiate(Resources.Load<PanelSettings>("PuttrApp/Panel"));
            _panel.sortingOrder = -1; // under a game's own interface (its pause button, its cards), over the picture
            var go = new GameObject("UI");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            _document = go.AddComponent<UIDocument>();
            _document.panelSettings = _panel;
            go.SetActive(true);

            _stage = _document.rootVisualElement;
            _stage.styleSheets.Add(Resources.Load<StyleSheet>("PuttrApp/App"));
            _stage.AddToClassList("stage");
            _stage.Face(Ui.Medium);

            _host = Ui.Box("screen-host");
            _tabBar = Ui.Box("tabs");
            for (int i = 0; i < TabKeys.Length; i++)
            {
                string key = TabKeys[i];
                VisualElement tab = Ui.Box("tab", new TabIcon(key), Ui.Text(TabNames[i], "tab-label")).OnTap(() => ShowTabs(key));
                if (key == "improve") _improveLabel = tab.Q<Label>();
                _tabs.Add(tab);
                _tabBar.Add(tab);
            }
            _toast = Ui.Text("", "toast hidden");
            _perf = Ui.Text("", "perf hidden", Ui.Regular);
            _perf.pickingMode = PickingMode.Ignore;
            _app = Ui.Box("app", _host, _tabBar, _toast, _perf);
            _stage.Add(_app);
            _stage.RegisterCallback<GeometryChangedEvent>(_ => OnLayout());
            Tv.Attach(_stage);
            if (Tv.On) _tabBar.style.display = DisplayStyle.None;   // one tab on a TV: Play
        }

        void OnLayout()
        {
            if (!_firstLayout) { _firstLayout = true; Perf.Mark("first screen laid out"); }
            // Keep clear of the notch and the home indicator: the safe area is in screen pixels, the panel in its own units.
            float width = _stage.resolvedStyle.width;
            if (width <= 0f || Screen.width <= 0) return;
            float k = width / Screen.width;
            Rect safe = Screen.safeArea;
            _app.style.paddingTop = (Screen.height - safe.yMax) * k;
            _app.style.paddingBottom = safe.yMin * k;
        }

        void Update()
        {
            SoundWatch();
            Perf.Tick();
            Analytics.Tick();
            Layout();
            Tv.Tick();
            if (_perfShown) _perf.text = Perf.Summary();
            if (_shotPath != null && Time.realtimeSinceStartup > _shotAt) StartCoroutine(Shoot());
            // Host mode: with nobody there (no touch, ShowHost's listener; no putt, GameBridge.OnPutt) and no game on, the attract screen comes back.
            if (HostMode.On)
            {
                if (_hostShown && HostMode.Idle && !(Front is HostPickerScreen home && home.AtRest) && (Games == null || !Games.Busy)) ShowHost(true);
            }
        }

        /// <summary>A tablet: the short side is 3.3 inches or more (or -app2Tablet on a desktop window).</summary>
        public static bool Tablet => _forceTablet || (Application.isMobilePlatform && Screen.dpi > 0f && Mathf.Min(Screen.width, Screen.height) / Screen.dpi >= 3.3f);
        static bool _forceTablet;

        /// <summary>
        /// How big a unit is. A phone fills its width upright (390 units) and its height on its side (390), so a
        /// unit stays about a point. A tablet is laid out at its own size in points (an iPad is 1024 or more
        /// across, not a phone blown up), and the screens use the room: a wider column, two columns of cards.
        /// </summary>
        void Layout()
        {
            if (_panel == null) return;
            if (Tv.On)
            {
                // Ten feet away: laid out at 1280 x 720 units, whatever the TV's pixels.
                if (_panel.scaleMode != PanelScaleMode.ScaleWithScreenSize) _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                if (_panel.referenceResolution != new Vector2Int(1280, 720)) _panel.referenceResolution = new Vector2Int(1280, 720);
                if (!Mathf.Approximately(_panel.match, 1f)) _panel.match = 1f;
                if (_app != null) { _app.EnableInClassList("tablet", true); _app.EnableInClassList("wide", true); }
                return;
            }
            bool wide = Screen.width > Screen.height;
            if (Tablet)
            {
                // Points: an iPad is 2 pixels a point (1x older ones); Android is dpi / 160.
                float scale = _forceTablet ? 1f : Application.platform == RuntimePlatform.IPhonePlayer ? Mathf.Max(1f, Mathf.Round(Screen.dpi / 150f)) : Mathf.Max(1f, Screen.dpi / 160f);
                if (_panel.scaleMode != PanelScaleMode.ConstantPixelSize) _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                if (!Mathf.Approximately(_panel.scale, scale)) _panel.scale = scale;
            }
            else
            {
                if (_panel.scaleMode != PanelScaleMode.ScaleWithScreenSize) _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                bool phone = _forcePhone || Application.isMobilePlatform;
                Vector2Int reference = wide && phone ? new Vector2Int(844, 390) : new Vector2Int(390, 844);
                float match = wide ? 1f : 0f;
                if (_panel.referenceResolution != reference) _panel.referenceResolution = reference;
                if (!Mathf.Approximately(_panel.match, match)) _panel.match = match;
            }
            // Room for two columns of cards (a tablet, or a phone on its side), and the app itself unboxed from the phone column.
            float units = _stage != null ? _stage.resolvedStyle.width : 0f;
            bool roomy = Tablet || (Application.isMobilePlatform && wide) || _forcePhone && wide;
            if (_app != null)
            {
                _app.EnableInClassList("tablet", roomy);
                _app.EnableInClassList("wide", roomy && units >= 700f);
            }
        }

        IEnumerator Shoot()
        {
            string path = _shotPath;
            _shotPath = null;
            Perf.Wake(3f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        // ── Which screen is up ───────────────────────────────────────────────

        public void ShowSignIn()
        {
            _tab = null;
            _stack.Clear();
            _tabBar.AddToClassList("hidden");
            Swap(Tv.On ? (AppScreen)new TvSignInScreen(this) : new SignInScreen(this));
        }

        // Screens opened over a tab (a game's page, results). Back goes to the one underneath.
        readonly List<AppScreen> _stack = new List<AppScreen>();
        /// <summary>The screen on top of the stack, or null on a tab.</summary>
        public AppScreen Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        Label _improveLabel;

        /// <summary>Once the profile is read: a coach (isCoach or coachApproved) gets the Coach tab in Improve's place, with its badge.</summary>
        public void ProfileRead()
        {
            bool coach = Coach.Is(Profile);
            if (_improveLabel != null) _improveLabel.text = coach ? "Coach" : "Improve";
            if (_screens.TryGetValue("improve", out AppScreen improve) && (improve is CoachScreen) != coach) _screens.Remove("improve");
            if (!coach) return;
            Coach.RefreshPending(() => { if (_improveLabel != null) _improveLabel.text = Coach.Pending > 0 ? "Coach (" + (Coach.Pending > 9 ? "9+" : Coach.Pending.ToString()) + ")" : "Coach"; });
        }

        /// <summary>The pushed screen at the front, or null when a tab is showing.</summary>
        public AppScreen Front => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public void Push(AppScreen screen)
        {
            _stack.Add(screen);
            _tabBar.AddToClassList("hidden");
            Swap(screen);
        }

        public void Pop()
        {
            if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
            if (_stack.Count > 0) Swap(_stack[_stack.Count - 1]); else ShowTabs(_tab ?? "home");
        }

        bool _hostShown, _hostListens;

        /// <summary>Host mode's home (Host.cs): the game picker, with the attract screen over it at the start and
        /// whenever nobody has been there for a while. The tab bar is the Player's app: it stays hidden.</summary>
        HostPickerScreen _hostHome;

        /// <summary>Host mode's one screen (Host.cs HostPickerScreen): the arena, the brand and the games. The tab bar
        /// is the Player's app: it stays hidden. idle: nobody has been there for a while, so whoever was signed in at a
        /// venue is signed out and the row of games goes back to the start.</summary>
        public void ShowHost(bool idle)
        {
            _stack.Clear();
            _tabBar.AddToClassList("hidden");
            HostMode.Touch();
            _hostShown = true;
            // Any touch anywhere counts as someone being there (the project reads input through UI Toolkit, not UnityEngine.Input).
            if (!_hostListens && _document != null && _document.rootVisualElement != null)
            {
                _hostListens = true;
                _document.rootVisualElement.RegisterCallback<PointerDownEvent>(_ => { if (HostMode.On) HostMode.Touch(); }, TrickleDown.TrickleDown);
            }
            if (idle) HostMode.EndSession();
            if (_hostHome == null) _hostHome = new HostPickerScreen(this);
            if (idle) _hostHome.Rest();
            _stack.Add(_hostHome);
            Swap(_hostHome);
        }

        public void ShowTabs(string tab)
        {
            // In host mode there are no tabs: everything that would go back to one goes to the host's games.
            if (HostMode.On && !_forceSignIn) { ShowHost(!_hostShown); return; }
            _hostShown = false;
            _hostHome = null;   // left host mode: the next start builds its home afresh
            _stack.Clear();
            if (_startTab != null) { tab = _startTab; _startTab = null; }
            if (Tv.On) tab = "play";   // the only tab on a TV
            if (_forceSignIn) { _forceSignIn = false; ShowSignIn(); return; }
            _tab = tab;
            _tabBar.RemoveFromClassList("hidden");
            // A TV has no Home to read the profile: read it here (the Play tab's account button and the Players need it).
            // As it was last time at once (so the Play tab is built for this profile from the start), then as it is now.
            if (Tv.On && Profile == null) Run(Api.Kept("/profile/", data => { Profile = J.First(data) ?? Profile; TvProfileRead?.Invoke(); }));
            // Once signed in: an enabled Vertex sensor is looked for, and the inbox read. Not on every tab tap (each
            // used to restart the sensor scan and fetch every message): the sensor once per sign-in, the inbox at most
            // once a minute here (a push and coming back to the front read it too).
            if (_vertexFor != Auth.Uid) { _vertexFor = Auth.Uid; Vertex.Boot(); }
            if (_inboxFor != Auth.Uid || Time.realtimeSinceStartup - _inboxAt > 60f) { _inboxFor = Auth.Uid; _inboxAt = Time.realtimeSinceStartup; Inbox.Load(); }
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool on = TabKeys[i] == tab;
                _tabs[i].EnableInClassList("tab-on", on);
                _tabs[i].Q<TabIcon>().SetTint(on ? Ui.Bright : Ui.Faint);
            }
            if (!_screens.TryGetValue(tab, out AppScreen screen))
            {
                switch (tab)
                {
                    case "home": screen = new HomeScreen(this); break;
                    case "play": screen = new PlayScreen(this); break;
                    case "improve": screen = Coach.Is(Profile) ? (AppScreen)new CoachScreen(this) : new ImproveScreen(this); break;
                    default: screen = new ProfileScreen(this); break;
                }
                _screens[tab] = screen;
            }
            Swap(screen);
        }

        AppScreen _front;
        string _vertexFor, _inboxFor;
        float _inboxAt;

        /// <summary>
        /// A page's title and back button stay put while the page scrolls: a screen whose scroll view starts with its
        /// header (Ui.Header, as Page.Make and most pushed screens build it) has the header lifted out above the scroll
        /// view, once. Screens without one are left as they are.
        /// </summary>
        public static void PinHeader(AppScreen screen)
        {
            if (!(screen.Root is ScrollView scroll) || scroll.contentContainer.childCount == 0) return;
            VisualElement header = scroll.contentContainer[0];
            if (!header.ClassListContains("top")) return;
            header.RemoveFromHierarchy();
            var bar = new VisualElement();
            bar.AddToClassList("page-viewport");   // centred to the page column on a wide screen, as the scroll's content is
            bar.AddToClassList("pinned-top");
            // A header that is only a back button has no line under it (Matt, 2026-11-24): the page's own title follows.
            Label title = header.Q<Label>(className: "top-title");
            if (title == null || string.IsNullOrEmpty(title.text)) bar.AddToClassList("pinned-top-bare");
            header.AddToClassList("page-column");
            bar.Add(header);
            var page = new VisualElement();
            page.AddToClassList("screen");
            page.AddToClassList("pinned-page");
            page.Add(bar);
            page.Add(scroll);
            screen.Root = page;
        }

        void Swap(AppScreen screen)
        {
            Tv.Swapped(_front, screen);   // a TV: the focus is remembered on the way in, and put back on the way out
            if (_front != null && _front != screen) _front.Hidden();
            _front = screen;
            _host.Clear();
            PinHeader(screen);
            _host.Add(screen.Root);
            screen.Shown();
            // A tab goes by its own name (Home, Play, Improve or Coach, Profile); a screen over it by its class.
            Analytics.Viewed(_stack.Count == 0 && _tab != null ? TabNames[Array.IndexOf(TabKeys, _tab)] + (screen is CoachScreen ? " (Coach)" : "") : Analytics.ScreenName(screen.GetType()));
            Spinner.Cover(_host, screen);   // never a blank screen while its data loads
            Perf.Wake();
        }

        /// <summary>Another profile on this account is now signed in (Profiles): start the app again as them.</summary>
        /// <summary>The panel's root: where dialogs go when no screen is to hand.</summary>
        public VisualElement Toasts => _stage;

        /// <summary>Where sheets and dialogs go: the stage, which carries the app's stylesheet. The panel's own root is above it and unstyled.</summary>
        public static VisualElement Layer(VisualElement fallback) => I != null && I._stage != null ? I._stage : fallback.panel != null ? fallback.panel.visualTree : fallback;

        public void SwitchedPlayer()
        {
            Profile = null;
            if (_screens.TryGetValue("home", out AppScreen was) && was is HomeScreen old) old.Forget();
            _screens.Clear();
            Inbox.Messages = new System.Collections.Generic.List<object>();
            ShowTabs("home");
            App.Run(Api.Get("/profile/", data => Profile = J.First(data)));
        }

        public void SignedOut()
        {
            // This phone stops getting this Player's pushes; IAPHUB forgets the user (the app never did).
            PuttrApp.Push.Unregister();
            Iap.Logout();
            Auth.SignOut();
            BrandKit.Forget();   // the worlds go back to Puttr; the kit stays with its owner
            Profile = null;
            if (_screens.TryGetValue("home", out AppScreen was) && was is HomeScreen old) old.Forget();
            _screens.Clear();
            ShowSignIn();
        }

        public void Toast(string text)
        {
            _toast.text = text;
            _toast.RemoveFromClassList("hidden");
            Perf.Wake(3f);
            _toast.schedule.Execute(() => { if (_toast.text == text) _toast.AddToClassList("hidden"); }).StartingIn(2800);
        }

        public void TogglePerf()
        {
            _perfShown = !_perfShown;
            _perf.EnableInClassList("hidden", !_perfShown);
        }

        // ── Handing the screen to a game and taking it back ──────────────────

        VisualElement _overlay;

        /// <summary>Hands the screen to a game. With a panel, the game shares it: the game draws its
        /// part (GameMode.Viewport) and the panel, one of the app's own, sits over the rest.</summary>
        public void EnterGame(VisualElement panel = null)
        {
            Perf.InGame = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep; // nobody touches the screen while putting
            _overlay = panel;
            if (panel == null) _stage.style.display = DisplayStyle.None;
            else
            {
                _app.style.display = DisplayStyle.None;
                _stage.AddToClassList("stage-clear");
                _stage.Add(panel);
            }
            Portrait(false);
        }

        /// <summary>Screen pixels to panel units, and the safe area's insets in panel units (left, bottom, right, top).</summary>
        public Vector4 SafeInsets()
        {
            float width = _stage.resolvedStyle.width;
            if (width <= 0f || Screen.width <= 0) return Vector4.zero;
            float k = width / Screen.width;
            Rect safe = Screen.safeArea;
            return new Vector4(safe.xMin, safe.yMin, Screen.width - safe.xMax, Screen.height - safe.yMax) * k;
        }

        /// <summary>One of the app's own putting screens: awake, and free to turn, but still the app's UI.</summary>
        public void EnterPractice()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Portrait(false);
        }

        public void LeavePractice()
        {
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            Portrait(true);
            HomeStale(); // the streak and Jump back in have changed
        }

        /// <summary>Exit from a game or its final screen: everything pushed is closed, back to the main screen of the tab it came from.</summary>
        public void ExitToTab() => ShowTabs(_tab ?? "home");

        /// <summary>A TV read the profile (the Play tab's account button redraws).</summary>
        public Action TvProfileRead;

        /// <summary>Back on a TV remote: out of a game, off the screen on top, else to the Play tab's first tab.</summary>
        public void TvBack()
        {
            if (Games != null && Games.TvBack()) return;
            if (_stack.Count > 0) { Pop(); return; }
            if (_screens.TryGetValue("play", out AppScreen play) && play is PlayScreen p && !p.AtLocal) { p.TvBack(); return; }
            TvPlatform.Exit();   // Back on the first screen leaves the app
        }

        /// <summary>The Tizen wrapper's Back key (keyCode 10009), sent to this object by name.</summary>
        public void TvBackKey(string _) => Tv.Back();

        /// <summary>The game page's backdrop: the stage goes see-through, so the arcade's set shows behind the app.</summary>
        public void SetBackdrop(bool on) => _stage.EnableInClassList("stage-clear", on);

        /// <summary>A challenge was joined or left: Home and Profile read their challenge cards again when next shown.</summary>
        public void ChallengesChanged()
        {
            HomeStale();
            if (_screens.TryGetValue("profile", out AppScreen profile) && profile is ProfileScreen p) p.Stale();
        }

        void HomeStale() { if (_screens.TryGetValue("home", out AppScreen home) && home is HomeScreen h) h.Stale = true; }

        public void LeaveGame()
        {
            Perf.InGame = false;
            Portrait(true);
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            if (_overlay != null) { _overlay.RemoveFromHierarchy(); _overlay = null; }
            _stage.RemoveFromClassList("stage-clear");
            _app.style.display = DisplayStyle.Flex;
            _stage.style.display = DisplayStyle.Flex;
            // What was just played changes the streak and Jump back in: Home refreshes in place, keeping what it shows.
            HomeStale();
            if (_stack.Count > 0) Swap(_stack[_stack.Count - 1]); else ShowTabs(_tab ?? "home");
            PuttrArcade.Arcade.FreeUnused();   // what the game alone used (a scene's textures and meshes)
        }
    }
}
