// The golf ball. It has two lives:
//
//   1. Replay: while the putt is on the real ramp, the ball is moved through
//      the positions the console's camera saw, one camera frame at a time.
//   2. Physics: at the front of the hole it becomes a rigidbody, launched with
//      the speed and heading measured from those frames.
using System;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class Ball : MonoBehaviour
    {
        public Rigidbody Body { get; private set; }
        public TrailRenderer Trail { get; private set; }
        public Action<Collision> Hit;
        /// <summary>True from Launch until the ball is put away.</summary>
        public bool Live { get; private set; }
        /// <summary>How hard the surface slows a rolling ball, in/s^2. Games set this for their own surface.</summary>
        public float Resistance = LaneSpec.RollingResistance;

        Material _skin;
        static Texture2D _color, _dimples;
        Light _charge;
        bool _grounded;
        Vector3 _lastReplay;
        bool _hasLastReplay;

        public static Ball Create(Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ball";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * LaneSpec.BallRadius * 2f;
            var ball = go.AddComponent<Ball>();
            if (_color == null) Make.Ball(out _color, out _dimples);
            Texture2D color = _color, dimples = _dimples;
            ball._skin = Make.Lit(Color.white, 0.8f, 0f, color, dimples, 0.45f);
            go.GetComponent<MeshRenderer>().sharedMaterial = ball._skin;
            go.GetComponent<SphereCollider>().sharedMaterial = Make.Physics(0.45f, 0.35f);

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.maxAngularVelocity = 400f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.02f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.isKinematic = true;
            ball.Body = rb;

            TrailRenderer trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.minVertexDistance = 0.1f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, 0.9f), new Keyframe(1, 0f));
            trail.sharedMaterial = Make.Glow(Color.white, true, Make.SoftDot());
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.numCapVertices = 4;
            ball.Trail = trail;

            // A holed ball carries a green light with it.
            ball._charge = new GameObject("Charge").AddComponent<Light>();
            ball._charge.transform.SetParent(go.transform, false);
            ball._charge.type = LightType.Point;
            ball._charge.color = Make.Bright;
            ball._charge.range = 6f;
            ball._charge.intensity = 7f;
            ball._charge.shadows = LightShadows.None;
            ball.SetCharged(false);

            go.SetActive(false);
            return ball;
        }

        /// <summary>A holed putt comes through the cup charged: it glows and hits harder.</summary>
        public void SetCharged(bool charged)
        {
            // The trail is the brand's colour: Puttr's green when charged and a cool white otherwise, or the host's colour.
            Color c = charged || Brand.Active ? Brand.Primary : new Color(0.8f, 0.88f, 1f);
            _charge.color = Brand.Primary;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(charged ? 0.6f : Brand.Active ? 0.32f : 0.16f, 0f), new GradientAlphaKey(0f, 1f) });
            Trail.colorGradient = gradient;
            _charge.enabled = charged;
            Body.mass = charged ? 1.5f : 1f;
        }

        /// <summary>Colour the ball, to tell Players apart.</summary>
        public void SetTint(Color color) => _skin.SetColor("_BaseColor", color);

        /// <summary>Put the ball at a replay position (its center), rolling it by the distance moved.</summary>
        public void ReplayAt(Vector3 center)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                Trail.Clear();
                _hasLastReplay = false;
            }
            Body.isKinematic = true;
            Live = false;
            if (_hasLastReplay)
            {
                Vector3 delta = center - _lastReplay;
                if (delta.sqrMagnitude > 1e-8f)
                {
                    Vector3 axis = Vector3.Cross(Vector3.up, delta.normalized);
                    transform.rotation = Quaternion.AngleAxis(delta.magnitude / LaneSpec.BallRadius * Mathf.Rad2Deg, axis) * transform.rotation;
                }
            }
            transform.position = center;
            _lastReplay = center;
            _hasLastReplay = true;
        }

        /// <summary>puttr-app-2: put the ball exactly here, turned exactly so (a simulated run being played back).</summary>
        public void Pose(Vector3 center, Quaternion rotation)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                Trail.Clear();
            }
            Body.isKinematic = true;
            Live = false;
            transform.SetPositionAndRotation(center, rotation);
            _lastReplay = center;
            _hasLastReplay = true;
        }

        /// <summary>Hand the ball to the physics engine, already rolling.</summary>
        public void Launch(Vector3 center, Vector3 velocity)
        {
            gameObject.SetActive(true);
            transform.position = center;
            Body.isKinematic = false;
            Body.position = center;
            Body.linearVelocity = velocity;
            // Rolling without slipping: the contact point is at rest.
            Body.angularVelocity = Vector3.Cross(Vector3.up, velocity) / LaneSpec.BallRadius;
            Body.WakeUp();
            Live = true;
            _hasLastReplay = false;
        }

        public void PutAway()
        {
            Live = false;
            _hasLastReplay = false;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            Body.isKinematic = true;
            gameObject.SetActive(false);
        }

        void FixedUpdate()
        {
            if (Body.isKinematic) return;
            // Rolling resistance. The engine has none, so a ball would roll
            // forever; a real one loses about 6.6 in/s^2 on this surface.
            if (_grounded)
            {
                Vector3 v = Body.linearVelocity;
                var flat = new Vector3(v.x, 0, v.z);
                float speed = flat.magnitude;
                if (speed > 0.001f)
                {
                    float drop = Mathf.Min(speed, Resistance * Time.fixedDeltaTime);
                    Body.linearVelocity = v - flat / speed * drop;
                }
            }
            _grounded = false;
        }

        void OnCollisionStay(Collision collision)
        {
            if (collision.rigidbody == null) _grounded = true;
        }

        void OnCollisionEnter(Collision collision) => Hit?.Invoke(collision);
    }
}
