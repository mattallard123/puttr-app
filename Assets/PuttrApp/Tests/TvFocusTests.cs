// Where the remote's focus goes (Tv.Pick): the nearest target that way, and a scrolling list is walked to its end
// before the move leaves it for what is outside (the menu above the cards).
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp.Tests
{
    public class TvFocusTests
    {
        const NavigationMoveEvent.Direction Up = NavigationMoveEvent.Direction.Up, Down = NavigationMoveEvent.Direction.Down;

        // A menu tab above the page (outside the list: rank 1), then cards in the list (rank 0). The second row
        // is not lined up with the third (a wide card over narrow ones), and the tab is straight above the focus.
        static readonly Rect Tab = new Rect(600, 20, 160, 60), Wide = new Rect(100, 200, 400, 200), Above = new Rect(100, 450, 400, 200), Focus = new Rect(560, 700, 240, 200);

        [Test]
        public void UpStaysInTheListWhileThereIsACardAbove()
        {
            var rects = new List<Rect> { Tab, Wide, Above };
            Assert.AreEqual(2, Tv.Pick(Focus, rects, new[] { 1, 0, 0 }, Up));
            // Without the list's rank the tab, straight above, would win over the card off to the side.
            Assert.AreEqual(0, Tv.Pick(Focus, rects, null, Up));
        }

        [Test]
        public void UpLeavesTheListFromTheTopCard()
        {
            Assert.AreEqual(0, Tv.Pick(Wide, new List<Rect> { Tab, Above, Focus }, new[] { 1, 0, 0 }, Up));
        }

        [Test]
        public void InTheListTheNearestInLineStillWins()
        {
            var inLine = new Rect(560, 450, 240, 200);
            Assert.AreEqual(3, Tv.Pick(Focus, new List<Rect> { Tab, Wide, Above, inLine }, new[] { 1, 0, 0, 0 }, Up));
        }

        // A row redraws under the focus (Go first): the focus goes to what is now where it was, not to the Back button.
        [Test]
        public void ARedrawKeepsTheFocusWhereItWas()
        {
            var back = new Rect(20, 20, 52, 52);
            var goFirst = new Rect(700, 400, 110, 40);
            var remove = new Rect(830, 400, 40, 40);
            Assert.AreEqual(1, Tv.Nearest(new Vector2(752, 421), new List<Rect> { back, goFirst, remove }));
            // The chip got narrower ("1st") and the point is now just outside it: still the nearest.
            Assert.AreEqual(1, Tv.Nearest(new Vector2(752, 421), new List<Rect> { back, new Rect(760, 400, 50, 40), remove }));
            Assert.AreEqual(-1, Tv.Nearest(Vector2.zero, new List<Rect>()));
        }

        const NavigationMoveEvent.Direction Left = NavigationMoveEvent.Direction.Left, Right = NavigationMoveEvent.Direction.Right;

        // Apple's rule: the focus goes the way of the press. Sideways, a target that is not in line is not "that way",
        // however much nearer it is than nothing: a row ends where it ends.
        [Test]
        public void SidewaysStaysInItsRow()
        {
            var play = new Rect(40, 500, 300, 60);
            var account = new Rect(1100, 20, 140, 50);   // up and a long way right
            var node = new Rect(520, 380, 50, 50);       // nearer, still not in line
            Assert.AreEqual(-1, Tv.Pick(play, new List<Rect> { account, node }, null, Right));
            // The last chip of a row, with the tabs just above it: right is not up.
            var chip = new Rect(306, 75, 44, 30);
            var tab = new Rect(591, 6, 153, 58);
            Assert.AreEqual(-1, Tv.Pick(chip, new List<Rect> { tab }, null, Right));
        }

        // Up and down go row to row: with nothing straight above, the row above at the place nearest across.
        [Test]
        public void UpReachesTheRowAboveEvenOffToTheSide()
        {
            var card = new Rect(506, 110, 196, 130);
            var chips = new List<Rect> { new Rect(98, 75, 38, 30), new Rect(141, 75, 46, 30), new Rect(306, 75, 44, 30) };
            Assert.AreEqual(2, Tv.Pick(card, chips, null, Up));
            // A nearer row wins over a row further up that is better lined up.
            var tab = new Rect(520, 6, 150, 58);
            var both = new List<Rect>(chips) { tab };
            Assert.AreEqual(3, Tv.Pick(card, both, null, Up));   // the tab is in the beam: the beam still comes first
            Assert.AreEqual(2, Tv.Pick(card, new List<Rect>(chips) { new Rect(900, 6, 150, 58) }, null, Up));
        }

        // In the beam, the nearest edge wins, not the nearest centre: a wide card next door beats a small one beyond it.
        [Test]
        public void TheNearestEdgeWinsInTheBeam()
        {
            var from = new Rect(0, 100, 200, 100);
            var wide = new Rect(220, 100, 800, 100);     // centre at 620
            var small = new Rect(400, 220, 60, 60);      // not in the beam
            var beyond = new Rect(1040, 100, 60, 100);
            Assert.AreEqual(0, Tv.Pick(from, new List<Rect> { wide, small, beyond }, null, Right));
        }

        // Down from a card as wide as the page to a row of four: the one under its left edge, as a page reads.
        [Test]
        public void DownFromAWideCardStartsTheRowBelow()
        {
            var hero = new Rect(40, 100, 1200, 300);
            var row = new List<Rect> { new Rect(940, 420, 280, 160), new Rect(40, 420, 280, 160), new Rect(340, 420, 280, 160), new Rect(640, 420, 280, 160) };
            Assert.AreEqual(1, Tv.Pick(hero, row, null, Down));
        }

        // A grid moves by rows and columns.
        [Test]
        public void AGridMovesByRowAndColumn()
        {
            var cells = new List<Rect>();
            for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) cells.Add(new Rect(40 + c * 300, 100 + r * 200, 280, 180));
            Rect at = cells[5];
            var others = new List<Rect>(cells); others[5] = Rect.zero;
            Assert.AreEqual(1, Tv.Pick(at, others, null, Up));
            Assert.AreEqual(9, Tv.Pick(at, others, null, Down));
            Assert.AreEqual(4, Tv.Pick(at, others, null, Left));
            Assert.AreEqual(6, Tv.Pick(at, others, null, Right));
            Assert.AreEqual(-1, Tv.Pick(cells[3], cells, null, Right));   // the end of a row: no wrap
        }

        // A switch at the end of a row, between two buttons: down from the button above stops at it, not past it.
        [Test]
        public void NothingOnTheWayIsSkipped()
        {
            var retry = new Rect(539, 279, 203, 44);
            var toggle = new Rect(767, 439, 46, 28);
            var row = new Rect(467, 528, 346, 58);
            Assert.AreEqual(0, Tv.Pick(retry, new List<Rect> { toggle, row }, null, Down));
            Assert.AreEqual(1, Tv.Pick(toggle, new List<Rect> { retry, row }, null, Down));
        }

        // The scroll that brings the focus into view leaves room past it, and takes the page's top or end with it.
        [Test]
        public void TheFocusIsRevealedWithRoomToSpare()
        {
            // 600 high view over 2000 of content, at the top. A row at 700..860 comes in with 44 under it.
            Assert.AreEqual(304f, Tv.RevealAt(0f, 600f, 2000f, 700f, 860f, 44f), 0.01f);
            // Already in view with room: no move.
            Assert.AreEqual(304f, Tv.RevealAt(304f, 600f, 2000f, 500f, 660f, 44f), 0.01f);
            // Back up to the first row: all the way to the top, so the page's heading shows.
            Assert.AreEqual(0f, Tv.RevealAt(304f, 600f, 2000f, 80f, 240f, 44f), 0.01f);
            // The last row: all the way to the end.
            Assert.AreEqual(1400f, Tv.RevealAt(304f, 600f, 2000f, 1800f, 1960f, 44f), 0.01f);
            // Content shorter than the view never scrolls.
            Assert.AreEqual(0f, Tv.RevealAt(0f, 600f, 400f, 300f, 380f, 44f), 0.01f);
        }

        // A putt as a button: not straight after the screen comes up, and not twice in a row.
        [Test]
        public void APuttIsNotTakenStraightAway()
        {
            Assert.IsFalse(TvPutt.Ready(11f, 10f, -99f));
            Assert.IsTrue(TvPutt.Ready(13f, 10f, -99f));
            Assert.IsFalse(TvPutt.Ready(13f, 2f, 12f));
        }

        [Test]
        public void NothingThatWayIsNoMove()
        {
            Assert.AreEqual(-1, Tv.Pick(Focus, new List<Rect> { Tab, Wide, Above }, new[] { 1, 0, 0 }, Down));
        }
    }
}
