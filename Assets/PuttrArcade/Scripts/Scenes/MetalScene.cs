// Metal as a foundry: the green runs up a steel-edged channel to the cup, a brick wall and a furnace behind. The putt
// leaves a molten trail. A make pours: a glowing ingot stamped with the putt's feet rises out of the cup, hangs there
// in the sparks, then swings over onto the Player's stack, cooling to steel. A back door make forges a dark, cracked
// ingot that goes on the scrap pile; a miss just cools to grey. Units are inches; the cup is at the origin.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class MetalScene : RuleScene
    {
        const float LaneW = 40f, BallR = 1.1f, CupR = 2.4f;
        static readonly Vector3 StackAt = new Vector3(50f, 0f, -80f), ScrapAt = new Vector3(-52f, 0f, -90f);
        static readonly Color Molten = new Color(1f, 0.45f, 0.08f);

        Transform _ball, _start, _stack, _scrap;
        LineRenderer _trail;
        TextMesh _owner;
        Mesh _ingot;
        Material _steel, _copper, _slag, _furnace;
        Light _forge;
        float _heat;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(Portrait ? new Vector3(-2f, 84f, -205f) : new Vector3(-6f, 76f, -205f));
        public override Vector3 CameraLook => At(Portrait ? new Vector3(-6f, 14f, -34f) : new Vector3(-30f, 14f, -34f));
        public override float CameraFov => Portrait ? 52f : 44f;

        static Vector3 SpotFor(int feet, int tee) => new Vector3((tee - 2) * 8f, 0f, -feet * 12f);

        public override void Build()
        {
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.11f, 0.3f, 0.17f), 0.15f, 0f, pile, pileNormal, 0.8f);
            turf.SetTextureScale("_BaseMap", new Vector2(3f, 12f));
            Material plate = Make.Lit(new Color(0.09f, 0.085f, 0.08f), 0.55f, 0.6f);
            Material iron = Make.Lit(new Color(0.3f, 0.3f, 0.32f), 0.6f, 0.85f);
            Material hole = Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f);
            _steel = Make.Lit(new Color(0.55f, 0.56f, 0.6f), 0.65f, 0.85f);
            _copper = Make.Lit(new Color(0.78f, 0.45f, 0.25f), 0.7f, 0.9f);
            _slag = Make.Lit(new Color(0.16f, 0.1f, 0.09f), 0.25f, 0.3f);
            _ingot = Ingot(15f, 9f, 10.5f, 5.4f, 5.4f);

            // The shop floor, the channel and its rivets.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1.6f, 0f), new Vector3(900f, 2f, 900f), plate);
            Shape(PrimitiveType.Cube, "Green", Root, new Vector3(0, -0.5f, -62f), new Vector3(LaneW, 1f, 168f), turf);
            for (int s = -1; s <= 1; s += 2)
            {
                Shape(PrimitiveType.Cube, "Rail", Root, new Vector3(s * (LaneW / 2f + 1.2f), 0.6f, -62f), new Vector3(2.4f, 2.6f, 168f), iron);
                for (int z = -140; z <= 16; z += 26)
                    Shape(PrimitiveType.Sphere, "Rivet", Root, new Vector3(s * (LaneW / 2f + 1.2f), 2f, z), Vector3.one * 1.2f, Make.Emissive(new Color(0.8f, 0.8f, 0.8f), new Color(0.25f, 0.25f, 0.25f), 0.8f));
            }
            Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), hole).transform.localPosition = new Vector3(0, 0.03f, 0);
            Hoop("Lip", Root, new Vector3(0, 0.05f, 0), CupR, CupR + 0.4f, Make.Emissive(Molten, Molten * 1.4f));
            Hoop("Glow", Root, new Vector3(0, 0.06f, 0), CupR + 0.6f, CupR + 3.2f, Make.Glow(new Color(1f, 0.55f, 0.15f, 0.35f)));
            for (int f = 3; f <= 11; f++)
                Words(Root, f.ToString(), new Vector3(-LaneW / 2f - 7f, 0.3f, -f * 12f), 3.2f, new Color(1f, 0.6f, 0.25f, 0.8f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // The back wall: brick, with the furnace mouth burning on the left.
            Material brick = Make.Lit(Color.white, 0.2f, 0f, Bricks());
            brick.SetTextureScale("_BaseMap", new Vector2(10f, 5f));
            Shape(PrimitiveType.Cube, "Wall", Root, new Vector3(0, 110f, 60f), new Vector3(520f, 240f, 8f), brick);
            _furnace = Make.Emissive(new Color(1f, 0.6f, 0.2f), new Color(4f, 1.6f, 0.35f));
            float fx = 52f;
            Shape(PrimitiveType.Cube, "Furnace", Root, new Vector3(fx, 20f, 55.5f), new Vector3(46f, 40f, 1f), _furnace);
            GameObject top = Shape(PrimitiveType.Cylinder, "Furnace arch", Root, new Vector3(fx, 40f, 55.5f), new Vector3(46f, 0.5f, 46f), _furnace);
            top.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            Shape(PrimitiveType.Cube, "Hearth", Root, new Vector3(fx, 3f, 50f), new Vector3(60f, 6f, 12f), Make.Lit(new Color(0.2f, 0.12f, 0.1f), 0.2f));
            _forge = World.NewLight(Root, "Forge", LightType.Point, new Color(1f, 0.5f, 0.15f), 90000f);
            _forge.transform.localPosition = new Vector3(fx, 30f, 30f);
            _forge.range = 320f;

            // The hoist over the cup: a chain and a hook.
            Shape(PrimitiveType.Cube, "Gantry", Root, new Vector3(0, 120f, 20f), new Vector3(200f, 5f, 5f), iron);
            for (int i = 0; i < 14; i++)
            {
                GameObject link = Shape(PrimitiveType.Cube, "Link", Root, new Vector3(-18f, 116f - i * 4.2f, 20f), new Vector3(i % 2 == 0 ? 1.6f : 0.6f, 4f, i % 2 == 0 ? 0.6f : 1.6f), iron);
                link.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            LineRenderer hook = Stroke(Root, "Hook", Color.white, 1.4f, false);
            hook.sharedMaterial = iron;
            var hk = new List<Vector3>();
            for (int i = 0; i <= 16; i++) { float a = Mathf.Lerp(0f, 1.25f * Mathf.PI, i / 16f); hk.Add(new Vector3(-18f + Mathf.Sin(a) * 4f, 58f - (1f - Mathf.Cos(a)) * 4f, 20f)); }
            Points(hook, hk);

            // The stack (this Player's ingots) and the scrap pile.
            _stack = new GameObject("Stack").transform; _stack.SetParent(Root, false); _stack.localPosition = StackAt;
            _scrap = new GameObject("Scrap").transform; _scrap.SetParent(Root, false); _scrap.localPosition = ScrapAt;
            Shape(PrimitiveType.Cube, "Pallet", _stack, new Vector3(0, 0.6f, 8f), new Vector3(52f, 1.2f, 30f), Make.Lit(new Color(0.2f, 0.14f, 0.09f), 0.3f));
            _owner = Words(Root, "", StackAt + new Vector3(0, 0.4f, -14f), 2.8f, new Color(1f, 0.7f, 0.35f));
            _owner.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            Words(Root, "BACK DOOR", ScrapAt + new Vector3(0, 0.4f, -10f), 2.4f, new Color(0.8f, 0.6f, 0.5f, 0.75f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            _start = new GameObject("Start").transform;
            _start.SetParent(Root, false);
            Hoop("Spot", _start, new Vector3(0, 0.05f, 0), 3f, 3.6f, Make.Emissive(Color.black, Molten * 1.5f));
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(Color.white, new Color(0.3f, 0.3f, 0.3f), 0.7f)).transform;
            _trail = Stroke(Root, "Molten", Molten, 1.8f);

            Sun(Root, new Vector3(50f, -25f, 0f), new Color(1f, 0.8f, 0.65f), 0.35f);
            Lamp(Root, new Vector3(30f, 220f, -200f), new Vector3(0, 0, -50f), new Color(1f, 0.88f, 0.75f), 380000f, 55f, 900f);
            Lamp(Root, new Vector3(120f, 140f, -160f), StackAt, new Color(1f, 0.9f, 0.8f), 70000f, 30f, 600f);
        }

        // A tapered bar: bottom w x d, top tw x td, h high, flat shaded. The front face is -z.
        static Mesh Ingot(float w, float d, float tw, float td, float h)
        {
            Vector3[] c =
            {
                new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0, -d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(-w / 2, 0, d / 2),
                new Vector3(-tw / 2, h, -td / 2), new Vector3(tw / 2, h, -td / 2), new Vector3(tw / 2, h, td / 2), new Vector3(-tw / 2, h, td / 2),
            };
            int[][] faces = { new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 3, 2, 1, 0 } };
            var verts = new List<Vector3>(); var tris = new List<int>();
            foreach (int[] f in faces)
            {
                int b = verts.Count;
                foreach (int i in f) verts.Add(c[i]);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            var m = new Mesh { name = "Ingot" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Texture2D Bricks()
        {
            var t = new Texture2D(128, 64) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var dice = new System.Random(5);
            var px = new Color[128 * 64];
            for (int row = 0; row < 4; row++)
                for (int b = -1; b < 4; b++)
                {
                    float shade = 0.75f + (float)dice.NextDouble() * 0.3f;
                    Color brick = new Color(0.36f * shade, 0.15f * shade, 0.09f * shade);
                    int x0 = b * 32 + (row % 2) * 16;
                    for (int y = row * 16; y < row * 16 + 16; y++)
                        for (int x = x0; x < x0 + 32; x++)
                        {
                            if (x < 0 || x >= 128) continue;
                            bool mortar = y % 16 < 2 || (x - x0) < 2;
                            px[y * 128 + x] = mortar ? new Color(0.08f, 0.07f, 0.065f) : brick * (0.92f + 0.08f * (float)dice.NextDouble());
                        }
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        Transform Bar(Transform parent, int feet, Material m, Color stamp)
        {
            Transform t = Make.MeshObject("Ingot", parent, _ingot, m).transform;
            TextMesh n = Words(t, feet.ToString(), new Vector3(0, 2.7f, -3.65f), 3.9f, stamp);
            n.transform.localRotation = Quaternion.Euler(19f, 0, 0);
            return t;
        }

        // Where the n-th ingot sits: pyramids of six (3, 2, 1), each new pyramid behind the last.
        static Vector3 StackSlot(int n)
        {
            int g = n / 6, p = n % 6;
            int row = p < 3 ? 0 : p < 5 ? 1 : 2;
            int k = row == 0 ? p : row == 1 ? p - 3 : 0;
            float x = row == 0 ? (k - 1) * 16f : row == 1 ? (k - 0.5f) * 16f : 0f;
            return new Vector3(x, 1.2f + row * 5.5f, g * 13f);
        }

        void Show(ShotView v, int who, int hide, int hideDoors = 0)
        {
            for (int i = _stack.childCount - 1; i >= 0; i--) if (_stack.GetChild(i).name == "Ingot") Make.Kill(_stack.GetChild(i).gameObject);
            for (int i = _scrap.childCount - 1; i >= 0; i--) Make.Kill(_scrap.GetChild(i).gameObject);
            int[] made = v.Get<int[]>("made" + who) ?? new int[0];
            int shown = Mathf.Max(0, made.Length - hide);
            for (int i = 0; i < shown && i < 24; i++)
            {
                Transform t = Bar(_stack, made[i], i == shown - 1 ? _copper : _steel, new Color(0.12f, 0.1f, 0.1f));
                t.localPosition = StackSlot(i);
                t.localRotation = Quaternion.Euler(0, (i * 37 % 7) - 3f, 0);
            }
            int doors = v.Int("doors" + who) - hideDoors;
            for (int i = 0; i < doors && i < 8; i++)
            {
                Transform t = Bar(_scrap, 0, _slag, Color.clear);
                t.localPosition = new Vector3((i % 3 - 1) * 16f, 1f + (i / 3) * 5.4f, (i * 13 % 5) * 1.5f);
                t.localRotation = Quaternion.Euler(0, (i * 53 % 30) - 15f, (i % 2 == 0 ? 6f : -4f));
            }
            string name = who < v.Names.Length ? v.Names[who] : "";
            _owner.text = v.Names.Length > 1 ? name.ToUpperInvariant() : "";
        }

        void Aim(ShotView v)
        {
            Vector3 from = SpotFor(v.NextFeet, v.NextTee);
            _start.localPosition = from;
            _ball.localPosition = from + Vector3.up * BallR;
            _ball.gameObject.SetActive(!v.Over);
        }

        public override void Begin(ShotView start) { Show(start, start.Next, 0); Aim(start); }
        public override void Ready(ShotView view) { Show(view, view.Over ? view.Player : view.Next, 0); Aim(view); }

        public override void Tick()
        {
            _heat = Mathf.MoveTowards(_heat, 0f, Time.deltaTime * 0.7f);
            float flicker = 1f + Mathf.PerlinNoise(Time.time * 3f, 0.3f) * 0.35f;
            if (_forge != null) _forge.intensity = 90000f * flicker * (1f + _heat);
            if (_furnace != null) _furnace.SetColor("_EmissionColor", new Color(4f, 1.6f, 0.35f) * (0.85f + 0.3f * flicker));
        }

        public override IEnumerator Play(ShotView v)
        {
            _trail.positionCount = 0;
            Show(v, v.Player, v.Good ? 1 : 0, v.BackDoor ? 1 : 0);
            int feet = v.Feet > 0 ? v.Feet : 6, tee = v.Tee > 0 ? v.Tee : 2;
            SetTrail(Molten, 1f);
            yield return Along(_ball, Roll(v, SpotFor(feet, tee), BallR), Mathf.Lerp(0.9f, 1.5f, Mathf.InverseLerp(3f, 11f, feet)), _trail);

            if (!v.Made)
            {
                // Nothing poured: the trail cools.
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f) { SetTrail(Color.Lerp(Molten, new Color(0.35f, 0.35f, 0.38f), t), 1f - t * 0.5f); yield return null; }
                yield return new WaitForSeconds(0.3f);
                yield break;
            }
            Vector3 top = _ball.localPosition;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = Vector3.Lerp(top, new Vector3(0, -BallR * 2f, 0), t); yield return null; }
            _ball.gameObject.SetActive(false);

            // The pour: the ingot rises out of the cup, glowing.
            bool door = v.BackDoor || !v.Good;
            Material hot = door ? Make.Emissive(new Color(0.4f, 0.2f, 0.15f), new Color(0.8f, 0.2f, 0.05f), 0.3f) : Make.Emissive(Molten, new Color(4f, 1.5f, 0.25f), 0.6f);
            Transform bar = Bar(Root, door ? 0 : feet, hot, new Color(0.25f, 0.06f, 0.02f));
            if (door) bar.GetComponentInChildren<TextMesh>().text = "";
            _heat = 1f;
            Sound("fire_whoosh", 0.35f, door ? 0.7f : 1.1f);
            Vector3 hover = new Vector3(0, 16f, 0);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
            {
                float e = Ease(t);
                bar.localPosition = Vector3.Lerp(new Vector3(0, -4f, 0), hover, e);
                bar.localScale = Vector3.one * Mathf.Lerp(0.3f, door ? 1f : 1.6f, e);
                bar.localRotation = Quaternion.Euler(0, (1f - e) * 180f, 0);
                yield return null;
            }
            if (!door) { Arcade.Sparks(At(0, 14f, 0), 30); Sound("sizzle", 0.4f); }
            yield return new WaitForSeconds(door ? 0.3f : 0.55f);

            // Over onto the stack (or the scrap pile), cooling as it goes.
            Transform pile = door ? _scrap : _stack;
            int n = door ? v.Int("doors" + v.Player) - 1 : (v.Get<int[]>("made" + v.Player) ?? new int[1]).Length - 1;
            Vector3 land = door ? ScrapAt + new Vector3((n % 3 - 1) * 16f, 1f + (n / 3) * 5.4f, 0) : StackAt + StackSlot(Mathf.Min(n, 23));
            Vector3 start = bar.localPosition;
            Color glow = door ? new Color(0.8f, 0.2f, 0.05f) : new Color(4f, 1.5f, 0.25f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.75f)
            {
                float e = Ease(t);
                bar.localPosition = Vector3.Lerp(start, land, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 22f;
                bar.localScale = Vector3.one * Mathf.Lerp(door ? 1f : 1.6f, 1f, e);
                hot.SetColor("_EmissionColor", glow * Mathf.Lerp(1f, 0.25f, e));
                yield return null;
            }
            bar.localPosition = land;
            Sound("anvil", door ? 0.5f : 0.9f, door ? 0.8f : 1f);
            Arcade.Shake(0.15f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f) { hot.SetColor("_EmissionColor", glow * Mathf.Lerp(0.25f, 0.05f, t)); yield return null; }
            Make.Kill(bar.gameObject);
            Show(v, v.Player, 0);
            yield return new WaitForSeconds(0.25f);
        }

        void SetTrail(Color c, float alpha)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c * 0.6f, 0f), new GradientColorKey(c, 0.8f), new GradientColorKey(new Color(1f, 0.9f, 0.6f), 1f) },
                      new[] { new GradientAlphaKey(alpha * 0.25f, 0f), new GradientAlphaKey(alpha, 1f) });
            _trail.colorGradient = g;
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
