// 01 as darts in a pub: the cup is the bull, and the multipliers are on the hole itself: a band of wedges round its
// front lip (x4 within 15°, x3 within 30°, x2 within 45°, x1 outside), turned to face the tee. The camera is close
// on the hole, as Center Cut's: the putt comes in over the wedge it entered through, and that wedge lights up
// (puttr-app-2, Matt 2026-11-18: the wedges were a 4 ft fan down the mat, so every putt rolled up the x4 one);
// on a checkout the centre wedge is the double. A chalk scoreboard on the panelled wall keeps what each Player has
// left, the old number struck through. Units are inches; the cup is at the origin, the putter looks up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class OhOneScene : RuleScene
    {
        const float K = 1.6f, CupR = 2.125f * K, BallR = 0.84f * K, Depth = 4f * K, MatY = 0.45f, FanIn = CupR + 0.55f, FanOut = CupR + 5.2f, WallZ = 110f, Near = 0.75f, Approach = 30f;
        static readonly float[] Edges = { 0f, 15f, 30f, 45f };

        // The cup is the Puttr's own, as Center Cut draws it: a white liner at the top, black below, a floor that slopes
        // down to the back and the mouth of the return tunnel there, where a make rolls out.
        const float RampFront = -0.35f * Depth, RampBack = -0.72f * Depth;
        static float RampY(float z) => Mathf.Lerp(RampFront, RampBack, Mathf.InverseLerp(-CupR, CupR, z));
        static float RampTilt => Mathf.Atan2(RampFront - RampBack, 2f * CupR) * Mathf.Rad2Deg;

        Transform _ball, _fan;
        LineRenderer _trail;
        readonly List<Renderer> _wedges = new List<Renderer>();     // centre, then right/left pairs outwards
        readonly List<int> _wedgeZone = new List<int>();
        readonly List<Material> _dim = new List<Material>(), _lit = new List<Material>();
        readonly List<TextMesh> _labels = new List<TextMesh>();
        readonly List<TextMesh> _names = new List<TextMesh>(), _now = new List<TextMesh>(), _was = new List<TextMesh>();
        readonly List<Transform> _strikes = new List<Transform>();
        TextMesh _header, _note;
        Transform _board;
        Light _lamp;
        int[] _cur = new int[0], _prev = new int[0];
        int _litWedge = -1;
        float _pulse;

        static bool Portrait => Screen.height > Screen.width;
        // The hole sits a little under the middle of the picture, clear of the score bar along the top.
        public override Vector3 CameraFrom => At(0f, Portrait ? 38f : 26f, Portrait ? -27f : -21f);
        public override Vector3 CameraLook => At(0f, 0f, Portrait ? 5.5f : 2.5f);
        public override float CameraFov => Portrait ? 44f : 40f;

        public override void Build()
        {
            Material planks = Make.Lit(new Color(0.32f, 0.2f, 0.12f), 0.35f, 0f, Make.Wood(11));
            Material panel = Make.Lit(new Color(0.36f, 0.22f, 0.12f), 0.45f, 0f, Make.Wood(4));
            Material trim = Make.Lit(new Color(0.22f, 0.12f, 0.06f), 0.5f);
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material mat = Make.Lit(new Color(0.05f, 0.2f, 0.11f), 0.12f, 0f, pile, pileNormal, 0.9f);
            Material matEdge = Make.Lit(new Color(0.08f, 0.26f, 0.12f), 0.2f);
            Material hole = Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f);
            Material white = Make.Lit(new Color(0.95f, 0.95f, 0.92f), 0.6f);
            Material slate = Make.Lit(new Color(0.08f, 0.11f, 0.1f), 0.15f);
            Material frame = Make.Lit(new Color(0.45f, 0.27f, 0.12f), 0.5f, 0f, Make.Wood(8));
            Material brass = Make.Lit(new Color(0.85f, 0.62f, 0.3f), 0.8f, 0.9f);

            // The room: plank floor, panelled wall, a rail.
            // The floor and the mat are cut round the cup: nothing may cross its column, or it would show in the hole.
            Make.MeshObject("Floor", Root, Make.HolePatch(700f, CupR + 1f, 1f / 120f, Vector3.zero, 1f, 48), planks);
            Shape(PrimitiveType.Cube, "Wall", Root, new Vector3(0, 150f, WallZ + 4f), new Vector3(700f, 300f, 8f), panel);
            for (int i = -6; i <= 6; i++)
                Shape(PrimitiveType.Cube, "Stile", Root, new Vector3(i * 52f, 100f, WallZ - 1f), new Vector3(4f, 200f, 3f), trim);
            Shape(PrimitiveType.Cube, "Dado", Root, new Vector3(0, 60f, WallZ - 2f), new Vector3(700f, 5f, 5f), trim);
            Shape(PrimitiveType.Cube, "Skirting", Root, new Vector3(0, 5f, WallZ - 2f), new Vector3(700f, 10f, 5f), trim);

            // The oche mat, the cup and its liner.
            for (int sd = -1; sd <= 1; sd += 2)
                Shape(PrimitiveType.Cube, "Mat edge", Root, new Vector3(sd * 60.5f, 0.1f, -36f), new Vector3(3f, 0.4f, 200f), matEdge);
            Shape(PrimitiveType.Cube, "Mat edge", Root, new Vector3(0, 0.1f, 60.5f), new Vector3(124f, 0.4f, 3f), matEdge);
            Make.MeshObject("Mat by the cup", Root, Make.HolePatch(59f, CupR, 1f / (10f * K), Vector3.zero, 1f, 96), mat).transform.localPosition = new Vector3(0, MatY, 0);
            Shape(PrimitiveType.Cube, "Mat near", Root, new Vector3(0, MatY - 0.2f, -96.5f), new Vector3(118f, 0.4f, 75f), mat);
            Shape(PrimitiveType.Cube, "Oche", Root, new Vector3(0, 0.5f, -112f), new Vector3(70f, 0.3f, 2.5f), brass);
            Transform cup = new GameObject("Hole").transform;
            cup.SetParent(Root, false);
            cup.localPosition = new Vector3(0, MatY, 0);
            Make.MeshObject("Liner", cup, Make.Cup(CupR, 1.1f * K, false, 72), Make.Lit(new Color(0.92f, 0.93f, 0.9f), 0.45f));
            Make.MeshObject("Cup", cup, Make.Cup(CupR, Depth - 1.1f * K, true, 72), Make.Lit(new Color(0.015f, 0.015f, 0.017f), 0.15f)).transform.localPosition = new Vector3(0, -1.1f * K, 0);
            GameObject ramp = Make.MeshObject("Cup floor", cup, Make.Disc(CupR - 0.05f, 72), hole);
            ramp.transform.localPosition = new Vector3(0, (RampFront + RampBack) / 2f, 0);
            ramp.transform.localRotation = Quaternion.Euler(RampTilt, 0, 0);
            ramp.transform.localScale = new Vector3(1f, 1f, 1f / Mathf.Cos(RampTilt * Mathf.Deg2Rad));
            Make.MeshObject("Return tunnel", cup, CenterCutScene.Mouth(CupR - 0.06f, 34f, RampBack - 0.2f, RampBack + BallR * 2.2f), Make.Glow(new Color(0.01f, 0.01f, 0.012f, 1f), false))
                .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The fan of wedges round the cup, opening towards the putter.
            _fan = new GameObject("Fan").transform;
            _fan.SetParent(Root, false);
            _fan.localPosition = new Vector3(0, MatY + 0.1f, 0);
            Color[] zone = { new Color(0.86f, 0.14f, 0.12f), new Color(0.12f, 0.55f, 0.22f), new Color(0.93f, 0.88f, 0.74f), new Color(0.1f, 0.3f, 0.15f) };
            for (int z = 0; z < 4; z++)
            {
                _dim.Add(Make.Lit(zone[z] * (z == 3 ? 1f : 0.72f), 0.25f));
                _lit.Add(Make.Emissive(zone[z], zone[z] * 2.2f, 0.4f));
            }
            AddWedge(-Edges[1], Edges[1], 0);
            for (int z = 1; z < 3; z++) { AddWedge(Edges[z], Edges[z + 1], z); AddWedge(-Edges[z + 1], -Edges[z], z); }
            AddWedge(45f, 90f, 3); AddWedge(-90f, -45f, 3);
            // Wire between the wedges, as on a board.
            Material wire = Make.Lit(new Color(0.8f, 0.8f, 0.82f), 0.85f, 1f);
            foreach (float e in new[] { -45f, -30f, -15f, 15f, 30f, 45f })
            {
                LineRenderer w = Stroke(_fan, "Wire", Color.white, 0.16f, false);
                w.sharedMaterial = wire;
                Points(w, new[] { Dir(e) * FanIn + Vector3.up * 0.2f, Dir(e) * FanOut + Vector3.up * 0.2f });
            }
            LineRenderer rim = Stroke(_fan, "Rim wire", Color.white, 0.16f, false);
            rim.sharedMaterial = wire;
            var arc = new List<Vector3>();
            for (int i = 0; i <= 40; i++) arc.Add(Dir(Mathf.Lerp(-90f, 90f, i / 40f)) * FanOut + Vector3.up * 0.2f);
            Points(rim, arc);
            // Each wedge's multiplier, written on it.
            float[] at = { 0f, 22.5f, -22.5f, 37.5f, -37.5f, 67f, -67f };
            for (int i = 0; i < at.Length; i++)
            {
                TextMesh t = Words(_fan, "", Dir(at[i]) * ((FanIn + FanOut) * 0.5f + 0.2f) + Vector3.up * 0.3f, i == 0 ? 1.35f : i < 5 ? 0.82f : 1.05f, Color.white);
                t.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                _labels.Add(t);
            }

            // The chalkboard, framed, on the wall to the right of the lamp.
            _board = new GameObject("Chalkboard").transform;
            _board.SetParent(Root, false);
            _board.localPosition = new Vector3(62f, 34f, 12f);
            _board.localRotation = Quaternion.Euler(10f, 18f, 0f);
            _board.localScale = Vector3.one * 0.62f;
            // puttr-app-2: the chalkboard is gone (Matt, 2026-11-21); the app's panel has a hopper of balls for each Player.
            _board.gameObject.SetActive(false);
            Shape(PrimitiveType.Cube, "Leg", _board, new Vector3(-46f, -40f, 6f), new Vector3(4f, 80f, 4f), frame).transform.localRotation = Quaternion.Euler(-10f, 0, 8f);
            Shape(PrimitiveType.Cube, "Leg", _board, new Vector3(46f, -40f, 6f), new Vector3(4f, 80f, 4f), frame).transform.localRotation = Quaternion.Euler(-10f, 0, -8f);
            Shape(PrimitiveType.Cube, "Back leg", _board, new Vector3(0, -30f, 30f), new Vector3(4f, 90f, 4f), frame).transform.localRotation = Quaternion.Euler(-30f, 0, 0);
            Shape(PrimitiveType.Cube, "Frame", _board, new Vector3(0, 0, 1f), new Vector3(124f, 78f, 3f), frame);
            Shape(PrimitiveType.Cube, "Slate", _board, new Vector3(0, 0, -0.8f), new Vector3(114f, 68f, 1f), slate);
            Shape(PrimitiveType.Cube, "Tray", _board, new Vector3(0, -40f, -3f), new Vector3(118f, 2.5f, 6f), frame);
            Shape(PrimitiveType.Cube, "Chalk", _board, new Vector3(30f, -38.2f, -4f), new Vector3(8f, 1.2f, 1.2f), white);
            Color chalk = new Color(0.92f, 0.92f, 0.88f);
            _header = Words(_board, "301", new Vector3(0, 25f, -1.6f), 8f, chalk);
            LineRenderer rule = Stroke(_board, "Rule", chalk, 0.5f, false);
            Points(rule, new[] { new Vector3(-44f, 18f, -1.5f), new Vector3(44f, 18f, -1.5f) });
            for (int i = 0; i < 4; i++)
            {
                _names.Add(Words(_board, "", Vector3.zero, 5.2f, chalk));
                _was.Add(Words(_board, "", Vector3.zero, 5.5f, new Color(0.7f, 0.7f, 0.68f)));
                _now.Add(Words(_board, "", Vector3.zero, 12f, Color.white));
                _strikes.Add(Shape(PrimitiveType.Cube, "Strike", _board, Vector3.zero, new Vector3(12f, 0.5f, 0.2f), white).transform);
            }
            _note = Words(_board, "", new Vector3(0, -29f, -1.6f), 4.2f, new Color(1f, 0.62f, 0.45f));

            // A pendant lamp over the mat.
            Material shade = Make.Lit(new Color(0.15f, 0.3f, 0.2f), 0.7f, 0.4f);
            Shape(PrimitiveType.Cylinder, "Cord", Root, new Vector3(-62f, 225f, 6f), new Vector3(0.6f, 45f, 0.6f), trim);
            GameObject sh = Make.MeshObject("Shade", Root, Make.Lathe(new[] { new Vector2(2f, 0f), new Vector2(5f, -3f), new Vector2(15f, -14f), new Vector2(16f, -15f) }, 32), shade);
            sh.transform.localPosition = new Vector3(-62f, 182f, 6f);
            Shape(PrimitiveType.Sphere, "Bulb", Root, new Vector3(-62f, 170f, 6f), Vector3.one * 7f, Make.Emissive(new Color(1f, 0.9f, 0.7f), new Color(4f, 3f, 1.6f)));
            _lamp = Lamp(Root, new Vector3(-62f, 168f, 6f), new Vector3(-10f, 0, -40f), new Color(1f, 0.82f, 0.55f), 160000f, 80f, 600f);

            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.65f, 0f, bc, bn)).transform;
            _trail = Stroke(Root, "Trace", new Color(1f, 0.92f, 0.7f, 0.9f), 0.5f);

            Sun(Root, new Vector3(50f, 15f, 0f), new Color(1f, 0.85f, 0.65f), 0.3f);
            Lamp(Root, new Vector3(160f, 200f, -200f), new Vector3(70f, 40f, 24f), new Color(1f, 0.85f, 0.65f), 160000f, 30f, 900f);
        }

        static Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a)); }

        void AddWedge(float from, float to, int zone)
        {
            GameObject w = Make.MeshObject("Wedge", _fan, Sector(FanIn, FanOut, from, to, Color.white, 20), _dim[zone]);
            w.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _wedges.Add(w.GetComponent<Renderer>());
            _wedgeZone.Add(zone);
        }

        // A flat ring segment in XZ, angles from straight back (-Z) towards +X.
        static Mesh Sector(float inner, float outer, float fromDeg, float toDeg, Color color, int segs)
        {
            var verts = new List<Vector3>(); var cols = new List<Color>(); var norms = new List<Vector3>(); var tris = new List<int>();
            for (int s = 0; s <= segs; s++)
            {
                Vector3 d = Dir(Mathf.Lerp(fromDeg, toDeg, s / (float)segs));
                verts.Add(d * inner); verts.Add(d * outer);
                cols.Add(color); cols.Add(color); norms.Add(Vector3.up); norms.Add(Vector3.up);
            }
            for (int s = 0; s < segs; s++) { int i = s * 2; tris.Add(i); tris.Add(i + 3); tris.Add(i + 1); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            var m = new Mesh();
            m.SetVertices(verts); m.SetColors(cols); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ── The roll ──

        static Vector2 TeeAt(int feet, int tee) => new Vector2(tee == 1 ? -8f : tee == 3 ? 8f : 0f, -Mathf.Max(3, feet) * 12f * Near);

        static Vector2 Finish(ShotView v)
        {
            if (v.Made) return Vector2.zero;
            float past = v.HasRollout ? v.RolloutIn : v.Reached ? 10f : -10f;
            float side = v.LineIn;
            if (v.Reached && Mathf.Abs(side) < CupR + 0.5f) side = (v.MissSide == "LEFT" || (v.MissSide == "" && side < 0)) ? -(CupR + 1f) : CupR + 1f;
            if (v.Reached && past > 0) side *= 1f + past / 30f;
            return new Vector2(Mathf.Clamp(side, -30f, 30f), Mathf.Clamp(past < 0f ? past * Near : past, -60f, 60f));
        }

        static List<Vector2> RollOf(ShotView v)
        {
            Vector2 start = TeeAt(v.Feet, v.Tee), end = Finish(v);
            var pts = new List<Vector2>();
            if (v.Path != null && v.Path.Count >= 2)
            {
                if (v.Path[0].y > start.y + 12f) pts.Add(start);
                foreach (Vector2 q in v.Path) pts.Add(new Vector2(q.x, q.y < 0f ? q.y * Near : q.y));
                if (Vector2.Distance(pts[pts.Count - 1], end) > 3f && (v.Made || v.HasRollout)) pts.Add(end);
                return pts;
            }
            Vector2 bend = new Vector2((start.x + end.x) * 0.5f + Mathf.Clamp(v.LineIn, -6f, 6f) * 0.9f, (start.y + end.y) * 0.5f);
            for (int i = 0; i <= 30; i++) { float t = i / 30f; pts.Add((1 - t) * (1 - t) * start + 2 * (1 - t) * t * bend + t * t * end); }
            return pts;
        }

        // How far along the roll at time t: slowing to a stop, or still rolling as it drops.
        static float Roll(float t, bool made) { float slow = 1f - (1f - t) * (1f - t); return made ? Mathf.Lerp(t, slow, 0.6f) : slow; }

        static Vector2 Along(List<Vector2> pts, float t)
        {
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
            float want = Mathf.Clamp01(t) * total;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector2.Distance(pts[i - 1], pts[i]);
                if (want <= seg && seg > 0f) return Vector2.Lerp(pts[i - 1], pts[i], want / seg);
                want -= seg;
            }
            return pts[pts.Count - 1];
        }

        // ── State ──

        void Aim(int feet, int tee)
        {
            Vector2 s = TeeAt(feet, tee);
            float baseDeg = Mathf.Atan2(s.x, -s.y) * Mathf.Rad2Deg;
            _fan.localRotation = Quaternion.Euler(0, -baseDeg, 0);
            // puttr-app-2: no ball waiting in the picture before the putt (Matt, 2026-11-21); it appears as the putt rolls in.
            _ball.gameObject.SetActive(false);
            _trail.positionCount = 0;
        }

        void Labels(ShotView v)
        {
            bool checkout = v.Flag("checkout");
            string[] words = checkout ? new[] { "D", "S", "S", "S", "S", "S", "S" } : new[] { "x4", "x3", "x3", "x2", "x2", "x1", "x1" };
            for (int i = 0; i < _labels.Count; i++)
            {
                _labels[i].text = words[i];
                _labels[i].color = checkout && i == 0 ? new Color(1f, 0.9f, 0.5f) : i == 3 || i == 4 ? new Color(0.25f, 0.15f, 0.08f) : new Color(1f, 1f, 1f, i >= 5 ? 0.6f : 0.95f);
            }
        }

        void Board(ShotView v)
        {
            int[] left = v.Get<int[]>("left") ?? new int[0];
            int n = Mathf.Clamp(v.Names.Length, 1, 4);
            _header.text = v.Int("start", 301).ToString();
            if (_cur.Length != n) { _cur = new int[n]; _prev = new int[n]; for (int i = 0; i < n; i++) _cur[i] = _prev[i] = i < left.Length ? left[i] : 0; }
            float width = 104f / n;
            for (int i = 0; i < 4; i++)
            {
                bool on = i < n;
                _names[i].gameObject.SetActive(on); _now[i].gameObject.SetActive(on); _was[i].gameObject.SetActive(false); _strikes[i].gameObject.SetActive(false);
                if (!on) continue;
                float x = -52f + width * (i + 0.5f);
                int now = i < left.Length ? left[i] : 0;
                if (now != _cur[i]) { _prev[i] = _cur[i]; _cur[i] = now; }
                _names[i].transform.localPosition = new Vector3(x, 12f, -1.6f);
                _names[i].text = v.Names[i].ToUpperInvariant();
                _names[i].color = i == v.Next && !v.Over ? new Color(1f, 0.8f, 0.35f) : new Color(0.92f, 0.92f, 0.88f);
                _now[i].transform.localPosition = new Vector3(x, -14f, -1.6f);
                _now[i].text = now.ToString();
                _now[i].characterSize = (n > 2 ? 9f : 12f) * 10f / 96f * 1.4f;
                if (_prev[i] != now)
                {
                    _was[i].gameObject.SetActive(true);
                    _was[i].text = _prev[i].ToString();
                    _was[i].transform.localPosition = new Vector3(x, 2f, -1.6f);
                    _strikes[i].gameObject.SetActive(true);
                    _strikes[i].localPosition = new Vector3(x, 2f, -1.9f);
                    _strikes[i].localScale = new Vector3(_prev[i].ToString().Length * 3.6f + 2f, 0.5f, 0.2f);
                }
            }
            int nextLeft = v.Next < left.Length ? left[v.Next] : 0;
            _note.text = v.Over ? "GAME SHOT" : v.Flag("double") ? "ON A DOUBLE  ·  D" + nextLeft / 2 : v.Flag("checkout") ? "SET UP A DOUBLE" : "";
        }

        public override void Begin(ShotView start)
        {
            Aim(start.NextFeet, start.NextTee);
            Labels(start);
            _cur = new int[0];
            Board(start);
            _trail.positionCount = 0;
        }

        public override void Ready(ShotView v)
        {
            Aim(v.NextFeet, v.NextTee);
            Labels(v);
            Board(v);
        }

        public override void Changed(ShotView v) { Aim(v.NextFeet, v.NextTee); Labels(v); Board(v); }

        public override IEnumerator Play(ShotView v)
        {
            // Last turn's light goes out; this putt's roll.
            if (_litWedge >= 0) _wedges[_litWedge].sharedMaterial = _dim[_wedgeZone[_litWedge]];
            _litWedge = -1;
            Vector2 tee = TeeAt(v.Feet, v.Tee);
            float baseDeg = Mathf.Atan2(tee.x, -tee.y) * Mathf.Rad2Deg;
            _fan.localRotation = Quaternion.Euler(0, -baseDeg, 0);
            List<Vector2> roll = RollOf(v);
            // A make that scores comes in over the wedge it is scored by: straight to the cup at its entry, measured
            // from the tee's line as the wedges are.
            if (v.Made && !v.BackDoor)
            {
                Vector3 from = Dir(baseDeg + Mathf.Clamp(v.Num("rel", v.EntryDeg), -89f, 89f)) * Approach;
                roll = new List<Vector2> { new Vector2(from.x, from.z), Vector2.zero };
            }
            Sound("ohone_throw", 0.6f);   // puttr-app-2: the dart is thrown (silent until recorded)
            var trace = new List<Vector3>();
            _ball.gameObject.SetActive(true);
            float seconds = v.Made && !v.BackDoor ? 0.9f : Mathf.Lerp(1.1f, 1.8f, Mathf.InverseLerp(3f, 11f, v.Feet));
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                Vector2 p = Along(roll, Roll(t, v.Made));
                _ball.localPosition = new Vector3(p.x, BallR + MatY, p.y);
                trace.Add(new Vector3(p.x, MatY + 0.3f, p.y));
                Points(_trail, trace);
                yield return null;
            }
            Vector2 end = roll[roll.Count - 1];
            _ball.localPosition = new Vector3(end.x, BallR + MatY, end.y);

            if (v.Made)
            {
                // Down into the cup, then the wedge it came through lights.
                // Down onto the cup's sloping floor, then out through the return at the back, as on the Puttr.
                float floor = MatY + RampY(0f) + BallR;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.16f) { _ball.localPosition = new Vector3(0, Mathf.Lerp(BallR + MatY, floor, t * t), 0); yield return null; }
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
                {
                    float z = Mathf.Lerp(0f, CupR + BallR, t);
                    _ball.localPosition = new Vector3(0, MatY + RampY(z) + BallR, z);
                    yield return null;
                }
                _ball.gameObject.SetActive(false);
                Sound("dart_thud", 1f);
                if (!v.BackDoor)
                {
                    float rel = Mathf.Clamp(v.Num("rel", v.EntryDeg), -89f, 89f);
                    float off = Mathf.Abs(rel);
                    int zone = off <= 15f ? 0 : off <= 30f ? 1 : off <= 45f ? 2 : 3;
                    // Which wedge: centre, then (right, left) pairs.
                    int idx = zone == 0 ? 0 : 1 + (zone - 1) * 2 + (rel >= 0 ? 0 : 1);
                    _litWedge = idx;
                    _wedges[idx].sharedMaterial = _lit[zone];
                    _pulse = 1f;
                    if (v.Points > 0 && zone == 0) Arcade.Sparks(At(Dir(baseDeg + rel) * FanOut + Vector3.up * 2f), 22);
                    if (v.Title == "OH ONE!") Arcade.Celebrate(60, At(0, 20f, 0));   // puttr-app-2: the checkout sound is the game's win (OhOneGame.SoundFor), played once
                }
            }
            yield return new WaitForSeconds(0.9f);
        }

        public override void Tick()
        {
            if (_pulse > 0f && _litWedge >= 0)
            {
                _pulse -= Time.deltaTime * 0.8f;
                float k = 1f + Mathf.Max(0f, _pulse) * 1.5f * (0.6f + 0.4f * Mathf.Sin(Time.time * 18f));
                int zone = _wedgeZone[_litWedge];
                Color c = zone == 0 ? new Color(0.86f, 0.14f, 0.12f) : zone == 1 ? new Color(0.12f, 0.55f, 0.22f) : zone == 2 ? new Color(0.93f, 0.88f, 0.74f) : new Color(0.1f, 0.3f, 0.15f);
                _lit[zone].SetColor("_EmissionColor", c * 2.2f * k);
            }
            if (_lamp != null) _lamp.intensity = 160000f * (0.97f + 0.03f * Mathf.PerlinNoise(Time.time * 3f, 0.3f));
        }
    }
}
