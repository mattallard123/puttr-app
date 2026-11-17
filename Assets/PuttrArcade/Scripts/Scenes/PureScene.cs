// Pure: the Crystal. A dark cavern with a faceted crystal floating over three glass columns, FACE, RHYTHM and LIE.
// Each putt replays the real stroke on a plinth in front; at impact a pulse of light runs from the putter face into the
// columns, which fill to the stroke's three Vertex scores (green 75+, amber 40 to 74, red under 40), the raw value at
// each base. Then the crystal takes the purity, their average: its colour, its glow. A Pure Strike (all three 75+)
// flares it and bursts shards. A ring of fifteen small crystals on the floor keeps every stroke's purity colour.
// Units: inches; the ball sits at the origin, the target is +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class PureScene : SensorStage
    {
        const float ColH = 24f, ColR = 3.2f, ColZ = 40f, PX = -14f, PZ = 14f;
        static readonly Vector3 CrystalAt = new Vector3(60f, 32f, 96f);
        static readonly Color Silver = Make.Hex(0xC7CFDA), Dim = new Color(0.32f, 0.36f, 0.42f);

        sealed class Column
        {
            public Transform Fill, Cap;
            public Material FillMat, CapMat;
            public TextMesh Name, Value, Score;
            public float Shown, Target;
            public Color Colour = Dim;
        }

        readonly Column[] _cols = new Column[3];
        readonly List<Material> _ring = new List<Material>();
        readonly List<Transform> _ringT = new List<Transform>();
        Transform _crystal, _halo, _ball;
        Material _crystalMat, _haloMat;
        Light _glow;
        Color _crystalColour = Dim, _crystalTo = Dim;
        float _flare, _spin = 18f, _bright = 0.25f, _yaw;
        Putter _putter;
        Readout _read;
        Transform _pulse;

        public override Vector3 CameraFrom => Portrait ? At(-22f, 32f, -56f) : At(-26f, 28f, -24f);
        public override Vector3 CameraLook => Portrait ? At(16f, 26f, 56f) : At(18f, 22f, 60f);
        public override float CameraFov => Portrait ? 56f : 44f;

        // A crystal: a six-sided bipyramid with flat facets (each face its own vertices).
        static Mesh Gem(float r, float up, float down, int sides = 6)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3 top = new Vector3(0, up, 0), bottom = new Vector3(0, -down, 0);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), p1 = new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
                int b = verts.Count;
                verts.Add(top); verts.Add(p1); verts.Add(p0);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                b = verts.Count;
                verts.Add(bottom); verts.Add(p0); verts.Add(p1);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            var m = new Mesh { name = "Gem" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public override void Build()
        {
            // The cavern: a dark polished floor, rocks and dim crystals round the edge.
            GameObject floor = Make.MeshObject("Floor", Root, Make.Disc(400f, 64), Make.Lit(new Color(0.05f, 0.055f, 0.07f), 0.6f, 0.1f));
            floor.transform.localPosition = new Vector3(0, -0.05f, 60f);
            Material rock = Make.Lit(new Color(0.07f, 0.075f, 0.09f), 0.35f);
            Material shard = Make.Emissive(new Color(0.2f, 0.24f, 0.32f), new Color(0.12f, 0.16f, 0.3f), 0.9f);
            var dice = new System.Random(5);
            for (int i = 0; i < 26; i++)
            {
                float a = (float)(dice.NextDouble() * 200 - 10) * Mathf.Deg2Rad, d = 130f + (float)dice.NextDouble() * 110f;
                Vector3 at = new Vector3(Mathf.Cos(a) * d, 0, 60f + Mathf.Sin(a) * d);
                float h = 10f + (float)dice.NextDouble() * 36f;
                GameObject g = Make.MeshObject("Shard", Root, Gem(2f + h * 0.12f, h, 2f), i % 3 == 0 ? rock : shard);
                g.transform.localPosition = at;
                g.transform.localRotation = Quaternion.Euler((float)dice.NextDouble() * 30f - 15f, (float)dice.NextDouble() * 360f, (float)dice.NextDouble() * 30f - 15f);
            }
            Shape(PrimitiveType.Sphere, "Dome", Root, new Vector3(0, 0, 60f), new Vector3(-900f, -500f, -900f), Make.Lit(new Color(0.03f, 0.035f, 0.05f), 0.1f));

            // The plinth, the putter on it, the ball.
            Material stone = Make.Lit(new Color(0.12f, 0.13f, 0.15f), 0.6f);
            Make.Panel("Plinth", Root, new Vector3(PX, -1.5f, PZ - 2f), new Vector3(20f, 3f, 34f), 1.2f, stone);
            Shape(PrimitiveType.Cube, "Edge light", Root, new Vector3(PX, 0.02f, PZ + 15.1f), new Vector3(20f, 0.15f, 0.3f), Make.Emissive(Color.black, Silver * 1.6f));
            _putter = BuildPutter(Root, new Vector3(PX - 0.4f, 0f, PZ - 2.4f), Silver);
            _putter.FaceScale = 2f;
            _ball = BuildBall(Root, new Vector3(PX, 0f, PZ));

            // The three columns.
            string[] names = { "FACE", "RHYTHM", "LIE" };
            Material glass = Make.Transparent(new Color(0.75f, 0.85f, 1f, 0.16f), 0.95f);
            Material brass = Make.Lit(new Color(0.55f, 0.58f, 0.64f), 0.85f, 0.9f);
            for (int i = 0; i < 3; i++)
            {
                var c = new Column();
                Vector3 b = new Vector3(4f + i * 19f, 0f, ColZ + i * 4f);
                Cylinder("Base", Root, b + Vector3.up * 1.2f, ColR + 1.4f, 2.4f, brass);
                Cylinder("Top", Root, b + Vector3.up * (ColH + 3.4f), ColR + 1.1f, 1.2f, brass);
                GameObject tube = Cylinder("Glass", Root, b + Vector3.up * (2.4f + ColH / 2f), ColR, ColH, glass);
                NoShadow(tube);
                c.FillMat = Make.Emissive(Dim, Dim * 0.4f, 0.8f);
                c.Fill = Cylinder("Fill", Root, b + Vector3.up * 2.4f, ColR * 0.82f, 1f, c.FillMat).transform;
                NoShadow(c.Fill.gameObject);
                c.CapMat = Make.Glow(new Color(1f, 1f, 1f, 0.9f));
                c.Cap = Hoop("Level", Root, b + Vector3.up * 2.4f, 0f, ColR * 0.82f, c.CapMat).transform;
                // The 75 line.
                Hoop("75", Root, b + Vector3.up * (2.4f + ColH * 0.75f), ColR + 0.1f, ColR + 0.9f, Make.Glow(new Color(1f, 1f, 1f, 0.55f), false));
                c.Name = Words(Root, names[i], b + new Vector3(0, -3.2f, -ColR - 2f), 2.1f, new Color(1f, 1f, 1f, 0.75f));
                c.Value = Words(Root, "", b + new Vector3(0, -6.6f, -ColR - 2f), 2.7f, Color.white);
                c.Score = Words(Root, "", b + new Vector3(0, ColH + 7.5f, 0), 4f, Color.white);
                _cols[i] = c;
                SetFill(c, 0f);
            }

            // The crystal, its halo and its light.
            _crystalMat = Make.Emissive(new Color(0.55f, 0.6f, 0.7f), Dim * 0.3f, 0.95f);
            _crystal = Make.MeshObject("Crystal", Root, Gem(12f, 21f, 21f), _crystalMat).transform;
            _crystal.localPosition = CrystalAt;
            _haloMat = Make.Glow(new Color(Dim.r, Dim.g, Dim.b, 0.25f));
            _halo = Shape(PrimitiveType.Sphere, "Halo", Root, CrystalAt, Vector3.one * 22f, _haloMat).transform;
            NoShadow(_halo.gameObject);
            _glow = World.NewLight(Root, "Crystal light", LightType.Point, Dim, 30000f);
            _glow.transform.localPosition = CrystalAt + new Vector3(0, -8f, -10f);
            _glow.range = 260f;
            _glow.shadows = LightShadows.None;

            // Fifteen small crystals round the floor under it, one per stroke.
            for (int i = 0; i < 15; i++)
            {
                float a = Mathf.Lerp(200f, -20f, i / 14f) * Mathf.Deg2Rad;
                Material m = Make.Emissive(new Color(0.18f, 0.2f, 0.24f), Color.black, 0.9f);
                GameObject g = Make.MeshObject("Mark " + i, Root, Gem(1.8f, 5f, 1f), m);
                g.transform.localPosition = CrystalAt + new Vector3(Mathf.Cos(a) * 34f, -CrystalAt.y + 1f, Mathf.Sin(a) * 26f);
                _ring.Add(m);
                _ringT.Add(g.transform);
            }

            _pulse = Shape(PrimitiveType.Sphere, "Pulse", Root, Vector3.zero, Vector3.one * 2.4f, Make.Glow(new Color(1f, 1f, 1f, 1f))).transform;
            NoShadow(_pulse.gameObject);
            _pulse.gameObject.SetActive(false);

            _read = new Readout(this, Portrait ? new Vector2(0.58f, -0.76f) : new Vector2(0.68f, -0.7f), Silver);

            Sun(Root, new Vector3(50f, 30f, 0f), new Color(0.7f, 0.8f, 1f), 0.35f);
            Lamp(Root, new Vector3(-60f, 160f, -40f), new Vector3(0, 0, 10f), new Color(0.9f, 0.95f, 1f), 160000f, 40f, 500f);
            Lamp(Root, new Vector3(40f, 140f, 20f), new Vector3(0, 10f, ColZ), new Color(0.7f, 0.8f, 1f), 120000f, 55f, 500f);
        }

        void SetFill(Column c, float score)
        {
            c.Shown = score;
            float h = Mathf.Max(0.05f, ColH * Mathf.Clamp01(score / 100f));
            Vector3 p = c.Fill.localPosition;
            c.Fill.localScale = new Vector3(ColR * 1.64f, h / 2f, ColR * 1.64f);
            c.Fill.localPosition = new Vector3(p.x, 2.4f + h / 2f, p.z);
            c.Cap.localPosition = new Vector3(p.x, 2.42f + h, p.z);
            c.FillMat.SetColor("_BaseColor", c.Colour * 0.6f);
            c.FillMat.SetColor("_EmissionColor", c.Colour * (score > 0 ? 1.5f : 0.15f));
            c.CapMat.SetColor("_Color", score > 0 ? new Color(c.Colour.r, c.Colour.g, c.Colour.b, 0.9f) : new Color(1, 1, 1, 0.15f));
        }

        void Ring(ShotView v)
        {
            int[] p = Ints(v, "purities");
            for (int i = 0; i < _ring.Count; i++)
            {
                bool on = i < p.Length;
                Color c = on ? ScoreColour(p[i]) : new Color(0.18f, 0.2f, 0.24f);
                _ring[i].SetColor("_BaseColor", on ? c * 0.7f : c);
                _ring[i].SetColor("_EmissionColor", on ? c * 1.8f : Color.black);
                _ringT[i].localScale = Vector3.one * (on ? 1.15f : 1f);
            }
        }

        public override void Begin(ShotView start)
        {
            foreach (Column c in _cols) { c.Colour = Dim; SetFill(c, 0f); c.Value.text = ""; c.Score.text = ""; }
            _crystalTo = _crystalColour = Dim;
            _bright = 0.25f;
            _read.Hide();
            Ring(start);
            if (StrokeOf(start).Has) Show(start, false);
        }

        // The columns, the crystal and the readout for a stroke, at once (a stroke already played).
        void Show(ShotView v, bool fresh)
        {
            float[] q = { v.Int("qFace"), v.Int("qRhythm"), v.Int("qLie") };
            for (int i = 0; i < 3; i++) { _cols[i].Colour = ScoreColour(q[i]); SetFill(_cols[i], q[i]); Labels(v, i, q[i]); }
            int purity = v.Int("purity");
            _crystalTo = _crystalColour = ScoreColour(purity);
            _bright = 0.4f + purity / 100f;
            _read.Show("PURITY", purity.ToString(), "", "", ScoreColour(purity));
        }

        void Labels(ShotView v, int i, float score)
        {
            StrokeData s = StrokeOf(v);
            Column c = _cols[i];
            c.Value.text = i == 0 ? Signed(s.Face) + "°" : i == 1 ? F1(s.Rhythm) + ":1" : Signed(s.Lie) + "°";
            c.Score.text = Mathf.RoundToInt(score).ToString();
            c.Score.color = c.Colour;
        }

        public override IEnumerator Play(ShotView v)
        {
            StrokeData s = StrokeOf(v);
            _read.Hide();
            // The columns drain while the putter goes back.
            float[] from = { _cols[0].Shown, _cols[1].Shown, _cols[2].Shown };
            bool hit = false;
            Arcade.StartCoroutine(Drain(from));
            yield return Swing(_putter, s, () => { hit = true; Sound("golf_iron", 0.5f, 1.4f); });
            if (hit) Arcade.StartCoroutine(RollBall(_ball, v.Made ? 70f : 60f, v.MissSide == "LEFT" ? -6f : v.MissSide == "RIGHT" ? 6f : 0f, 1.6f));

            // The pulse from the face to each column, filling it.
            float[] q = { v.Int("qFace"), v.Int("qRhythm"), v.Int("qLie") };
            Vector3 face = Root.InverseTransformPoint(_putter.Face.position);
            for (int i = 0; i < 3; i++)
            {
                Column c = _cols[i];
                Vector3 to = new Vector3(c.Fill.localPosition.x, 3f, c.Fill.localPosition.z);
                _pulse.gameObject.SetActive(true);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.16f)
                {
                    _pulse.localPosition = Vector3.Lerp(face, to, t) + Vector3.up * (8f * t * (1f - t));
                    yield return null;
                }
                _pulse.gameObject.SetActive(false);
                c.Colour = ScoreColour(q[i]);
                Arcade.StartCoroutine(Fill(c, q[i], 0.55f));
                Labels(v, i, 0f);
                c.Score.text = "";
                Sound("click", 0.35f, 1.2f + i * 0.15f);
            }
            yield return new WaitForSeconds(0.55f);
            for (int i = 0; i < 3; i++) Labels(v, i, q[i]);

            // The crystal takes the purity.
            int purity = v.Int("purity");
            Color pc = ScoreColour(purity);
            _crystalTo = pc;
            _bright = 0.4f + purity / 100f;
            _flare = v.Flag("strike") ? 1f : 0.45f;
            _read.Show("PURITY", purity.ToString(), "", "", pc);
            if (v.Flag("strike"))
            {
                _spin = 260f;
                Sound("bell_ding", 0.8f, 1.1f);
                Sound("riser", 0.4f, 1.3f);
                Arcade.Sparks(At(CrystalAt), 50);
                Arcade.Celebrate(18, At(CrystalAt));
            }
            else Sound("lab_beep", 0.35f, 0.6f + purity / 120f);
            Ring(v);
            int n = Ints(v, "purities").Length;
            if (n > 0 && n <= _ringT.Count) Arcade.StartCoroutine(Pop(_ringT[n - 1]));
            yield return Settle(_putter, 0.5f);
            yield return new WaitForSeconds(0.25f);
        }

        IEnumerator Drain(float[] from)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
            {
                for (int i = 0; i < 3; i++) SetFill(_cols[i], Mathf.Lerp(from[i], 0f, t));
                yield return null;
            }
            for (int i = 0; i < 3; i++) { SetFill(_cols[i], 0f); _cols[i].Value.text = ""; _cols[i].Score.text = ""; }
        }

        IEnumerator Fill(Column c, float to, float seconds)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                SetFill(c, Mathf.Lerp(0f, to, 1f - (1f - t) * (1f - t)));
                yield return null;
            }
            SetFill(c, to);
        }

        static IEnumerator Pop(Transform t)
        {
            for (float u = 0f; u < 1f; u += Time.deltaTime / 0.4f)
            {
                t.localScale = Vector3.one * (1.15f + Mathf.Sin(u * Mathf.PI) * 0.9f);
                yield return null;
            }
            t.localScale = Vector3.one * 1.15f;
        }

        public override void Ready(ShotView v)
        {
            _ball.localPosition = new Vector3(PX, 0.84f, PZ);
            _ball.localRotation = Quaternion.identity;
        }

        public override void Tick()
        {
            float dt = Time.deltaTime;
            _spin = Mathf.Lerp(_spin, 18f, dt * 1.5f);
            _yaw += _spin * dt;
            _crystal.localRotation = Quaternion.Euler(8f * Mathf.Sin(Time.time * 0.7f), _yaw, 0);
            float bob = Mathf.Sin(Time.time * 1.1f) * 1.6f;
            _crystal.localPosition = CrystalAt + Vector3.up * bob;
            _halo.localPosition = CrystalAt + Vector3.up * bob;
            _flare = Mathf.Max(0f, _flare - dt * 0.9f);
            _crystalColour = Color.Lerp(_crystalColour, _crystalTo, dt * 4f);
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 2.4f);
            float glow = _bright * pulse + _flare * 2.5f;
            _crystalMat.SetColor("_BaseColor", Color.Lerp(new Color(0.55f, 0.6f, 0.7f), _crystalColour, 0.6f));
            _crystalMat.SetColor("_EmissionColor", _crystalColour * glow);
            _haloMat.SetColor("_Color", new Color(_crystalColour.r, _crystalColour.g, _crystalColour.b, 0.04f + 0.05f * _bright + 0.3f * _flare));
            _halo.localScale = Vector3.one * (24f + 22f * _flare);
            _crystal.localScale = Vector3.one * (1f + 0.25f * _flare);
            _glow.color = _crystalColour;
            _glow.intensity = 6000f * glow;
            foreach (Column c in _cols) { Face(c.Name.transform); Face(c.Value.transform); Face(c.Score.transform); }
            _read.Spot = Portrait ? new Vector2(0.58f, -0.76f) : new Vector2(0.68f, -0.7f);
            _read.Size = Portrait ? 1f : 0.6f;
            _read.Tick(Eye(), Root);
        }
    }
}
