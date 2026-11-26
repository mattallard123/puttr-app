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
        /// <summary>The card's own camera (AimStage gives it the arcade's dark room to draw in).</summary>
        public Camera Camera => _camera;

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
            _path.widthMultiplier = 0.42f;   // the card is close in on the cup: a line a little thinner than the ball
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

            // Where the ideal putt goes in: a green dot on the lip of the cup, ringed white so it shows on the carpet.
            GameObject spot = Make.MeshObject("Spot", root, Make.Disc(0.62f), Make.Glow(Color.white, false));
            GameObject core = Make.MeshObject("Entry point", spot.transform, Make.Disc(0.46f), Make.Glow(new Color(0.3f, 0.9f, 0.25f, 1f), false));
            core.layer = Layer;
            core.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            spot.layer = Layer;
            _spot = spot.transform;
            root.gameObject.SetActive(false);
            _root = root;
        }

        readonly Transform _root;

        static readonly Vector3 WideFrom = new Vector3(0f, 21f, -31f), WideAt = new Vector3(0f, 0f, -5f);
        // The card's picture is the cup: from nearly overhead, the hole fills the box top to bottom (Matt, 2026-11-23).
        // A target off to one side of the cup is kept in the picture with it: the camera sits between the two and
        // opens its lens only as far as that takes.
        float _frameTarget, _frameMargin = 0.55f;
        void Close(float targetIn, float margin = 0.55f)
        {
            _frameTarget = targetIn; _frameMargin = margin;
            float mid = Mathf.Abs(targetIn) > LaneSpec.HoleRadius ? targetIn * 0.5f : 0f;
            var from = new Vector3(mid, 13f, -5.2f);
            var at = new Vector3(mid, 0f, -0.25f);
            float halfTall = LaneSpec.HoleRadius + margin;
            if (mid != 0f) halfTall = Mathf.Max(halfTall, (Mathf.Abs(mid) + LaneSpec.HoleRadius + 0.9f) * _texture.height / _texture.width);
            _camera.transform.localPosition = from;
            _camera.transform.LookAt(_camera.transform.parent.TransformPoint(at));
            _camera.fieldOfView = 2f * Mathf.Atan(halfTall / Vector3.Distance(from, at)) * Mathf.Rad2Deg;
        }
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
            Close(_frameTarget, _frameMargin);
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
            _band.gameObject.SetActive(true); _spot.gameObject.SetActive(true);
            if (_arcOn != null) { _arcOn.gameObject.SetActive(false); _arcOff.gameObject.SetActive(false); _entry.gameObject.SetActive(false); }
            if (_real != null) { _real.gameObject.SetActive(false); _ball.gameObject.SetActive(false); }
            Close(targetIn);
            float teeIn = (Mathf.Clamp(tee, 1, 3) - 2) * TeeSideInches, length = Mathf.Max(3, feet) * 12f;
            float slope = Mathf.Tan(LaneSpec.RampAngleDeg * Mathf.Deg2Rad);

            // Where that line meets the lip of the cup: the entry point. A target outside the cup has none: the spot
            // marks where it crosses the hole's centre line.
            Vector2 entry = new Vector2(targetIn, 0f);
            if (Mathf.Abs(targetIn) < LaneSpec.HoleRadius)
            {
                Vector2 back = new Vector2(teeIn - targetIn, -length).normalized;   // from the crossing point toward the tee
                float b = targetIn * back.x, along = -b + Mathf.Sqrt(Mathf.Max(0f, b * b - targetIn * targetIn + LaneSpec.HoleRadius * LaneSpec.HoleRadius));
                entry += back * along;
            }

            // From the tee to the entry point.
            var points = new List<Vector3>();
            for (float z = ViewFromZ; z < entry.y - 0.05f; z += 0.5f)
            {
                float x = targetIn + (teeIn - targetIn) * (-z) / length;
                points.Add(new Vector3(x, LaneSpec.SurfaceY(z) + 0.12f, z));
            }
            points.Add(new Vector3(entry.x, LaneSpec.SurfaceY(entry.y) + 0.12f, entry.y));
            _path.positionCount = points.Count;
            _path.SetPositions(points.ToArray());

            float band = Mathf.Min(goalIn, LaneSpec.MatHalfWidth * 2f);
            _band.position = new Vector3(0f, 0.06f, 0f);
            _band.rotation = Quaternion.Euler(90f - LaneSpec.RampAngleDeg, 0f, 0f);
            _band.localScale = new Vector3(band, 0.9f, 1f);
            _spot.position = new Vector3(entry.x, LaneSpec.SurfaceY(entry.y) + 0.16f, entry.y);
            _spot.rotation = Quaternion.Euler(-LaneSpec.RampAngleDeg, 0f, 0f);

            _camera.Render();
        }

        public void Hide() => _root.gameObject.SetActive(false);

        /// <summary>The picture is given back (an AimStage that is closed: the arcade's own lives as long as the app).</summary>
        public void Release()
        {
            // (On the way out of the app the camera may already be gone.)
            if (_camera != null) _camera.targetTexture = null;
            if (_texture != null) { _texture.Release(); Object.Destroy(_texture); }
        }

        // ── An entry window (Beer Pong): the arc of the rim a make must come in through ──
        LineRenderer _arcOn, _arcOff;
        Transform _entry;
        Material _entryPaint;

        LineRenderer Arc(string name, Color colour, float width)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(_root, false);
            LineRenderer l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = true;
            l.material = Make.Glow(colour, false);
            l.widthMultiplier = width;
            l.numCapVertices = 4;
            l.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // flat on the mat
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return l;
        }

        static void ArcPoints(LineRenderer l, float from, float to, float radius)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to - from) / 3f) + 1);
            l.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(from, to, i / (n - 1f)) * Mathf.Deg2Rad;
                float z = -Mathf.Cos(a) * radius;
                l.SetPosition(i, new Vector3(Mathf.Sin(a) * radius, LaneSpec.SurfaceY(z) + 0.1f, z));
            }
        }

        /// <summary>The ideal putt straight into the cup from the dealt tee, close in on the cup, with the rim lit green
        /// where a make counts (windowDeg either side of straight in, 0 the front, + to the right) and, after a putt,
        /// a dot where it went in: white for a make that counted, amber for one that did not.</summary>
        /// <summary>centreDeg: where on the rim the window is centred (Tracks turns it with the tee's own entry).</summary>
        public void ShowWindow(int feet, int tee, float windowDeg, float? entryDeg, bool made, float centreDeg = 0f)
        {
            Show(feet, tee, 0f, 0f);
            Close(0f, 1.2f);   // room for the lit rim round the cup
            _band.gameObject.SetActive(false);
            _spot.gameObject.SetActive(false);
            if (_arcOn == null)
            {
                _arcOff = Arc("Rim out", new Color(1f, 1f, 1f, 0.22f), 0.7f);
                _arcOn = Arc("Window", new Color(0.48f, 0.83f, 0.27f, 1f), 0.9f);
                _entryPaint = Make.Glow(Color.white, false);
                GameObject dot = Make.MeshObject("Entry", _root, Make.Disc(0.55f), _entryPaint);
                dot.layer = Layer;
                _entry = dot.transform;
            }
            float r = LaneSpec.HoleRadius + 0.55f, w = Mathf.Clamp(windowDeg, 1f, 180f);
            _arcOn.gameObject.SetActive(true); _arcOff.gameObject.SetActive(true);
            ArcPoints(_arcOn, centreDeg - w, centreDeg + w, r);
            ArcPoints(_arcOff, centreDeg + w, centreDeg + 360f - w, r);
            Entry(entryDeg, made);
        }

        /// <summary>The dot where the putt went in, set (or taken away) on the window already shown, leaving a replay
        /// that is running or drawn as it is.</summary>
        public void Entry(float? entryDeg, bool made)
        {
            if (_entry == null) return;
            if (entryDeg.HasValue)
            {
                float a = entryDeg.Value * Mathf.Deg2Rad, er = LaneSpec.HoleRadius - 0.5f;
                float z = -Mathf.Cos(a) * er;
                _entry.position = new Vector3(Mathf.Sin(a) * er, LaneSpec.SurfaceY(z) + 0.2f, z);
                _entry.rotation = Quaternion.Euler(-LaneSpec.RampAngleDeg, 0f, 0f);
                _entryPaint.color = made ? Color.white : new Color(1f, 0.7f, 0f);
                _entry.gameObject.SetActive(true);
            }
            else _entry.gameObject.SetActive(false);
            _camera.Render();
        }

        /// <summary>The angle the perfect putt comes in at, degrees; + is rolling right.</summary>
        public static float EntryDegrees(int feet, int tee, float targetIn)
        {
            float teeIn = (Mathf.Clamp(tee, 1, 3) - 2) * TeeSideInches, length = Mathf.Max(3, feet) * 12f;
            return Mathf.Atan2(targetIn - teeIn, length) * Mathf.Rad2Deg;
        }
    }
}
