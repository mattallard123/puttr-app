using System.Linq;
// The parts of the shell that can be wrong without anyone seeing it on screen: what is sent to
// the API, how its loosely typed rows are read, and what never reaches a log.
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Puttr;

namespace PuttrApp.Tests
{
    public class AppTests
    {
        [Test]
        public void JsonWritesWhatTheApiReads()
        {
            var body = new Dictionary<string, object> { { "userId", "abc" }, { "players", 1 }, { "made", true }, { "note", "a \"quoted\"\nline" }, { "none", null }, { "half", 0.5 } };
            object back = J.Parse(J.Write(body));
            Assert.AreEqual("abc", J.Str(back, "userId"));
            Assert.AreEqual(1, J.Int(back, "players"));
            Assert.IsTrue(J.Flag(back, "made"));
            Assert.AreEqual("a \"quoted\"\nline", J.Str(back, "note"));
            Assert.AreEqual(0.5, J.Num(back, "half"), 1e-9);
            Assert.AreEqual("fallback", J.Str(back, "none", "fallback"));
        }

        [Test]
        public void JsonWritesLists()
        {
            Assert.AreEqual("[1,2,\"x\"]", J.Write(new List<object> { 1, 2, "x" }));
        }

        [Test]
        public void RowsAreReadForgivingly()
        {
            // MySQL sends some numbers as strings, and single rows as one-row arrays.
            object row = J.First(J.Parse("[{\"score\":\"42\",\"isMember\":1,\"title\":7}]"));
            Assert.AreEqual(42, J.Int(row, "score"));
            Assert.IsTrue(J.Flag(row, "isMember"));
            Assert.AreEqual("7", J.Str(row, "title"));
            Assert.AreEqual(0, J.List(null).Count);
            Assert.IsNull(J.First(J.Parse("[]")));
        }

        [Test]
        public void UidComesFromTheIdToken()
        {
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"user_id\":\"player-123\",\"sub\":\"other\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.AreEqual("player-123", Auth.UidFromIdToken("header." + payload + ".signature"));
            Assert.IsNull(Auth.UidFromIdToken("not a token"));
        }

        [Test]
        public void SignInProblemsReadAsSentences()
        {
            StringAssert.Contains("do not match", Auth.Friendly(400, "INVALID_LOGIN_CREDENTIALS"));
            StringAssert.Contains("No connection", Auth.Friendly(0, null));
            StringAssert.Contains("Too many", Auth.Friendly(400, "TOO_MANY_ATTEMPTS_TRY_LATER : Access disabled"));
        }

        [Test]
        public void PuttsAreSavedInTheAppsFormat()
        {
            var path = new List<BallPoint> { new BallPoint(320, 400, 30), new BallPoint(318, 250, 22) };
            Assert.AreEqual("[[320,400,30],[318,250,22]]", RoundSaver.PathJson(path));
            Assert.AreEqual("[]", RoundSaver.PathJson(null));
            Assert.AreEqual("[320,100,110]", RoundSaver.HoleJson(new Hole(320, 100, 110)));
            // Nothing in either that could close the quotes the API wraps them in.
            StringAssert.DoesNotContain("'", RoundSaver.PathJson(path));
        }

        [Test]
        public void OnlyOnFireIsPlayable()
        {
            Assert.AreEqual("On Fire", GameBridge.ArcadeNameFor(J.Parse("{\"gameType\":\"ARCADESTREAK\"}")));
            Assert.AreEqual("On Fire", GameBridge.ArcadeNameFor(J.Parse("{\"gameType\":\"onfire\"}")));
            Assert.IsNull(GameBridge.ArcadeNameFor(J.Parse("{\"gameType\":\"CENTERCUT\"}")));
        }

        [Test]
        public void GreetingUsesTheFirstName()
        {
            Assert.AreEqual("Good morning, Sam", HomeScreen.Greeting(8, "Sam"));
            Assert.AreEqual("Good afternoon", HomeScreen.Greeting(13, ""));
            Assert.AreEqual("Good evening, Sam", HomeScreen.Greeting(21, " Sam "));
            Assert.AreEqual("Sam", HomeScreen.FirstName(J.Parse("{\"displayName\":\"Sam Rivera\"}")));
            Assert.AreEqual("Samantha", HomeScreen.FirstName(J.Parse("{\"firstName\":\"Samantha\",\"displayName\":\"Sam Rivera\"}")));
        }

        [Test]
        public void TheFirebaseKeyNeverReachesTheLog()
        {
            Perf.Request("POST", "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=SECRET", 200, 120f);
            StringAssert.DoesNotContain("SECRET", Perf.Summary());
            StringAssert.Contains("signInWithPassword", Perf.Summary());
        }

        [Test]
        public void GoogleSignInAsksWithPkce()
        {
            // RFC 7636, appendix B.
            Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", SocialSignIn.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
            string url = SocialSignIn.GoogleUrl("123-abc.apps.googleusercontent.com", "com.googleusercontent.apps.123-abc", "verifier");
            StringAssert.Contains("code_challenge_method=S256", url);
            StringAssert.Contains("redirect_uri=com.googleusercontent.apps.123-abc%3a%2foauth2redirect", url.ToLowerInvariant());
            StringAssert.DoesNotContain("verifier", url);
            Assert.AreEqual("4/abc def", SocialSignIn.Query("com.x:/oauth2redirect?state=1&code=4%2Fabc%20def&scope=openid", "code"));
            Assert.AreEqual("", SocialSignIn.Query("com.x:/oauth2redirect", "code"));
        }

        [Test]
        public void AppleSignInSendsTheNonceBothWays()
        {
            // Apple is given the hash; Firebase is shown the nonce itself.
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SocialSignIn.Sha256Hex("abc"));
            Assert.AreEqual("id_token=a.b.c&providerId=apple.com&nonce=raw", Auth.IdpPostBody("apple.com", "a.b.c", "raw"));
            Assert.AreEqual("id_token=a.b.c&providerId=google.com", Auth.IdpPostBody("google.com", "a.b.c", null));
            Assert.AreEqual(32, SocialSignIn.Random(32).Length);
            Assert.AreNotEqual(SocialSignIn.Random(32), SocialSignIn.Random(32));
        }

        [Test]
        public void NativeFirebaseAnswersAreRead()
        {
            Assert.IsTrue(FirebaseNative.Parse("7|ok|uid123|known", out string id, out string value, out string error));
            Assert.AreEqual("7", id); Assert.AreEqual("uid123|known", value); Assert.IsNull(error);
            // An id token is dots and dashes, never a bar, but a message may hold anything.
            Assert.IsTrue(FirebaseNative.Parse("8|error|ERROR_WRONG_PASSWORD|The password is invalid | try again", out id, out value, out error));
            Assert.AreEqual("ERROR_WRONG_PASSWORD", error); Assert.IsNull(value);
            Assert.IsFalse(FirebaseNative.Parse("", out id, out value, out error));
            StringAssert.Contains("do not match", Auth.Friendly(400, "ERROR_WRONG_PASSWORD"));
            StringAssert.Contains("No connection", Auth.Friendly(400, "ERROR_NETWORK_REQUEST_FAILED"));
            StringAssert.Contains("already linked", Auth.Friendly(400, "ERROR_ACCOUNT_EXISTS_WITH_DIFFERENT_CREDENTIAL"));
            StringAssert.Contains("Too many", Auth.Friendly(400, "ERROR_TOO_MANY_REQUESTS"));
        }

        static PracticePutt Made(int feet = 6) => new PracticePutt { Feet = feet, Tee = 2, Made = true, Reached = true, Rollout = 4 };
        static PracticePutt Missed(MissSide side, bool reached = true, int feet = 6) => new PracticePutt { Feet = feet, Tee = 2, Made = false, Reached = reached, Side = side, Rollout = reached ? 10 : (double?)null };

