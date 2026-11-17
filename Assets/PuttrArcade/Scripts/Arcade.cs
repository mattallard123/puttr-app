// Puttr Arcade: games played by putting on a real Puttr.
//
// The Player putts. The console's camera watches the ball climb the ramp to
// the hole and sends the path once the ball is gone. The arcade shows a
// life-size Puttr, replays those camera frames on its ramp, and then:
//
//   - a holed putt drops into the cup and rolls out behind the console, at
//     the speed and on the line it reached the hole with
//   - a miss carries on up the ramp. With enough pace it runs off the end;
//     without, it rolls back
//
// What is behind the console is the game: see GameMode, and the three games
// in LanesMode, DeadWeightMode and UnderTheCupMode.
//
// Three more games do not show the Puttr at all. They take the putt's line,
// pace and direction and launch something with them: see StageMode, and
// SpotKickMode, SlingshotMode and SendItMode.
//
// The whole scene is built in code. The scene file holds one GameObject with
// this component and a PuttrConsole.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.InputSystem;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PuttrArcade
{
    [RequireComponent(typeof(PuttrConsole))]
    public sealed class Arcade : MonoBehaviour
    {
        // ── puttr-app-2: hooks for the app shell (PuttrApp.GameBridge) ───────
        // When Embedded, the arcade starts straight in one game, sends no arcade analytics,
        // and tells the shell when the Player leaves for "All games".
        public static bool Embedded;
        public static string EmbeddedGameName;
        /// <summary>The app's one connection to the Puttr, used in place of this object's own.</summary>
        public static PuttrConsole SharedConsole;
        public static System.Action<GameMode, int> AppGameStarted;
        public static System.Action<GameMode, PuttResult> AppPutt;
        public static System.Action<GameMode, int[]> AppGameEnded;
        public static System.Action AppExit;
        /// <summary>The app may hold putts back (a round that must be on camera before a putt counts). Null: every putt is played.</summary>
        public static System.Func<bool> AppAcceptsPutts;
        /// <summary>The app's own verdict on a putt (puttr-app's maths), given the distance and tee it was putted from:
        /// it sets Made, the entry and the miss side on the putt, and says false for a putt the app's games ignore.</summary>
        public static System.Func<PuttResult, int, int, bool> AppJudge;
        public bool IsReady => _ready;
        public int IndexOf(string gameName) { for (int i = 0; _games != null && i < _games.Length; i++) if (_games[i].Name == gameName) return i; return -1; }
        public void Launch(int index) { players = Mathf.Clamp(EmbeddedPlayers, 1, 4); ApplyEmbeddedNames(); StartGame(Mathf.Clamp(index, 0, _games.Length - 1)); }
        /// <summary>puttr-app-2: how many play the embedded game, and their names (the app's player picker).</summary>
        public static int EmbeddedPlayers = 1;
        /// <summary>puttr-app-2: which Player putts first (the game page's choice). Games that take turns read it.</summary>
        public static int EmbeddedFirst;
        public static string[] EmbeddedNames;
        void ApplyEmbeddedNames() { for (int i = 0; i < Names.Length; i++) Names[i] = EmbeddedNames != null && i < EmbeddedNames.Length ? EmbeddedNames[i] : ""; }
        /// <summary>The app is done with this game: back to the app (through AppExit).</summary>
        public void Leave() => ShowHome();

        [Range(1, 4)] public int players = 1;
        [Tooltip("Slow the action down at the big moments so it can be seen.")]
        public bool slowMotion = true;
        [Tooltip("Play by itself with made-up putts. Also -puttrDemo on the command line.")]
        public bool demo = false;

        public Hud Hud { get; private set; }
        public GameAudio Audio { get; private set; }
        public World World { get; private set; }

        enum Phase { Home, Setup, Settings, Ready, Rolling, Between, GameOver }

        /// <summary>What each Player typed on the setup screen. Empty means "Player 1".</summary>
        public readonly string[] Names = { "", "", "", "" };
        public string PlayerName(int index) => index < Names.Length && !string.IsNullOrWhiteSpace(Names[index]) ? Names[index].Trim() : "Player " + (index + 1);
        int _nameFocus;
        bool _setupShot;

        PuttrConsole _console;
        Camera _camera;
        ParticleSystem _sparks;
        DepthOfField _focus;
        float _focusDistance = 48f;
        LineRenderer _trackLine;
        Material _trackMaterial;
        float _trackFade;

        GameMode[] _games;
        GameMode _game;
        int _selected;
        readonly List<Ball> _spare = new List<Ball>();
        Ball _ball;

        Phase _phase = Phase.Home;
        float _slowUntil, _ringFlash, _shake, _idleSince;
        Color _ringColor = Make.Bright;
        bool _flying, _offEnd;
        bool _typingHost, _swallowText;
        string _hostText = "";
        string _shotDir;
        int _shotIndex, _startGame = -1, _demoPutts = int.MaxValue, _demoStep, _puttsThisGame;
        Vector3 _camPos, _camLook;
        readonly System.Random _rng = new System.Random();

        // ── Setup ───────────────────────────────────────────────────────────

        AimView _aim;

        /// <summary>The ideal putt, drawn on the ramp and shown top right.</summary>
        public void ShowAim(int feet, int tee, float targetIn, float goalIn)
        {
            if (_aim == null || Hud == null) return;
            _aim.Show(feet, tee, targetIn, goalIn);
            string where = Mathf.Abs(targetIn) < 0.2f ? "Center cut" : Mathf.Abs(targetIn).ToString(Mathf.Abs(targetIn) < 3f ? "0.0" : "0") + " in " + (targetIn < 0f ? "left" : "right");
            float angle = AimView.EntryDegrees(feet, tee, targetIn);
            int deg = Mathf.RoundToInt(Mathf.Abs(angle));
            AimCard(where + "   \u00B7   " + (deg == 0 ? "straight in" : deg + "\u00B0 from the " + (angle > 0f ? "left" : "right")), -1f);
        }

        // The card's words, with the pace bar for games where pace is the point.
        void AimCard(string words, float paceNow, string caption = "IDEAL PUTT")
        {
            GameMode g = _game;
            if (g != null && g.AimPace(out float low, out float high, out float min, out float max))
            {
                string target = PaceWords.Length(PaceWords.Past(low)) + " to " + PaceWords.Length(PaceWords.Past(high)) + " past";
                Hud.SetAimCard(_aim.Texture, words, Mathf.InverseLerp(min, max, low), Mathf.InverseLerp(min, max, high), paceNow < 0f ? -1f : Mathf.InverseLerp(min, max, paceNow), target, caption);
            }
            else Hud.SetAimCard(_aim.Texture, words, caption: caption);
        }

        // After a putt: the real path rolled again in the card, close in on the cup.
        void ReplayAim(PuttResult real)
        {
            if (_aim == null || !_aim.Showing || real == null || _game == null || !_game.ShowsAim) return;
            _aim.Replay(real.PathInches, real.Made);
            PuttLaunch launch = real.Launch;
            float line = launch.LineAtHoleInches;
            string where = !launch.Reached ? "Short" : Mathf.Abs(line) < 0.2f ? "Center" : Mathf.Abs(line).ToString("0.0") + " in " + (line < 0f ? "left" : "right");
            float speed = launch.Reached ? (launch.HandoffSpeedInPerSec > 0f ? launch.HandoffSpeedInPerSec : launch.SpeedInPerSec) : 0f;
            AimCard(where + "   \u00B7   " + PaceWords.Of(real), launch.Reached ? speed : 0f, "YOUR PUTT");
        }

        // Set once Boot has built everything; Update and LateUpdate wait for it.
        bool _ready;

        void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
            Screen.sleepTimeout = SleepTimeout.NeverSleep; // nobody touches the screen while putting
#if UNITY_TVOS
            // The remote is held upright, or lies on a table: never turn its swipes round as if it
            // were held sideways for a game (with rotation on, a swipe up was read as right).
            UnityEngine.tvOS.Remote.allowRemoteRotation = false;
            UnityEngine.tvOS.Remote.reportAbsoluteDpadValues = false; // a swipe, not where the thumb rests
#endif
            _pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            World.ApplyPhysicsSettings();
            // The Puttr mark and its progress bar are up from the first frame; the arcade builds behind them a piece a frame.
            // Boot runs on the loader, not here: ShowHome stops this component's coroutines.
            Loader.Show().StartCoroutine(Boot());
            if (TraceNav)
            {
                // What the system hands Unity as input, and anything that arrives later.
                foreach (var dev in UnityEngine.InputSystem.InputSystem.devices) Debug.Log("Puttr Arcade nav device: " + dev.displayName + " (" + dev.layout + ")");
                UnityEngine.InputSystem.InputSystem.onDeviceChange += (dev, change) => Debug.Log("Puttr Arcade nav device " + change + ": " + dev.displayName + " (" + dev.layout + ")");
                UnityEngine.InputSystem.InputSystem.onEvent += (ev, dev) => { if (dev != null && !(dev is UnityEngine.InputSystem.Pointer)) _traceEvents++; };
            }
        }

        IEnumerator Boot()
        {
            yield return null; // let the spinner draw before the heavy work starts
            var clock = System.Diagnostics.Stopwatch.StartNew();
            World = new World();
            World.Build(transform);
            Audio = new GameAudio(transform);
            Hud = new Hud(transform);
            // The HUD stays off while it is built, or bits of it show through the spinner.
            Canvas hud = transform.Find("HUD")?.GetComponent<Canvas>();
            if (hud != null) hud.enabled = false;
            Loader.Progress(0.1f);
            yield return null;

            _games = new GameMode[]
            {
                new OnFireMode(), new MajorLeagueMode(), new CricketMode(), new ArcheryMode(), new DerbyMode(), new TugOfWarMode(), new TurfWarMode(), new WindmillMode(),
                new LanesMode(), new DeadWeightMode(), new UnderTheCupMode(),
                new SpotKickMode(), new SlingshotMode(), new SendItMode(),
                new BalloonPopMode(), new PuttStarMode(), new BeerPongMode(), new RuleStageMode(), // puttr-app-2: rebuilt for the app
                new FreePuttMode(), // puttr-app-2: last, so the arcade's own order is as it was
            };
            int stages = 0, built = 0;
            for (int i = 0; i < Names.Length; i++) Names[i] = PlayerPrefs.GetString("puttr.arcade.name." + i, "");
            foreach (GameMode game in _games)
            {
                game.Arcade = this;
                game.Root = new GameObject(game.Name).transform;
                // A stage game is built well away from the console, on its own.
                game.Root.SetParent(game.OwnStage ? World.Root : World.Hall, false);
                if (game.OwnStage) game.Root.localPosition = new Vector3(6000f * ++stages, 0f, 0f);
                game.Build();
                game.Root.gameObject.SetActive(false);
                Loader.Progress(0.1f + 0.8f * ++built / _games.Length);
                yield return null;
            }
            Hud.BuildHome(_games);
            Hud.BuildSetup();
            Hud.BuildSettings();
            Loader.Progress(0.95f);
            yield return null;

            BuildCamera();
            BuildEffects();
            _aim = new AimView(transform);
            _camera.cullingMask &= ~(1 << AimView.Layer); // the ideal path is for the aim card only

            _console = SharedConsole != null ? SharedConsole : GetComponent<PuttrConsole>(); // puttr-app-2
            _console.Putt += OnPutt;

            string[] args = System.Environment.GetCommandLineArgs();
            // Launch arguments do not reach an app on the iPad or Apple TV simulator, but its
            // environment does (SIMCTL_CHILD_PUTTR_ARGS="-puttrDemo -puttrShots /path").
            string extra = System.Environment.GetEnvironmentVariable("PUTTR_ARGS");
            if (!string.IsNullOrEmpty(extra))
            {
                var all = new List<string>(args);
                all.AddRange(extra.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
                args = all.ToArray();
            }
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : "";
                if (args[i] == "-puttrDemo") demo = true;
                if (args[i] == "-puttrShots") _shotDir = next;
                if (args[i] == "-puttrPlayers" && int.TryParse(next, out int n)) players = Mathf.Clamp(n, 1, 4);
                if (args[i] == "-puttrGame" && int.TryParse(next, out int g)) _startGame = Mathf.Clamp(g - 1, 0, _games.Length - 1);
                if (args[i] == "-puttrDemoPutts" && int.TryParse(next, out int k)) _demoPutts = Mathf.Max(1, k);
                if (args[i] == "-puttrEndAfter" && int.TryParse(next, out int e)) _endAfter = Mathf.Max(0, e);
                // -puttrLevel 0|1|2: every Player on Easy, Medium or Hard (demos and tests).
                if (args[i] == "-puttrLevel" && int.TryParse(next, out int lv)) for (int j = 0; j < _levels.Length; j++) _levels[j] = Mathf.Clamp(lv, 0, 2);
            }
            if (_shotDir != null) System.IO.Directory.CreateDirectory(_shotDir);

            if (Keyboard.current != null) Keyboard.current.onTextInput += OnTextInput;
            // What gets played is logged, but not the demo's made-up putts or work in the editor.
            if (Embedded) { int at = IndexOf(EmbeddedGameName); if (at >= 0) { _startGame = at; players = Mathf.Clamp(EmbeddedPlayers, 1, 4); ApplyEmbeddedNames(); } }
            if (!Embedded && !demo && !Application.isEditor && System.Array.IndexOf(args, "-puttrNoLog") < 0)
            {
                ArcadeLog.Begin(gameObject);
                ArcadeLog.Event("app_open");
            }
            // puttr-app-2: -puttrShots inside the app writes every game's card picture out (the app's game cards use them).
            if (Embedded && _shotDir != null) MakeCards();
            if (_startGame >= 0) StartGame(_startGame); else ShowHome();
            // -puttrSettings opens the console list straight away (and screenshots it, with -puttrShots).
            if (System.Array.IndexOf(args, "-puttrSettings") >= 0) { OpenSettings(); _settingsShotAt = Time.unscaledTime + 9f; }
            if (hud != null) hud.enabled = true;
            _ready = true;
            _idleSince = Time.unscaledTime;
            Debug.Log("Puttr Arcade: built in " + clock.ElapsedMilliseconds + " ms");
            // One frame of the home screen drawn under the spinner, then it fades away.
            yield return null;
            Loader.Hide();
        }

        const float ConsoleShadows = 140f;
        UniversalRenderPipelineAsset _pipeline;

        void OnDestroy()
        {
            // In the editor a change to the pipeline asset outlives Play: put it back.
            if (_pipeline != null) _pipeline.shadowDistance = ConsoleShadows;
            if (Keyboard.current != null) Keyboard.current.onTextInput -= OnTextInput;
            Time.timeScale = 1f;
        }

        void BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(transform, false);
            _camera = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.004f, 0.005f, 0.006f);
            _camera.fieldOfView = 38f;
            _camera.nearClipPlane = 0.5f;
            _camera.farClipPlane = 9000f;
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var volume = new GameObject("Post").AddComponent<Volume>();
            volume.transform.SetParent(transform, false);
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 1.0f;
            bloom.intensity.value = 0.55f;
            bloom.scatter.value = 0.7f;
            Tonemapping tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;
            // The photographs are cool in the shadows and a little desaturated.
            ColorAdjustments grade = profile.Add<ColorAdjustments>(true);
            grade.postExposure.value = 0.15f;
            grade.contrast.value = 12f;
            grade.saturation.value = -8f;
            grade.colorFilter.value = new Color(0.97f, 1f, 1.02f);
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.34f;
            vignette.smoothness.value = 0.5f;
            // A long lens, wide open: the photographs have very little in focus.
            _focus = profile.Add<DepthOfField>(true);
            _focus.mode.value = DepthOfFieldMode.Bokeh;
            _focus.focusDistance.value = 48f;
            _focus.focalLength.value = 200f;
            _focus.aperture.value = 2.4f;
            MotionBlur blur = profile.Add<MotionBlur>(true);
            blur.intensity.value = 0.28f;
            FilmGrain grain = profile.Add<FilmGrain>(true);
            grain.intensity.value = 0.22f;
            grain.response.value = 0.8f;
            volume.profile = profile;

            _camPos = IdleCameraPosition;
            _camLook = IdleCameraLook;
            _camera.transform.position = _camPos;
            _camera.transform.LookAt(_camLook);
        }

        void BuildEffects()
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(World.Root, false);
            _sparks = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _sparks.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 34f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.4f);
            main.gravityModifier = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            ParticleSystem.EmissionModule emission = _sparks.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = _sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            ParticleSystem.ColorOverLifetimeModule fade = _sparks.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Make.Glow(Color.white, true, Make.SoftDot());
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            // The path the console's camera saw, drawn on the carpet as the ball is replayed.
            var lineGo = new GameObject("Tracked path");
            lineGo.transform.SetParent(World.Root, false);
            _trackLine = lineGo.AddComponent<LineRenderer>();
            _trackMaterial = Make.Glow(Make.Bright, true);
            _trackLine.sharedMaterial = _trackMaterial;
            _trackLine.widthMultiplier = 0.1f;
            _trackLine.useWorldSpace = true;
            _trackLine.alignment = LineAlignment.View;
            _trackLine.numCapVertices = 3;
            _trackLine.shadowCastingMode = ShadowCastingMode.Off;
            _trackLine.positionCount = 0;
        }

        // ── For the games ───────────────────────────────────────────────────

        /// <summary>Slow time for a moment (if slow motion is on).</summary>
        public void SlowMotion(float seconds)
        {
            if (slowMotion) _slowUntil = Time.unscaledTime + seconds;
        }

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        public void Sparks(Vector3 position, int count)
        {
            _sparks.transform.position = position;
            _sparks.Emit(count);
        }

        /// <summary>A burst of green and white over a spot: something good happened.</summary>
        public void Celebrate(int count, Vector3 center)
        {
            for (int i = 0; i < count; i++)
            {
                var emit = new ParticleSystem.EmitParams
                {
                    position = center + new Vector3(Random.Range(-5f, 5f), Random.Range(1f, 5f), Random.Range(-3f, 5f)),
                    velocity = new Vector3(Random.Range(-14f, 14f), Random.Range(14f, 44f), Random.Range(-16f, 6f)),
                    startColor = i % 3 == 0 ? (Color32)Color.white : (Color32)Make.Bright,
                    startLifetime = Random.Range(0.6f, 1.3f),
                    startSize = Random.Range(0.14f, 0.4f),
                };
                _sparks.Emit(emit, 1);
            }
        }

        /// <summary>Give a ball back once the game has finished with it.</summary>
        public void Release(Ball ball)
        {
            if (ball == null) return;
            ball.PutAway();
            if (!_spare.Contains(ball)) _spare.Add(ball);
            if (_ball == ball) _ball = null;
        }

        Ball TakeBall()
        {
            Ball ball;
            if (_spare.Count > 0) { ball = _spare[_spare.Count - 1]; _spare.RemoveAt(_spare.Count - 1); }
            else ball = Ball.Create(World.Root);
            ball.SetCharged(false);
            ball.SetTint(Color.white);
            ball.Resistance = LaneSpec.RollingResistance;
            ball.Hit = OnBallHit;
            return ball;
        }

        void OnBallHit(Collision collision)
        {
            if (collision.rigidbody != null) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed > 12f && (_game == null || !_game.QuietBall)) Audio.Thump(speed); // puttr-app-2
        }

        /// <summary>Save a screenshot, when started with -puttrShots folder.</summary>
        public void Shot(string tag)
        {
            if (_shotDir == null) return;
            string path = System.IO.Path.Combine(_shotDir, (_shotIndex++).ToString("000") + "_" + tag + ".png");
            // On an iPad or Apple TV CaptureScreenshot puts any path under the app's own
            // folder, so the picture is taken and written here instead.
            if (Application.isMobilePlatform || Application.platform == RuntimePlatform.tvOS) StartCoroutine(ShotNow(path));
            else ScreenCapture.CaptureScreenshot(path);
        }

        IEnumerator ShotNow(string path)
        {
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            try { System.IO.File.WriteAllBytes(path, shot.EncodeToPNG()); }
            catch (System.Exception e) { Debug.LogWarning("Screenshot not saved: " + e.Message); }
            Destroy(shot);
        }

        // ── Home, and moving between games ──────────────────────────────────

        // ── The pictures on the game cards ──────────────────────────────────
        //
        // Each card shows its game: a picture taken of the game's own set, once, the first
        // time the home screen opens. Nothing is drawn by hand, so a new game gets its card
        // for free and a card can never be out of date.

        bool _cardsMade;
        const int CardW = 480, CardH = 288;

        void MakeCards()
        {
            if (_cardsMade) return;
            _cardsMade = true;
            var eye = new GameObject("Card camera").AddComponent<Camera>();
            eye.CopyFrom(_camera);
            eye.enabled = false;
            eye.aspect = (float)CardW / CardH;
            for (int i = 0; i < _games.Length; i++)
            {
                GameMode game = _games[i];
                for (int k = 0; k < _games.Length; k++) _games[k].Root.gameObject.SetActive(k == i);
                game.Preview();
                game.PoseForCard();
                try { game.Tick(); } catch (System.Exception) { } // lets a game set its fire going before the picture
                // Fire and smoke start from nothing: run them on a little, so the picture has some.
                foreach (ParticleSystem smoke in game.Root.GetComponentsInChildren<ParticleSystem>()) smoke.Simulate(2.5f, true, true);
                var picture = new RenderTexture(CardW, CardH, 24) { antiAliasing = 4, name = "Card " + game.Name };
                eye.transform.position = game.CardFrom;
                eye.transform.rotation = Quaternion.LookRotation(game.CardLook - game.CardFrom, Vector3.up);
                eye.fieldOfView = game.CardFov;
                eye.targetTexture = picture;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = picture };
                if (RenderPipeline.SupportsRenderRequest(eye, request)) RenderPipeline.SubmitRenderRequest(eye, request);
                else eye.Render();
                eye.targetTexture = null;
                game.AfterCard();
                Hud.SetCard(i, picture);
                if (_shotDir != null) SaveCard(picture, game.Name);
                foreach (ParticleSystem smoke in game.Root.GetComponentsInChildren<ParticleSystem>()) smoke.Play(true);
            }
            SceneCardsPass(eye);
            Destroy(eye.gameObject);
        }

        /// <summary>puttr-app-2: the rule games' own 3D worlds, rendered as cards too (with -puttrShots): game type, its scene,
        /// and the view it opens on. Written as card_rule_TYPE.png from each scene's own camera.</summary>
        public static System.Collections.Generic.List<(string type, System.Func<RuleScene> scene, ShotView view)> SceneCards;

        void SceneCardsPass(Camera eye)
        {
            if (SceneCards == null || _shotDir == null) return;
            int at = System.Array.FindIndex(_games, g => g is RuleStageMode);
            if (at < 0) return;
            var stage = (RuleStageMode)_games[at];
            for (int k = 0; k < _games.Length; k++) _games[k].Root.gameObject.SetActive(k == at);
            foreach (var card in SceneCards)
            {
                RuleStageMode.NextScene = card.scene;
                RuleStageMode.StartView = card.view;
                try { stage.Begin(1); stage.Tick(); }
                catch (System.Exception e) { Debug.LogWarning("Puttr card: " + card.type + " scene failed: " + e.Message); stage.End(); continue; }
                Audio.GameBed(null);
                var picture = new RenderTexture(CardW, CardH, 24) { antiAliasing = 4, name = "Card " + card.type };
                eye.transform.position = stage.CameraPosition;
                eye.transform.rotation = Quaternion.LookRotation(stage.CameraLook - stage.CameraPosition, Vector3.up);
                eye.fieldOfView = stage.CameraFov;
                eye.targetTexture = picture;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = picture };
                if (RenderPipeline.SupportsRenderRequest(eye, request)) RenderPipeline.SubmitRenderRequest(eye, request);
                else eye.Render();
                eye.targetTexture = null;
                SaveCard(picture, "rule_" + card.type);
                picture.Release();
                stage.End();
            }
            RuleStageMode.NextScene = null;
            RuleStageMode.StartView = null;
        }

        // With -puttrShots, the cards are written out too, to be looked at.
        void SaveCard(RenderTexture picture, string name)
        {
            RenderTexture was = RenderTexture.active;
            RenderTexture.active = picture;
            var copy = new Texture2D(picture.width, picture.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, picture.width, picture.height), 0, 0);
            copy.Apply();
            RenderTexture.active = was;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(_shotDir, "card_" + name.Replace(" ", "") + ".png"), copy.EncodeToPNG());
            Destroy(copy);
        }

        void ShowHome()
        {
            LogLeave();
            StopAllCoroutines();
            MakeCards();
            Time.timeScale = 1f;
            Audio.Hush();
            if (_game != null && _game.OwnStage) { _camPos = IdleCameraPosition; _camLook = IdleCameraLook; }
            if (_game != null) _game.End();
            Hud.SetTeeSign(0, 0);
            _game = null;
            Release(_ball);
            _flying = false;
            _trackLine.positionCount = 0;
            _phase = Phase.Home;
            _idleSince = Time.unscaledTime;
            _menu = false;
            Hud.SetMenuButton(false);
            Hud.HideOverlay();
            Hud.HideSetup();
            Hud.HideSettings();
            Hud.SetScoreVisible(false);
            Hud.SetPrompt("");
            Hud.HidePutt();
            Hud.SetGameName(Embedded ? "" : "A R C A D E"); // puttr-app-2: no Arcade name inside the app
            Select(_selected);
            if (Embedded && _ready) AppExit?.Invoke(); // puttr-app-2: back to the app
        }

        GameMode _heard;

        // The highlighted game is live behind the home screen: its set is
        // switched on, and the camera cuts to where that game is watched from.
        void Select(int index)
        {
            _selected = (index + _games.Length) % _games.Length;
            GameMode game = _games[_selected];
            for (int i = 0; i < _games.Length; i++) _games[i].Root.gameObject.SetActive(i == _selected);
            game.Preview();
            if (Audio != null) { bool moved = _heard != game; _heard = game; Audio.SetScene(game.Sound); if (moved && _phase == Phase.Home) Audio.UiMove(); }
            Hud.SetScoreVisible(false);
            _camPos = game.CameraPosition;
            _camLook = game.CameraLook;
            _camera.fieldOfView = Fitted(game.CameraFov, game);
            Hud.ShowHome(_selected, BoardText(game, 3));
        }

        // ── Settings: which console to play on ──────────────────────────────

        int _settingsColumn, _settingsRow;
        // The console just chosen: once it connects, go on to the games.
        int _chosenColumn = -1;
        string _chosenKey;
        float _settingsShotAt;
        readonly List<Hud.ConsoleRow>[] _consoleRows = { new List<Hud.ConsoleRow>(), new List<Hud.ConsoleRow>() };
        readonly List<string>[] _consoleKeys = { new List<string>(), new List<string>() };

        void OpenSettings()
        {
            StopAllCoroutines();
            _phase = Phase.Settings;
            _chosenColumn = -1;
            _clearArmed = false;
            Hud.HideHome();
            Hud.SetGameName("S E T T I N G S");
            StartCoroutine(WatchConsoles());
        }

        // While the screen is open: ask the WiFi addresses again every few
        // seconds, and redraw twice a second so signal and state stay current.
        IEnumerator WatchConsoles()
        {
            float nextProbe = 0f;
            while (_phase == Phase.Settings)
            {
                if (Time.unscaledTime >= nextProbe) { nextProbe = Time.unscaledTime + 5f; StartCoroutine(_console.Probe()); }
                ShowSettings();
                if (ChosenIsConnected())
                {
                    // Long enough to see it say CONNECTED.
                    yield return new WaitForSecondsRealtime(0.9f);
                    ShowHome();
                    yield break;
                }
                yield return new WaitForSecondsRealtime(_chosenColumn >= 0 ? 0.15f : 0.5f);
            }
        }

        const string TypeAnAddress = "(type)", ClearScores = "(clear)";
        static string UseIt => Hud.Choose("ENTER TO USE", "TAP TO USE", "PRESS TO USE");
        bool _clearArmed;
        TouchScreenKeyboard _addressKeys;

        // Ask for the console's address: on the device's own keyboard where
        // it has one, otherwise typed on this one.
        void BeginAddress()
        {
            if (TouchScreenKeyboard.isSupported)
            {
                _addressKeys = TouchScreenKeyboard.Open(_console.host ?? "", TouchScreenKeyboardType.URL, false, false, false, false, "192.168.1.20 or 192.168.1.20:3100");
                return;
            }
            _typingHost = true;
            _swallowText = false;
            _hostText = _console.host ?? "";
            ShowHostEntry();
        }

        // Use this address, and go on to the games once it answers.
        void UseAddress(string address)
        {
            address = (address ?? "").Trim();
            _console.SetHost(address);
            if (_phase != Phase.Settings || address.Length == 0) return;
            _chosenColumn = 1;
            _chosenKey = address;
            _settingsColumn = 1;
        }

        // Use the highlighted console. True if there was nothing to wait for.
        bool ChooseConsole()
        {
            if (_settingsRow >= _consoleKeys[_settingsColumn].Count) return false;
            string key = _consoleKeys[_settingsColumn][_settingsRow];
            if (key == TypeAnAddress) { _clearArmed = false; BeginAddress(); return false; }
            if (key == ClearScores)
            {
                // Twice, so a slip of the finger does not wipe an evening's scores.
                if (_clearArmed) { Leaderboard.Clear(); _clearArmed = false; } else _clearArmed = Leaderboard.Count() > 0;
                return false;
            }
            _clearArmed = false;
            _chosenColumn = _settingsColumn;
            _chosenKey = key;
            if (ChosenIsConnected()) { ShowHome(); return true; }
            if (_settingsColumn == 0) _console.UseBluetooth(key); else _console.SetHost(key);
            return false;
        }

        bool ChosenIsConnected()
        {
            if (_chosenColumn == 0) return _console.Bluetooth.Connected && _console.Bluetooth.ConnectedId == _chosenKey;
            // A typed address may have had its port added.
            if (_chosenColumn == 1) return _console.State == ConsoleState.Connected && _console.ActiveHost != null && (_console.ActiveHost == _chosenKey || _console.ActiveHost.StartsWith(_chosenKey + ":"));
            return false;
        }

        void ShowSettings()
        {
            PuttrBluetooth radio = _console.Bluetooth;
            for (int c = 0; c < 2; c++) { _consoleRows[c].Clear(); _consoleKeys[c].Clear(); }

            foreach (PuttrBluetooth.Device d in radio.Devices)
            {
                bool on = radio.Connected && d.Id == radio.ConnectedId;
                string signal = d.Rssi == 0 ? "" : d.Rssi > -62 ? "Strong signal" : d.Rssi > -78 ? "Good signal" : "Weak signal";
                string detail = on ? "Firmware " + (radio.Version ?? "…") + (signal.Length > 0 ? "      " + signal : "") : signal;
                _consoleRows[0].Add(new Hud.ConsoleRow { Title = d.Name, Detail = detail, Connected = on, Badge = on ? "CONNECTED" : _chosenColumn == 0 && _chosenKey == d.Id ? "CONNECTING…" : UseIt });
                _consoleKeys[0].Add(d.Id);
            }
            foreach (KeyValuePair<string, string> host in _console.Probed)
            {
                bool answered = host.Value != null;
                bool on = _console.State == ConsoleState.Connected && _console.ActiveHost == host.Key;
                if (!answered && !on) continue;
                _consoleRows[1].Add(new Hud.ConsoleRow { Title = host.Key, Detail = "Firmware " + (host.Value ?? _console.Version ?? "…"), Connected = on, Badge = on ? "CONNECTED" : _chosenColumn == 1 && _chosenKey == host.Key ? "CONNECTING…" : UseIt });
                _consoleKeys[1].Add(host.Key);
            }

            // The one in use, if the search did not list it (an address that was typed).
            if (_console.State == ConsoleState.Connected && _console.ActiveHost != null && !_consoleKeys[1].Contains(_console.ActiveHost))
            {
                _consoleRows[1].Add(new Hud.ConsoleRow { Title = _console.ActiveHost, Detail = "Firmware " + (_console.Version ?? "…"), Connected = true, Badge = "CONNECTED" });
                _consoleKeys[1].Add(_console.ActiveHost);
            }

            // Last on the WiFi side: a console this app cannot find for itself.
            _consoleRows[1].Add(new Hud.ConsoleRow { Title = "Enter an address…", Detail = string.IsNullOrEmpty(_console.host) ? "For a Puttr this app has not found, or a simulator on another computer" : "Now set to " + _console.host, Badge = "" });
            _consoleKeys[1].Add(TypeAnAddress);

            // And the scores saved on this device.
            int saved = Leaderboard.Count();
            _consoleRows[1].Add(new Hud.ConsoleRow { Title = _clearArmed ? Hud.Choose("Choose again to clear every score", "Tap again to clear every score", "Press again to clear every score") : "Clear leaderboards", Detail = saved == 0 ? "No scores saved on this device" : saved + (saved == 1 ? " score" : " scores") + " saved on this device", Badge = "", Faint = saved == 0 });
            _consoleKeys[1].Add(ClearScores);

            string bluetooth =
                !PuttrBluetooth.Supported ? "Not available in this build" :
                !_console.useBluetooth ? "Turned off" :
                radio.State == BluetoothState.NotAllowed ? "Not allowed" :
                radio.State == BluetoothState.Off ? "Bluetooth is turned off" :
                radio.State == BluetoothState.Connected ? "Connected to " + radio.ConnectedName :
                radio.State == BluetoothState.Connecting ? "Connecting…" :
                radio.State == BluetoothState.Scanning ? "Searching for consoles…" : "Starting…";
            string wifi = _console.State == ConsoleState.Connected ? "Connected to " + _console.ActiveHost : "Searching for consoles…";
            string noBluetooth =
                !PuttrBluetooth.Supported ? "Bluetooth works in the Mac app and the Mac editor for now." :
                radio.State == BluetoothState.NotAllowed ? Hud.Choose("Allow Puttr (or Unity) under\nSystem Settings > Privacy & Security > Bluetooth,\nthen start it again.", "Allow Bluetooth for Puttr Arcade in the Settings app,\nthen open it again.", "Allow Bluetooth for Puttr Arcade in Settings,\nthen open it again.") :
                radio.State == BluetoothState.Off ? Hud.Choose("Turn Bluetooth on in the menu bar.", "Turn Bluetooth on in the Settings app.", "Turn Bluetooth on in Settings.") :
                "No Puttr in range yet.\nIs it switched on, and is the Puttr app on your phone closed?\nA console talks to one device at a time." + (radio.LastError != null ? "\n\n" + radio.LastError : "");
            string noWifi = "No Puttr has answered on this network.\nSet its WiFi up from the Puttr app, or " + Hud.Choose("press H to type its address.", "tap Enter an address below.", "choose Enter an address below.");

            bool touch = Hud.Kind == Hud.Device.Touch && !_clearArmed; // no highlight to move with fingers
            _settingsColumn = _consoleRows[_settingsColumn].Count > 0 ? _settingsColumn : _consoleRows[1 - _settingsColumn].Count > 0 ? 1 - _settingsColumn : _settingsColumn;
            _settingsRow = Mathf.Clamp(_settingsRow, 0, Mathf.Max(0, _consoleRows[_settingsColumn].Count - 1));
            Hud.ShowSettings(new[] { bluetooth, wifi }, _consoleRows, new[] { noBluetooth.Replace("\\n", "\n"), noWifi.Replace("\\n", "\n") }, touch ? -1 : _settingsColumn, touch ? -1 : _settingsRow);
        }

        // Before a game: how it is played, how many Players, and their names.
        void OpenSetup(int index)
        {
            ArcadeLog.Event("game_view", "game", _games[(index + _games.Length) % _games.Length].Name);
            // With a remote the highlight starts on Let's play, so one press plays. Elsewhere it
            // starts on the first name, ready to be typed.
            _zone = Hud.Kind == Hud.Device.Tv ? ZoneStart : ZoneNames;
            _col = 0;
            Select(index);
            _phase = Phase.Setup;
            _idleSince = Time.unscaledTime;
            players = Mathf.Clamp(players, 1, _games[_selected].MaxPlayers);
            _nameFocus = Mathf.Min(_nameFocus, players - 1);
            Hud.HideHome();
            Hud.SetGameName(Hud.Spaced(_games[_selected].Name));
            ShowSetup();
        }

        // ── Setup: what is highlighted ──────────────────────────────────────
        //
        // The same for a remote and for keys: up and down move through the Players, the
        // Add Player row and Let's play; on a Player's row left and right move between the
        // name, the difficulty and the cross that takes the Player out; a press does what
        // is highlighted. Fingers just tap the thing itself.

        const int ZoneNames = 0, ZoneAdd = 1, ZoneStart = 2;
        const int ColName = 0, ColLevel = 1, ColRemove = 2;
        int _zone, _col;
        readonly int[] _levels = new int[4]; // Easy, Medium or Hard for each Player

        bool Room => _games[_selected].MaxPlayers > 1 && players < _games[_selected].MaxPlayers;
        bool TypingName => _zone == ZoneNames && _col == ColName;

        void ShowSetup()
        {
            GameMode game = _games[_selected];
            if (_zone == ZoneAdd && !Room) _zone = ZoneStart;
            if (_col == ColLevel && !game.HasLevels) _col = ColName;
            if (_col == ColRemove && players <= 1) _col = game.HasLevels ? ColLevel : ColName;
            // This game's leaderboard on this device, at Player 1's difficulty. Team games and
            // games against Puttr do not keep one, so they show none.
            string key = BoardKey(game, _levels[0]);
            List<Leaderboard.Row> top = Leaderboard.Top(key, 5);
            bool ranked = top.Count > 0 || !(game is CricketMode || game is DerbyMode || game is TugOfWarMode || game is TurfWarMode || game is MajorLeagueMode);
            string title = !ranked ? null : "TOP SCORES ON THIS DEVICE" + (game.HasLevels ? "      " + LevelNames[_levels[0]].ToUpperInvariant() : "");
            var board = new Hud.Line[top.Count];
            for (int i = 0; i < top.Count; i++) board[i] = new Hud.Line { Name = top[i].Name, Value = top[i].Score + " " + game.ScoreWord(top[i].Score), Lit = top[i].Fresh };
            // Alone you play Puttr; with a second Player you play each other.
            string opponent = game.SoloOpponent == null ? null
                : players <= 1 ? game.SoloOpponent + "   <color=#FFFFFF80>·   Add a Player to play each other instead</color>"
                : "Each other";
            Hud.ShowSetup(game, players, Names, _zone, _zone == ZoneNames ? _nameFocus : -1, _col, game.HasLevels ? _levels : null, title, board, opponent);
        }

        /// <summary>Up or down on the setup screen.</summary>
        void SetupMove(int step)
        {
            if (_zone == ZoneNames)
            {
                int next = _nameFocus + step;
                if (next >= 0 && next < players) { _nameFocus = next; return; }
                _zone = next < 0 ? ZoneStart : Room ? ZoneAdd : ZoneStart;
            }
            else if (_zone == ZoneAdd)
            {
                if (step > 0) _zone = ZoneStart; else { _zone = ZoneNames; _nameFocus = players - 1; }
            }
            else // Let's play
            {
                // Up from Let's play is the Player's name (the last one), not Add Player: naming
                // is what people go up for. Down from the names comes back through Add Player.
                if (step > 0) { _zone = ZoneNames; _nameFocus = 0; }
                else { _zone = ZoneNames; _nameFocus = players - 1; }
                _col = ColName;
            }
        }

        /// <summary>Left or right: along a Player's row.</summary>
        void SetupSide(int step)
        {
            if (_zone != ZoneNames) return;
            var cols = new List<int> { ColName };
            if (_games[_selected].HasLevels) cols.Add(ColLevel);
            if (players > 1) cols.Add(ColRemove);
            int at = Mathf.Max(0, cols.IndexOf(_col));
            _col = cols[Mathf.Clamp(at + step, 0, cols.Count - 1)];
        }

        /// <summary>A press on what is highlighted. True if it started the game.</summary>
        bool SetupPress()
        {
            if (_zone == ZoneStart) { LeaveSetup(); StartGame(_selected); return true; }
            if (_zone == ZoneAdd) { AddPlayer(false); return false; }
            if (_col == ColLevel) { CycleLevel(_nameFocus); return false; }
            if (_col == ColRemove) { RemovePlayer(_nameFocus); return false; }
            // The name. Where there is an on-screen keyboard it comes up; with real keys the
            // name is typed straight in, so Enter there means play.
            if (TouchScreenKeyboard.isSupported)
            {
                if (_keys == null) _keys = TouchScreenKeyboard.Open(Names[_nameFocus], TouchScreenKeyboardType.Default, false, false, false, false, "Player name", 12);
                return false;
            }
            LeaveSetup();
            StartGame(_selected);
            return true;
        }

        void CycleLevel(int player) => _levels[player] = (_levels[player] + 1) % 3;

        /// <summary>One more Player, whose name is the one now being typed. With fingers the
        /// keyboard comes up for it too, so adding and naming is one tap.</summary>
        void AddPlayer(bool keyboard)
        {
            if (players >= _games[_selected].MaxPlayers) return;
            players++;
            _nameFocus = players - 1;
            _levels[_nameFocus] = _levels[0]; // starts where the first Player is
            _zone = ZoneNames;
            _col = ColName;
            if (keyboard && TouchScreenKeyboard.isSupported && Hud.Kind != Hud.Device.Tv)
                _keys = TouchScreenKeyboard.Open(Names[_nameFocus], TouchScreenKeyboardType.Default, false, false, false, false, "Player name", 12);
        }

        /// <summary>Take this Player out. Those after move up; the name is kept in the freed
        /// place, so adding a Player again brings it back.</summary>
        void RemovePlayer(int index)
        {
            if (players <= 1 || index < 0 || index >= players) return;
            string gone = Names[index];
            for (int i = index; i < players - 1; i++) { Names[i] = Names[i + 1]; _levels[i] = _levels[i + 1]; }
            Names[players - 1] = gone;
            players--;
            _nameFocus = Mathf.Min(_nameFocus > index ? _nameFocus - 1 : _nameFocus, players - 1);
        }

        void LeaveSetup()
        {
            for (int i = 0; i < Names.Length; i++) PlayerPrefs.SetString("puttr.arcade.name." + i, Names[i].Trim());
            PlayerPrefs.Save();
            Hud.HideSetup();
        }

        // ── Logging: what was played, and how it went ───────────────────────

        string _logGame, _logConsole = "";
        float _logSince;
        int _logPlayers;

        /// <summary>A game was left before it ended (another was started, or back to all games).</summary>
        void LogLeave()
        {
            if (_logGame == null) return;
            ArcadeLog.Event("game_abandon", "game", _logGame, "players", _logPlayers, "putts", _puttsThisGame, "seconds", Mathf.RoundToInt(Time.unscaledTime - _logSince));
            _logGame = null;
        }

        string LogConsole() => _console == null ? "none" : _console.Bluetooth != null && _console.Bluetooth.Connected ? "bluetooth" : _console.State == ConsoleState.Connected ? "wifi" : "none";

        void LogPutt(PuttResult putt)
        {
            if (_game == null) return;
            if (!demo) AppPutt?.Invoke(_game, _truePutt ?? putt); // puttr-app-2
            PuttLaunch launch = putt.Launch;
            string result = !launch.Reached ? "short" : putt.Made ? "made" : putt.MissByInches <= 0.01f ? "lip_out" : putt.MissSide == MissSide.Left ? "miss_left" : putt.MissSide == MissSide.Right ? "miss_right" : "miss";
            // "result" is what the game saw at this level; the true_ fields are the putt as it was.
            PuttResult real = _truePutt ?? putt;
            ArcadeLog.Event("putt", "game", _game.Name, "players", _logPlayers, "number", _puttsThisGame, "level", LevelNames[Level], "result", result,
                "speed_fps", real.Launch.SpeedInPerSec / 12f, "line_in", real.Launch.LineAtHoleInches, "miss_in", real.MissByInches, "true_made", real.Made,
                "entry", putt.Made ? putt.EntryDescription : null);
        }

        // ── puttr-app-2: a game's set behind the app's game page ────────────
        // The set is switched on with nothing to play, the HUD is off, the camera swings slowly round it, and the
        // arcade takes no input: the app's page is on top. Starting the game ends it.

        bool _backdrop;
        GameMode _backdropMode;
        public bool InBackdrop => _backdrop;

        public void Backdrop(int index)
        {
            if (!_ready || index < 0 || index >= _games.Length) return;
            if (_game != null) { StopAllCoroutines(); _game.End(); _game = null; }
            _phase = Phase.Home;
            _selected = index;
            GameMode m = _games[index];
            for (int i = 0; i < _games.Length; i++) _games[i].Root.gameObject.SetActive(i == index);
            if (m is RuleStageMode stage) stage.Begin(1); else m.Preview();
            Audio.Hush();
            Audio.GameBed(null);
            Hud.SetShown(false);
            _backdrop = true;
            _backdropMode = m;
            _camPos = m.CardFrom;
            _camLook = m.CardLook;
            _backdropSince = Time.unscaledTime;
        }

        public void EndBackdrop()
        {
            if (!_backdrop) return;
            _backdrop = false;
            if (_backdropMode is RuleStageMode stage) stage.End();
            _backdropMode = null;
            Hud.SetShown(true);
        }

        float _backdropSince;

        // Round the set and back, slowly: about 30 degrees either way over half a minute.
        void OrbitBackdrop()
        {
            GameMode m = _backdropMode;
            if (_camera.rect != new Rect(0f, 0f, 1f, 1f)) _camera.rect = new Rect(0f, 0f, 1f, 1f);
            Vector3 center = m.CardLook, offset = m.CardFrom - m.CardLook;
            float t = Time.unscaledTime - _backdropSince;
            float swing = Mathf.Sin(t * 0.2f) * 30f;
            Vector3 want = center + Quaternion.Euler(0f, swing, 0f) * offset;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2f);
            _camPos = Vector3.Lerp(_camPos, want, k);
            _camLook = Vector3.Lerp(_camLook, center, k);
            _camera.transform.position = _camPos;
            _camera.transform.LookAt(_camLook);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, Fitted(Mathf.Max(m.CardFov, m.CameraFov), m), k);   // the game's own lens, wider than a card's
        }

        void StartGame(int index)
        {
            EndBackdrop();
            LogLeave();
            StopAllCoroutines();
            Time.timeScale = 1f;
            bool fromStage = _game != null && _game.OwnStage;
            if (_game != null) _game.End();
            Hud.SetTeeSign(0, 0);
            Release(_ball);
            _selected = index;
            _game = _games[index];
            for (int i = 0; i < _games.Length; i++) _games[i].Root.gameObject.SetActive(i == index);
            _flying = false;
            _puttsThisGame = 0;
            _trackLine.positionCount = 0;
            Hud.HideHome();
            Hud.HideSetup();
            Hud.HideOverlay();
            Hud.HidePutt();
            _menu = false;
            Hud.SetMenuButton(true);
            Hud.SetGameName(Hud.Spaced(_game.DisplayName));
            Audio.Hush();
            Audio.SetScene(_game.Sound);
            Audio.UiStart();
            Hud.SmallBanner = _game.SmallBanner;
            _game.Begin(players);
            AppGameStarted?.Invoke(_game, Mathf.Min(players, _game.MaxPlayers)); // puttr-app-2
            _logGame = _game.Name;
            _logPlayers = Mathf.Min(players, _game.MaxPlayers);
            _logSince = Time.unscaledTime;
            var levels = new List<string>();
            for (int i = 0; i < _logPlayers; i++) levels.Add(LevelNames[LevelOf(i)]);
            ArcadeLog.Event("game_start", "game", _logGame, "players", _logPlayers, "level", LevelNames[LevelOf(0)], "levels", string.Join(",", levels), "console", LogConsole());
            // From the home screen's view of the game, cut to where play starts.
            if (_game.OwnStage || _game.OnMat) { _camPos = _game.CameraPosition; _camLook = _game.CameraLook; }
            else { _camPos = IdleCameraPosition; _camLook = IdleCameraLook; }
            Hud.SetScoreVisible(true);
            if (!_game.OwnReadout) Hud.Banner(_game.Name.ToUpperInvariant(), _game.Rule, Color.white, 3f);
            StartCoroutine(NextPutt());
        }

        IEnumerator NextPutt()
        {
            _phase = Phase.Between;
            Hud.SetPrompt("");
            if (_game.DealsTee && !_game.Over) _game.DealTee();
            yield return _game.Prepare();
            yield return new WaitForSecondsRealtime(0.15f);
            _phase = Phase.Ready;
            _idleSince = Time.unscaledTime;
            Shot("ready");
        }

        // The best scores on this device for a game, one to a line.
        string BoardText(GameMode game, int count)
        {
            var text = new System.Text.StringBuilder();
            string unit = game.ScoreUnit.Length > 0 ? " " + game.ScoreUnit : "";
            int place = 1;
            foreach (Leaderboard.Row row in Leaderboard.Top(BoardKey(game, _levels[0]), count))
                text.Append(place++).Append(".   ").Append(row.Name).Append("      ").Append(row.Score).Append(unit).Append(row.Fresh ? "   ·   new" : "").Append('\n');
            return text.ToString();
        }

        // For testing the end card: -puttrEndAfter n ends any game after n putts.
        int _endAfter;
        bool Finished => _game.Over || (_endAfter > 0 && _puttsThisGame >= _endAfter);

        void GameOver()
        {
            _phase = Phase.GameOver;
            _idleSince = Time.unscaledTime;
            _menu = false;
            Hud.SetPrompt("");
            Audio.GameOver();
            _game.Final(out string title, out string body);
            if (!demo && AppGameEnded != null) // puttr-app-2
            {
                var appScores = new int[Mathf.Min(players, _game.MaxPlayers)];
                for (int i = 0; i < appScores.Length; i++) appScores[i] = _game.Score(i);
                AppGameEnded(_game, appScores);
            }
            // Onto this device's leaderboard (not the demo's putts).
            Leaderboard.BeginGame();
            if (!demo && _game.Results(out _) == null)
                for (int i = 0; i < Mathf.Min(players, _game.MaxPlayers); i++)
                {
                    int score = _game.Score(i);
                    if (score > 0) Leaderboard.Add(BoardKey(_game, LevelOf(i)), PlayerName(i), score, _game.LowerIsBetter);
                }
            int count = Mathf.Min(players, _game.MaxPlayers);
            if (_logGame != null)
            {
                var scores = new int[count];
                for (int i = 0; i < count; i++) scores[i] = _game.Score(i);
                ArcadeLog.Event("game_end", "game", _logGame, "players", count, "putts", _puttsThisGame, "seconds", Mathf.RoundToInt(Time.unscaledTime - _logSince),
                    "scores", scores, "level", LevelNames[Level], "lower_is_better", _game.LowerIsBetter, "result", title);
                ArcadeLog.Flush();
                _logGame = null;
            }
            var card = new Hud.Summary
            {
                Icon = _game.Icon, Accent = _game.Accent, Eyebrow = "GAME OVER",
                BoardCaption = "TOP SCORES ON THIS DEVICE",
                Hint = Hud.Choose("Putt to play again", "Putt to play again", "Putt, or press the remote, to play again      ·      Menu for all games"),
                Buttons = new[] { "Play again", Embedded ? "Exit" : "All games" },   // puttr-app-2: Exit goes back to the tab the game came from
            };
            // What the game itself has to add ("3 putts in a row"), without the names it lists for several Players.
            var notes = new List<string>();
            if (count == 1) foreach (string line in body.Split('\n')) if (line.Trim().Length > 0) notes.Add(line.Trim());
            Hud.Line[] sides = _game.Results(out string headline);
            if (sides != null)
            {
                card.Headline = headline;
                card.Players = sides;
                notes.Clear();
            }
            else if (count == 1)
            {
                int score = _game.Score(0);
                card.Figure = score.ToString();
                card.Unit = _game.ScoreWord(score).ToUpperInvariant();
            }
            else
            {
                // Best first decides the winner; level at the top is a draw.
                int best = 0, level = 0;
                for (int i = 1; i < count; i++)
                    if (_game.LowerIsBetter ? _game.Score(i) < _game.Score(best) : _game.Score(i) > _game.Score(best)) best = i;
                for (int i = 0; i < count; i++) if (_game.Score(i) == _game.Score(best)) level++;
                card.Headline = level > 1 ? "A draw" : PlayerName(best) + " wins";
                card.Players = new Hud.Line[count];
                for (int i = 0; i < count; i++)
                {
                    int score = _game.Score(i);
                    bool won = level == 1 && i == best;
                    card.Players[i] = new Hud.Line { Name = PlayerName(i), Value = score + " " + _game.ScoreWord(score), Dot = Hud.PlayerColors[i % Hud.PlayerColors.Length], Lit = won, Tag = won ? "WINNER" : "" };
                }
            }
            card.Notes = notes.ToArray();
            List<Leaderboard.Row> top = Leaderboard.Top(BoardKey(_game, LevelOf(0)), count > 2 ? 3 : 5);
            card.Board = new Hud.Line[top.Count];
            for (int i = 0; i < top.Count; i++) card.Board[i] = new Hud.Line { Name = top[i].Name, Value = top[i].Score + " " + _game.ScoreWord(top[i].Score), Lit = top[i].Fresh };
            Hud.SetTeeSign(0, 0);
            Hud.ShowSummary(card);
            Shot("gameover");
        }

        // In a game: stop and choose what next. The game waits behind the card.
        bool _menu;

        void OpenMenu()
        {
            if (_game == null || _phase == Phase.GameOver) return;
            _menu = true;
            Hud.ShowSummary(new Hud.Summary
            {
                Icon = _game.Icon, Accent = _game.Accent, Eyebrow = "PAUSED", Headline = _game.Name,
                Notes = new[] { "The game is waiting for you.", "Putts are not counted while this is open." },
                Buttons = new[] { "Keep playing", "Start over", Embedded ? "Exit" : "All games" },
            });
        }

        void CloseMenu()
        {
            _menu = false;
            Hud.HideOverlay();
        }

        // ── How wide the goal is ────────────────────────────────────────────
        //
        // Easy: the whole width of the mat counts as the hole. Hard: only the hole does.
        // Medium is between. Every game reads the same PuttResult, so the widening is done
        // once, here, before a game sees the putt: the ball's path is drawn in toward the
        // center line by the ratio of the hole to the goal, and then judged as usual. A
        // putt 6 inches wide on Easy is, to the game, a putt 1 inch off center. Pace is not
        // helped: a putt still has to get there.

        public const int Easy = 0, Medium = 1, Hard = 2;
        public static readonly string[] LevelNames = { "Easy", "Medium", "Hard" };
        public const float HoleInches = 4.25f, MatInches = 24f;
        static readonly float[] GoalInches = { MatInches, 12f, HoleInches };

        /// <summary>How wide the goal is at this level, in inches across the mat.</summary>
        public float Goal => GoalInches[Level];

        /// <summary>Easy, Medium or Hard, for every game. Starts on Easy each time the arcade opens.</summary>
        /// <summary>The level of whoever is putting now. Each Player has their own, so a good
        /// putter and a beginner can play the same game.</summary>
        public int Level => LevelOf(_game != null ? _game.Putter : 0);

        int LevelOf(int player) => (_game ?? _games[_selected]).HasLevels ? _levels[Mathf.Clamp(player, 0, 3)] : Hard;

        PuttResult _truePutt; // the putt as it really was, for the putt card and the log

        /// <summary>This putt as it really was, before Easy or Medium drew it in toward the hole.
        /// For a game that maps where the ball went onto its own picture of the mat.</summary>
        public PuttResult TruePutt(PuttResult given) => _truePutt ?? given;

        /// <summary>Half the width of the mat, in inches: its edge is this far from the center line.</summary>
        public const float MatHalfInches = MatInches * 0.5f;

        PuttResult Widen(PuttResult putt)
        {
            if (_game.OwnLevels || Level == Hard || putt.Path == null || putt.Path.Count < 2) return putt;
            float squeeze = HoleInches / GoalInches[Level];
            Hole hole = putt.Hole;
            var path = new List<BallPoint>(putt.Path.Count + 3);
            foreach (BallPoint p in putt.Path)
            {
                var q = new BallPoint(Mathf.RoundToInt(hole.X + (p.X - hole.X) * squeeze), p.Y, p.R);
                path.Add(q);
                // Over the hole, it drops: a ball that would have run on past the side of the
                // real hole is not asked to have been going slowly enough to fall in.
                float dx = q.X - hole.X, dy = q.Y - hole.Y;
                if (dx * dx + dy * dy < hole.Radius * hole.Radius * 0.8f)
                {
                    for (int i = 0; i < 3; i++) path.Add(new BallPoint(Mathf.RoundToInt(hole.X + dx * 0.5f), Mathf.RoundToInt(hole.Y + dy * 0.5f), p.R));
                    break;
                }
            }
            PuttResult wide = PuttrAnalysis.Analyze(path, hole, _console.distanceFt, _console.tee, _console.framesPerSecond);
            return wide != null && wide.Valid ? wide : putt;
        }

        /// <summary>A game's top scores are kept apart for each level. Hard keeps the plain name,
        /// which is what scores were saved under before there were levels.</summary>
        string BoardKey(GameMode game, int level) => !game.HasLevels || level == Hard ? game.Name : game.Name + " (" + LevelNames[level] + ")";

        void OnPutt(PuttResult putt)
        {
            if (!isActiveAndEnabled) return; // puttr-app-2: the app is using the console for something else
            if (AppJudge != null && _game != null && !AppJudge(putt, _game.PuttFeet > 0 ? _game.PuttFeet : 6, _game.PuttTee > 0 ? _game.PuttTee : 2)) return; // puttr-app-2
            if (_typingHost || _menu) return;
            _idleSince = Time.unscaledTime;
            switch (_phase)
            {
                case Phase.Home:
                    // Where the putt went picks the game: left, center or right.
                    // Left or right of the hole browses; the middle plays.
                    if (!putt.Launch.Reached) break;
                    float line = putt.Launch.LineAtHoleInches;
                    if (line < -1f) Select(_selected - 1);
                    else if (line > 1f) Select(_selected + 1);
                    else OpenSetup(_selected);
                    break;
                case Phase.Setup:
                    // Short of the hole adds a Player; anything else starts the game.
                    if (!putt.Launch.Reached)
                    {
                        if (players < _games[_selected].MaxPlayers) AddPlayer(false); else { players = 1; _nameFocus = 0; }
                        ShowSetup();
                        break;
                    }
                    LeaveSetup();
                    StartGame(_selected);
                    break;
                case Phase.GameOver:
                    StartGame(_selected);
                    break;
                case Phase.Ready:
                    if (AppAcceptsPutts != null && !AppAcceptsPutts()) break; // puttr-app-2
                    _truePutt = putt;
                    ReplayAim(putt);
                    StartCoroutine(PlayPutt(Widen(putt)));
                    break;
            }
        }

        // ── One putt ────────────────────────────────────────────────────────

        IEnumerator PlayPutt(PuttResult putt)
        {
            _phase = Phase.Rolling;
            _puttsThisGame++;
            LogPutt(putt);
            Hud.SetPrompt("");
            if (_game.OwnStage)
            {
                // Nothing to replay: the game takes the putt's numbers.
                yield return _game.Play(putt);
                Audio.SetRoll(0);
                if (Finished) GameOver();
                else yield return NextPutt();
                yield break;
            }
            if (_game.SkipMat)
            {
                // No mat and no hole on screen: the putt goes straight into the game, placed by
                // where it really crossed, measured against the goal for this level.
                ShowPutt(putt);
                PuttLaunch real = (_truePutt ?? putt).Launch;
                Ball sent = null;
                float across = real.LineAtHoleInches / (Goal * 0.5f);
                if (real.Reached)
                {
                    sent = _ball = TakeBall();
                    _game.Send(sent, across, real.SpeedInPerSec);
                    _flying = true;
                    Shot("launch");
                    yield return _game.Run(sent, putt, Mathf.Abs(across) <= 0.34f);
                }
                Audio.SetRoll(0);
                yield return _game.Finish(sent, putt, sent != null && Mathf.Abs(across) <= 1f);
                _flying = false;
                _ball = null;
                if (Finished) GameOver();
                else yield return NextPutt();
                yield break;
            }
            PuttLaunch launch = putt.Launch;
            _offEnd = false;
            _trackLine.positionCount = 0;
            _trackFade = 1f;
            if (!_game.OwnReadout) ShowPutt(putt);
            Ball ball = _ball = TakeBall();

            // 1. Replay the camera frames on the ramp, one frame every 1/fps
            //    seconds: this is the real ball, as the console saw it.
            // puttr-app-2: in the app the path is eased first, and the ball is carried along a curve
            // through the frames, not in straight steps from one to the next.
            Vector2[] pts = Embedded ? Eased(launch.ApproachInches) : launch.ApproachInches;
            float dt = 1f / Mathf.Max(20f, _console.framesPerSecond);
            int n = pts.Length;
            Vector3 center = BallCenter(n > 0 ? pts[n - 1] : new Vector2(0, 3f));
            Vector3 previous = center;
            if (n >= 2)
            {
                const float lead = 4f; // frames of run-in before the camera's first sighting
                int drawn = 0;
                for (float t = -lead * dt; t < (n - 1) * dt; t += Time.deltaTime)
                {
                    float f = t / dt;
                    Vector2 p;
                    if (f < 0) p = pts[0] + (pts[0] - pts[1]) * -f;
                    else
                    {
                        int i = Mathf.Min(n - 2, (int)f);
                        p = Embedded ? Curve(pts, i, f - i) : Vector2.Lerp(pts[i], pts[i + 1], f - i);
                    }
                    previous = center;
                    center = BallCenter(p);
                    ball.ReplayAt(center);
                    Audio.SetRoll(launch.SpeedInPerSec);
                    while (drawn < n && drawn <= f) Trace(drawn++, pts);
                    yield return null;
                }
                while (drawn < n) Trace(drawn++, pts);
                previous = BallCenter(pts[n - 2]);
                center = BallCenter(pts[n - 1]);
                ball.ReplayAt(center);
            }

            float speed = launch.HandoffSpeedInPerSec;
            if (n < 2) speed = putt.HasSpeed ? putt.SpeedInPerSec : 36f;
            bool arrived = false;

            if (_game.OnMat && launch.Reached)
            {
                // The game is on the mat: the ball is its to carry on.
                yield return _game.RollOnMat(ball, putt, n > 0 ? pts[n - 1] : new Vector2(0, 3f), speed);
                Audio.SetRoll(0);
                yield return _game.Finish(ball, putt, putt.Made);
                _ball = null;
                if (Finished) GameOver();
                else yield return NextPutt();
                yield break;
            }

            if (!launch.Reached)
            {
                // Left short. The ramp won: let it roll back down.
                _game.Called(putt); // puttr-app-2
                Vector3 back = n >= 2 ? (center - previous) / dt : new Vector3(0, 0, -10f);
                ball.Launch(center, back);
                float until = Time.time + 1.4f;
                while (Time.time < until && ball.transform.position.z > LaneSpec.CurveStartZ - 6f) yield return null;
            }
            else if (putt.Made)
            {
                // 2a. Holed: into the cup, and out behind the console.
                yield return DropInCup(ball, putt, center, speed);
                _game.Called(putt); // puttr-app-2
                arrived = true;
            }
            else
            {
                // 2b. Missed: hand the ball to the physics where the camera
                //     left it, and let the ramp decide.
                // puttr-app-2: a lip-out (or a ball that hopped the cup) goes over the hole first: it
                // catches the rim, rides it and spins away, or flies the gap and bounces off the far
                // edge (CupPhysics). Then the ramp has it, as before.
                CupPhysics.Run lip = putt.MissByInches <= 0.01f
                    ? CupPhysics.LipOut(center, launch.HeadingDeg, Mathf.Max(speed, 2f), putt.MissSide == MissSide.Left ? -1f : putt.MissSide == MissSide.Right ? 1f : Mathf.Sign(launch.LineAtHoleInches))
                    : null;
                if (lip != null)
                {
                    yield return PlayCup(ball, lip, null);
                    ball.Launch(lip.Centers[lip.Centers.Count - 1], lip.ExitVelocity);
                }
                else
                {
                    float heading = ClearOfHole(center, launch.HeadingDeg);
                    ball.Launch(center, LaneSpec.SurfaceDirection(heading, center.z) * Mathf.Max(speed, 2f));
                }
                _ringColor = Make.Red;
                _ringFlash = 0.7f;
                _game.Called(putt); // puttr-app-2
                yield return WaitForRamp(ball);
                arrived = _offEnd;
            }

            // 3. The game takes it from here.
            if (arrived)
            {
                if (!_game.WatchesMat) { _flying = true; Shot("launch"); } // puttr-app-2: or the camera stays on the mat
                yield return _game.Run(ball, putt, putt.Made);
            }
            Audio.SetRoll(0);
            if (!arrived) Release(ball);
            yield return _game.Finish(arrived ? ball : null, putt, putt.Made);
            _flying = false;
            _ball = null;

            if (Finished) GameOver();
            else yield return NextPutt();
        }

        // puttr-app-2: the camera's fix on the ball wanders by a fraction of an inch from frame to
        // frame, which reads as a jagged roll. Two passes of a 1-2-1 average take that out; the first
        // and last frames stay where they were, so the ball still starts and is handed on exactly
        // where the camera saw it.
        static Vector2[] Eased(Vector2[] pts)
        {
            if (pts == null || pts.Length < 4) return pts;
            var from = (Vector2[])pts.Clone();
            var to = (Vector2[])pts.Clone();
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < from.Length - 1; i++) to[i] = (from[i - 1] + from[i] * 2f + from[i + 1]) * 0.25f;
                Vector2[] swap = from; from = to; to = swap;
                System.Array.Copy(from, to, from.Length);
            }
            return from;
        }

        // A Catmull-Rom curve through the frames: between frame i and i + 1, t from 0 to 1.
        static Vector2 Curve(Vector2[] pts, int i, float t)
        {
            Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Length - 1, i + 2)];
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        // A holed putt: on to where the ball really crossed the lip and into the
        // cup, and out from under the end of the mat with the speed it reached
        // the hole with.
        // puttr-app-2: the last inches are simulated (CupPhysics): from the handoff toward the measured
        // entry point, the ball topples over the lip when slow, flies the gap and rattles off the far
        // wall when quick, drops with gravity and rolls out of the back of the cup, as the real Puttr's
        // through-hole does. The speed it comes out behind the console with is worked out as before.
        IEnumerator DropInCup(Ball ball, PuttResult putt, Vector3 from, float speed)
        {
            PuttLaunch launch = putt.Launch;
            Vector2 measured = putt.HasEntry ? PuttrAnalysis.ToInchesIntegrated(putt.EntryPoint, putt.Hole) : new Vector2(Mathf.Clamp(launch.LineAtHoleInches, -1.5f, 1.5f), 0.5f);
            Vector2 entry = Vector2.ClampMagnitude(putt.HasEntry ? measured : Vector2.zero, LaneSpec.HoleRadius - 0.95f);
            Vector3 lip = LaneSpec.OnSurface(entry, LaneSpec.BallRadius);
            float distance = Vector3.Distance(from, lip);

            bool rang = false;
            System.Action drop = () =>
            {
                if (rang) return;
                rang = true;
                Audio.Holed();
                _ringColor = Make.Bright;
                _ringFlash = 1f;
            };
            CupPhysics.Run run = CupPhysics.Holed(from, measured, launch.HeadingDeg, speed);
            if (run != null)
            {
                yield return PlayCup(ball, run, drop);
                ball.PutAway();
                Audio.SetRoll(0);
                drop();
            }
            else
            {
                // Never seen (a run always drops once it is slow enough), but just in case: roll to the lip and fall.
                float rolled = 0f, v = Mathf.Max(speed, 10f);
                while (rolled < distance)
                {
                    v = Mathf.Max(8f, v - PuttrAnalysis.RampDecelInPerSec2 * Time.deltaTime);
                    rolled += v * Time.deltaTime;
                    ball.ReplayAt(Vector3.Lerp(from, lip, Mathf.Clamp01(rolled / distance)));
                    Audio.SetRoll(v);
                    yield return null;
                }
                Vector3 forward = (lip - from).normalized;
                Vector3 down = -LaneSpec.SurfaceNormal(0f);
                for (float t = 0; t < 1f; t += Time.deltaTime / 0.14f)
                {
                    ball.ReplayAt(lip + forward * (0.7f * t) + down * (3.2f * t * t));
                    yield return null;
                }
                ball.PutAway();
                Audio.SetRoll(0);
                drop();
            }

            // Through the console.
            yield return new WaitForSeconds(0.22f);

            float out2 = speed * speed - 2f * PuttrAnalysis.RampDecelInPerSec2 * distance;
            float outSpeed = Mathf.Max(_game.HoledMinSpeed, Mathf.Sqrt(Mathf.Max(0f, out2)));
            float tan = Mathf.Tan(launch.HeadingDeg * Mathf.Deg2Rad);
            float z = LaneSpec.LaneHeadZ + 1f;
            float x = Mathf.Clamp(launch.LineAtHoleInches + _game.HoledOffsetX + tan * z, -LaneSpec.LaneHalfWidth + 1f, LaneSpec.LaneHalfWidth - 1f);
            float h = launch.HeadingDeg * Mathf.Deg2Rad;
            ball.Launch(new Vector3(x, LaneSpec.LaneHeadY + LaneSpec.BallRadius + 0.02f, z), new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * outSpeed);
            ball.Trail.Clear();
        }

        // puttr-app-2: play a CupPhysics run back in game time (so slow motion slows it too), with a
        // knock for each hit on the rim or the cup's wall and the roll under the carpet's part of it.
        IEnumerator PlayCup(Ball ball, CupPhysics.Run run, System.Action onDrop)
        {
            int last = run.Centers.Count - 1;
            if (last < 0) yield break;
            Quaternion start = ball.transform.rotation;
            int knock = 0;
            bool dropped = false;
            for (float t = 0f; ; t += Time.deltaTime)
            {
                float f = t / CupPhysics.Step;
                int i = Mathf.Min(last, (int)f), j = Mathf.Min(last, i + 1);
                float u = Mathf.Clamp01(f - i);
                ball.Pose(Vector3.Lerp(run.Centers[i], run.Centers[j], u), Quaternion.Slerp(run.Spins[i], run.Spins[j], u) * start);
                if (!_game.QuietBall) Audio.SetRoll(run.Speeds[i]);
                while (knock < run.Knocks.Count && run.Knocks[knock] <= i) Audio.Thump(run.KnockSpeeds[knock++]);
                if (!dropped && run.DropAt >= 0 && i >= run.DropAt) { dropped = true; onDrop?.Invoke(); }
                if (i >= last) yield break;
                yield return null;
            }
        }

        // A missed ball does not roll across the hole. If its line would take
        // it over the cup (a lip-out), turn it just enough to pass the lip on
        // the side it is already on, which is what the lip did to it.
        static float ClearOfHole(Vector3 from, float headingDeg)
        {
            float atHole = from.x + Mathf.Tan(headingDeg * Mathf.Deg2Rad) * (0f - from.z);
            float clear = LaneSpec.HoleRadius + LaneSpec.BallRadius * 0.45f;
            if (Mathf.Abs(atHole) >= clear || from.z >= 0f) return headingDeg;
            float target = (atHole >= 0f ? 1f : -1f) * clear;
            return Mathf.Clamp(Mathf.Atan2(target - from.x, -from.z) * Mathf.Rad2Deg, -35f, 35f);
        }

        // A missed ball on the ramp: does it run off the far end, or come back?
        IEnumerator WaitForRamp(Ball ball)
        {
            float start = Time.time, deadFor = 0f;
            _offEnd = false;
            while (Time.time - start < 6f)
            {
                yield return null;
                Vector3 p = ball.transform.position;
                Vector3 v = ball.Body.linearVelocity;
                if (!_game.QuietBall) Audio.SetRoll(v.magnitude); // puttr-app-2
                if (p.z > LaneSpec.RampEndZ + 0.2f) { _offEnd = true; yield break; }
                if (p.z < LaneSpec.InclineStartZ && v.z < 0f) yield break;
                deadFor = v.magnitude < 0.8f ? deadFor + Time.deltaTime : 0f;
                if (deadFor > 0.5f) yield break;
            }
        }

        /// <summary>Where the ball's center is when it sits on the surface at this console position.</summary>
        static Vector3 BallCenter(Vector2 inches)
        {
            // The camera only sees the ramp, but a made-up run-in can start on the flat.
            inches.y = Mathf.Min(inches.y, -LaneSpec.MatStartZ - 2f);
            return LaneSpec.OnSurface(inches, LaneSpec.BallRadius);
        }

        // Extend the line on the carpet to this camera frame.
        void Trace(int index, Vector2[] pts)
        {
            _trackLine.positionCount = index + 1;
            _trackLine.SetPosition(index, LaneSpec.OnSurface(pts[index], 0.06f));
        }

        // The last putt, in three numbers.
        void ShowPutt(PuttResult putt)
        {
            // Speed and line are the putt as it really was; the result is what the game made of it.
            PuttLaunch launch = (_truePutt ?? putt).Launch;
            string speed = PaceWords.Of(_truePutt ?? putt);

            float lineIn = launch.LineAtHoleInches;
            string line = Mathf.Abs(lineIn) < 0.15f ? "Center" : Mathf.Abs(lineIn).ToString("0.0") + " in " + (lineIn < 0 ? "left" : "right");

            string result;
            Color color;
            if (!launch.Reached) { result = "Short"; color = Make.Amber; }
            else if (putt.Made) { result = Sentence(putt.EntryDescription); color = Make.Bright; }
            else if (putt.MissByInches <= 0.01f) { result = "Lipped out"; color = Make.Amber; }
            else { result = putt.MissSide == MissSide.Left ? "Missed left" : putt.MissSide == MissSide.Right ? "Missed right" : "Missed"; color = Make.Red; }
            Hud.SetPutt(speed, line, result, color);
        }

        // "CENTER CUT" to "Center cut".
        static string Sentence(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Holed";
            return char.ToUpperInvariant(text[0]) + text.Substring(1).ToLowerInvariant();
        }

        // ── Every frame ─────────────────────────────────────────────────────

        // From where the Player stands: the console, and the game through it.
        static readonly Vector3 IdleCameraPosition = new Vector3(0f, 17f, -46f);
        static readonly Vector3 IdleCameraLook = new Vector3(0f, 0f, 2f);
        // Just behind the console, under the level of its hood.
        static readonly Vector3 Doorway = new Vector3(0f, 9f, LaneSpec.RampEndZ + 2.6f);

        void Update()
        {
            if (!_ready) return;
            if (_backdrop) { Audio.Tick(Time.unscaledDeltaTime); _backdropMode?.Tick(); return; }   // puttr-app-2: no input under the app's page
            if (TraceNav && _traceEvents != _traceEventsSaid) { _traceEventsSaid = _traceEvents; Debug.Log("Puttr Arcade nav events so far: " + _traceEvents + "  keyboard " + (Keyboard.current != null ? Keyboard.current.displayName : "none") + "  gamepad " + (Gamepad.current != null ? Gamepad.current.displayName : "none")); }
            HandleTaps();
#if UNITY_TVOS
            // Apple's rule: Menu leaves the app from the first screen, and only from there.
            UnityEngine.tvOS.Remote.allowExitToHome = _phase == Phase.Home;
#endif
            HandleRemote();
            HandleKeys();
            if (_settingsShotAt > 0f && Time.unscaledTime > _settingsShotAt) { _settingsShotAt = 0f; Shot("settings"); }
            if (demo && _phase != Phase.Settings) RunDemo();
            UpdateStatus();
            string link = LogConsole();
            if (link != _logConsole) { _logConsole = link; if (link != "none") ArcadeLog.Event("console_connected", "console", link, "firmware", _console.Version); }
            Hud.Tick(Time.unscaledDeltaTime);
            _aim?.Tick(Time.unscaledDeltaTime);
            Audio.Tick(Time.unscaledDeltaTime);
            if (_game != null) _game.Tick();
            else if (_phase == Phase.Home || _phase == Phase.Setup) _games[_selected].Tick();

            // Slow motion eases in and back out again.
            float targetScale = Time.unscaledTime < _slowUntil ? 0.22f : 1f;
            Time.timeScale = Mathf.MoveTowards(Time.timeScale, targetScale, Time.unscaledDeltaTime * (targetScale < 1f ? 12f : 1.6f));

            // The ring around the hole lights when a ball arrives.
            _ringFlash = Mathf.MoveTowards(_ringFlash, 0f, Time.unscaledDeltaTime * 1.1f);
            Color ring = _ringColor * (2.4f * _ringFlash);
            ring.a = Mathf.Clamp01(_ringFlash * 1.5f);
            World.HoleRing.SetColor("_Color", ring);

            // The traced path fades once the putt is over.
            if (_phase != Phase.Rolling) _trackFade = Mathf.MoveTowards(_trackFade, 0f, Time.unscaledDeltaTime * 0.6f);
            Color trace = Make.Bright;
            trace.a = 0.85f * _trackFade;
            _trackMaterial.SetColor("_Color", trace);

            // Left alone at the end of a game, go back to the home screen.
            if (_phase == Phase.GameOver && !demo && Time.unscaledTime - _idleSince > 30f) ShowHome();
        }

        // Every set is framed for a 16:9 screen. On a squarer one (an iPad is
        // 4:3) the lens is widened until just as much shows from side to side,
        // so nothing is cut off; the extra is above and below.
        float Fitted(float fov, GameMode view = null)
        {
            // puttr-app-2: a game with its own upright layout frames itself.
            if (view != null && view.HasPortrait && GameMode.Portrait) return fov;
            const float wide = 16f / 9f;
            float aspect = _camera.aspect;
            if (aspect >= wide) return fov;
            return 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * wide / aspect) * Mathf.Rad2Deg;
        }

        // puttr-app-2: the console's outside, for framing it: half its width, its front, its top and the floor it stands on.
        const float ConsoleHalfWidth = LaneSpec.MatHalfWidth + 1.1f, ConsoleFrontZ = -11f, ConsoleTop = 14f;
        /// <summary>How much of the width of the game's part of the screen the console fills on a game that watches the mat.</summary>
        public static float MatFill = 0.94f;

        // The lens (vertical, degrees) that makes the console's front fill MatFill of the picture's width, from here, looking there.
        float MatFov(Vector3 from, Vector3 at)
        {
            Quaternion facing = Quaternion.Inverse(Quaternion.LookRotation(at - from));
            float floor = LaneSpec.MatY - 0.3f, widest = 0.01f;
            foreach (float y in new[] { floor, ConsoleTop })
                foreach (float z in new[] { ConsoleFrontZ, LaneSpec.RampEndZ })
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 p = facing * (new Vector3(side * ConsoleHalfWidth, y, z) - from);
                        if (p.z > 1f) widest = Mathf.Max(widest, Mathf.Abs(p.x) / p.z);
                    }
            float tanHalfWide = widest / Mathf.Max(0.2f, MatFill);
            return Mathf.Clamp(2f * Mathf.Atan(tanHalfWide / Mathf.Max(0.2f, _camera.aspect)) * Mathf.Rad2Deg, 10f, 90f);
        }

        void LateUpdate()
        {
            if (!_ready) return;
            if (_backdrop && _backdropMode != null) { OrbitBackdrop(); return; }
            // Once the ball is through, the camera goes through the console after it.
            // On the home and setup screens the highlighted game is on show.
            bool browsing = _phase == Phase.Home || _phase == Phase.Setup;
            GameMode view = _game != null ? _game : browsing ? _games[_selected] : null;
            bool stage = view != null && (view.OwnStage || view.OnMat || view.SkipMat || view.WatchesMat || browsing);
            // puttr-app-2: a game may be drawn in part of the screen, with the app's panel in the rest.
            Rect port = _game != null ? _game.Viewport : new Rect(0f, 0f, 1f, 1f);
            if (_camera.rect != port) _camera.rect = port;
            bool action = stage || (_flying && _game != null);
            Vector3 wantPos = action ? view.CameraPosition : IdleCameraPosition;
            Vector3 wantLook = action ? view.CameraLook : IdleCameraLook;
            // puttr-app-2: a game that watches the mat (Free putt, and the rule games drawn on the Puttr)
            // holds one framing the whole time, putts and all: no following, no zoom. The lens is set
            // so the console fills most of the width of the game's part of the screen (MatFov).
            bool fixedMat = action && view.WatchesMat;
            // The only way between the room and the hall is under the hood of
            // the console, so the camera goes by way of the gap, both ways.
            bool inHall = _camPos.z > Doorway.z - 0.6f;
            if (!stage && action && !inHall) wantPos = Doorway;
            if (!stage && !action && inHall && _camPos.y > Doorway.y + 2.5f) wantPos = Doorway;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * (action ? 5.5f : 2.6f));
            _camPos = Vector3.Lerp(_camPos, wantPos, k);
            _camLook = Vector3.Lerp(_camLook, wantLook, k);
            _shake = Mathf.MoveTowards(_shake, 0f, Time.unscaledDeltaTime * 1.4f);
            Vector3 jitter = _shake > 0.001f ? Random.insideUnitSphere * _shake : Vector3.zero;
            // Never quite still: a slow drift, as if hand held.
            float now = Time.unscaledTime;
            if (!fixedMat) jitter += new Vector3(Mathf.Sin(now * 0.31f) * 0.12f, Mathf.Sin(now * 0.23f + 1.3f) * 0.08f, 0f);
            _camera.transform.position = _camPos + jitter;
            // Coming back from the hall the camera backs out through the
            // console. It keeps facing the way it was: the point it looks at
            // is never allowed behind it, or it would spin round on the way.
            Vector3 look = _camLook;
            if (!stage) look.z = Mathf.Max(look.z, _camPos.z + 10f);
            _camera.transform.LookAt(look);
            // Behind the home screen the game sits to the right, clear of the words.
            if (_phase == Phase.Home) _camera.transform.Rotate(0f, -_camera.fieldOfView * 0.3f, 0f, Space.Self);
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fixedMat ? MatFov(_camPos, _camLook) : Fitted(action ? view.CameraFov : 38f, action ? view : null), k);

            // Focus on the hole while waiting, on the ball while it is on the
            // ramp, and on the game once it is through.
            Vector3 subject = Vector3.zero;
            if (action) subject = view.CameraLook;
            else if (_phase == Phase.Rolling && _ball != null && _ball.gameObject.activeSelf) subject = _ball.transform.position;
            float want = Mathf.Max(4f, Vector3.Dot(subject - _camPos, _camera.transform.forward));
            _focusDistance = Mathf.Lerp(_focusDistance, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6f));
            _focus.focusDistance.value = _focusDistance;
            // A stage is far bigger than the console, so its lens is set by
            // distance: sharp out to the action, soft behind it.
            _focus.mode.value = stage ? DepthOfFieldMode.Gaussian : DepthOfFieldMode.Bokeh;
            if (stage)
            {
                _focus.gaussianStart.value = view.BlurFrom;
                _focus.gaussianEnd.value = view.BlurTo;
                _focus.gaussianMaxRadius.value = 1f;
                _focus.highQualitySampling.value = true;
            }
            // Shadows reach as far as the set does.
            float reach = stage ? view.ShadowDistance : ConsoleShadows;
            if (_pipeline != null && !Mathf.Approximately(_pipeline.shadowDistance, reach)) _pipeline.shadowDistance = reach;
        }

        void UpdateStatus()
        {
            // In a game, say nothing about the console when all is well. On
            // the home screen, say which one it is and how to change it.
            bool home = _phase == Phase.Home;
            string more = home && Hud.Kind == Hud.Device.Desktop ? "      ·      S for settings" : "";
            if (_phase == Phase.Settings) Hud.SetStatus(null, Make.Bright);
            else if (_console.Online)
            {
                if (!_console.HoleCalibrated) Hud.SetStatus("Clear the hole and restart your Puttr", Make.Red);
                else Hud.SetStatus(home ? _console.Link + more : null, Make.Bright);
            }
            else if (_console.State == ConsoleState.Connecting || _console.Bluetooth.State == BluetoothState.Connecting) Hud.SetStatus("Connecting to your Puttr" + more, Make.Amber);
            else Hud.SetStatus("Looking for your Puttr" + more, Make.Amber);
            if (_phase == Phase.Ready && !_typingHost) { Hud.SetPrompt(_game.Prompt); Hud.SetTee(_game.Tee); }
        }

        // ── Fingers and the mouse ───────────────────────────────────────────

        TouchScreenKeyboard _keys;
        bool _down, _dragging, _onRail;
        Vector2 _downAt, _lastAt;
        float _dragSpeed, _dragFromX;
        readonly List<Vector2> _trail = new List<Vector2>(); // (time, x) of the finger over the last moments
        string _held;
        long _doneTouch = -1;
        const bool TraceTaps = false;

        void HandleTaps()
        {
            // A name being typed on a phone's own keyboard.
            if (_keys != null)
            {
                // While it is up, and once more when Done is pressed: a keyboard
                // that takes the whole screen (Apple TV) only hands its text over then.
                if (_phase == Phase.Setup && (_keys.status == TouchScreenKeyboard.Status.Visible || _keys.status == TouchScreenKeyboard.Status.Done))
                {
                    var clean = new System.Text.StringBuilder();
                    foreach (char c in _keys.text) if ((char.IsLetterOrDigit(c) || c == ' ') && clean.Length < 12) clean.Append(c);
                    if (clean.ToString() != Names[_nameFocus]) { Names[_nameFocus] = clean.ToString(); ShowSetup(); }
                    if (_keys.status == TouchScreenKeyboard.Status.Done) _keys = null;
                }
                else _keys = null;
            }

            // An address being typed on the device's keyboard.
            if (_addressKeys != null && _addressKeys.status != TouchScreenKeyboard.Status.Visible)
            {
                if (_addressKeys.status == TouchScreenKeyboard.Status.Done) UseAddress(_addressKeys.text);
                _addressKeys = null;
                if (_phase == Phase.Settings) ShowSettings();
            }

            // A press only counts when the finger comes up where it went
            // down. One that travels is a drag: it slides the row of games,
            // or on the home screen flicks to the next game.
            if (_typingHost) return;
#if UNITY_TVOS
            // An Apple TV has no screen to touch. Its remote's touch surface can still come in
            // as touches (and a pointer) placed on the screen, so every click on it also landed
            // as a tap there: on the setup screen, on Let's play, whatever was highlighted.
            // The remote is read as a controller in HandleRemote, and only there.
            return;
#else
            bool began, ended;
            Vector2 at, start;
            if (ETouch.activeTouches.Count > 0)
            {
                // A finger. Its own start point is used: the pointer's position
                // on the frame a touch begins can still be the last touch's.
                ETouch touch = ETouch.activeTouches[0];
                // A touch that has ended can be listed for more than one frame: take it once.
                long id = touch.touchId * 100000L + (long)(touch.startTime * 1000.0) % 100000L;
                if (id == _doneTouch) return;
                at = touch.screenPosition;
                start = touch.startScreenPosition;
                ended = touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                began = !_down; // a quick tap can begin and end before it is seen
                if (ended) _doneTouch = id;
                if (TraceTaps && (began || ended)) Debug.Log("[tap] touch " + touch.touchId + " " + touch.phase + " at " + at + " start " + start + " began " + began + " n " + ETouch.activeTouches.Count);
            }
            else if (Mouse.current != null && Hud.Kind != Hud.Device.Touch)
            {
                at = start = Mouse.current.position.ReadValue();
                began = Mouse.current.leftButton.wasPressedThisFrame;
                ended = !Mouse.current.leftButton.isPressed;
                if (!began && !_down) return;
            }
            else
            {
                if (!_down) return;
                at = _lastAt; start = _downAt; began = false; ended = true; // the finger has gone
            }
            float slop = Mathf.Max(12f, Screen.dpi * 0.07f);
            if (began)
            {
                _down = true;
                _dragging = false;
                _downAt = _lastAt = start;
                _dragSpeed = 0f;
                _onRail = _phase == Phase.Home && Hud.OnRail(start);
                if (_onRail) Hud.HoldRail();
                _held = Hud.Press(start);
                _idleSince = Time.unscaledTime;
            }
            if (began) _trail.Clear();
            if (!_dragging && (at - _downAt).magnitude > slop) { _dragging = true; _dragFromX = at.x; Hud.LetGo(); }
            if (_dragging && _onRail && !began)
            {
                Hud.DragRail(at.x - _dragFromX);
                // How fast it is let go is read across the last tenth of a second, not from
                // one frame: a finger often twitches back as it lifts, and frames and touches
                // do not arrive in step.
                float now = Time.unscaledTime;
                if (!ended && (_trail.Count == 0 || _trail[_trail.Count - 1].y != at.x)) _trail.Add(new Vector2(now, at.x));
                while (_trail.Count > 1 && now - _trail[0].x > 0.1f) _trail.RemoveAt(0);
                _dragSpeed = 0f;
                if (_trail.Count > 1)
                {
                    Vector2 first = _trail[0], last = _trail[_trail.Count - 1];
                    bool resting = now - last.x > 0.07f; // held still before lifting
                    if (!resting && last.x - first.x > 0.015f) _dragSpeed = (last.y - first.y) / (last.x - first.x);
                }
            }
            _lastAt = at;
            if (!ended) return;
            _down = false;
            Hud.LetGo();
            if (_onRail) Hud.FlingRail(_dragging ? _dragSpeed : 0f);
            if (_dragging)
            {
                Vector2 swipe = _lastAt - _downAt;
                bool across = Mathf.Abs(swipe.x) > slop * 4f && Mathf.Abs(swipe.x) > Mathf.Abs(swipe.y) * 1.5f;
                if (!_onRail && _phase == Phase.Home && across) Select(_selected + (swipe.x < 0f ? 1 : -1));
                else if (_phase == Phase.Ready && !_menu && across && _game != null) _game.Nudge(swipe.x < 0f ? 1 : -1);
                return;
            }
            string hit = _held;
            if (TraceTaps) Debug.Log("[tap] up, held " + hit + " now " + Hud.TapAt(_lastAt) + " dragging " + _dragging + " phase " + _phase);
            if (hit == null || Hud.TapAt(_lastAt) != hit) return;
            _idleSince = Time.unscaledTime;
            string[] part = hit.Split(':');
            int n = part.Length > 1 && int.TryParse(part[1], out int parsed) ? parsed : 0;

            if (_menu)
            {
                if (part[0] != "button") return;
                if (n == 0) CloseMenu();
                else if (n == 1) StartGame(_selected);
                else ShowHome();
                return;
            }

            switch (_phase)
            {
                case Phase.Home:
                    if (part[0] == "tile") { if (n == _selected) OpenSetup(n); else Select(n); }
                    else if (hit == "play") OpenSetup(_selected);
                    else if (hit == "status" || hit == "settings") OpenSettings();
                    break;
                case Phase.Setup:
                    if (hit == "add") { AddPlayer(true); ShowSetup(); }
                    else if (part[0] == "level") { _zone = ZoneNames; _nameFocus = n; _col = ColLevel; CycleLevel(n); ShowSetup(); }
                    else if (part[0] == "remove") { RemovePlayer(n); ShowSetup(); }
                    else if (part[0] == "name")
                    {
                        _nameFocus = n;
                        _zone = ZoneNames;
                        _col = ColName;
                        if (TouchScreenKeyboard.isSupported) _keys = TouchScreenKeyboard.Open(Names[n], TouchScreenKeyboardType.Default, false, false, false, false, "Player name", 12);
                        ShowSetup();
                    }
                    else if (hit == "start") { LeaveSetup(); StartGame(_selected); }
                    else if (hit == "back" || hit == "brand") { LeaveSetup(); ShowHome(); }
                    break;
                case Phase.Settings:
                    if (part[0] == "console" && part.Length > 2 && int.TryParse(part[2], out int row)) { _settingsColumn = n; _settingsRow = row; if (!ChooseConsole()) ShowSettings(); }
                    else if (hit == "back" || hit == "brand") ShowHome();
                    break;
                case Phase.GameOver:
                    if (hit == "button:0") StartGame(_selected);
                    else if (hit == "button:1") ShowHome();
                    break;
                default:
                    if (hit == "menu" || hit == "brand") OpenMenu();
                    break;
            }
#endif
        }

        // ── A game controller, or the Apple TV remote ───────────────────────
        //
        // Left, right, up, down; one button to choose and one to go back.
        // That is everything an Apple TV has, and it is enough.

        // ── One swipe, one step, whichever way it arrives ───────────────────
        // A TV remote reaches the app twice (as a controller and as arrow keys), and its touch
        // surface can swing back the other way as the finger lifts. On the setup screen any
        // pair of steps that cancel (down then up, or a repeat that wraps) lands back on
        // Let's play, so the highlight never seemed to move. So: a step the other source
        // reports soon after is the same swipe, and a step straight back is the swing.
        const float SameSwipe = 0.4f, SwingBack = 0.35f;
        Vector2Int _navDir;
        int _navSource = -1;
        float _navAt = -9f;
        bool _traceMoving;
        int _traceEvents, _traceEventsSaid;
        const bool TraceNav = true; // true: each step (and the remote's raw stick and dpad) to the log, for Xcode's console

        bool NavGate(int source, Vector2Int dir, string what)
        {
            float now = Time.unscaledTime, since = now - _navAt;
            string why = null;
            if (source != _navSource && since < SameSwipe) why = "same swipe, other source";
            else if (dir == -_navDir && since < SwingBack) why = "swing back";
            if (TraceNav) Debug.Log("Puttr Arcade nav: " + (source == 0 ? "remote " : "keys ") + what + (why == null ? "" : "  (ignored: " + why + ")") + "  " + _phase);
            if (why != null) return false;
            _navDir = dir; _navSource = source; _navAt = now;
            return true;
        }

        bool _padHeldX, _padHeldY;
        int _padLastX, _padLastY;
        float _padAtX = -9f, _padAtY = -9f;

        /// <summary>One step, -1 or 1, when the stick is first pushed well out along this axis.</summary>
        static int PadStep(float value, ref bool held, ref int last, ref float at)
        {
            // "at" is when the stick was last away from rest. After a step nothing more counts
            // until it has sat at rest for a moment: a swipe that swings back past the middle
            // and out the other side is still moving, so it cannot count as a step back.
            float push = Mathf.Abs(value), now = Time.unscaledTime;
            if (held)
            {
                if (push > 0.2f) at = now;
                else if (now - at > 0.22f) held = false;
                return 0;
            }
            if (push < 0.5f) return 0;
            held = true;
            at = now;
            last = value > 0f ? 1 : -1;
            return last;
        }

        void HandleRemote()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null || _typingHost) return;
            // The Apple TV remote's touch surface arrives as a stick that follows the finger. A
            // swipe pushes it one way and, as the finger lifts, it can swing back through the
            // middle and out the other side, which read as a second move the wrong way: the
            // row of games jumped on and came back. So one swipe is one step: the stick has to
            // come back to rest before it counts again, and a step the opposite way straight
            // after one is ignored.
            Vector2 d = pad.dpad.ReadValue(), stick = pad.leftStick.ReadValue();
            if (TraceNav)
            {
                // Every start of movement and every press, whether or not it becomes a step.
                bool movingNow = d.sqrMagnitude > 0.09f || stick.sqrMagnitude > 0.09f;
                string buttons = (pad.buttonSouth.wasPressedThisFrame ? " south" : "") + (pad.buttonEast.wasPressedThisFrame ? " east" : "") + (pad.buttonWest.wasPressedThisFrame ? " west" : "")
                    + (pad.buttonNorth.wasPressedThisFrame ? " north" : "") + (pad.startButton.wasPressedThisFrame ? " start" : "") + (pad.selectButton.wasPressedThisFrame ? " select" : "")
                    + (pad.dpad.up.wasPressedThisFrame ? " dpad-up" : "") + (pad.dpad.down.wasPressedThisFrame ? " dpad-down" : "") + (pad.dpad.left.wasPressedThisFrame ? " dpad-left" : "") + (pad.dpad.right.wasPressedThisFrame ? " dpad-right" : "");
                if ((movingNow && !_traceMoving) || buttons.Length > 0)
                    Debug.Log("Puttr Arcade nav input: " + pad.displayName + "  dpad " + d.ToString("0.00") + "  stick " + stick.ToString("0.00") + (buttons.Length > 0 ? "  pressed" + buttons : "") + "  " + _phase);
                _traceMoving = movingNow;
            }
            // The Siri Remote's touch surface arrives as both. The stick follows the thumb; the
            // dpad snaps to eight directions and lands on diagonals easily: a swipe down read
            // stick (0.24, -0.97) but dpad (0.71, -0.71), and taking the dpad made it "right".
            // So the stick decides whenever it is moving; the dpad only when it is at rest.
            bool stickMoving = stick.sqrMagnitude > 0.09f;
            if (stickMoving) d = stick;
            // A quick click on a TV remote's direction pad can be down and up again inside one
            // frame, so the pad reads as at rest by the time it is looked at. The press itself
            // is still reported: count it as a full push that way (not while the stick is moving:
            // those presses are the same diagonal-prone dpad).
            if (!stickMoving)
            {
                if (pad.dpad.left.wasPressedThisFrame) d.x = -1f; else if (pad.dpad.right.wasPressedThisFrame) d.x = 1f;
                if (pad.dpad.down.wasPressedThisFrame) d.y = -1f; else if (pad.dpad.up.wasPressedThisFrame) d.y = 1f;
            }
            int stepX = PadStep(d.x, ref _padHeldX, ref _padLastX, ref _padAtX);
            int stepY = PadStep(d.y, ref _padHeldY, ref _padLastY, ref _padAtY);
            if (stepX != 0 && stepY != 0) { if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) stepY = 0; else stepX = 0; } // a swipe goes one way
            bool left = stepX < 0, right = stepX > 0, up = stepY > 0, down = stepY < 0;
            if (left || right || up || down)
            {
                if (TraceNav) Debug.Log("Puttr Arcade nav raw: " + pad.displayName + " (" + pad.layout + ")  dpad " + pad.dpad.ReadValue().ToString("0.00") + "  stick " + pad.leftStick.ReadValue().ToString("0.00"));
                var dir = new Vector2Int(stepX, stepY);
                if (!NavGate(0, dir, left ? "left" : right ? "right" : up ? "up" : "down")) left = right = up = down = false;
            }
            bool choose = pad.buttonSouth.wasPressedThisFrame;
            bool back = pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame;
            bool other = pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame;
            // A TV remote can arrive twice: as this controller and as arrow keys and Enter on a
            // keyboard. Both used to act, so every swipe moved the highlight two places (on a
            // one-Player setup screen, off Let's play and straight back onto it) and every
            // press counted twice. A press the keyboard has just taken is not taken again.
            if (Time.unscaledTime - _keysNavAt < 0.2f) left = right = up = down = choose = back = false;
            if (left || right || up || down || choose || back) _remoteNavAt = Time.unscaledTime;
            if (!(left || right || up || down || choose || back || other)) return;
            _idleSince = Time.unscaledTime;

            switch (_phase)
            {
                case Phase.Home:
                    if (left) Select(_selected - 1);
                    else if (right) Select(_selected + 1);
                    else if (choose) OpenSetup(_selected);
                    else if (up || other) OpenSettings();
                    break;
                case Phase.Setup:
                    if (down) SetupMove(1);
                    if (up) SetupMove(-1);
                    if (left) SetupSide(-1);
                    if (right) SetupSide(1);
                    if (choose) { if (SetupPress()) break; }
                    else if (back) { LeaveSetup(); ShowHome(); break; }
                    ShowSetup();
                    break;
                case Phase.Settings:
                    if (down) _settingsRow++;
                    if (up) _settingsRow = Mathf.Max(0, _settingsRow - 1);
                    if (left && _consoleRows[0].Count > 0) { _settingsColumn = 0; _settingsRow = 0; }
                    if (right && _consoleRows[1].Count > 0) { _settingsColumn = 1; _settingsRow = 0; }
                    if (back) ShowHome();
                    else if (!choose || !ChooseConsole()) ShowSettings();
                    break;
                case Phase.GameOver:
                    if (choose) StartGame(_selected);
                    else if (back) ShowHome();
                    break;
                default:
                    if (back) ShowHome();
                    else if (_phase == Phase.Ready && _game != null && (left || right)) _game.Nudge(left ? -1 : 1);
                    break;
            }
        }

        // ── Keyboard (nothing here is shown on screen) ──────────────────────

        static readonly Key[] Digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8 };

        float _remoteNavAt = -9f, _keysNavAt = -9f; // when the controller and the keyboard last moved or chose

        void HandleKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // The other half of the remote arriving twice (see HandleRemote): arrows, Enter and
            // Escape that the controller has just acted on are left alone here.
            bool nav = kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame
                || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame;
            if (nav && !_typingHost)
            {
                if (Time.unscaledTime - _remoteNavAt < 0.2f) return;
                var arrow = new Vector2Int(kb.leftArrowKey.wasPressedThisFrame ? -1 : kb.rightArrowKey.wasPressedThisFrame ? 1 : 0,
                    kb.upArrowKey.wasPressedThisFrame ? 1 : kb.downArrowKey.wasPressedThisFrame ? -1 : 0);
                if (arrow != Vector2Int.zero && !NavGate(1, arrow, arrow.x < 0 ? "left" : arrow.x > 0 ? "right" : arrow.y > 0 ? "up" : "down")) return;
                _keysNavAt = Time.unscaledTime;
            }

            if (_typingHost)
            {
                bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                if (enter || kb.escapeKey.wasPressedThisFrame)
                {
                    _typingHost = false;
                    if (enter) UseAddress(_hostText);
                    Hud.HideOverlay();
                    if (_phase == Phase.GameOver) GameOver();
                }
                else if (kb.backspaceKey.wasPressedThisFrame && _hostText.Length > 0)
                {
                    _hostText = _hostText.Substring(0, _hostText.Length - 1);
                    ShowHostEntry();
                }
                return;
            }

            if (_phase == Phase.Settings)
            {
                if (kb.escapeKey.wasPressedThisFrame) { ShowHome(); return; }
                int count = _consoleRows[_settingsColumn].Count;
                if (kb.downArrowKey.wasPressedThisFrame) _settingsRow = Mathf.Min(_settingsRow + 1, Mathf.Max(0, count - 1));
                if (kb.upArrowKey.wasPressedThisFrame) _settingsRow = Mathf.Max(0, _settingsRow - 1);
                if (kb.leftArrowKey.wasPressedThisFrame && _consoleRows[0].Count > 0) { _settingsColumn = 0; _settingsRow = 0; }
                if (kb.rightArrowKey.wasPressedThisFrame && _consoleRows[1].Count > 0) { _settingsColumn = 1; _settingsRow = 0; }
                if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && _settingsRow < count)
                {
                    if (ChooseConsole()) return;
                }
                if (kb.anyKey.wasPressedThisFrame && !kb.hKey.wasPressedThisFrame) ShowSettings();
                if (!kb.hKey.wasPressedThisFrame) return;
            }

            if (_phase == Phase.Setup)
            {
                if (kb.escapeKey.wasPressedThisFrame) { LeaveSetup(); ShowHome(); return; }
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { if (SetupPress()) return; }
                if (kb.leftArrowKey.wasPressedThisFrame) SetupSide(-1);
                if (kb.rightArrowKey.wasPressedThisFrame) SetupSide(1);
                if (kb.downArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) SetupMove(1);
                if (kb.upArrowKey.wasPressedThisFrame) SetupMove(-1);
                if (TypingName && !TouchScreenKeyboard.isSupported && kb.backspaceKey.wasPressedThisFrame && Names[_nameFocus].Length > 0) Names[_nameFocus] = Names[_nameFocus].Substring(0, Names[_nameFocus].Length - 1);
                _nameFocus = Mathf.Min(_nameFocus, players - 1);
                if (kb.anyKey.wasPressedThisFrame) ShowSetup();
                return;
            }

            if (kb.hKey.wasPressedThisFrame)
            {
                _typingHost = true;
                _swallowText = true;
                _hostText = _console.host ?? "";
                ShowHostEntry();
                return;
            }

            if (_phase == Phase.Home)
            {
                if (kb.escapeKey.wasPressedThisFrame) { Application.Quit(); return; }
                if (kb.leftArrowKey.wasPressedThisFrame) Select(_selected - 1);
                if (kb.rightArrowKey.wasPressedThisFrame) Select(_selected + 1);
                if (kb.sKey.wasPressedThisFrame) { OpenSettings(); return; }
                // An Android TV remote arrives as arrow keys, Enter and Escape (its Back button).
                if (Hud.Kind == Hud.Device.Tv && kb.upArrowKey.wasPressedThisFrame) { OpenSettings(); return; }
                if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) { OpenSetup(_selected); return; }
                for (int i = 0; i < Mathf.Min(_games.Length, Digits.Length); i++)
                    if (kb[Digits[i]].wasPressedThisFrame) { OpenSetup(i); return; }
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame || kb.mKey.wasPressedThisFrame) { ShowHome(); return; }
            if (kb.rKey.wasPressedThisFrame) { StartGame(_selected); return; }
            if (_phase == Phase.GameOver && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { StartGame(_selected); return; }
            if (_phase == Phase.Ready && _game != null && !_menu)
            {
                if (kb.leftArrowKey.wasPressedThisFrame) _game.Nudge(-1);
                if (kb.rightArrowKey.wasPressedThisFrame) _game.Nudge(1);
            }

            // Putts without a console. They go through the same path as a
            // real one: PuttrConsole.Inject, then the Putt event.
            Hole hole = _console.HasHole ? _console.Hole : Hole.Typical;
            float fps = _console.framesPerSecond;
            if (kb.spaceKey.wasPressedThisFrame)
            {
                if (_phase == Phase.GameOver) { StartGame(_selected); return; }
                _console.Inject(RandomPutt(hole, fps), true);
            }
            for (int i = 0; i < Digits.Length; i++)
                if (kb[Digits[i]].wasPressedThisFrame) _console.Inject(PuttrSamples.Recorded[i].Path, true);
            if (kb.zKey.wasPressedThisFrame) _console.Inject(PuttrSamples.Synthesize(0.4f, -14f, 0f, hole, fps), true);
            if (kb.cKey.wasPressedThisFrame) _console.Inject(PuttrSamples.Synthesize(-0.3f, 17f, 0f, hole, fps), true);
            if (kb.vKey.wasPressedThisFrame) _console.Inject(PuttrSamples.Synthesize(0.75f, 60f, 0.5f, hole, fps), true);
            if (kb.xKey.wasPressedThisFrame) _console.Inject(PuttrSamples.Hand(hole), true);
        }

        // A putt a decent Player might hit: mostly near the middle, at a sensible pace.
        List<BallPoint> RandomPutt(Hole hole, float fps)
        {
            float line = (float)(_rng.NextDouble() + _rng.NextDouble() + _rng.NextDouble() - 1.5) * 1.5f;
            float speed = 30f + (float)_rng.NextDouble() * 22f;
            return PuttrSamples.Synthesize(line, speed, line * 0.6f, hole, fps);
        }

        void ShowHostEntry()
        {
            Hud.ShowOverlay("C O N S O L E",
                "Type your Puttr's name or address, then press Enter.\n" +
                "Leave it empty to search for it.\n\n" +
                _hostText + "_");
        }

        void OnTextInput(char c)
        {
            if (_phase == Phase.Setup && !_typingHost)
            {
                // Where the device has its own keyboard, the name comes from
                // that keyboard's text box. The same letters also arrive here,
                // one by one or all at once when it closes: taking both wrote
                // every name twice.
                if (TouchScreenKeyboard.isSupported) return;
                // A name: letters, figures and spaces, twelve at most.
                // (The key that opened this screen arrives here too: ignore it.)
                if (Time.unscaledTime - _idleSince < 0.2f) return;
                if (TypingName && (char.IsLetterOrDigit(c) || c == ' ') && Names[_nameFocus].Length < 12) { Names[_nameFocus] += c; ShowSetup(); }
                return;
            }
            if (!_typingHost) return;
            // The H that opened the box arrives here too.
            if (_swallowText) { _swallowText = false; if (c == 'h' || c == 'H') return; }
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == ':' || c == '_')
            {
                if (_hostText.Length < 60) _hostText += c;
                ShowHostEntry();
            }
        }

        // ── Demo: the arcade plays itself with made-up putts ────────────────

        void RunDemo()
        {
            if (_typingHost) return;
            if (Time.unscaledDeltaTime > 0.5f) _idleSince = Time.unscaledTime; // a long first frame is not idling
            float idle = Time.unscaledTime - _idleSince;
            if (_phase == Phase.Home)
            {
                if (idle > 1f && _shotIndex == 0) Shot("home");
                if (idle > 3f) OpenSetup(_selected);
                return;
            }
            if (_phase == Phase.Setup)
            {
                if (idle > 1f && !_setupShot) { _setupShot = true; Shot("setup"); }
                if (idle > 2.5f) { _setupShot = false; LeaveSetup(); StartGame(_selected); }
                return;
            }
            if (_phase == Phase.GameOver && idle > 5f) { _selected = (_selected + 1) % _games.Length; ShowHome(); return; }
            if (_phase != Phase.Ready || idle < 1.4f) return;
            _idleSince = Time.unscaledTime;

            // -puttrDemoPutts n: move on to the next game after n putts.
            if (_puttsThisGame >= _demoPutts) { _selected = (_selected + 1) % _games.Length; ShowHome(); return; }

            Hole hole = _console.HasHole ? _console.Hole : Hole.Typical;
            float fps = _console.framesPerSecond;
            // On Fire ends at the first miss, so the demo holes nine there to let the fire build.
            if (_game is OnFireMode && _puttsThisGame < 9) { _console.Inject(PuttrSamples.Synthesize(0.2f, 40f, 0f, hole, fps), true); return; }
            // One of each kind first, then putts a decent Player might hit.
            switch (_demoStep++)
            {
                case 0: _console.Inject(PuttrSamples.Synthesize(0.05f, 42f, 0f, hole, fps), true); break;
                case 1: _console.Inject(PuttrSamples.Synthesize(0.3f, -14f, 0f, hole, fps), true); break;
                case 2: _console.Inject(PuttrSamples.Synthesize(4.6f, 46f, 1.5f, hole, fps), true); break;
                case 3: _console.Inject(PuttrSamples.Hand(hole), true); break;
                default: _console.Inject(RandomPutt(hole, fps), true); break;
            }
        }
    }
}
