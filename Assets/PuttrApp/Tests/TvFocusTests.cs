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

        [Test]
        public void NothingThatWayIsNoMove()
        {
            Assert.AreEqual(-1, Tv.Pick(Focus, new List<Rect> { Tab, Wide, Above }, new[] { 1, 0, 0 }, Down));
        }
    }
}
