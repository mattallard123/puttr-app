// Home's lead card and feed, the Play tab's search, League Night's team suggestion, and the Profiles PIN and
// add-player wording: the parts that decide what is shown, checked without a screen.
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class HomePlayLeagueProfilesTests
    {
        [Test]
        public void HomeWeekStartsOnSundayLikeTheApp()
        {
            var calendar = new Dictionary<string, int> { { "2026-11-15", 40 }, { "2026-11-17", 12 }, { "2026-11-14", 9 } };
            // Wednesday 18 November 2026: Sunday the 15th is this week's first day, Saturday the 14th was last week.
            bool[] dots = HomeScreen.WeekDots(calendar, new DateTime(2026, 11, 18), 0, out int today, true);
            Assert.AreEqual(3, today);
            Assert.AreEqual(new[] { true, false, true, false, false, false, false }, dots);
            Assert.IsTrue(HomeScreen.WeekDots(calendar, new DateTime(2026, 11, 18), 1, out _, true)[3]);
            HomeScreen.WeekDots(null, new DateTime(2026, 11, 22), 0, out int sunday, true);
            Assert.AreEqual(0, sunday);
            // Monday first is unchanged.
            HomeScreen.WeekDots(null, new DateTime(2026, 11, 22), 0, out int mondayFirst);
            Assert.AreEqual(6, mondayFirst);
        }

        [Test]
        public void HomeSessionIsTheImproveSession()
        {
            var grid = AnalysisMath.Grid(new List<object>());
            object coach = J.Parse("{\"dailyGoal\":12,\"lastPracticeDate\":\"2026-11-18T09:12:00.000Z\"}");
            var now = new DateTime(2026, 11, 18, 15, 0, 0);
            List<TournamentMath.Dealt> home = TodaysSession.Build(grid, coach, now, out string title, out _);
            // ImproveScreen.Today: Clamp(dailyGoal, 3, 30) putts from AnalysisMath.Session seeded with the day of the year.
            List<TournamentMath.Dealt> improve = AnalysisMath.Session(grid, 12, new System.Random(now.DayOfYear), out string improveTitle, out _);
            Assert.AreEqual(improve.Count, home.Count);
            for (int i = 0; i < home.Count; i++) { Assert.AreEqual(improve[i].Feet, home[i].Feet); Assert.AreEqual(improve[i].Tee, home[i].Tee); }
            Assert.AreEqual(improveTitle, title);
            Assert.IsTrue(TodaysSession.DoneToday(coach, now));
            Assert.IsFalse(TodaysSession.DoneToday(coach, now.AddDays(1)));
            Assert.AreEqual(20, TodaysSession.Count(J.Parse("{}")));
            Assert.AreEqual(30, TodaysSession.Count(J.Parse("{\"dailyGoal\":80}")));
            Assert.AreEqual(3, TodaysSession.Count(J.Parse("{\"dailyGoal\":1}")));
        }

        [Test]
        public void BriefGroupsThePuttsBySpot()
        {
            var putts = new List<TournamentMath.Dealt>
            {
                new TournamentMath.Dealt { Feet = 6, Tee = 2 }, new TournamentMath.Dealt { Feet = 7, Tee = 3 },
                new TournamentMath.Dealt { Feet = 6, Tee = 2 }, new TournamentMath.Dealt { Feet = 4, Tee = 1 },
            };
            var groups = TodaysSession.Groups(putts);
            Assert.AreEqual(3, groups.Count);
            Assert.AreEqual((6, 2, 2), groups[0]);
            Assert.AreEqual((7, 3, 1), groups[1]);
            Assert.AreEqual((4, 1, 1), groups[2]);
            Assert.AreEqual("left to right", TodaysSession.Break(1));
            Assert.AreEqual("straight", TodaysSession.Break(2));
            Assert.AreEqual("right to left", TodaysSession.Break(3));
            Assert.AreEqual("Varied", TodaysSession.Distance(putts));
            Assert.AreEqual("Mixed", TodaysSession.BreakTile(putts));
            var same = new List<TournamentMath.Dealt> { new TournamentMath.Dealt { Feet = 6, Tee = 1 }, new TournamentMath.Dealt { Feet = 6, Tee = 1 } };
            Assert.AreEqual("6 ft", TodaysSession.Distance(same));
            Assert.AreEqual("L→R", TodaysSession.BreakTile(same));
        }

        [Test]
        public void FeedRowsSayWhatWasPlayed()
        {
            var rows = J.List(J.Parse("[{\"gameType\":\"COACHSESSION\",\"title\":\"Session\"},{\"gameType\":\"OPEN\",\"title\":\"Practice\",\"hasLeaderboard\":0},{\"gameType\":\"MAJORLEAGUE\",\"title\":\"Major League\",\"hasLeaderboard\":1,\"score\":18}]"));
            List<object> clean = Feed.Clean(rows);
            Assert.AreEqual(2, clean.Count);
            Assert.AreEqual("played Practice", Feed.What(clean[0]));
            StringAssert.StartsWith("scored ", Feed.What(clean[1]));
            StringAssert.EndsWith(" on Major League", Feed.What(clean[1]));
            // A game with a leaderboard but no score yet was only played.
            Assert.AreEqual("played Ladder", Feed.What(J.Parse("{\"title\":\"Ladder\",\"hasLeaderboard\":1}")));
        }

        [Test]
        public void PlaySearchMatchesTitleOrDescription()
        {
            object game = J.Parse("{\"title\":\"Balloon Pop\",\"description\":\"Pop balloons by making putts.\"}");
            Assert.IsTrue(GameSearch.Matches(game, ""));
            Assert.IsTrue(GameSearch.Matches(game, "   "));
            Assert.IsTrue(GameSearch.Matches(game, "ball"));
            Assert.IsTrue(GameSearch.Matches(game, " BALLOON "));
            Assert.IsTrue(GameSearch.Matches(game, "making"));
            Assert.IsFalse(GameSearch.Matches(game, "ladder"));
            Assert.IsFalse(GameSearch.Matches(J.Parse("{}"), "x"));
        }

        [Test]
        public void LeagueSuggestsTheRegionsTeam()
        {
            Assert.AreEqual("lions", League.Suggest("GB", ""));
            Assert.AreEqual("lions", League.Suggest("uk", null));
            Assert.AreEqual("mounties", League.Suggest("CA", "Austin, TX"));
            Assert.AreEqual("mavericks", League.Suggest("US", "Austin, TX"));
            Assert.AreEqual("renegades", League.Suggest("US", "Miami, FL"));
            Assert.AreEqual("noreasters", League.Suggest("US", "Boston, MA"));
            Assert.AreEqual("blizzards", League.Suggest("US", "Chicago, IL"));
            Assert.IsNull(League.Suggest("US", ""));            // as the app: no state, no guess
            Assert.IsNull(League.Suggest("FR", "Paris, TX"));   // another country: none of the six
            // No country from the phone: the home town decides.
            Assert.AreEqual("mavericks", League.Suggest("", "Austin, TX"));
            Assert.AreEqual("mounties", League.Suggest(null, "Toronto, ON"));
            Assert.AreEqual("lions", League.Suggest("IV", "Leeds, England"));
            Assert.IsNull(League.Suggest("", "Somewhere"));
        }

        [Test]
        public void ProfilesPinNeedsToBeEnteredTwice()
        {
            Assert.AreEqual("confirm", Profiles.NextPinStep("new", null, "1234", out string e1));
            Assert.IsNull(e1);
            Assert.AreEqual("save", Profiles.NextPinStep("confirm", "1234", "1234", out string e2));
            Assert.IsNull(e2);
            Assert.AreEqual("new", Profiles.NextPinStep("confirm", "1234", "4321", out string e3));
            Assert.AreEqual("Those did not match. Try again.", e3);
        }

        [Test]
        public void AddPlayerErrorsComeFromTheApi()
        {
            Assert.AreEqual("All profiles are in use", Profiles.AddError("http_409").title);
            Assert.AreEqual("Remove a player to add another.", Profiles.AddError("http_409").body);
            Assert.AreEqual("Check the name", Profiles.AddError("http_400").title);
            Assert.AreEqual("Ask the account owner", Profiles.AddError("http_403").title);
            Assert.AreEqual("Couldn't add the player", Profiles.AddError("offline").title);
            foreach (string why in new[] { "http_409", "http_400", "http_403", "offline" })
                StringAssert.DoesNotContain("5", Profiles.AddError(why).body);
        }
    }
}
