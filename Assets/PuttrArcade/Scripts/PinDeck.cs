// Ten pins: building them, racking them, and counting what is left standing.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    /// <summary>One pin. Reports hard knocks so the game can make noise.</summary>
    public sealed class Pin : MonoBehaviour
    {
        public int Number;
        public Rigidbody Body;
        public Action<Pin, Collision> Hit;

        void OnCollisionEnter(Collision collision) => Hit?.Invoke(this, collision);

        public bool Standing
        {
            get
            {
                if (!gameObject.activeSelf) return false;
                Vector3 p = transform.position;
                if (p.y < LaneSpec.DeckY - 0.3f || p.z > LaneSpec.LaneEndZ || Mathf.Abs(p.x) > LaneSpec.LaneHalfWidth) return false;
                return Vector3.Dot(transform.up, Vector3.up) > 0.92f;
            }
        }
    }

    public sealed class PinDeck
    {
        public readonly List<Pin> Pins = new List<Pin>();
        /// <summary>What a pin looks like, for anything that needs to draw one (reflections).</summary>
        public Mesh PinMesh { get; private set; }
        public Material PinPaint { get; private set; }
        /// <summary>A pin was struck: the pin, and how hard (relative speed, in/s).</summary>
        public event Action<Pin, float> Knock;

        // A regulation pin: (height, diameter) in inches, base to crown.
        static readonly float[,] Profile =
        {
            { 0f, 2.03f }, { 0.75f, 2.83f }, { 2.25f, 3.91f }, { 3.375f, 4.51f }, { 4.5f, 4.766f },
            { 5.875f, 4.56f }, { 7.25f, 3.70f }, { 8.625f, 2.47f }, { 9.375f, 1.97f }, { 10f, 1.80f },
            { 10.875f, 1.87f }, { 11.75f, 2.09f }, { 12.625f, 2.42f }, { 13.5f, 2.55f }, { 14.2f, 2.30f },
            { 14.7f, 1.60f }, { 15f, 0.02f },
        };

        public void Build(Transform parent)
        {
            float s = LaneSpec.PinScale;
            var full = new List<Vector2> { new Vector2(0.001f, 0f) };
            var body = new List<Vector2> { new Vector2(0.001f, 0f) };
            for (int i = 0; i < Profile.GetLength(0); i++)
            {
                var p = new Vector2(Profile[i, 1] * 0.5f * s, Profile[i, 0] * s);
                full.Add(p);
                if (Profile[i, 0] <= 8.7f) body.Add(p);
            }
            body.Add(new Vector2(0.001f, body[body.Count - 1].y));

            Mesh visual = Make.Lathe(full, 28);
            // The collider is three convex pieces: a low-poly belly with a flat
            // base (so the pin stands), a neck and a head.
            Mesh belly = Make.Lathe(body, 10);
            Material paint = Make.Lit(Color.white, 0.86f, 0f, Make.Pin());
            PinMesh = visual;
            PinPaint = paint;
            PhysicsMaterial physics = Make.Physics(LaneSpec.PinFriction, LaneSpec.PinBounce);

            for (int n = 1; n <= 10; n++)
            {
                var go = new GameObject("Pin " + n);
                go.transform.SetParent(parent, false);

                GameObject skin = Make.MeshObject("Skin", go.transform, visual, paint);
                skin.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                var hull = go.AddComponent<MeshCollider>();
                hull.sharedMesh = belly;
                hull.convex = true;
                hull.sharedMaterial = physics;

                var neck = go.AddComponent<CapsuleCollider>();
                neck.direction = 1;
                neck.radius = 0.95f * s;
                neck.height = 5.2f * s;
                neck.center = new Vector3(0, 10.6f * s, 0);
                neck.sharedMaterial = physics;

                var head = go.AddComponent<SphereCollider>();
                head.radius = 1.27f * s;
                head.center = new Vector3(0, 13.55f * s, 0);
                head.sharedMaterial = physics;

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = LaneSpec.PinMass;
                rb.centerOfMass = new Vector3(0, 5.8f * s, 0); // a real pin balances 5.8 in up
                rb.maxAngularVelocity = 120f;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.25f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                var pin = go.AddComponent<Pin>();
                pin.Number = n;
                pin.Body = rb;
                pin.Hit = OnPinHit;
                Pins.Add(pin);
            }
            Rack();
        }

        void OnPinHit(Pin pin, Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed > 4f) Knock?.Invoke(pin, speed);
        }

        /// <summary>Stand all ten back on their spots.</summary>
        public void Rack(float lift = 0f)
        {
            foreach (Pin pin in Pins) Spot(pin, LaneSpec.PinSpot(pin.Number) + Vector3.up * lift);
        }

        static void Spot(Pin pin, Vector3 position)
        {
            pin.gameObject.SetActive(true);
            pin.transform.localScale = Vector3.one;
            Rigidbody rb = pin.Body;
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.position = position;
            rb.rotation = Quaternion.identity;
            pin.transform.SetPositionAndRotation(position, Quaternion.identity);
            rb.Sleep();
        }

        public void SetKinematic(bool kinematic)
        {
            foreach (Pin pin in Pins) pin.Body.isKinematic = kinematic;
        }

        public int StandingCount()
        {
            int count = 0;
            foreach (Pin pin in Pins) if (pin.Standing) count++;
            return count;
        }

        public List<int> StandingNumbers()
        {
            var numbers = new List<int>();
            foreach (Pin pin in Pins) if (pin.Standing) numbers.Add(pin.Number);
            return numbers;
        }

        /// <summary>True once nothing on the deck is moving.</summary>
        public bool Settled()
        {
            foreach (Pin pin in Pins)
            {
                if (!pin.gameObject.activeSelf) continue;
                Vector3 p = pin.transform.position;
                if (p.y < LaneSpec.DeckY - 1f) continue; // in the pit or a gutter: out of play
                if (pin.Body.linearVelocity.sqrMagnitude > 0.6f || pin.Body.angularVelocity.sqrMagnitude > 0.6f) return false;
            }
            return true;
        }

        /// <summary>Take away everything that fell, and square up what is left.</summary>
        public void ClearFallen()
        {
            foreach (Pin pin in Pins)
            {
                if (!pin.gameObject.activeSelf) continue;
                if (!pin.Standing) { pin.gameObject.SetActive(false); continue; }
                // A standing pin stays where it slid to, as it would on a real deck.
                Vector3 p = pin.transform.position;
                Spot(pin, new Vector3(p.x, LaneSpec.DeckY, p.z));
            }
        }
    }
}
