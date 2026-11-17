// Center Cut, up close: the cup on the mat fills the view, looked down on from behind the ball. A band round the
// front of the rim is the scoring window (the tee's line ±90): lime in the middle (the center cut), fading through
// amber to red at the sides, dark round the back (the back door). Each putt replays its real roll (the camera's
// path) as a line on the carpet with the ball rolling along it and slowing as a ball does: a make tips over the
// lip where it entered, drops onto the cup's sloped floor and rolls out the back through the return tunnel (the
// Puttr cup is a through-hole); a back door rides round the rim before it drops; a lip out curls round the edge
// and rolls on; a miss rolls by and stops where it stopped. Then the window lights where the ball went in, a
// needle marks the entry, the putt's score comes up big (with sparks, a ring pulse and a cheer for a center cut),
// and after a hold the read fades and the window swings round to the next putt's line, so the Player sees where
// to enter before putting. The game starts on that view too.
// Units: inches x K; the cup is at the origin, the Player is down -Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class CenterCutScene : RuleScene
    {
        const float K = 5f, CupR = 2.125f * K, BallR = 0.84f * K, Depth = 4f * K, MatHalf = 12f * K, FarEnd = 7f * K;
        const float BandIn = CupR + 0.3f * K, BandOut = CupR + 1.5f * K, Step = 6f;
        static readonly Color Lime = new Color(0.55f, 1f, 0.3f), Mid = new Color(0.2f, 0.8f, 0.35f), Amb = new Color(1f, 0.72f, 0.1f), Red = new Color(1f, 0.32f, 0.3f);

        sealed class Seg { public float Deg; public Material Mat; public Color Col; public Transform T; }

        readonly List<Seg> _segs = new List<Seg>();
        Transform _wedge, _ball, _hud, _block, _angleTag;
        LineRenderer _trail, _aim, _needle;
        TextMesh _kick, _big, _of, _side, _angle;
        Transform _plate, _ring;
        Material _plateMat, _ringMat, _enterMat;
        TextMesh _enter;
        Seg _lit;
        float _line, _shown, _hudPop = -1f;
        Camera _eye;
        // The hold: seconds the read stays up, then it fades and the window turns to the next putt's line.
        const float Hold = 3f, FadeTime = 0.45f;
        float _readAt = -1f, _fadeT = -1f, _nextLine, _ringT = -1f;
        int _ringsLeft;
        bool _preview, _haveNext;
        Color _kickCol, _bigCol, _ofCol, _sideCol, _angleCol, _needleCol;
        static readonly Color PlateCol = new Color(0.01f, 0.03f, 0.02f, 0.72f), TrailCol = new Color(1f, 0.95f, 0.75f, 0.75f), AimCol = new Color(1f, 1f, 1f, 0.22f);

        // The cup's floor slopes down to the back, where the ball rolls out (the Puttr cup is a through-hole).
        const float RampFront = -0.35f * Depth, RampBack = -0.72f * Depth;
        static float RampY(float z) => Mathf.Lerp(RampFront, RampBack, Mathf.InverseLerp(-CupR, CupR, z));
        static float RampTilt => Mathf.Atan2(RampFront - RampBack, 2f * CupR) * Mathf.Rad2Deg;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(0f, Portrait ? 92f : 54f, Portrait ? -66f : -38f);
        public override Vector3 CameraLook => At(0f, 0f, Portrait ? 14f : 4f);
        public override float CameraFov => Portrait ? 42f : 38f;
        public override float ShadowDistance => 500f;

        static Vector3 Rim(float deg, float r) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, 0f, -Mathf.Cos(a) * r); }
        static float AngleOf(Vector3 p) => Mathf.Atan2(p.x, -p.z) * Mathf.Rad2Deg;

        // ── The set ──────────────────────────────────────────────────────────

        public override void Build()
        {
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.16f, 0.5f, 0.24f), 0.12f, 0f, pile, pileNormal, 0.9f);
            const float uv = 1f / (10f * K);
            Make.MeshObject("Mat by the cup", Root, Make.HolePatch(FarEnd, CupR, uv, Vector3.zero, 1f, 96), turf);
            Make.MeshObject("Mat left", Root, Flat(-MatHalf, -FarEnd, -FarEnd, FarEnd, uv), turf);
            Make.MeshObject("Mat right", Root, Flat(FarEnd, MatHalf, -FarEnd, FarEnd, uv), turf);
            Make.MeshObject("Mat near", Root, Flat(-MatHalf, MatHalf, -1000f, -FarEnd, uv), turf);
            Material edge = Make.Lit(new Color(0.05f, 0.2f, 0.1f), 0.2f);
            // The mat's edges, and the floor round it (nothing may cross the cup's column, or it would show in the hole).
            for (int sd = -1; sd <= 1; sd += 2)
                Shape(PrimitiveType.Cube, "Mat edge", Root, new Vector3(sd * (MatHalf + 0.6f), -1.6f, -480f), new Vector3(1.2f, 3.2f, 1030f), edge);
            GameObject floor = Make.MeshObject("Floor", Root, Make.HolePatch(1500f, CupR + 1f, 0f, Vector3.zero, 1f, 48), Make.Lit(new Color(0.05f, 0.055f, 0.06f), 0.35f));
            floor.transform.localPosition = new Vector3(0, -3.2f, 0);
            // The foam stop at the far end of the mat.
            GameObject stop = Make.Panel("Back stop", Root, new Vector3(0, 5f, FarEnd + 6f), new Vector3(MatHalf * 2f + 8f, 13f, 12f), 4f, Make.Lit(new Color(0.07f, 0.08f, 0.09f), 0.25f));
            stop.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
            Shape(PrimitiveType.Cube, "Stop stripe", Root, new Vector3(0, 9f, FarEnd - 0.2f), new Vector3(MatHalf * 2f + 6f, 1.2f, 0.4f), Make.Emissive(Color.black, Make.Green * 1.4f));

            // The cup: a white liner at the top, black below (as on the real mat), a floor sloping down to the back and the mouth of the
            // return tunnel in the back wall, where a make rolls out (as on the Puttr device).
            Make.MeshObject("Liner", Root, Make.Cup(CupR, 1.1f * K, false, 72), Make.Lit(new Color(0.92f, 0.93f, 0.9f), 0.45f));
            GameObject inner = Make.MeshObject("Cup", Root, Make.Cup(CupR, Depth - 1.1f * K, true, 72), Make.Lit(new Color(0.015f, 0.015f, 0.017f), 0.15f));
            inner.transform.localPosition = new Vector3(0, -1.1f * K, 0);
            GameObject ramp = Make.MeshObject("Cup floor", Root, Make.Disc(CupR - 0.05f, 72), Make.Lit(new Color(0.01f, 0.01f, 0.012f), 0.1f));
            ramp.transform.localPosition = new Vector3(0, (RampFront + RampBack) / 2f, 0);
            ramp.transform.localRotation = Quaternion.Euler(RampTilt, 0, 0);
            ramp.transform.localScale = new Vector3(1f, 1f, 1f / Mathf.Cos(RampTilt * Mathf.Deg2Rad));
            Make.MeshObject("Return tunnel", Root, Mouth(CupR - 0.06f, 34f, RampBack - 0.5f, RampBack + BallR * 2.2f), Make.Glow(new Color(0.01f, 0.01f, 0.012f, 1f), false))
                .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // The window round the rim, laid out round its own line (turned to the tee's line).
            _wedge = new GameObject("Window").transform;
            _wedge.SetParent(Root, false);
            for (float d = -90f; d < 270f - 0.01f; d += Step)
            {
                var s = new Seg { Deg = d + Step / 2f, Col = ColourFor(d + Step / 2f) };
                s.Mat = Make.Glow(Dim(s.Col), false);
                GameObject go = Make.MeshObject("Window " + d, _wedge, Arc(BandIn, BandOut, d + 0.4f, d + Step - 0.4f, 6), s.Mat);
                go.transform.localPosition = new Vector3(0, 0.12f, 0);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                s.T = go.transform;
                _segs.Add(s);
            }
            Material tick = Make.Glow(new Color(1f, 1f, 1f, 0.85f), false);
            foreach (float d in new[] { -90f, -18f, 18f, 90f })
            {
                GameObject go = Make.MeshObject("Tick", _wedge, Arc(BandIn - 0.2f * K, BandOut + 0.35f * K, d - 0.8f, d + 0.8f, 1), tick);
                go.transform.localPosition = new Vector3(0, 0.16f, 0);
            }
            TextMesh back = Words(_wedge, "BACK DOOR", Rim(180f, CupR + 0.9f * K) + Vector3.up * 0.25f, 0.55f * K, new Color(1f, 0.55f, 0.5f, 0.9f));
            back.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            // Before a putt: where to come in, on the line in front of the band.
            _enter = Words(_wedge, "ENTER HERE", Rim(0f, BandOut + 1.5f * K) + Vector3.up * 0.25f, 0.5f * K, new Color(1f, 1f, 1f, 0f));
            _enter.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            MeshRenderer er = _enter.GetComponent<MeshRenderer>();
            er.sharedMaterial = _enterMat = new Material(er.sharedMaterial);

            // The celebration ring: a pulse out from the rim on a make.
            _ringMat = Make.Glow(new Color(Lime.r, Lime.g, Lime.b, 0f), true);
            _ring = Hoop("Pulse", Root, new Vector3(0, 0.3f, 0), CupR + 0.1f * K, CupR + 0.55f * K, _ringMat).transform;
            _ring.gameObject.SetActive(false);

            _aim = Stroke(Root, "Perfect line", AimCol, 0.5f, false);
            _trail = Stroke(Root, "Replay line", TrailCol, 0.75f);
            _needle = Stroke(Root, "Entry", Color.white, 0.7f);
            _angle = Words(Root, "", Vector3.zero, 0.9f * K, Color.white);
            _angleTag = _angle.transform;

            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, new Vector3(0, BallR, -60f), Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.65f, 0f, bc, bn)).transform;
            _ball.gameObject.SetActive(false);

            // The read, in front of the camera (laid out in units of half the view's height).
            _hud = new GameObject("Read").transform;
            _hud.SetParent(Root, false);
            _block = new GameObject("Block").transform;
            _block.SetParent(_hud, false);
            Material plate = _plateMat = Make.Glow(PlateCol, false);
            plate.renderQueue = 3600;
            _plate = Make.MeshObject("Plate", _block, RoundRect(1f, 1f), plate).transform;
            _plate.GetComponent<MeshRenderer>().sortingOrder = 20;
            _kick = Words(_block, "", new Vector3(0, 0.17f, -0.01f), 0.055f, Lime);
            _big = Words(_block, "", new Vector3(0.07f, 0.0f, -0.01f), 0.19f, Color.white, TextAnchor.MiddleRight, 160);
            _of = Words(_block, "", new Vector3(0.1f, -0.025f, -0.01f), 0.075f, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
            _side = Words(_block, "", new Vector3(0, -0.165f, -0.01f), 0.045f, new Color(1f, 1f, 1f, 0.85f));
            foreach (TextMesh t in new[] { _kick, _big, _of, _side, _angle })
            {
                MeshRenderer m = t.GetComponent<MeshRenderer>();
                m.sharedMaterial = new Material(m.sharedMaterial) { renderQueue = t == _angle ? 3200 : 3700 };
                if (t != _angle) m.sortingOrder = 30;
            }
            _hud.gameObject.SetActive(false);
            _kickCol = _kick.color; _bigCol = _big.color; _ofCol = _of.color; _sideCol = _side.color; _angleCol = Color.white; _needleCol = Color.white;

            Sun(Root, new Vector3(58f, 25f, 0f), new Color(1f, 0.98f, 0.92f), 0.7f);
            Lamp(Root, new Vector3(-40f, 260f, -110f), new Vector3(0, 0, 0), new Color(1f, 0.97f, 0.9f), 180000f, 55f, 900f);
            Lamp(Root, new Vector3(0, 120f, 25f), new Vector3(0, -Depth, 0), new Color(0.95f, 1f, 0.95f), 9000f, 22f, 400f);
        }

        // ── The window ───────────────────────────────────────────────────────

        // The share a make keeps at d degrees off the line: lime for the center cut, then green, amber, red. The back door is dark red.
        static Color ColourFor(float d)
        {
            float off = Mathf.Abs(Mathf.DeltaAngle(0f, d));
            if (off >= 90f) return new Color(0.45f, 0.08f, 0.08f);
            if (off < 18f) return Lime;
            float keep = 1f - off / 90f;
            return keep > 0.5f ? Color.Lerp(Amb, Mid, (keep - 0.5f) / 0.3f) : Color.Lerp(Red, Amb, keep / 0.5f);
        }
        static Color Dim(Color c) => new Color(c.r, c.g, c.b, 0.5f);

        // The window's line: snapped, or swung round to it over a moment (Tick eases the shown line in).
        void Turn(float line, bool snap = true)
        {
            _line = line;
            if (snap) Show(line);
        }

        void Show(float line)
        {
            _shown = line;
            _wedge.localRotation = Quaternion.Euler(0, -line, 0);
            Points(_aim, new[] { Rim(line, 130f * K) + Vector3.up * 0.1f, Rim(line, BandOut + 0.4f * K) + Vector3.up * 0.1f });
        }

        static void Tint(Material m, Color c, float alpha) => m.SetColor("_Color", new Color(c.r, c.g, c.b, c.a * alpha));

        void ResetSegs() { foreach (Seg s in _segs) s.Mat.SetColor("_Color", Dim(s.Col)); }

        // The next putt's desired entry: the window turns to its line, the center cut pulses, the line brightens.
        void Preview(float line)
        {
            _hud.gameObject.SetActive(false);
            LightUp(null);
            _needle.positionCount = 0;
            _angle.text = "";
            _trail.positionCount = 0;
            _ball.gameObject.SetActive(false);
            Turn(line, false);
            _preview = true;
        }

        // Out of the preview, back to the plain window.
        void EndPreview()
        {
            _preview = false;
            _readAt = -1f; _fadeT = -1f;
            ResetSegs();
            Tint(_aim.sharedMaterial, AimCol, 1f);
            _enter.color = new Color(1f, 1f, 1f, 0f);
            SetReadAlpha(1f);
        }

        void SetReadAlpha(float a)
        {
            _kick.color = Fade(_kickCol, a); _big.color = Fade(_bigCol, a); _of.color = Fade(_ofCol, a); _side.color = Fade(_sideCol, a);
            _angle.color = Fade(_angleCol, a);
            Tint(_plateMat, PlateCol, a);
            Tint(_needle.sharedMaterial, _needleCol, a);
            Tint(_trail.sharedMaterial, TrailCol, a);
            if (_lit != null) Tint(_lit.Mat, new Color(Mathf.Min(1f, _lit.Col.r * 1.3f + 0.15f), Mathf.Min(1f, _lit.Col.g * 1.3f + 0.15f), Mathf.Min(1f, _lit.Col.b * 1.3f + 0.15f), 1f), Mathf.Lerp(0.5f, 1f, a));
        }
        static Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        void Pulse(int rings) { _ringsLeft = rings; _ringT = 0f; _ring.gameObject.SetActive(true); }

        void LightUp(Seg s)
        {
            if (_lit != null) { _lit.Mat.SetColor("_Color", Dim(_lit.Col)); _lit.T.localScale = Vector3.one; _lit.T.localPosition = new Vector3(0, 0.12f, 0); }
            _lit = s;
            if (s == null) return;
            s.Mat.SetColor("_Color", new Color(Mathf.Min(1f, s.Col.r * 1.3f + 0.15f), Mathf.Min(1f, s.Col.g * 1.3f + 0.15f), Mathf.Min(1f, s.Col.b * 1.3f + 0.15f), 1f));
            s.T.localPosition = new Vector3(0, 0.2f, 0);
        }

        Seg SegFor(float offLine)
        {
            Seg best = null;
            float bestD = 999f;
            foreach (Seg s in _segs) { float d = Mathf.Abs(Mathf.DeltaAngle(s.Deg, offLine)); if (d < bestD) { bestD = d; best = s; } }
            return best;
        }

        // ── The game ─────────────────────────────────────────────────────────

        // The game opens on the first putt's desired entry.
        public override void Begin(ShotView start)
        {
            EndPreview();
            Turn(start.Num("nextLine"));
            Preview(start.Num("nextLine"));
        }

        // After the putt plays: hold the read, then (Tick) fade it and turn to the next putt's line.
        public override void Ready(ShotView view)
        {
            _haveNext = !view.Over && view.Data.ContainsKey("nextLine");
            _nextLine = view.Num("nextLine", _line);
        }

        public override void Tick()
        {
            FitHud();
            float dt = Time.deltaTime;
            if (Mathf.Abs(Mathf.DeltaAngle(_shown, _line)) > 0.05f) Show(Mathf.LerpAngle(_shown, _line, 1f - Mathf.Exp(-dt * 5f)));
            else if (_shown != _line) Show(_line);

            // The hold, then the fade, then the next putt's entry.
            if (_readAt >= 0f && _haveNext && Time.time - _readAt > Hold) { _readAt = -1f; _fadeT = 0f; }
            if (_fadeT >= 0f)
            {
                _fadeT += dt / FadeTime;
                SetReadAlpha(1f - Ease(Mathf.Min(1f, _fadeT)));
                _ball.localScale = Vector3.one * BallR * 2f * (1f - Ease(Mathf.Min(1f, _fadeT)));
                if (_fadeT >= 1f)
                {
                    _fadeT = -1f;
                    SetReadAlpha(1f);
                    _ball.localScale = Vector3.one * BallR * 2f;
                    Preview(_nextLine);
                }
            }
            if (_preview)
            {
                // The center cut breathes, the perfect line brightens, the label comes up.
                float w = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
                foreach (Seg s in _segs)
                    if (Mathf.Abs(Mathf.DeltaAngle(s.Deg, 0f)) < 18f) s.Mat.SetColor("_Color", Color.Lerp(Dim(s.Col), new Color(0.75f, 1f, 0.55f, 1f), w));
                Tint(_aim.sharedMaterial, new Color(0.85f, 1f, 0.7f, 1f), Mathf.Lerp(0.45f, 0.85f, w));
                Color e = _enter.color;
                _enter.color = new Color(0.85f, 1f, 0.7f, Mathf.MoveTowards(e.a, 0.95f, dt * 2f));
            }
            if (_ringT >= 0f)
            {
                _ringT += dt / 0.7f;
                float t = Mathf.Min(1f, _ringT);
                float sc = Mathf.Lerp(1f, (BandOut + 2.2f * K) / CupR, Ease(t));
                _ring.localScale = new Vector3(sc, 1f, sc);
                Tint(_ringMat, new Color(Lime.r, Lime.g, Lime.b, 1f), 1f - t);
                if (t >= 1f)
                {
                    if (--_ringsLeft > 0) _ringT = 0f;
                    else { _ringT = -1f; _ring.gameObject.SetActive(false); }
                }
            }
            if (_lit != null && _fadeT < 0f)
            {
                float k = 1f + 0.06f * Mathf.Sin(Time.time * 6f);
                _lit.T.localScale = new Vector3(k, 1f, k);
            }
            if (_angleTag != null && _eye != null) _angleTag.rotation = Quaternion.LookRotation(_angleTag.position - _eye.transform.position, _eye.transform.up);
            if (_hudPop >= 0f)
            {
                _hudPop += Time.deltaTime / 0.35f;
                float t = Mathf.Min(1f, _hudPop);
                float s = t < 0.7f ? Mathf.Lerp(0.6f, 1.08f, Ease(t / 0.7f)) : Mathf.Lerp(1.08f, 1f, (t - 0.7f) / 0.3f);
                _block.localScale = Vector3.one * s;
                if (t >= 1f) _hudPop = -1f;
            }
        }

        // The read rides on the stage camera, low in the middle of the view, the same size whatever the camera does.
        void FitHud()
        {
            if (_eye == null || !_eye.isActiveAndEnabled)
            {
                _eye = null;
                foreach (Camera c in Camera.allCameras) if (c.farClipPlane >= 8999f) { _eye = c; break; }
            }
            if (_eye == null || _hud == null) return;
            const float dist = 40f;
            float half = dist * Mathf.Tan(_eye.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Transform cam = _eye.transform;
            // Up on the right, clear of the tee sign (top left) and the cup.
            _hud.position = cam.position + cam.forward * dist + cam.right * (0.5f * _eye.aspect * half) + cam.up * ((Portrait ? 0.2f : 0.5f) * half);
            _hud.rotation = cam.rotation;
            float s = Root.lossyScale.x > 0f ? Root.lossyScale.x : 1f;
            _hud.localScale = Vector3.one * (half / s);
        }

        public override IEnumerator Play(ShotView v)
        {
            EndPreview();
            _hud.gameObject.SetActive(false);
            LightUp(null);
            _needle.positionCount = 0;
            _angle.text = "";
            _ball.localScale = Vector3.one * BallR * 2f;
            // From the previewed line round to the one the ball was judged against (the same for a putt from the tee).
            Turn(v.Num("line", _line), false);

            float entry = v.HasEntry ? v.EntryDeg : _line;
            bool made = v.Made, back = v.BackDoor || (made && Mathf.Abs(Mathf.DeltaAngle(_line, entry)) >= 90f);
            List<Vector3> pts = Track(v, entry);
            float stopAt = made ? CupR + (back ? BallR * 0.25f : 0f) : -1f;
            int cut = pts.Count;
            if (made)
            {
                for (int i = 1; i < pts.Count; i++)
                    if (Flat(pts[i]).magnitude < stopAt)
                    {
                        // Where the roll crossed the lip (or the entry the read gives, if the camera's path disagrees).
                        Vector3 a = pts[i - 1], b = pts[i];
                        float ra = Flat(a).magnitude, rb = Flat(b).magnitude;
                        Vector3 at = Vector3.Lerp(a, b, Mathf.InverseLerp(ra, rb, stopAt));
                        if (!back && Mathf.Abs(Mathf.DeltaAngle(AngleOf(at), entry)) > 40f) at = Rim(entry, CupR);
                        pts[i] = at; cut = i + 1; break;
                    }
                pts.RemoveRange(cut, pts.Count - cut);
                Vector3 end = pts[pts.Count - 1];
                if (Flat(end).magnitude > stopAt + 0.5f) pts.Add(back ? Rim(AngleOf(end), stopAt) : Rim(entry, CupR));
            }
            // A miss that crossed the cup rode the lip: keep the ball's center on the rim there, dipped into the hole.
            var dip = new float[pts.Count];
            bool lipped = false;
            if (!made)
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 f = Flat(pts[i]);
                    float r = f.magnitude;
                    if (r < CupR + BallR * 0.35f)
                    {
                        lipped = true;
                        float rr = Mathf.Max(r, CupR);
                        if (r < 0.01f) f = Vector3.back;
                        pts[i] = f.normalized * rr;
                        dip[i] = Mathf.InverseLerp(CupR + BallR * 0.35f, CupR, r) * BallR * 0.45f;
                    }
                }
            for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(pts[i].x, BallR - dip[i], pts[i].z);

            _ball.gameObject.SetActive(true);
            _ball.localPosition = pts[0];
            _trail.positionCount = 0;
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
            float seconds = Mathf.Clamp(total / 170f, 0.9f, 2.2f);
            float keep = made ? (back ? 0.55f : 0.42f) : 0f;
            yield return Roll(pts, seconds, keep, lipped);
            float vEnd = keep * 2f * total / (seconds * (1f + keep));
            Vector3 dir = pts.Count > 1 ? Flat(pts[pts.Count - 1] - pts[pts.Count - 2]).normalized : Vector3.forward;

            if (made)
            {
                Vector3 vel = dir * vEnd;
                if (back)
                {
                    yield return RideRim(AngleOf(_ball.localPosition), entry, vEnd);
                    Vector3 inward = -Rim(entry, 1f);
                    vel = inward * 40f + Vector3.Cross(Vector3.up, inward) * 10f;
                }
                else
                {
                    // Tip over the lip toward the middle, at least a little.
                    Vector3 inward = -Rim(entry, 1f);
                    if (Vector3.Dot(vel, inward) < 25f) vel += inward * (25f - Vector3.Dot(vel, inward));
                }
                yield return DropOut(vel);
            }
            else yield return new WaitForSeconds(0.15f);

            // The read: the window lights where it went in, the needle points at the entry, the score comes up.
            if (made)
            {
                float off = Mathf.DeltaAngle(_line, entry);
                LightUp(SegFor(off));
                Color c = back ? Red : ColourFor(off);
                _needleCol = new Color(Mathf.Min(1f, c.r * 1.6f), Mathf.Min(1f, c.g * 1.6f), Mathf.Min(1f, c.b * 1.6f), 1f);
                _needle.sharedMaterial.SetColor("_Color", _needleCol);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
                {
                    Points(_needle, new[] { Rim(entry, CupR) + Vector3.up * 0.4f, Rim(entry, Mathf.Lerp(CupR + 0.1f, BandOut + 1.2f * K, Ease(t))) + Vector3.up * 0.4f });
                    yield return null;
                }
                Points(_needle, new[] { Rim(entry, CupR) + Vector3.up * 0.4f, Rim(entry, BandOut + 1.2f * K) + Vector3.up * 0.4f });
                int deg = Mathf.RoundToInt(Mathf.Abs(off));
                _angle.text = back ? "BACK" : deg == 0 ? "0°" : deg + "° " + (off < 0 ? "L" : "R");
                _angle.color = _angleCol = new Color(c.r, c.g, c.b, 1f);
                _angleTag.localPosition = Rim(entry, BandOut + 1.7f * K) + Vector3.up * 2f;
                Celebrate(v, entry, off);
            }
            else _angleCol = Color.white;
            ShowRead(v, lipped);
            if (!made || !v.Good) Sound("rule_miss", 0.55f);
            yield return new WaitForSeconds(made ? 1.2f : 0.9f);
        }

        // A make scores: the bigger the share kept, the bigger the party. A center cut gets sparks from the entry,
        // confetti over the cup, two rings off the rim and a cheer.
        void Celebrate(ShotView v, float entry, float off)
        {
            if (!v.Good || Arcade == null) return;
            int feet = v.Feet > 0 ? v.Feet : 6;
            double share = v.Points / feet;
            Vector3 lip = At(Rim(entry, CupR) + Vector3.up * 1f);
            if (v.Title == "CENTER CUT" || share > 0.9)
            {
                Arcade.Sparks(lip, 40);
                Arcade.Celebrate(60, At(0f, 6f, 0f));
                Arcade.Shake(0.25f);
                Pulse(2);
                Sound("rule_bonus", 0.8f);
                Sound("golf_clap", 0.55f);
                Sound("bell_ding", 0.4f, 1.25f);
            }
            else if (share > 0.4)
            {
                Arcade.Sparks(lip, 22);
                Pulse(1);
                Sound("rule_make", 0.7f);
                Sound("golf_clap", 0.3f);
            }
            else
            {
                Arcade.Sparks(lip, 10);
                Sound("rule_make", 0.5f, 0.9f);
            }
        }

        void ShowRead(ShotView v, bool lipped)
        {
            int feet = v.Feet > 0 ? v.Feet : 6;
            Color c = !v.Good ? Red : v.Points / feet > 0.9 ? Lime : v.Points / feet > 0.7 ? Mid : v.Points / feet > 0.4 ? Color.white : Amb;
            _kick.text = v.Good ? v.Title : v.BackDoor ? "BACK DOOR" : lipped ? "LIP OUT" : "MISS";
            _kick.color = c;
            _big.text = v.Points.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            _big.color = v.Good ? c : new Color(1f, 1f, 1f, 0.9f);
            _of.text = "/ " + feet;
            // The entry in words, unless the call already says it (a center cut); the degrees are on the needle.
            string side = v.Str("side").ToUpperInvariant();
            _side.text = v.Good ? (side == _kick.text ? "" : side) : v.BackDoor ? "IN THE BACK OF THE CUP" : v.MissSide == "LEFT" ? "MISSED LEFT" : v.MissSide == "RIGHT" ? "MISSED RIGHT" : v.HasRollout && v.RolloutIn < 0 ? "SHORT" : "NO SCORE";
            _kickCol = _kick.color; _bigCol = _big.color;
            SetReadAlpha(1f);
            _readAt = Time.time;
            float w = Mathf.Max(0.5f, Mathf.Max(_side.text.Length * 0.034f, _kick.text.Length * 0.042f) + 0.1f);
            bool third = _side.text.Length > 0;
            _plate.localScale = new Vector3(w, third ? 0.46f : 0.36f, 1f);
            _plate.localPosition = new Vector3(0, third ? 0f : 0.05f, 0f);
            _hud.gameObject.SetActive(true);
            _block.localScale = Vector3.one * 0.6f;
            _hudPop = 0f;
            FitHud();
        }

        // ── The roll ─────────────────────────────────────────────────────────

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);

        // The camera's roll in scene units, from the bottom of the view on; a made-up one when there is none.
        List<Vector3> Track(ShotView v, float entry)
        {
            var pts = new List<Vector3>();
            const float from = -40f * K;
            foreach (Vector2 p in v.Path)
            {
                if (p.y * K < from) continue;
                var q = new Vector3(Mathf.Clamp(p.x * K, -MatHalf + BallR, MatHalf - BallR), 0f, Mathf.Min(p.y * K, FarEnd - BallR));
                if (pts.Count == 0 || Vector3.Distance(pts[pts.Count - 1], q) > 0.6f) pts.Add(q);
            }
            if (pts.Count < 3)
            {
                pts.Clear();
                Vector3 start = Rim(_line, 40f * K);
                Vector3 end = v.Made ? Rim(entry, CupR * 0.5f)
                    : new Vector3(Mathf.Clamp(Mathf.Abs(v.LineIn) > 2.6f ? v.LineIn * K : (v.MissSide == "LEFT" ? -1f : 1f) * (CupR + BallR + 2f), -MatHalf + BallR, MatHalf - BallR), 0f,
                                  Mathf.Clamp((v.HasRollout ? v.RolloutIn : 8f) * K, -30f * K, FarEnd - BallR));
                Vector3 mid = Vector3.Lerp(start, end, 0.55f) + new Vector3((start.x - end.x) * 0.15f, 0, 0);
                for (int i = 0; i <= 30; i++) { float t = i / 30f, u = 1f - t; pts.Add(u * u * start + 2f * u * t * mid + t * t * end); }
                return pts;
            }
            // Lead in from the bottom of the view along the way it was rolling.
            if (pts[0].z > from + 10f)
            {
                Vector3 d = (pts[Mathf.Min(3, pts.Count - 1)] - pts[0]).normalized;
                if (d.z > 0.2f) pts.Insert(0, pts[0] - d * ((pts[0].z - from) / d.z));
            }
            return pts;
        }

        // Along the points, slowing at a steady rate to keep (a share of its starting speed) at the end. The replay
        // line draws behind it.
        IEnumerator Roll(List<Vector3> pts, float seconds, float keep, bool lipped)
        {
            int n = pts.Count;
            if (n < 2) yield break;
            var cum = new float[n];
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float total = Mathf.Max(0.001f, cum[n - 1]);
            var drawn = new List<Vector3> { Line(pts[0]) };
            int seg = 0;
            Vector3 last = pts[0];
            bool rattled = false;
            for (float t = 0f; ; t += Time.deltaTime / seconds)
            {
                float u = Mathf.Min(1f, t);
                float s = total * (u - (1f - keep) * u * u / 2f) / ((1f + keep) / 2f);
                while (seg < n - 2 && cum[seg + 1] < s) { seg++; drawn.Add(Line(pts[seg])); }
                Vector3 p = Vector3.Lerp(pts[seg], pts[seg + 1], Mathf.InverseLerp(cum[seg], cum[seg + 1], s));
                if (lipped && !rattled && p.y < BallR * 0.8f) { rattled = true; Sound("rim_clang", 0.35f, 1.6f); Sound("golf_cup", 0.4f, 1.3f); }
                Spin(p - last);
                RollSound(p - last);
                _ball.localPosition = p;
                last = p;
                _trail.positionCount = drawn.Count + 1;
                for (int i = 0; i < drawn.Count; i++) _trail.SetPosition(i, drawn[i]);
                _trail.SetPosition(drawn.Count, Line(p));
                if (t >= 1f) break;
                yield return null;
            }
        }

        static Vector3 Line(Vector3 p) => new Vector3(p.x, 0.3f, p.z);

        // No roll hum on Center Cut (Matt): the only sound of the putt is the ball dropping into the cup.
        void RollSound(Vector3 step)
        {
            if (Arcade != null) Arcade.Audio.SetRoll(0f);
        }

        void Spin(Vector3 step)
        {
            step.y = 0f;
            if (step.sqrMagnitude > 1e-6f) _ball.localRotation = Quaternion.AngleAxis(step.magnitude / BallR * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, step.normalized)) * _ball.localRotation;
        }

        // A back door: round the lip, sagging into the hole and rattling, slowing, to where it fell in.
        IEnumerator RideRim(float from, float to, float speed)
        {
            float sweep = Mathf.DeltaAngle(from, to);
            float r = CupR - BallR * 0.1f;
            float w0 = Mathf.Max(60f, speed / r * Mathf.Rad2Deg);
            float seconds = Mathf.Clamp(Mathf.Abs(sweep) / (w0 * 0.7f), 0.35f, 1.1f);
            Sound("rim_clang", 0.4f, 1.5f);
            Vector3 last = _ball.localPosition;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float u = Mathf.Clamp01(t * (1.3f - 0.3f * t)); // slowing
                float a = from + sweep * u;
                float sag = BallR * (0.25f + 0.25f * t) + Mathf.Abs(Mathf.Sin(t * 22f)) * BallR * 0.12f * (1f - t);
                Vector3 p = Rim(a, r) + Vector3.up * (BallR - sag);
                Spin(p - last);
                _ball.localPosition = p;
                last = p;
                if (Mathf.Sin(t * 22f) * Mathf.Sin((t + Time.deltaTime / seconds) * 22f) < 0f && t < 0.7f) Sound("golf_cup", 0.18f, 1.6f);
                yield return null;
            }
        }

        // Into the cup with real gravity (in slow motion): off the wall and onto the cup's floor, which slopes down
        // to the back, then down the slope and out through the return tunnel in the back wall, as a made putt does
        // on the Puttr device. Nothing comes to rest in the cup.
        IEnumerator DropOut(Vector3 vel)
        {
            const float g = 386f * K, slow = 0.42f;
            Vector3 p = _ball.localPosition;
            float wallR = CupR - BallR, lift = BallR / Mathf.Cos(RampTilt * Mathf.Deg2Rad), shown = 0f, landedAt = -1f;
            while (shown < 1.2f && (landedAt < 0f || shown - landedAt < 0.3f))
            {
                float dt = Time.deltaTime * slow;
                shown += Time.deltaTime;
                for (int k = 0; k < 4; k++)
                {
                    float h = dt / 4f;
                    vel.y -= g * h;
                    Vector3 before = p;
                    p += vel * h;
                    Vector3 f = Flat(p);
                    float r = f.magnitude;
                    bool outward = Vector3.Dot(f, Flat(vel)) > 0f;
                    if (r > wallR && (p.y < -BallR * 0.2f || outward) && r > 0.01f)
                    {
                        Vector3 nrm = f / r;
                        float vr = Vector3.Dot(Flat(vel), nrm);
                        if (vr > 0f)
                        {
                            vel -= nrm * vr * 1.4f;
                            if (vr > 30f) Sound("knock", Mathf.Clamp01(vr / 300f) * 0.5f, 1.4f);
                        }
                        float push = Mathf.Min(r - wallR, 120f * h + (p.y < -BallR * 0.2f ? 0f : r - wallR));
                        p -= nrm * push;
                    }
                    float floorY = RampY(p.z) + lift;
                    if (p.y < floorY)
                    {
                        p.y = floorY;
                        if (vel.y < -25f && landedAt < 0f) { landedAt = shown; Sound("golf_cup", 1f); }
                        else if (vel.y < -25f) Sound("knock", 0.2f, 1.6f);
                        vel.y = -vel.y * 0.3f;
                        vel.x *= 0.7f; vel.z *= 0.7f;
                    }
                    Spin(p - before);
                }
                _ball.localPosition = p;
                yield return null;
            }

            // Down the slope, steering into the tunnel's mouth, and away under the mat.
            Vector3 v = new Vector3(vel.x * 0.3f, 0f, Mathf.Max(vel.z * 0.2f, 8f));
            float accel = g * Mathf.Sin(RampTilt * Mathf.Deg2Rad) * 0.2f, mouth = CupR * Mathf.Sin(34f * Mathf.Deg2Rad) - BallR * 1.05f;
            bool rattled = false;
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                float dt = Time.deltaTime;
                v.z += accel * dt;
                v.x = Mathf.Lerp(v.x, -p.x * 5f, 1f - Mathf.Exp(-dt * 8f));
                Vector3 before = p;
                p.x += v.x * dt; p.z += v.z * dt;
                Vector3 f = Flat(p);
                if (f.magnitude > wallR && Mathf.Abs(p.x) > mouth && p.z < CupR) { Vector3 nrm = f.normalized; p -= nrm * (f.magnitude - wallR); }
                if (p.z > CupR - BallR * 1.2f) p.x = Mathf.Clamp(p.x, -mouth, mouth);
                p.y = RampY(Mathf.Min(p.z, CupR)) + lift - Mathf.Max(0f, p.z - CupR) * 0.35f;
                if (!rattled && p.z > CupR - BallR * 1.5f) { rattled = true; Sound("golf_cup", 0.3f, 0.7f); Sound("ball_land", 0.3f, 0.75f); }
                Spin(p - before);
                RollSound(p - before);
                _ball.localPosition = p;
                if (p.z > CupR + BallR * 2.6f) break;
                yield return null;
            }
            _ball.gameObject.SetActive(false);
        }

        // ── Meshes ───────────────────────────────────────────────────────────

        // A flat strip of carpet, mapped by where it lies.
        static Mesh Flat(float x0, float x1, float z0, float z1, float uv)
        {
            var mesh = new Mesh { name = "Carpet" };
            mesh.vertices = new[] { new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { new Vector2(x0 * uv, z0 * uv), new Vector2(x0 * uv, z1 * uv), new Vector2(x1 * uv, z1 * uv), new Vector2(x1 * uv, z0 * uv) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A piece of a flat ring between two angles (the scene's angles: 0 toward the Player, + to the right), both sides.
        static Mesh Arc(float inner, float outer, float d0, float d1, int n)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float d = Mathf.Lerp(d0, d1, i / (float)n);
                verts.Add(Rim(d, inner)); verts.Add(Rim(d, outer));
            }
            for (int i = 0; i < n; i++)
            {
                int a = i * 2;
                tris.AddRange(new[] { a, a + 3, a + 1, a, a + 2, a + 3 });
                tris.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
            }
            var mesh = new Mesh { name = "Arc" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // The return tunnel's mouth: an arch on the back of the cup's wall (radius r, half as wide as halfDeg either
        // side of straight back), from y0 up to y1 at its crown, both sides.
        static Mesh Mouth(float r, float halfDeg, float y0, float y1)
        {
            const int n = 24;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float h = y1 - y0;
            for (int i = 0; i <= n; i++)
            {
                float u = Mathf.Lerp(-1f, 1f, i / (float)n);
                Vector3 at = Rim(180f + u * halfDeg, r);
                verts.Add(new Vector3(at.x, y0, at.z));
                verts.Add(new Vector3(at.x, y0 + h * (0.55f + 0.45f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u))), at.z));
            }
            for (int i = 0; i < n; i++)
            {
                int a = i * 2;
                tris.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
                tris.AddRange(new[] { a, a + 3, a + 1, a, a + 2, a + 3 });
            }
            var mesh = new Mesh { name = "Mouth" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // A unit plate (scaled to fit the read) with rounded corners, facing the camera (-Z), both sides.
        static Mesh RoundRect(float w, float h)
        {
            const int arc = 6;
            float r = 0.08f;
            var outline = new List<Vector3>();
            Vector2[] corners = { new Vector2(w / 2 - r, h / 2 - r), new Vector2(-w / 2 + r, h / 2 - r), new Vector2(-w / 2 + r, -h / 2 + r), new Vector2(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= arc; i++)
                {
                    float a = (c * 90f + i * 90f / arc) * Mathf.Deg2Rad;
                    outline.Add(new Vector3(corners[c].x + Mathf.Cos(a) * r, corners[c].y + Mathf.Sin(a) * r, 0));
                }
            var verts = new List<Vector3> { Vector3.zero };
            verts.AddRange(outline);
            var tris = new List<int>();
            for (int i = 0; i < outline.Count; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % outline.Count;
                tris.AddRange(new[] { 0, a, b, 0, b, a });
            }
            var mesh = new Mesh { name = "Plate" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
