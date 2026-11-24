// High Striker: just the strength tester, on a carnival midway at night. The putt is the swing of the hammer: the
// closer it stopped to the front of the cup without reaching it, the higher the puck goes up the tower (the score,
// 0 to 100). Over 80 rings the bell. A putt that reached the hole sends the puck through the top: too hard, 0.
// Units are inches; the tower stands at the origin.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class HighStrikerScene : RuleScene
    {
        const float Base = 30f, Top = 330f;   // the scale runs from Base (0) to Top (100)
        Transform _puck, _bell, _clapper;
        readonly List<Renderer> _bulbs = new List<Renderer>();
        Material _bulbOn, _bulbOff, _bulbRed;
        TextMesh _score, _history;
        float _flash;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(Portrait ? 0f : 60f, 215f, Portrait ? -470f : -380f);
        public override Vector3 CameraLook => At(0f, 215f, 0f);
        public override float CameraFov => Portrait ? 60f : 48f;

        static float Height(float score) => Mathf.Lerp(Base, Top, Mathf.Clamp01(score / 100f));

        public override void Build()
        {
            Material ground = Make.Lit(new Color(0.18f, 0.12f, 0.08f), 0.3f);
            Material red = Make.Lit(new Color(0.75f, 0.1f, 0.12f), 0.5f);
            Material cream = Make.Lit(new Color(0.95f, 0.9f, 0.78f), 0.5f);
            Material gold = Make.Lit(new Color(1f, 0.78f, 0.25f), 0.9f, 0.9f);
            Material wood = Make.Lit(new Color(0.42f, 0.26f, 0.14f), 0.4f);
            Material steel = Make.Lit(new Color(0.6f, 0.62f, 0.66f), 0.8f, 0.9f);
            _bulbOn = Make.Emissive(new Color(1f, 0.85f, 0.4f), new Color(3f, 2.2f, 0.8f));
            _bulbOff = Make.Lit(new Color(0.3f, 0.26f, 0.2f), 0.6f);
            _bulbRed = Make.Emissive(new Color(1f, 0.3f, 0.3f), new Color(3f, 0.4f, 0.4f));

            Shape(PrimitiveType.Cube, "Boardwalk", Root, new Vector3(0, -2f, -60f), new Vector3(900f, 4f, 500f), ground);
            // The tent behind: red and cream stripes.
            for (int i = -8; i <= 8; i++)
                Shape(PrimitiveType.Cube, "Stripe", Root, new Vector3(i * 34f, 210f, 120f), new Vector3(34f, 420f, 2f), i % 2 == 0 ? red : cream);
            Shape(PrimitiveType.Cube, "Valance", Root, new Vector3(0, 400f, 110f), new Vector3(600f, 30f, 6f), red);
            for (int i = -14; i <= 14; i++)
            {
                GameObject b = Shape(PrimitiveType.Sphere, "Marquee", Root, new Vector3(i * 20f, 382f, 106f), Vector3.one * 6f, _bulbOn);
                b.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // The tower: a base with the strike pad, the board, the track, the bell.
            Shape(PrimitiveType.Cube, "Plinth", Root, new Vector3(0, 10f, 0), new Vector3(90f, 20f, 70f), wood);
            Shape(PrimitiveType.Cylinder, "Pad", Root, new Vector3(0, 22f, -12f), new Vector3(34f, 2f, 34f), red);
            Words(Root, "PUTTR", new Vector3(0, 23.5f, -12f), 5f, Color.white).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            Shape(PrimitiveType.Cube, "Board", Root, new Vector3(0, (Base + Top) / 2f + 10f, 6f), new Vector3(46f, Top - Base + 60f, 4f), red);
            Shape(PrimitiveType.Cube, "Track", Root, new Vector3(0, (Base + Top) / 2f, 1f), new Vector3(5f, Top - Base + 20f, 3f), steel);
            for (int s = 10; s <= 100; s += 10)
            {
                float y = Height(s);
                Shape(PrimitiveType.Cube, "Tick", Root, new Vector3(-12f, y, 3.6f), new Vector3(10f, 1.2f, 0.5f), cream);
                Words(Root, s.ToString(), new Vector3(-22f, y, 3.4f), 4.5f, Color.white, TextAnchor.MiddleRight);
                GameObject bulb = Shape(PrimitiveType.Sphere, "Bulb", Root, new Vector3(14f, y, 2.5f), Vector3.one * 5f, _bulbOff);
                _bulbs.Add(bulb.GetComponent<Renderer>());
            }
            Words(Root, "TEST YOUR", new Vector3(0, Top + 70f, 3f), 7f, new Color(1f, 0.85f, 0.4f));
            Words(Root, "TOUCH", new Vector3(0, Top + 55f, 3f), 10f, Color.white);
            _bell = Shape(PrimitiveType.Sphere, "Bell", Root, new Vector3(0, Top + 22f, 0), new Vector3(26f, 22f, 26f), gold).transform;
            _clapper = Shape(PrimitiveType.Cube, "Bar", Root, new Vector3(0, Top + 8f, 0), new Vector3(30f, 2f, 4f), steel).transform;
            _puck = Shape(PrimitiveType.Cylinder, "Puck", Root, new Vector3(0, Base, -3f), new Vector3(12f, 3f, 12f), steel).transform;
            _puck.localRotation = Quaternion.Euler(90f, 0, 0);

            _score = Words(Root, "", new Vector3(0, 70f, -40f), 16f, Color.white);
            _history = Words(Root, "", new Vector3(0, -30f, -70f), 6f, new Color(1f, 1f, 1f, 0.8f));
            _history.transform.localRotation = Quaternion.Euler(60f, 0, 0);

            Sun(Root, new Vector3(35f, -20f, 0f), new Color(1f, 0.85f, 0.7f), 0.4f);
            Lamp(Root, new Vector3(-120f, 260f, -220f), new Vector3(0, 180f, 0), new Color(1f, 0.9f, 0.75f), 600000f, 45f, 700f);
            Lamp(Root, new Vector3(140f, 300f, -160f), new Vector3(0, 160f, 0), new Color(1f, 0.6f, 0.4f), 300000f, 40f, 700f);
        }

        void Lights(float score, bool red)
        {
            for (int i = 0; i < _bulbs.Count; i++) _bulbs[i].sharedMaterial = (i + 1) * 10 <= score + 0.01f ? (red ? _bulbRed : _bulbOn) : _bulbOff;
        }

        void History(ShotView v)
        {
            float[] mine = v.Get<float[]>("mine") ?? new float[0];
            string line = "";
            foreach (float s in mine) line += Mathf.RoundToInt(s) + "   ";
            _history.text = line.Trim();
        }

        public override void Begin(ShotView start) { _score.text = ""; Lights(0, false); History(start); }
        public override void Ready(ShotView view) { History(view); }

        public override IEnumerator Play(ShotView v)
        {
            float score = (float)v.Points;
            bool tooHard = v.Reached && score <= 0f && v.Title == "TOO FAR";
            Lights(0, false);
            _score.text = "";
            Sound("hammer_hit", 1f);                       // the hammer on the pad
            Sound("highstriker_puck", 0.6f, tooHard ? 1.15f : 1f);   // puttr-app-2: up the rail (silent until recorded)
            float target = tooHard ? Top + 60f : Height(score);
            float rise = tooHard ? 0.45f : Mathf.Lerp(0.35f, 0.9f, score / 100f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / rise)
            {
                float e = 1f - (1f - t) * (1f - t);
                _puck.localPosition = new Vector3(0, Mathf.Lerp(Base, target, e), -3f);
                Lights(Mathf.InverseLerp(Base, Top, _puck.localPosition.y) * 100f, tooHard);
                yield return null;
            }
            if (tooHard)
            {
                Sound("highstriker_dud", "rule_miss");   // puttr-app-2: the game's own sound, the shared one until recorded
                _score.text = "TOO HARD";
                _score.color = new Color(1f, 0.4f, 0.4f);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f) { _puck.localPosition = new Vector3(0, Mathf.Lerp(target, Base, t), -3f); yield return null; }
                Lights(0, false);
            }
            else
            {
                if (score > 80f)
                {
                    Sound("bell_ding", 1f);
                    _flash = 1f;
                    Arcade.Sparks(At(0, Top + 22f, 0), 24);
                }
                _score.text = Mathf.RoundToInt(score).ToString();
                _score.color = score > 80f ? new Color(1f, 0.85f, 0.3f) : score >= 50f ? new Color(0.5f, 0.85f, 0.3f) : Color.white;
                yield return new WaitForSeconds(0.6f);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f) { _puck.localPosition = new Vector3(0, Mathf.Lerp(target, Base, Ease(t)), -3f); yield return null; }
            }
        }

        public override void Tick()
        {
            if (_flash <= 0f) return;
            _flash -= Time.deltaTime;
            _bell.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 40f) * 8f * _flash);
        }
    }
}
