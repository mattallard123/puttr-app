// The coach's lesson tools that can be wrong without anyone seeing it on screen: the session grade the
// workspace shows (puttr-app getPuttGrade), the 30-day grid built from raw putts, and the key findings
// the coach picks and sends with a lesson.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class LessonDeliverTests
    {
        [Test]
        public void PuttGradeMatchesTheApp()
        {
            Assert.AreEqual("F", Coach.PuttGrade(0));
            Assert.AreEqual("F", Coach.PuttGrade(19.9));
            Assert.AreEqual("D", Coach.PuttGrade(20));
            Assert.AreEqual("D+", Coach.PuttGrade(36));
            Assert.AreEqual("C", Coach.PuttGrade(54));
            Assert.AreEqual("C+", Coach.PuttGrade(57));
            Assert.AreEqual("B", Coach.PuttGrade(60));
            Assert.AreEqual("B+", Coach.PuttGrade(76));
            Assert.AreEqual("A", Coach.PuttGrade(84));
            Assert.AreEqual("A+", Coach.PuttGrade(96));
        }

        [Test]
        public void HandicapReadsOneDecimalAndPlus()
        {
            Assert.AreEqual("7.5", Coach.Handicap(J.Parse("{\"h\":7.5}"), "h"));
            Assert.AreEqual("12.0", Coach.Handicap(J.Parse("{\"h\":\"12\"}"), "h"));
            Assert.AreEqual("+2.1", Coach.Handicap(J.Parse("{\"h\":-2.1}"), "h"));
            Assert.AreEqual("", Coach.Handicap(J.Parse("{}"), "h"));
        }

        [Test]
        public void GridCountsRawPuttsBySpot()
        {
            var rows = J.List(J.Parse("[{\"distance\":5,\"tee\":2,\"made\":1},{\"distance\":5,\"tee\":2,\"made\":0},{\"distance\":11,\"tee\":3,\"made\":1},{\"distance\":12,\"tee\":1,\"made\":1}]"));
            List<AnalysisMath.Cell> grid = LessonFindings.Grid(rows);
            Assert.AreEqual(27, grid.Count);
            AnalysisMath.Cell five = grid[(5 - 3) * 3 + 1];
            Assert.AreEqual(2, five.Total);
            Assert.AreEqual(1, five.Made);
            Assert.AreEqual(1, grid[26].Total);
            Assert.AreEqual(3, AnalysisMath.Putts(grid), "a 12 ft putt is off the mat and not counted");
        }

        [Test]
        public void SessionMadeIsAnyEntryButZero()
        {
            Assert.IsTrue(LessonFindings.SessionMade(J.Parse("{\"entry\":\"45\"}")));
            Assert.IsFalse(LessonFindings.SessionMade(J.Parse("{\"entry\":\"0\"}")));
            Assert.IsFalse(LessonFindings.SessionMade(J.Parse("{}")));
        }

        [Test]
        public void CandidatesComeFromTheSessionAndTheMonth()
        {
            object session = J.Parse("{\"score\":41}");
            var sessionPutts = J.List(J.Parse("[{\"entry\":\"30\"},{\"entry\":\"0\"},{\"entry\":\"10\"}]"));
            var rows = J.List(J.Parse("[{\"distance\":3,\"tee\":2,\"made\":1},{\"distance\":3,\"tee\":2,\"made\":1},{\"distance\":9,\"tee\":3,\"made\":0},{\"distance\":9,\"tee\":3,\"made\":1}]"));
            var found = LessonFindings.Candidates(session, sessionPutts, LessonFindings.Grid(rows), new List<AnalysisMath.Putt>(), false);
            var byLabel = new Dictionary<string, string>();
            foreach (var f in found) byLabel[f.label] = f.value;
            Assert.AreEqual("C", byLabel["Session grade"]);
            Assert.AreEqual("2 of 3", byLabel["Session putts made"]);
            Assert.AreEqual("75%", byLabel["Make % (last 30 days)"]);
            Assert.IsTrue(byLabel.ContainsKey("Best spot"));
            Assert.IsTrue(byLabel.ContainsKey("Worst spot"));
            Assert.IsFalse(byLabel.ContainsKey("Typical pace"), "no pace without putts that carry a rollout");
        }

        [Test]
        public void CandidatesAreEmptyWithNoData()
        {
            Assert.AreEqual(0, LessonFindings.Candidates(null, null, null, null, false).Count);
        }

        [Test]
        public void PayloadSendsOnlyThePicked()
        {
            var candidates = new List<(string label, string value)> { ("Make % (last 30 days)", "72%"), ("Worst spot", "6ft Right · 50% made") };
            Assert.IsNull(LessonFindings.Payload(candidates, new HashSet<string>()));
            List<object> rows = LessonFindings.Payload(candidates, new HashSet<string> { "Worst spot" });
            Assert.AreEqual(1, rows.Count);
            object back = J.Parse(J.Write(rows));
            Assert.AreEqual("Worst spot", J.Str(J.List(back)[0], "label"));
            Assert.AreEqual("6ft Right · 50% made", J.Str(J.List(back)[0], "value"));
        }
    }
}
