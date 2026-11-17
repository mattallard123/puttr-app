// The ideal putt on the console itself: a small camera looks down the real 3D ramp at the hole
// (the one Under the Cup's table sits behind), and on the carpet lies the path the perfect putt
// takes from the dealt tee, the band across the hole that counts at this difficulty, and the
// spot it should cross. The picture goes into a card top right (Hud.SetAimCard), across from
// the tee sign. Tees sit 8 in either side of centre, as in the Puttr app (getTeeEntry).
//
// Only this camera sees the path: it lives on its own layer, which the main camera leaves out.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PuttrArcade
{
    public sealed class AimView
    {
        public const int Layer = 4;            // Unity's built-in "Water" layer: unused here
        const int ShellLayer = 1;              // the console's hood and sides (World.ShellLayer): left out so the ramp shows
        public const float TeeSideInches = 8f;
        const float ViewFromZ = -40f;          // how much of the ramp the path is drawn over

        readonly Camera _camera;
        readonly RenderTexture _texture;
        readonly LineRenderer _path;
        readonly Transform _band, _spot;
        readonly Material _bandPaint;

        public Texture Texture => _texture;

        public AimView(Transform parent)
        {
            var root = new GameObject("Aim view").transform;
            root.SetParent(parent, false);

            _texture = new RenderTexture(640, 400, 24, RenderTextureFormat.ARGB32) { name = "Aim view", antiAliasing = 4 };
            var eye = new GameObject("Aim camera");
            eye.transform.SetParent(root, false);
            // Above the tees, looking down the ramp at the cup.
            eye.transform.localPosition = WideFrom;
            eye.transform.LookAt(WideAt);
            _camera = eye.AddComponent<Camera>();
            _camera.enabled = false; // drawn on demand: the ramp does not move
            _camera.targetTexture = _texture;
            _camera.fieldOfView = 34f;
            _camera.nearClipPlane = 1f;
            _camera.farClipPlane = 110f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Make.Ink;
            _camera.cullingMask = ~((1 << ShellLayer) | (1 << 5)); // no hood, no UI
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;

            var green = new Color(0.48f, 0.83f, 0.27f);
            var line = new GameObject("Ideal path") { layer = Layer };
            line.transform.SetParent(root, false);
            _path = line.AddComponent<LineRenderer>();
            _path.useWorldSpace = true;
            _path.material = Make.Glow(new Color(green.r, green.g, green.b, 1f), false);
            _path.widthMultiplier = 0.9f;
            _path.numCapVertices = 6;
            _path.alignment = LineAlignment.View;
            _path.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The band that counts: a strip across the ramp through the hole.
            _bandPaint = Make.Glow(new Color(green.r, green.g, green.b, 0.28f), false);
            var band = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Make.Kill(band.GetComponent<Collider>());
            band.name = "Counts";
            band.layer = Layer;
            band.transform.SetParent(root, false);
            band.GetComponent<MeshRenderer>().sharedMaterial = _bandPaint;
            band.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _band = band.transform;

            GameObject spot = Make.MeshObject("Spot", root, Make.Disc(0.75f), Make.Glow(Color.white, false));
            spot.layer = Layer;
            _spot = spot.transform;
            root.gameObject.SetActive(false);
            _root = root;
        }

        readonly Transform _root;

        // The ideal putt is seen from above the tees; the replay of a real putt from close to the cup.
        static readonly Vector3 WideFrom = new Vector3(0f, 21f, -31f), WideAt = new Vector3(0f, 0f, -5f);
        static readonly Vector3 CloseFrom = new Vector3(0f, 11f, -15f), CloseAt = new Vector3(0f, 0f, -1.5f);
        LineRenderer _real;
        Transform _ball;
        Vector3[] _replay;
        float _replayT = -1f;
        const float ReplayTime = 1.4f;

        public bool Showing => _root.gameObject.activeSelf;

        void BuildReplay()
        {
            var line = new GameObject("Real path") { layer = Layer };
            line.transform.SetParent(_root, false);
            _real = line.AddComponent<LineRenderer>();
            _real.useWorldSpace = true;
            _real.material = Make.Glow(new Color(1f, 1f, 1f, 0.95f), false);
            _real.widthMultiplier = 0.45f;
            _real.numCapVertices = 4;
            _real.alignment = LineAlignment.View;
            _real.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Make.Kill(ball.GetComponent<Collider>());
            ball.name = "Ball";
            ball.layer = Layer;
            ball.transform.SetParent(_root, false);
            ball.transform.localScale = Vector3.one * LaneSpec.BallRadius * 2f;
            ball.GetComponent<MeshRenderer>().sharedMaterial = Make.Glow(Color.white, false);
            _ball = ball.transform;
        }

        /// <summary>The putt just hit, rolled again on the ramp, close in on the cup, beside the ideal one.
        /// pathInches: x right of the hole, y toward the Player. made: it finishes in the cup.</summary>
        public void Replay(Vector2[] pathInches, bool made)
        {
            if (!Showing || pathInches == null || pathInches.Length < 2) return;
            if (_real == null) BuildReplay();
            var pts = new List<Vector3>();
            foreach (Vector2 p in pathInches)
            {
                float z = -p.y;
                if (z > LaneSpec.RampEndZ) break; // off the far end of the mat
                pts.Add(new Vector3(p.x, LaneSpec.SurfaceY(z) + LaneSpec.BallRadius, z));
            }
            if (made && pts.Count > 0) { Vector3 last = pts[pts.Count - 1]; pts.Add(new Vector3(last.x * 0.3f, -1.5f, last.z * 0.3f)); }
            if (pts.Count < 2) return;
            _replay = pts.ToArray();
            _replayT = 0f;
            _camera.transform.localPosition = CloseFrom;
            _camera.transform.LookAt(CloseAt);
            _real.gameObject.SetActive(true);
            _ball.gameObject.SetActive(true);
            Tick(0f);
        }

        /// <summary>Plays the replay on: the card's picture is drawn again each frame while it runs.</summary>
        public void Tick(float dt)
        {
            if (_replayT < 0f || _replay == null) return;
            _replayT = Mathf.Min(1f, _replayT + dt / ReplayTime);
            float f = _replayT * (_replay.Length - 1);
            int i = Mathf.Min((int)f, _replay.Length - 2);
            Vector3 at = Vector3.Lerp(_replay[i], _replay[i + 1], f - i);
            _real.positionCount = i + 2;
            for (int k = 0; k <= i; k++) _real.SetPosition(k, _replay[k] + Vector3.down * (LaneSpec.BallRadius - 0.1f));
            _real.SetPosition(i + 1, at + Vector3.down * (LaneSpec.BallRadius - 0.1f));
            _ball.position = at;
            _camera.Render();
            if (_replayT >= 1f) _replayT = -1f;
        }

        /// <summary>Lays out the ideal putt and draws the picture. targetIn: where it crosses the
        /// hole's centre line, inches right of centre. goalIn: how wide the band that counts is.</summary>
        public void Show(int feet, int tee, float targetIn, float goalIn)
        {
            _root.gameObject.SetActive(true);
            _replayT = -1f;
            if (_real != null) { _real.gameObject.SetActive(false); _ball.gameObject.SetActive(false); }
            _camera.transform.localPosition = WideFrom;
            _camera.transform.LookAt(WideAt);
            float teeIn = (Mathf.Clamp(tee, 1, 3) - 2) * TeeSideInches, length = Mathf.Max(3, feet) * 12f;
            float slope = Mathf.Tan(LaneSpec.RampAngleDeg * Mathf.Deg2Rad);

            // From the tee to the crossing point, then on into the cup.
            var points = new List<Vector3>();
            for (float z = ViewFromZ; z <= 0.01f; z += 1f)
            {
                float x = targetIn + (teeIn - targetIn) * (-z) / length;
                points.Add(new Vector3(x, LaneSpec.SurfaceY(z) + 0.12f, z));
            }
            _path.positionCount = points.Count;
            _path.SetPositions(points.ToArray());

            float band = Mathf.Min(goalIn, LaneSpec.MatHalfWidth * 2f);
            _band.position = new Vector3(0f, 0.06f, 0f);
            _band.rotation = Quaternion.Euler(90f - LaneSpec.RampAngleDeg, 0f, 0f);
            _band.localScale = new Vector3(band, 1.6f, 1f);
            _spot.position = new Vector3(targetIn, 0.15f, 0f);
            _spot.rotation = Quaternion.Euler(-LaneSpec.RampAngleDeg, 0f, 0f);

            _camera.Render();
        }

        public void Hide() => _root.gameObject.SetActive(false);

        /// <summary>The angle the perfect putt comes in at, degrees; + is rolling right.</summary>
        public static float EntryDegrees(int feet, int tee, float targetIn)
        {
            float teeIn = (Mathf.Clamp(tee, 1, 3) - 2) * TeeSideInches, length = Mathf.Max(3, feet) * 12f;
            return Mathf.Atan2(targetIn - teeIn, length) * Mathf.Rad2Deg;
        }
    }
}
