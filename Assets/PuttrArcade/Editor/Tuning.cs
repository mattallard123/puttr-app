// Puttr > Print Pin Table: rolls a grid of putts through the physics with no
// rendering and prints how many pins each knocks down. Use it after changing
// anything in LaneSpec or the pin and ball materials.
//
//   Unity -batchmode -quit -projectPath . -executeMethod PuttrArcade.EditorTools.Tuning.Run
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PuttrArcade.EditorTools
{
    public static class Tuning
    {
        static readonly float[] Lines = { -4.5f, -3.3f, -2.5f, -1.8f, -1.2f, -0.6f, -0.3f, 0f, 0.3f, 0.6f, 1.2f, 1.8f, 2.5f, 3.3f, 4.5f, 6.5f };
        static readonly float[] Speeds = { 12f, 20f, 28f, 34f, 40f, 48f, 58f };

        [MenuItem("Puttr/Print Pin Table")]
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            World.ApplyPhysicsSettings();
            // Step the physics by hand. Put this back before leaving: the
            // editor saves it as a project setting.
            Physics.simulationMode = SimulationMode.Script;

            var root = new GameObject("Tuning").transform;
            var world = new World();
            world.Build(root);
            LanesMode.BuildLane(world.Hall);
            var pins = new PinDeck();
            pins.Build(world.Hall);
            Ball ball = Ball.Create(world.Root);

            var sb = new StringBuilder();
            foreach (bool charged in new[] { false, true })
            {
                sb.AppendLine(charged ? "Holed putt (out of the cup, ball mass 1.5). Rows: speed at the hole, in/s. Columns: line, inches right of center." : "Missed putt (over the end of the ramp, ball mass 1.0). A line inside the lip is moved just outside it.");
                sb.Append("  in/s |");
                foreach (float line in Lines) sb.Append(line.ToString("0.0").PadLeft(5));
                sb.AppendLine();
                foreach (float speed in Speeds)
                {
                    sb.Append(speed.ToString("0").PadLeft(6) + " |");
                    foreach (float line in Lines) sb.Append(Roll(pins, ball, line, speed, charged).ToString().PadLeft(5));
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
            Debug.Log("PIN TABLE\n" + sb);

            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        public static int Roll(PinDeck pins, Ball ball, float line, float speed, bool charged)
        {
            float dt = Time.fixedDeltaTime;
            pins.SetKinematic(false);
            pins.Rack();
            for (int i = 0; i < 40; i++) Physics.Simulate(dt);

            ball.SetCharged(charged);
            if (charged)
            {
                // Holed: out of the cup at the head of the lane, a little slower for the last inch of ramp.
                float z = LaneSpec.LaneHeadZ + 1f;
                float outSpeed = Mathf.Max(LaneSpec.HoledMinSpeed, Mathf.Sqrt(Mathf.Max(0f, speed * speed - 2f * 64f * 1.3f)));
                ball.Launch(LaneSpec.OnSurface(new Vector2(line, -z), LaneSpec.BallRadius), LaneSpec.SurfaceDirection(0f, z) * outSpeed);
            }
            else
            {
                // Missed: on up the ramp from where the camera lost it, passing outside the lip.
                float z = -2.9f;
                float x = Mathf.Abs(line) < LaneSpec.HoleRadius + 0.25f ? Mathf.Sign(line + 0.001f) * (LaneSpec.HoleRadius + 0.25f) : line;
                ball.Launch(LaneSpec.OnSurface(new Vector2(x, -z), LaneSpec.BallRadius), LaneSpec.SurfaceDirection(0f, z) * speed);
            }

            Rigidbody rb = ball.Body;
            for (int i = 0; i < 240 * 8; i++)
            {
                // FixedUpdate does not run in the editor, so apply the ball's rolling resistance here.
                Vector3 v = rb.linearVelocity;
                var flat = new Vector3(v.x, 0, v.z);
                float s = flat.magnitude;
                if (s > 0.001f && rb.position.z < LaneSpec.LaneEndZ && Mathf.Abs(rb.position.y - LaneSpec.BallRadius - LaneSpec.SurfaceY(rb.position.z)) < 0.25f)
                    rb.linearVelocity = v - flat / s * Mathf.Min(s, LaneSpec.RollingResistance * dt);
                Physics.Simulate(dt);
            }
            int down = 10 - pins.StandingCount();
            ball.PutAway();
            return down;
        }
    }
}
