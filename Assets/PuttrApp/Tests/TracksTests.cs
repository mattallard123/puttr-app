// Tracks: the words on the shot interface, the shot card and the caddie's advice, and which line the booth is
// asked for. All of it is worked out from a shot's facts by TracksEngine, so it is checked here without a course.
using System;
using NUnit.Framework;
using UnityEngine;

namespace PuttrApp.Tests
{
    public class TracksTests
    {
        [Test]
        public void AdjustmentsAreTheAppsAmberWords()
        {
            TracksEngine.Club seven = TracksEngine.Suggest(150, "rough");
            CollectionAssert.AreEqual(new[] { "left tee", "+1 ft rough" }, TracksEngine.Adjustments(seven, "rough", 1));
            CollectionAssert.AreEqual(new[] { "+2 ft sand" }, TracksEngine.Adjustments(TracksEngine.Suggest(120, "sand"), "sand", 2));
            CollectionAssert.IsEmpty(TracksEngine.Adjustments(TracksEngine.Suggest(420, "tee"), "tee", 2));
            // On the green the side tee is the break, not an adjustment.
            CollectionAssert.IsEmpty(TracksEngine.Adjustments(TracksEngine.GreenPutt(8), "green", 3));
        }

        [Test]
        public void HintAndDistanceLeft()
        {
            Assert.AreEqual("perfect speed", TracksEngine.Hint(TracksEngine.Suggest(420, "tee"), 2));
            Assert.AreEqual("make it, breaks left to right", TracksEngine.Hint(TracksEngine.GreenPutt(8), 1));
            Assert.AreEqual("lag it close", TracksEngine.Hint(TracksEngine.GreenPutt(40), 2));
            Assert.AreEqual("Gimme inside 2.5 ft. Tapping in.", TracksEngine.Hint(TracksEngine.GreenPutt(2), 2));
            Assert.AreEqual("365 YDS TO THE PIN", TracksEngine.DistanceLeft(false, 365.2));
            Assert.AreEqual("8.4 FT TO THE HOLE", TracksEngine.DistanceLeft(true, 2.8));
            Assert.AreEqual("22 FT TO THE HOLE", TracksEngine.DistanceLeft(true, 22 / 3.0));
            Assert.AreEqual("amber", TracksEngine.LieTone("rough"));
            Assert.AreEqual("red", TracksEngine.LieTone("water"));
            Assert.AreEqual("green", TracksEngine.LieTone("fairway"));
        }

        [Test]
        public void ShotCardWordsAreTheApps()
        {
            Assert.AreEqual("Dead center", TracksEngine.CardLine(0.6, out bool ok)); Assert.IsTrue(ok);
            Assert.AreEqual("18 yds left", TracksEngine.CardLine(-18.2, out ok)); Assert.IsFalse(ok);
            Assert.AreEqual("3 yds right", TracksEngine.CardLine(2.5, out ok), "halves round up, as JavaScript");
            Assert.AreEqual("Perfect", TracksEngine.CardSpeed(-0.9, out ok)); Assert.IsTrue(ok);
            Assert.AreEqual("20 yds long", TracksEngine.CardSpeed(20.4, out ok)); Assert.IsFalse(ok);
            Assert.AreEqual("12 yds short", TracksEngine.CardSpeed(-12, out ok));
            Assert.AreEqual("3.0 in left on the mat", TracksEngine.CardLineMat(-3));
            Assert.AreEqual("1.2 in right on the mat", TracksEngine.CardLineMat(1.24));
            Assert.AreEqual("0.0 in on the mat", TracksEngine.CardLineMat(0));
            Assert.AreEqual("+12 in past", TracksEngine.CardSpeedMat(12));
            Assert.AreEqual("-3 in short", TracksEngine.CardSpeedMat(-3));
            Assert.AreEqual("0 in past", TracksEngine.CardSpeedMat(0));
            Assert.AreEqual("Bunker", TracksEngine.CardLie("sand")); Assert.AreEqual("bad", TracksEngine.CardLieTone("sand"));
            Assert.AreEqual("On the green", TracksEngine.CardLie("green")); Assert.AreEqual("good", TracksEngine.CardLieTone("green"));
            Assert.AreEqual("Rough", TracksEngine.CardLie("rough")); Assert.AreEqual("plain", TracksEngine.CardLieTone("rough"));
        }

