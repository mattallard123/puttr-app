// Where a round goes: what -app2HoldPlay holds back from production (Api.Held, nothing by default), which putts are test putts,
// and who a pass-and-play game's putts and scores are saved under. None of it shows on screen when it is wrong.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class SaveTests
    {
        bool _writePlay;

        [SetUp]
        public void Keep() => _writePlay = Api.WritePlay;

        [TearDown]
        public void Restore() => Api.WritePlay = _writePlay;

        [Test]
        public void PlayRecordsAreWrittenUnlessHoldPlay()
        {
            // The default: every play record goes to the API.
            Api.HoldPlay = false;
            Assert.IsTrue(Api.WritePlay);
            foreach (string path in new[] { "/game/new/", "/game/savePutt/", "/game/over/", "/club/leaderboard/add", "/tracks/round", "/versus/create", "/quests/savePutt/", "/coach/session-start" })
            {
                Assert.IsFalse(Api.Held("POST", path), path);
                Assert.IsFalse(Api.Holds(path), path);
            }
            Assert.IsFalse(Api.Held("PUT", "/tracks/round/7"));
            Assert.IsFalse(Api.HoldsPlay);
            Assert.IsNull(GameBridge.HeldNote(new CompetitionRound(CompetitionRound.Kind.Game, new RoundSession(), "5"), false), "no held note when nothing is held");

            // -app2HoldPlay: answered on the device.
            Api.HoldPlay = true;
            Assert.IsFalse(Api.WritePlay);
            foreach (string path in new[]
            {
                "/game/new/", "/game/savePutt/", "/game/over/", "/game/multiplayer/over", "/game/abandon", "/club/leaderboard/add",
                "/tracks/round", "/versus/create", "/versus/12/complete",
                // Quest levels and the coach's session are play too: putts, stars, a game row, the streak.
                "/quests/savePutt/", "/quests/level/finish", "/coach/session-start", "/coach/session-complete",
            })
            {
                Assert.IsTrue(Api.Held("POST", path), path);
                Assert.IsTrue(Api.Holds(path), path);
            }
            Assert.IsTrue(Api.Held("PUT", "/tracks/round/7"));
            Assert.IsTrue(Api.HoldsPlay);
            // Reads, and writes that are not play, go through.
            Assert.IsFalse(Api.Held("GET", "/game/best/5"));
            Assert.IsFalse(Api.Held("GET", "/quests/"));
            Assert.IsFalse(Api.Holds("/coach/settings"));
            Assert.IsFalse(Api.Holds("/lessons/review"));
            Assert.IsFalse(Api.Holds("/profile/save"));

            Api.HoldPlay = false;
            Assert.IsFalse(Api.Holds("/game/new/"));
            Assert.IsFalse(Api.Holds("/quests/savePutt/"));
            Assert.IsFalse(Api.Holds("/coach/session-complete"));
            Assert.IsFalse(Api.HoldsPlay);
        }

        [Test]
        public void LocalIdsAreTheHoldsOwn()
        {
            Assert.IsTrue(Api.IsLocal("local-3"));
            Assert.IsFalse(Api.IsLocal("48213"));
            Assert.IsFalse(Api.IsLocal(""));
            Assert.IsFalse(Api.IsLocal(null));
        }

        [Test]
        public void HeldPuttsAreSaidPlainly()
        {
            // Only ever said under -app2HoldPlay.
            Assert.AreEqual("Held on this device: 1 putt was not saved.", Api.PuttsNotSaved(1));
            Assert.AreEqual("Held on this device: 12 putts were not saved.", Api.PuttsNotSaved(12));
        }

        [Test]
        public void TestPuttsAreNeverSavedWithoutTheFlag()
        {
            // IsTestPutt(demo, realPutts, saveTestPutts, keyboard)
            Assert.IsFalse(RoundSaver.IsTestPutt(false, true, false, false), "a real putt on a real Puttr");
            Assert.IsTrue(RoundSaver.IsTestPutt(false, false, false, false), "no Puttr, or the simulator");
            Assert.IsTrue(RoundSaver.IsTestPutt(false, true, false, true), "a keyboard putt with a real Puttr connected");
            Assert.IsTrue(RoundSaver.IsTestPutt(true, true, false, false), "the arcade's demo");
            Assert.IsFalse(RoundSaver.IsTestPutt(false, true, true, true), "-app2SaveTestPutts lets the keyboard through");
            Assert.IsTrue(RoundSaver.IsTestPutt(true, true, true, false), "the demo is never saved, flag or not");
        }

        [Test]
        public void EachSeatSavesAsWhoeverSitsThere()
        {
            var ids = new List<string> { "host", "kid-1", RoundSaver.Guest, null };
            Assert.AreEqual("host", RoundSaver.SeatUser(ids, 0, 0, "host"));
            Assert.AreEqual("kid-1", RoundSaver.SeatUser(ids, 1, 0, "host"));
            Assert.AreEqual(RoundSaver.Guest, RoundSaver.SeatUser(ids, 2, 0, "host"));
            Assert.IsNull(RoundSaver.SeatUser(ids, 3, 0, "host"), "Puttr's putts are never saved");
            // A seat nobody named is a guest; so is a second seat carrying the host's id.
            Assert.AreEqual(RoundSaver.Guest, RoundSaver.SeatUser(null, 1, 0, "host"));
            Assert.AreEqual(RoundSaver.Guest, RoundSaver.SeatUser(ids, 9, 0, "host"));
            Assert.AreEqual(RoundSaver.Guest, RoundSaver.SeatUser(new List<string> { "host", "host" }, 1, 0, "host"));
            // The host need not sit first (whoever goes first sits first).
            var turned = new List<string> { "kid-1", "host" };
            Assert.AreEqual("host", RoundSaver.SeatUser(turned, 1, 1, "host"));
            Assert.AreEqual("kid-1", RoundSaver.SeatUser(turned, 0, 1, "host"));
        }

        [Test]
        public void MultiplayerOverNamesProfilesAndZeroesGuestsAndPuttr()
        {
            var scores = new List<double> { 12, 30, 7, 18 };
            var ids = new List<string> { "kid-1", "host", RoundSaver.Guest, null };
            Dictionary<string, object> body = RoundSaver.OverBody("555", scores, ids, 1, "host");
            Assert.AreEqual("555", body["gameId"]);
            CollectionAssert.AreEqual(new List<object> { 12.0, 30.0, 7.0, 18.0 }, (List<object>)body["scoreArray"]);
            CollectionAssert.AreEqual(new List<object> { "kid-1", "host", 0, 0 }, (List<object>)body["playerArray"]);
            // The host's id is at the host's seat even when the seats were not named, and nowhere else.
            body = RoundSaver.OverBody("9", new List<double> { 3, 4 }, null, 0, "host");
            CollectionAssert.AreEqual(new List<object> { "host", 0 }, (List<object>)body["playerArray"]);
            body = RoundSaver.OverBody("9", new List<double> { 3, 4 }, new List<string> { "host", "host" }, 0, "host");
            CollectionAssert.AreEqual(new List<object> { "host", 0 }, (List<object>)body["playerArray"]);
        }

        [Test]
        public void JumpBackInOffersOnlyWhatThisBuildPlays()
        {
            // Retired here (a Puttr streak game), or not brought over: no tile.
            Assert.IsFalse(GameBridge.Offered(new Dictionary<string, object> { { "id", 76.0 }, { "gameType", "STREAK" }, { "puttData", "[{\"distance\":6,\"tee\":2}]" } }));
            Assert.IsFalse(GameBridge.Offered(J.Parse("{\"gameType\":\"NOTAGAME\",\"title\":\"Off The Old Block\"}")));
            Assert.IsFalse(GameBridge.Offered(J.Parse("{\"gameType\":\"CHIPPING\"}")));
            Assert.IsTrue(GameBridge.Offered(J.Parse("{\"gameType\":\"ARCADESTREAK\"}")));
            Assert.IsTrue(GameBridge.Offered(J.Parse("{\"gameType\":\"OPEN\"}")));
        }
    }
}