        [Test]
        public void PracticeCountsWhatFreePuttShows()
        {
            var s = new PracticeSession(1);
            foreach (PracticePutt p in new[] { Made(), Made(), Missed(MissSide.Left), Made(8), Made(8), Made(8), Missed(MissSide.None, false, 8) }) s.Add(p);
            Assert.AreEqual(7, s.Total);
            Assert.AreEqual(5, s.Makes);
            Assert.AreEqual(71, s.MakePercent);
            Assert.AreEqual(0, s.Streak);      // the last one missed
            Assert.AreEqual(3, s.BestStreak);
            s.Misses(out int left, out int shortOnes, out int right);
            Assert.AreEqual(1, left); Assert.AreEqual(1, shortOnes); Assert.AreEqual(0, right);
            var rows = s.ByDistance();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual((6, 2, 3), rows[0]);
            Assert.AreEqual((8, 3, 4), rows[1]);
            Assert.AreEqual(30.0 / 6.0, s.AverageRollout.Value, 1e-9); // five at 4 in, one at 10; the unreadable one is left out
            Assert.AreEqual("Missed left", Missed(MissSide.Left).Headline);
            Assert.AreEqual("4.0 in past", Made().Pace);
            Assert.AreEqual("Short", Missed(MissSide.None, false).Headline);
        }

        [Test]
        public void PracticeGoalsAreMetOnce()
        {
            var streak = new PracticeSession(1) { Goal = PracticeGoal.Streak, Target = 3 };
            Assert.IsFalse(streak.Add(Made())); Assert.IsFalse(streak.Add(Made()));
            Assert.IsTrue(streak.Add(Made()));            // this putt met it
            Assert.IsFalse(streak.Add(Missed(MissSide.Right)));
            Assert.IsTrue(streak.GoalMet);                // and a miss afterwards does not undo it
            Assert.AreEqual(1f, streak.Progress);

            var total = new PracticeSession(1) { Goal = PracticeGoal.Total, Target = 2 };
            Assert.IsFalse(total.Add(Missed(MissSide.Left)));
            Assert.AreEqual(0.5f, total.Progress);
            Assert.IsTrue(total.Add(Made()));
            Assert.IsFalse(new PracticeSession(1).Add(Made())); // just putting: no goal to meet
        }

        [Test]
        public void RandomTeesStayOnTheMatAndMove()
        {
            var s = new PracticeSession(42) { RandomTees = true, RangeMin = 1, RangeMax = 30 }; // a range wider than the mat
            for (int i = 0; i < 300; i++)
            {
                int feet = s.Feet, tee = s.Tee;
                s.Deal();
                Assert.That(s.Feet, Is.InRange(3, 11));
                Assert.That(s.Tee, Is.InRange(1, 3));
                Assert.IsFalse(s.Feet == feet && s.Tee == tee, "the same spot twice running");
            }
            var narrow = new PracticeSession(7) { RangeMin = 9, RangeMax = 5 }; // the wrong way round
            for (int i = 0; i < 50; i++) { narrow.Deal(); Assert.That(narrow.Feet, Is.InRange(5, 9)); }
        }

        [Test]
        public void FreePuttIsPlayable()
        {
            Assert.IsTrue(GameBridge.Playable(J.Parse("{\"gameType\":\"OPEN\"}")));
            Assert.IsTrue(GameBridge.Playable(J.Parse("{\"gameType\":\"ARCADESTREAK\"}")));
            Assert.IsTrue(GameBridge.Playable(J.Parse("{\"gameType\":\"CENTERCUT\"}")));   // a rule game since 2026-11-17
            Assert.IsFalse(GameBridge.Playable(J.Parse("{\"gameType\":\"NOTAGAME\"}")));
        }

        [Test]
        public void MembershipReadsAsInTheApp()
        {
            var now = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual("Unlock all games and features", ProfileMath.Membership(J.Parse("{\"isMember\":0}"), now));
            Assert.AreEqual("Active", ProfileMath.Membership(J.Parse("{\"isMember\":1}"), now));
            Assert.AreEqual("Renews Mar 14, 2027", ProfileMath.Membership(J.Parse("{\"isMember\":1,\"subscriptionExpiration\":\"2027-03-14T00:00:00.000Z\",\"subscriptionType\":\"annual\"}"), now));
            Assert.AreEqual("Ends Mar 14, 2027", ProfileMath.Membership(J.Parse("{\"isMember\":1,\"subscriptionExpiration\":\"2027-03-14T00:00:00.000Z\",\"membershipSource\":\"store\",\"subscriptionCancelled\":1}"), now));
            Assert.AreEqual("Club year ends Jan 5, 2027", ProfileMath.Membership(J.Parse("{\"isMember\":1,\"trialExpiration\":\"2027-01-05T00:00:00.000Z\"}"), now));
            Assert.AreEqual("Junior membership through Jan 5, 2027", ProfileMath.Membership(J.Parse("{\"isMember\":1,\"trialExpiration\":\"2027-01-05T00:00:00.000Z\",\"membershipSource\":\"junior\"}"), now));
            Assert.AreEqual("Active", ProfileMath.Membership(J.Parse("{\"isMember\":1,\"trialExpiration\":\"2025-01-05T00:00:00.000Z\"}"), now)); // a date that has passed is not shown
        }

        [Test]
        public void ProfileNumbersReadAsInTheApp()
        {
            Assert.AreEqual("+2.1", ProfileMath.Handicap(J.Parse("{\"handicap\":-2.1}")));
            Assert.AreEqual("8.4", ProfileMath.Handicap(J.Parse("{\"handicap\":8.4}")));
            Assert.AreEqual("\u2013", ProfileMath.Handicap(J.Parse("{\"handicap\":null}")));
            Assert.AreEqual(66, ProfileMath.MadePercent(J.Parse("{\"totalMade\":5391,\"totalPutts\":8214}")));
            Assert.AreEqual(0, ProfileMath.MadePercent(J.Parse("{\"totalMade\":1,\"totalPutts\":1}")));
            Assert.AreEqual("60,210", ProfileMath.Number(60210));
            StringAssert.StartsWith("Newport 2", ProfileMath.PutterDetail(J.Parse("{\"model\":\"Newport 2\",\"length\":\"34\",\"head\":\"null\",\"shaft\":\"\"}")));
            StringAssert.EndsWith("34\"", ProfileMath.PutterDetail(J.Parse("{\"model\":\"Newport 2\",\"length\":\"34\",\"head\":\"null\",\"shaft\":\"\"}")));
            Assert.AreEqual("3 days left", ProfileMath.TimeLeft(new DateTime(2026, 11, 19, 12, 0, 0), new DateTime(2026, 11, 16, 0, 0, 0)));
            Assert.AreEqual("Ended", ProfileMath.TimeLeft(new DateTime(2026, 11, 1), new DateTime(2026, 11, 16)));
        }

        [Test]
        public void ProfileChartsAddUp()
        {
            // SUM columns arrive as strings; the calendar arrives as a JSON string inside the row.
            object stats = J.Parse("{\"calendar_year\":\"[{\\\"count\\\":10,\\\"date\\\":\\\"2026-11-03T00:00:00.000Z\\\"},{\\\"count\\\":5,\\\"date\\\":\\\"2026-11-10T00:00:00.000Z\\\"},{\\\"count\\\":7,\\\"date\\\":\\\"2026-09-30T00:00:00.000Z\\\"}]\"}");
            var calendar = ProfileMath.Calendar(stats);
            Assert.AreEqual(3, calendar.Count);
            var months = ProfileMath.ByMonth(calendar, new DateTime(2026, 11, 16));
            Assert.AreEqual(7, months.Count);
            Assert.AreEqual(("Nov", 15), months[6]);
            Assert.AreEqual(("Oct", 0), months[5]);
            Assert.AreEqual(("Sep", 7), months[4]);
            Assert.AreEqual("May", months[0].month);
            int[] breaks = ProfileMath.Breaks(J.List(J.Parse("[{\"ct\":3,\"tee\":1,\"made\":1},{\"ct\":1,\"tee\":1,\"made\":0},{\"ct\":2,\"tee\":3,\"made\":0}]")));
            Assert.AreEqual(new[] { 75, -1, 0 }, breaks);
        }

