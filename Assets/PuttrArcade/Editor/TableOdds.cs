// Puttr > Print Pin Table Odds: rolls a spread of holed putts down the Under
// the Cup table with no rendering and prints the pocket each one finds.
//
//   Unity -batchmode -quit -projectPath . -executeMethod PuttrArcade.EditorTools.TableOdds.Run
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PuttrArcade.EditorTools
{
    public static class TableOdds
    {
        static readonly float[] Speeds = { 22f, 30f, 38f, 46f };

        [MenuItem("Puttr/Print Pin Table Odds")]
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            World.ApplyPhysicsSettings();
            Physics.simulationMode = SimulationMode.Script;
            float[][][] variants = { UnderTheCupMode.Layout };
            var sb = new StringBuilder();
            for (int v = 0; v < variants.Length; v++)
            {
                UnderTheCupMode.Layout = variants[v];
                var root = new GameObject("Odds " + v).transform;
                var mode = new UnderTheCupMode { Root = root };
                mode.Build();
                Ball ball = Ball.Create(root);
                sb.AppendLine("Layout " + v + ". Rows: line at the hole, inches right of center. Columns: speed out of the cup " + string.Join(", ", Speeds) + " in/s, straight, then the same drifting outward.");
                int clean = 0, wide = 0, stuck = 0;
                for (float line = -2f; line <= 2.001f; line += 0.125f)
                {
                    sb.Append(line.ToString("+0.000;-0.000").PadLeft(8) + " |");
                    foreach (float heading in new[] { 0f, 1f })
                    {
                        foreach (float speed in Speeds)
                        {
                            int value = Roll(mode, ball, line, speed, heading * line * 0.8f);
                            sb.Append(value.ToString().PadLeft(4));
                            if (value == 0) stuck++;
                            if (value == 100) { if (Mathf.Abs(line) <= 0.3f) clean++; else if (Mathf.Abs(line) >= 0.6f) wide++; }
                        }
                        sb.Append("  ");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("Middle pocket from within 0.3 in of center: " + clean + " of 40.   Middle pocket from 0.6 in or more off line: " + wide + " of 192 (these touched a pin, so they score 60).   Balls that found no pocket: " + stuck + ".");
                sb.AppendLine();
                Object.DestroyImmediate(root.gameObject);
            }
            Debug.Log("TABLE ODDS\n" + sb + "END ODDS");
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        static int Roll(UnderTheCupMode mode, Ball ball, float line, float speed, float headingDeg)
        {
            float dt = Time.fixedDeltaTime;
            float h = headingDeg * Mathf.Deg2Rad;
            float z = LaneSpec.LaneHeadZ + 1f;
            float x = Mathf.Clamp(line + Mathf.Tan(h) * z, -5f, 5f);
            ball.Launch(new Vector3(x, LaneSpec.LaneHeadY + LaneSpec.BallRadius + 0.02f, z), new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h)) * speed);
            Rigidbody rb = ball.Body;
            int still = 0;
            for (int i = 0; i < 240 * 9; i++)
            {
                // FixedUpdate does not run in the editor, so apply rolling resistance here.
                Vector3 v = rb.linearVelocity;
                var flat = new Vector3(v.x, 0, v.z);
                float s = flat.magnitude;
                if (s > 0.001f) rb.linearVelocity = v - flat / s * Mathf.Min(s, LaneSpec.RollingResistance * dt);
                Physics.Simulate(dt);
                Vector3 local = mode.Table.InverseTransformPoint(rb.position);
                still = local.z > 20f && rb.linearVelocity.magnitude < 1.2f ? still + 1 : 0;
                if (still > 80 || local.y < -3f) break;
            }
            int value = UnderTheCupMode.PocketValue(mode.Table.InverseTransformPoint(rb.position));
            ball.PutAway();
            return value;
        }
    }
}
