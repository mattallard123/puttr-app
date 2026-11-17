// puttr-app-2: the 3D worlds for the app's rule games (Tour Juice, HORSE, Gears...).
//
// The rules live in the app (PuttrApp RuleGames*.cs): they deal the putt, score it and keep the game's state. A
// RuleScene only shows it: its own 3D set, and what the putt just did in it. RuleStageMode is the arcade game that
// hosts a scene in the top of the screen, above the app's panel (as FreePuttMode does), and plays each putt there.
//
// For each putt the app scores it first (Arcade.AppPutt fires before the stage plays), then hands the stage a
// ShotView: what happened and the game's state after it. The scene animates that, then shows what comes next.
//
// Writing a scene: subclass RuleScene, build everything under Root in local coordinates (inches are a good unit;
// the stage sits far from the console), point the camera with CameraFrom / CameraLook, animate in Play. Read the
// game's own state from view.Data (each game's View fills it: see the app's RuleViews.cs).
using System;
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    /// <summary>A putt as the app's rules called it, and the game after it.</summary>
    public sealed class ShotView
    {
        // The putt
        public int Player, Feet, Tee;
        public bool Made, Good, BackDoor;
        public double Points;
        public string Title = "", Sub = "";
        public bool HasEntry; public float EntryDeg;          // degrees off straight in, - left, + right
        public float OffLineDeg;                              // |entry - the tee's perfect line|
        public bool HasRollout; public float RolloutIn;        // inches past (+) or short (-) of the cup
        public float LineIn;                                  // inches right (+) or left (-) of centre as it reached the hole
        public bool Reached;                                  // the ball got to the hole
        public string MissSide = "";                          // "LEFT", "RIGHT" or ""
        public List<Vector2> Path = new List<Vector2>();      // the roll as the camera saw it: inches right, inches past the cup (+) / short (-)
        // The game now
        public int Next, NextFeet, NextTee;                   // who putts next, and from where
        public bool Over;
        public string[] Names = new string[0];
        public double[] Scores = new double[0];
        public string[] ScoreTexts = new string[0];
        public string Progress = "";
        public readonly Dictionary<string, object> Data = new Dictionary<string, object>();

        public int Int(string key, int fallback = 0) => Data.TryGetValue(key, out object v) && v != null ? Convert.ToInt32(v) : fallback;
        public float Num(string key, float fallback = 0f) => Data.TryGetValue(key, out object v) && v != null ? Convert.ToSingle(v) : fallback;
        public bool Flag(string key) => Data.TryGetValue(key, out object v) && v is bool b && b;
        public string Str(string key, string fallback = "") => Data.TryGetValue(key, out object v) && v != null ? v.ToString() : fallback;
        public T Get<T>(string key) where T : class => Data.TryGetValue(key, out object v) ? v as T : null;
    }

    /// <summary>One rule game's 3D world.</summary>
    public abstract class RuleScene
    {
        public Arcade Arcade;
        /// <summary>Everything the scene builds goes under here. Local coordinates; it is placed far from the console.</summary>
        public Transform Root;

        public abstract Vector3 CameraFrom { get; }
        public abstract Vector3 CameraLook { get; }
        public virtual float CameraFov => 40f;
        public virtual float ShadowDistance => 300f;

        /// <summary>Build the set. Called once, before Begin.</summary>
        public abstract void Build();
        /// <summary>A new game: lay the set out for the state the game starts in (Next, NextFeet, NextTee, Data).</summary>
        public virtual void Begin(ShotView start) { }
        /// <summary>Show what the putt just did. view has the result and the state after it.</summary>
        public abstract IEnumerator Play(ShotView view);
        /// <summary>After Play: set up for the next putt (the state is the same view).</summary>
        public virtual void Ready(ShotView view) { }
        /// <summary>The game's state changed without a putt (a shot called, a cup picked): show it.</summary>
        public virtual void Changed(ShotView view) { }
        /// <summary>Every frame.</summary>
        public virtual void Tick() { }

        // ── Building blocks (as StageMode's) ─────────────────────────────────

        protected Vector3 At(float x, float y, float z) => Root.TransformPoint(new Vector3(x, y, z));
        protected Vector3 At(Vector3 local) => Root.TransformPoint(local);

        protected static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Make.Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        protected static GameObject Hoop(string name, Transform parent, Vector3 position, float inner, float outer, Material material)
        {
            GameObject go = Make.MeshObject(name, parent, Make.Ring(inner, outer, 96), material);
            go.transform.localPosition = position;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go; // flat in the XZ plane: rotate it to stand up
        }

        static Font _font;
        protected static TextMesh Words(Transform parent, string text, Vector3 position, float height, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, int resolution = 96)
        {
            if (_font == null) _font = Resources.Load<Font>("PuttrArcade/Fonts/Montserrat-Bold");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Words");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var words = go.AddComponent<TextMesh>();
            words.font = _font;
            words.fontSize = resolution;
            words.characterSize = height * 10f / resolution * 1.4f;
            words.anchor = anchor;
            words.alignment = TextAlignment.Center;
            words.color = color;
            words.text = text;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _font.material;
            renderer.sortingOrder = 10;
            return words;
        }

        protected static LineRenderer Stroke(Transform parent, string name, Color color, float width, bool additive = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Make.Glow(color, additive);
            line.widthMultiplier = width;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.positionCount = 0;
            return line;
        }

        protected static void Points(LineRenderer line, IList<Vector3> pts) { line.positionCount = pts.Count; for (int i = 0; i < pts.Count; i++) line.SetPosition(i, pts[i]); }

        protected static Light Sun(Transform parent, Vector3 euler, Color color, float intensity)
        {
            Light sun = World.NewLight(parent, "Stage light", LightType.Directional, color, intensity);
            sun.transform.localRotation = Quaternion.Euler(euler);
            return sun;
        }

        protected static Light Lamp(Transform parent, Vector3 at, Vector3 lookAt, Color color, float intensity, float angle = 60f, float range = 400f)
        {
            Light lamp = World.NewLight(parent, "Lamp", LightType.Spot, color, intensity);
            lamp.transform.localPosition = at;
            lamp.transform.localRotation = Quaternion.LookRotation(lookAt - at);
            lamp.spotAngle = angle;
            lamp.range = range;
            return lamp;
        }

        protected static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>Move something along a parabola over seconds (apex in units above the straight line).</summary>
        protected static IEnumerator Arc(Transform thing, Vector3 from, Vector3 to, float seconds, float apex)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, seconds))
            {
                thing.localPosition = Vector3.Lerp(from, to, t) + Vector3.up * (4f * apex * t * (1f - t));
                yield return null;
            }
            thing.localPosition = to;
        }

        /// <summary>The library sound for a slot (Soundly SoundDrop), one take at random.</summary>
        protected void Sound(string slot, float volume = 0.9f, float pitch = 1f) { if (Arcade != null) Arcade.Audio.Library(slot, volume, pitch); }
    }

    /// <summary>The arcade game that hosts a RuleScene above the app's panel.</summary>
    public sealed class RuleStageMode : StageMode
    {
        public const string Title = "Rule Stage";
        /// <summary>Set by the app before each game: the scene to build, its starting state, its bed.</summary>
        public static Func<RuleScene> NextScene;
        public static ShotView StartView;
        public static string Bed;
        /// <summary>The putt the app just scored (set from Arcade.AppPutt, before Play).</summary>
        public static ShotView Pending;
        /// <summary>The app's label for the top bar ("Tour Juice").</summary>
        public static string Label;

        RuleScene _scene;
        Transform _sceneRoot;

        public override string Name => Title;
        public override string DisplayName => string.IsNullOrEmpty(Label) ? Title : Label;
        public override string Blurb => "";
        public override string Rule => "";
        public override string Prompt => "";
        public override int MaxPlayers => 4;
        public override bool HasLevels => false;
        public override bool DealsTee => false;
        public override string Tee => null;
        public override bool ShowsAim => false;
        public override bool OwnReadout => true;
        public override bool QuietBall => true;
        public override bool HasPortrait => true;
        public override int PuttFeet => StartView != null ? StartView.NextFeet : 6;
        public override int PuttTee => StartView != null ? StartView.NextTee : 2;
        public override Rect Viewport => Portrait ? new Rect(0f, 1f - FreePuttMode.UprightShare, 1f, FreePuttMode.UprightShare) : new Rect(0f, 0f, FreePuttMode.WideShare, 1f);
        public override Vector3 CameraPosition => _scene != null ? _scene.CameraFrom : At(0, 50, -100);
        public override Vector3 CameraLook => _scene != null ? _scene.CameraLook : At(0, 0, 0);
        public override float CameraFov => _scene != null ? _scene.CameraFov : 40f;
        public override float ShadowDistance => _scene != null ? _scene.ShadowDistance : 300f;

        public static RuleStageMode Instance;
        public override void Build() { Instance = this; }
        public override void Preview() { }

        public override void Begin(int players)
        {
            // Off at once (Destroy waits for the frame's end): the card pass builds one scene after another in a frame.
            if (_sceneRoot != null) { _sceneRoot.gameObject.SetActive(false); UnityEngine.Object.Destroy(_sceneRoot.gameObject); }
            _scene = NextScene != null ? NextScene() : null;
            if (_scene == null) return;
            _sceneRoot = new GameObject("Scene").transform;
            _sceneRoot.SetParent(Root, false);
            _scene.Root = _sceneRoot;
            _scene.Arcade = Arcade;
            _scene.Build();
            if (StartView != null) _scene.Begin(StartView);
            if (Arcade != null) Arcade.Audio.GameBed(Bed, 0.28f);
        }

        public override void End()
        {
            if (Arcade != null) Arcade.Audio.GameBed(null);
            if (_sceneRoot != null) { _sceneRoot.gameObject.SetActive(false); UnityEngine.Object.Destroy(_sceneRoot.gameObject); }
            _scene = null; _sceneRoot = null;
        }

        /// <summary>The app changed the state without a putt (HORSE's called shot, Beer Pong's cup).</summary>
        public void Changed(ShotView view) { StartView = view; _scene?.Changed(view); }

        public override void Tick() { _scene?.Tick(); }

        public override IEnumerator Play(PuttResult putt)
        {
            ShotView view = Pending;
            Pending = null;
            if (_scene == null || view == null) yield break;
            yield return _scene.Play(view);
            StartView = view;
            _scene.Ready(view);
        }

        public override bool Over => false; // the app ends the game and leaves
        public override void Final(out string title, out string body) { title = ""; body = ""; }
    }
}
