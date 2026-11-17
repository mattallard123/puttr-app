// Dead Center through a scope: the camera sits low behind the ball, looking down the putt, and everything outside the
// lens is black. The crosshair's reticle sits on the cup and a red laser marks the perfect line from the tee into it.
// The putt rolls up the green leaving a teal line beside the laser, then the scope zooms in on the cup to show the
// entry: the red needle is the perfect entry, the teal one is where the ball went in (or the side it missed on).
// Attempts from the same spot stay on the green, faint. Units are inches; the cup is at the origin.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class DeadCenterScene : RuleScene
    {
        const float LaneW = 48f, BallR = 1.1f, CupR = 2.4f, LensD = 20f, Fov = 34f;
        static readonly Color Teal = new Color(0.25f, 0.95f, 0.85f), Laser = new Color(1f, 0.22f, 0.2f);

        Transform _ball, _start, _scope, _lens, _cupCam;
        LineRenderer _laser, _trail, _needleYou, _needlePerfect;
        Material _reticleMat;
        readonly List<LineRenderer> _old = new List<LineRenderer>();
        readonly List<LineRenderer> _lensLines = new List<LineRenderer>();
        readonly List<float> _lensWidths = new List<float>();
        Vector3 _cam, _look;
        float _fov = Fov, _fovTo = Fov;
        int _feet = -1, _tee = -1;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(_cam);
        public override Vector3 CameraLook => At(_look);
        public override float CameraFov => _fov;

        static Vector3 SpotFor(int feet, int tee) => new Vector3((tee - 2) * 8f, 0f, -feet * 12f);
        static float TeeEntry(int feet, int tee) => Mathf.Round(Mathf.Atan((tee == 1 ? -8f : tee == 3 ? 8f : 0f) / (feet * 12f)) * Mathf.Rad2Deg) * 1.1f;
        // The way into the cup for an entry angle: from behind the cup (where the ball comes from), swung by the angle.
        static Vector3 EntryDir(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0, -Mathf.Cos(deg * Mathf.Deg2Rad));

        public override void Build()
        {
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.08f, 0.3f, 0.2f), 0.1f, 0f, pile, pileNormal, 0.8f);
            turf.SetTextureScale("_BaseMap", new Vector2(3f, 14f));
            Material room = Make.Lit(new Color(0.02f, 0.07f, 0.08f), 0.3f);
            Material hole = Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f);
            Material white = Make.Emissive(Color.white, new Color(0.7f, 0.7f, 0.7f));

            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1.6f, 0f), new Vector3(800f, 2f, 900f), room);
            Shape(PrimitiveType.Cube, "Back wall", Root, new Vector3(0, 100f, 160f), new Vector3(800f, 300f, 4f), Make.Lit(new Color(0.03f, 0.1f, 0.11f), 0.3f));
            // Range panels on the back wall: faint vertical bars, like a firing range.
            Material bar = Make.Emissive(Color.black, new Color(0.08f, 0.3f, 0.3f));
            for (int i = -8; i <= 8; i++)
                Shape(PrimitiveType.Cube, "Bar", Root, new Vector3(i * 26f, 60f, 157.5f), new Vector3(1.2f, 120f, 0.5f), bar);
            Shape(PrimitiveType.Cube, "Green", Root, new Vector3(0, -0.5f, -72f), new Vector3(LaneW, 1f, 180f), turf);
            Material edge = Make.Emissive(Color.black, Teal * 0.6f);
            for (int s = -1; s <= 1; s += 2)
                Shape(PrimitiveType.Cube, "Edge", Root, new Vector3(s * (LaneW / 2f + 0.3f), 0.15f, -72f), new Vector3(0.6f, 0.4f, 180f), edge);
            Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), hole).transform.localPosition = new Vector3(0, 0.03f, 0);
            Hoop("Lip", Root, new Vector3(0, 0.05f, 0), CupR, CupR + 0.35f, white);
            _reticleMat = Make.Emissive(Color.black, Teal * 2f);
            Hoop("Reticle", Root, new Vector3(0, 0.07f, 0), CupR + 1.6f, CupR + 2f, _reticleMat);
            for (int k = 0; k < 4; k++)
            {
                Vector3 d = Quaternion.Euler(0, k * 90f, 0) * Vector3.forward;
                Points(Stroke(Root, "Reticle tick", Teal, 0.3f), new[] { d * (CupR + 2.4f) + Vector3.up * 0.08f, d * (CupR + 5f) + Vector3.up * 0.08f });
            }
            // Foot lines across the green.
            Material tick = Make.Glow(new Color(0.6f, 1f, 1f, 0.08f), false);
            for (int f = 1; f <= 12; f++)
                Shape(PrimitiveType.Quad, "Foot", Root, new Vector3(0, 0.03f, -f * 12f), new Vector3(LaneW, 0.3f, 1f), tick).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            _laser = Stroke(Root, "Perfect line", Laser * 0.85f, 0.5f);
            _trail = Stroke(Root, "Your roll", Teal * 0.8f, 0.6f);
            _needlePerfect = Stroke(Root, "Perfect entry", Laser * 1.6f, 0.35f);
            _needleYou = Stroke(Root, "Your entry", Teal * 1.6f, 0.35f);

            _start = new GameObject("Start").transform;
            _start.SetParent(Root, false);
            Hoop("Spot", _start, new Vector3(0, 0.05f, 0), 2.6f, 3.1f, Make.Emissive(Color.black, Teal * 1.2f));
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(Color.white, new Color(0.3f, 0.3f, 0.3f), 0.7f)).transform;

            // The scope: a black mask with a round hole, a bezel, a crosshair with ticks and a legend.
            _scope = new GameObject("Scope").transform;
            _scope.SetParent(Root, false);
            _lens = new GameObject("Lens").transform;
            _lens.SetParent(_scope, false);
            _lens.localPosition = new Vector3(0, 0, LensD);
            Material black = Make.Glow(new Color(0f, 0f, 0f, 1f), false);
            black.renderQueue = 3500;
            GameObject mask = Make.MeshObject("Mask", _lens, Make.Ring(1f, 60f, 96), black);
            mask.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            mask.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Material rim = Make.Glow(new Color(Teal.r, Teal.g, Teal.b, 0.8f));
            rim.renderQueue = 3550;
            GameObject bezel = Make.MeshObject("Bezel", _lens, Make.Ring(0.985f, 1.02f, 96), rim);
            bezel.transform.localRotation = Quaternion.Euler(-90f, 0, 0);
            Color hair = new Color(Teal.r, Teal.g, Teal.b, 0.55f);
            Points(Stroke(_lens, "Hair", hair, 0.006f), new[] { new Vector3(-1f, 0, -0.01f), new Vector3(-0.08f, 0, -0.01f) });
            Points(Stroke(_lens, "Hair", hair, 0.006f), new[] { new Vector3(0.08f, 0, -0.01f), new Vector3(1f, 0, -0.01f) });
            Points(Stroke(_lens, "Hair", hair, 0.006f), new[] { new Vector3(0, -1f, -0.01f), new Vector3(0, -0.08f, -0.01f) });
            Points(Stroke(_lens, "Hair", hair, 0.006f), new[] { new Vector3(0, 0.08f, -0.01f), new Vector3(0, 1f, -0.01f) });
            for (int i = 1; i <= 8; i++)
            {
                float k = i * 0.11f, l = i % 2 == 0 ? 0.03f : 0.018f;
                foreach (int s in new[] { -1, 1 })
                {
                    Points(Stroke(_lens, "Tick", hair, 0.005f), new[] { new Vector3(s * k, -l, -0.01f), new Vector3(s * k, l, -0.01f) });
                    Points(Stroke(_lens, "Tick", hair, 0.005f), new[] { new Vector3(-l, s * k, -0.01f), new Vector3(l, s * k, -0.01f) });
                }
            }
            // The legend under the lens (in lens units; Aim scales the lens to the view).
            Points(Stroke(_lens, "Key red", Laser * 1.4f, 0.012f), new[] { new Vector3(-0.98f, -1.1f, -0.01f), new Vector3(-0.88f, -1.1f, -0.01f) });
            Words(_lens, "PERFECT LINE", new Vector3(-0.85f, -1.1f, -0.01f), 0.055f, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
            Points(Stroke(_lens, "Key teal", Teal, 0.012f), new[] { new Vector3(0.88f, -1.1f, -0.01f), new Vector3(0.98f, -1.1f, -0.01f) });
            Words(_lens, "YOUR ROLL", new Vector3(0.85f, -1.1f, -0.01f), 0.055f, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleRight);

            foreach (LineRenderer l in _lens.GetComponentsInChildren<LineRenderer>())
            {
                l.sharedMaterial.renderQueue = 3600;
                _lensLines.Add(l);
                _lensWidths.Add(l.widthMultiplier);
            }
            foreach (TextMesh t in _lens.GetComponentsInChildren<TextMesh>()) { MeshRenderer m = t.GetComponent<MeshRenderer>(); m.sharedMaterial = new Material(m.sharedMaterial) { renderQueue = 3700 }; }

            Sun(Root, new Vector3(55f, 20f, 0f), new Color(0.8f, 1f, 1f), 0.25f);
            Lamp(Root, new Vector3(0, 220f, -60f), new Vector3(0, 0, -40f), new Color(0.85f, 1f, 1f), 110000f, 60f, 800f);
            Lamp(Root, new Vector3(0, 90f, 40f), new Vector3(0, 0, 0), new Color(0.8f, 1f, 0.95f), 30000f, 30f, 400f);
        }

        // The view's aspect (RuleStageMode's viewport) and the lens radius that fits it.
        static float Aspect => Portrait ? Screen.width / (Screen.height * 0.52f) : Screen.width * 0.58f / Mathf.Max(1f, Screen.height);

        Camera _eye;

        // The lens rides on the stage camera itself (which eases toward CameraFrom / CameraFov), so the hole always
        // sits centred on the view at the same size, whatever the camera is doing.
        void FitLens()
        {
            if (_eye == null || !_eye.isActiveAndEnabled)
                foreach (Camera c in Camera.allCameras) if (c.farClipPlane >= 8999f) { _eye = c; break; }
            float fov = _eye != null ? _eye.fieldOfView : _fov;
            float aspect = _eye != null ? _eye.aspect : Aspect;
            if (_eye != null) { _scope.position = _eye.transform.position; _scope.rotation = _eye.transform.rotation; }
            float half = LensD * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float r = Mathf.Min(half, half * aspect) * 0.94f;
            _lens.localScale = Vector3.one * r;
            float s = _scope.lossyScale.z;
            for (int i = 0; i < _lensLines.Count; i++) _lensLines[i].widthMultiplier = _lensWidths[i] * r * s;
        }

        void Aim(int feet, int tee)
        {
            Vector3 from = SpotFor(feet, tee);
            _start.localPosition = from;
            _ball.localPosition = from + Vector3.up * BallR;
            _ball.gameObject.SetActive(true);
            Vector3 back = new Vector3(from.x, 0, from.z).normalized;
            _cam = from + back * 46f + Vector3.up * (16f + feet * 0.7f);
            _look = new Vector3(0, 0.5f, 0);
            _scope.localPosition = _cam;
            _scope.localRotation = Quaternion.LookRotation(_look - _cam, Vector3.up);
            _fov = _fovTo = Fov;
            FitLens();
            Points(_laser, new[] { from + Vector3.up * 0.12f, new Vector3(0, 0.12f, 0) });
            if (feet != _feet || tee != _tee)
            {
                foreach (LineRenderer l in _old) Make.Kill(l.gameObject);
                _old.Clear();
            }
            _feet = feet; _tee = tee;
            _needleYou.positionCount = 0;
            _needlePerfect.positionCount = 0;
        }

        public override void Begin(ShotView start) { Aim(start.NextFeet, start.NextTee); }
        public override void Ready(ShotView view)
        {
            if (view.Over) return; // found it: leave the winning roll and the zoom on screen
            if (_trail.positionCount > 1)
            {
                LineRenderer keep = Stroke(Root, "Old roll", new Color(Teal.r, Teal.g, Teal.b, 0.22f), 0.45f);
                var pts = new Vector3[_trail.positionCount];
                _trail.GetPositions(pts);
                Points(keep, pts);
                _old.Add(keep);
                while (_old.Count > 6) { Make.Kill(_old[0].gameObject); _old.RemoveAt(0); }
            }
            _trail.positionCount = 0;
            Aim(view.NextFeet, view.NextTee);
        }

        public override void Tick()
        {
            if (_lens == null) return;
            _fov = Mathf.Lerp(_fov, _fovTo, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            FitLens();
            if (_reticleMat != null) _reticleMat.SetColor("_EmissionColor", Teal * (1.6f + 0.6f * Mathf.Sin(Time.time * 4f)));
        }

        public override IEnumerator Play(ShotView v)
        {
            int feet = v.Feet > 0 ? v.Feet : 6, tee = v.Tee > 0 ? v.Tee : 2;
            if (feet != _feet || tee != _tee) Aim(feet, tee);
            Sound("synth_whoosh", 0.45f, 0.8f);
            _trail.positionCount = 0;
            List<Vector3> roll = Roll(v, SpotFor(feet, tee), BallR);
            yield return Along(_ball, roll, Mathf.Lerp(0.9f, 1.6f, Mathf.InverseLerp(3f, 11f, feet)), _trail);
            if (v.Made)
            {
                Vector3 top = _ball.localPosition;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = Vector3.Lerp(top, new Vector3(0, -BallR * 2f, 0), t); yield return null; }
                _ball.gameObject.SetActive(false);
            }

            // Zoom in on the cup: the perfect entry and yours.
            _fovTo = Mathf.Lerp(12f, 7f, Mathf.InverseLerp(3f, 11f, feet));
            float perfect = TeeEntry(feet, tee);
            Points(_needlePerfect, new[] { Vector3.up * 0.2f, EntryDir(perfect) * (CupR + 4.5f) + Vector3.up * 0.2f });
            if (v.HasEntry)
            {
                Vector3 to = EntryDir(v.EntryDeg) * (CupR + 4.5f) + Vector3.up * 0.25f;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f)
                {
                    Points(_needleYou, new[] { Vector3.up * 0.25f, Vector3.Lerp(EntryDir(perfect) * (CupR + 4.5f) + Vector3.up * 0.25f, to, Ease(t)) });
                    yield return null;
                }
            }
            bool dead = v.Good;
            if (dead)
            {
                Sound("bell_ding", 0.6f, 1.2f);
                Arcade.Sparks(At(0, 1f, 0), 24);
                _reticleMat.SetColor("_EmissionColor", new Color(0.6f, 1f, 0.4f) * 3f);
            }
            yield return new WaitForSeconds(dead ? 1.4f : 1.0f);
            if (!v.Over) _fovTo = Fov;
            yield return new WaitForSeconds(0.25f);
        }

        // ── The roll ─────────────────────────────────────────────────────────

        static List<Vector3> Roll(ShotView v, Vector3 start, float y)
        {
            var pts = new List<Vector3> { new Vector3(start.x, y, start.z) };
            if (v.Path != null && v.Path.Count >= 2)
            {
                int step = Mathf.Max(1, v.Path.Count / 40);
                for (int i = 0; i < v.Path.Count; i += step)
                {
                    Vector2 p = v.Path[i];
                    if (p.y < start.z + 4f) continue;
                    pts.Add(new Vector3(Mathf.Clamp(p.x, -LaneW / 2f + 2f, LaneW / 2f - 2f), y, Mathf.Min(p.y, 20f)));
                }
                Vector2 last = v.Path[v.Path.Count - 1];
                pts.Add(new Vector3(Mathf.Clamp(last.x, -LaneW / 2f + 2f, LaneW / 2f - 2f), y, Mathf.Min(last.y, 20f)));
            }
            else
            {
                float endZ = v.Reached ? (v.Made ? 0f : Mathf.Clamp(v.HasRollout ? v.RolloutIn : 8f, 2f, 20f)) : Mathf.Min(-3f, v.HasRollout ? v.RolloutIn : -8f);
                Vector3 at = new Vector3(v.Made ? v.LineIn * 0.4f : v.LineIn + (v.MissSide == "LEFT" ? -3f : v.MissSide == "RIGHT" ? 3f : 0f), y, endZ);
                Vector3 mid = Vector3.Lerp(pts[0], at, 0.5f) + new Vector3(-at.x * 0.5f, 0, 0);
                for (int i = 1; i <= 16; i++) { float t = i / 16f; pts.Add((1 - t) * (1 - t) * pts[0] + 2 * (1 - t) * t * mid + t * t * at); }
            }
            Vector3 end = pts[pts.Count - 1];
            if (v.Made && new Vector2(end.x, end.z).magnitude > 0.6f) pts.Add(new Vector3(0, y, 0));
            return pts;
        }

        static IEnumerator Along(Transform ball, List<Vector3> pts, float seconds, LineRenderer trail)
        {
            var cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float total = Mathf.Max(0.01f, cum[pts.Count - 1]);
            var drawn = new List<Vector3>();
            int seg = 1;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float s = (1f - (1f - t) * (1f - t)) * total;
                while (seg < pts.Count - 1 && cum[seg] < s) seg++;
                Vector3 p = Vector3.Lerp(pts[seg - 1], pts[seg], Mathf.InverseLerp(cum[seg - 1], cum[seg], s));
                Vector3 step = p - ball.localPosition;
                if (step.sqrMagnitude > 0.0001f) ball.Rotate(Vector3.Cross(Vector3.up, step.normalized), step.magnitude / BallR * Mathf.Rad2Deg, Space.World);
                ball.localPosition = p;
                drawn.Add(new Vector3(p.x, 0.15f, p.z));
                if (trail != null) Points(trail, drawn);
                yield return null;
            }
            ball.localPosition = pts[pts.Count - 1];
        }
    }
}