        [Test]
        public void HomeShowsThisWeekFromMonday()
        {
            var calendar = new Dictionary<string, int> { { "2026-11-16", 12 }, { "2026-11-17", 0 }, { "2026-11-18", 30 }, { "2026-11-15", 40 } };
            // Thursday 19 November 2026. Sunday the 15th was last week.
            bool[] dots = HomeScreen.WeekDots(calendar, new DateTime(2026, 11, 19), 0, out int today);
            Assert.AreEqual(3, today);
            Assert.AreEqual(new[] { true, false, true, false, false, false, false }, dots);
            Assert.IsTrue(HomeScreen.WeekDots(calendar, new DateTime(2026, 11, 19), 4, out _)[3]); // today counts from the first putt
            Assert.AreEqual(6, ((Func<int>)(() => { HomeScreen.WeekDots(null, new DateTime(2026, 11, 22), 0, out int sunday); return sunday; }))());
        }

        [Test]
        public void RoundsScoreAsTheAppDoes()
        {
            // Tournament: a scored make (more than one point in the hole) scores its feet, wherever it came in; the back door scores nothing.
            Assert.AreEqual(7, RoundSession.Score(RoundRule.Feet, 7, 2, 4, 30));
            Assert.AreEqual(0, RoundSession.Score(RoundRule.Feet, 7, 2, 4, 170));
            Assert.AreEqual(0, RoundSession.Score(RoundRule.Feet, 7, 2, 1, 0)); // one point in the hole is saved as made but scores nothing
            // League Night: the distance, less the share it came in off its line.
            Assert.AreEqual(9, RoundSession.Score(RoundRule.CenterCut, 9, 2, 3, 0));
            Assert.AreEqual(4.5, RoundSession.Score(RoundRule.CenterCut, 9, 2, 3, 45), 1e-9);
            // From the left tee the true line is a few degrees off center (getTeeEntry).
            double line = AppPutt.TeeEntry(4, 1);
            Assert.Less(line, 0);
            Assert.AreEqual(4, RoundSession.Score(RoundRule.CenterCut, 4, 1, 2, line), 1e-9);
            // No entry seen: the app reads its angle as 0, so a make from a side tee loses a little.
            Assert.AreEqual(4 * (1 - Math.Abs(line) / 90), RoundSession.Score(RoundRule.CenterCut, 4, 1, 2, 0), 1e-9);
        }

        [Test]
        public void TournamentRoundsReadFromTheApi()
        {
            object round = J.Parse("{\"difficulty\":\"7.0\",\"puttData\":\"[{\\\"distance\\\":4,\\\"tee\\\":2},{\\\"distance\\\":11,\\\"tee\\\":3},{\\\"distance\\\":14,\\\"tee\\\":1}]\"}");
            var putts = TournamentMath.Putts(round);
            Assert.AreEqual(2, putts.Count); // 14 ft is off the mat and is not putted
            Assert.AreEqual(15, TournamentMath.Possible(new List<object> { round }));
            Assert.AreEqual("Hard", TournamentMath.Level(round));
            Assert.AreEqual(1, TournamentMath.CurrentRound(J.Parse("{\"r1_score\":null}")));
            Assert.AreEqual(3, TournamentMath.CurrentRound(J.Parse("{\"r1_score\":20,\"r2_score\":0,\"r3_score\":null}")));
            Assert.AreEqual(5, TournamentMath.CurrentRound(J.Parse("{\"r1_score\":1,\"r2_score\":2,\"r3_score\":3,\"r4_score\":4}")));
            Assert.AreEqual(1, TournamentMath.CurrentRound(null));
            Assert.AreEqual("https://puttr.imgix.net/tournaments/Nov_26_Prize.jpg?w=400&h=400", TournamentMath.Sized("https://storage.googleapis.com/images.puttr.co/tournaments/Nov_26_Prize.jpg", 400));
            Assert.AreEqual("https://media0.giphy.com/media/146YfoNq3cuM7u/480w_s.jpg", TournamentMath.GiphyStill("https://media0.giphy.com/media/146YfoNq3cuM7u/giphy.gif?cid=abc&rid=giphy.gif"));
            Assert.AreEqual("https://media0.giphy.com/media/146YfoNq3cuM7u/480w_s.jpg", TournamentMath.Art(J.Parse("{\"heroImage\":\"https://media0.giphy.com/media/146YfoNq3cuM7u/giphy.gif\"}"), 400));
            var nov = J.Parse("{\"startDate\":\"2026-11-01T00:00:00.000Z\",\"endDate\":\"2026-11-30T23:59:59.000Z\"}");
            Assert.AreEqual(TournamentMath.State.Upcoming, TournamentMath.StateOf(nov, new DateTime(2026, 10, 31, 12, 0, 0)));
            Assert.AreEqual(TournamentMath.State.Active, TournamentMath.StateOf(nov, new DateTime(2026, 11, 30, 23, 0, 0)));
            Assert.AreEqual(TournamentMath.State.Ended, TournamentMath.StateOf(nov, new DateTime(2026, 12, 1, 0, 1, 0)));
        }

        [Test]
        public void ARoundIsSavedAsTheApiStoresIt()
        {
            var session = new RoundSession { Putts = RoundSession.Shuffled(new System.Random(5)) };
            Assert.AreEqual(9, session.Putts.Count);
            var feet = new HashSet<int>();
            foreach (var putt in session.Putts) { feet.Add(putt.Feet); Assert.That(putt.Tee, Is.InRange(1, 3)); }
            Assert.AreEqual(9, feet.Count); // one from each distance, 3 to 11
            Assert.AreEqual(63, session.Possible);
            session.Scores.Add(4); session.Scores.Add(0); session.Scores.Add(6.25);
            Assert.AreEqual(10.25, session.Total, 1e-9);
            Assert.AreEqual(2, session.Made);
            var round = new CompetitionRound(CompetitionRound.Kind.League, session, "472");
            object state = J.Parse(round.StateJson());
            Assert.AreEqual(3, J.Int(state, "puttCounter"));
            Assert.AreEqual(3, J.List(J.List(J.Get(state, "playerPutts"))[0]).Count);
            StringAssert.Contains("\"distance\"", session.DealtJson());
            Assert.AreEqual("6.25", RoundSession.Points(6.25));
            Assert.AreEqual("4", RoundSession.Points(4));
        }

        [Test]
        public void ChallengeTiersReadAsInTheApp()
        {
            var levels = J.List(J.Parse("[{\"title\":\"BRONZE\",\"number\":500},{\"title\":\"SILVER\",\"number\":1000},{\"title\":\"GOLD\",\"number\":1500}]"));
            Assert.AreEqual(50, ChallengeMath.Percent(250, levels, out int reached, out double goal));
            Assert.AreEqual(-1, reached); Assert.AreEqual(500, goal);
            Assert.AreEqual(100, ChallengeMath.Percent(500, levels, out reached, out _)); // equal is not past: still working on bronze
            Assert.AreEqual(-1, reached);
            Assert.AreEqual(75, ChallengeMath.Percent(750, levels, out reached, out goal));
            Assert.AreEqual(0, reached); Assert.AreEqual(1000, goal);
            Assert.AreEqual(100, ChallengeMath.Percent(2000, levels, out reached, out _));
            Assert.AreEqual(2, reached);
            Assert.AreEqual("feet", ChallengeMath.Unit("DISTANCE"));
        }

