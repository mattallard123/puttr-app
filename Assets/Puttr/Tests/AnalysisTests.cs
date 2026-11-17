// The C# port must agree with sdk/puttr.js. The expected numbers below come
// from running analyzePutt in the JavaScript SDK over the same recordings.
using System.Collections.Generic;
using NUnit.Framework;
using PuttrArcade;
using UnityEngine;

namespace Puttr.Tests
{
    public class AnalysisTests
    {
        static readonly Hole H = PuttrSamples.SampleHole;

        // id, made, entry angle, miss side, miss by (in), speed (in/s), distinct frames
        static readonly object[] Expected =
        {
            new object[] { "center", true, 0, MissSide.None, 0f, 38.23f, 14 },
            new object[] { "left-side", true, -35, MissSide.None, 0f, 38.64f, 14 },
            new object[] { "right-side", true, 35, MissSide.None, 0f, 40.51f, 13 },
            new object[] { "left-edge", true, -72, MissSide.None, 0f, 40.95f, 13 },
            new object[] { "right-edge", true, 42, MissSide.None, 0f, 44.27f, 13 },
            new object[] { "miss-right", false, 0, MissSide.Right, 0f, 44.12f, 14 },
            new object[] { "wide-left", false, 0, MissSide.Left, 1.29f, 45.26f, 12 },
            new object[] { "wide-right", false, 0, MissSide.Right, 1.30f, 40.60f, 12 },
        };

        [TestCaseSource(nameof(Expected))]
        public void MatchesTheJavaScriptSdk(string id, bool made, int angle, MissSide side, float missBy, float speed, int frames)
        {
            PuttrSamples.Sample sample = System.Array.Find(PuttrSamples.Recorded, s => s.Id == id);
            PuttResult r = PuttrAnalysis.Analyze(sample.Path, H);
            Assert.IsTrue(r.Valid);
            Assert.AreEqual(made, r.Made);
            if (made) Assert.AreEqual(angle, r.EntryAngle);
            Assert.AreEqual(side, r.MissSide);
            Assert.AreEqual(missBy, r.MissByInches, 0.01f);
            Assert.AreEqual(speed, r.SpeedInPerSec, 0.02f);
            Assert.AreEqual(frames, r.Frames);
        }

        [Test]
        public void ParsesTheWireFormat()
        {
            // A console formats the path the way Python prints a list: with spaces.
            List<BallPoint> path = PuttrAnalysis.ParsePath("[[340, 299, 13], [340, 283, 19]]");
            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(283, path[1].Y);
            Assert.IsNull(PuttrAnalysis.ParsePath("[[340, 299"));

            Assert.IsTrue(PuttrAnalysis.TryParseHole("{\"x\":320,\"y\":100,\"diameter\":110}", out Hole wifi));
            Assert.IsTrue(PuttrAnalysis.TryParseHole("[320, 100, 110]", out Hole ble));
            Assert.AreEqual(55f, wifi.Radius);
            Assert.AreEqual(wifi.Diameter, ble.Diameter);
            Assert.IsTrue(PuttrAnalysis.IsHoleCalibrated(wifi));
            Assert.IsFalse(PuttrAnalysis.IsHoleCalibrated(new Hole(100, 300, 40)));
        }

        [Test]
        public void ScaleMatchesTheDocs()
        {
            Assert.AreEqual(26.45f, PuttrAnalysis.PixelsPerInch(55, 55), 0.01f);
            Assert.AreEqual(17.96f, PuttrAnalysis.PixelsPerInch(250, 55), 0.01f);
            Assert.AreEqual(-7f, PuttrAnalysis.ExpectedEntryAngle(6, 1) / 1.1f, 1.01f);
            Assert.AreEqual("LEFT SIDE", PuttrAnalysis.EntryDescription(-72));
            Assert.AreEqual("BACK DOOR", PuttrAnalysis.EntryDescription(120));
        }

        [Test]
        public void IntegratedInchesRoundTrip()
        {
            var inches = new Vector2(1.3f, 7.5f);
            Vector2 px = PuttrAnalysis.ToPixelsIntegrated(inches, H);
            Vector2 back = PuttrAnalysis.ToInchesIntegrated(px.x, px.y, H);
            Assert.AreEqual(inches.x, back.x, 0.02f);
            Assert.AreEqual(inches.y, back.y, 0.02f);
            // At the lip the hole's own size is the ruler: about 2.1 inches.
            Assert.AreEqual(2.1f, PuttrAnalysis.RadialInches(55, 55), 0.06f);
        }

