// Test putts (TestPutts.cs): when they are offered, which rounds take them, and that the
// recorded paths land on whatever hole the Puttr reports. A mistake here saves a fake putt to a real account.
using System.Collections.Generic;
using NUnit.Framework;
using Puttr;

namespace PuttrApp.Tests
{
    public class TestPuttTests
    {
        [Test]
        public void TournamentAndLeagueRoundsNeverTakeATestPuttOnceOpened()
        {
            foreach (CompetitionRound.Kind kind in new[] { CompetitionRound.Kind.Tournament, CompetitionRound.Kind.League })
            {
                Assert.IsFalse(TestPutts.RoundTakes(true, kind, CompetitionRound.Mode.Entered), kind + " entered");
                Assert.IsFalse(TestPutts.RoundTakes(true, kind, CompetitionRound.Mode.Rehearsal), kind + " rehearsal");
                Assert.IsTrue(TestPutts.RoundTakes(false, kind, CompetitionRound.Mode.Local), kind + " with no Puttr saves nothing");
            }
        }

        [Test]
        public void ATestersOwnPracticeTakesATestPutt()
        {
            foreach (CompetitionRound.Kind kind in new[] { CompetitionRound.Kind.Coach, CompetitionRound.Kind.Practice, CompetitionRound.Kind.Game, CompetitionRound.Kind.Quest })
            {
                Assert.IsTrue(TestPutts.RoundTakes(true, kind, CompetitionRound.Mode.Entered), kind + " for a tester with the buttons on");
                Assert.IsFalse(TestPutts.RoundTakes(false, kind, CompetitionRound.Mode.Entered), kind + " for anyone else");
            }
            Assert.IsTrue(TestPutts.RoundTakes(false, CompetitionRound.Kind.Drill, CompetitionRound.Mode.Entered), "a lesson drill saves nothing");
        }

        [Test]
        public void AnOnScreenTestPuttIsATestPuttWhateverIsConnected()
        {
            bool inside = false;
            Assert.IsFalse(RoundSaver.TestPuttNow, "before");
            RoundSaver.AsTest(() => inside = RoundSaver.TestPuttNow);
            Assert.IsTrue(inside, "while the putt is handled");
            Assert.IsFalse(RoundSaver.TestPuttNow, "after");
            // The mark comes down even if the putt's handling throws.
            Assert.Throws<System.InvalidOperationException>(() => RoundSaver.AsTest(() => throw new System.InvalidOperationException()));
            Assert.IsFalse(RoundSaver.TestPuttNow, "after a throw");
        }

        [Test]
        public void EveryButtonHasARecordedPutt()
        {
            Assert.AreEqual(6, TestPutts.Labels.Length);
            for (int i = 0; i < TestPutts.Labels.Length; i++)
            {
                IReadOnlyList<BallPoint> path = TestPutts.Recorded(i);
                Assert.IsNotNull(path, TestPutts.Labels[i]);
                Assert.Greater(path.Count, 10, TestPutts.Labels[i]);
            }
            Assert.IsNull(TestPutts.Recorded(-1));
            Assert.IsNull(TestPutts.Recorded(6));
        }

        [Test]
        public void TheButtonsRunLeftToRightAcrossTheHole()
        {
            // Where each path ends, against the hole it was recorded on: Wide L furthest left, R Edge right of Center.
            float End(int button) { IReadOnlyList<BallPoint> p = TestPutts.Recorded(button); return p[p.Count - 1].X - PuttrSamples.SampleHole.X; }
            Assert.Less(End(0), -60f, "Wide L misses left");
            Assert.Less(End(2), 0f, "L Side ends left of centre");
            Assert.AreEqual(0f, End(3), 3f, "Center");
            Assert.Greater(End(4), 0f, "R Side ends right of centre");
        }

        [Test]
        public void APathIsMovedAndSizedOntoTheHole()
        {
            var path = new List<BallPoint> { new BallPoint(320, 100, 22), new BallPoint(330, 210, 20) };
            // The same hole: unchanged.
            List<BallPoint> same = TestPutts.Fit(path, PuttrSamples.SampleHole, PuttrSamples.SampleHole);
            Assert.AreEqual(330, same[1].X); Assert.AreEqual(210, same[1].Y); Assert.AreEqual(20, same[1].R);
            // A hole elsewhere, the same size: moved with it.
            List<BallPoint> moved = TestPutts.Fit(path, PuttrSamples.SampleHole, new Hole(339, 73, 110));
            Assert.AreEqual(339, moved[0].X); Assert.AreEqual(73, moved[0].Y);
            Assert.AreEqual(349, moved[1].X); Assert.AreEqual(183, moved[1].Y);
            // A hole seen at half the size: the path shrinks about the hole.
            List<BallPoint> half = TestPutts.Fit(path, PuttrSamples.SampleHole, new Hole(320, 100, 55));
            Assert.AreEqual(325, half[1].X); Assert.AreEqual(155, half[1].Y); Assert.AreEqual(10, half[1].R);
        }
    }
}
