// Match Play on a seaside links at sunset: a round green on the cliff top, the flag, a pot bunker, dunes and the sea
// with the sun going down. Both Players putt the same hole: the first ball and its gold track stay on the green
// while the second (blue) putts, then the hole's tile is slotted by hand into the wooden scoreboard beside the green.
// Units are inches; the cup is at the origin, the putter looks up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class MatchPlayScene : RuleScene
    {
        const float CupR = 3.4f, BallR = 1.7f, Near = 0.8f;
        static readonly Color[] Tint = { new Color(1f, 0.74f, 0.25f), new Color(0.42f, 0.62f, 1f) };

        readonly Transform[] _balls = new Transform[2];
        readonly LineRenderer[] _tracks = new LineRenderer[2];
        readonly TextMesh[] _tags = new TextMesh[2];
        readonly Transform[] _markers = new Transform[2];
        Transform _flag, _board;
        readonly List<Transform> _tiles = new List<Transform>();
        readonly List<TextMesh> _tileWords = new List<TextMesh>();
        readonly List<Renderer> _tileFaces = new List<Renderer>();
        readonly TextMesh[] _rowNames = new TextMesh[2];
        TextMesh _status;
        Material[] _tileMat;
        Material _halfMat;
        int _slotted;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(Portrait ? 6f : 30f, 80f, Portrait ? -240f : -230f);
        public override Vector3 CameraLook => At(-4f, 46f, 10f);
        public override float CameraFov => Portrait ? 52f : 42f;
        public override float ShadowDistance => 600f;

        public override void Build()
        {
            // Sky and sun.
            GameObject sky = Make.MeshObject("Sky", Root, Gradient(9000f, new[] { -200f, 120f, 420f, 900f, 1800f, 3200f },
                new[] { new Color(1f, 0.78f, 0.45f), new Color(1f, 0.66f, 0.36f), new Color(0.93f, 0.52f, 0.38f), new Color(0.62f, 0.45f, 0.5f), new Color(0.32f, 0.36f, 0.55f), new Color(0.16f, 0.2f, 0.38f) }), Make.Glow(Color.white, false));
            sky.transform.localPosition = new Vector3(0, 0, 3000f);
            GameObject glow = Make.MeshObject("Sun glow", Root, Quad(1400f, new Color(1f, 0.75f, 0.35f, 0.75f)), Make.Glow(Color.white, true, Make.SoftDot()));
            glow.transform.localPosition = new Vector3(520f, 200f, 2950f);
            GameObject disc = Make.MeshObject("Sun", Root, Quad(240f, new Color(1f, 0.95f, 0.75f, 1f)), Make.Glow(Color.white, true, Make.SoftDot()));
            disc.transform.localPosition = new Vector3(520f, 200f, 2940f);
            // Gulls.
            Material gull = Make.Glow(new Color(0.15f, 0.12f, 0.15f, 0.9f), false);
            foreach (Vector3 g in new[] { new Vector3(260f, 620f, 2000f), new Vector3(340f, 580f, 2000f) })
            {
                LineRenderer w = Stroke(Root, "Gull", Color.black, 5f, false); w.sharedMaterial = gull;
                Points(w, new[] { g + new Vector3(-30f, 8f, 0), g + new Vector3(-12f, 12f, 0), g, g + new Vector3(12f, 12f, 0), g + new Vector3(30f, 8f, 0) });
            }

            // The sea, with the sun's glitter on it.
            Shape(PrimitiveType.Cube, "Sea", Root, new Vector3(0, -20f, 1850f), new Vector3(9000f, 2f, 2500f), Make.Lit(new Color(0.08f, 0.13f, 0.22f), 0.95f));
            GameObject glint = Make.MeshObject("Glitter", Root, Quad(1f, new Color(1f, 0.75f, 0.4f, 0.45f)), Make.Glow(Color.white, true, Make.SoftDot()));
            glint.transform.localPosition = new Vector3(470f, -18f, 2100f);
            glint.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            glint.transform.localScale = new Vector3(260f, 2000f, 1f);

            // The links: rough, dunes, the green and its fringe, a pot bunker.
            Material rough = Make.Lit(new Color(0.3f, 0.33f, 0.14f), 0.2f);
            Material dune = Make.Lit(new Color(0.45f, 0.42f, 0.22f), 0.2f);
            Shape(PrimitiveType.Cube, "Land", Root, new Vector3(0, -3f, 100f), new Vector3(4000f, 6f, 1100f), rough);
            var dice = new System.Random(3);
            for (int i = 0; i < 14; i++)
            {
                float x = -1400f + i * 210f + (float)dice.NextDouble() * 80f;
                Shape(PrimitiveType.Sphere, "Dune", Root, new Vector3(x, -14f, 640f + (float)dice.NextDouble() * 40f), new Vector3(320f + (float)dice.NextDouble() * 200f, 40f + (float)dice.NextDouble() * 30f, 160f), i % 3 == 0 ? rough : dune);
            }
            Material marram = Make.Lit(new Color(0.55f, 0.5f, 0.28f), 0.2f);
            for (int i = 0; i < 40; i++)
            {
                float x = -500f + (float)dice.NextDouble() * 1000f, z = 260f + (float)dice.NextDouble() * 320f;
                if (Mathf.Abs(x) < 170f && z < 330f) continue;
                Shape(PrimitiveType.Cube, "Grass", Root, new Vector3(x, 6f, z), new Vector3(1.2f, 14f, 1.2f), marram).transform.localRotation = Quaternion.Euler(0, 0, (float)dice.NextDouble() * 30f - 15f);
            }
            Shape(PrimitiveType.Cylinder, "Fringe", Root, new Vector3(0, 0.2f, -30f), new Vector3(300f, 0.2f, 290f), Make.Lit(new Color(0.22f, 0.42f, 0.14f), 0.25f));
            Shape(PrimitiveType.Cylinder, "Green", Root, new Vector3(0, 0.4f, -30f), new Vector3(280f, 0.2f, 270f), Make.Lit(new Color(0.34f, 0.62f, 0.22f), 0.35f));
            // Mowing stripes, inside the green.
            Material stripe = Make.Lit(new Color(0.4f, 0.7f, 0.27f), 0.35f);
            for (int i = -6; i <= 6; i += 2)
            {
                float x = i * 20f, half = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(x / 140f, 2f))) * 135f - 6f;
                if (half > 8f) Shape(PrimitiveType.Cube, "Stripe", Root, new Vector3(x, 0.52f, -30f), new Vector3(20f, 0.05f, half * 2f), stripe);
            }
            Shape(PrimitiveType.Sphere, "Bunker lip", Root, new Vector3(150f, -3f, 70f), new Vector3(86f, 10f, 52f), rough);
            Shape(PrimitiveType.Sphere, "Bunker", Root, new Vector3(150f, -1.6f, 69f), new Vector3(76f, 6f, 44f), Make.Lit(new Color(0.86f, 0.74f, 0.55f), 0.1f));

            // The cup and the flag.
            GameObject cup = Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f));
            cup.transform.localPosition = new Vector3(0, 0.72f, 0);
            Hoop("Liner", Root, new Vector3(0, 0.76f, 0), CupR, CupR + 0.45f, Make.Lit(Color.white, 0.5f));
            Shape(PrimitiveType.Cylinder, "Pin", Root, new Vector3(0, 48f, 0), new Vector3(0.9f, 48f, 0.9f), Make.Lit(new Color(0.96f, 0.95f, 0.9f), 0.6f));
            _flag = new GameObject("Flag").transform;
            _flag.SetParent(Root, false);
            _flag.localPosition = new Vector3(0, 86f, 0);
            Shape(PrimitiveType.Cube, "Cloth", _flag, new Vector3(15f, 0, 0), new Vector3(30f, 18f, 0.4f), Make.Lit(new Color(1f, 0.78f, 0.3f), 0.3f));

            // The two balls, their tracks, a tag each.
            for (int p = 0; p < 2; p++)
            {
                _balls[p] = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(new Color(0.98f, 0.98f, 0.96f), 0.6f)).transform;
                _balls[p].gameObject.SetActive(false);
                _markers[p] = Hoop("Marker", Root, Vector3.zero, BallR + 0.6f, BallR + 1.6f, Make.Emissive(Tint[p], Tint[p] * 1.5f)).transform;
                _markers[p].gameObject.SetActive(false);
                _tracks[p] = Stroke(Root, "Track", new Color(Tint[p].r, Tint[p].g, Tint[p].b, 0.95f), 1.5f);
                _tags[p] = Words(Root, "", Vector3.zero, 5.5f, Tint[p]);
                _tags[p].transform.localRotation = Quaternion.Euler(22f, 0, 0);
            }

            // The scoreboard: a weathered board on two posts, a slot a hole a Player.
            Material wood = Make.Lit(new Color(0.42f, 0.3f, 0.18f), 0.35f, 0f, Make.Wood(5));
            Material paint = Make.Lit(new Color(0.12f, 0.24f, 0.16f), 0.4f);
            Material slot = Make.Lit(new Color(0.04f, 0.06f, 0.05f), 0.2f);
            Material cream = Make.Lit(new Color(0.95f, 0.92f, 0.82f), 0.4f);
            _tileMat = new[] { Make.Lit(new Color(0.85f, 0.6f, 0.15f), 0.5f), Make.Lit(new Color(0.25f, 0.42f, 0.85f), 0.5f) };
            _halfMat = cream;
            _board = new GameObject("Scoreboard").transform;
            _board.SetParent(Root, false);
            _board.localPosition = new Vector3(62f, 18f, 8f);
            _board.localRotation = Quaternion.Euler(2f, 30f, 0);
            _board.localScale = Vector3.one * 0.8f;
            foreach (float x in new[] { -38f, 38f }) Shape(PrimitiveType.Cube, "Post", _board, new Vector3(x, -10f, 2f), new Vector3(3f, 44f, 3f), wood);
            Shape(PrimitiveType.Cube, "Board", _board, new Vector3(0, 8f, 0.6f), new Vector3(96f, 32f, 2f), paint);
            Shape(PrimitiveType.Cube, "Cap", _board, new Vector3(0, 24.8f, 0.6f), new Vector3(100f, 2.2f, 4f), wood);
            Color chalk = new Color(0.95f, 0.92f, 0.82f);
            Words(_board, "HOLE", new Vector3(-36f, 18.5f, -0.6f), 3.2f, chalk);
            for (int h = 0; h < 9; h++)
            {
                float x = -21f + h * 8.2f;
                Words(_board, (h + 1).ToString(), new Vector3(x, 18.5f, -0.6f), 3.4f, chalk);
                for (int p = 0; p < 2; p++)
                {
                    float y = 9.5f - p * 9f;
                    Shape(PrimitiveType.Cube, "Slot", _board, new Vector3(x, y, -0.45f), new Vector3(6.6f, 7f, 0.3f), slot);
                    Transform tile = new GameObject("Tile").transform;
                    tile.SetParent(_board, false);
                    tile.localPosition = new Vector3(x, y, -0.9f);
                    _tileFaces.Add(Shape(PrimitiveType.Cube, "Face", tile, Vector3.zero, new Vector3(6f, 6.4f, 0.8f), cream).GetComponent<Renderer>());
                    _tileWords.Add(Words(tile, "", new Vector3(0, 0, -0.5f), 4.2f, Color.white));
                    tile.gameObject.SetActive(false);
                    _tiles.Add(tile);
                }
            }
            for (int p = 0; p < 2; p++) _rowNames[p] = Words(_board, "", new Vector3(-36f, 9.5f - p * 9f, -0.6f), 3.2f, Tint[p]);
            _status = Words(_board, "", new Vector3(0, -2.8f, -0.6f), 3f, chalk);
            _status.transform.localPosition = new Vector3(0, -12f, -0.6f);

            Sun(Root, new Vector3(42f, 200f, 0f), new Color(1f, 0.8f, 0.6f), 1.1f);
            Sun(Root, new Vector3(30f, -20f, 0f), new Color(0.75f, 0.7f, 0.8f), 0.45f);
            Lamp(Root, new Vector3(-160f, 260f, -320f), new Vector3(0, 0, -20f), new Color(0.75f, 0.8f, 1f), 160000f, 45f, 900f);
        }

        static Mesh Gradient(float width, float[] ys, Color[] cs)
        {
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            for (int i = 0; i < ys.Length; i++)
            {
                verts.Add(new Vector3(-width / 2, ys[i], 0)); verts.Add(new Vector3(width / 2, ys[i], 0));
                cols.Add(cs[i]); cols.Add(cs[i]);
                if (i > 0) { int a = (i - 1) * 2; tris.Add(a); tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3); }
            }
            var m = new Mesh();
            m.SetVertices(verts); m.SetColors(cols); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh Quad(float size, Color c)
        {
            float h = size / 2;
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(-h, h, 0), new Vector3(h, h, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetColors(new List<Color> { c, c, c, c });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
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
                if (v.Path[0].y * Near > start.y + 12f) pts.Add(start);
                foreach (Vector2 q in v.Path) pts.Add(new Vector2(q.x, q.y < 0f ? q.y * Near : q.y));
                if (Vector2.Distance(pts[pts.Count - 1], end) > 3f && (v.Made || v.HasRollout)) pts.Add(end);
                return pts;
            }
            Vector2 bend = new Vector2((start.x + end.x) * 0.5f + Mathf.Clamp(v.LineIn, -6f, 6f) * 0.9f, (start.y + end.y) * 0.5f);
            for (int i = 0; i <= 30; i++) { float t = i / 30f; pts.Add((1 - t) * (1 - t) * start + 2 * (1 - t) * t * bend + t * t * end); }
            return pts;
        }

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

        static string Short(string name)
        {
            string n = name.ToUpperInvariant();
            string[] parts = n.Split(' ');
            if (n.Length > 6 && parts.Length > 1 && parts[parts.Length - 1].Length <= 2) return parts[0].Substring(0, 1) + parts[parts.Length - 1];
            return n.Length > 6 ? n.Substring(0, 6) : n;
        }

        void ClearHole()
        {
            for (int p = 0; p < 2; p++)
            {
                _balls[p].gameObject.SetActive(false);
                _markers[p].gameObject.SetActive(false);
                _tracks[p].positionCount = 0;
                _tags[p].text = "";
            }
        }

        void Tee(int who, int feet, int tee)
        {
            Vector2 s = TeeAt(feet, tee);
            _balls[who].gameObject.SetActive(true);
            _balls[who].localPosition = new Vector3(s.x, BallR + 0.55f, s.y);
            _markers[who].gameObject.SetActive(true);
            _markers[who].localPosition = new Vector3(s.x, 0.85f, s.y);   // clear of the green's top (0.6): level with it, the ring flickered
        }

        void Board(ShotView v, bool all)
        {
            int[] results = v.Get<int[]>("results") ?? new int[0];
            for (int p = 0; p < 2; p++) _rowNames[p].text = p < v.Names.Length ? Short(v.Names[p]) : "";
            int upto = all ? Mathf.Min(9, results.Length) : _slotted;
            for (int h = 0; h < 9; h++) Slot(h, h < upto ? results[h] : 99, false);
            _slotted = upto;
            int mc = v.Int("mc");
            string lead = mc > 0 ? Short(v.Names[0]) : mc < 0 && v.Names.Length > 1 ? Short(v.Names[1]) : "";
            _status.text = v.Flag("sudden") ? "EXTRA HOLES" + (mc == 0 ? "  ·  ALL SQUARE" : "") : mc == 0 ? "ALL SQUARE" : lead + "  " + Mathf.Abs(mc) + " UP";
        }

        // Show hole h's tiles: 1 the first Player won it, -1 the second, 0 halved, 99 not played.
        void Slot(int h, int result, bool drop)
        {
            for (int p = 0; p < 2; p++)
            {
                int i = h * 2 + p;
                bool on = result == 0 || (result == 1 && p == 0) || (result == -1 && p == 1);
                _tiles[i].gameObject.SetActive(on && result != 99);
                if (!on || result == 99) continue;
                _tileFaces[i].sharedMaterial = result == 0 ? _halfMat : _tileMat[p];
                _tileWords[i].text = result == 0 ? "/" : "W";
                _tileWords[i].color = result == 0 ? new Color(0.2f, 0.2f, 0.2f) : Color.white;
            }
        }

        public override void Begin(ShotView start)
        {
            ClearHole();
            _slotted = 0;
            Board(start, true);
            Tee(start.Next, start.NextFeet, start.NextTee);
        }

        public override void Ready(ShotView v)
        {
            if (v.Next == 0) ClearHole();
            Board(v, true);
            if (!v.Over) Tee(v.Next, v.NextFeet, v.NextTee);
        }

        public override IEnumerator Play(ShotView v)
        {
            int who = Mathf.Clamp(v.Player, 0, 1);
            if (who == 0) { ClearHole(); Tee(0, v.Feet, v.Tee); }
            List<Vector2> roll = RollOf(v);
            var track = new List<Vector3>();
            Transform ball = _balls[who];
            ball.gameObject.SetActive(true);
            _markers[who].gameObject.SetActive(false);
            _tags[who].text = "";
            float seconds = Mathf.Lerp(1.2f, 1.9f, Mathf.InverseLerp(3f, 11f, v.Feet));
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                Vector2 p = Along(roll, Roll(t, v.Made));
                ball.localPosition = new Vector3(p.x, BallR + 0.55f, p.y);
                ball.Rotate(Vector3.right, 400f * Time.deltaTime * (1f - t), Space.World);
                track.Add(new Vector3(p.x, 1.0f, p.y));
                Points(_tracks[who], track);
                yield return null;
            }
            Vector2 end = roll[roll.Count - 1];
            ball.localPosition = new Vector3(end.x, BallR + 0.55f, end.y);
            string name = who < v.Names.Length ? Short(v.Names[who]) : "";
            if (v.Made)
            {
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f) { ball.localPosition = new Vector3(0, Mathf.Lerp(BallR + 0.55f, -4f, t), 0); yield return null; }
                ball.gameObject.SetActive(false);
                Sound("golf_cup", 0.8f);
                _tags[who].text = name + (v.BackDoor ? " IN, BACK DOOR" : " IN");
                _tags[who].transform.localPosition = new Vector3(who == 0 ? -16f : 16f, 14f + who * 8f, 4f);
                _tags[who].anchor = who == 0 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            }
            else
            {
                float past = v.HasRollout ? v.RolloutIn : end.y;
                string how = past >= 0f ? Mathf.Abs(Mathf.RoundToInt(past)) + " IN PAST" : Mathf.Abs(Mathf.RoundToInt(past)) + " IN SHORT";
                _tags[who].text = name + "  " + how;
                _tags[who].anchor = end.x < 0 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                _tags[who].transform.localPosition = new Vector3(end.x + (end.x < 0 ? -5f : 5f), 9f + who * 7f, end.y);
            }

            if (who == 1 || v.Next == 0)
            {
                // The hole is decided: slot its tile in by hand, from above.
                int[] results = v.Get<int[]>("results") ?? new int[0];
                int h = results.Length - 1;
                if (h >= 0 && h < 9)
                {
                    yield return new WaitForSeconds(0.5f);
                    Slot(h, results[h], true);
                    var moving = new List<Transform>();
                    for (int p = 0; p < 2; p++) if (_tiles[h * 2 + p].gameObject.activeSelf) moving.Add(_tiles[h * 2 + p]);
                    var homes = new List<Vector3>();
                    foreach (Transform m in moving) homes.Add(m.localPosition);
                    for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
                    {
                        float e = 1f - (1f - t) * (1f - t);
                        for (int k = 0; k < moving.Count; k++)
                        {
                            moving[k].localPosition = homes[k] + new Vector3(0, (1f - e) * 26f, (1f - e) * -6f);
                            moving[k].localRotation = Quaternion.Euler(0, 0, (1f - e) * 25f);
                        }
                        yield return null;
                    }
                    for (int k = 0; k < moving.Count; k++) { moving[k].localPosition = homes[k]; moving[k].localRotation = Quaternion.identity; }
                    Sound("matchplay_board", "door_slam", 0.35f, 1.8f);   // puttr-app-2: the game's own sound, the shared one until recorded
                    _slotted = Mathf.Max(_slotted, h + 1);
                }
                if (v.Points != 0) Arcade.Sparks(At(_board.localPosition + new Vector3(0, 10f, -4f)), 14);
                if (v.Over) Arcade.Celebrate(50, At(0, 40f, 0));
                yield return new WaitForSeconds(0.9f);
            }
            else yield return new WaitForSeconds(0.6f);
        }

        public override void Tick()
        {
            if (_flag != null) _flag.localRotation = Quaternion.Euler(0, -20f + Mathf.Sin(Time.time * 1.7f) * 9f + Mathf.Sin(Time.time * 4.1f) * 3f, 0);
        }
    }
}
