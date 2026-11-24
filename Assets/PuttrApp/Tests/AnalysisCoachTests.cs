// Full analysis start lines, the coach's /ask-coach words and Coach settings' time: the pure parts, checked against
// what puttr-app's StartLineScatter, CoachFunctions, AnalysisCoach and CoachSettings produce.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class AnalysisCoachTests
    {
        static List<StartLineMath.Point> Degs(params double[] degs)
        {
            var list = new List<StartLineMath.Point>();
            foreach (double d in degs) list.Add(new StartLineMath.Point { Deg = d, CrossingIn = d, Made = true });
            return list;
        }

        [Test]
        public void StartLineWordsMatchTheApp()
        {
            Assert.AreEqual("on line", StartLineMath.Format(0.04));
            Assert.AreEqual("on line", StartLineMath.Format(-0.049));
            Assert.AreEqual("0.4° left", StartLineMath.Format(-0.4));
            Assert.AreEqual("1.5° right", StartLineMath.Format(1.46));
            Assert.AreEqual("±2.5°", StartLineMath.Spread1(2.5));
        }

        [Test]
        public void SpreadIsTheMedianDeviationFromTheUpperMiddle()
        {
            // The app's median of an even count is the upper middle: [-1, 0, 2, 3] -> 2; deviations [3, 2, 0, 1] -> 2.
            Assert.AreEqual(2.0, StartLineMath.Median(Degs(-1, 0, 2, 3), false));
            Assert.AreEqual(2.0, StartLineMath.Spread(Degs(-1, 0, 2, 3)));
            Assert.IsNull(StartLineMath.Spread(Degs(1)), "one putt has no spread");
        }

        [Test]
        public void RowsPickTheTeeAndDistance()
        {
            var rows = new List<object>
            {
                J.Parse("{\"distance\":6,\"tee\":2,\"made\":1}"),
                J.Parse("{\"distance\":6,\"tee\":1,\"made\":0}"),
                J.Parse("{\"distance\":7,\"tee\":2,\"made\":0}"),
            };
            Assert.AreEqual(2, StartLineMath.Rows(rows, 2).Count);
            Assert.AreEqual(1, StartLineMath.Rows(rows, 2, 6).Count);
            Assert.AreEqual(0, StartLineMath.Points(rows).Count, "rows with no path are dropped");
        }

        [Test]
        public void ACoachsViewCountsSpotsFromThePutts()
        {
            var putts = new List<object>
            {
                J.Parse("{\"distance\":5,\"tee\":2,\"made\":1}"),
                J.Parse("{\"distance\":5,\"tee\":2,\"made\":0}"),
                J.Parse("{\"distance\":11,\"tee\":3,\"made\":\"1\"}"),
            };
            List<AnalysisMath.Cell> grid = AnalysisMath.Grid(AnalysisPage.PerPutt(putts));
            AnalysisMath.Cell five = grid[(5 - 3) * 3 + 1];
            Assert.AreEqual(2, five.Total);
            Assert.AreEqual(1, five.Made);
            Assert.AreEqual(1, grid[(11 - 3) * 3 + 2].Made);
            Assert.AreEqual(3, AnalysisMath.Putts(grid));
        }

        [Test]
        public void TheAskCoachQueryReadsAsTheAppWritesIt()
        {
            var a = new CoachPrompt.Assessment { PercentMade = 60, PushPull = "pull", PushPullPercent = 71, High = 2, Low = 1, SpeedConsistency = "consistent" };
            Assert.AreEqual(
                "Here is an analysis of tendencies based on their putt data: They made 60 % of their recent putts. Their push / pull tendency is pull which they do 71% of the time. " +
                "Their putt speed consistency is consistent. For breaking putts, they made 2 putts on the high side and 1 putts on the low side.  " +
                "Based on this data, what are some things they should focus on to improve? ",
                CoachPrompt.Query(a, null));
            var none = new CoachPrompt.Assessment { PercentMade = 0 };
            StringAssert.Contains("They have no push / pull tendency meaning they push the same as they pull. ", CoachPrompt.Query(none, null));
        }

        [Test]
        public void TheUserPromptReadsAsTheAppWritesIt()
        {
            object profile = J.Parse("{\"firstName\":\"Tiger\",\"handedness\":\"RIGHTY\",\"realHandicap\":\"1-9\",\"putterData\":\"{\\\"grip\\\":\\\"CLAW\\\"}\"}");
            Assert.AreEqual("You are assisting Tiger, a right handed golfer with a 1-9 handicap. and uses a CLAW putting grip style ", CoachPrompt.User(profile));
            Assert.AreEqual("You are assisting with no handicap. ", CoachPrompt.User(J.Parse("{\"realHandicap\":\"NONE\"}")));
        }

        [Test]
        public void PaceConsistencyUsesTheFleetCutoffs()
        {
            Assert.AreEqual("consistent", CoachPrompt.PaceConsistency(new List<double> { 1, 5 }), "under three putts says nothing");
            Assert.AreEqual("very consistent", CoachPrompt.PaceConsistency(new List<double> { 1.0, 1.05, 1.1, 1.12 }));
            Assert.AreEqual("not consistent", CoachPrompt.PaceConsistency(new List<double> { 0.2, 0.8, 1.6, 2.4 }));
            Assert.AreEqual(0.5, CoachPrompt.Quantile(new List<double> { 0, 1 }, 0.5), 1e-9);
        }

        [Test]
        public void TheAskCoachAnswerIsTheFirstText()
        {
            object data = J.Parse("{\"answer\":[{\"type\":\"reasoning\",\"content\":[]},{\"content\":[{\"text\":\"Work the gate drill.\"}]}],\"conversationId\":\"resp_1\"}");
            Assert.AreEqual("Work the gate drill.", CoachPrompt.AskCoachText(data));
            Assert.AreEqual("", CoachPrompt.AskCoachText(null));
        }

        [Test]
        public void ReminderTimesReadAndWriteAsTheApp()
        {
            CoachSettingsMath.ParseTime("6:30 PM", out int h, out int m);
            Assert.AreEqual(18, h); Assert.AreEqual(30, m);
            CoachSettingsMath.ParseTime("7:05 am", out h, out m);
            Assert.AreEqual(7, h); Assert.AreEqual(5, m);
            CoachSettingsMath.ParseTime("", out h, out m);
            Assert.AreEqual(18, h); Assert.AreEqual(30, m);
            Assert.AreEqual("9:00 AM", CoachSettingsMath.FormatTime(9, 0));
        }
    }
}