        [Test]
        public void HandsAreNotPutts()
        {
            Assert.IsFalse(PuttrAnalysis.Analyze(PuttrSamples.Hand(H), H).Valid);
        }

        [Test]
        public void LaunchIsUsable()
        {
            foreach (PuttrSamples.Sample s in PuttrSamples.Recorded)
            {
                PuttLaunch l = PuttrAnalysis.Launch(s.Path, H);
                Assert.IsTrue(l.Reached, s.Id);
                Assert.GreaterOrEqual(l.ApproachInches.Length, 4, s.Id);
                Assert.That(l.SpeedInPerSec, Is.InRange(30f, 55f), s.Id);
                Assert.That(l.HandoffSpeedInPerSec, Is.InRange(25f, l.SpeedInPerSec), s.Id);
                Assert.That(Mathf.Abs(l.HeadingDeg), Is.LessThan(6f), s.Id);
                // Every approach point is on the ramp, short of the hole.
                foreach (Vector2 p in l.ApproachInches) Assert.Greater(p.y, 2.5f, s.Id);
            }
            Assert.AreEqual(0f, PuttrAnalysis.Launch(PuttrSamples.Recorded[0].Path, H).LineAtHoleInches, 0.25f);
            Assert.Less(PuttrAnalysis.Launch(PuttrSamples.Recorded[6].Path, H).LineAtHoleInches, -2f);
        }

        [Test]
        public void SyntheticPuttsBehave()
        {
            // A normal putt on line drops, and reads back at about the speed asked for.
            PuttResult made = PuttrAnalysis.Analyze(PuttrSamples.Synthesize(0.2f, 40f, 0f, H), H);
            Assert.IsTrue(made.Valid && made.Made);
            Assert.AreEqual(40f, made.Launch.HandoffSpeedInPerSec, 6f);

            // Too little speed never gets there.
            PuttResult shortPutt = PuttrAnalysis.Analyze(PuttrSamples.Synthesize(0f, -14f, 0f, H), H);
            Assert.IsTrue(shortPutt.Valid);
            Assert.IsFalse(shortPutt.Made);
            Assert.IsFalse(shortPutt.Launch.Reached);

            // Wide is a miss on the right side.
            PuttResult wide = PuttrAnalysis.Analyze(PuttrSamples.Synthesize(3.5f, 42f, 0f, H), H);
            Assert.IsTrue(wide.Valid);
            Assert.IsFalse(wide.Made);
            Assert.AreEqual(MissSide.Right, wide.MissSide);
            Assert.AreEqual(3.5f, wide.Launch.LineAtHoleInches, 0.4f);
        }
    }

    public class ScoringTests
    {
        static BowlingScore Play(params int[] rolls)
        {
            var s = new BowlingScore();
            foreach (int r in rolls) s.Add(r);
            return s;
        }

        [Test]
        public void PerfectGame()
        {
            BowlingScore s = Play(10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10);
            Assert.IsTrue(s.Complete);
            Assert.AreEqual(300, s.Total);
        }

        [Test]
        public void AllSpares()
        {
            var rolls = new List<int>();
            for (int i = 0; i < 21; i++) rolls.Add(5);
            BowlingScore s = Play(rolls.ToArray());
            Assert.IsTrue(s.Complete);
            Assert.AreEqual(150, s.Total);
            Assert.AreEqual("/", s.Frames()[0].Marks[1]);
        }

        [Test]
        public void OpenFramesAndRacks()
        {
            BowlingScore s = Play(9, 0, 3);
            Assert.AreEqual(1, s.FrameIndex);
            Assert.AreEqual(1, s.BallInFrame);
            Assert.AreEqual(7, s.PinsStanding);
            Assert.AreEqual(9, s.Frames()[0].Total);
            Assert.IsNull(s.Frames()[1].Total);
        }

        [Test]
        public void TenthFrame()
        {
            var open = new List<int>();
            for (int i = 0; i < 18; i++) open.Add(0);
            var a = new List<int>(open) { 10, 6 };
            BowlingScore s = Play(a.ToArray());
            Assert.IsFalse(s.Complete);
            Assert.AreEqual(4, s.PinsStanding);
            s.Add(4);
            Assert.IsTrue(s.Complete);
            Assert.AreEqual(20, s.Total);
            Assert.AreEqual(new[] { "X", "6", "/" }, s.Frames()[9].Marks);

            var b = new List<int>(open) { 3, 4 };
            Assert.IsTrue(Play(b.ToArray()).Complete);
        }
    }
}
