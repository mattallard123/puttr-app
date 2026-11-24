// A live 3D card: a small scene of its own, drawn by its own camera into a RenderTexture that is the
// background of a UI Toolkit element. Built for the Improve tab's Arena hub (today's session over the mat,
// the stroke replay, the PQS ring, the last 30 days in columns), usable by any screen.
//
//   var view = new Live3D(stage =>
//   {
//       stage.Background = new Color32(14, 26, 17, 255);
//       stage.Light(new Color(1f, 0.97f, 0.92f), 1.6f, new Vector3(50, -30, 0));
//       Transform ball = stage.Primitive(PrimitiveType.Sphere, "Ball", stage.Lit(Color.white)).transform;
//       stage.Tick = dt => { ball.Rotate(0, 90 * dt, 0); stage.Orbit(Vector3.zero, 0.3f, 0.4f, 10f); return false; };
//   });
//   view.style.height = 200;      // size it like any element
//
// What keeps it cheap:
// - The scene exists only while the element is on a panel: it is built on attach and destroyed (objects,
//   materials, meshes, the texture) on detach, so a hidden tab or a pushed screen costs nothing.
// - Its camera is off unless the element is shown and on screen (no ancestor display:none, inside the
//   window, no game running), and then draws at most Fps times a second, only on frames the app draws.
// - The texture is the element's size, at most MaxScale pixels a unit (2x).
// - Tick returns true while something worth a smooth frame rate moves; only then does it Perf.Wake the app.
//   The first 1.5 s on screen always wake (the intro). Otherwise it follows the app's idle rate.
// - Every scene sits far below the arcade's world (y = -20000, beyond every game camera's far plane) on its
//   own layer (29), each in its own slot 4000 units apart, so no camera sees another's scene.
// - Its lights are on only while its own camera draws (they are directional, so they would light everything).
using System;
using System.Collections.Generic;
using PuttrArcade;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class Live3D : VisualElement
    {
        public const int Layer = 29;
        public const float Depth = -20000f;
        const float SlotSpacing = 4000f;

        /// <summary>Highest frame rate the scene draws at.</summary>
        public int Fps = 30;
        /// <summary>Texture pixels per UI unit, at most.</summary>
        public float MaxScale = 2f;
        /// <summary>Added to the scene's yaw by a horizontal drag (radians). Read it in Tick.</summary>
        public float Yaw;
        public bool Draggable;

        Action<Stage> _build;
        Stage _stage;
        RenderTexture _texture;
        bool _dirty, _wasVisible, _dragging;
        float _lastRender = -10f, _visibleSince, _lastX;

        // What it costs, for perf.log when it goes away.
        int _renders;
        float _attachedAt;
        public string Name = "Live 3D";

        public Live3D(Action<Stage> build = null)
        {
            _build = build;
            AddToClassList("live3d");
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Teardown());
            RegisterCallback<PointerDownEvent>(e => { _dragging = true; _lastX = e.position.x; }, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging || !Draggable) return;
                if ((e.pressedButtons & 1) == 0) { _dragging = false; return; }
                Yaw += (e.position.x - _lastX) * 0.012f;
                _lastX = e.position.x;
                _dirty = true;
                Perf.Wake(0.4f);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ => _dragging = false, TrickleDown.TrickleDown);
            RegisterCallback<PointerLeaveEvent>(_ => _dragging = false);
        }

        /// <summary>The scene, while the element is on a panel.</summary>
        public Stage Scene => _stage;

        /// <summary>A new scene: the old one goes now and this one is built (now if on a panel, else on attach).</summary>
        public void Rebuild(Action<Stage> build)
        {
            _build = build;
            if (panel == null) return;
            Teardown();
            Attach();
        }

        /// <summary>Draw again on the next frame the app draws (something changed that Tick does not move).</summary>
        public void Invalidate() => _dirty = true;

        void Attach()
        {
            if (_build == null || _stage != null || panel == null) return;
            Runner.Ensure();
            _stage = new Stage(this, Runner.TakeSlot());
            try { _build(_stage); }
            catch (Exception e) { Debug.LogException(e); }
            _stage.Seal();
            Runner.Views.Add(this);
            _dirty = true;
            _wasVisible = false;
            _renders = 0;
            _attachedAt = Time.unscaledTime;
        }

        void Teardown()
        {
            Runner.Views.Remove(this);
            if (_stage != null)
            {
                // The scene's objects can already be gone (the app is closing, or its scene went first): then there is nothing to count.
                if (_renders > 0 && _stage.Root != null) Perf.Note("live3d " + Name + ": " + _renders + " frames in " + Mathf.RoundToInt(Time.unscaledTime - _attachedAt) + " s at " + (_texture != null ? _texture.width + "x" + _texture.height : "-") + ", " + _stage.Root.GetComponentsInChildren<Renderer>().Length + " draws");
                Runner.FreeSlot(_stage.Slot);
                _stage.Destroy();
                _stage = null;
            }
            style.backgroundImage = StyleKeyword.None;
            if (_texture != null) { _texture.Release(); UnityEngine.Object.Destroy(_texture); _texture = null; }
        }

        bool Visible()
        {
            if (panel == null || Perf.InGame) return false;
            for (VisualElement e = this; e != null; e = e.parent)
            {
                IResolvedStyle s = e.resolvedStyle;
                if (s.display == DisplayStyle.None || s.visibility == Visibility.Hidden || s.opacity <= 0.01f) return false;
            }
            Rect r = worldBound;
            if (float.IsNaN(r.width) || r.width < 4f || r.height < 4f) return false;
            return r.Overlaps(panel.visualTree.worldBound);
        }

        /// <summary>Once a frame, from the runner (LateUpdate).</summary>
        internal void Step()
        {
            if (_stage == null) return;
            Camera camera = _stage.Camera;
            if (!Visible())
            {
                camera.enabled = false;
                _wasVisible = false;
                return;
            }
            float now = Time.unscaledTime;
            if (!_wasVisible) { _wasVisible = true; _visibleSince = now; _dirty = true; }
            Size();
            bool due = now - _lastRender >= 1f / Mathf.Max(1, Fps) - 0.004f;
            bool render = OnDemandRendering.willCurrentFrameRender && (_dirty || (_stage.Tick != null && due));
            if (render)
            {
                float dt = Mathf.Clamp(now - _lastRender, 0f, 0.1f);
                _stage.Age = now - _visibleSince;
                _stage.Time += dt;
                bool lively = false;
                if (_stage.Tick != null)
                {
                    try { lively = _stage.Tick(dt); }
                    catch (Exception e) { Debug.LogException(e); _stage.Tick = null; }
                }
                if (lively || _stage.Age < 1.5f) Perf.Wake(0.12f);
                if (_stage.ShiftY != 0f || _stage.ShiftX != 0f)
                {
                    Matrix4x4 p = Matrix4x4.Perspective(camera.fieldOfView, _texture.width / (float)_texture.height, camera.nearClipPlane, camera.farClipPlane);
                    p[0, 2] = -2f * _stage.ShiftX;
                    p[1, 2] = -2f * _stage.ShiftY;
                    camera.projectionMatrix = p;
                }
                _stage.PlacePins(contentRect.width, contentRect.height);
                _lastRender = now;
                _dirty = false;
                _renders++;
            }
            camera.enabled = render;
        }

        void Size()
        {
            float w = resolvedStyle.width, h = resolvedStyle.height;
            float units = panel.visualTree.worldBound.width;
            if (w <= 0 || h <= 0 || units <= 0) return;
            float k = Mathf.Min(Screen.width / units, MaxScale);
            int pw = Mathf.Clamp(Mathf.RoundToInt(w * k), 16, 2048), ph = Mathf.Clamp(Mathf.RoundToInt(h * k), 16, 2048);
            if (_texture != null && Mathf.Abs(_texture.width - pw) <= 4 && Mathf.Abs(_texture.height - ph) <= 4) return;
            RenderTexture old = _texture;
            _texture = new RenderTexture(pw, ph, 24) { antiAliasing = pw * ph > 400000 ? 2 : 4, name = Name };
            _texture.Create();
            _stage.Camera.targetTexture = _texture;
            _stage.Camera.aspect = pw / (float)ph;
            _stage.Camera.ResetProjectionMatrix();
            style.backgroundImage = new StyleBackground(Background.FromRenderTexture(_texture));
            if (old != null) { old.Release(); UnityEngine.Object.Destroy(old); }
            _dirty = true;
        }

        /// <summary>One live scene: its root (far below the world), its camera and lights, and what it owns.</summary>
        public sealed class Stage
        {
            public readonly Live3D View;
            public readonly Transform Root;
            public readonly Camera Camera;
            internal readonly int Slot;
            internal readonly List<Light> Lights = new List<Light>();
            readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
            readonly List<(VisualElement label, Transform at, Vector3 local)> _pins = new List<(VisualElement, Transform, Vector3)>();

            /// <summary>Called before each frame it draws, with the seconds since the last one. True: keep the app drawing smoothly.</summary>
            public Func<float, bool> Tick;
            /// <summary>Seconds since it came on screen this time, and its own clock (it only runs while drawn).</summary>
            public float Age, Time;
            /// <summary>Moves the picture up by this share of its height (a lens shift), so a scene can sit above words laid over the bottom.</summary>
            public float ShiftY;
            /// <summary>Moves the picture right by this share of its width.</summary>
            public float ShiftX;
            /// <summary>Pins lower than this share of the height hide (under words laid over the bottom).</summary>
            public float PinLimit = 1f;

            internal Stage(Live3D view, int slot)
            {
                View = view;
                Slot = slot;
                var go = new GameObject("Live 3D " + view.Name);
                go.transform.SetParent(Runner.Home, false);
                go.transform.position = new Vector3(slot * SlotSpacing, Depth, 0f);
                Root = go.transform;
                var cam = new GameObject("Camera");
                cam.transform.SetParent(Root, false);
                Camera = cam.AddComponent<Camera>();
                Camera.enabled = false;
                Camera.cullingMask = 1 << Layer;
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = new Color32(14, 16, 18, 255);
                Camera.fieldOfView = 40f;
                Camera.nearClipPlane = 0.5f;
                Camera.farClipPlane = 1500f;
                Camera.allowHDR = false;
                Camera.useOcclusionCulling = false;
            }

            public Color Background { get => Camera.backgroundColor; set => Camera.backgroundColor = value; }

            public T Own<T>(T thing) where T : UnityEngine.Object { if (thing != null) _owned.Add(thing); return thing; }

            public Material Lit(Color colour, float smoothness = 0.4f, float metallic = 0f) => Own(Make.Lit(colour, smoothness, metallic));
            public Material Emissive(Color colour, Color emission, float smoothness = 0.4f) => Own(Make.Emissive(colour, emission, smoothness));
            /// <summary>Unlit and see-through: additive for light, alpha blended for paint.</summary>
            public Material Glow(Color colour, bool additive = true, Texture texture = null) => Own(Make.Glow(colour, additive, texture));

            public Transform Group(string name, Transform parent = null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent != null ? parent : Root, false);
                return go.transform;
            }

            public GameObject Box(string name, Vector3 centre, Vector3 size, Material material, Transform parent = null)
            {
                GameObject go = Primitive(PrimitiveType.Cube, name, material, parent);
                go.transform.localPosition = centre;
                go.transform.localScale = size;
                return go;
            }

            public GameObject Primitive(PrimitiveType type, string name, Material material, Transform parent = null)
            {
                GameObject go = GameObject.CreatePrimitive(type);
                go.name = name;
                Collider collider = go.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);
                go.transform.SetParent(parent != null ? parent : Root, false);
                Renderer r = go.GetComponent<Renderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                return go;
            }

            public GameObject Mesh(string name, Mesh mesh, Material material, Transform parent = null)
            {
                Own(mesh);
                var go = new GameObject(name);
                go.transform.SetParent(parent != null ? parent : Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                return go;
            }

            /// <summary>Many pieces with one material as one mesh: one draw instead of one each (grid lines, ticks).</summary>
            public GameObject Merge(string name, List<CombineInstance> parts, Material material, Transform parent = null)
            {
                int verts = 0;
                foreach (CombineInstance part in parts) verts += part.mesh.vertexCount;
                var mesh = new Mesh { name = name, indexFormat = verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.CombineMeshes(parts.ToArray(), true, true);
                return Mesh(name, mesh, material, parent);
            }

            /// <summary>Boxes (centre, size, turn) with one material, merged into one mesh.</summary>
            public GameObject Boxes(string name, List<(Vector3 centre, Vector3 size, Quaternion turn)> boxes, Material material, Transform parent = null)
            {
                var parts = new List<CombineInstance>(boxes.Count);
                foreach (var b in boxes) parts.Add(new CombineInstance { mesh = Cube, transform = Matrix4x4.TRS(b.centre, b.turn, b.size) });
                return Merge(name, parts, material, parent);
            }

            static Mesh _cube;
            /// <summary>Unity's own cube mesh (shared, never destroyed).</summary>
            public static Mesh Cube
            {
                get
                {
                    if (_cube != null) return _cube;
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _cube = go.GetComponent<MeshFilter>().sharedMesh;
                    UnityEngine.Object.Destroy(go);
                    return _cube;
                }
            }

            /// <summary>A directional light for this scene only (on while its camera draws).</summary>
            public Light Light(Color colour, float intensity, Vector3 euler)
            {
                var go = new GameObject("Light");
                go.transform.SetParent(Root, false);
                go.transform.rotation = Quaternion.Euler(euler);
                Light light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = colour;
                light.intensity = intensity;
                light.shadows = LightShadows.None;
                light.cullingMask = 1 << Layer;
                light.enabled = false;
                Lights.Add(light);
                return light;
            }

            /// <summary>Camera on a sphere round target (scene coordinates): yaw 0 looks along +z, pitch up from level.</summary>
            public void Orbit(Vector3 target, float yaw, float pitch, float distance)
            {
                Vector3 eye = target + new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), -Mathf.Cos(yaw) * Mathf.Cos(pitch)) * distance;
                Look(eye, target);
            }

            public void Look(Vector3 eye, Vector3 target)
            {
                Camera.transform.localPosition = eye;
                Camera.transform.LookAt(Root.TransformPoint(target));
            }

            /// <summary>A UI label that follows a point of the scene (the element's child; it goes with the scene).</summary>
            public VisualElement Pin(VisualElement label, Vector3 local, Transform at = null)
            {
                label.AddToClassList("live3d-pin");
                label.pickingMode = PickingMode.Ignore;
                label.style.position = Position.Absolute;
                label.style.left = 0;
                label.style.top = 0;
                View.Add(label);
                _pins.Add((label, at, local));
                return label;
            }

            /// <summary>Where a point of the scene is on the element, in its units (null when behind the camera).</summary>
            public Vector2? Project(Vector3 local, Transform at = null)
            {
                Vector3 world = (at != null ? at : Root).TransformPoint(local);
                Vector3 vp = Camera.WorldToViewportPoint(world);
                if (vp.z <= 0f) return null;
                return new Vector2(vp.x * View.contentRect.width, (1f - vp.y) * View.contentRect.height);
            }

            internal void PlacePins(float w, float h)
            {
                foreach (var pin in _pins)
                {
                    Vector3 world = (pin.at != null ? pin.at : Root).TransformPoint(pin.local);
                    Vector3 vp = Camera.WorldToViewportPoint(world);
                    bool on = vp.z > 0f && 1f - vp.y <= PinLimit && pin.label.style.display != DisplayStyle.None;
                    pin.label.style.visibility = on ? Visibility.Visible : Visibility.Hidden;
                    if (!on) continue;
                    float lw = pin.label.resolvedStyle.width, lh = pin.label.resolvedStyle.height;
                    if (float.IsNaN(lw)) lw = 0;
                    if (float.IsNaN(lh)) lh = 0;
                    pin.label.style.translate = new Translate(Mathf.Round(vp.x * w - lw * 0.5f), Mathf.Round((1f - vp.y) * h - lh * 0.5f));
                }
            }

            /// <summary>After the build: everything on the scene's layer.</summary>
            internal void Seal()
            {
                foreach (Transform t in Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            }

            internal void Destroy()
            {
                foreach (var pin in _pins) pin.label.RemoveFromHierarchy();
                _pins.Clear();
                Lights.Clear();
                if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
                foreach (UnityEngine.Object thing in _owned) if (thing != null) UnityEngine.Object.Destroy(thing);
                _owned.Clear();
            }
        }

        /// <summary>Steps every live view once a frame, and turns each scene's lights on only for its own camera.</summary>
        sealed class Runner : MonoBehaviour
        {
            static Runner _one;
            internal static readonly List<Live3D> Views = new List<Live3D>();
            static readonly List<int> Taken = new List<int>();
            internal static Transform Home => _one.transform;

            internal static void Ensure()
            {
                if (_one != null) return;
                var go = new GameObject("Live 3D");
                DontDestroyOnLoad(go);
                _one = go.AddComponent<Runner>();
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endCameraRendering += End;
            }

            internal static int TakeSlot()
            {
                int slot = 0;
                while (Taken.Contains(slot)) slot++;
                Taken.Add(slot);
                return slot;
            }

            internal static void FreeSlot(int slot) => Taken.Remove(slot);

            void LateUpdate()
            {
                for (int i = Views.Count - 1; i >= 0; i--)
                {
                    if (i >= Views.Count) continue;
                    Views[i].Step();
                }
            }

            static void Lights(Camera camera, bool on)
            {
                foreach (Live3D view in Views)
                    if (view._stage != null && view._stage.Camera == camera)
                        foreach (Light light in view._stage.Lights) if (light != null) light.enabled = on;
            }

            static void Begin(ScriptableRenderContext context, Camera camera) => Lights(camera, true);
            static void End(ScriptableRenderContext context, Camera camera) => Lights(camera, false);
        }
    }
}
