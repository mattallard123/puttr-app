// Tracks with 1 to 4 Players (puttr-app Tracks.js nextTurn): everyone tees off in order, then whoever is farthest
// from the hole plays, a Player who has holed out sits out, and the round ranks everyone by strokes.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PuttrApp.Tests
{
    public class TracksRoundTests
    {
        static readonly Vector2 Tee = new Vector2(0, 0), Pin = new Vector2(0, 400);

        static TracksRound Round(int n, int first = 0, int holes = 2)
        {
            var names = new List<string>(); var ids = new List<string>(); var colours = new List<Color>();
            for (int i = 0; i < n; i++) { names.Add("P" + (i + 1)); ids.Add(i == 0 ? "me" : i == 1 ? "kid" : "0"); colours.Add(Color.HSVToRGB(i / 4f, 1, 1)); }
            var r = new TracksRound(names, ids, colours, holes, first);
            r.StartHole(0, Tee);
            return r;
        }

        // A shot: a stroke for whoever is up, their ball to where it lands.
        static void Hit(TracksRound r, Vector2 to) { r.Now.Strokes[r.Hole]++; r.Now.Ball = to; }

        [Test]
        public void EveryoneTeesOffInOrderThenTheFarthestPlays()
        {
            TracksRound r = Round(3);
            Assert.AreEqual(0, r.Cur);
            Hit(r, new Vector2(0, 250)); Assert.IsTrue(r.NextTurn(Pin)); Assert.AreEqual(1, r.Cur);
            Hit(r, new Vector2(0, 150)); Assert.IsTrue(r.NextTurn(Pin)); Assert.AreEqual(2, r.Cur);
            Hit(r, new Vector2(0, 300)); Assert.IsTrue(r.NextTurn(Pin));
            // Everyone is off the tee: P2 is 250 yds out, the farthest.
            Assert.AreEqual(1, r.Cur);
            Hit(r, new Vector2(0, 390)); Assert.IsTrue(r.NextTurn(Pin));
            Assert.AreEqual(0, r.Cur);   // P1 at 150 out
        }

        [Test]
        public void TheFarthestCanPlayAgain()
        {
            TracksRound r = Round(2);
            Hit(r, new Vector2(0, 100)); r.NextTurn(Pin);
            Hit(r, new Vector2(0, 300)); r.NextTurn(Pin);
            Assert.AreEqual(0, r.Cur);
            Hit(r, new Vector2(0, 200)); r.NextTurn(Pin);
            Assert.AreEqual(0, r.Cur, "still farther out than P2, so P1 plays again");
        }

        [Test]
        public void AHoledOutPlayerSitsOutAndTheHoleEndsWithTheLast()
        {
            TracksRound r = Round(2);
            Hit(r, new Vector2(0, 390)); r.NextTurn(Pin);
            Hit(r, new Vector2(0, 200)); r.NextTurn(Pin);
            Assert.AreEqual(1, r.Cur);
            Hit(r, Pin); r.Now.HoledOut = true;
            Assert.IsTrue(r.NextTurn(Pin));
            Assert.AreEqual(0, r.Cur);
            Hit(r, Pin); r.Now.HoledOut = true;
            Assert.IsFalse(r.NextTurn(Pin));
            Assert.IsTrue(r.AllHoledOut);
        }

        [Test]
        public void TheFirstPlayerHasTheHonourOnEveryHole()
        {
            TracksRound r = Round(3, first: 2);
            Assert.AreEqual(2, r.Cur);
            CollectionAssert.AreEqual(new[] { 2, 0, 1 }, r.TeeOrder);
            Hit(r, new Vector2(0, 200)); r.NextTurn(Pin);
            Assert.AreEqual(0, r.Cur);
            r.StartHole(1, Tee);
            Assert.AreEqual(2, r.Cur);
            Assert.IsFalse(r.Players[0].HoledOut);
            Assert.AreEqual(Tee, r.Players[1].Ball);
            Assert.AreEqual("tee", r.Players[1].Lie);
        }

        [Test]
        public void EachPlayerKeepsTheirOwnBallAndCard()
        {
            TracksRound r = Round(2);
            Hit(r, new Vector2(10, 240)); r.Now.Lie = "rough"; r.NextTurn(Pin);
            Hit(r, new Vector2(-5, 260)); r.Now.Lie = "fairway";
            Assert.AreEqual("rough", r.Players[0].Lie);
            Assert.AreEqual("fairway", r.Players[1].Lie);
            Assert.AreEqual(new Vector2(10, 240), r.Players[0].Ball);
            Assert.AreEqual(1, r.Players[0].Strokes[0]);
            Assert.AreEqual(1, r.Players[1].Strokes[0]);
        }

        [Test]
        public void RankingPlacesAndOutcome()
        {
            TracksRound r = Round(3);
            r.Players[0].Strokes[0] = 5; r.Players[1].Strokes[0] = 4; r.Players[2].Strokes[0] = 5;
            List<TracksPlayer> ranked = r.Ranking();
            Assert.AreEqual(1, ranked[0].Seat);
            Assert.AreEqual(0, ranked[1].Seat, "a tie keeps tee order");
            Assert.AreEqual(1, r.Place(r.Players[1]));
            Assert.AreEqual(2, r.Place(r.Players[0]));
            Assert.AreEqual(2, r.Place(r.Players[2]));
            Assert.AreEqual("P2 wins", r.Outcome());
            r.Players[1].Strokes[0] = 5;
            Assert.AreEqual("Tied", r.Outcome());
        }

        [Test]
        public void ToParCountsFinishedHolesOnly()
        {
            TracksRound r = Round(1);
            TracksPlayer p = r.Players[0];
            p.Strokes[0] = 3; p.Holed[0] = true; p.Strokes[1] = 2;
            Assert.AreEqual(-1, p.ToParSoFar(new[] { 4, 4 }));
            Assert.IsFalse(r.Multi);
        }

        [Test]
        public void GuestsAndWords()
        {
            TracksRound r = Round(3);
            Assert.IsFalse(r.Players[0].Guest);
            Assert.IsFalse(r.Players[1].Guest);
            Assert.IsTrue(r.Players[2].Guest);
            Assert.AreEqual("Ava's shot", TracksEngine.TurnWords("Ava"));
            Assert.AreEqual("Your shot", TracksEngine.TurnWords("You"));
            Assert.AreEqual("ALEXA", TracksEngine.CardName("Alexander"));
            Assert.AreEqual("SAM", TracksEngine.CardName(" Sam "));
        }
    }
}
