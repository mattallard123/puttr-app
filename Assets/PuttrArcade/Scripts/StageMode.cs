// A game that does not show the Puttr.
//
// The three games behind the console carry the real ball on into the game. A
// stage game only takes the putt's numbers: where it crossed the hole (line),
// how fast (pace), and which way it was travelling (push or pull). What it
// launches with them is up to the game: a penalty kick, a space probe, a
// stunt bike.
//
// Each stage is built a long way from the console, with its own light, and
// the camera stays on it for the whole game. Build in local coordinates and
// use At() for anything the arcade needs in world space.
using System.Collections;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public abstract class StageMode : GameMode
    {
        public override bool OwnStage => true;
        public sealed override IEnumerator Run(Ball ball, PuttResult putt, bool holed) { yield break; }
        public sealed override IEnumerator Finish(Ball ball, PuttResult putt, bool holed) { yield break; }

        // A stage is laid out by Begin, so that is what the home screen shows.
        public override void Preview() => Begin(1);

        /// <summary>A point on this stage, in world space.</summary>
        protected Vector3 At(float x, float y, float z) => Root.TransformPoint(new Vector3(x, y, z));


        // ── The putt, as three numbers ──────────────────────────────────────

        /// <summary>Pace at the hole, in/s. A putt left short counts as a weak one.</summary>
        protected static float Pace(PuttResult putt)
        {
            PuttLaunch launch = putt.Launch;
            if (launch.Reached && launch.SpeedInPerSec > 1f) return launch.SpeedInPerSec;
            return putt.HasSpeed ? Mathf.Clamp(putt.SpeedInPerSec, 10f, 18f) : 12f;
        }

        /// <summary>Inches right of the center of the hole.</summary>
        protected static float Line(PuttResult putt) => putt.Launch.LineAtHoleInches;

        /// <summary>Degrees right of straight: a push is positive, a pull negative.</summary>
        protected static float Push(PuttResult putt) => putt.Launch.Reached ? putt.Launch.HeadingDeg : 0f;

        /// <summary>The card bottom left: pace, line, and what the game made of them.</summary>
        protected void ShowPutt(PuttResult putt, string result, Color color)
        {
            float line = Line(putt);
            string where = Mathf.Abs(line) < 0.15f ? "Center" : Mathf.Abs(line).ToString("0.0") + " in " + (line < 0 ? "left" : "right");
            Arcade.Hud.SetPutt(PaceWords.Of(putt), where, result, color);
        }

        // ── Building blocks ─────────────────────────────────────────────────

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

        /// <summary>A flat card facing the camera (which looks along +Z).</summary>
        protected static GameObject Card(string name, Transform parent, Vector3 position, Vector2 size, Material material)
        {
            GameObject go = Shape(PrimitiveType.Quad, name, parent, position, new Vector3(size.x, size.y, 1f), material);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A flat ring or disc facing the camera.</summary>
        protected static GameObject Hoop(string name, Transform parent, Vector3 position, float inner, float outer, Material material)
        {
            GameObject go = Make.MeshObject(name, parent, Make.Ring(inner, outer, 96), material);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static Font _font;

        /// <summary>Words in the scene, in Montserrat. height is the cap height, in units.</summary>
        protected static TextMesh Words(Transform parent, string text, Vector3 position, float height, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, int resolution = 96)
        {
            if (_font == null) _font = Resources.Load<Font>("PuttrArcade/Fonts/Montserrat-Bold");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Words");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var words = go.AddComponent<TextMesh>();
            words.font = _font;
            // resolution is the size the letters are drawn at before they are scaled: raise it for big words.
            words.fontSize = resolution;
            words.characterSize = height * 10f / resolution * 1.4f;
            words.anchor = anchor;
            words.alignment = TextAlignment.Center;
            words.color = color;
            words.text = text;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _font.material;
            renderer.sortingOrder = 10; // words are always on top
            return words;
        }

        /// <summary>A line of light, drawn in this stage's own coordinates.</summary>
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

        protected static Light Sun(Transform parent, Vector3 euler, Color color, float intensity)
        {
            Light sun = World.NewLight(parent, "Stage light", LightType.Directional, color, intensity);
            sun.transform.localRotation = Quaternion.Euler(euler);
            return sun;
        }

        protected static float Ease(float t) => t * t * (3f - 2f * t);
    }
}