        [Test]
        public void LeagueWeeksReadAsInTheApp()
        {
            object season = J.Parse("{\"weeks\":[{\"weekNumber\":1,\"startsAt\":\"2026-11-02T00:00:00\",\"endsAt\":\"2026-11-08T23:59:59\"},{\"weekNumber\":2,\"startsAt\":\"2026-11-09T00:00:00\",\"endsAt\":\"2026-11-15T23:59:59\"}]}");
            Assert.AreEqual(2, J.Int(League.CurrentWeek(season, new DateTime(2026, 11, 12)), "weekNumber"));
            Assert.AreEqual(2, J.Int(League.CurrentWeek(season, new DateTime(2026, 12, 1)), "weekNumber")); // after the last week: the last week
            Assert.IsTrue(League.Live(League.CurrentWeek(season, new DateTime(2026, 11, 12)), new DateTime(2026, 11, 12)));
            Assert.AreEqual("3d left", League.Countdown(new DateTime(2026, 11, 15, 23, 59, 59), new DateTime(2026, 11, 12, 12, 0, 0)));
            Assert.AreEqual("02:30:05 left", League.Countdown(new DateTime(2026, 11, 15, 14, 30, 5), new DateTime(2026, 11, 15, 12, 0, 0)));
            Assert.AreEqual(6, League.AllSix(J.List(J.Parse("[{\"team\":\"lions\",\"total\":12}]"))).Count);
            Assert.AreEqual("The Mavericks", League.TeamOf("mavericks").Name);
            Assert.AreEqual("3rd", League.Ordinal(3)); Assert.AreEqual("11th", League.Ordinal(11)); Assert.AreEqual("22nd", League.Ordinal(22));
            Assert.AreEqual("CENTERCUT_456", Capture.Channel("CENTERCUT", "456", null));
            Assert.AreEqual("TOURNAMENT_123_0", Capture.Channel("TOURNAMENT", "123", 0));
            Assert.AreEqual("3 days ago", Ago.Words(new DateTime(2026, 11, 13), new DateTime(2026, 11, 16)));
        }

        [Test]
        public void AnalysisGridWeighsAgainstTheTour()
        {
            var grid = AnalysisMath.Grid(J.List(J.Parse("[{\"distance\":3,\"tee\":2,\"total\":20,\"made\":\"19\"},{\"distance\":11,\"tee\":1,\"total\":10,\"made\":\"3\"},{\"distance\":11,\"tee\":1,\"total\":10,\"made\":\"3\"},{\"distance\":14,\"tee\":2,\"total\":5,\"made\":\"5\"}]")));
            Assert.AreEqual(27, grid.Count);
            AnalysisMath.Cell eleven = grid[(11 - 3) * 3 + 0];
            Assert.AreEqual(20, eleven.Total); // two rows for the same spot add up
            Assert.AreEqual(30, eleven.Percent);
            Assert.AreEqual(49, eleven.Weighted); // 30 of the Tour's 61
            Assert.AreEqual(96, grid[1].Weighted); // 95% at 3 ft against 99
            Assert.AreEqual(40, AnalysisMath.Putts(grid)); // 14 ft is off the mat
            AnalysisMath.Extremes(grid, out var best, out var worst);
            Assert.AreEqual(3, best.Feet); Assert.AreEqual(11, worst.Feet);
            Assert.AreEqual(11, AnalysisMath.WorstFirst(grid)[0].Feet);
            Assert.AreEqual("11 ft Left", worst.Name);
        }

        [Test]
        public void EntryBandsReadAsInTheApp()
        {
            Assert.AreEqual(2, AnalysisMath.Band(0f));
            Assert.AreEqual(2, AnalysisMath.Band(-17f));
            Assert.AreEqual(3, AnalysisMath.Band(30f));
            Assert.AreEqual(1, AnalysisMath.Band(-30f));
            Assert.AreEqual(4, AnalysisMath.Band(70f));
            Assert.AreEqual(0, AnalysisMath.Band(-70f));
            Assert.AreEqual(-1, AnalysisMath.Band(95f)); // the back door
            var putts = new List<AnalysisMath.Putt>
            {
                new AnalysisMath.Putt { Feet = 6, Made = true, HasEntry = true, OffLine = 2f },
                new AnalysisMath.Putt { Feet = 6, Made = true, HasEntry = true, OffLine = 120f },
                new AnalysisMath.Putt { Feet = 6, Made = false, Side = Puttr.MissSide.Left },
                new AnalysisMath.Putt { Feet = 6, Made = false, Side = Puttr.MissSide.Left },
                new AnalysisMath.Putt { Feet = 6, Made = false, Side = Puttr.MissSide.Right },
            };
            int[] bands = AnalysisMath.Entries(putts, out int backDoor, out int holed);
            Assert.AreEqual(1, bands[2]); Assert.AreEqual(1, backDoor); Assert.AreEqual(2, holed);
            AnalysisMath.Misses(putts, out int left, out int right);
            Assert.AreEqual(2, left); Assert.AreEqual(1, right);
            AnalysisMath.PushPull(putts, false, out int push, out int pull);
            Assert.AreEqual(2, pull); Assert.AreEqual(3, push); // right-handed: left is a pull; makes count by side of the line
            AnalysisMath.PushPull(putts, true, out push, out pull);
            Assert.AreEqual(2, push);
        }

        [Test]
        public void PaceAndPqsAddUp()
        {
            var putts = new List<AnalysisMath.Putt>();
            foreach (float r in new[] { -6f, 4f, 8f, 10f, 20f }) putts.Add(new AnalysisMath.Putt { Feet = 5, HasRollout = true, Rollout = r });
            AnalysisMath.Pace(putts, out int shortOnes, out int ideal, out int firm, out float median);
            Assert.AreEqual(1, shortOnes); Assert.AreEqual(3, ideal); Assert.AreEqual(1, firm); Assert.AreEqual(8f, median);
            Assert.AreEqual("6 in short", AnalysisMath.Rollout(-6f));
            Assert.AreEqual(7.2f, AnalysisMath.PaceAt(putts, 3, 5), 1e-4);
            Assert.IsTrue(float.IsNaN(AnalysisMath.PaceAt(putts, 9, 11)));

            // Tour level everywhere, every make dead on its line: PQS 100.
            var tour = new List<AnalysisMath.Putt>();
            for (int i = 0; i < 100; i++) tour.Add(new AnalysisMath.Putt { Feet = 3, Made = i < 99, HasEntry = true, OffLine = 0f });
            Assert.AreEqual(100, AnalysisMath.Pqs(tour, out float index, out float accuracy));
            Assert.AreEqual(1f, index, 1e-4); Assert.AreEqual(1f, accuracy, 1e-4);
            // Half the Tour's make %, holed putts 45 degrees off: 0.65 * 0.5 + 0.35 * 0.5 = 50.
            var half = new List<AnalysisMath.Putt>();
            for (int i = 0; i < 99; i++) half.Add(new AnalysisMath.Putt { Feet = 3, Made = i % 2 == 0 && i < 98, HasEntry = true, OffLine = 45f });
            Assert.AreEqual(50, AnalysisMath.Pqs(half, out _, out _));
            Assert.AreEqual(-1, AnalysisMath.Pqs(new List<AnalysisMath.Putt>(), out _, out _));
        }

        [Test]
        public void TodaysSessionWorksTheWeakestSpots()
        {
            var empty = AnalysisMath.Grid(new List<object>());
            var mapping = AnalysisMath.Session(empty, 9, new System.Random(1), out string title, out _);
            Assert.AreEqual("Map your green", title);
            Assert.AreEqual(9, mapping.Count);
            var grid = AnalysisMath.Grid(J.List(J.Parse("[{\"distance\":3,\"tee\":2,\"total\":40,\"made\":\"40\"},{\"distance\":9,\"tee\":3,\"total\":30,\"made\":\"6\"},{\"distance\":7,\"tee\":1,\"total\":2,\"made\":\"0\"}]")));
            var putts = AnalysisMath.Session(grid, 10, new System.Random(1), out title, out string why);
            StringAssert.StartsWith("Lock in 9 ft", title);
            StringAssert.Contains("20%", why);
            int nine = 0;
            foreach (var p in putts) { Assert.That(p.Feet, Is.InRange(3, 11)); if (p.Feet == 9 && p.Tee == 3) nine++; }
            Assert.GreaterOrEqual(nine, 3); // the weakest spot comes round again and again
            Assert.AreEqual(3, AnalysisMath.Session(grid, 1, new System.Random(1), out _, out _).Count); // never fewer than three
        }

