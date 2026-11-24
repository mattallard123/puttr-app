// The lights and lit surfaces of an old Apple chip (Arcade.Plain: an A9 or A10 iPhone or iPad).
//
// On an A9X iPad Pro (iPad6,7, iPadOS 16.7) the pipeline's own Lit shader drew the mat and the floor of every game
// pure white (2026-11-19). Taking away HDR and post-processing, every light, the reflections, the textures, the
// shadows, the normal maps and batching, one at a time on the iPad itself, changed nothing: only another shader did
// (2026-11-20). Why the pipeline's shader fails there is not known. The first Apple TV 4K (A10X) drew it correctly.
//
// So on those chips:
//   - every lit material is drawn with the arcade's own shader (PlainLit.shader): Make.Base gives it to the
//     materials it makes, and this keeper gives it to any other it finds on show
//   - that shader is lit by one sun and an even light from everywhere, which this keeper sets each frame from the
//     lights on show
//   - each spot light is turned into a directional light down the same axis, as bright as the spot would have been
//     where its axis meets the floor, and the point lights are put out: the games' spots and points are hundreds to
//     hundreds of thousands bright, for a tone-mapped picture an old chip does not have
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    [DefaultExecutionOrder(10000)]   // after every game has set its lights for the frame
    public sealed class PlainLights : MonoBehaviour
    {
        /// <summary>The brightest a light may be here: the games are lit for a tone-mapped picture, and an old chip
        /// has no tone mapping.</summary>
        public const float Brightest = 1.5f;

        static PlainLights _only;

        static Shader _lit, _pipelineLit;
        /// <summary>The arcade's own lit shader, for an old chip (PlainLit.shader).</summary>
        public static Shader Lit => _lit != null ? _lit : _lit = Shader.Find("PuttrArcade/PlainLit");
        static Shader PipelineLit => _pipelineLit != null ? _pipelineLit : _pipelineLit = Shader.Find("Universal Render Pipeline/Lit");
        static readonly int SunDir = Shader.PropertyToID("_PlainSunDir"), SunColor = Shader.PropertyToID("_PlainSunColor"), Ambient = Shader.PropertyToID("_PlainAmbient");

        sealed class Held { public LightType Type; public float Made, Wrote; }
        readonly Dictionary<Light, Held> _lights = new Dictionary<Light, Held>();
        readonly HashSet<Material> _seen = new HashSet<Material>();
        readonly List<Light> _gone = new List<Light>();
        readonly List<Material> _shared = new List<Material>();
        int _frame;

        /// <summary>Starts the keeper (once), on an old chip only. Called where the pipeline is settled, before any
        /// 3D camera is made.</summary>
        public static void Ensure()
        {
            if (_only != null || !Arcade.Plain) return;
            var go = new GameObject("Plain Lights");
            DontDestroyOnLoad(go);
            _only = go.AddComponent<PlainLights>();
        }

        /// <summary>A light just made: taken in now, so it never draws a frame as it was made.</summary>
        public static void Add(Light light)
        {
            if (_only != null && light != null) _only.Take(light);
        }

        /// <summary>How bright a spot is as a directional light: its brightness over the square of the distance to
        /// where its axis meets the floor (y = 0), or to half its reach when it does not point down.</summary>
        public static float Flat(float made, Vector3 at, Vector3 forward, float reach)
        {
            float far = forward.y < -0.05f && at.y > 0f ? at.y / -forward.y : reach * 0.5f;
            far = Mathf.Clamp(far, 1f, Mathf.Max(1f, reach));
            return Mathf.Min(Brightest, made / (far * far));
        }

        void Take(Light light)
        {
            if (_lights.ContainsKey(light)) return;
            _lights[light] = new Held { Type = light.type, Made = light.intensity, Wrote = light.intensity };
        }

        void LateUpdate()
        {
            // Lights and materials made without Make (a ball's charge, the lab's figure) are found here.
            if (_frame % 6 == 0)
                foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Take(light);
            if (_frame % 30 == 1) Surfaces();
            _frame++;
            Settle();
            Sun();
        }

        void Settle()
        {
            foreach (KeyValuePair<Light, Held> pair in _lights)
            {
                Light light = pair.Key;
                Held held = pair.Value;
                if (light == null) { _gone.Add(light); continue; }
                // A game sets its lights' brightness as it plays: anything but what was last written here is its word.
                if (!Mathf.Approximately(light.intensity, held.Wrote)) held.Made = light.intensity;

                float bright = held.Made;
                if (held.Type == LightType.Spot)
                {
                    if (light.type != LightType.Directional) light.type = LightType.Directional;
                    bright = Flat(held.Made, light.transform.position, light.transform.forward, light.range);
                }
                else if (held.Type == LightType.Point) bright = 0f;
                if (!Mathf.Approximately(light.intensity, bright)) light.intensity = bright;
                held.Wrote = bright;
            }
            if (_gone.Count > 0)
            {
                foreach (Light light in _gone) _lights.Remove(light);
                _gone.Clear();
            }
        }

        // What the arcade's own lit shader is lit by: the brightest directional light on show as its sun (a spot drawn
        // as one counts), the rest of them and the scene's own ambient light as an even light from everywhere. There
        // is always a little of that, so that a set whose point lights are out is never black.
        void Sun()
        {
            Light key = null;
            float best = 0f;
            Color rest = Color.black;
            foreach (KeyValuePair<Light, Held> pair in _lights)
            {
                Light light = pair.Key;
                if (light == null || !light.isActiveAndEnabled || light.type != LightType.Directional || light.intensity <= 0f) continue;
                if ((light.cullingMask & 1) == 0) continue;   // a light for one layer alone (the console's shell)
                if (light.intensity > best)
                {
                    if (key != null) rest += key.color.linear * (Mathf.Min(key.intensity, Brightest) * 0.3f);
                    key = light; best = light.intensity;
                }
                else rest += light.color.linear * (Mathf.Min(light.intensity, Brightest) * 0.3f);
            }
            Color ambient = RenderSettings.ambientLight.linear + rest + new Color(0.06f, 0.065f, 0.07f);
            ambient.r = Mathf.Min(ambient.r, 0.6f); ambient.g = Mathf.Min(ambient.g, 0.6f); ambient.b = Mathf.Min(ambient.b, 0.6f);
            Shader.SetGlobalVector(Ambient, ambient);
            if (key != null)
            {
                Shader.SetGlobalVector(SunDir, -key.transform.forward);
                Shader.SetGlobalVector(SunColor, key.color.linear * Mathf.Min(key.intensity, Brightest));
            }
            else
            {
                Shader.SetGlobalVector(SunDir, Vector3.up);
                Shader.SetGlobalVector(SunColor, new Color(0.25f, 0.26f, 0.28f));   // no sun in this set: a dim one from above
            }
        }

        // Any material on show that still has the pipeline's Lit shader (one made without Make) gets the arcade's own.
        void Surfaces()
        {
            Shader own = Lit, pipeline = PipelineLit;
            if (own == null || pipeline == null) return;
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                renderer.GetSharedMaterials(_shared);
                foreach (Material m in _shared)
                {
                    if (m == null || !_seen.Add(m)) continue;
                    if (m.shader == pipeline) m.shader = own;
                }
            }
            _seen.RemoveWhere(m => m == null);
        }
    }
}
