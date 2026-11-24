// A live 1v1's sums (Live.cs LiveMath, LiveResult): which state is newer after a reconnect, the wait between
// tries, and what the final screen says.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class LiveTests
    {
        static object Row(string winnerId, string state) => new Dictionary<string, object>
        {
            { "id", 55 }, { "userId", "host" }, { "participantId", "guest" }, { "hostName", "Marcus" }, { "participantName", "Sam" },
            { "status", "1" }, { "winnerId", winnerId },
            { "puttData", "[{\"distance\":6,\"tee\":2},{\"distance\":7,\"tee\":1}]" }, { "gameState", state },
        };

        [Test]
        public void ProgressOnlyGoesUp()
        {
            // Host putts, participant putts, next putt.
            Assert.Less(LiveMath.Progress(0, 1), LiveMath.Progress(0, 2));
            Assert.Less(LiveMath.Progress(0, 2), LiveMath.Progress(1, 1));
            Assert.AreEqual(LiveMath.Progress(3, 2), LiveMath.Progress(J.Parse("{\"puttCounter\":3,\"currentPlayer\":2}")));
        }

        [Test]
        public void BackoffDoublesThenHolds()
        {
            Assert.AreEqual(1f, LiveMath.Backoff(1));
            Assert.AreEqual(2f, LiveMath.Backoff(2));
            Assert.AreEqual(4f, LiveMath.Backoff(3));
            Assert.AreEqual(8f, LiveMath.Backoff(4));
            Assert.AreEqual(8f, LiveMath.Backoff(LiveMath.MaxAttempts));
        }

        [Test]
        public void MakePercentCountsPuttsThatScored()
        {
            Assert.AreEqual(-1, LiveMath.MakePercent(new List<double>()));
            Assert.AreEqual(67, LiveMath.MakePercent(new List<double> { 6, 0, 4.5 }));
        }

        [Test]
        public void SuddenDeathCountsTheExtraPutts()
        {
            Assert.AreEqual(0, LiveMath.SuddenDeathPutts(9, 9, false));
            Assert.AreEqual(1, LiveMath.SuddenDeathPutts(10, 9, true));
            Assert.AreEqual(3, LiveMath.SuddenDeathPutts(12, 9, true));
        }

        [Test]
        public void ResultReadsAFinishedRow()
        {
            LiveResult r = LiveResult.From(Row("guest", "{\"currentPlayer\":1,\"playerPutts\":[[6,0,0],[0,7,6]],\"puttCounter\":3,\"isOvertime\":true,\"rules\":\"entry\"}"), "guest");
            Assert.AreEqual(2, r.Winner);
            Assert.AreEqual(2, r.Me);
            Assert.AreEqual("You won", r.Banner);
            Assert.AreEqual(2, r.Dealt.Count);
            Assert.AreEqual(6.0, LiveMath.Score(r.Putts[0]));
            Assert.AreEqual(13.0, LiveMath.Score(r.Putts[1]));
            Assert.AreEqual(1, LiveMath.SuddenDeathPutts(r.PuttCounter, r.Dealt.Count, r.Overtime));
            Assert.AreEqual("You lost", LiveResult.From(Row("guest", "{}"), "host").Banner);
            Assert.AreEqual("Sam won", LiveResult.From(Row("guest", "{}"), "someone else").Banner);
        }
    }
}
