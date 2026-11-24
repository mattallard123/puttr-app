// The realistic look of the Tracks world: scanned turf, rough and sand over the lies' own colours, a photographed
// sky, haze toward the horizon, water that mirrors the sky, a graded picture, and trees and bushes as cut-outs of
// scanned models. TracksWorld builds the same holes either way; where TracksReal.On is false (a device short of
// memory, a web build, or -app2TracksPlain) it keeps the painted look it had before, which needs none of this.
//
// The art is in Resources/PuttrApp/Tracks, baked by Tools/tracks-art/bake.sh from CC0 scans (Poly Haven, ambientCG).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PuttrApp
{
    internal static class TracksReal
    {
        const string Folder = "PuttrApp/Tracks/";
        const int GradeLayer = 31;            // the layer only the Tracks cameras take their grading from
        const float SunU = 0.5945f;           // where the sun is in Sky.jpg, across (bake_ground.py prints it)
        public const float SunElevation = 48.7f;
        const float HazePerYard = 0.0005f;    // a fifth hazed over at 400 yd, three quarters at the horizon

        // The light the sky and the ground give (sRGB), and the horizon's colour the distance fades to (linear, from the panorama).
        static readonly Color SkyLight = new Color(0.50f, 0.56f, 0.68f), HorizonLight = new Color(0.44f, 0.48f, 0.50f), GroundLight = new Color(0.30f, 0.33f, 0.25f);
        static readonly Color Horizon = new Color(0.323f, 0.346f, 0.418f);

        // The lies' own colours under this look, by lie id (0 is the ground outside every shape): what the turf is before the
        // sun, the sky and the scans' grain. Quieter than the painted look's: the light does the rest.
        static readonly Color[] Lies =
        {
            Hex(0x4a6030), Hex(0xbfb087), Hex(0x3f4f29), Hex(0x36432a), Hex(0x47612e),
            Hex(0x5b8239), Hex(0x60893c), Hex(0x679141), Hex(0xd6caa6), Hex(0x3a6078),
        };

        static readonly string[] ScanFiles = { "GroundMown", "GroundMownNormal", "GroundRough", "GroundRoughNormal", "GroundScrub", "GroundScrubNormal", "GroundSand", "GroundSandNormal" };
        static readonly string[] ScanProps = { "_Mown", "_MownN", "_Rough", "_RoughN", "_Scrub", "_ScrubN", "_Sand", "_SandN" };
        static readonly Texture2D[] Scans = new Texture2D[8];
        static Shader _ground, _tree, _sky;
        static Texture2D _skyTex, _atlas, _atlasNormal, _flatMacro, _flatControl;
        static Material _skyMat;
        static VolumeProfile _grade;
        static readonly List<Species> Wood = new List<Species>(), Scrub = new List<Species>();
        static readonly HashSet<Camera> Cameras = new HashSet<Camera>();
        static bool? _on;
        static bool _hooked, _swapped;
        static AmbientMode _modeWas;
        static Color _skyWas, _equatorWas, _groundWas;

        static Color Hex(int rgb) => PuttrArcade.Make.Hex(rgb);

        /// <summary>Whether this device draws Tracks the realistic way. Decided once.</summary>
        public static bool On => _on ??= Decide();

        static bool Decide()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-app2TracksPlain") >= 0) return false;
            bool forced = Array.IndexOf(args, "-app2TracksReal") >= 0;
            if (!forced)
            {
                // Eleven more textures and a heavier ground shader: not for a browser on a television, nor a phone short of memory.
                if (Application.platform == RuntimePlatform.WebGLPlayer) return false;
                if (SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize < 2800) return false;
                if (SystemInfo.graphicsShaderLevel < 35) return false;
                if (PuttrArcade.Arcade.Plain) return false; // an old Apple chip: no graded camera, no heavy ground shader
            }
            _ground = Shader.Find("PuttrApp/TracksGround"); _tree = Shader.Find("PuttrApp/TracksTree"); _sky = Shader.Find("PuttrApp/TracksSky");
            if (_ground == null || _tree == null || _sky == null || !_ground.isSupported || !_tree.isSupported) return false;
            for (int i = 0; i < Scans.Length; i++) { Scans[i] = Resources.Load<Texture2D>(Folder + ScanFiles[i]); if (Scans[i] == null) return false; }
            _skyTex = Resources.Load<Texture2D>(Folder + "Sky");
            _atlas = Resources.Load<Texture2D>(Folder + "Trees");
            _atlasNormal = Resources.Load<Texture2D>(Folder + "TreesNormal");
            var table = Resources.Load<TextAsset>(Folder + "Trees");
            if (_skyTex == null || _atlas == null || _atlasNormal == null || table == null) return false;
            ReadSpecies(table.text);
            return Wood.Count > 0 && Scrub.Count > 0;
        }

        // ── Colours ──

        /// <summary>The lies' colours by id, for the hole's colour map.</summary>
        public static Color[] Colours() => (Color[])Lies.Clone();

        // ── The camera: sky, light, haze, grading ──

        /// <summary>A Tracks camera and its sun under this look: the panorama behind everything with its sun where the
        /// scene's sun is, the haze and sky light the ground and tree shaders read, and the picture graded.</summary>
        public static void Dress(Camera cam, Light sun)
        {
            sun.transform.localRotation = Quaternion.Euler(SunElevation, -35f, 0f);
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.05f;
            Vector3 toSun = -sun.transform.forward;
            Shader.SetGlobalTexture("_TracksSkyTex", _skyTex);
            Shader.SetGlobalFloat("_TracksSkyTurn", SunU - Mathf.Atan2(toSun.x, toSun.z) / (2f * Mathf.PI));
            Shader.SetGlobalVector("_TracksHaze", new Vector4(Horizon.r, Horizon.g, Horizon.b, HazePerYard));
            Shader.SetGlobalVector("_TracksAmbientSky", SkyLight.linear);
            Shader.SetGlobalVector("_TracksAmbientGround", GroundLight.linear);

            if (_skyMat == null) _skyMat = new Material(_sky) { name = "Tracks sky" };
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.gameObject.AddComponent<Skybox>().material = _skyMat;

            // Graded on its own layer, so the arcade's own volume (its bloom, grain and shallow focus) stays the arcade's.
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 1 << GradeLayer;
            if (_grade == null)
            {
                _grade = ScriptableObject.CreateInstance<VolumeProfile>();
                _grade.name = "Tracks grade";
                Tonemapping tone = _grade.Add<Tonemapping>(true);
                tone.mode.value = TonemappingMode.Neutral;
                ColorAdjustments grade = _grade.Add<ColorAdjustments>(true);
                grade.contrast.value = 8f;
                Vignette vignette = _grade.Add<Vignette>(true);
                vignette.intensity.value = 0.16f;
                vignette.smoothness.value = 0.5f;
            }
            var volume = new GameObject("Tracks grade") { layer = GradeLayer }.AddComponent<Volume>();
            volume.transform.SetParent(cam.transform.parent, false);
            volume.isGlobal = true;
            volume.sharedProfile = _grade;

            // What is drawn with the pipeline's own lit shader (the ball, the flag, the putting set) takes its sky light from
            // the scene's ambient, which the arcade keeps near black for its dark room: it is the sky's for these cameras only.
            Cameras.Add(cam);
            if (!_hooked)
            {
                _hooked = true;
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endCameraRendering += End;
            }
        }

        public static void Forget(Camera cam) { if (cam != null) Cameras.Remove(cam); }

        static void Begin(ScriptableRenderContext context, Camera cam)
        {
            if (_swapped || !Cameras.Contains(cam)) return;
            _swapped = true;
            _modeWas = RenderSettings.ambientMode; _skyWas = RenderSettings.ambientSkyColor; _equatorWas = RenderSettings.ambientEquatorColor; _groundWas = RenderSettings.ambientGroundColor;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = SkyLight; RenderSettings.ambientEquatorColor = HorizonLight; RenderSettings.ambientGroundColor = GroundLight;
        }

        static void End(ScriptableRenderContext context, Camera cam)
        {
            if (!_swapped || !Cameras.Contains(cam)) return;
            _swapped = false;
            RenderSettings.ambientMode = _modeWas;
            RenderSettings.ambientSkyColor = _skyWas; RenderSettings.ambientEquatorColor = _equatorWas; RenderSettings.ambientGroundColor = _groundWas;
        }

        // ── The ground ──

        /// <summary>
        /// How much of each kind of ground is where, from the hole's lie ids, at half their resolution (the shader draws
        /// the edges sharp again): r mown (fairway half, greens and tees full), g sand (and roads and paths, which are
        /// gravel, not grass), b scrub and woodland floor, a water. What is left is rough.
        /// </summary>
        public static Texture2D Control(byte[] ids, int tw, int th, byte[] road)
        {
            int cw = (tw + 1) / 2, ch = (th + 1) / 2;
            var px = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    int mown = 0, sand = 0, scrub = 0, water = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int o = Mathf.Min(th - 1, y * 2 + (k >> 1)) * tw + Mathf.Min(tw - 1, x * 2 + (k & 1));
                        if (road != null && road[o] != 0) { sand += 255; continue; }
                        switch (ids[o])
                        {
                            case 5: mown += 128; break;
                            case 6: case 7: mown += 255; break;
                            case 1: case 8: sand += 255; break;
                            case 2: case 3: scrub += 255; break;
                            case 9: water += 255; break;
                        }
                    }
                    px[y * cw + x] = new Color32((byte)(mown >> 2), (byte)(sand >> 2), (byte)(scrub >> 2), (byte)(water >> 2));
                }
            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Tracks lie weights" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The ground's material: the hole's colour map and lie weights, over the scans. unit: world units in a yard.</summary>
        public static Material Ground(Texture macro, Texture control, float unit = 1f, float edge = 5f)
        {
            var m = new Material(_ground) { name = "Tracks ground" };
            m.SetTexture("_Macro", macro);
            m.SetTexture("_Control", control);
            for (int i = 0; i < Scans.Length; i++) m.SetTexture(ScanProps[i], Scans[i]);
            m.SetFloat("_Unit", unit);
            m.SetFloat("_Edge", edge);   // a whole course's map is coarse: drawn sharp, its texels would show as steps
            return m;
        }

        /// <summary>Plain rough all over, for the field round a hole that runs to the horizon.</summary>
        public static Material Field()
        {
            if (_flatMacro == null)
            {
                _flatMacro = new Texture2D(1, 1, TextureFormat.RGB24, false) { name = "Tracks field" };
                _flatMacro.SetPixel(0, 0, Lies[0]); _flatMacro.Apply();
                _flatControl = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Tracks field weights" };
                _flatControl.SetPixel(0, 0, new Color(0, 0, 0, 0)); _flatControl.Apply();
            }
            return Ground(_flatMacro, _flatControl);
        }

        // ── Trees ──

        sealed class Species
        {
            public int Side, Top;                               // cells of the 4 x 4 atlas
            public float HeightShare, WidthShare, TopShare;      // of the side cell's square; the canopy card's square over it
            public float Min, Max;                               // yards tall
        }

        static void ReadSpecies(string table)
        {
            Wood.Clear(); Scrub.Clear();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string line in table.Split('\n'))
            {
                string[] f = line.Trim().Split(' ');
                if (f.Length < 9) continue;
                var s = new Species
                {
                    Side = int.Parse(f[2], inv), Top = int.Parse(f[3], inv),
                    HeightShare = float.Parse(f[4], inv), WidthShare = float.Parse(f[5], inv), TopShare = float.Parse(f[6], inv),
                    Min = float.Parse(f[7], inv), Max = float.Parse(f[8], inv),
                };
                (f[1] == "scrub" ? Scrub : Wood).Add(s);
            }
        }

        /// <summary>
        /// All of a hole's trees and bushes as one mesh for the tree shader: per tree, a card that stands on its foot and
        /// a card of its canopy from above (see TracksTree.shader for what each vertex carries).
        /// </summary>
        public sealed class Grove
        {
            readonly List<Vector3> _feet = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>(), _corner = new List<Vector2>();
            readonly List<Vector4> _kind = new List<Vector4>();
            readonly List<int> _tris = new List<int>();
            Bounds _bounds;
            public int Count { get; private set; }

            /// <summary>
            /// A tree (or, on scrub, a bush) with its foot here. pick 0..1 chooses which: on woodland the first half
            /// are conifers, the second broadleaves. size 0..1 between the kind's shortest and tallest; shade is its
            /// own brightness; unit is world units in a yard.
            /// </summary>
            public void Add(Vector3 foot, bool scrub, float pick, float size, float shade, bool flip, float unit = 1f)
            {
                List<Species> from = scrub ? Scrub : Wood;
                Species s = from[Mathf.Clamp(Mathf.FloorToInt(pick * from.Count), 0, from.Count - 1)];
                float height = Mathf.Lerp(s.Min, s.Max, size) * unit, square = height / s.HeightShare, reach = square * s.WidthShare * 0.5f;
                Rect side = Cell(s.Side), top = Cell(s.Top);
                float u0 = flip ? side.xMax : side.xMin, u1 = flip ? side.xMin : side.xMax;
                Quad(foot, new Vector4(0f, 0f, shade, reach),
                    new Vector2(-square / 2, -square * 0.01f), new Vector2(square / 2, square * 0.99f), new Vector2(u0, side.yMin), new Vector2(u1, side.yMax));
                float canopy = square * s.TopShare / 2;
                Quad(foot, new Vector4(1f, height * 0.62f, shade, reach),
                    new Vector2(-canopy, -canopy), new Vector2(canopy, canopy), new Vector2(flip ? top.xMax : top.xMin, top.yMin), new Vector2(flip ? top.xMin : top.xMax, top.yMax));
                var box = new Bounds(foot + Vector3.up * square / 2, Vector3.one * square * 1.5f);
                if (Count == 0) _bounds = box; else _bounds.Encapsulate(box);
                Count++;
            }

            static Rect Cell(int index)
            {
                const float inset = 1f / 2048f;
                int col = index % 4, row = index / 4;
                return Rect.MinMaxRect(col / 4f + inset, 1f - (row + 1) / 4f + inset, (col + 1) / 4f - inset, 1f - row / 4f - inset);
            }

            void Quad(Vector3 foot, Vector4 kind, Vector2 c0, Vector2 c1, Vector2 uv0, Vector2 uv1)
            {
                int at = _feet.Count;
                for (int k = 0; k < 4; k++)
                {
                    bool right = k == 1 || k == 2, up = k >= 2;
                    _feet.Add(foot);
                    _corner.Add(new Vector2(right ? c1.x : c0.x, up ? c1.y : c0.y));
                    _uv.Add(new Vector2(right ? uv1.x : uv0.x, up ? uv1.y : uv0.y));
                    _kind.Add(kind);
                }
                _tris.Add(at); _tris.Add(at + 2); _tris.Add(at + 1);
                _tris.Add(at); _tris.Add(at + 3); _tris.Add(at + 2);
            }

            /// <summary>The grove as an object under parent (nothing if it has no trees). made takes what to free with the hole.</summary>
            public GameObject Finish(Transform parent, float unit, List<UnityEngine.Object> made)
            {
                if (Count == 0) return null;
                var mesh = new Mesh { name = "Tracks trees", indexFormat = _feet.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_feet);
                mesh.SetUVs(0, _uv); mesh.SetUVs(1, _corner); mesh.SetUVs(2, _kind);
                mesh.SetTriangles(_tris, 0, false);
                mesh.bounds = _bounds;   // the cards are opened out in the shader: the mesh itself is only the feet
                var m = new Material(_tree) { name = "Tracks trees" };
                m.SetTexture("_Atlas", _atlas);
                m.SetTexture("_AtlasN", _atlasNormal);
                m.SetFloat("_Unit", unit);
                var go = new GameObject("Trees");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = false;
                made?.Add(mesh); made?.Add(m);
                return go;
            }
        }
    }
}
