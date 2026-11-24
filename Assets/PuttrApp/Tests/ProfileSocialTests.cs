// Profile and social: the putt card's grade and pace (common/PuttCard.js computePuttDisplay, GameFunctions getPuttGrade),
// the Putt Quality Score (members/PuttScore.js) and the feed's score badge (utils/activityScore.js scoreBadge).
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PuttrApp.Tests
{
    public class ProfileSocialTests
    {
        // getPuttGrade's bands, value by value: a plus only above 35, 55, 75 and 95.
        [TestCase(0, "F")] [TestCase(19.9, "F")] [TestCase(20, "D")] [TestCase(35, "D")] [TestCase(35.5, "D+")] [TestCase(39.9, "D+")]
        [TestCase(40, "C")] [TestCase(55, "C")] [TestCase(56, "C+")] [TestCase(60, "B")] [TestCase(75, "B")] [TestCase(76, "B+")]
        [TestCase(80, "A")] [TestCase(95, "A")] [TestCase(95.5, "A+")] [TestCase(98, "A+")]
        public void GradeBandsMatchTheApp(double score, string grade) => Assert.AreEqual(grade, AppPutt.Grade(score));

        [Test]
        public void AMadeStraightPuttWithItsStoredPace()
        {
            // The walk's "A+ 98", "10ft Straight", "1.4 in past": the entry half full, the pace half 50 x (1 - 1.4 / 36).
            PuttDisplay d = PuttDisplay.Of(J.Parse("{\"made\":1,\"distance\":10,\"tee\":2,\"rollout\":\"1.40\"}"));
            Assert.AreEqual("A+", d.Grade);
            Assert.AreEqual(98, d.RoundedScore);
            Assert.AreEqual("Straight", d.BreakLabel);
            Assert.AreEqual("1.4 in past", d.PaceText);
            Assert.AreEqual(PuttDisplay.Ideal, d.PaceColour);
            Assert.AreEqual(16.4f / 45f, d.PacePct, 1e-4f);
            Assert.AreEqual("Center cut · 0.0°", d.EntryLine);
        }

        [Test]
        public void AShortMissReadsBlueAndD()
        {
            // The walk's "D+ 39", "2.7 in short": no entry half, the pace half 50 x (1 - 2.7 / 12) on the short side.
            PuttDisplay d = PuttDisplay.Of(J.Parse("{\"made\":0,\"distance\":3,\"tee\":3,\"rollout\":-2.7}"));
            Assert.AreEqual("D+", d.Grade);
            Assert.AreEqual(39, d.RoundedScore);
            Assert.AreEqual("Right to left", d.BreakLabel);
            Assert.AreEqual("2.7 in short", d.PaceText);
            Assert.AreEqual(PuttDisplay.Short, d.PaceColour);
            Assert.AreEqual(Ui.Red, d.GradeColour);
            StringAssert.StartsWith("Missed ", d.EntryLine);
        }

        [Test]
        public void NoPaceReadIsAStub()
        {
            PuttDisplay d = PuttDisplay.Of(J.Parse("{\"made\":0,\"distance\":6,\"tee\":1}"));
            Assert.AreEqual("no read", d.PaceText);
            Assert.AreEqual(0.05f, d.PacePct, 1e-6f);
            Assert.AreEqual("F", d.Grade);
            Assert.AreEqual("Left to right", d.BreakLabel);
        }

        [Test]
        public void PqsScoresMakeIndexAndAccuracy()
        {
            var putts = new List<PqsModel.Putt>();
            for (int i = 0; i < 10; i++) putts.Add(new PqsModel.Putt { Distance = 3, Made = i < 5 ? 1 : 0, Score = 100, At = DateTime.Now });
            PqsModel.Point p = PqsModel.Score(putts);
            // Make index 0.5 / 0.99 against the Tour at 3 ft; accuracy 1 from the five makes: 0.65 x 0.505 + 0.35.
            Assert.AreEqual(51, p.MakeIndex);
            Assert.AreEqual(100, p.Accuracy);
            Assert.AreEqual(68, p.Pqs);
            Assert.IsNull(PqsModel.Score(new List<PqsModel.Putt>()));
        }

        [Test]
        public void WeeksStartOnMonday()
        {
            Assert.AreEqual(new DateTime(2026, 11, 16), PqsModel.WeekOf(new DateTime(2026, 11, 18, 15, 0, 0)));
            Assert.AreEqual(new DateTime(2026, 11, 16), PqsModel.WeekOf(new DateTime(2026, 11, 22, 9, 0, 0)));
            Assert.AreEqual(new DateTime(2026, 10, 1), PqsModel.QuarterOf(new DateTime(2026, 11, 18)));
        }

        [Test]
        public void ManyPracticeDaysRollUpIntoWeeks()
        {
            DateTime now = new DateTime(2026, 11, 18, 20, 0, 0);
            var putts = new List<PqsModel.Putt>();
            for (int day = 0; day < 20; day++)
                for (int k = 0; k < 5; k++) putts.Add(new PqsModel.Putt { Distance = 5, Made = k % 2, Score = 80, At = now.AddDays(-day * 3).AddMinutes(-k) });
            PqsModel m = PqsModel.Of(putts, now);
            Assert.AreEqual(20, m.Days.Count);
            Assert.AreEqual("Last 30 days", m.HeroLabel);
            Assert.IsTrue(m.ShowSelector);
            Assert.AreEqual("1M", m.DefaultRange);
            List<PqsModel.Point> all = m.Series("ALL", out List<string> labels, out string level);
            Assert.AreEqual("week", level);
            Assert.AreEqual(all.Count, labels.Count);
            Assert.IsFalse(string.IsNullOrEmpty(labels[labels.Count - 1]), "the latest point keeps its label");
            m.Series("1M", out _, out string month);
            Assert.AreEqual("day", month);
            Assert.IsFalse(m.Calibrating);
        }

        [Test]
        public void FeedBadgeIsTheStoredScoreOrToPar()
        {
            Assert.AreEqual("17.58", FeedScreen.Badge(J.Parse("{\"gameType\":\"STANDARD\",\"score\":17.58}")));
            Assert.AreEqual("0", FeedScreen.Badge(J.Parse("{\"gameType\":\"STREAK\",\"score\":0}")));
            Assert.AreEqual("E", FeedScreen.Badge(J.Parse("{\"gameType\":\"TRACKS\",\"score\":0}")));
            Assert.AreEqual("+3", FeedScreen.Badge(J.Parse("{\"gameType\":\"TRACKS\",\"score\":3}")));
            Assert.AreEqual("-2", FeedScreen.Badge(J.Parse("{\"gameType\":\"TRACKS\",\"score\":-2}")));
        }
    }
}
