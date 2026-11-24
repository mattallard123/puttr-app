// What the saves send now that play records go to production (Matt, 2026-11-18): the fields that were checked against
// puttr-app and puttr-api one by one. A mistake here puts a wrong score, flag or board row into a real account.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class ProductionSaveTests
    {
        [Test]
        public void CompetitionRoundsAreEnteredUnlessRehearsing()
        {
            foreach (CompetitionRound.Kind kind in new[] { CompetitionRound.Kind.Tournament, CompetitionRound.Kind.League })
            {
                Assert.AreEqual(CompetitionRound.Mode.Entered, CompetitionRound.ModeFor(kind, true, false), kind + ": entered, as in the app");
                Assert.AreEqual(CompetitionRound.Mode.Rehearsal, CompetitionRound.ModeFor(kind, true, true), kind + ": a tester's rehearsal");
                Assert.AreEqual(CompetitionRound.Mode.Local, CompetitionRound.ModeFor(kind, false, false), kind + ": no Puttr, nothing sent");
            }
            foreach (CompetitionRound.Kind kind in new[] { CompetitionRound.Kind.Coach, CompetitionRound.Kind.Practice, CompetitionRound.Kind.Game })
                Assert.AreEqual(CompetitionRound.Mode.Entered, CompetitionRound.ModeFor(kind, true, true), kind + ": never rehearsed");
        }

        [Test]
        public void MadeIsSavedAsEachScreenOfTheAppSavesIt()
        {
            var nearMiss = new AppPutt.Verdict { Count = 1 };   // the camera's count of 1: scored a miss, saved as made by most games
            var make = new AppPutt.Verdict { Count = 2 };
            // Tournament, League Night, a catalog game: GameActions.savePutt's made > 0.
            Assert.IsTrue(CompetitionRound.MadeFlag(CompetitionRound.Kind.Tournament, false, nearMiss, 0));
            Assert.IsTrue(CompetitionRound.MadeFlag(CompetitionRound.Kind.League, false, nearMiss, 0));
            Assert.IsTrue(CompetitionRound.MadeFlag(CompetitionRound.Kind.Game, false, nearMiss, 0));
            // A quest level: the level's own verdict (a Precision make outside the window is a miss).
            Assert.IsFalse(CompetitionRound.MadeFlag(CompetitionRound.Kind.Quest, false, make, 0));
            Assert.IsTrue(CompetitionRound.MadeFlag(CompetitionRound.Kind.Quest, false, make, 1));
            // The coach's session and calibration: getMakeProbability > 1.
            Assert.IsFalse(CompetitionRound.MadeFlag(CompetitionRound.Kind.Coach, false, nearMiss, 6));
            Assert.IsTrue(CompetitionRound.MadeFlag(CompetitionRound.Kind.Coach, false, make, 0));
            Assert.IsFalse(CompetitionRound.MadeFlag(CompetitionRound.Kind.Practice, true, nearMiss, 6));
        }

        [Test]
        public void TheCoachSessionClosesWithTheCamerasMakes()
        {
            var session = new RoundSession();
            session.Seen.Add(new AppPutt.Verdict { Count = 2 });
            session.Seen.Add(new AppPutt.Verdict { Count = 1 });
            session.Seen.Add(new AppPutt.Verdict { Count = 3 });
            session.Seen.Add(new AppPutt.Verdict { Count = 0 });
            session.Seen.Add(new AppPutt.Verdict { Count = 2 });
            session.Seen.Add(new AppPutt.Verdict { Count = 0 });
            session.Seen.Add(new AppPutt.Verdict { Count = 0 });
            session.Seen.Add(new AppPutt.Verdict { Count = 0 });
            Dictionary<string, object> body = CompetitionRound.CoachBody(session, "8812");
            Assert.AreEqual(3, body["made"]);
            Assert.AreEqual(38.0, (double)body["makePct"], 0.0001, "3 of 8 is 37.5, rounded up as Math.round does");
            Assert.AreEqual(8812, body["userGameId"]);
            Assert.AreEqual(0, CompetitionRound.CoachBody(new RoundSession(), "")["userGameId"], "offline at the start: 0, and the API makes the row");
        }

        [Test]
        public void ATournamentRoundOpensWithItsOwnPutts()
        {
            var session = new RoundSession { Putts = new List<TournamentMath.Dealt> { new TournamentMath.Dealt { Feet = 5, Tee = 2 } } };
            string raw = "[{\"id\":91,\"distance\":5,\"tee\":2}]";
            Assert.AreEqual(raw, CompetitionRound.RoundPuttData(new Dictionary<string, object> { { "puttData", raw } }, session));
            Assert.AreEqual(session.DealtJson(), CompetitionRound.RoundPuttData(new Dictionary<string, object>(), session));
        }

        [Test]
        public void BoardRowsGoWhereTheAppPutsThem()
        {
            // The games puttr-app never posts.
            foreach (string type in new[] { "OPEN", "CRICKET", "ROCK", "BEERPONG", "DERBY", "HORSE", "MATCH", "ASSESSMENT", "PURE", "TRACKS" })
                Assert.IsFalse(RoundSaver.Boarded(type), type);
            foreach (string type in new[] { "ARCADESTREAK", "TARGET", "BOWLING", "BALLOONPOP", "MAJORLEAGUE", "CENTERCUT", "STANDARD", "STREAK", "OHONE", "DEADCENTER", "CLOCK", "DEADWEIGHT" })
                Assert.IsTrue(RoundSaver.Boarded(type), type);
            Assert.IsFalse(RoundSaver.Boarded(null));

            // The distance column, where the app's board is read by it.
            Assert.AreEqual(3, RoundSaver.BoardDistanceFor("MAJORLEAGUE", 0, true, 3));
            Assert.AreEqual(301, RoundSaver.BoardDistanceFor("OHONE", 0, true, 3));
            Assert.AreEqual(7, RoundSaver.BoardDistanceFor("DEADCENTER", 7, true, 3), "a picked spot");
            Assert.AreEqual(0, RoundSaver.BoardDistanceFor("DEADCENTER", 7, false, 3), "random putts");
            Assert.AreEqual(5, RoundSaver.BoardDistanceFor("LAB", 5, true, 3));
            Assert.AreEqual(9, RoundSaver.BoardDistanceFor("CENTERCUT", 0, false, 0));
            Assert.AreEqual(12, RoundSaver.BoardDistanceFor("SWITCHBACK", 0, false, 0));
            Assert.AreEqual(15, RoundSaver.BoardDistanceFor("CLOCK", 0, false, 0));
            Assert.AreEqual(0, RoundSaver.BoardDistanceFor("STANDARD", 6, false, 0));
            Assert.AreEqual(0, RoundSaver.BoardDistanceFor("ARCADESTREAK", 6, false, 0));
        }
    }
}
