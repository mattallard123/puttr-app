// The Siri Remote's touch surface as d-pad steps (RemoteSwipe): one swipe, one step, one way. Frames are
// "time,stickX,stickY,dpadX,dpadY", as a traced Apple TV build logs them ("[remote] down | ..."): paste the part
// after the "|" of a swipe that went wrong into Recorded below and it is checked from then on.
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace PuttrApp.Tests
{
    public class RemoteSwipeTests
    {
        // A swipe at 60 frames a second: the stick goes from rest to `to` over `frames`, holds, then the thumb lifts.
        // The d-pad snaps to whichever of the eight ways the stick is past half way in.
        static string Swipe(float start, Vector2 to, int frames = 6, int hold = 4, Vector2? settle = null)
        {
            var sb = new StringBuilder();
            float t = start;
            void Add(Vector2 stick, Vector2? dpad = null)
            {
                Vector2 d = dpad ?? new Vector2(Mathf.Abs(stick.x) > 0.5f ? Mathf.Sign(stick.x) : 0f, Mathf.Abs(stick.y) > 0.5f ? Mathf.Sign(stick.y) : 0f);
                if (sb.Length > 0) sb.Append(';');
                sb.Append(RemoteSwipe.Frame(t, stick, d));
                t += 1f / 60f;
            }
            for (int i = 1; i <= frames; i++) Add(to * (i / (float)frames));
            for (int i = 0; i < hold; i++) Add(to);
            if (settle.HasValue) for (int i = 0; i < 4; i++) Add(Vector2.zero, settle.Value);
            for (int i = 0; i < 30; i++) Add(Vector2.zero);
            return sb.ToString();
        }

        static string Steps(string frames) => string.Join(",", RemoteSwipe.Replay(frames));

        [Test]
        public void AStraightSwipeIsOneStep()
        {
            Assert.AreEqual("down", Steps(Swipe(0f, new Vector2(0f, -1f))));
            Assert.AreEqual("up", Steps(Swipe(0f, new Vector2(0f, 1f))));
            Assert.AreEqual("left", Steps(Swipe(0f, new Vector2(-1f, 0f))));
            Assert.AreEqual("right", Steps(Swipe(0f, new Vector2(1f, 0f))));
        }

        // The bug: down crosses the step point first, right crosses a few frames later, and both stepped.
        [Test]
        public void ADownSwipeThatDriftsRightIsOnlyDown()
        {
            Assert.AreEqual("down", Steps(Swipe(0f, new Vector2(0.7f, -1f))));
            Assert.AreEqual("down", Steps(Swipe(0f, new Vector2(0.95f, -1f), frames: 12)));
        }

        // Frames at 60 a second from stick positions; the d-pad snaps as in Swipe.
        static string Frames(params Vector2[] sticks)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < sticks.Length; i++)
            {
                Vector2 s = sticks[i];
                if (i > 0) sb.Append(';');
                sb.Append(RemoteSwipe.Frame(i / 60f, s, new Vector2(Mathf.Abs(s.x) > 0.5f ? Mathf.Sign(s.x) : 0f, Mathf.Abs(s.y) > 0.5f ? Mathf.Sign(s.y) : 0f)));
            }
            return sb.ToString();
        }

        static Vector2[] Rest(int frames) => new Vector2[frames];

        // Down first, then the thumb curls right without lifting.
        [Test]
        public void ASwipeThatCurvesIsOnlyItsFirstWay()
        {
            var sticks = new List<Vector2> { new Vector2(0f, -0.3f), new Vector2(0.1f, -0.7f), new Vector2(0.1f, -0.9f) };
            for (int i = 1; i <= 8; i++) sticks.Add(new Vector2(0.1f + 0.1f * i, -0.9f));
            sticks.AddRange(Rest(30));
            Assert.AreEqual("down", Steps(Frames(sticks.ToArray())));
        }

        // The stick comes to rest while the d-pad is still snapped to a diagonal.
        [Test]
        public void ADiagonalLeftOnTheDpadAddsNothing()
        {
            Assert.AreEqual("down", Steps(Swipe(0f, new Vector2(0.2f, -1f), settle: new Vector2(1f, -1f))));
        }

        // A d-pad diagonal with a little stick under it goes the way the stick leans.
        [Test]
        public void ADpadDiagonalGoesTheWayTheStickLeans()
        {
            var swipe = new RemoteSwipe();
            Assert.AreEqual("down", swipe.Read(new Vector2(0.1f, -0.25f), new Vector2(1f, -1f), 0f));
        }

        [Test]
        public void TheSwingBackIsNotAStepBack()
        {
            var sticks = new List<Vector2> { new Vector2(0f, -0.3f), new Vector2(0f, -0.7f), new Vector2(0f, -1f), new Vector2(0f, -0.6f) };
            sticks.AddRange(Rest(15));                                               // at rest long enough to unlock
            sticks.Add(new Vector2(0f, 0.6f)); sticks.Add(new Vector2(0f, 0.8f));    // the swing back, a third of a second after the step
            sticks.AddRange(Rest(30));
            Assert.AreEqual("down", Steps(Frames(sticks.ToArray())));
        }

        [Test]
        public void TwoSwipesAreTwoSteps()
        {
            Assert.AreEqual("down,down", Steps(Swipe(0f, new Vector2(0f, -1f)) + ";" + Swipe(1f, new Vector2(0f, -1f))));
            Assert.AreEqual("down,right", Steps(Swipe(0f, new Vector2(0f, -1f)) + ";" + Swipe(1f, new Vector2(1f, 0f))));
        }

        [Test]
        public void IsBusyFromTheFirstTouchUntilJustAfterTheSwipe()
        {
            var swipe = new RemoteSwipe();
            Assert.IsFalse(swipe.Busy(0f));
            swipe.Read(new Vector2(0.1f, -0.3f), Vector2.zero, 1f);
            Assert.IsTrue(swipe.Busy(1f));
            swipe.Read(new Vector2(0.1f, -0.9f), Vector2.zero, 1.05f);
            swipe.Read(Vector2.zero, Vector2.zero, 1.1f);
            Assert.IsTrue(swipe.Busy(1.3f));
            swipe.Read(Vector2.zero, Vector2.zero, 1.6f);
            Assert.IsFalse(swipe.Busy(1.6f));
        }

        // Swipes recorded from a real remote ("[remote]" lines of a traced build): the step each should give, then its frames.
        static readonly (string want, string frames)[] Recorded = { };

        [Test]
        public void RecordedSwipesStepAsTheyShould()
        {
            foreach ((string want, string frames) in Recorded) Assert.AreEqual(want, Steps(frames));
        }
    }
}