        [Test]
        public void PlayTagsReadFromTheCatalogue()
        {
            Assert.AreEqual("Solo", GameTags.Who(J.Parse("{\"multiPlayer\":0}")));
            Assert.AreEqual("1 to 4", GameTags.Who(J.Parse("{\"multiPlayer\":3}")));
            Assert.AreEqual("Line", GameTags.Skill(J.Parse("{\"skills\":\"angle,pressure\"}")));
            Assert.AreEqual("Pace", GameTags.Skill(J.Parse("{\"skills\":\" speed\"}")));
            Assert.AreEqual("Practice", GameTags.Skill(J.Parse("{\"gameType\":\"OPEN\"}")));
            Assert.AreEqual("4 min", GameTags.Length(J.Parse("{\"putts\":9,\"multiPlayer\":0}")));
            Assert.AreEqual("Until you miss", GameTags.Length(J.Parse("{\"putts\":0,\"gameType\":\"ARCADESTREAK\"}")));
            var bests = GameTags.Bests(J.List(J.Parse("[{\"gameId\":7,\"score\":20},{\"gameId\":7,\"score\":35},{\"gameId\":8,\"score\":0}]")));
            Assert.AreEqual(35, bests["7"]);
            Assert.IsFalse(bests.ContainsKey("8"));
            Assert.IsTrue(Club.Member(J.Parse("{\"isMember\":1}")));
            Assert.IsTrue(Club.Member(J.Parse("{\"isMember\":0,\"isCoach\":1}")));
            Assert.IsFalse(Club.Member(J.Parse("{\"isMember\":0}")));
        }

        [Test]
        public void AppPuttMathsKeepsTheAppsQuirks()
        {
            double[] hole = { 320, 100, 110 };
            // JavaScript's rounding: halves go up, also below zero.
            Assert.AreEqual(-2, AppPutt.JsRound(-2.5)); Assert.AreEqual(3, AppPutt.JsRound(2.5));
            // The app's median is the upper middle of an even count.
            Assert.AreEqual(3, AppPutt.Median(new double[] { 4, 1, 3, 2 }));
            Assert.AreEqual("dead center", AppPutt.FmtRollout(0.04));
            Assert.AreEqual("2.5 ft past", AppPutt.FmtRollout(30));
            Assert.AreEqual("B+", AppPutt.Grade(77)); Assert.AreEqual("A+", AppPutt.Grade(96)); Assert.AreEqual("F", AppPutt.Grade(19.9));
            // A first point inside the hole level with its centre: the app's atan reads right as -90.
            int[][] level = { new[] { 330, 300, 30 }, new[] { 345, 100, 20 }, new[] { 330, 95, 18 } };
            Assert.AreEqual(-90, AppPutt.HoleEntry(level, hole, out bool found)); Assert.IsTrue(found);
            // One point in the hole at the end: saved as made, scored as a miss.
            int[][] grazed = { new[] { 320, 300, 30 }, new[] { 330, 250, 28 }, new[] { 360, 200, 26 }, new[] { 400, 150, 24 }, new[] { 420, 120, 22 }, new[] { 370, 110, 22 } };
            var v = AppPutt.Judge(grazed, hole, 6, 2);
            Assert.AreEqual(1, v.Count); Assert.IsTrue(v.SavedMade); Assert.IsFalse(v.Made);
            Assert.AreEqual(4.55, AppPutt.RemainingTravel(1.0), 1e-9);
        }

        [Test]
        public void PuttReadsAreGraded()
        {
            Assert.AreEqual(("0.4\u00B0 right", "Perfect", PuttGrades.Tone.Best), PuttGrades.Line(0.4));
            Assert.AreEqual("Great", PuttGrades.Line(-0.8).grade);
            Assert.AreEqual("1.5\u00B0 left", PuttGrades.Line(-1.5).text);
            Assert.AreEqual("Off line", PuttGrades.Line(3).grade);
            Assert.AreEqual("Not read", PuttGrades.Line(null).text);
            Assert.AreEqual("Ideal", PuttGrades.Pace(6, true).grade);
            Assert.AreEqual("Ideal", PuttGrades.Pace(-0.4, true).grade); // a make short of centre is noise
            Assert.AreEqual("Short", PuttGrades.Pace(-5, false).grade);
            Assert.AreEqual("Firm", PuttGrades.Pace(18, false).grade);
            Assert.AreEqual("Too firm", PuttGrades.Pace(40, true).grade);
            Assert.AreEqual("3.3 ft past", PuttGrades.Pace(40, true).text);
            var made = new AppPutt.Verdict { Count = 3, Description = "CENTER CUT" };
            Assert.AreEqual(("Center cut", "Perfect", PuttGrades.Tone.Best), PuttGrades.Entry(made, 7));
            var backDoor = new AppPutt.Verdict { Count = 3, Description = "BACK DOOR" };
            Assert.AreEqual("Does not count", PuttGrades.Entry(backDoor, 0).grade);
            var miss = new AppPutt.Verdict { Count = 0, MissSide = "LEFT" };
            Assert.AreEqual("Missed left", PuttGrades.Entry(miss, 0).text);
        }

        [Test]
        public void PlayerCountsReadAsInTheApp()
        {
            Assert.AreEqual("1 Player", GameScreen.Players(0));
            Assert.AreEqual("1 to 4 Players", GameScreen.Players(3));
        }

        [Test]
        public void FixturesAnswerEveryHomeCall()
        {
            foreach (string path in new[] { "/profile/", "/streaks/", "/profile/recentGames/", "/games/recommended/", "/profile/memberGames/", "/games/trending/" })
                Assert.IsNotNull(Fixtures.For(path), path);
            Assert.AreEqual("Sam", HomeScreen.FirstName(J.First(Fixtures.For("/profile/"))));
            Assert.AreEqual(40, J.List(Fixtures.For("/profile/memberGames/")).Count);
        }
            [Test]
        public void VertexPuttIsRead()
        {
            var packets = new byte[6][];
            for (int i = 0; i < 6; i++) { packets[i] = new byte[20]; packets[i][0] = (byte)i; }
            packets[1][3] = 60; packets[2][3] = 30;               // 0.6 s back, 0.3 s forward
            packets[1][4] = 0x80; packets[1][5] = 25;             // back rotation -2.5
            packets[1][14] = 0; packets[1][15] = 30;              // forward rotation +3.0
            packets[1][6] = 0; packets[1][7] = 150;               // 1.50 m/s
            packets[1][8] = 1; packets[1][9] = 44;                // 300 mm
            VertexPutt v = VertexPutt.Read(packets);
            Assert.AreEqual(2f, v.Rhythm, 1e-4);
            Assert.AreEqual(60f / 0.9f, v.Tempo, 1e-3);
            Assert.AreEqual(0.5f, v.FaceChange, 1e-4);
            Assert.AreEqual(1.5f, v.Speed, 1e-4);
            Assert.AreEqual(300f, v.BackLength, 1e-4);
            Assert.AreEqual("Open", VertexPutt.FaceWords(v.FaceChange));
            Assert.AreEqual("Square", VertexPutt.FaceWords(0.4f));
        }

        [Test]
        public void NotificationBodiesAreTidied()
        {
            var m = new Dictionary<string, object> { { "displayName", "Sam Lee" }, { "body", "Sam Lee beat your score of 12.34567 in On Fire" }, { "messageData", "{\"type\":\"GAME\",\"gameId\":7}" } };
            Assert.AreEqual("beat your score of 12.34 in On Fire", Inbox.Body(m));
            Assert.AreEqual("GAME", Inbox.Type(m));
            Assert.AreEqual("FillJoystick", Inbox.Icon("GAME"));
            Assert.AreEqual("GraduationCap", Inbox.Icon("LESSON_BOOKED"));
            Assert.AreEqual("CalendarBlank", Inbox.Icon("LESSON_OVERDUE"));
        }
            [Test]
        public void QuestAndDrillScoring()
        {
            Assert.AreEqual(1, RoundSession.Score(RoundRule.Make, 7, 2, 3, 40));
            Assert.AreEqual(0, RoundSession.Score(RoundRule.Make, 7, 2, 1, 0));
            // Precision: the tee's line plus or minus half the window, strict.
            double te = AppPutt.TeeEntry(8, 3);
            Assert.AreEqual(1, RoundSession.Score(RoundRule.Precision, 8, 3, 3, te + 4, 10));
            Assert.AreEqual(0, RoundSession.Score(RoundRule.Precision, 8, 3, 3, te + 5, 10));
            var s = new RoundSession { Rule = RoundRule.Make, StopOnMiss = true, Putts = new List<TournamentMath.Dealt> { new TournamentMath.Dealt { Feet = 7, Tee = 1 }, new TournamentMath.Dealt { Feet = 8, Tee = 2 } } };
            Assert.AreEqual(2, s.Possible);
            Assert.IsFalse(s.Done);
        }

