// 3D sessions: that their writes reach production while other play stays held, and that the session page's numbers
// (capture verdict, Ball overview, a putt's card, the body reads) come out as puttr-app's do.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class LabTests
    {
        [Test]
        public void ThreeDSessionWritesAreNeverHeld()
        {
            // Written by default like every play record, and still written under -app2HoldPlay, which holds games.
            foreach (bool hold in new[] { false, true })
            {
                Assert.IsFalse(Api.IsHeld(hold, "POST", "/lab/session/start"), "session start");
                Assert.IsFalse(Api.IsHeld(hold, "POST", "/lab/session/putt"), "session putt");
                Assert.IsFalse(Api.IsHeld(hold, "POST", "/lab/session/end"), "session end");
                Assert.IsFalse(Api.IsHeld(hold, "POST", "/lab/recording/start"), "lab recorder start");
                Assert.IsFalse(Api.IsHeld(hold, "POST", "/lab/recording/stop"), "lab recorder stop");
            }
            Assert.IsFalse(Api.IsHeld(false, "POST", "/game/new"), "a game is written by default");
            Assert.IsTrue(Api.IsHeld(true, "POST", "/game/new"), "a game is held under -app2HoldPlay");
            Assert.IsTrue(Api.IsHeld(true, "POST", "/video/agora/startRecording"), "and so is a round's recorder");
        }

        // A face-on pose file for putt 0 (its clip is 3 to 8 s of the replay, its window the last 3 s): the hands hold
        // at address, dip for the backstroke at backAt, peak for the follow through 0.5 s later, then come back.
        static object Stroke(double backAt)
        {
            var frames = new Dictionary<string, object>();
            for (int i = 0; i <= 100; i++)
            {
                double t = 3 + i * 0.05, x = 0.5;
                if (t > backAt - 0.4 && t <= backAt) x = 0.5 - 0.2 * (t - (backAt - 0.4)) / 0.4;
                else if (t > backAt && t <= backAt + 0.5) x = 0.3 + 0.5 * (t - backAt) / 0.5;
                else if (t > backAt + 0.5 && t <= backAt + 0.9) x = 0.8 - 0.3 * (t - (backAt + 0.5)) / 0.4;
                else if (t > backAt + 0.9) x = 0.5 - 0.02 * (t - (backAt + 0.9));   // the hands drift down after it
                object At(double px, double py) => new Dictionary<string, object> { { "x", px }, { "y", py }, { "z", 0.0 }, { "visibility", 1.0 } };
                frames["f" + i] = new Dictionary<string, object>
                {
                    { "timestamp", t },
                    { "landmarks", new Dictionary<string, object>
                        {
                            { "left_wrist", At(x + 0.02, 0.6) }, { "right_wrist", At(x - 0.02, 0.6) }, { "left_hip", At(0.56, 0.55) }, { "right_hip", At(0.44, 0.55) },
                            { "left_shoulder", At(0.58, 0.3) }, { "right_shoulder", At(0.42, 0.3) }, { "left_eye", At(0.52, 0.15) }, { "right_eye", At(0.48, 0.15) },
                            { "left_foot_index", At(0.6, 0.95) }, { "right_foot_index", At(0.4, 0.95) }, { "left_ankle", At(0.59, 0.9) }, { "right_ankle", At(0.41, 0.9) },
                            { "left_knee", At(0.58, 0.75) }, { "right_knee", At(0.42, 0.75) }, { "nose", At(0.5, 0.17) },
                        } },
                };
            }
            return frames;
        }

        [Test]
        public void TheSetupFrameIsFoundWhenTheStrokeIsOverBeforeTheWindow()
        {
            // A stroke inside the window: the setup is the address before the backstroke, as it always was.
            BodyFaults.Result inside = BodyFaults.Analyze(Stroke(6.4), 0, false);
            Assert.IsNotNull(inside);
            Assert.That(inside.SetupTime, Is.InRange(5.0, 6.1), "a stroke in the window");
            // A slow putt or a miss: the result came late, so the stroke was over before the window began and only the
            // follow through is in it. The setup is still the address before the backstroke, not the follow through.
            BodyFaults.Result late = BodyFaults.Analyze(Stroke(4.7), 0, false);
            Assert.IsNotNull(late);
            Assert.That(late.SetupTime, Is.LessThan(4.5), "a stroke before the window");
        }

        static object Session(string capture) => new Dictionary<string, object> { { "captureCheck", capture } };

        [Test]
        public void CaptureVerdictsReadAsTheAppWordsThem()
        {
            LabCapture.Info ok = LabCapture.Describe(LabCapture.Parse(Session("{\"verdict\":\"ok\"}")));
            Assert.IsTrue(ok.Ok);
            Assert.AreEqual("Good capture", ok.Short);
            Assert.AreEqual("Both camera views were recorded correctly: down the line, then face on.", ok.Detail);

            LabCapture.Info stayed = LabCapture.Describe(LabCapture.Parse(Session("{\"verdict\":\"bad\",\"reasons\":[\"wrongPerspective\"],\"halves\":{\"first\":\"downTheLine\",\"second\":\"downTheLine\"}}")));
            Assert.IsFalse(stayed.Ok);
            Assert.AreEqual("All down the line", stayed.Short);
            Assert.AreEqual("Check your camera setup", stayed.Title);
            StringAssert.StartsWith("It looks like the camera stayed down the line", stayed.Detail);
            StringAssert.EndsWith("record a new session for the most accurate analysis.", stayed.Detail);

            LabCapture.Info frame = LabCapture.Describe(LabCapture.Parse(Session("{\"verdict\":\"bad\",\"reasons\":[\"poorFraming\"]}")));
            Assert.AreEqual("Out of frame", frame.Short);

            Assert.IsNull(LabCapture.Parse(Session(null)), "no check before the checker existed");
            Assert.IsNull(LabCapture.Parse(Session("not json")));
        }

        [Test]
        public void MadeIsEntryNotZero()
        {
            Assert.IsTrue(LabBall.Made(new Dictionary<string, object> { { "entry", "[240,70,12,\"FRONT LEFT\"]" } }));
            Assert.IsFalse(LabBall.Made(new Dictionary<string, object> { { "entry", "0" } }));
            Assert.IsFalse(LabBall.Made(new Dictionary<string, object> { { "entry", 0.0 } }));
            Assert.IsFalse(LabBall.Made(new Dictionary<string, object>()));
        }

        static object Putt(string entry, double? rollout) => new Dictionary<string, object>
        {
            { "distance", 5.0 }, { "tee", 2.0 }, { "entry", entry }, { "rollout", rollout.HasValue ? (object)rollout.Value : null },
            { "ballPath", "[]" }, { "holeBounds", "[240,60,60]" },
        };

        [Test]
        public void BallOverviewMatchesTheApp()
        {
            // Two makes dead on line, dying at the hole: accuracy 10, speed 10 (50 / 5), total 20, A grade.
            LabBall.Overview o = LabBall.Of(new List<object> { Putt("[240,70,0,\"FRONT LEFT\"]", 0), Putt("[240,70,0,\"FRONT RIGHT\"]", 0) });
            Assert.AreEqual("10.00", o.Accuracy);
            Assert.AreEqual("10.00", o.Speed);
            Assert.AreEqual("20.00", o.Total);
            Assert.AreEqual("100%", o.Percent);
            Assert.AreEqual("A+", o.Grade);
            Assert.AreEqual("0.0", o.SpeedDiff);
            Assert.AreEqual("0", o.PathDiff);

            // A make 20° off line 6 in past, and a miss 6 in short: entry 8 then 0, pace spread 12 in.
            LabBall.Overview m = LabBall.Of(new List<object> { Putt("[240,70,20,\"FRONT RIGHT\"]", 6), Putt("0", -6) });
            Assert.AreEqual("4.00", m.Accuracy);
            Assert.AreEqual("50%", m.Percent);
            Assert.AreEqual("12.0", m.SpeedDiff);
            Assert.AreEqual("20", m.PathDiff, "the miss counts as 0 in the spread, as the app's does");

            Assert.AreEqual("0", LabBall.Of(new List<object>()).Accuracy);
        }

        [Test]
        public void BodyWordsAndColours()
        {
            Assert.AreEqual("Square", BodyFaults.ShoulderWords(0.01));
            Assert.AreEqual("Slightly closed", BodyFaults.ShoulderWords(0.03));
            Assert.AreEqual("Over ball", BodyFaults.EyeWords(0.5, false, false));
            Assert.AreEqual("Over the ball", BodyFaults.EyeWords(0.5, false, true));
            Assert.AreEqual("Slightly inside", BodyFaults.EyeWords(0.47, false, false));
            Assert.AreEqual("Slightly outside", BodyFaults.EyeWords(0.47, true, false), "a lefty's inside is the other way");
            Assert.AreEqual("Slight flex 15°", BodyFaults.KneeValue(15.2, false));
            Assert.AreEqual("Right heavy", BodyFaults.WeightWords(-0.05, false));
            Assert.AreEqual("Right side", BodyFaults.WeightWords(-0.05, true));
            Assert.AreEqual(0, BodyFaults.Grade("Square"));
            Assert.AreEqual(1, BodyFaults.Grade("Forward"));
            Assert.AreEqual(2, BodyFaults.Grade("Well forward"));
            Assert.AreEqual(2, BodyFaults.Grade("Wide"));
            Assert.AreEqual("0.69s", BodyFaults.Seconds(0.69));
            Assert.AreEqual("—", BodyFaults.Seconds(double.NaN));
        }

        static Dictionary<string, object> Mark(double x, double y, double z = 0, double vis = 0.99) =>
            new Dictionary<string, object> { { "x", x }, { "y", y }, { "z", z }, { "visibility", vis } };

        // A face-on stroke for the first putt (its window is 5 to 8 s): the hands still, back, through, then held.
        static object FaceOnPose()
        {
            var frames = new Dictionary<string, object>();
            int n = 0;
            for (double t = 4.9; t <= 8.1; t += 1 / 30.0)
            {
                double hands = t < 5.6 ? 0.50 : t < 6.3 ? 0.50 - (t - 5.6) * 0.1 : t < 6.8 ? 0.43 + (t - 6.3) * 0.26 : 0.56;
                var lm = new Dictionary<string, object>
                {
                    { "left_wrist", Mark(hands + 0.01, 0.6) }, { "right_wrist", Mark(hands - 0.01, 0.6) },
                    { "left_hip", Mark(0.56, 0.5) }, { "right_hip", Mark(0.44, 0.5) },
                    { "left_shoulder", Mark(0.58, 0.3) }, { "right_shoulder", Mark(0.42, 0.3) },
                    { "left_foot_index", Mark(0.62, 0.95) }, { "right_foot_index", Mark(0.38, 0.95) },
                };
                frames[(n++).ToString()] = new Dictionary<string, object> { { "timestamp", t }, { "landmarks", lm } };
            }
            return frames;
        }

        [Test]
        public void AFaceOnStrokeReadsInOrder()
        {
            BodyFaults.Pose pose = BodyFaults.Pose.Read(FaceOnPose());
            BodyFaults.Stroke a = BodyFaults.Analyze(pose, 5, 8, false);
            Assert.IsNotNull(a);
            Assert.IsFalse(a.DownTheLine, "the wrists spread across the picture: face on");
            Assert.Less(a.Back, a.Impact, "backstroke before impact");
            Assert.LessOrEqual(a.Impact, a.Follow, "impact before the follow through ends");
            BodyFaults.Reading r = BodyFaults.Read(a, false);
            Assert.IsNotNull(r);
            Assert.AreEqual(0.5, r.BallPosition, 1e-6, "between the toes");
            Assert.AreEqual(0.0, r.WeightBias, 1e-6, "shoulders level");
            Assert.AreEqual("Wide", BodyFaults.StanceWords(r.StanceWidth), "feet 0.24 apart, shoulders 0.16");

            BodyFaults.Session s = BodyFaults.ReadSession(pose, false, 6);
            Assert.IsNotNull(s.FaceOn);
            Assert.IsNull(s.DownTheLine);
            Assert.AreEqual(1, s.FaceOn.Count);
            List<BodyFaults.Fault> faults = BodyFaults.SessionFaults(s, false);
            Assert.AreEqual("stance_wide", faults[0].Key);
            Assert.IsNull(BodyFaults.ReadPutt(pose, 3, false), "no frames for putt 4");
        }
    }
}
