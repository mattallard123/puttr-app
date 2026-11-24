// Tour Juice: the set. A glossy black bar counter, a back wall of shelves lit from below in Puttr green with bottles
// of juice glowing on them, the neon TOUR JUICE sign, the dispenser over the big glass bottle, the readout beside it
// and the test tube rack in front. Plus the little effects (drops, foam, the grey puff) the putt plays with.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed partial class TourJuiceScene
    {
        // The bottle (its own local space: base at y = 0).
        const float BottleX = 16f, BottleZ = -10f, R = 8f, LiquidR = 7.3f, LiquidBase = 1.4f, BodyH = 20f;
        const float NeckTop = 33f, NozzleY = 42f, GlowLight = 450f;
        // The rack.
        const int Tubes = 9;
        // The brand green in linear terms (the renderer's), so it reads as the brand rather than a pale mint.
        static readonly Color G = Make.Bright.linear;
        const float RackZ = -46f, RackTop = 2.4f, TubeR = 1.8f, TubeBodyH = 6.6f, TubeH = 8.6f;
        static float TubeX(int i) => (i - (Tubes - 1) * 0.5f) * 4.5f;

        Transform _bottle, _liquid, _surface, _stream, _drop, _numberRoot, _nowRing;
        Material _tubeShine, _liquidMat, _surfaceMat, _streamMat, _bubbleMat, _ringMat, _signHalo, _capDim, _capLit, _capMiss;
        Light _glowLight;
        TextMesh _number, _of, _call, _total;
        readonly List<Material> _neonMats = new List<Material>();
        readonly Dictionary<Material, Color> _neonBase = new Dictionary<Material, Color>();
        readonly List<Transform> _bubbles = new List<Transform>();
        float[] _bubbleY, _bubbleV;
        Vector2[] _bubbleXZ;
        readonly Transform[] _tubeJuice = new Transform[Tubes];
        readonly Material[] _tubeMats = new Material[Tubes];
        readonly Renderer[] _tubeCap = new Renderer[Tubes];
        readonly TextMesh[] _tubeLabel = new TextMesh[Tubes];
        readonly List<LineRenderer> _ticks = new List<LineRenderer>();
        readonly List<TextMesh> _tickWords = new List<TextMesh>();

        Material Neon(Color c, float power)
        {
            Material m = Make.Emissive(c, c * power, 0.6f);
            _neonMats.Add(m);
            _neonBase[m] = c * power;
            return m;
        }

        public override void Build()
        {
            Color ink = new Color(0.02f, 0.025f, 0.03f);
            const float tilt = 20f;   // words lean back to the camera

            // The room: counter, back wall, a ceiling of dark.
            Shape(PrimitiveType.Cube, "Counter", Root, new Vector3(0, -1.5f, -10f), new Vector3(520f, 3f, 150f), Make.Lit(new Color(0.03f, 0.034f, 0.04f), 0.62f, 0.25f));
            Shape(PrimitiveType.Cube, "Counter edge", Root, new Vector3(0, -0.6f, -85.2f), new Vector3(520f, 1.2f, 0.8f), Neon(G, 2.2f));
            Shape(PrimitiveType.Cube, "Back wall", Root, new Vector3(0, 150f, 96f), new Vector3(900f, 400f, 2f), Make.Lit(ink, 0.35f));
            // Slatted panels on the wall, so the light has something to rake across.
            Material slat = Make.Lit(new Color(0.045f, 0.05f, 0.055f), 0.55f, 0.2f);
            for (int i = -14; i <= 14; i++) Shape(PrimitiveType.Cube, "Slat", Root, new Vector3(i * 9f, 110f, 94.4f), new Vector3(6.4f, 220f, 1.2f), slat);

            // Shelves lit from beneath, with bottles of juice glowing on them.
            Material plank = Make.Lit(new Color(0.06f, 0.065f, 0.07f), 0.7f, 0.6f);
            var dice = new System.Random(11);
            Color[] juices = { G, new Color(0.86f, 0.9f, 0.2f).linear, new Color(1f, 0.55f, 0.15f).linear, new Color(0.3f, 0.85f, 0.6f).linear, G, new Color(0.55f, 0.95f, 0.35f).linear };
            foreach (float y in new[] { 30f, 60f })
            {
                Shape(PrimitiveType.Cube, "Shelf", Root, new Vector3(0, y, 82f), new Vector3(300f, 1.4f, 14f), plank);
                Shape(PrimitiveType.Cube, "Shelf light", Root, new Vector3(0, y - 0.9f, 75.2f), new Vector3(300f, 0.5f, 0.5f), Neon(G, 3.2f));
                GameObject wash = Make.MeshObject("Shelf glow", Root, Quad(300f, 22f), Make.Glow(new Color(G.r, G.g, G.b, 0.12f), true, Make.SoftDot()));
                wash.transform.localPosition = new Vector3(0, y - 10f, 93f);
                for (float x = -130f; x <= 130f; x += 9f + (float)dice.NextDouble() * 9f)
                {
                    if (dice.NextDouble() < 0.2) continue;
                    float h = 10f + (float)dice.NextDouble() * 9f, r = 2.4f + (float)dice.NextDouble() * 1.6f;
                    Transform b = new GameObject("Shelf bottle").transform;
                    b.SetParent(Root, false);
                    b.localPosition = new Vector3(x, y + 0.7f, 82f + (float)dice.NextDouble() * 4f - 2f);
                    Color j = juices[dice.Next(juices.Length)];
                    float level = 0.3f + (float)dice.NextDouble() * 0.6f;
                    GameObject jj = Make.MeshObject("Juice", b, Make.Lathe(new[] { new Vector2(0, 0.4f), new Vector2(r * 0.88f, 0.4f), new Vector2(r * 0.88f, 0.4f + h * 0.6f * level), new Vector2(0, 0.4f + h * 0.6f * level) }, 20), Make.Emissive(j * 0.5f, j * 0.9f, 0.6f));
                    jj.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    GameObject glass = Make.MeshObject("Glass", b, Make.Lathe(BottleProfile(r, h), 20), Make.Transparent(new Color(0.12f, 0.16f, 0.15f, 0.3f), 0.95f));
                    glass.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // The sign.
            Transform sign = new GameObject("Sign").transform;
            sign.SetParent(Root, false);
            sign.localPosition = new Vector3(0, 84f, 90f);
            _signHalo = Make.Glow(new Color(G.r, G.g, G.b, 0.22f), true, Make.SoftDot());
            GameObject halo = Make.MeshObject("Halo", sign, Quad(150f, 40f), _signHalo);
            halo.transform.localPosition = new Vector3(0, 0, 1f);
            Words(sign, "TOUR JUICE", new Vector3(0, 1f, -0.5f), 14f, new Color(0.78f, 1f, 0.6f), TextAnchor.MiddleCenter, 160);
            Shape(PrimitiveType.Cube, "Underline", sign, new Vector3(0, -9f, 0), new Vector3(96f, 0.9f, 0.9f), Neon(G, 4f));
            Shape(PrimitiveType.Cube, "Overline", sign, new Vector3(0, 11f, 0), new Vector3(96f, 0.9f, 0.9f), Neon(G, 4f));
            foreach (float x in new[] { -86f, 86f }) Shape(PrimitiveType.Cube, "Neon post", Root, new Vector3(x, 60f, 88f), new Vector3(1.2f, 120f, 1.2f), Neon(G, 3.5f));

            // ── The bottle ──
            _bottle = new GameObject("Bottle").transform;
            _bottle.SetParent(Root, false);
            _bottle.localPosition = new Vector3(BottleX, 0, BottleZ);
            // A puck under it with a ring of green light.
            Shape(PrimitiveType.Cylinder, "Puck", _bottle, new Vector3(0, 0.6f, 0), new Vector3(26f, 0.6f, 26f), Make.Lit(new Color(0.05f, 0.055f, 0.06f), 0.85f, 0.7f));
            _ringMat = Make.Emissive(G, G * 1.6f, 0.6f);
            Hoop("Ring", _bottle, new Vector3(0, 1.25f, 0), 11.4f, 12.6f, _ringMat);
            GameObject pool = Make.MeshObject("Pool", _bottle, Quad(70f, 70f), Make.Glow(new Color(G.r, G.g, G.b, 0.18f), true, Make.SoftDot()));
            pool.transform.localPosition = new Vector3(0, 0.05f, 0);
            pool.transform.localRotation = Quaternion.Euler(90f, 0, 0);

            Transform sit = new GameObject("Seat").transform;   // the glass sits on the puck
            sit.SetParent(_bottle, false);
            sit.localPosition = new Vector3(0, 1.3f, 0);
            _liquidMat = Make.Emissive(G * 0.15f, G * 0.7f, 0.2f);
            _liquid = Make.MeshObject("Juice", sit, Make.Lathe(new[] { new Vector2(0, 0), new Vector2(LiquidR, 0), new Vector2(LiquidR, 1f), new Vector2(0, 1f) }, 48), _liquidMat).transform;
            _liquid.localPosition = new Vector3(0, LiquidBase, 0);
            _liquid.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            _surfaceMat = Make.Glow(new Color(1f, 1f, 1f, 0.9f), true);
            _surface = Make.MeshObject("Surface", sit, Make.Ring(LiquidR - 1.6f, LiquidR, 64), _surfaceMat).transform;
            _streamMat = Make.Emissive(G * 0.6f, G * 2.4f, 0.9f);
            _bubbleMat = Make.Glow(new Color(1f, 1f, 1f, 1f), true);
            _bubbleY = new float[32]; _bubbleV = new float[32]; _bubbleXZ = new Vector2[32];
            for (int i = 0; i < 32; i++)
            {
                float a = Random.value * Mathf.PI * 2f, d = Mathf.Sqrt(Random.value) * (LiquidR - 1.4f);
                _bubbleXZ[i] = new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
                _bubbleY[i] = LiquidBase + Random.value * BodyH;
                _bubbleV[i] = Random.Range(5f, 13f);
                float s = Random.Range(0.5f, 1.2f);
                Transform b = Shape(PrimitiveType.Sphere, "Bubble", sit, Vector3.zero, Vector3.one * s, _bubbleMat).transform;
                b.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                b.gameObject.SetActive(false);
                _bubbles.Add(b);
            }
            // Unlit glass: a lit see-through material picks up the bright reflection sky and washes the juice out.
            Material glassMat = Make.Glow(new Color(0.02f, 0.05f, 0.04f, 0.22f), false);
            GameObject glass2 = Make.MeshObject("Glass", sit, Make.Lathe(BottleProfile(R, NeckTop), 64), glassMat);
            glass2.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            // Highlights down the glass, as studio lights make on a bottle.
            Material shine = Make.Glow(new Color(1f, 1f, 1f, 0.35f), true), edge = Make.Glow(new Color(0.8f, 1f, 0.9f, 0.3f), true);
            foreach (float a in new[] { -88f, -38f, -30f, 44f, 88f })
            {
                float rad = (a - 90f) * Mathf.Deg2Rad;
                LineRenderer l = Stroke(sit, "Shine", Color.white, a == -30f ? 0.35f : Mathf.Abs(a) > 80f ? 0.3f : 0.8f); l.sharedMaterial = Mathf.Abs(a) > 80f ? edge : shine;
                var pts = new List<Vector3>();
                foreach (Vector2 p in BottleProfile(R + 0.08f, NeckTop))
                    if (p.y > 2f && p.y < NeckTop - 1f && p.x > 1f) pts.Add(new Vector3(Mathf.Cos(rad) * p.x, p.y, Mathf.Sin(rad) * p.x));
                Points(l, pts);
            }
            // A cap ring of green at the neck.
            Hoop("Lip", sit, new Vector3(0, NeckTop + 0.02f, 0), 2.6f, 3.6f, Neon(G, 2.5f));
            _glowLight = World.NewLight(sit, "Juice glow", LightType.Point, Make.Bright, 0f);
            _glowLight.shadows = LightShadows.None;
            _glowLight.range = 90f;

            // The dispenser over it.
            Material chrome = Make.Lit(new Color(0.7f, 0.72f, 0.75f), 0.9f, 1f);
            Shape(PrimitiveType.Cylinder, "Pipe", sit, new Vector3(0, NozzleY + 40f, 0), new Vector3(3.2f, 34f, 3.2f), chrome);
            Make.MeshObject("Nozzle", sit, Make.Lathe(new[] { new Vector2(0.6f, NozzleY), new Vector2(1.1f, NozzleY), new Vector2(1.4f, NozzleY + 2.5f), new Vector2(3.2f, NozzleY + 5.5f), new Vector2(3.4f, NozzleY + 6.5f), new Vector2(0, NozzleY + 6.6f) }, 32), chrome);
            Hoop("Nozzle light", sit, new Vector3(0, NozzleY + 4.6f, 0), 3.3f, 3.9f, Neon(G, 3f)).transform.localRotation = Quaternion.identity;
            _stream = Shape(PrimitiveType.Cylinder, "Stream", sit, Vector3.zero, new Vector3(1.3f, 1f, 1.3f), _streamMat).transform;
            _stream.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            _stream.gameObject.SetActive(false);
            _drop = Shape(PrimitiveType.Sphere, "Drop", sit, Vector3.zero, Vector3.one * 1.1f, _streamMat).transform;
            _drop.gameObject.SetActive(false);

            // The graduations: one mark a point, up to the putt's distance (made in Graduate).
            Material chalk = Make.Glow(new Color(1f, 1f, 1f, 0.6f), false);
            for (int k = 0; k < 12; k++)
            {
                LineRenderer t = Stroke(sit, "Mark", Color.white, 0.3f, false);
                t.sharedMaterial = chalk;
                _ticks.Add(t);
                TextMesh w = Words(sit, "", Vector3.zero, 1.7f, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleRight);
                w.transform.localRotation = Quaternion.Euler(tilt, 0, 0);
                _tickWords.Add(w);
            }

            // ── The readout, left of the bottle ──
            _numberRoot = new GameObject("Readout").transform;
            _numberRoot.SetParent(Root, false);
            _numberRoot.localPosition = new Vector3(-13f, 12f, -24f);
            _numberRoot.localRotation = Quaternion.Euler(tilt, 0, 0);
            _number = Words(_numberRoot, "", new Vector3(3f, 0, 0), 10f, Color.white, TextAnchor.MiddleRight, 160);
            _of = Words(_numberRoot, "", new Vector3(4f, -2.2f, 0), 3.4f, new Color(0.75f, 0.8f, 0.75f), TextAnchor.MiddleLeft);
            _call = Words(_numberRoot, "", new Vector3(3f, -7.4f, 0), 2.7f, G);

            // ── The rack of test tubes: the round so far ──
            Material rackMat = Make.Lit(new Color(0.08f, 0.085f, 0.09f), 0.8f, 0.85f);
            float rackW = TubeX(Tubes - 1) - TubeX(0) + 9f;
            Make.MeshObject("Rack base", Root, Make.RoundedBox(new Vector3(rackW, RackTop, 9f), 0.8f), rackMat).transform.localPosition = new Vector3(0, RackTop * 0.5f, RackZ);
            Make.MeshObject("Rack bar", Root, Make.RoundedBox(new Vector3(rackW, 1.8f, 5f), 0.5f), rackMat).transform.localPosition = new Vector3(0, RackTop + 2.4f, RackZ);
            foreach (float x in new[] { -rackW * 0.5f + 0.8f, rackW * 0.5f - 0.8f })
                Shape(PrimitiveType.Cube, "Rack post", Root, new Vector3(x, RackTop + 1.2f, RackZ), new Vector3(1.1f, 3.2f, 4.4f), rackMat);
            Shape(PrimitiveType.Cube, "Rack light", Root, new Vector3(0, RackTop + 0.1f, RackZ - 4.55f), new Vector3(rackW - 1f, 0.35f, 0.3f), Neon(G, 2.4f));
            _capDim = Make.Lit(new Color(0.16f, 0.17f, 0.19f), 0.7f, 0.8f);
            _capLit = Make.Emissive(G * 0.5f, G * 0.9f, 0.6f);
            _capMiss = Make.Emissive(new Color(0.9f, 0.3f, 0.28f).linear, new Color(0.9f, 0.3f, 0.28f).linear, 0.6f);
            _tubeShine = Make.Glow(new Color(1f, 1f, 1f, 0.3f), true);
            Material tubeGlass = Make.Glow(new Color(0.1f, 0.16f, 0.13f, 0.35f), false);
            for (int i = 0; i < Tubes; i++)
            {
                Transform t = new GameObject("Tube").transform;
                t.SetParent(Root, false);
                t.localPosition = new Vector3(TubeX(i), RackTop, RackZ);
                _tubeMats[i] = Make.Emissive(G * 0.15f, G * 0.7f, 0.2f);
                _tubeJuice[i] = Make.MeshObject("Juice", t, Make.Lathe(new[] { new Vector2(0, 0), new Vector2(TubeR - 0.4f, 0), new Vector2(TubeR - 0.4f, 1f), new Vector2(0, 1f) }, 24), _tubeMats[i]).transform;
                _tubeJuice[i].localPosition = new Vector3(0, 0.6f, 0);
                _tubeJuice[i].GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                GameObject g = Make.MeshObject("Glass", t, Make.Lathe(new[] { new Vector2(0, 0), new Vector2(TubeR * 0.7f, 0.15f), new Vector2(TubeR, 0.9f), new Vector2(TubeR, TubeH), new Vector2(0, TubeH + 0.01f) }, 24), tubeGlass);
                g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                _tubeCap[i] = Shape(PrimitiveType.Cylinder, "Cap", t, new Vector3(0, TubeH + 0.4f, 0), new Vector3(TubeR * 2.2f, 0.5f, TubeR * 2.2f), _capDim).GetComponent<Renderer>();
                LineRenderer gl = Stroke(t, "Shine", Color.white, 0.22f); gl.sharedMaterial = _tubeShine;
                Points(gl, new[] { new Vector3(-TubeR * 0.62f, 1.2f, -TubeR * 0.78f), new Vector3(-TubeR * 0.62f, TubeH - 0.4f, -TubeR * 0.78f) });
                _tubeLabel[i] = Words(Root, "", new Vector3(TubeX(i), RackTop + 2.4f, RackZ - 2.6f), 1.6f, Color.white);
                _tubeLabel[i].transform.localRotation = Quaternion.Euler(tilt, 0, 0);
            }
            _nowRing = Hoop("Now", Root, Vector3.zero, TubeR + 0.25f, TubeR + 0.75f, Make.Emissive(G, G * 2.6f, 0.6f)).transform;
            _total = Words(Root, "", new Vector3(0, RackTop * 0.5f, RackZ - 4.65f), 1.6f, new Color(0.8f, 0.95f, 0.7f));

            // Effects.
            _fxDot = Make.SoftDot();

            // Light: a dim cool key for the shadows, a warm spot on the bottle, a green wash from behind.
            Sun(Root, new Vector3(50f, -20f, 0f), new Color(0.75f, 0.82f, 0.95f), 0.35f);
            Lamp(Root, new Vector3(BottleX - 30f, 150f, -70f), new Vector3(BottleX, 18f, BottleZ), new Color(1f, 0.95f, 0.88f), 32000f, 34f, 500f);
            Lamp(Root, new Vector3(-40f, 120f, -110f), new Vector3(-10f, 6f, RackZ), new Color(0.9f, 0.95f, 1f), 16000f, 40f, 400f);
            Lamp(Root, new Vector3(0f, 140f, 70f), new Vector3(BottleX * 0.5f, 10f, -10f), Make.Bright, 26000f, 60f, 500f).shadows = LightShadows.None;
        }

        // Out from the base, up the body, round the shoulder, the neck and its lip.
        static Vector2[] BottleProfile(float r, float top)
        {
            float body = top * 0.65f, shoulder = top * 0.82f;
            return new[]
            {
                new Vector2(0, 0), new Vector2(r * 0.8f, 0), new Vector2(r * 0.96f, 0.4f), new Vector2(r, 1.4f),
                new Vector2(r, body), new Vector2(r * 0.97f, body + top * 0.04f), new Vector2(r * 0.86f, body + top * 0.09f), new Vector2(r * 0.6f, shoulder - top * 0.03f),
                new Vector2(r * 0.38f, shoulder), new Vector2(r * 0.34f, top - 1.4f), new Vector2(r * 0.4f, top - 1.1f), new Vector2(r * 0.4f, top),
                new Vector2(r * 0.3f, top + 0.01f),
            };
        }

        static Mesh Quad(float w, float h)
        {
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { new Vector3(-w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(w / 2, -h / 2, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // Mark the bottle out in points to this putt's distance: a tick each, every one numbered.
        void Graduate(int feet)
        {
            _feet = Mathf.Clamp(feet, 1, 12);
            for (int k = 0; k < _ticks.Count; k++)
            {
                bool on = k < _feet;
                _ticks[k].gameObject.SetActive(on);
                _tickWords[k].gameObject.SetActive(on);
                if (!on) continue;
                float y = LiquidBase + (k + 1f) / _feet * BodyH;
                bool top = k == _feet - 1;
                float a0 = top ? 228f : 236f, a1 = 262f;   // round the front of the glass, left of centre
                var pts = new List<Vector3>();
                for (int s = 0; s <= 4; s++)
                {
                    float a = Mathf.Lerp(a0, a1, s / 4f) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(Mathf.Cos(a) * (R + 0.12f), y, Mathf.Sin(a) * (R + 0.12f)));
                }
                Points(_ticks[k], pts);
                _ticks[k].widthMultiplier = top ? 0.42f : 0.26f;
                float aw = a0 * Mathf.Deg2Rad;
                _tickWords[k].transform.localPosition = new Vector3(Mathf.Cos(aw) * (R + 0.6f) - 0.4f, y, Mathf.Sin(aw) * (R + 0.6f));
                _tickWords[k].text = top || _feet <= 6 || ((k + 1) % 2 == 0 && k + 1 < _feet - 1) ? (k + 1).ToString() : "";
                _tickWords[k].characterSize = (top ? 2.3f : 1.6f) * 10f / 96f * 1.4f;
            }
        }

        void StreamTo(float y)
        {
            float top = NozzleY, len = Mathf.Max(0.1f, top - y);
            _stream.localPosition = new Vector3(0, y + len * 0.5f, 0);
            _stream.localScale = new Vector3(1.1f + Mathf.Sin(Time.time * 40f) * 0.08f, len * 0.5f, 1.1f);
        }

        // ── A great putt: a flare of green behind the bottle and a ring of light racing out across the counter ──

        Transform _flare, _wave;
        Material _flareMat, _waveMat;
        float _burstT = -1f, _burstPower = 1f;

        void Burst(float power)
        {
            if (_flare == null)
            {
                _flareMat = Make.Glow(new Color(G.r, G.g, G.b, 0f), true, Make.SoftDot());
                _flare = Make.MeshObject("Flare", _bottle, Quad(1f, 1f), _flareMat).transform;
                _flare.localPosition = new Vector3(0, LiquidBase + BodyH * 0.6f, 9f);
                _waveMat = Make.Glow(new Color(G.r, G.g, G.b, 0f), true);
                _wave = Make.MeshObject("Wave", _bottle, Make.Ring(0.9f, 1f, 96), _waveMat).transform;
                _wave.localPosition = new Vector3(0, 0.3f, 0);
            }
            _burstT = 0f;
            _burstPower = power;
            _flare.gameObject.SetActive(true);
            _wave.gameObject.SetActive(true);
        }

        void TickBurst(float dt)
        {
            if (_burstT < 0f) return;
            _burstT += dt / 0.9f;
            float t = Mathf.Min(1f, _burstT), fade = 1f - t;
            _flare.localScale = Vector3.one * Mathf.Lerp(30f, 110f, Ease(t)) * _burstPower;
            _flareMat.SetColor("_Color", new Color(G.r, G.g, G.b, fade * fade));
            float r = Mathf.Lerp(13f, 70f * _burstPower, 1f - (1f - t) * (1f - t));
            _wave.localScale = new Vector3(r, 1f, r);
            _waveMat.SetColor("_Color", new Color(Mathf.Lerp(G.r, 1f, 0.3f), Mathf.Lerp(G.g, 1f, 0.3f), Mathf.Lerp(G.b, 1f, 0.3f), fade));
            if (t >= 1f) { _burstT = -1f; _flare.gameObject.SetActive(false); _wave.gameObject.SetActive(false); }
        }

        // ── Little effects: soft dots that drift and fade ──

        sealed class Fx { public Transform T; public Material M; public Vector3 V; public float Age, Life, Size, Grow, Gravity; public Color C; }
        readonly List<Fx> _fx = new List<Fx>();
        Texture2D _fxDot;
        static Mesh _fxQuad;

        Fx Spawn(Vector3 world, Vector3 velocity, Color c, float size, float life, bool additive, float grow, float gravity)
        {
            Fx f = _fx.Find(x => !x.T.gameObject.activeSelf && x.M.GetFloat("_DstBlend") == (additive ? 1f : 10f));
            if (f == null)
            {
                if (_fxQuad == null) _fxQuad = Quad(1f, 1f);
                Material m = Make.Glow(c, additive, _fxDot);
                Transform t = Make.MeshObject("Fx", Root, _fxQuad, m).transform;
                t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                f = new Fx { T = t, M = m };
                _fx.Add(f);
            }
            f.T.gameObject.SetActive(true);
            f.T.position = world;
            f.V = velocity; f.C = c; f.Size = size; f.Life = life; f.Age = 0f; f.Grow = grow; f.Gravity = gravity;
            return f;
        }

        void Splat(Vector3 world, int n, Color c)
        {
            float s = Root.lossyScale.x;
            for (int i = 0; i < n; i++) Spawn(world, new Vector3(Random.Range(-6f, 6f), Random.Range(4f, 10f), Random.Range(-6f, 6f)) * s, c, 1.2f * s, 0.4f, true, 0.5f, 30f * s);
        }

        void Puff(Vector3 world)
        {
            float s = Root.lossyScale.x;
            for (int i = 0; i < 9; i++)
                Spawn(world + new Vector3(Random.Range(-2f, 2f), Random.Range(0f, 3f), Random.Range(-2f, 2f)) * s, new Vector3(Random.Range(-3f, 3f), Random.Range(5f, 10f), Random.Range(-2f, 2f)) * s,
                    new Color(0.55f, 0.57f, 0.6f, 0.55f), Random.Range(4f, 7f) * s, Random.Range(1.1f, 1.7f), false, 2.2f, -1f * s);
        }

        void Foam(int n)
        {
            float s = Root.lossyScale.x;
            Vector3 lip = _bottle.TransformPoint(new Vector3(0, NeckTop + 1.6f, 0));
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector3 out1 = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Spawn(lip + out1 * 2f * s, (out1 * Random.Range(4f, 10f) + Vector3.up * Random.Range(10f, 22f)) * s,
                    Color.Lerp(new Color(0.85f, 1f, 0.75f, 0.95f), new Color(G.r, G.g, G.b, 0.95f), Random.value), Random.Range(1.6f, 3.2f) * s, Random.Range(0.7f, 1.2f), true, 0.6f, 36f * s);
            }
        }

        void TickParticles(float dt)
        {
            if (_fx.Count == 0) return;
            Vector3 eye = Camera.main != null ? Camera.main.transform.position : CameraFrom;
            foreach (Fx f in _fx)
            {
                if (!f.T.gameObject.activeSelf) continue;
                f.Age += dt;
                if (f.Age >= f.Life) { f.T.gameObject.SetActive(false); continue; }
                float u = f.Age / f.Life;
                f.V += Vector3.down * f.Gravity * dt;
                f.T.position += f.V * dt;
                f.T.rotation = Quaternion.LookRotation(f.T.position - eye);
                f.T.localScale = Vector3.one * f.Size * (1f + f.Grow * u) / Mathf.Max(0.0001f, Root.lossyScale.x);
                f.M.SetColor("_Color", new Color(f.C.r, f.C.g, f.C.b, f.C.a * (1f - u * u)));
            }
        }
    }
}