        [Test]
        public void VersusTurns()
        {
            string me = "me-uid";
            var sentNotPlayed = new Dictionary<string, object> { { "status", "ACTIVE" }, { "challengerId", me }, { "challengerUserGameId", null } };
            var waiting = new Dictionary<string, object> { { "status", "ACTIVE" }, { "challengerId", me }, { "challengerUserGameId", 12.0 } };
            var theirsPlayed = new Dictionary<string, object> { { "status", "ACTIVE" }, { "challengerId", "someone" }, { "challengerUserGameId", 12.0 }, { "challengedUserGameId", null } };
            var done = new Dictionary<string, object> { { "status", "COMPLETE" }, { "challengerId", "someone" }, { "challengerUserGameId", 12.0 } };
            Assert.IsTrue(VersusMath.MyTurn(sentNotPlayed, me));
            Assert.IsFalse(VersusMath.MyTurn(waiting, me));
            Assert.IsTrue(VersusMath.MyTurn(theirsPlayed, me));
            Assert.IsFalse(VersusMath.MyTurn(done, me));
            // Versus offers the Play tab's games that end in a score made alone, higher winning; the legacy Standard and
            // Streak catalogue is out, whatever its flags say.
            Assert.IsFalse(VersusMath.Eligible(new Dictionary<string, object> { { "gameType", "STANDARD" }, { "hasLeaderboard", 1.0 } }));
            Assert.IsFalse(VersusMath.Eligible(new Dictionary<string, object> { { "gameType", "STREAK" }, { "versusEnabled", 1.0 } }));
            Assert.IsTrue(VersusMath.Eligible(new Dictionary<string, object> { { "gameType", "CENTERCUT" }, { "multiPlayer", 3.0 } }));
            Assert.IsTrue(VersusMath.Eligible(new Dictionary<string, object> { { "gameType", "ARCADESTREAK" } }));
            Assert.IsFalse(VersusMath.Eligible(new Dictionary<string, object> { { "gameType", "CENTERCUT" }, { "multiPlayer", 2.0 } }));
            Assert.IsFalse(VersusMath.Eligible(new Dictionary<string, object> { { "gameType", "PENDULUM" }, { "gameCat", "SENSOR" } }));
            Assert.IsFalse(VersusMath.Eligible(new Dictionary<string, object> { { "gameType", "OPEN" } }));
        }

        [Test]
        public void QuestTiers()
        {
            Assert.AreEqual(0, QuestMath.Tier(new Dictionary<string, object> { { "difficulty", 1.0 } }));
            Assert.AreEqual(0, QuestMath.Tier(new Dictionary<string, object> { { "difficulty", 2.0 } }));
            Assert.AreEqual(1, QuestMath.Tier(new Dictionary<string, object> { { "difficulty", 5.0 } }));
            Assert.AreEqual(2, QuestMath.Tier(new Dictionary<string, object> { { "difficulty", 8.0 } }));
        }
            [Test]
        public void CertificationPassRule()
        {
            Assert.IsTrue(AdminCertGradeScreen.Passes(new[] { 4, 4, 4, 3, 3 }));
            Assert.IsFalse(AdminCertGradeScreen.Passes(new[] { 5, 5, 5, 5, 1 }));   // a 1 fails
            Assert.IsTrue(AdminCertGradeScreen.Passes(new[] { 5, 5, 4, 4, 2 }));    // one 2 is allowed
            Assert.IsFalse(AdminCertGradeScreen.Passes(new[] { 5, 5, 5, 2, 2 }));   // two 2s fail
            Assert.IsFalse(AdminCertGradeScreen.Passes(new[] { 4, 4, 3, 3, 3 }));   // 17 of 25
        }
            [Test]
        public void DeepLinksParseAsTheApp()
        {
            CollectionAssert.AreEqual(new[] { "game", "123" }, DeepLinks.Parse("https://app.puttr.co/game/123?_kx=abc"));
            CollectionAssert.AreEqual(new[] { "versus" }, DeepLinks.Parse("https://app.puttr.co/versus"));
            CollectionAssert.AreEqual(new[] { "tournament", "164" }, DeepLinks.Parse("https://api.puttr.co/w/tournament/164"));
            CollectionAssert.AreEqual(new[] { "play", "1620" }, DeepLinks.Parse("puttr://play/1620"));
            CollectionAssert.AreEqual(new[] { "coach", "A B" }, DeepLinks.Parse("puttr://coach/A%20B"));
            Assert.IsNull(DeepLinks.Parse("https://app.puttr.co/leaderboard/abc"));
            Assert.IsNull(DeepLinks.Parse("https://example.com/game/1"));
        }

        [Test]
        public void CreatorStats()
        {
            var putts = new List<TournamentMath.Dealt> { new TournamentMath.Dealt { Feet = 3, Tee = 2 }, new TournamentMath.Dealt { Feet = 11, Tee = 1 } };
            CreatorMath.Stats(putts, out double difficulty, out int distance);
            Assert.AreEqual(5.5, difficulty, 1e-9); // ((1 + 0) + (9 + 1)) / 2
            Assert.AreEqual(14, distance);
            CreatorMath.Stats(new List<TournamentMath.Dealt>(), out difficulty, out distance);
            Assert.AreEqual(0, difficulty); Assert.AreEqual(0, distance);
        }

        [Test]
        public void ChatMarkdown()
        {
            Assert.AreEqual("<b><color=#7AD446>Speed</color></b> first", ChatUi.Rich("**Speed** first"));
            Assert.AreEqual("<color=#7AD446>\u2022</color> Grip", ChatUi.Rich("- Grip"));
            Assert.AreEqual("<b>Plan</b>", ChatUi.Rich("## Plan"));
            Assert.AreEqual("Watch Eye Line", ChatUi.StripIds("Watch Eye Line (#5)"));
            Assert.AreEqual("\u2039script\u203A", ChatUi.Rich("<script>"));
        }
    
        [Test]
        public void LessonWords()
        {
            Assert.AreEqual("$50", LessonInfo.Price(50));
            Assert.AreEqual("$55.50", LessonInfo.Price(55.5));
            Assert.AreEqual("$12.34", LessonInfo.Money(1234));
            Assert.AreEqual("1 week", LessonInfo.Turnaround(168));
            Assert.AreEqual("4 days", LessonInfo.Turnaround(96));
            Assert.AreEqual("72 hours", LessonInfo.Turnaround(0));
            Assert.AreEqual("24 hr", LessonInfo.TurnaroundShort(24));
            var coach = new Dictionary<string, object> { { "lessonPrice", 50.0 } };
            var (m, n) = LessonInfo.Prices(coach);
            Assert.AreEqual(55.0, m, 1e-9); Assert.AreEqual(60.0, n, 1e-9);
            Assert.AreEqual("\u2605\u2605\u2605\u2605\u2606", LessonInfo.Stars(4.2));
        }