        [Test]
        public void BoothCategoriesFollowTheApp()
        {
            Assert.AreEqual("tee_fairway", TracksEngine.CourseCall("fairway", true, false, false, 0));
            Assert.AreEqual("tee_trees", TracksEngine.CourseCall("gorse", true, false, false, 0));
            Assert.AreEqual("tee_rough", TracksEngine.CourseCall("rough", true, false, false, 0));
            Assert.AreEqual("tee_water", TracksEngine.CourseCall("water", true, false, false, 0));
            Assert.AreEqual("approach_water", TracksEngine.CourseCall("water", false, true, false, 0));
            Assert.AreEqual("tee_sand", TracksEngine.CourseCall("sand", true, false, false, 0));
            Assert.AreEqual("approach_sand", TracksEngine.CourseCall("sand", false, true, false, 0));
            Assert.AreEqual("approach_tight", TracksEngine.CourseCall("green", false, true, false, 8));
            Assert.AreEqual("approach_on", TracksEngine.CourseCall("green", true, true, false, 20), "a par 3's tee shot on the green is an approach");
            Assert.AreEqual("chip_tight", TracksEngine.CourseCall("green", false, true, true, 5));
            Assert.AreEqual("chip_on", TracksEngine.CourseCall("green", false, true, true, 9));
            Assert.AreEqual("approach_miss", TracksEngine.CourseCall("rough", false, true, false, 0));
            Assert.AreEqual("shot_fairway", TracksEngine.CourseCall("fairway", false, false, false, 0));
            Assert.AreEqual("shot_rough", TracksEngine.CourseCall("rough", false, false, false, 0));

            Assert.AreEqual("putt_made_long", TracksEngine.PuttCall(true, 15, false, true, 0, 0));
            Assert.IsNull(TracksEngine.PuttCall(true, 8, false, false, 0, 0), "a short make lets the score call speak");
            Assert.AreEqual("putt_lip", TracksEngine.PuttCall(false, 8, true, false, 1.5, 18));
            Assert.AreEqual("lag_good", TracksEngine.PuttCall(false, 40, false, true, 3.5, -30));
            Assert.AreEqual("putt_miss_short", TracksEngine.PuttCall(false, 8, false, false, 1, -3));
            Assert.AreEqual("putt_miss_long", TracksEngine.PuttCall(false, 8, false, false, 2.5, 30));
            Assert.AreEqual("putt_miss_line", TracksEngine.PuttCall(false, 8, false, false, 1, 6));

            Assert.AreEqual("score_pickup", TracksEngine.ScoreCall(true, 12, 4));
            Assert.AreEqual("score_good", TracksEngine.ScoreCall(false, 4, 4));
            Assert.AreEqual("score_bad", TracksEngine.ScoreCall(false, 5, 4));
            Assert.AreEqual("round_under", TracksEngine.RoundCall(-1));
            Assert.AreEqual("round_even", TracksEngine.RoundCall(0));
            Assert.AreEqual("round_over", TracksEngine.RoundCall(3));
        }

        [Test]
        public void TroubleIsFoundBesideShortOfAndBehindTheTarget()
        {
            // The shot flies up the y axis from the origin to (0, 200); its right is +x.
            Vector2 from = Vector2.zero, target = new Vector2(0, 200);
            Func<Vector2, string> waterLeft = p => p.x < -10 ? "water" : "fairway";
            Assert.IsTrue(TracksEngine.Trouble(waterLeft, from, target, out string lie, out string side));
            Assert.AreEqual("water", lie); Assert.AreEqual("left", side);

            Func<Vector2, string> sandRightWaterLong = p => p.y > 220 ? "water" : p.x > 5 ? "sand" : "fairway";
            Assert.IsTrue(TracksEngine.Trouble(sandRightWaterLong, from, target, out lie, out side));
            Assert.AreEqual("water", lie, "water is named before sand, even further off"); Assert.AreEqual("long", side);

            Func<Vector2, string> sandBoth = p => Mathf.Abs(p.x) > 6 ? "sand" : "fairway";
            Assert.IsTrue(TracksEngine.Trouble(sandBoth, from, target, out lie, out side));
            Assert.AreEqual("sand", lie); Assert.AreEqual("both", side);

            Func<Vector2, string> sandShort = p => p.y < 190 && p.y > 170 ? "sand" : "fairway";
            Assert.IsTrue(TracksEngine.Trouble(sandShort, from, target, out lie, out side));
            Assert.AreEqual("short", side);

            Assert.IsFalse(TracksEngine.Trouble(p => "fairway", from, target, out lie, out side));
            Assert.IsNull(lie); Assert.IsNull(side);
            Assert.IsFalse(TracksEngine.Trouble(p => "water", from, from, out lie, out side), "no shot, no side");
        }
    }
}
