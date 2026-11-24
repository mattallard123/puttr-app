// Square Up, "The Gate": a low look down the target line in a dark arena. From the ball a laser gate projects forward:
// two tall posts and an amber wedge at ±gate degrees round the center (your normal or true zero), drawn K times wider
// than life so a degree can be seen. Each stroke replays with the face turn exaggerated the same way, and at impact a
// beam leaves the face at its face change: inside the gate it runs clean through and the gate glows; outside it hits
// the post line in red and a ball drops off the rack of lives beside the lane. A cleared level grinds the posts inward;
// calibration strokes leave white marks and the third one raises the gate round their median.
// Units: inches; the ball is at the origin, the target down +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class SquareUpScene : SensorStage
    {
        const float K = 7f, D = 46f, PostH = 14f, MaxDeg = 52f;
        static readonly Color Amber = Make.Hex(0xF59E0B), Clean = Make.Hex(0xFFD36B), Bad = Make.Hex(0xFF4A3D);

        Putter _putter;
        Transform _ball, _postL, _postR, _gateRoot;
        Material _wedgeMat, _postMatL, _postMatR, _beamMat;
        Mesh _wedge;
        LineRenderer _edgeL, _edgeR, _beam, _centre;
        TextMesh _gateLabel;
        readonly List<Transform> _lives = new List<Transform>();
        readonly List<LineRenderer> _marks = new List<LineRenderer>();
        Readout _read;
        float _center, _gate, _shown = 1f, _glow;
        bool _hasCenter;

        public override Vector3 CameraFrom => Portrait ? At(0f, 30f, -36f) : At(0f, 24f, -34f);
        public override Vector3 CameraLook => Portrait ? At(4f, 7f, 34f) : At(2f, 5f, 34f);
        public override float CameraFov => Portrait ? 44f : 40f;
        public override float ShadowDistance => 260f;

        static float Clamp(float deg) => Mathf.Clamp(deg * K, -MaxDeg, MaxDeg);
        /// <summary>Straight down the line at a face change (+ closed points left), drawn K times wider.</summary>
        static Vector3 Dir(float faceDeg) => Quaternion.Euler(0f, -Clamp(faceDeg), 0f) * Vector3.forward;

        public override void Build()
        {
            // The arena: a dark floor, a lane of turf, a back wall with an amber stripe.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -0.6f, 60f), new Vector3(400f, 1f, 400f), Make.Lit(new Color(0.035f, 0.035f, 0.04f), 0.55f));
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.14f, 0.42f, 0.2f), 0.12f, 0f, pile, pileNormal, 0.9f);
            turf.mainTextureScale = new Vector2(2f, 8f);
            Shape(PrimitiveType.Cube, "Lane", Root, new Vector3(0, -0.08f, 36f), new Vector3(30f, 0.16f, 110f), turf);
            Material rim = Make.Lit(new Color(0.06f, 0.07f, 0.08f), 0.6f, 0.3f);
            for (int s = -1; s <= 1; s += 2) Shape(PrimitiveType.Cube, "Lane edge", Root, new Vector3(s * 15.6f, 0.4f, 36f), new Vector3(1.2f, 1f, 110f), rim);
            Shape(PrimitiveType.Cube, "Back wall", Root, new Vector3(0, 30f, 125f), new Vector3(260f, 70f, 2f), Make.Lit(new Color(0.05f, 0.05f, 0.06f), 0.3f));
            GameObject stripe = Shape(PrimitiveType.Cube, "Stripe", Root, new Vector3(0, 9f, 123.8f), new Vector3(260f, 1.2f, 0.4f), Make.Emissive(Color.black, Amber * 1.6f));
            NoShadow(stripe);
            for (int i = -3; i <= 3; i++)
            {
                GameObject pillar = Shape(PrimitiveType.Cube, "Pillar", Root, new Vector3(i * 36f, 30f, 121f), new Vector3(3f, 70f, 3f), Make.Lit(new Color(0.09f, 0.09f, 0.1f), 0.5f, 0.4f));
                pillar.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // The gate: a wedge on the turf, its two edges, two posts, the center line, the gate's size over it.
            _gateRoot = new GameObject("Gate").transform;
            _gateRoot.SetParent(Root, false);
            _wedge = new Mesh { name = "Wedge" };
            _wedgeMat = Make.Glow(new Color(Amber.r, Amber.g, Amber.b, 0.22f), false);
            GameObject w = Make.MeshObject("Wedge", _gateRoot, _wedge, _wedgeMat);
            w.transform.localPosition = new Vector3(0, 0.06f, 0);
            NoShadow(w);
            _edgeL = Stroke(_gateRoot, "Edge", Amber, 0.35f);
            _edgeR = Stroke(_gateRoot, "Edge", Amber, 0.35f);
            _centre = Stroke(_gateRoot, "Center", new Color(1f, 1f, 1f, 0.35f), 0.18f, false);
            _postMatL = Make.Emissive(new Color(0.2f, 0.12f, 0.02f), Amber * 2.2f, 0.7f);
            _postMatR = Make.Emissive(new Color(0.2f, 0.12f, 0.02f), Amber * 2.2f, 0.7f);
            _postL = Post(_postMatL);
            _postR = Post(_postMatR);
            _gateLabel = Words(_gateRoot, "", Vector3.zero, 2.6f, Clean);

            _beamMat = Make.Glow(Color.white, true);
            _beam = Stroke(Root, "Beam", Color.white, 0.9f);
            _beam.sharedMaterial = _beamMat;

            // The rack of lives beside the lane.
            Material rack = Make.Lit(new Color(0.12f, 0.12f, 0.13f), 0.7f, 0.6f);
            Shape(PrimitiveType.Cube, "Rack", Root, new Vector3(9.5f, 0.5f, 16.2f), new Vector3(2.4f, 1f, 8.4f), rack);
            for (int i = 0; i < 3; i++) _lives.Add(BuildBall(Root, new Vector3(9.5f, 1f, 13.6f + i * 2.6f)));

            _ball = BuildBall(Root, Vector3.zero);
            _putter = BuildPutter(Root, new Vector3(0, 0, -1.75f), Amber);
            _putter.FaceScale = K;

            _read = new Readout(this, Portrait ? new Vector2(0.56f, -0.74f) : new Vector2(0.62f, -0.72f), Amber) { Size = Portrait ? 0.8f : 0.6f };

            Sun(Root, new Vector3(52f, -25f, 0f), new Color(1f, 0.96f, 0.9f), 0.55f);
            Lamp(Root, new Vector3(0, 70f, -20f), new Vector3(0, 0, 30f), new Color(1f, 0.95f, 0.88f), 60000f, 50f, 600f);
            Lamp(Root, new Vector3(0, 60f, 90f), new Vector3(0, 0, 55f), new Color(1f, 0.85f, 0.6f), 45000f, 55f, 500f);
        }

        Transform Post(Material m)
        {
            Transform t = new GameObject("Post").transform;
            t.SetParent(_gateRoot, false);
            Cylinder("Post", t, new Vector3(0, PostH / 2f, 0), 0.9f, PostH, m);
            Cylinder("Base", t, new Vector3(0, 0.4f, 0), 2f, 0.8f, Make.Lit(new Color(0.1f, 0.1f, 0.11f), 0.6f, 0.5f));
            GameObject cap = Shape(PrimitiveType.Sphere, "Cap", t, new Vector3(0, PostH + 0.6f, 0), Vector3.one * 2.4f, m);
            NoShadow(cap);
            return t;
        }

        // ── The gate ─────────────────────────────────────────────────────────

        void Lay(float center, float gate, float shown)
        {
            float a = center - gate, b = center + gate;
            Vector3 pa = Dir(a) * D, pb = Dir(b) * D;
            _postL.localPosition = pb; _postR.localPosition = pa;   // + is left
            _gateRoot.localScale = new Vector3(1f, Mathf.Max(0.001f, shown), 1f);
            Points(_edgeL, new[] { new Vector3(0, 0.15f, 1f), pb + Vector3.up * 0.15f });
            Points(_edgeR, new[] { new Vector3(0, 0.15f, 1f), pa + Vector3.up * 0.15f });
            Points(_centre, new[] { new Vector3(0, 0.12f, 1f), Dir(center) * (D + 8f) + Vector3.up * 0.12f });
            var verts = new List<Vector3> { Vector3.zero };
            const int n = 24;
            for (int i = 0; i <= n; i++) verts.Add(Dir(Mathf.Lerp(a, b, i / (float)n)) * D);
            var tris = new List<int>();
            for (int i = 1; i <= n; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); tris.Add(0); tris.Add(i + 1); tris.Add(i); }
            _wedge.Clear();
            _wedge.SetVertices(verts);
            _wedge.SetTriangles(tris, 0);
            _wedge.SetNormals(verts.ConvertAll(_ => Vector3.up));
            _wedge.RecalculateBounds();
            _gateLabel.text = "±" + gate.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "°";
            _gateLabel.transform.localPosition = Dir(center) * (D * 0.72f) + Vector3.up * 3.2f;
            _gateLabel.gameObject.SetActive(shown > 0.99f);   // squashed with the gate it shears into a mirror image: up only once the gate stands
        }

        void Marks(float[] faces, bool show)
        {
            foreach (LineRenderer m in _marks) Make.Kill(m.gameObject);
            _marks.Clear();
            if (!show) return;
            for (int i = Mathf.Max(0, faces.Length - 8); i < faces.Length; i++)
            {
                LineRenderer m = Stroke(Root, "Mark", new Color(1f, 1f, 1f, 0.3f + 0.4f * (i + 1f) / faces.Length), 0.45f);
                Points(m, new[] { Dir(faces[i]) * (D - 14f) + Vector3.up * 0.2f, Dir(faces[i]) * (D - 2f) + Vector3.up * 0.2f });
                _marks.Add(m);
            }
        }

        void Lives(int n) { for (int i = 0; i < _lives.Count; i++) { _lives[i].gameObject.SetActive(i < n); _lives[i].localPosition = new Vector3(9.5f, 1.84f, 13.6f + i * 2.6f); } }

        void State(ShotView v)
        {
            _hasCenter = v.Flag("hasCenter");
            _center = v.Num("center");
            _gate = v.Num("gate", 2f);
            _shown = _hasCenter ? 1f : 0.3f;   // calibrating: a low ghost of the opening gate, round zero, until GATE SET raises it
            Lay(_center, _gate, _shown);
            _gateRoot.gameObject.SetActive(true);
            Lives(v.Int("balls", 3));
            Marks(Floats(v, "faces"), true);
        }

        public override void Begin(ShotView start)
        {
            State(start);
            _ball.localPosition = new Vector3(0, 0.84f, 0);
            _beam.positionCount = 0;
            _read.Hide();
            Pose(_putter, 0f, 0f);
        }

        public override void Ready(ShotView v)
        {
            _ball.localPosition = new Vector3(0, 0.84f, 0);
            _ball.gameObject.SetActive(true);
            _beam.positionCount = 0;
            State(v);
        }

        public override void Tick()
        {
            _read.Tick(Eye(), Root);
            Face(_gateLabel.transform);
            if (_glow > 0f)
            {
                _glow = Mathf.Max(0f, _glow - Time.deltaTime * 1.4f);
                _wedgeMat.SetColor("_Color", new Color(Amber.r, Amber.g, Amber.b, 0.22f + 0.35f * _glow));
            }
            else _wedgeMat.SetColor("_Color", new Color(Amber.r, Amber.g, Amber.b, 0.2f + 0.04f * Mathf.Sin(Time.time * 2.5f)));
        }

        // ── The putt ─────────────────────────────────────────────────────────

        public override IEnumerator Play(ShotView v)
        {
            StrokeData s = StrokeOf(v);
            _read.Hide();
            bool calib = !v.Flag("hasCenter") || (v.Flag("hasCenter") && !_hasCenter);   // a calibration stroke (the third sets the gate)
            bool gateSet = v.Flag("hasCenter") && !_hasCenter && !v.Flag("zero");
            bool inside = calib || v.Flag("inside");
            bool cleared = v.Flag("cleared");
            float judged = calib ? 0f : v.Num("lastGate", _gate);
            int livesBefore = _lives.FindAll(b => b.gameObject.activeSelf).Count;
            int lost = !calib && !inside ? v.Int("balls") : -1;

            bool hit = false;
            yield return Swing(_putter, s, () => { hit = true; Sound("putter_strike", "golf_iron", 0.35f, 1.3f); });   // puttr-app-2: the putter on the ball, shared by every sensor replay (the old sound until it is recorded)

            // The beam and the ball go out together.
            Color c = calib ? Color.white : inside ? Clean : Bad;
            _beamMat.SetColor("_Color", new Color(c.r, c.g, c.b, 1f) * 1.4f);
            Sound("squareup_beam", "laser_zap", 0.45f, inside ? 1.2f : 0.85f);   // puttr-app-2: the game's own sound, the shared one until recorded
            float reach = inside ? D + 46f : D;
            Vector3 from = new Vector3(0, 0.9f, 1f), dir = Dir(s.Face);
            Vector3 ballFrom = _ball.localPosition, ballTo = ballFrom + Quaternion.Euler(0, -Clamp(s.Face) / 3f, 0) * Vector3.forward * 46f;
            Vector3 lastBall = ballFrom;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 1.0f)
            {
                float u = Mathf.Min(1f, t / 0.35f);
                Points(_beam, new[] { from, from + dir * Mathf.Lerp(2f, reach, Ease(u)) });
                float r = 1f - (1f - t) * (1f - t);
                Vector3 p = Vector3.Lerp(ballFrom, ballTo, r);
                Vector3 step = p - lastBall;
                if (step.sqrMagnitude > 1e-6f) _ball.localRotation = Quaternion.AngleAxis(step.magnitude / 0.84f * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, step.normalized)) * _ball.localRotation;
                _ball.localPosition = p;
                lastBall = p;
                yield return null;
            }
            Vector3 end = from + dir * reach;

            if (calib)
            {
                _read.Show("FACE", Signed(s.Face) + "°", "", "", Color.white);
                if (gateSet)
                {
                    // The gate rises round the median.
                    _hasCenter = true;
                    _center = v.Num("center"); _gate = v.Num("gate", 2f);
                    _gateRoot.gameObject.SetActive(true);
                    Sound("squareup_block", "riser", 0.5f);   // puttr-app-2: the game's own sound, the shared one until recorded
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.7f) { Lay(_center, _gate, Mathf.Lerp(0.3f, 1f, Ease(t))); yield return null; }
                    Lay(_center, _gate, 1f);
                    Arcade.Sparks(At(Dir(_center) * D + Vector3.up * PostH), 30);
                    _glow = 1f;
                }
                else yield return new WaitForSeconds(0.5f);
            }
            else if (inside)
            {
                _glow = 1f;
                Arcade.Sparks(At(end), 14);
                _read.Show("FACE", Signed(s.Face) + "°", "", "", Clean);
                yield return new WaitForSeconds(0.25f);
                if (cleared)
                {
                    // The posts grind inward to the next level's gate.
                    float next = v.Num("gate", judged);
                    Sound("squareup_block", "stone_grind", 0.6f);   // puttr-app-2: the game's own sound, the shared one until recorded
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.75f) { Lay(_center, Mathf.Lerp(judged, next, Ease(t)), 1f); yield return null; }
                    _gate = next;
                    Lay(_center, _gate, 1f);
                    Sound("gate", 0.6f);
                    Arcade.Celebrate(20, At(Dir(_center) * D + Vector3.up * 6f));
                }
                else yield return new WaitForSeconds(0.35f);
            }
            else
            {
                // It struck the gate: the post on that side flashes red, a life drops off the rack.
                Material post = s.Face - _center > 0 ? _postMatL : _postMatR;
                post.SetColor("_EmissionColor", Bad * 3f);
                Arcade.Sparks(At(end), 40);
                Arcade.Shake(0.35f);
                Sound("squareup_miss", "anvil", 0.5f);   // puttr-app-2: the beam breaks on the post, Metal's anvil until recorded
                _read.Show("FACE", Signed(s.Face) + "°", "", "", Bad);
                Transform life = lost >= 0 && lost < _lives.Count && livesBefore > lost ? _lives[lost] : null;
                if (life != null)
                {
                    Vector3 a = life.localPosition, b = a + new Vector3(3.2f, -1f, 0.5f), cc = b + new Vector3(26f, 0f, 4f);
                    yield return Arc(life, a, b, 0.25f, 1.2f);
                    if (!Has("squareup_miss")) Sound("thud", 0.4f);   // puttr-app-2: the beam break says it once recorded
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.7f)
                    {
                        life.localPosition = Vector3.Lerp(b, cc, 1f - (1f - t) * (1f - t));
                        life.localRotation = Quaternion.AngleAxis(t * 900f, Vector3.back) * Quaternion.identity;
                        yield return null;
                    }
                    life.gameObject.SetActive(false);
                }
                else yield return new WaitForSeconds(0.6f);
                post.SetColor("_EmissionColor", Amber * 2.2f);
            }
            yield return Settle(_putter, 0.45f);
        }
    }
}