        [Test]
        public void CoachFirstRunReveal()
        {
            Assert.AreEqual(70, FirstRunMath.Target(""));
            Assert.AreEqual(85, FirstRunMath.Target("+2"));
            Assert.AreEqual(78, FirstRunMath.Target("7.4"));
            Assert.AreEqual(68, FirstRunMath.Target("10-19"));
            Assert.AreEqual(58, FirstRunMath.Target("24"));
            Assert.AreEqual(50, FirstRunMath.Target("new"));
            AppPutt.Verdict Miss(string side, double ro) => new AppPutt.Verdict { Count = 0, MissSide = side, Rollout = ro };
            AppPutt.Verdict Make(double off) => new AppPutt.Verdict { Count = 2, OffLine = off, Rollout = 1 };
            var putts = new List<AppPutt.Verdict> { Miss("LEFT", 5), Miss("LEFT", 6), Make(-2), Make(0), Miss("LEFT", 8) };
            Dictionary<string, object> r = FirstRunMath.Reveal(putts, "15");
            Assert.AreEqual("left", r["leanSide"]);
            Assert.AreEqual(100, r["leanPct"]);
            Assert.AreEqual("runs hot", r["speedBias"]);
            Assert.AreEqual(40, r["percentMade"]);
            Assert.AreEqual("You're leaning left.", r["leakTitle"]);
            Assert.AreEqual("Early read: 100% of your putts drifted left of center, and your speed runs hot. Let's confirm it over a few sessions.", r["leakBody"]);
            Assert.AreEqual("pull-left", r["focusKey"]);
        }

        [Test]
        public void DrillsAndJunior()
        {
            var rolls = new List<AppPutt.Verdict> { new AppPutt.Verdict { Rollout = 4 }, new AppPutt.Verdict { Rollout = -2.5 }, new AppPutt.Verdict() };
            Assert.AreEqual(6.5, DrillMath.SpeedSpread(rolls).Value, 1e-9);
            Assert.IsNull(DrillMath.SpeedSpread(new List<AppPutt.Verdict> { new AppPutt.Verdict { Rollout = 1 } }));
            Assert.AreEqual(11, JuniorMath.Age(new DateTime(2014, 6, 2), new DateTime(2026, 6, 1))); // the day before turning 12
            Assert.AreEqual(13, JuniorMath.Age(new DateTime(2013, 6, 1), new DateTime(2026, 6, 1)));
            Assert.IsTrue(JuniorMath.NoPostTrack(16, "US", "AL"));
            Assert.IsFalse(JuniorMath.NoPostTrack(18, "US", "HI"));
            Assert.IsFalse(JuniorMath.NoPostTrack(16, "US", "TX"));
        }

        [Test]
        public void PaceConsistencyAndBoardNames()
        {
            Assert.AreEqual("consistent", PuttAssessment.Consistency(new List<double> { 1, 2 }).words);
            Assert.AreEqual("very consistent", PuttAssessment.Consistency(new List<double> { 1.0, 1.05, 1.1, 1.12 }).words);
            Assert.AreEqual("not consistent", PuttAssessment.Consistency(new List<double> { 0.5, 1.5, 2.5, 3.5 }).words);
            object p = GameLeaderboardScreen.Player(new Dictionary<string, object> { { "userName", "{\"displayName\":\"Ava\u200B Chen\",\"homeTown\":\"Denver\"}" } });
            Assert.AreEqual("Ava Chen", J.Str(p, "displayName"));
            Assert.IsNull(GameLeaderboardScreen.Player(new Dictionary<string, object> { { "userName", "undefined" } }));
        }

        [Test]
        public void BodyFaultLines()
        {
            var faults = new List<BodyFaults.Fault> { new BodyFaults.Fault { Key = "shoulder_open", Severity = 0.1 }, new BodyFaults.Fault { Key = "knees_straight", Severity = 0.05 } };
            string line = BodyFaults.Line(false, "RIGHT", faults);
            StringAssert.StartsWith("This one leaked right, and here's why. Here's the main thing", line);
            StringAssert.Contains(" Also, your knees are pretty locked", line);
            StringAssert.StartsWith("This one dropped, but watch this.", BodyFaults.Line(true, null, faults));
        }

        [Test]
        public void StreakGamesRetired()
        {
            Assert.IsTrue(GameBridge.Retired(new Dictionary<string, object> { { "id", 76.0 }, { "gameType", "STREAK" } }));
            Assert.IsFalse(GameBridge.Retired(new Dictionary<string, object> { { "gameType", "STREAK" }, { "creatorId", 5.0 } }));
            Assert.IsFalse(GameBridge.Retired(new Dictionary<string, object> { { "gameType", "ARCADESTREAK" } }));
            Assert.AreEqual(76, GameBridge.StreakGameByFeet[6]);
            Assert.AreEqual(82, GameBridge.StreakGameByFeet[11]);
            Assert.IsFalse(GameBridge.StreakGameByFeet.ContainsKey(0));
            foreach (var (type, name) in new[] { ("TARGET", "Archery"), ("CRICKET", "Cricket"), ("DERBY", "Derby"), ("MAJORLEAGUE", "Major League"), ("ARCADESTREAK", "On Fire") })
                Assert.AreEqual(name, GameBridge.ArcadeNameFor(new Dictionary<string, object> { { "gameType", type } }));
        }

        [Test]
        public void TracksCaddiePicksTheShortestClubThatReaches()
        {
            Assert.AreEqual("DR", TracksEngine.Suggest(420, "tee").Id);
            Assert.AreEqual(11, TracksEngine.Suggest(420, "tee").Ft);
            Assert.AreEqual("7I", TracksEngine.Suggest(150, "fairway").Id);
            TracksEngine.Club rough = TracksEngine.Suggest(150, "rough");
            Assert.AreEqual(8, rough.Ft, "the rough adds a foot to the mat putt");
            Assert.IsTrue(TracksEngine.Suggest(60, "fairway").Pitch);
            Assert.IsTrue(TracksEngine.Playable("sand").TrueForAll(c => c.Yds <= 160), "no long clubs from sand");
        }

        [Test]
        public void TracksGreenPutts()
        {
            Assert.IsTrue(TracksEngine.GreenPutt(2).Tap);
            TracksEngine.Club real = TracksEngine.GreenPutt(8.4);
            Assert.AreEqual(8, real.Ft); Assert.IsFalse(real.Lag);
            TracksEngine.Club lag = TracksEngine.GreenPutt(40);
            Assert.IsTrue(lag.Lag); Assert.AreEqual(10, lag.Ft); Assert.AreEqual(4, lag.Ratio, 1e-9);
            Assert.IsTrue(TracksEngine.GreenResult(8, 1, 3, "pro", 1).made);
            Assert.IsFalse(TracksEngine.GreenResult(8, 1, -2, "pro", 1).made, "short never goes in");
            Assert.IsFalse(TracksEngine.GreenResult(40, 1, 3, "pro", 4).made, "a lag's miss scales up");
        }

        [Test]
        public void TracksScoring()
        {
            Assert.AreEqual("Birdie", TracksEngine.ScoreName(3, 4));
            Assert.AreEqual("Ace", TracksEngine.ScoreName(1, 3));
            Assert.AreEqual("Double", TracksEngine.ScoreName(6, 4));
            Assert.AreEqual("E", TracksEngine.ToPar(0)); Assert.AreEqual("+3", TracksEngine.ToPar(3)); Assert.AreEqual("-2", TracksEngine.ToPar(-2));
            Assert.AreEqual(12, TracksEngine.LeaderboardCode("front", "pro"));
            Assert.AreEqual(33, TracksEngine.LeaderboardCode("all", "major"));
        }

        [Test]
        public void TracksReadsTheCourseAndItsLies()
        {
            var doc = J.Parse(UnityEngine.Resources.Load<UnityEngine.TextAsset>("PuttrApp/Fixtures/tracks-standrews").text);
            TracksCourse c = TracksCourse.Read(doc);
            Assert.AreEqual(18, c.Holes.Count);
            Assert.AreEqual(72, c.Holes.Sum(h => h.Par));
            Assert.AreEqual("green", c.LieAt(c.Holes[0].Pin));
            Assert.AreEqual(c.Holes[0].Pin, TracksCourse.PointAt(c.Holes[0], c.Holes[0].Len));
        }

