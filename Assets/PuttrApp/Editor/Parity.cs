// The Unity side of the parity check (Tools/parity/run.sh).
//
//   Parity.MakePutts: writes Tools/parity/putts.json, a test set of putts, unless one is there
//     already (a real export from the app replaces it): the SDK's eight recorded console putts,
//     a sweep of made-up putts over line, pace, heading and hole size, and edge cases.
//   Parity.Judge: reads putts.json and writes unity.json, what AppPutt (the port of the app's
//     maths) makes of each putt, beside what the putt SDK says, so both can be compared.
using System.Collections.Generic;
using System.IO;
using Puttr;
using UnityEngine;

namespace PuttrApp.EditorTools
{
    public static class Parity
    {
        const string Folder = "Tools/parity";

        public static void MakePutts()
        {
            string file = Path.Combine(Folder, "putts.json");
            if (File.Exists(file) && System.Environment.GetCommandLineArgs().Length > 0 && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fresh") < 0)
            {
                Debug.Log("Puttr 2 parity: keeping " + file);
                return;
            }
            var rows = new List<Dictionary<string, object>>();
            int id = 0;
            void Add(IReadOnlyList<BallPoint> path, Hole hole, int distance, int tee)
            {
                rows.Add(new Dictionary<string, object>
                {
                    { "id", ++id }, { "distance", distance }, { "tee", tee },
                    { "ballPath", RoundSaver.PathJson(path) }, { "holeBounds", "[" + hole.X + "," + hole.Y + "," + hole.Diameter + "]" },
                });
            }
            foreach (PuttrSamples.Sample sample in PuttrSamples.Recorded) Add(sample.Path, PuttrSamples.SampleHole, 6, 2);

            var dice = new System.Random(20261116);
            Hole[] holes = { PuttrSamples.SampleHole, new Hole(312, 96, 104), new Hole(330, 108, 118), new Hole(300, 90, 96) };
            foreach (Hole hole in holes)
                for (float line = -4f; line <= 4f; line += 0.5f)
                    foreach (float speed in new[] { -14f, -6f, 18f, 26f, 34f, 42f, 52f, 64f, 80f })
                    {
                        float heading = (float)(dice.NextDouble() * 8 - 4);
                        Add(PuttrSamples.Synthesize(line, speed, heading, hole), hole, 3 + dice.Next(9), 1 + dice.Next(3));
                    }

            // Edge cases: a first point inside the hole level with its centre (the app's atan quirk),
            // a path all under the hole, frames read twice, a hand over the hole, a tiny path.
            Hole h = PuttrSamples.SampleHole;
            Add(new List<BallPoint> { new BallPoint(330, 300, 30), new BallPoint(332, 220, 26), new BallPoint(345, 100, 20), new BallPoint(330, 95, 18), new BallPoint(331, 94, 18), new BallPoint(331, 94, 18) }, h, 5, 2);
            Add(new List<BallPoint> { new BallPoint(300, 300, 30), new BallPoint(305, 220, 26), new BallPoint(295, 100, 20), new BallPoint(300, 95, 18), new BallPoint(300, 96, 18) }, h, 5, 1);
            Add(new List<BallPoint> { new BallPoint(320, 110, 22), new BallPoint(321, 108, 22), new BallPoint(322, 104, 21) }, h, 7, 3);
            Add(PuttrSamples.Hand(h), h, 6, 2);
            // One point inside the hole's edge at the end: the app saves it as made and scores it as a miss.
            Add(new List<BallPoint> { new BallPoint(320, 300, 30), new BallPoint(330, 250, 28), new BallPoint(360, 200, 26), new BallPoint(400, 150, 24), new BallPoint(420, 120, 22), new BallPoint(370, 110, 22) }, h, 6, 2);
            Add(new List<BallPoint> { new BallPoint(300, 300, 30), new BallPoint(290, 250, 28), new BallPoint(270, 200, 26), new BallPoint(240, 150, 24), new BallPoint(222, 120, 22), new BallPoint(268, 112, 22) }, h, 9, 1);
            var doubled = new List<BallPoint>();
            foreach (BallPoint p in PuttrSamples.Synthesize(0.4f, 38f, 0f, h)) { doubled.Add(p); doubled.Add(p); }
            Add(doubled, h, 8, 2);

            Directory.CreateDirectory(Folder);
            File.WriteAllText(file, J.Write(rows));
            Debug.Log("Puttr 2 parity: wrote " + rows.Count + " putts to " + file);
        }

        public static void Judge()
        {
            string input = Path.Combine(Folder, "putts.json");
            var output = new List<Dictionary<string, object>>();
            foreach (object row in J.List(J.Parse(File.ReadAllText(input))))
            {
                int[][] bp = AppPutt.Path(J.Str(row, "ballPath"));
                double[] hb = AppPutt.Hole(J.Str(row, "holeBounds"));
                int distance = J.Int(row, "distance", 6), tee = J.Int(row, "tee", 2);
                if (distance == 0) distance = 6;
                if (tee == 0) tee = 2;
                var o = new Dictionary<string, object> { { "id", J.Get(row, "id") } };
                if (bp.Length == 0 || hb == null) { o["unreadable"] = true; output.Add(o); continue; }
                AppPutt.Verdict v = AppPutt.Judge(bp, hb, distance, tee);
                AppPutt.Rollout analysis = AppPutt.AnalyzeRollout(bp, hb);
                o["ball"] = v.Ball; o["rollUnder"] = v.RollUnder; o["count"] = v.Count; o["entry"] = v.Entry;
                o["teeLine"] = AppPutt.TeeEntry(distance, tee); o["description"] = v.Description;
                o["missResult"] = AppPutt.MissResult(bp, hb); o["missSide"] = v.MissSide;
                o["rollout"] = v.Rollout; o["arrival"] = AppPutt.ArrivalSpeed(analysis); o["score"] = v.Score; o["grade"] = v.Grade;
                o["startLine"] = AppPutt.StartLine(bp, hb, distance, out double deg, out _) ? deg : (object)null;

                // The putt SDK's own reading, which the 3D replay uses: reported, not required to match.
                var points = new List<BallPoint>();
                foreach (int[] p in bp) points.Add(new BallPoint(p[0], p[1], p[2]));
                var hole = new Hole((float)hb[0], (float)hb[1], (float)hb[2]);
                o["sdkMade"] = PuttrAnalysis.IsMade(points, hole);
                o["sdkEntry"] = PuttrAnalysis.TryHoleEntry(points, hole, out _, out int angle) ? angle : (object)null;
                MissSide side = PuttrAnalysis.GetMissSide(points, hole);
                o["sdkMissSide"] = side == MissSide.None ? null : side.ToString().ToUpperInvariant();
                output.Add(o);
            }
            File.WriteAllText(Path.Combine(Folder, "unity.json"), J.Write(output));
            Debug.Log("Puttr 2 parity: judged " + output.Count + " putts");
        }
    }
}
