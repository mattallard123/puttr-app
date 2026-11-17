// Cricket: the Puttr app's game, on a pub dartboard.
//
// The numbers are distances: 3 to 10 feet round the board (each twice, so
// the board keeps its twenty wedges) and the bull at 11. Choose a distance,
// putt from it, and a holed putt throws a dart into that number:
//
//   single  1 mark   the ball went in
//   double  2 marks  it went in within 10 degrees of the center of the cup
//   triple  3 marks  within 5 degrees
//
// A miss, a putt left short, or one that drops in the back door scores
// nothing. Three marks closes a number. Marks past three score the distance
// in feet each, for as long as the other side has not closed it. Close them
// all, and be level or ahead on points, to win. Three darts a turn.
//
// Two sides: Players 1 and 2 against 3 and 4, or one against one. A Player
// alone plays Puttr AI, which throws for itself.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class CricketMode : StageMode
    {
        public override string Name => "Cricket";
        public override bool DealsTee => false;
        public override string Tee => "Center tee"; // the distance is the number being thrown at
        public override int Putter => _members[_side].Count > 0 ? _members[_side][_thrower[_side] % _members[_side].Count] : 0; // whose difficulty applies to this putt
        public override string Blurb => "Darts, with a putter.\nHole it to hit your number.\nClose them all to win.";
        public override string Rule => "Three darts a turn. Hole the putt to hit the number you are putting from.";
        public override string Icon => "Target";
        public override Color Accent => new Color(0.92f, 0.36f, 0.3f);
        public override string[] Tags => new[] { "2 sides", "3 to 11 ft" };
        public override string SoloOpponent => "Puttr";
        public override int Difficulty => 2;
        public override GameAudio.Scene Sound => GameAudio.Scene.Table;
        public override string[] HowTo => new[]
        {
            "Putt from the distance that is lit, 3 to 11 feet. Hole it and a dart hits that number. " + Hud.Choose("The left and right arrows change the distance.", "Swipe left or right to change the distance.", "Left and right on the remote change the distance."),
            "A clean drop in the middle of the cup is a triple, 3 marks. Slightly off center is a double. Anywhere else in the cup is a single.",
            "Three marks closes a number. Marks past three score its distance in feet until the other side closes it. Close everything and lead to win.",
        };
        public override int Score(int player) => _points == null ? 0 : _points[SideOf(player)];

        const int Numbers = 9;         // 3 to 10 feet, and the bull at 11
        const int TurnLimit = 15;      // a side's turns before the game is called
        // The board, in fifths of a millimetre of a real one.
        const float Double1 = 34f, Double0 = 32.4f, Triple1 = 21.4f, Triple0 = 19.8f, Bull1 = 3.18f, Bull0 = 1.27f, Edge = 45.1f;
        static readonly int[] Wedges = { 10, 3, 7, 5, 9, 4, 8, 6, 10, 3, 7, 5, 9, 4, 8, 6, 10, 3, 7, 5 };
        static readonly Vector3 BoardAt = new Vector3(-34f, 2f, 0f);
        static readonly Vector3 SlateAt = new Vector3(58f, 2f, -0.4f);
        static readonly Color[] SideColor = { Make.Hex(0x5687FF), Make.Hex(0xFFB300) };

        // While a dart is in the air and its result is up, the camera moves in on where it
        // lands, close enough to read the number and see the triple or double ring. The spot
        // sits a little above the middle of the screen, clear of the result banner.
        Vector3? _zoom;
        public override Vector3 CameraPosition => _zoom is Vector3 z ? At(z.x, z.y - 6f, -120f) : At(9f, 13f, -218f);
        public override Vector3 CameraLook => _zoom is Vector3 z ? At(z.x, z.y - 6f, 0f) : At(9f, 13f, 0f);
        public override float CameraFov => _zoom.HasValue ? 26f : 34f;

        // The close-up is on the dart itself, a little above the middle of the screen so the
        // result banner never covers it. Its number, about 20 further out, is in the frame too.
        void ZoomOn(Vector3 to, int index, int miss) => _zoom = new Vector3(to.x, to.y, 0f);
        public override float ShadowDistance => 420f;

        int _players, _side, _dart, _turns, _triples; // _triples: triples thrown this turn
        bool _house, _over, _clear;
        int[,] _marks;
        int[] _points, _target, _thrower;
        readonly List<int>[] _members = { new List<int>(), new List<int>() };
        readonly string[] _sideName = new string[2];
        readonly System.Random _rng = new System.Random(5);

        // The set.
        readonly Transform[,] _darts = new Transform[2, 3];
        MeshFilter _lit;
        Material _litGlow;
        TextMesh[] _slateName = new TextMesh[2], _slatePoints = new TextMesh[2];
        readonly TextMesh[,] _slateMark = new TextMesh[2, Numbers];
        readonly GameObject[,] _slateRing = new GameObject[2, Numbers];
        readonly TextMesh[] _slateNumber = new TextMesh[Numbers];
        Material _slateBull; // the bull's row shows the Puttr mark: it is the 11 foot tee on the mat
        Transform _slateBar;
        TextMesh _slateTurn;

        int SideOf(int player) => _members[1].Contains(player) ? 1 : 0;
        static int Feet(int index) => index + 3;
        static string Label(int index) => index == Numbers - 1 ? "BULL" : Feet(index).ToString();

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // A pub wall, one warm lamp over the board.
            Sun(Root, new Vector3(34f, 18f, 0f), new Color(1f, 0.93f, 0.82f), 1.25f);
            Sun(Root, new Vector3(10f, -40f, 0f), new Color(0.6f, 0.7f, 1f), 0.25f).shadows = LightShadows.None;
            Light lamp = World.NewLight(Root, "Board lamp", LightType.Spot, new Color(1f, 0.9f, 0.72f), 9000f);
            lamp.transform.localPosition = BoardAt + new Vector3(0f, 78f, -56f);
            lamp.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -78f, 56f));
            lamp.spotAngle = 70f;
            lamp.innerSpotAngle = 40f;
            lamp.range = 220f;

            Material wall = Make.Lit(new Color(0.42f, 0.29f, 0.2f), 0.32f, 0f, Make.Wood(3));
            Make.Box("Wall", Root, new Vector3(10f, 0f, 4f), new Vector3(520f, 300f, 4f), wall, null, false);
            Material dark = Make.Lit(new Color(0.03f, 0.03f, 0.032f), 0.25f);
            Make.Box("Rail", Root, new Vector3(10f, -78f, 0.5f), new Vector3(520f, 10f, 5f), Make.Lit(new Color(0.16f, 0.1f, 0.07f), 0.5f), null, false);

            // The surround that catches a miss, then the board itself.
            // (Round meshes of our own: a Unity cylinder shows its twenty flat sides at this size.)
            Make.MeshObject("Surround", Root, FaceMesh(62f, 128), Make.Lit(new Color(0.045f, 0.05f, 0.055f), 0.2f)).transform.localPosition = BoardAt + new Vector3(0f, 0f, 0.6f);
            Make.MeshObject("Surround edge", Root, Props.Torus(62f, 1.3f, 128, 10), Make.Lit(new Color(0.03f, 0.032f, 0.036f), 0.35f)).transform.SetLocalPositionAndRotation(BoardAt + new Vector3(0f, 0f, 0.9f), Quaternion.Euler(-90f, 0f, 0f));
            Disc("Board", BoardAt + new Vector3(0f, 0f, -1f), Edge - 0.6f, 4f, dark);
            Make.MeshObject("Board edge", Root, Props.Torus(Edge - 0.5f, 1.6f, 128, 10), dark).transform.SetLocalPositionAndRotation(BoardAt + new Vector3(0f, 0f, -1.6f), Quaternion.Euler(-90f, 0f, 0f));
            Material face = Make.Lit(Color.white, 0.1f, 0f, BoardFace());
            GameObject front = Make.MeshObject("Face", Root, FaceMesh(Edge, 96), face);
            front.transform.localPosition = BoardAt + new Vector3(0f, 0f, -3.02f);

            // The spider: real wire, standing off the sisal.
            Material wire = Make.Lit(new Color(0.78f, 0.8f, 0.83f), 0.86f, 1f);
            float z = -3.2f;
            foreach (float r in new[] { Double1, Double0, Triple1, Triple0, Bull1, Bull0 })
            {
                GameObject ring = Make.MeshObject("Wire", Root, Props.Torus(r, 0.13f, 96, 6), wire);
                ring.transform.localPosition = BoardAt + new Vector3(0f, 0f, z);
                ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            for (int i = 0; i < 20; i++)
            {
                Vector2 d = Around(i * 18f + 9f);
                Props.Rod("Wire", Root, BoardAt + new Vector3(d.x * Bull1, d.y * Bull1, z), BoardAt + new Vector3(d.x * (Double1 + 1.6f), d.y * (Double1 + 1.6f), z), 0.12f, wire);
            }
            GameObject band = Make.MeshObject("Number ring", Root, Props.Torus(Edge - 1.6f, 0.32f, 128, 8), wire);
            band.transform.localPosition = BoardAt + new Vector3(0f, 0f, z);
            band.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            for (int i = 0; i < 20; i++)
            {
                Vector2 d = Around(i * 18f);
                Words(Root, Wedges[i].ToString(), BoardAt + new Vector3(d.x * 39.3f, d.y * 39.3f, z - 0.3f), 4.4f, new Color(0.93f, 0.93f, 0.94f), TextAnchor.MiddleCenter, 80);
            }

            // The number in play glows.
            _litGlow = Make.Glow(new Color(0.48f, 0.9f, 0.3f, 0.22f), true);
            GameObject lit = Make.MeshObject("Lit", Root, new Mesh(), _litGlow);
            lit.transform.localPosition = BoardAt + new Vector3(0f, 0f, z - 0.25f);
            lit.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            _lit = lit.GetComponent<MeshFilter>();

            BuildSlate();

            for (int s = 0; s < 2; s++)
                for (int i = 0; i < 3; i++)
                {
                    _darts[s, i] = BuildDart(SideColor[s]);
                    _darts[s, i].gameObject.SetActive(false);
                }
        }

        // A cylinder facing the camera.
        void Disc(string name, Vector3 at, float radius, float thick, Material material)
        {
            GameObject go = Shape(PrimitiveType.Cylinder, name, Root, at, new Vector3(radius * 2f, thick * 0.5f, radius * 2f), material);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>Unit vector for an angle in degrees, clockwise from the top of the board.</summary>
        static Vector2 Around(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }

        // A disc in the XY plane, facing the camera, textured edge to edge.
        static Mesh FaceMesh(float radius, int segments)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var normals = new List<Vector3> { Vector3.back };
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                Vector2 d = Around(i * 360f / segments);
                verts.Add(new Vector3(d.x * radius, d.y * radius, 0f));
                uvs.Add(new Vector2(0.5f + d.x * 0.5f, 0.5f + d.y * 0.5f));
                normals.Add(Vector3.back);
                if (i > 0) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        // Part of a ring, for lighting up a wedge. Angles clockwise from the top.
        static void Band(List<Vector3> verts, List<int> tris, float r0, float r1, float a0, float a1)
        {
            const int steps = 8;
            int first = verts.Count;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 d = Around(Mathf.Lerp(a0, a1, i / (float)steps));
                verts.Add(new Vector3(d.x * r0, d.y * r0, 0f));
                verts.Add(new Vector3(d.x * r1, d.y * r1, 0f));
                if (i == 0) continue;
                int a = first + (i - 1) * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
                tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
            }
        }

        // The face of the board, painted: wedges, rings, bull, and the grain of the sisal.
        static Texture2D BoardFace()
        {
            const int n = 1024;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            var px = new Color32[n * n];
            Color black = new Color(0.085f, 0.075f, 0.065f), cream = new Color(0.83f, 0.76f, 0.58f);
            Color red = new Color(0.66f, 0.12f, 0.1f), green = new Color(0.06f, 0.42f, 0.2f), rim = new Color(0.035f, 0.035f, 0.038f);
            var rng = new System.Random(21);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * Edge;
                    float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    int wedge = Mathf.FloorToInt(Mathf.Repeat(angle + 9f, 360f) / 18f) % 20;
                    bool even = wedge % 2 == 0;
                    Color c;
                    if (r > Double1) c = rim;
                    else if (r < Bull0) c = red;
                    else if (r < Bull1) c = green;
                    else if (r > Double0 || (r > Triple0 && r < Triple1)) c = even ? red : green;
                    else c = even ? black : cream;
                    // Sisal: a fine speckle, and fibres that run in toward the middle.
                    float speck = (float)rng.NextDouble() - 0.5f;
                    float fibre = Mathf.PerlinNoise(angle * 2.1f, r * 0.35f) - 0.5f;
                    float grain = r > Double1 ? speck * 0.02f : speck * 0.11f + fibre * 0.1f;
                    c = new Color(Mathf.Clamp01(c.r + grain), Mathf.Clamp01(c.g + grain), Mathf.Clamp01(c.b + grain), 1f);
                    px[y * n + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // The chalk slate beside the board.
        void BuildSlate()
        {
            Material frame = Make.Lit(new Color(0.3f, 0.19f, 0.12f), 0.45f, 0f, Make.Wood(9));
            Make.Box("Slate frame", Root, SlateAt + new Vector3(0f, 0f, 1.2f), new Vector3(62f, 98f, 3f), frame, null, false);
            Make.Box("Slate", Root, SlateAt + new Vector3(0f, 0f, -0.2f), new Vector3(56f, 92f, 1f), Make.Lit(new Color(0.055f, 0.065f, 0.062f), 0.18f), null, false);
            var root = new GameObject("Chalk").transform;
            root.SetParent(Root, false);
            root.localPosition = SlateAt + new Vector3(0f, 0f, -0.8f);
            Color chalk = new Color(0.92f, 0.92f, 0.88f);

            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -17f : 17f;
                _slateName[s] = Words(root, "", new Vector3(x, 40f, 0f), 2.7f, SideColor[s], TextAnchor.MiddleCenter, 64);
                _slatePoints[s] = Words(root, "0", new Vector3(x, -39f, 0f), 5.4f, SideColor[s], TextAnchor.MiddleCenter, 96);
            }
            Card("Rule", root, new Vector3(0f, 35.6f, 0f), new Vector2(50f, 0.25f), Make.Glow(new Color(1f, 1f, 1f, 0.35f), false));
            Card("Rule", root, new Vector3(0f, -32.6f, 0f), new Vector2(50f, 0.25f), Make.Glow(new Color(1f, 1f, 1f, 0.35f), false));
            Words(root, "POINTS", new Vector3(0f, -39f, 0f), 1.7f, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter, 48);
            _slateTurn = Words(root, "", new Vector3(0f, 44.6f, 0f), 1.5f, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter, 48);

            _slateBar = Card("In play", root, Vector3.zero, new Vector2(52f, 6.6f), Make.Glow(new Color(0.48f, 0.83f, 0.27f, 0.2f), false)).transform;
            Material ring = Make.Glow(chalk, false);
            for (int i = 0; i < Numbers; i++)
            {
                float y = RowY(i);
                bool bull = i == Numbers - 1;
                Texture2D mark = bull ? Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite") : null;
                _slateNumber[i] = Words(root, mark != null ? "" : Label(i), new Vector3(0f, y, -0.05f), bull ? 2.6f : 3.6f, chalk, TextAnchor.MiddleCenter, 80);
                if (mark != null)
                {
                    const float tall = 5.8f;
                    _slateBull = Make.Glow(chalk, false, mark);
                    Card("Bull", root, new Vector3(0f, y, -0.05f), new Vector2(tall * mark.width / mark.height, tall), _slateBull);
                }
                for (int s = 0; s < 2; s++)
                {
                    float x = s == 0 ? -17f : 17f;
                    _slateMark[s, i] = Words(root, "", new Vector3(x, y, -0.05f), 3.4f, chalk, TextAnchor.MiddleCenter, 80);
                    _slateRing[s, i] = Hoop("Closed", root, new Vector3(x, y + 0.1f, -0.05f), 2.65f, 3.05f, ring);
                    _slateRing[s, i].SetActive(false);
                }
            }
        }

        static float RowY(int index) => 30f - index * 7.2f;

        // A dart, tip at the origin, flying along +Z.
        Transform BuildDart(Color flight)
        {
            var dart = new GameObject("Dart").transform;
            dart.SetParent(Root, false);
            Quaternion back = Quaternion.Euler(-90f, 0f, 0f); // a lathe's height runs back from the tip
            Material steel = Make.Lit(new Color(0.8f, 0.82f, 0.85f), 0.9f, 1f);
            Material brass = Make.Lit(new Color(0.83f, 0.62f, 0.22f), 0.78f, 1f);
            Material black = Make.Lit(new Color(0.03f, 0.03f, 0.035f), 0.45f);
            Material fin = Make.Lit(flight, 0.55f);

            var tip = new List<Vector2> { new Vector2(0.02f, 0f), new Vector2(0.16f, 3.4f), new Vector2(0.2f, 5.2f) };
            Make.MeshObject("Tip", dart, Make.Lathe(tip, 12), steel).transform.localRotation = back;

            // The barrel, with its grip rings.
            var barrel = new List<Vector2> { new Vector2(0.2f, 5.2f), new Vector2(0.62f, 5.9f) };
            for (int i = 0; i < 7; i++)
            {
                float h = 6.2f + i * 1.0f;
                barrel.Add(new Vector2(0.68f, h));
                barrel.Add(new Vector2(0.68f, h + 0.55f));
                barrel.Add(new Vector2(0.56f, h + 0.65f));
                barrel.Add(new Vector2(0.56f, h + 0.9f));
            }
            barrel.Add(new Vector2(0.62f, 13.4f));
            barrel.Add(new Vector2(0.3f, 14.2f));
            Make.MeshObject("Barrel", dart, Make.Lathe(barrel, 16), brass).transform.localRotation = back;

            var shaft = new List<Vector2> { new Vector2(0.3f, 14.2f), new Vector2(0.26f, 15f), new Vector2(0.22f, 20.6f), new Vector2(0.1f, 21f) };
            Make.MeshObject("Shaft", dart, Make.Lathe(shaft, 10), black).transform.localRotation = back;

            // Four fins.
            for (int i = 0; i < 2; i++)
            {
                GameObject wing = Make.Box("Flight", dart, new Vector3(0f, 0f, -20.6f), i == 0 ? new Vector3(4.8f, 0.07f, 5.2f) : new Vector3(0.07f, 4.8f, 5.2f), fin, null, false);
                wing.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            return dart;
        }

        // ── The game ────────────────────────────────────────────────────────

        public override void Preview()
        {
            Begin(2);
            // A board with a game in progress, and a dart in the triple.
            _marks[0, 0] = 3; _marks[1, 0] = 2; _marks[0, 2] = 1; _marks[1, 1] = 3; _marks[0, 4] = 2;
            _points[0] = 6;
            _target[0] = 4;
            Stick(_darts[0, 0], Spot(4, 3, 0), 0);
            Show();
        }

        public override void Begin(int players)
        {
            _zoom = null;
            _players = Mathf.Clamp(players, 1, 4);
            _house = _players == 1;
            _members[0].Clear();
            _members[1].Clear();
            // Players 1 and 2 are a pair when there are more than two.
            int first = _players >= 3 ? 2 : 1;
            for (int i = 0; i < _players; i++) _members[i < first ? 0 : 1].Add(i);
            for (int s = 0; s < 2; s++)
            {
                var names = new List<string>();
                foreach (int p in _members[s]) names.Add(Arcade != null ? Arcade.PlayerName(p) : "Player " + (p + 1));
                // Two Players nobody has named: "Players 1 & 2", which fits where two full names would not.
                bool plain = names.Count == 2 && names[0].StartsWith("Player ") && names[1].StartsWith("Player ");
                _sideName[s] = names.Count == 0 ? "Puttr" : plain ? "Players " + names[0].Substring(7) + " & " + names[1].Substring(7) : string.Join(" & ", names);
            }
            _marks = new int[2, Numbers];
            _points = new int[2];
            _target = new int[2];
            _thrower = new int[2];
            _side = 0;
            _dart = 0;
            _turns = 0;
            _over = false;
            _clear = false;
            foreach (Transform dart in _darts) dart.gameObject.SetActive(false);
            if (Arcade != null && Arcade.Hud != null)
            {
                Arcade.Hud.BuildBoard(new List<string> { Short(_sideName[0]), Short(_sideName[1]) }, 3, "POINTS", true, "DARTS");
                Board();
            }
            Show();
        }

        static string Short(string name) => name.Length > 14 ? name.Substring(0, 13) + "…" : name;

        public override IEnumerator Prepare()
        {
            if (_clear)
            {
                _clear = false;
                foreach (Transform dart in _darts) dart.gameObject.SetActive(false);
                Board();
            }
            yield break;
        }

        public override string Prompt
        {
            get
            {
                string who = _members[_side].Count > 0 ? Arcade.PlayerName(_members[_side][_thrower[_side] % _members[_side].Count]) : _sideName[_side];
                int t = _target[_side];
                string from = t == Numbers - 1 ? "the bull, 11 feet" : Feet(t) + " feet";
                return who + "   ·   dart " + (_dart + 1) + " of 3   ·   putt from " + from;
            }
        }

        /// <summary>Left or right: the next distance still worth putting at.</summary>
        public override void Nudge(int direction)
        {
            if (_over) return;
            int t = _target[_side];
            for (int i = 0; i < Numbers; i++)
            {
                t = (t + direction + Numbers) % Numbers;
                if (!Dead(t)) break;
            }
            _target[_side] = t;
            Arcade.Audio.UiMove();
            Show();
            Board();
            Arcade.Hud.SetPrompt(Prompt);
        }

        bool Dead(int index) => _marks[0, index] >= 3 && _marks[1, index] >= 3;

        public override IEnumerator Play(PuttResult putt)
        {
            int side = _side, index = _target[side];
            bool holed = putt.Made, shortOf = !putt.Launch.Reached;
            int off = putt.HasEntry ? Mathf.Abs(putt.EntryAngle) : 45;
            bool backDoor = holed && putt.HasEntry && off >= 90;
            int marks = !holed || backDoor ? 0 : off < 5 ? 3 : off < 10 ? 2 : 1;

            yield return Throw(side, _dart, index, marks, shortOf ? 2 : backDoor ? 1 : 0);
            string result = Land(side, index, marks, shortOf ? "SHORT" : backDoor ? "BACK DOOR" : "MISS", shortOf ? "It never reached the hole" : backDoor ? "In the back of the cup: no score" : "No dart on the board");
            ShowPutt(putt, result, marks > 0 ? Make.Bright : Make.Red);
            Arcade.Shot("result");
            yield return new WaitForSeconds(_dart == 3 && _triples == 3 ? 3.4f : 1.3f); // a nine mark gets its moment
            _zoom = null; // back out to the whole board
            yield return After();
        }

        // A dart has landed: score it, say so, and return a word for the putt card.
        string Land(int side, int index, int marks, string missWord, string missSub)
        {
            Hud hud = Arcade.Hud;
            string number = index == Numbers - 1 ? "BULL" : Feet(index).ToString();
            string result;
            if (marks == 0)
            {
                hud.Banner(missWord, missSub, Make.Red, 1.4f);
                Arcade.Audio.Miss();
                result = missWord.Substring(0, 1) + missWord.Substring(1).ToLowerInvariant();
            }
            else
            {
                int before = _marks[side, index], total = before + marks;
                int extra = Mathf.Max(0, total - 3);
                bool open = _marks[1 - side, index] < 3;
                int scored = open ? extra * Feet(index) : 0;
                _marks[side, index] = Mathf.Min(3, total);
                _points[side] += scored;
                string word = marks == 3 ? "TRIPLE" : marks == 2 ? "DOUBLE" : "SINGLE";
                bool closed = before < 3 && total >= 3;
                string sub = scored > 0 ? "+" + scored + " points" : closed ? (Dead(index) ? "Closed, and dead" : "Closed") : marks + (marks == 1 ? " mark" : " marks");
                hud.Banner(word + " " + number, sub, marks == 3 ? Make.Bright : Color.white, 1.5f);
                if (marks == 3) { Arcade.Audio.Strike(); Arcade.Celebrate(40, At(BoardAt.x, BoardAt.y, -6f)); }
                else if (marks == 2) Arcade.Audio.Spare();
                else Arcade.Audio.Coin();
                result = word.Substring(0, 1) + word.Substring(1).ToLowerInvariant() + " " + number.ToLowerInvariant();
                // On to the next number once this one is closed, unless it still pays.
                if (_marks[side, index] >= 3 && !open) Advance(side);
                else if (closed && scored == 0) Advance(side);
            }
            hud.SetPip(side, _dart, marks > 0 ? Hud.Pip.Good : Hud.Pip.Bad, marks > 0 ? marks.ToString() : "");
            if (_dart == 0) _triples = 0;
            if (marks == 3) _triples++;
            _dart++;
            // Three triples in one turn: nine marks, the best a turn can be.
            if (_dart == 3 && _triples == 3)
            {
                hud.Banner("NINE MARK!", "Three triples in one turn   ·   " + _sideName[side], Make.Amber, 3.4f);
                Arcade.Audio.Strike();
                Arcade.Audio.Roar(true);
                Arcade.Audio.Bell();
                Arcade.SlowMotion(1.2f);
                Arcade.Shake(0.5f);
                Arcade.Celebrate(220, At(BoardAt.x, BoardAt.y, -8f));
                Arcade.Celebrate(160, At(SlateAt.x, SlateAt.y + 20f, -8f));
                _nineAt = Time.time;
                result = "Nine mark!";
            }
            Show();
            Board();
            return result;
        }

        // Move a side to its lowest number that is still open to it.
        void Advance(int side)
        {
            for (int i = 0; i < Numbers; i++)
                if (_marks[side, i] < 3) { _target[side] = i; return; }
            // Everything closed: go where points can still be had.
            for (int i = Numbers - 1; i >= 0; i--)
                if (!Dead(i)) { _target[side] = i; return; }
        }

        // After a dart: is the game won, is the turn over, and does Puttr AI throw?
        IEnumerator After()
        {
            if (Decided()) { _over = true; Show(); yield break; }
            if (_dart < 3) yield break;
            // The other side's turn.
            _thrower[_side]++;
            _side = 1 - _side;
            _dart = 0;
            if (_side == 0) _turns++;
            if (_turns >= TurnLimit) { _over = true; Show(); yield break; }
            if (Dead(_target[_side]) || _marks[_side, _target[_side]] >= 3) Advance(_side);
            _clear = true;
            Show();
            if (!_house || _side != 1) yield break;

            // Puttr AI throws three for itself.
            yield return new WaitForSeconds(0.7f);
            foreach (Transform dart in _darts) dart.gameObject.SetActive(false);
            _clear = false;
            Board();
            Arcade.Hud.SetPrompt("Puttr is throwing");
            for (int i = 0; i < 3 && !_over; i++)
            {
                int index = _target[1];
                double roll = _rng.NextDouble();
                int marks = roll < 0.5 ? 0 : roll < 0.84 ? 1 : roll < 0.95 ? 2 : 3;
                yield return Throw(1, _dart, index, marks, 0);
                Land(1, index, marks, "MISS", "Puttr misses");
                Arcade.Shot("house");
                yield return new WaitForSeconds(1.1f);
                _zoom = null;
                yield return new WaitForSeconds(0.5f);
                if (Decided()) { _over = true; Show(); yield break; }
            }
            _side = 0;
            _dart = 0;
            _turns++;
            if (_turns >= TurnLimit) _over = true;
            if (Dead(_target[0]) || _marks[0, _target[0]] >= 3) Advance(0);
            _clear = true;
            Show();
        }

        // Won when a side has closed everything and is not behind on points.
        bool Decided()
        {
            for (int s = 0; s < 2; s++)
            {
                bool all = true;
                for (int i = 0; i < Numbers; i++) if (_marks[s, i] < 3) all = false;
                if (all && _points[s] >= _points[1 - s]) return true;
            }
            return false;
        }

        int Winner()
        {
            if (_points[0] != _points[1]) return _points[0] > _points[1] ? 0 : 1;
            int a = 0, b = 0;
            for (int i = 0; i < Numbers; i++) { a += _marks[0, i]; b += _marks[1, i]; }
            return a == b ? -1 : a > b ? 0 : 1;
        }

        public override bool Over => _over;

        public override void Final(out string title, out string body)
        {
            int won = Winner();
            title = won < 0 ? "A draw" : _sideName[won] + " win";
            body = _sideName[0] + "     " + _points[0] + "\n" + _sideName[1] + "     " + _points[1] + "\n";
        }

        public override Hud.Line[] Results(out string headline)
        {
            int won = Winner();
            headline = won < 0 ? "A draw" : _sideName[won] + (_members[won].Count == 1 || _house && won == 1 ? " wins" : " win");
            var lines = new Hud.Line[2];
            for (int s = 0; s < 2; s++)
            {
                int closed = 0;
                for (int i = 0; i < Numbers; i++) if (_marks[s, i] >= 3) closed++;
                lines[s] = new Hud.Line { Name = _sideName[s], Value = _points[s] + (_points[s] == 1 ? " point" : " points") + "   ·   " + closed + " closed", Dot = SideColor[s], Lit = s == won, Tag = s == won ? "WINNER" : "" };
            }
            return lines;
        }

        public override void End()
        {
            _zoom = null;
            foreach (Transform dart in _darts) dart.gameObject.SetActive(false);
        }

        float _nineAt = -100f;

        public override void Tick()
        {
            // After a nine mark the three darts and the board flash gold for a few seconds.
            float since = Time.time - _nineAt;
            if (since < 3.2f && _litGlow != null)
            {
                _litGlow.SetColor("_BaseColor", new Color(1f, 0.78f, 0.2f, 0.25f + 0.25f * Mathf.Abs(Mathf.Sin(since * 9f))));
                return;
            }
            // The number in play breathes.
            if (_litGlow != null) _litGlow.SetColor("_BaseColor", new Color(0.48f, 0.9f, 0.3f, 0.2f + 0.09f * Mathf.Sin(Time.time * 3.4f)));
        }

        // ── Darts ───────────────────────────────────────────────────────────

        /// <summary>Where on the wall a dart for this number and this many marks lands. 0 marks: just off the scoring board.</summary>
        Vector3 Spot(int index, int marks, int miss)
        {
            if (miss == 2) return BoardAt + new Vector3((float)(_rng.NextDouble() - 0.5) * 30f, -56f - (float)_rng.NextDouble() * 6f, 0f);
            float jitter = (float)(_rng.NextDouble() - 0.5);
            if (index == Numbers - 1)
            {
                float rb = marks == 0 ? Bull1 + 4f + jitter * 3f : marks == 1 ? (Bull0 + Bull1) * 0.5f : 0.45f;
                Vector2 b = Around((float)_rng.NextDouble() * 360f) * rb;
                return BoardAt + new Vector3(b.x, b.y, -3.3f);
            }
            // Each distance has two or three wedges: take one of them.
            var mine = new List<int>();
            for (int i = 0; i < 20; i++) if (Wedges[i] == Feet(index)) mine.Add(i);
            int wedge = mine[_rng.Next(mine.Count)];
            float r = marks == 3 ? (Triple0 + Triple1) * 0.5f : marks == 2 ? (Double0 + Double1) * 0.5f : marks == 1 ? 27f + jitter * 5f : Double1 + 4.2f + jitter * 2.5f;
            Vector2 d = Around(wedge * 18f + jitter * (marks == 1 ? 9f : 6f)) * r;
            return BoardAt + new Vector3(d.x, d.y, marks == 0 ? -3.1f : -3.3f);
        }

        void Stick(Transform dart, Vector3 at, int seed)
        {
            var rng = new System.Random(seed * 31 + 7);
            dart.gameObject.SetActive(true);
            dart.localPosition = at + new Vector3(0f, 0f, 1.6f); // the tip is buried
            // Tail a little down and to one side, as a thrown dart hangs. Any more and, seen from in
            // front, the flight sits over the next wedge and the dart looks as if it missed.
            dart.localRotation = Quaternion.LookRotation(new Vector3(-0.1f + (float)(rng.NextDouble() - 0.5) * 0.12f, 0.15f + (float)rng.NextDouble() * 0.08f, 1f));
        }

        // Throw a dart from beside the camera. miss: 0 lands, 1 hits a wire and drops out, 2 falls short under the board.
        IEnumerator Throw(int side, int slot, int index, int marks, int miss)
        {
            Transform dart = _darts[side, Mathf.Clamp(slot, 0, 2)];
            Vector3 to = Spot(index, miss == 1 ? 1 : marks, miss);
            Vector3 from = new Vector3(BoardAt.x + 50f, BoardAt.y - 24f, -205f);
            ZoomOn(to, index, miss);
            dart.gameObject.SetActive(true);
            Arcade.Audio.Whoosh();
            const float flight = 0.34f;
            Vector3 last = from;
            for (float t = 0f; t < 1f; t += Time.deltaTime / flight)
            {
                Vector3 p = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 9f;
                dart.localPosition = p;
                Vector3 way = p - last;
                if (way.sqrMagnitude > 0.0001f) dart.localRotation = Quaternion.LookRotation(way) * Quaternion.Euler(0f, 0f, t * 300f);
                last = p;
                yield return null;
            }
            Arcade.Audio.Thump(marks > 0 ? 46f : 24f);
            Arcade.Shake(0.12f);
            if (miss != 1)
            {
                Stick(dart, to, _rng.Next(1000));
                // It quivers where it lands.
                Quaternion rest = dart.localRotation;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
                {
                    dart.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 50f) * 3f * (1f - t), 0f, 0f);
                    yield return null;
                }
                dart.localRotation = rest;
                yield break;
            }
            // Off the wire: it drops out of the board.
            Arcade.Audio.PinKnock(30f);
            Vector3 v = new Vector3(6f, 22f, -34f);
            Vector3 at = to + new Vector3(0f, 0f, -2f);
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                v += Vector3.down * 260f * Time.deltaTime;
                at += v * Time.deltaTime;
                dart.localPosition = at;
                dart.localRotation *= Quaternion.Euler(540f * Time.deltaTime, 80f * Time.deltaTime, 0f);
                yield return null;
            }
            dart.gameObject.SetActive(false);
        }

        // ── What is on show ─────────────────────────────────────────────────

        // The slate, and the light on the number in play.
        void Show()
        {
            if (_marks == null) return;
            for (int s = 0; s < 2; s++)
            {
                _slateName[s].text = Short(_sideName[s]).ToUpperInvariant();
                _slateName[s].characterSize = (_sideName[s].Length > 9 ? 1.9f : 2.7f) * 10f / 64f * 1.4f; // long names are written smaller
                _slateName[s].color = s == _side && !_over ? SideColor[s] : new Color(SideColor[s].r, SideColor[s].g, SideColor[s].b, 0.55f);
                _slatePoints[s].text = _points[s].ToString();
                for (int i = 0; i < Numbers; i++)
                {
                    int m = _marks[s, i];
                    _slateMark[s, i].text = m == 0 ? "" : m == 1 ? "/" : "X";
                    _slateRing[s, i].SetActive(m >= 3);
                    Color ink = Dead(i) ? new Color(1f, 1f, 1f, 0.3f) : new Color(0.92f, 0.92f, 0.88f);
                    _slateMark[s, i].color = ink;
                }
            }
            for (int i = 0; i < Numbers; i++) _slateNumber[i].color = Dead(i) ? new Color(1f, 1f, 1f, 0.3f) : new Color(0.92f, 0.92f, 0.88f);
            if (_slateBull != null) _slateBull.SetColor("_Color", _slateNumber[Numbers - 1].color);
            int target = _target[_side];
            _slateBar.gameObject.SetActive(!_over);
            _slateBar.localPosition = new Vector3(0f, RowY(target) + 0.1f, 0.05f);
            _slateTurn.text = _over ? "GAME OVER" : "TURN " + Mathf.Min(_turns + 1, TurnLimit);

            // Light the wedges of the number in play.
            var verts = new List<Vector3>();
            var tris = new List<int>();
            if (!_over)
            {
                if (target == Numbers - 1) Band(verts, tris, 0f, Bull1, 0f, 360f);
                else
                    for (int i = 0; i < 20; i++)
                        if (Wedges[i] == Feet(target)) Band(verts, tris, Bull1, Double1, i * 18f - 9f, i * 18f + 9f);
            }
            Mesh mesh = _lit.sharedMesh;
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        // The small board at the top: this turn's darts, and the points.
        void Board()
        {
            if (Arcade == null || Arcade.Hud == null || _points == null) return;
            Hud hud = Arcade.Hud;
            for (int s = 0; s < 2; s++)
            {
                hud.SetTotal(s, _points[s]);
                if (s != _side || _dart == 0)
                    for (int i = 0; i < 3; i++) if (s != _side || i >= _dart) hud.SetPip(s, i, Hud.Pip.Empty);
            }
            hud.SetTurn(_over ? -1 : _side, _over ? -1 : Mathf.Min(_dart, 2));
            int t = _target[_side];
            hud.SetBoardTitle(_over ? "GAME OVER" : "PUTT FROM " + (t == Numbers - 1 ? "11 FEET      THE BULL" : Feet(t) + " FEET"));
        }
    }
}
