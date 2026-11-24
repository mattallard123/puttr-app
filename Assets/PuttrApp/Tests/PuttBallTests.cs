// Putt-ball's ball (PuttBallPhysics): the board is the Puttr's ramp. A make lands in the bullseye, short lands in the
// lower rings, a miss runs to the top of the board, and the back corners are the 100s.
using NUnit.Framework;
using PuttrArcade;

namespace PuttrApp.Tests
{
    public class PuttBallTests
    {
        static int Holed(float line, float past) => PuttBallPhysics.Run(line, past).Value;
        static int Miss(float line, float past) => PuttBallPhysics.Run(line, past, false).Value;
        static int Short(float line, float by) => PuttBallPhysics.Run(line, 0f, false, by).Value;

        [Test]
        public void AMakeIsTheBullseye()
        {
            foreach (float past in new[] { 0f, 3f, 6f, 9f, 12f, 16f }) Assert.AreEqual(40, Holed(0f, past), past + " in past");
            Assert.AreEqual(40, Holed(1.5f, 6f));
            Assert.AreEqual(40, Holed(-1.5f, 6f));
        }

        [Test]
        public void ADeadCenterMakeLandsHalfWayUpTheRings()
        {
            Assert.AreEqual(PuttBallPhysics.RingZ, PuttBallPhysics.Target(0f, 9f, true).y, 0.01f);
            Assert.AreEqual(0f, PuttBallPhysics.Target(0f, 9f, true).x, 0.01f);
        }

        [Test]
        public void TooFirmFallsBackToALowerRing()
        {
            PuttBallPhysics.FortyWindow(out float near, out float far);
            Assert.AreEqual(0f, near, 0.01f, "a putt that dies in is in the window");
            PuttBallPhysics.Flight past40 = PuttBallPhysics.Run(0f, far + 6f);
            Assert.Less(past40.Value, 40);
            Assert.Greater(past40.Value, 0);
            Assert.IsTrue(past40.Fell);
        }

        [Test]
        public void ShortLandsInTheLowerRings()
        {
            Assert.AreEqual(30, Short(0f, 1.5f));
            Assert.AreEqual(20, Short(0f, 2.5f));
            foreach (float by in new[] { 3.5f, 5f, 8f, 12f }) Assert.AreEqual(10, Short(0f, by), by + " in short");
        }

        [Test]
        public void TheLineSteersTheBallWithoutTouchingARail()
        {
            PuttBallPhysics.Flight left = PuttBallPhysics.Run(-10f, 12f, false), right = PuttBallPhysics.Run(10f, 12f, false);
            Assert.Less(left.Frames[left.LaneFrames - 1].x, -1f);
            Assert.Greater(right.Frames[right.LaneFrames - 1].x, 1f);
            for (int i = 0; i < right.LaneFrames; i++) Assert.Less(right.Frames[i].x, PuttBallPhysics.Half - PuttBallPhysics.BallR);
        }

        [Test]
        public void TheBackCornersOfTheMatAreTheHundreds()
        {
            Assert.AreEqual(100, Miss(-10.5f, 12f));
            Assert.AreEqual(100, Miss(10.5f, 12f));
            Assert.AreEqual(100, Miss(10.5f, 40f));
        }

        [Test]
        public void JustPastTheEdgeOfTheCupIsTheFifty()
        {
            Assert.AreEqual(50, Miss(2.2f, 12f));
            Assert.AreEqual(50, Miss(-2.2f, 12f));
        }

        [Test]
        public void AWideMissScoresLittle()
        {
            Assert.LessOrEqual(Miss(6f, 12f), 10);
            Assert.LessOrEqual(Miss(-6f, 12f), 10);
        }

        [Test]
        public void OnePuttAlwaysPlaysTheSame()
        {
            for (float line = -3f; line <= 3f; line += 0.5f)
                Assert.AreEqual(Holed(line, 21f), Holed(line, 21f));
        }
    }
}