        static AppPutt.Verdict Holed(double entry = 0, double? rollout = 0) => new AppPutt.Verdict { Ball = true, Count = 5, Entry = entry, HasEntry = true, Rollout = rollout };
        static AppPutt.Verdict Rimmed(double? rollout = -6, string side = "LEFT") => new AppPutt.Verdict { Ball = true, Count = 0, Rollout = rollout, MissSide = side };

        [Test]
        public void RuleGamesAreAllRegistered()
        {
            foreach (string t in new[] { "TOURJUICE", "SPEEDDEMON", "METAL", "LAB", "FULLWORKOUT", "FITTING", "DEADCENTER", "ASSESSMENT", "HORSE", "OHONE", "REDUCER", "MATCH", "HIGHSTRIKER", "BEERPONG", "LEAPFROG", "DOORS", "HIGHSIDE", "SWITCHBACK", "GEARS", "GROOVE", "CLOCK" })
                Assert.IsNotNull(RuleCatalog.Make(new Dictionary<string, object> { { "gameType", t } }), t);
        }

        [Test]
        public void TourJuicePerfectRoundIs63()
        {
            var g = new TourJuiceGame(); g.Start();
            while (!g.Over) g.Putt(Holed(AppPutt.TeeEntry(g.Feet, g.Tee), 0));
            Assert.AreEqual(63, g.Score[0], 1e-6);
            Assert.AreEqual(9, g.Shots.Count);
        }

        [Test]
        public void MetalBackDoorScoresNothing()
        {
            var g = new MetalGame(); g.Start();
            Shot s = g.Putt(Holed(AppPutt.TeeEntry(g.Feet, g.Tee) + 120));
            Assert.AreEqual(0, s.Points);
            Assert.AreEqual("BACK DOOR", s.Title);
        }

        [Test]
        public void GearsTopGearEndsWithBonus()
        {
            var g = new GearsGame(); g.Start();
            double[] inBand = { 1, 4, 7, 10, 13 };
            foreach (double r in inBand) g.Putt(Holed(0, r));
            Assert.IsTrue(g.Over);
            Assert.AreEqual(4 * 25 + 25 + 10 * 10, g.Score[0]);
        }

        [Test]
        public void HorseFollowerMissTakesALetter()
        {
            var g = new HorseGame(); g.Names = new List<string> { "A", "B" }; g.Start();
            g.Putt(Holed(AppPutt.TeeEntry(g.Feet, g.Tee)));   // A sets the shot
            Assert.AreEqual(1, g.P);
            g.Putt(Rimmed());                                // B misses: H
            Assert.AreEqual("H", g.ScoreText(1));
            Assert.AreEqual(0, g.P, "back to the caller");
        }

        [Test]
        public void MatchPlayDecidedEarly()
        {
            var g = new MatchPlayGame(); g.Names = new List<string> { "A", "B" }; g.Start();
            for (int h = 0; h < 5 && !g.Over; h++) { g.Putt(Holed(AppPutt.TeeEntry(g.Feet, g.Tee))); g.Putt(Rimmed()); }
            Assert.IsTrue(g.Over);
            Assert.AreEqual(0, g.Winner);
            StringAssert.Contains("5 & 4", g.Outcome());
        }

        [Test]
        public void HighSideLowMissCostsThree()
        {
            var g = new HighSideGame(); g.Start();
            string low = g.Tee == 1 ? "RIGHT" : "LEFT";
            Assert.AreEqual(-3, g.Putt(Rimmed(-6, low)).Points);
        }

        [Test]
        public void OhOneDoubleOutFinishes()
        {
            var g = new OhOneGame(); g.Start();
            // Count down with perfect x4 makes until a checkout, then a center-cut double finishes.
            for (int i = 0; i < 200 && !g.Over; i++) g.Putt(Holed(AppPutt.TeeEntry(g.Feet, g.Tee)));
            Assert.IsTrue(g.Over);
            Assert.AreEqual("0", g.ScoreText(0));
        }

        [Test]
        public void AvatarColourMatchesTheRnApp()
        {
            // Avatar.js getColorForName, run in node for these names.
            var want = new Dictionary<string, string> { { "Dwasem", "2E7D32" }, { "Ryker", "036B36" }, { "User_266", "7AD446" }, { "NSalezze", "2D8C3C" }, { "FTGA2000", "388E3C" }, { "Arnold-Golfer", "388E3C" } };
            foreach (var kv in want) Assert.AreEqual(kv.Value, UnityEngine.ColorUtility.ToHtmlStringRGB(Ui.ColourFor(kv.Key)), kv.Key);
        }

        [Test]
        public void CenterCutReadsCenterFromAnyLine()
        {
            // The putt simulator rolls a center cut from its own spot, not the dealt tee: it must still read 0.
            foreach (float heading in new[] { -6f, -3f, 0f, 3f, 6f })
            {
                var path = Puttr.PuttrSamples.Synthesize(0f, 40f, heading, Puttr.PuttrSamples.SampleHole);
                var putt = Puttr.PuttrAnalysis.Analyze(path, Puttr.PuttrSamples.SampleHole, 6, 1, 30f);
                Assert.IsTrue(Verdicts.Judge(putt, 6, 1));
                Assert.AreEqual(0f, putt.OffCenter, 1.5f, "heading " + heading);
            }
        }
    
        [Test]
        public void TvSignInQrCodeIsWellFormed()
        {
            // Short, a real sign-in link's length, and long enough for version 7+ (version information blocks).
            string[] texts = { "PUTTR", "https://api.puttr.co/w/tv/abcdefghijklmnopqrstuvwxyz012345", new string('x', 150) + "/https://app.puttr.co" };
            string dir = Environment.GetEnvironmentVariable("PUTTR_QR_OUT");
            for (int t = 0; t < texts.Length; t++)
            {
                bool[,] m = QrCode.Encode(texts[t]);
                int size = m.GetLength(0);
                Assert.AreEqual(0, (size - 17) % 4, "a QR is 4v+17 modules");
                // The three finder patterns: dark corners, light ring, dark core.
                foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
                {
                    Assert.IsTrue(m[cy - 3, cx - 3] && m[cy + 3, cx + 3] && m[cy, cx], "finder at " + cx + "," + cy);
                    Assert.IsFalse(m[cy - 2, cx - 2] || m[cy + 2, cx + 2], "finder ring at " + cx + "," + cy);
                }
                if (!string.IsNullOrEmpty(dir)) System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "qr" + t + ".png"), UnityEngine.ImageConversion.EncodeToPNG(QrCode.Texture(texts[t], 6)));
            }
        }

        [Test]
        public void PlayRecordsAreWrittenUnlessHeldForDevelopment()
        {
            // Matt, 2026-11-18: play records go to the API as in production. -app2HoldPlay (development) still holds them.
            string[] play = { "/game/new/", "/game/savePutt/", "/game/over/", "/club/leaderboard/add", "/tournaments/game/end/", "/league/score/submit", "/versus/create", "/multiplayer/savePutt/", "/tracks/round", "/video/saveGame/", "/video/agora/startRecording", "/video/agora/stopRecording" };
            foreach (string path in play)
            {
                Assert.IsFalse(Api.IsHeld(false, "POST", path), "written by default: " + path);
                Assert.IsTrue(Api.IsHeld(true, "POST", path), "held under -app2HoldPlay: " + path);
            }
            // Launched without the flag (as every test run and every Player's app is), nothing is held.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-app2HoldPlay") < 0)
            {
                Assert.IsFalse(Api.HoldPlay);
                Assert.IsTrue(Api.WritePlay);
                Assert.IsFalse(Api.HoldsPlay);
            }
            Assert.IsFalse(Api.IsHeld(true, "POST", "/video/agora/token")); // the camera itself always starts
            Assert.IsFalse(Api.IsHeld(true, "GET", "/game/leaderboard/472"));
            Assert.IsFalse(Api.IsHeld(true, "POST", "/profile/remove"));
            Assert.IsFalse(Api.IsHeld(true, "POST", "/league/me/join"));
        }
}
}
