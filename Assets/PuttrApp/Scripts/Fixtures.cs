// Made-up answers for every call the spike makes, so the screens can be looked at with no
// account and no network: start with -app2Fixture (or PUTTR_ARGS="-app2Fixture" on a simulator).
// Nothing is sent anywhere in this mode, and no game is saved.
using System;
using System.Collections.Generic;

namespace PuttrApp
{
    public static class Fixtures
    {
        public static bool On;
        // -app2LabPose url: the sample 3D session's pose file (to see a real stroke in the cards from a script).
        // -app2LabBuilding: the session list says the newest is still being built; its status route says ready.
        static readonly string LabPose = ArgAfter("-app2LabPose");
        static readonly bool LabBuilding = Array.IndexOf(Environment.GetCommandLineArgs(), "-app2LabBuilding") >= 0;
        static string Lab(string json, bool building)
        {
            if (LabPose != null) json = json.Replace("\"blazePoseUrl\":\"\"", "\"blazePoseUrl\":\"" + LabPose + "\"");
            if (building)
            {
                int at = json.IndexOf("\"status\":4", StringComparison.Ordinal);
                if (at >= 0) json = json.Substring(0, at) + "\"status\":2" + json.Substring(at + 10);
                json = json.Replace("2026-11-12T17:00:00.000Z", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.000Z"));
            }
            return json;
        }
        static readonly string TracksFile = ArgAfter("-app2TracksFile");
        static string ArgAfter(string flag) { string[] a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, flag); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
        public static float Delay = 0.15f;
        /// <summary>-app2NotMember: the made-up Player is not in Puttr Club (to see the paywall).</summary>
        public static bool NotMember;
        /// <summary>-app2Connected (store captures): the made-up Player's Puttr shows as connected.</summary>
        public static bool Connected;

        public static object For(string path)
        {
            if (path.StartsWith("/tracks/featured")) return J.Parse("{\"track\":{\"slug\":\"st-andys-old\",\"name\":\"St Andrews Old Course\",\"location\":\"St Andrews, Scotland\",\"rank\":1,\"par\":72,\"holes\":18,\"yds\":7305,\"isFree\":1},\"week\":47,\"roundsThisWeek\":38,\"roundsTotal\":412,\"lowest\":{\"toPar\":-7,\"displayName\":\"Ava Chen\"},\"myBest\":-2,\"myRoundsTotal\":4}");
            if (path.StartsWith("/tracks/leaderboard")) return J.Parse("{\"rows\":[{\"id\":1,\"rank\":1,\"level\":\"major\",\"userId\":\"u2\",\"displayName\":\"Ava Chen\",\"homeTown\":\"Austin, TX\",\"holeSet\":\"all18\",\"toPar\":-4,\"strokes\":68,\"par\":72,\"created_at\":\"2026-11-15T18:00:00Z\",\"trackName\":\"St Andrews Old Course\",\"userGameId\":\"9001\"},{\"id\":2,\"rank\":1,\"level\":\"pro\",\"userId\":\"fixture\",\"displayName\":\"Sam Rivera\",\"homeTown\":\"Denver, CO\",\"holeSet\":\"front9\",\"toPar\":-1,\"strokes\":35,\"par\":36,\"created_at\":\"2026-11-12T18:00:00Z\",\"trackName\":\"Pine Valley\"},{\"id\":3,\"rank\":2,\"level\":\"pro\",\"userId\":\"u3\",\"displayName\":\"Marcus Bell\",\"holeSet\":\"all18\",\"toPar\":2,\"strokes\":74,\"par\":72,\"created_at\":\"2026-11-10T18:00:00Z\",\"trackName\":\"Augusta National\"}]}");
            if (path.StartsWith("/tracks/search")) return J.Parse("[{\"osmType\":\"way\",\"osmId\":123,\"name\":\"Cherry Hills Country Club\",\"place\":\"Cherry Hills Village, Colorado\"},{\"osmType\":\"way\",\"osmId\":456,\"name\":\"Cherry Creek Golf\",\"place\":\"Denver, Colorado\",\"existing\":{\"slug\":\"cherry-creek\",\"name\":\"Cherry Creek Golf\",\"mine\":false}}]");
            if (path.StartsWith("/tracks/courses")) return J.Parse("[{\"slug\":\"st-andys-old\",\"name\":\"St Andrews Old Course\",\"location\":\"St Andrews, Scotland\",\"rank\":1,\"par\":72,\"holes\":18,\"yds\":7305,\"isFree\":1,\"myBest\":-2,\"rounds\":4},{\"slug\":\"pine-valley\",\"name\":\"Pine Valley\",\"location\":\"Pine Valley, NJ\",\"rank\":2,\"par\":70,\"holes\":18,\"yds\":7181,\"isFree\":0,\"rounds\":0},{\"slug\":\"augusta\",\"name\":\"Augusta National\",\"location\":\"Augusta, GA\",\"rank\":3,\"par\":72,\"holes\":18,\"yds\":7545,\"isFree\":0,\"rounds\":1},{\"slug\":\"cherry-creek\",\"name\":\"Cherry Creek Golf\",\"location\":\"Denver, CO\",\"par\":71,\"holes\":18,\"yds\":6700,\"isFree\":0,\"rounds\":3,\"userId\":\"u2\",\"creatorName\":\"Ava Chen\",\"creatorHomeTown\":\"Austin, TX\"}]");
            // -app2TracksFile course.json: that course's document from a file (a seed from puttr-app/scripts/tracks/courses), to look at other ground than St Andrews.
            if (path.StartsWith("/tracks/course/") && TracksFile != null) return J.Parse(System.IO.File.ReadAllText(TracksFile));
            if (path.StartsWith("/tracks/course/")) { var doc = J.Parse(UnityEngine.Resources.Load<UnityEngine.TextAsset>("PuttrApp/Fixtures/tracks-standrews").text) as Dictionary<string, object>; doc["slug"] = "st-andys-old"; doc["location"] = "St Andrews, Scotland"; return doc; }
            if (path.StartsWith("/lesson/coaches")) return J.Parse("[{\"userId\": \"c1\", \"displayName\": \"Tom Ellery\", \"photoURL\": \"\", \"homeTown\": \"Scottsdale, AZ\", \"headline\": \"Short game specialist, 20 yrs teaching\", \"memberPrice\": 55, \"nonMemberPrice\": 60, \"specialties\": \"STROKE,SPEED\", \"turnaroundHours\": 72, \"avgRating\": 4.8, \"ratingCount\": 12, \"lessonCount\": 40, \"acceptingLessons\": 1, \"live\": 1, \"openLessons\": 2}, {\"userId\": \"c2\", \"displayName\": \"Jess Moreno\", \"homeTown\": \"Leeds, UK\", \"headline\": \"Green reading and pressure\", \"memberPrice\": 82.5, \"nonMemberPrice\": 90, \"specialties\": \"GREEN_READING,PRESSURE\", \"turnaroundHours\": 48, \"ratingCount\": 0, \"lessonCount\": 0, \"acceptingLessons\": 1, \"live\": 0}]");
            if (path.StartsWith("/lesson/coach/")) return J.Parse("{\"coach\": {\"userId\": \"c1\", \"displayName\": \"Tom Ellery\", \"headline\": \"Short game specialist, 20 yrs teaching\", \"bio\": \"I coach players of every level to roll it pure and trust their speed.\", \"memberPrice\": 55, \"nonMemberPrice\": 60, \"specialties\": \"STROKE,SPEED\", \"turnaroundHours\": 72, \"avgRating\": 4.8, \"ratingCount\": 12, \"lessonCount\": 40, \"acceptingLessons\": 1, \"live\": 1}, \"reviews\": [{\"rating\": 5, \"review\": \"Fixed my pull in a week.\", \"reviewedAt\": \"2026-11-10T12:00:00.000Z\", \"displayName\": \"Ava Chen\"}]}");
            if (path.StartsWith("/coach/academy")) return J.Parse("{\"academy\": {\"version\": 5, \"passThreshold\": 80, \"modules\": [{\"key\": \"o1\", \"num\": 1, \"track\": \"online\", \"title\": \"How a Puttr lesson works\", \"summary\": \"Booking to delivery, start to finish.\", \"status\": 2, \"quizScore\": 100, \"lessons\": [{\"heading\": \"The booking\", \"body\": \"A player books you and submits a Stroke Lab session.\"}], \"quiz\": [{\"q\": \"When does the clock start?\", \"options\": [\"At booking\", \"When the session is submitted\"]}]}, {\"key\": \"o2\", \"num\": 2, \"track\": \"online\", \"title\": \"Reading the data\", \"summary\": \"Make %, pace and start line.\", \"status\": 1, \"lessons\": [], \"quiz\": [{\"q\": \"Pace is measured as\", \"options\": [\"Rollout past the hole\", \"Club speed\"]}]}, {\"key\": \"s1\", \"num\": 1, \"track\": \"studio\", \"title\": \"The studio toolset\", \"summary\": \"Player Review and 3D sessions.\", \"status\": 0, \"lessons\": [], \"quiz\": []}]}, \"goLive\": {\"approved\": true, \"academyComplete\": false, \"academyProgress\": {\"passed\": 1, \"total\": 2}, \"certPassed\": false, \"profileComplete\": false, \"payoutsEnabled\": false, \"live\": false}}");
            if (path.StartsWith("/coach/golive")) return J.Parse("{\"goLive\": {\"approved\": true, \"academyComplete\": false, \"academyProgress\": {\"passed\": 1, \"total\": 2}, \"live\": false}}");
            if (path.StartsWith("/coach/application")) return J.Parse("{\"application\": {\"status\": 2, \"submittedAt\": \"2026-11-01T12:00:00.000Z\"}}");
            if (path.StartsWith("/lesson/bookings")) return J.Parse("[{\"id\": 701, \"coachId\": \"c1\", \"coachName\": \"Tom Ellery\", \"status\": 4, \"price\": 5500, \"created_at\": \"2026-11-12T12:00:00.000Z\", \"planId\": 301, \"planStatus\": 1, \"planEndDate\": \"2026-11-30T00:00:00.000Z\"}, {\"id\": 702, \"coachId\": \"c2\", \"coachName\": \"Jess Moreno\", \"status\": 2, \"price\": 9000, \"created_at\": \"2026-11-15T12:00:00.000Z\", \"dueAt\": \"2026-11-18T18:00:00.000Z\"}]");
            if (path.StartsWith("/lesson/booking/")) return J.Parse("{\"id\": 701, \"coachId\": \"c1\", \"userId\": \"a\", \"coachName\": \"Tom Ellery\", \"playerName\": \"Ava Chen\", \"status\": 4, \"price\": 5500, \"platformFee\": 500, \"goals\": \"Stop leaving putts short.\", \"created_at\": \"2026-11-12T12:00:00.000Z\", \"submittedAt\": \"2026-11-12T13:00:00.000Z\", \"deliveredAt\": \"2026-11-14T13:00:00.000Z\", \"watchedAt\": \"2026-11-14T15:00:00.000Z\", \"coachSummary\": \"Your stroke is solid; your pace dies at the hole. Accelerate through.\", \"findings\": \"[{\\\"label\\\":\\\"Pace\\\",\\\"value\\\":\\\"4 in short\\\"},{\\\"label\\\":\\\"Start line\\\",\\\"value\\\":\\\"0.4 deg left\\\"}]\", \"planId\": 301, \"videoUrl\": \"https://storage.googleapis.com/coaching.puttr.co/x.mp4\", \"videoLength\": 184}");
            if (path.StartsWith("/lesson/plan/")) return J.Parse("{\"plan\": {\"id\": 301, \"bookingId\": 701, \"coachId\": \"c1\", \"coachName\": \"Tom Ellery\", \"playerName\": \"Ava Chen\", \"startDate\": \"2026-11-14T00:00:00.000Z\", \"endDate\": \"2026-11-28T00:00:00.000Z\", \"status\": 1}, \"items\": [{\"gameId\": 102, \"gameTitle\": \"Center Cut\", \"gameType\": \"CENTERCUT\", \"targetCount\": 3, \"weekDone\": 2, \"note\": \"Eyes over the ball\"}, {\"gameId\": 104, \"gameTitle\": \"Speed Demon\", \"gameType\": \"SPEEDDEMON\", \"targetCount\": 2, \"weekDone\": 2}], \"weekSessions\": 4, \"weekTarget\": 5, \"daysLeft\": 12, \"progress\": {\"before\": {\"makePct\": 61, \"avgRollout\": -4, \"bands\": [{\"label\": \"6-8 ft\", \"pct\": 48}]}, \"after\": {\"makePct\": 68, \"avgRollout\": -1, \"bands\": [{\"label\": \"6-8 ft\", \"pct\": 57}]}}}");
            if (path.StartsWith("/lesson/coaching/inbox")) return J.Parse("[{\"id\": 702, \"userId\": \"a\", \"playerName\": \"Ava Chen\", \"playerHandicap\": \"12.4\", \"status\": 2, \"goals\": \"Pressure putts\", \"submittedAt\": \"2026-11-15T12:00:00.000Z\", \"dueAt\": \"2026-11-17T09:00:00.000Z\", \"price\": 9000, \"platformFee\": 1500}, {\"id\": 703, \"playerName\": \"Marcus Bell\", \"status\": 1, \"price\": 6000}, {\"id\": 701, \"playerName\": \"Priya Nair\", \"status\": 5, \"rating\": 5, \"deliveredAt\": \"2026-11-10T12:00:00.000Z\", \"price\": 5500, \"platformFee\": 500}]");
            if (path.StartsWith("/lesson/coaching/pending")) return J.Parse("{\"needsReview\": 1, \"overdue\": 0}");
            if (path.StartsWith("/lesson/coaching/earnings")) return J.Parse("{\"lessons\": 7, \"earned\": 35000}");
            if (path.StartsWith("/lesson/coaching/stripe/status")) return J.Parse("{\"onboarded\": false, \"payoutsEnabled\": false, \"pendingCount\": 1, \"pendingCents\": 5000}");
            if (path.StartsWith("/lesson/coaching/players")) return J.Parse("{\"players\": [{\"id\": 301, \"userId\": \"a\", \"playerName\": \"Ava Chen\", \"weekNo\": 1, \"totalWeeks\": 2, \"weekDone\": 4, \"weekTarget\": 5, \"idleDays\": 0}, {\"id\": 302, \"userId\": \"b\", \"playerName\": \"Marcus Bell\", \"weekNo\": 2, \"totalWeeks\": 4, \"weekDone\": 0, \"weekTarget\": 6, \"idleDays\": 5}], \"recentlyEnded\": [{\"id\": 299, \"playerName\": \"Priya Nair\", \"completedAt\": \"2026-11-08T00:00:00.000Z\", \"rating\": 5}]}");
            if (path.StartsWith("/lesson/coaching/profile")) return J.Parse("{\"headline\": \"Short game specialist\", \"bio\": \"20 years teaching.\", \"lessonPrice\": 50, \"turnaroundHours\": 72, \"specialties\": \"STROKE,SPEED\", \"acceptingLessons\": 1}");
            if (path.StartsWith("/junior/application")) return J.Parse("{\"application\": null}");
            if (path.StartsWith("/coach/affiliate")) return J.Parse("{\"configured\": true, \"enrolled\": false}");
            if (path.StartsWith("/club/analytics/games/")) return J.Parse("[{\"id\":9001,\"title\":\"On Fire\",\"score\":57,\"status\":2,\"players\":1,\"created_at\":\"2026-11-16T15:20:00.000Z\",\"email\":\"ava@example.com\",\"userId\":\"a\",\"displayName\":\"Ava Chen\",\"homeTown\":\"Denver, CO\"},{\"id\":9002,\"title\":\"Free Putt\",\"score\":0,\"status\":1,\"players\":1,\"created_at\":\"2026-11-16T14:02:00.000Z\",\"email\":\"marcus@example.com\",\"userId\":\"b\",\"displayName\":\"Marcus Bell\"}]");
            if (path.StartsWith("/club/analytics/users/")) return J.Parse("[{\"userId\":\"a\",\"displayName\":\"Ava Chen\",\"email\":\"ava@example.com\",\"homeTown\":\"Denver, CO\"}]");
            if (path.StartsWith("/club/analytics/userDevices/")) return J.Parse("[{\"deviceInfo\":\"{\\\"model\\\":\\\"iPhone 16 Pro\\\",\\\"brand\\\":\\\"Apple\\\",\\\"platVer\\\":\\\"27.1\\\",\\\"version\\\":\\\"2.9.5\\\"}\"}]");
            if (path.StartsWith("/club/analytics/user/")) return J.Parse("{\"user\":[{\"userId\":\"a\",\"displayName\":\"Ava Chen\",\"firstName\":\"Ava\",\"lastName\":\"Chen\",\"email\":\"ava@example.com\",\"persona\":\"COMPETITOR\",\"created_at\":\"2023-05-02T12:00:00.000Z\",\"isMember\":1,\"trialExpiration\":\"2027-05-02T00:00:00.000Z\",\"isCoach\":0}],\"userPutts\":[{\"distance\":6,\"tee\":2,\"made\":1,\"created_at\":\"2026-11-16T15:20:00.000Z\",\"ballPath\":\"[[60,170,12],[150,172,12],[240,173,12],[320,172,12]]\",\"holeBounds\":\"[320,172,30]\"},{\"distance\":9,\"tee\":1,\"made\":0,\"created_at\":\"2026-11-16T15:18:00.000Z\",\"ballPath\":\"[[60,120,12],[200,130,12],[340,150,12],[420,160,12]]\",\"holeBounds\":\"[320,172,30]\"}]}");
            if (path.StartsWith("/club/analytics/putts/")) return For("/club/analytics/user/x") is Dictionary<string, object> u ? u["userPutts"] : null;
            if (path.StartsWith("/admin/user/overview/")) return J.Parse("{\"profile\":{\"userId\":\"a\",\"isMember\":1,\"ownsPuttr\":1,\"handedness\":\"RIGHTY\",\"homeTown\":\"Denver, CO\"},\"auth\":{\"lastSignInTime\":\"2026-11-15T10:00:00.000Z\",\"providers\":[\"apple.com\"],\"emailVerified\":true},\"activity\":{\"putts\":{\"total\":15220,\"last30\":610,\"last7\":140,\"activeDays\":212,\"firstPutt\":\"2023-05-03T00:00:00.000Z\",\"lastPutt\":\"2026-11-16T15:20:00.000Z\",\"avgRollout\":11.4},\"stats\":{\"active_days_last_30\":18,\"active_days_prev_30\":21,\"current_streak\":4,\"longest_streak\":19},\"byMonth\":[{\"month\":\"2026-10\",\"putts\":820},{\"month\":\"2026-11\",\"putts\":610},{\"month\":\"2026-08\",\"putts\":300}],\"byDistance\":[{\"distance\":3,\"putts\":400,\"made\":380},{\"distance\":7,\"putts\":300,\"made\":150}]},\"games\":{\"totals\":{\"completed\":540,\"abandoned\":31,\"tournament\":12,\"lab\":3,\"multi\":20},\"video\":{\"gamesWithVideo\":40,\"emptyRecordings\":2,\"lastVideo\":\"2026-11-10T00:00:00.000Z\"},\"top\":[{\"title\":\"On Fire\",\"gameType\":\"ARCADESTREAK\",\"plays\":120,\"avg\":31.2,\"best\":57,\"last\":\"2026-11-16T15:20:00.000Z\"}],\"recent\":[{\"id\":9001,\"title\":\"On Fire\",\"status\":2,\"score\":57,\"puttCounter\":30,\"created_at\":\"2026-11-16T15:20:00.000Z\",\"videoSegments\":0,\"videoRows\":0}]},\"billing\":{\"activations\":[{\"orderNumber\":\"7777\",\"result\":\"ACTIVATED\"}]}}");
            if (path.StartsWith("/admin/lab/sessions")) return For("/lab/sessions");
            if (path.StartsWith("/admin/coach/applications")) return J.Parse("{\"applications\":[{\"id\":31,\"displayName\":\"Jo Park\",\"email\":\"jo@example.com\",\"location\":\"Chicago, IL\",\"yearsCoaching\":8,\"credentials\":\"[{\\\"type\\\":\\\"PGA_AM\\\"}]\",\"status\":0,\"submittedAt\":\"2026-11-14T00:00:00.000Z\"}]}");
            if (path.StartsWith("/admin/coach/certifications")) return J.Parse("{\"certifications\":[{\"id\":801,\"coachName\":\"Tom Ellery\",\"deliveredAt\":\"2026-11-15T00:00:00.000Z\",\"lessonVideoLength\":312}]}");
            if (path.StartsWith("/admin/coach/application/")) return J.Parse("{\"application\":{\"id\":31,\"displayName\":\"Jo Park\",\"email\":\"jo@example.com\",\"location\":\"Chicago, IL\",\"country\":\"US\",\"yearsCoaching\":8,\"playerCount\":25,\"teachingContext\":\"BOTH\",\"intendedPrice\":60,\"intendedTurnaround\":48,\"weeklyCapacity\":10,\"credentials\":\"[{\\\"type\\\":\\\"PGA_AM\\\",\\\"proofUrl\\\":\\\"\\\"},{\\\"type\\\":\\\"TPI\\\"}]\",\"freeResponse\":\"Speed first, then line.\",\"status\":0,\"submittedAt\":\"2026-11-14T00:00:00.000Z\",\"playerStats\":{\"putts\":2400,\"games\":90,\"labSessions\":2,\"lastPlayed\":\"2026-11-12T00:00:00.000Z\"}}}");
            if (path.StartsWith("/admin/junior/applications")) return J.Parse("{\"applications\":[{\"id\":41,\"displayName\":\"Riley Q\",\"email\":\"riley@example.com\",\"birthDate\":\"2010-04-02\",\"hometown\":\"Austin\",\"status\":0,\"postingRequired\":1,\"handicap\":\"6\",\"submittedAt\":\"2026-11-13T00:00:00.000Z\"}]}");
            if (path.StartsWith("/admin/junior/application/") && path.EndsWith("/emails")) return J.Parse("{\"addresses\":[\"riley@example.com\"],\"messages\":[{\"to\":\"parent@example.com\",\"subject\":\"Approve Riley's application\",\"status\":\"delivered\",\"lastEvent\":\"2026-11-13T02:00:00.000Z\",\"opens\":1,\"clicks\":0}]}");
            if (path.StartsWith("/admin/junior/application/")) return J.Parse("{\"application\":{\"id\":41,\"displayName\":\"Riley Q\",\"email\":\"riley@example.com\",\"birthDate\":\"2010-04-02\",\"hometown\":\"Austin\",\"country\":\"US\",\"region\":\"TX\",\"status\":0,\"postingRequired\":1,\"handicap\":\"6\",\"level\":\"SCHOOL_TEAM\",\"instagram\":\"rileyputts\",\"followersBand\":\"5K\",\"primaryPlatform\":\"INSTAGRAM\",\"parentEmail\":\"parent@example.com\",\"submittedAt\":\"2026-11-13T00:00:00.000Z\",\"playerStats\":{\"putts\":900,\"games\":40}},\"timeline\":{\"eventsLogged\":true,\"items\":[{\"at\":\"2026-11-13T00:00:00.000Z\",\"title\":\"Submitted\",\"tone\":\"neutral\"},{\"at\":\"2026-11-13T00:01:00.000Z\",\"title\":\"Parent consent email sent\",\"detail\":\"to parent@example.com\",\"tone\":\"good\"}]}}");
            if (path.StartsWith("/creator/games/")) return J.Parse("[{\"id\":5001,\"title\":\"Lag ladder\",\"description\":\"Long putts, all three tees.\",\"gameType\":\"STANDARD\",\"putts\":3,\"distance\":30,\"difficulty\":\"8.7\"},{\"id\":5002,\"title\":\"Five in a row\",\"description\":\"Short streak.\",\"gameType\":\"STREAK\",\"putts\":5,\"distance\":20,\"difficulty\":\"2.6\"}]");
            if (path.StartsWith("/quests/levels/")) return J.Parse("[{\"id\":71,\"levelNum\":1,\"levelType\":\"STANDARD\",\"puttData\":\"[{\\\"id\\\":0,\\\"distance\\\":7,\\\"tee\\\":1},{\\\"id\\\":1,\\\"distance\\\":8,\\\"tee\\\":2},{\\\"id\\\":2,\\\"distance\\\":9,\\\"tee\\\":3}]\",\"levelStars\":\"[{\\\"star\\\":1,\\\"puttsMade\\\":1},{\\\"star\\\":2,\\\"puttsMade\\\":2},{\\\"star\\\":3,\\\"puttsMade\\\":3}]\",\"result\":3},{\"id\":72,\"levelNum\":2,\"levelType\":\"STREAK\",\"puttData\":\"[{\\\"id\\\":0,\\\"distance\\\":7,\\\"tee\\\":2}]\",\"levelStars\":\"[]\",\"result\":1},{\"id\":73,\"levelNum\":3,\"levelType\":\"REDUCER\",\"puttData\":\"[]\",\"levelStars\":\"[]\",\"result\":null},{\"id\":74,\"levelNum\":4,\"levelType\":\"STANDARD\",\"puttData\":\"[]\",\"levelStars\":\"[]\",\"result\":null}]");
            if (path.StartsWith("/quests/")) return J.Parse("[{\"id\":1,\"title\":\"First Steps\",\"description\":\"Short putts to build confidence.\",\"levels\":10,\"difficulty\":1,\"questType\":\"FREE\",\"totalStars\":4,\"completed_levels\":2},{\"id\":2,\"title\":\"Lag Master\",\"description\":\"Distance control from long range.\",\"levels\":10,\"difficulty\":5,\"questType\":\"CLUB\",\"totalStars\":null,\"completed_levels\":0},{\"id\":3,\"title\":\"Pin Seeker\",\"description\":\"Precision entries only.\",\"levels\":10,\"difficulty\":9,\"questType\":\"CLUB\",\"totalStars\":null,\"completed_levels\":0}]");
            if (path.StartsWith("/improve/lessons")) return J.Parse("[{\"id\":11,\"title\":\"Grip pressure\",\"description\":\"Light hands, steady face.\",\"category\":\"LEARN\",\"subCategory\":\"BASICS\",\"orderNum\":0,\"thumbnail\":\"\",\"videoPath\":\"basics/grip.mp4\",\"drillType\":\"STANDARD\",\"drillDescription\":\"Five putts from the center tee.\",\"drillPutts\":\"[{\\\"distance\\\":5,\\\"tee\\\":2},{\\\"distance\\\":6,\\\"tee\\\":2}]\"},{\"id\":12,\"title\":\"Eye line\",\"description\":\"Eyes over the ball.\",\"category\":\"LEARN\",\"subCategory\":\"BASICS\",\"orderNum\":1,\"thumbnail\":\"\",\"videoPath\":\"basics/eyes.mp4\"},{\"id\":13,\"title\":\"Tempo\",\"description\":\"Two to one.\",\"category\":\"LEARN\",\"subCategory\":\"BASICS\",\"orderNum\":2,\"thumbnail\":\"\",\"videoPath\":\"basics/tempo.mp4\"},{\"id\":14,\"title\":\"Reading breaks\",\"description\":\"High side misses.\",\"category\":\"LEARN\",\"subCategory\":\"ADVANCED\",\"orderNum\":0,\"thumbnail\":\"\",\"videoPath\":\"adv/breaks.mp4\"}]");
            if (path.StartsWith("/improve/workouts")) return J.Parse("[{\"id\":21,\"title\":\"Wrist warm-up\",\"description\":\"Two minutes before you putt.\",\"category\":\"BODY\",\"subCategory\":\"WARMUP\",\"orderNum\":0,\"thumbnail\":\"\",\"videoPath\":\"body/wrists.mp4\"},{\"id\":22,\"title\":\"Hip turn\",\"description\":\"Loosen up.\",\"category\":\"BODY\",\"subCategory\":\"MOBILITY\",\"orderNum\":0,\"thumbnail\":\"\",\"videoPath\":\"body/hips.mp4\",\"meta\":\"{\\\"equipmentRequired\\\":true}\"}]");
            if (path.StartsWith("/versus/inbox")) return J.Parse("{\"sent\":[{\"id\":501,\"gameId\":102,\"status\":\"ACTIVE\",\"challengerId\":\"fixture\",\"challengerUserGameId\":8801,\"opponentId\":\"a\",\"opponentName\":\"Ava Chen\",\"gameTitle\":\"Center Cut\",\"expiresAt\":\"2099-11-18T00:00:00.000Z\",\"createdAt\":\"2026-11-15T10:00:00.000Z\",\"message\":\"Beat this\"}],\"received\":[{\"id\":502,\"gameId\":102,\"status\":\"ACTIVE\",\"challengerId\":\"b\",\"challengerUserGameId\":8802,\"opponentId\":\"b\",\"opponentName\":\"Marcus Bell\",\"gameTitle\":\"Center Cut\",\"expiresAt\":\"2099-11-17T00:00:00.000Z\",\"createdAt\":\"2026-11-16T08:00:00.000Z\",\"game\":{\"id\":102,\"title\":\"Center Cut\",\"gameType\":\"CENTERCUT\",\"hasLeaderboard\":1,\"puttData\":\"[{\\\"distance\\\":5,\\\"tee\\\":1},{\\\"distance\\\":7,\\\"tee\\\":2}]\"}},{\"id\":499,\"gameId\":102,\"status\":\"COMPLETE\",\"challengerId\":\"a\",\"challengerUserGameId\":8790,\"challengedUserGameId\":8791,\"challengerScore\":21.5,\"challengedScore\":26,\"winnerId\":\"fixture\",\"opponentId\":\"a\",\"opponentName\":\"Ava Chen\",\"gameTitle\":\"Center Cut\",\"createdAt\":\"2026-11-12T08:00:00.000Z\"}]}");
            if (path.StartsWith("/versus/")) return J.Parse("{\"versus\":{\"id\":499,\"status\":\"COMPLETE\",\"challengerId\":\"a\",\"challengerName\":\"Ava Chen\",\"challengedName\":\"Sam Rivera\",\"challengerUserGameId\":8790,\"challengedUserGameId\":8791,\"challengerScore\":21.5,\"challengedScore\":26,\"winnerId\":\"fixture\",\"gameTitle\":\"Center Cut\",\"message\":\"Rematch?\"},\"challengerPutts\":[{\"distance\":5,\"made\":1},{\"distance\":7,\"made\":0}],\"challengedPutts\":[{\"distance\":5,\"made\":1},{\"distance\":7,\"made\":1}]}");
            if (path.StartsWith("/multiplayer/stats")) return J.Parse("[{\"total\":12,\"wins\":7}]");
            if (path.StartsWith("/multiplayer/active")) return J.Parse("[{\"id\":61,\"userId\":\"fixture\",\"participantId\":\"a\",\"hostName\":\"Sam Rivera\",\"participantName\":\"Ava Chen\",\"status\":\"0\",\"numPutts\":9,\"distanceRange\":\"3,11\",\"entryAngle\":90,\"puttData\":\"[{\\\"distance\\\":6,\\\"tee\\\":2}]\",\"gameState\":\"{\\\"currentPlayer\\\":1,\\\"playerPutts\\\":[[],[]],\\\"puttCounter\\\":0,\\\"isOvertime\\\":false,\\\"rules\\\":\\\"entry\\\"}\",\"created_at\":\"2099-11-16T08:00:00.000Z\"}]");
            if (path.StartsWith("/multiplayer/completed")) return J.Parse("[{\"id\":55,\"userId\":\"b\",\"participantId\":\"fixture\",\"hostName\":\"Marcus Bell\",\"participantName\":\"Sam Rivera\",\"status\":\"1\",\"winnerId\":\"fixture\",\"numPutts\":9,\"distanceRange\":\"6,9\",\"entryAngle\":180,\"puttData\":\"[{\\\"distance\\\":6,\\\"tee\\\":2},{\\\"distance\\\":7,\\\"tee\\\":1},{\\\"distance\\\":8,\\\"tee\\\":3},{\\\"distance\\\":9,\\\"tee\\\":2},{\\\"distance\\\":6,\\\"tee\\\":1},{\\\"distance\\\":7,\\\"tee\\\":3},{\\\"distance\\\":8,\\\"tee\\\":2},{\\\"distance\\\":9,\\\"tee\\\":1},{\\\"distance\\\":6,\\\"tee\\\":3}]\",\"gameState\":\"{\\\"currentPlayer\\\":1,\\\"playerPutts\\\":[[6,0,8,0,6,7,0,9,0,0],[6,7,0,9,0,0,8,0,6,6]],\\\"puttCounter\\\":10,\\\"isOvertime\\\":true,\\\"rules\\\":\\\"entry\\\"}\",\"created_at\":\"2026-11-10T08:00:00.000Z\"}]");
            if (path.StartsWith("/family/")) return J.Parse("{\"ownerId\":\"fixture\",\"isOwner\":true,\"pinSet\":false,\"maxPlayers\":5,\"players\":[{\"userId\":\"fixture\",\"name\":\"Sam\",\"displayName\":\"Sam Rivera\",\"isOwner\":true,\"isMe\":true,\"totalPutts\":8214,\"totalMade\":5391},{\"userId\":\"k1\",\"name\":\"Mia\",\"displayName\":\"Sunny Fox 27\",\"isKid\":true,\"photoURL\":\"https://storage.googleapis.com/images.puttr.co/avatars/fox-4.png\",\"avatar\":{\"animal\":\"fox\",\"color\":4},\"totalPutts\":610,\"totalMade\":330},{\"userId\":\"p2\",\"name\":\"Jordan\",\"displayName\":\"Jordan\",\"totalPutts\":1200,\"totalMade\":702}]}");
            if (path.StartsWith("/members/profile/")) return J.Parse("[{\"userId\":\"a\",\"displayName\":\"Ava Chen\",\"homeTown\":\"Denver, CO\",\"isMember\":1,\"photoURL\":\"\",\"created_at\":\"2023-05-02T12:00:00.000Z\",\"bio\":\"Lag putting is my love language.\",\"handicap\":3.1,\"following\":40,\"followers\":118,\"totalPutts\":15220,\"totalMade\":10102,\"totalFeet\":110400,\"totalGames\":612}]");
            if (path.StartsWith("/members/userStats/")) return For("/profile/userStats");
            if (path.StartsWith("/members/achievements/")) return For("/profile/achievements");
            if (path.StartsWith("/members/putts/")) return For("/profile/putts/90");
            if (path.StartsWith("/members/breaks/")) return For("/profile/breaks/90");
            if (path.StartsWith("/members/summary/")) return For("/profile/summary/");
            if (path.StartsWith("/members/games/")) return For("/profile/games/");
            if (path.StartsWith("/members/gameCounts/")) return For("/profile/gameCounts/");
            if (path.StartsWith("/members/best/")) return For("/profile/following");
            if (path.StartsWith("/club/analytics/game/")) return J.Parse("[{\"game\":{\"id\":9001,\"title\":\"On Fire\",\"created_at\":\"2026-11-15T18:00:00.000Z\",\"score\":57,\"gameType\":\"ARCADESTREAK\",\"userId\":\"a\"}},{\"user\":{\"userId\":\"a\",\"displayName\":\"Ava Chen\",\"homeTown\":\"Denver, CO\",\"photoURL\":\"\",\"isMember\":1}}]");
            // The gameview route's game: tracked putts (so the putt rows read pace and entry), and its replay when
            // PUTTR_FIXTURE_VIDEO names one (open --env PUTTR_FIXTURE_VIDEO=http://... to check the video player).
            if (path == "/profile/game/putts/9001") return J.Parse(GameViewPutts());
            if (path == "/video/recordings/9001" && (System.Environment.GetEnvironmentVariable("PUTTR_FIXTURE_VIDEO") ?? "").StartsWith("http")) return new List<object> { new Dictionary<string, object> { { "videoUrl", System.Environment.GetEnvironmentVariable("PUTTR_FIXTURE_VIDEO") }, { "created_at", "2026-11-15T18:00:00.000Z" } } };
            if (path.StartsWith("/profile/game/putts/")) return J.Parse("[{\"distance\":5,\"tee\":2,\"made\":1,\"rollout\":11},{\"distance\":7,\"tee\":1,\"made\":0,\"rollout\":-4},{\"distance\":5,\"tee\":3,\"made\":1,\"rollout\":16},{\"distance\":9,\"tee\":2,\"made\":1,\"rollout\":8}]");
            if (path.StartsWith("/video/recordings/")) return J.Parse("[]");
            if (path.StartsWith("/challenges/ACTIVE") || path.StartsWith("/challenges/UPCOMING") || path.StartsWith("/challenges/COMPLETED")) return J.Parse("[{\"challenge\":{\"title\":\"November 1,000\",\"subTitle\":\"Hole 1,000 putts this month\",\"cType\":\"PUTTS\",\"startDate\":\"2026-11-01\",\"endDate\":\"2026-11-30\",\"userJoined\":\"2026-11-01\"},\"participants\":340,\"userResults\":412,\"challengeLevels\":[{\"number\":250,\"title\":\"BRONZE\"},{\"number\":500,\"title\":\"SILVER\"},{\"number\":1000,\"title\":\"GOLD\"}]},{\"challenge\":{\"title\":\"Go the distance\",\"subTitle\":\"A mile of putts\",\"cType\":\"DISTANCE\",\"startDate\":\"2026-11-01\",\"endDate\":\"2026-11-30\",\"userJoined\":null},\"participants\":120,\"userResults\":0,\"challengeLevels\":[{\"number\":5280,\"title\":\"GOLD\"}]}]");
            if (path.StartsWith("/league/seasons/past")) return J.Parse("[{\"id\":\"s3-2026\",\"number\":3,\"startDate\":\"2026-08-01\",\"endDate\":\"2026-09-30\",\"champion\":\"lions\",\"seasonReelUrl\":\"\",\"weeks\":[{\"weekNumber\":1,\"hasRecap\":true,\"gameTitle\":\"On Fire\"},{\"weekNumber\":2,\"hasRecap\":true,\"gameTitle\":\"Center Cut\"}]}]");
            if (path.Contains("/finale")) return J.Parse("{\"champion\":\"lions\",\"mvp\":{\"player\":{\"username\":\"MarcusB\"},\"team\":\"lions\",\"totalScore\":402.5},\"finalStandings\":[{\"team\":\"lions\",\"seasonRank\":1,\"weeksWon\":3,\"seasonTotal\":1650.2},{\"team\":\"mavericks\",\"seasonRank\":2,\"weeksWon\":1,\"seasonTotal\":1590.4}]}");
            if (path.Contains("/team/") && path.EndsWith("/scores")) return J.Parse("[{\"player\":{\"userId\":\"b\",\"displayName\":\"Marcus Bell\",\"homeTown\":\"Leeds, UK\",\"putterBrand\":\"Odyssey\",\"putter\":{\"brand\":\"Odyssey\",\"model\":\"Ai-ONE Seven\"}},\"score\":52.3,\"userGameId\":9001},{\"player\":{\"userId\":\"fixture\",\"displayName\":\"Sam Rivera\",\"homeTown\":\"Austin, TX\"},\"score\":38.4,\"userGameId\":9002}]");
            if (path.StartsWith("/profile/messages/all")) return J.Parse("[{\"id\":31,\"status\":0,\"messageData\":\"{\\\"type\\\":\\\"FOLLOW\\\",\\\"userId\\\":\\\"a\\\"}\",\"body\":\"Ava Chen started following you\",\"displayName\":\"Ava Chen\",\"photoURL\":\"\",\"created_at\":\"2026-11-16T09:00:00.000Z\"},{\"id\":30,\"status\":0,\"messageData\":\"{\\\"type\\\":\\\"LEAGUE\\\",\\\"route\\\":\\\"weekRecap\\\"}\",\"body\":\"Your League Night week 5 recap is ready. The Mavericks moved up to 2nd.\",\"created_at\":\"2026-11-15T20:00:00.000Z\"},{\"id\":29,\"status\":1,\"messageData\":\"{\\\"type\\\":\\\"GAME\\\",\\\"gameId\\\":100}\",\"body\":\"Marcus Bell beat your On Fire score with 61.33333\",\"displayName\":\"Marcus Bell\",\"photoURL\":\"\",\"created_at\":\"2026-11-14T18:00:00.000Z\"},{\"id\":28,\"status\":1,\"messageData\":\"{\\\"type\\\":\\\"TOURNAMENT\\\"}\",\"body\":\"Round 2 of Watch It Drop is open. 212 players are in.\",\"created_at\":\"2026-11-10T12:00:00.000Z\"}]");
            if (path.StartsWith("/profile/message/")) return new Dictionary<string, object>();
            if (path.StartsWith("/notifications/prefs")) return J.Parse("{\"practice\":true,\"competition\":true,\"offers\":false}");
            // 3D sessions: a finished session with a good capture, and an older one still processing (LabSessions, the session page).
            if (path.StartsWith("/lab/sessions")) return J.Parse(Lab("[{\"id\":812,\"status\":4,\"handedness\":\"RIGHTY\",\"score\":18.0,\"created_at\":\"2026-11-12T17:00:00.000Z\",\"captureCheck\":\"{\\\"verdict\\\":\\\"ok\\\",\\\"halves\\\":{\\\"first\\\":\\\"downTheLine\\\",\\\"second\\\":\\\"faceOn\\\"}}\",\"blazePoseUrl\":\"\",\"replayUrl\":\"\",\"thumbUrl\":\"\"},{\"id\":640,\"status\":2,\"handedness\":\"RIGHTY\",\"score\":14.5,\"created_at\":\"2026-05-30T17:00:00.000Z\",\"captureCheck\":\"{\\\"verdict\\\":\\\"bad\\\",\\\"reasons\\\":[\\\"wrongPerspective\\\"],\\\"halves\\\":{\\\"first\\\":\\\"downTheLine\\\",\\\"second\\\":\\\"downTheLine\\\"}}\"}]", LabBuilding));
            if (path.StartsWith("/lab/session/putts") || path.StartsWith("/coaching/session/putts")) return J.Parse(LabPutts());
            if (path.StartsWith("/lab/session/status/")) return J.Parse(Lab("[{\"id\":812,\"status\":4,\"handedness\":\"RIGHTY\",\"score\":18.0,\"created_at\":\"2026-11-12T17:00:00.000Z\",\"blazePoseUrl\":\"\",\"replayUrl\":\"\"}]", false));
            if (path.StartsWith("/lab/session/start")) return J.Parse("{\"insertId\":9812}");
            if (path.StartsWith("/lab/session/") || path.StartsWith("/lab/recording/")) return J.Parse("{\"ok\":true}");
            if (path.StartsWith("/coach/lessons/")) return J.Parse("[{\"id\":41,\"title\":\"STANCE\",\"description\":\"Guide of how to get yourself into a solid stance over the ball.\"},{\"id\":42,\"title\":\"POSTURE\",\"description\":\"Getting our body into a good position to help move freely.\"}]");
            if (path.StartsWith("/coach/settings")) return J.Parse("{\"dailyGoal\":10,\"longestStreak\":9,\"currentStreak\":4,\"lastPracticeDate\":\"2026-01-01\"}");
            if (path.StartsWith("/profile/analysis/putts")) return J.Parse(AnalysisPutts());
            if (path.StartsWith("/profile/analysis")) return J.Parse("[{\"distance\":3,\"tee\":2,\"total\":40,\"made\":\"39\"},{\"distance\":4,\"tee\":1,\"total\":22,\"made\":\"19\"},{\"distance\":5,\"tee\":3,\"total\":25,\"made\":\"18\"},{\"distance\":6,\"tee\":2,\"total\":30,\"made\":\"21\"},{\"distance\":7,\"tee\":1,\"total\":18,\"made\":\"9\"},{\"distance\":8,\"tee\":3,\"total\":20,\"made\":\"8\"},{\"distance\":9,\"tee\":2,\"total\":16,\"made\":\"7\"},{\"distance\":10,\"tee\":1,\"total\":12,\"made\":\"3\"},{\"distance\":11,\"tee\":3,\"total\":10,\"made\":\"2\"}]");
            if (path.StartsWith("/profile/following") || path.StartsWith("/profile/followers") || path.StartsWith("/members/search")) return J.Parse("[{\"userId\":\"a\",\"displayName\":\"Ava Chen\",\"homeTown\":\"Denver, CO\",\"isMember\":1},{\"userId\":\"b\",\"displayName\":\"Marcus Bell\",\"homeTown\":\"Leeds, UK\"},{\"userId\":\"c\",\"displayName\":\"Priya Nair\",\"homeTown\":\"Austin, TX\",\"isMember\":1}]");
            if (path.StartsWith("/profile/gameCounts")) return J.Parse("[{\"count\":120,\"title\":\"On Fire\"},{\"count\":64,\"title\":\"Center Cut\"},{\"count\":31,\"title\":\"Tracks\"}]");
            if (path.StartsWith("/profile/games")) return J.Parse("[{\"gameId\":100,\"title\":\"On Fire\",\"score\":57,\"updated_at\":\"2026-11-15T18:00:00.000Z\",\"tournamentId\":0},{\"title\":\"Watch It Drop - ROUND 1\",\"score\":41,\"updated_at\":\"2026-11-12T18:00:00.000Z\",\"tournamentId\":164},{\"title\":\"Free Putt\",\"score\":0,\"updated_at\":\"2026-11-10T18:00:00.000Z\",\"tournamentId\":0}]");
            if (path.StartsWith("/league/me/contribution")) return J.Parse("{\"bestScore\":38.4,\"teamRank\":3,\"inTopFive\":true,\"playedToday\":false}");
            if (path.StartsWith("/league/me")) return J.Parse("{\"team\":\"mavericks\",\"seasonId\":\"s4-2026\"}");
            if (path.StartsWith("/league/season/current")) return J.Parse(Season());
            if (path.Contains("/leaderboard?")) return J.Parse("[{\"rank\":1,\"team\":\"lions\",\"score\":52.3,\"attempts\":4,\"player\":{\"displayName\":\"Marcus Bell\",\"homeTown\":\"Leeds, UK\",\"putterBrand\":\"Scotty Cameron\",\"putter\":{\"brand\":\"Scotty Cameron\",\"model\":\"Phantom X 5.5\",\"length\":\"34\",\"head\":\"Mallet\",\"shaft\":\"Slant neck\",\"grip\":\"Pistolini Plus\"}}},{\"rank\":2,\"team\":\"mavericks\",\"score\":47.9,\"attempts\":6,\"player\":{\"userId\":\"fixture\",\"displayName\":\"Sam Rivera\",\"homeTown\":\"Austin, TX\",\"putterBrand\":\"L.A.B. Golf\",\"putter\":{\"brand\":\"L.A.B. Golf\",\"model\":\"DF3\",\"length\":\"35\"}}},{\"rank\":3,\"team\":\"blizzards\",\"score\":44.1,\"attempts\":2,\"player\":{\"displayName\":\"Jo Park\",\"homeTown\":\"Chicago, IL\",\"putterBrand\":\"Byron Morgan\",\"putter\":null}}]");
            if (path.Contains("/standings")) return J.Parse("[{\"team\":\"lions\",\"total\":231.4,\"seasonTotal\":690.2,\"weekRank\":1,\"seasonRank\":1,\"weekRankChange\":2},{\"team\":\"mavericks\",\"total\":219.8,\"seasonTotal\":701.5,\"weekRank\":2,\"seasonRank\":2,\"weekRankChange\":0},{\"team\":\"noreasters\",\"total\":188.0,\"seasonTotal\":610.0,\"weekRank\":3,\"seasonRank\":3,\"weekRankChange\":-1}]");
            if (path.StartsWith("/tournaments/upcoming")) return J.Parse("[{\"id\":164,\"title\":\"Watch It Drop\",\"description\":\"November's monthly tournament.\",\"startDate\":\"2026-11-01T00:00:00.000Z\",\"endDate\":\"2099-11-30T23:59:59.000Z\",\"participants\":212,\"userJoined\":\"2026-11-02\",\"prizeImage\":\"https://storage.googleapis.com/images.puttr.co/games/Tournament_Button.jpg\",\"prizeDescription\":\"Winner receives a Garmin Approach S50 golf GPS watch\",\"displayName\":\"Puttr\"}]");
            if (path.StartsWith("/tournaments/PRIVATE")) return J.Parse("[]");
            if (path.StartsWith("/tournaments/past")) return J.Parse("[{\"id\":163,\"title\":\"October Classic\",\"description\":\"October's monthly tournament.\",\"startDate\":\"2026-10-01T00:00:00.000Z\",\"endDate\":\"2026-10-31T23:59:59.000Z\",\"participants\":188,\"prizeImage\":\"https://storage.googleapis.com/images.puttr.co/games/Tournament_Button.jpg\",\"displayName\":\"Puttr\",\"reelVideoUrl\":\"https://storage.googleapis.com/images.puttr.co/howto/Unboxing_2025.mp4\"}]");
            if (path.StartsWith("/tournaments/details")) return J.Parse("[{\"participants\":212,\"rounds\":[{\"id\":901,\"roundNum\":1,\"gameId\":1700,\"difficulty\":\"7.0\",\"puttData\":\"[{\\\"id\\\":0,\\\"distance\\\":4,\\\"tee\\\":2},{\\\"id\\\":1,\\\"distance\\\":7,\\\"tee\\\":1},{\\\"id\\\":2,\\\"distance\\\":5,\\\"tee\\\":3},{\\\"id\\\":3,\\\"distance\\\":9,\\\"tee\\\":2},{\\\"id\\\":4,\\\"distance\\\":6,\\\"tee\\\":1}]\"},{\"id\":902,\"roundNum\":2,\"gameId\":1701,\"difficulty\":\"7.0\",\"puttData\":\"[{\\\"id\\\":0,\\\"distance\\\":3,\\\"tee\\\":2}]\"}]}]");
            if (path.StartsWith("/tournaments/userResults")) return J.Parse("[{\"r1_score\":null}]");
            if (path.StartsWith("/tournaments/userGames/")) return J.Parse("[{\"id\":9001,\"roundNum\":1,\"status\":2},{\"id\":9002,\"roundNum\":2,\"status\":2},{\"id\":9001,\"roundNum\":3,\"status\":2}]");
            if (path.StartsWith("/tournaments/leaderboard")) return J.Parse("[{\"userId\":\"x\",\"displayName\":\"Ava Chen\",\"homeTown\":\"Denver, CO\",\"totalScore\":\"112\",\"r1_score\":31,\"r2_score\":44,\"r3_score\":37,\"r4_score\":null,\"putterData\":\"{\\\"brand\\\":\\\"PING\\\",\\\"model\\\":\\\"Anser 2D\\\",\\\"length\\\":\\\"33\\\"}\"},{\"userId\":\"fixture\",\"displayName\":\"Sam Rivera\",\"homeTown\":\"Austin, TX\",\"totalScore\":\"96\",\"r1_score\":52,\"r2_score\":44,\"putter\":{\"brand\":\"TaylorMade\",\"model\":\"Spider Tour\"}},{\"userId\":\"y\",\"displayName\":\"Tom Ellery\",\"homeTown\":\"Perth, AU\",\"totalScore\":\"0\"}]");
            if (path.StartsWith("/user-putters")) return J.Parse("[{\"brand\":\"Scotty Cameron\",\"model\":\"Newport 2\",\"length\":\"34\",\"head\":\"Blade\",\"shaft\":\"null\",\"isPrimary\":1}]");
            if (path.StartsWith("/profile/userStats")) return J.Parse("[{\"active_days_last_30\":18,\"active_days_prev_30\":13,\"calendar_year\":" + J.Write(CalendarJson()) + "}]");
            if (path.StartsWith("/profile/achievements")) return J.Parse("[{\"title\":\"1,000 putts\",\"description\":\"Rolled a thousand\",\"aType\":\"PUTTS\"},{\"title\":\"7 day streak\",\"description\":\"A week without a miss\",\"aType\":\"STREAK\"},{\"title\":\"One mile\",\"description\":\"5,280 feet holed\",\"aType\":\"DISTANCE\"},{\"title\":\"50 games\",\"description\":\"Played fifty\",\"aType\":\"GAMES\"},{\"title\":\"Quest master\",\"description\":\"30 stars\",\"aType\":\"QUEST_STARS\"}]");
            if (path.StartsWith("/profile/putts")) return J.Parse("[{\"total\":120,\"made\":\"112\",\"distance\":3},{\"total\":140,\"made\":\"118\",\"distance\":5},{\"total\":160,\"made\":\"109\",\"distance\":7},{\"total\":110,\"made\":\"61\",\"distance\":9},{\"total\":90,\"made\":\"38\",\"distance\":11}]");
            if (path.StartsWith("/members/analytics/puttsByDistance")) return J.Parse("[{\"total\":1000,\"made\":700,\"distance\":3},{\"total\":1000,\"made\":560,\"distance\":5},{\"total\":1000,\"made\":440,\"distance\":7},{\"total\":1000,\"made\":350,\"distance\":9},{\"total\":1000,\"made\":280,\"distance\":11}]");
            if (path.StartsWith("/profile/breaks")) return J.Parse("[{\"ct\":140,\"tee\":1,\"made\":1},{\"ct\":90,\"tee\":1,\"made\":0},{\"ct\":210,\"tee\":2,\"made\":1},{\"ct\":70,\"tee\":2,\"made\":0},{\"ct\":60,\"tee\":3,\"made\":1},{\"ct\":110,\"tee\":3,\"made\":0}]");
            if (path.StartsWith("/challenges/userChallenges")) return J.Parse("[{\"challenge\":{\"title\":\"November 1,000\",\"cType\":\"PUTTS\",\"endDate\":\"2099-01-01T00:00:00.000Z\",\"userJoined\":\"2026-11-01\",\"currentScore\":412},\"challengeLevels\":[{\"number\":250},{\"number\":1000}]},{\"challenge\":{\"title\":\"Go the distance\",\"cType\":\"DISTANCE\",\"endDate\":\"2099-01-01T00:00:00.000Z\",\"userJoined\":null},\"challengeLevels\":[{\"number\":5280}]}]");
            if (path.StartsWith("/profile/summary")) return J.Parse("{\"league\":{\"team\":\"The Lag Kings\",\"weeksPlayed\":5},\"tournaments\":{\"played\":3,\"bestFinish\":2},\"versus\":{\"total\":9,\"won\":6,\"lost\":3,\"open\":1},\"quests\":{\"stars\":41,\"maxStars\":90,\"questsTotal\":6,\"questsComplete\":2,\"nextQuest\":{\"title\":\"The Long Game\"}},\"bests\":[{\"game\":{\"title\":\"On Fire\"},\"plays\":22,\"best\":57,\"bestAt\":\"2026-10-28T10:00:00.000Z\"},{\"game\":{\"title\":\"Center Cut\"},\"plays\":14,\"best\":88,\"bestAt\":\"2026-11-02T10:00:00.000Z\"}]}");
            if (path.StartsWith("/game/best")) return J.Parse("[{\"best\":57,\"games\":12}]");
            if (path.StartsWith("/game/leaderboard")) return J.Parse(Feed());
            if (path.StartsWith("/profile/recentGames")) return J.Parse(Games(new[] { "On Fire", "Center Cut", "Around the Clock" }, true));
            if (path.StartsWith("/profile/memberGames") || path.StartsWith("/profile/communityGames")) return J.Parse(Feed());
            if (path.StartsWith("/profile")) return J.Parse("[{\"userId\":\"fixture\",\"displayName\":\"Sam Rivera\",\"firstName\":\"Sam\",\"homeTown\":\"Austin, TX\",\"isMember\":1,\"photoURL\":\"\",\"created_at\":\"2024-03-09T12:00:00.000Z\",\"bio\":\"Weekend golfer chasing a single figure handicap.\",\"handicap\":8.4,\"following\":12,\"followers\":31,\"totalPutts\":8214,\"totalMade\":5391,\"totalFeet\":60210,\"totalGames\":386,\"subscriptionExpiration\":\"2099-03-14T00:00:00.000Z\",\"subscriptionType\":\"annual\",\"membershipSource\":\"store\",\"familyCount\":2,\"isAdmin\":1,\"handedness\":\"RIGHTY\"}]".Replace("\"isMember\":1", NotMember ? "\"isMember\":0" : "\"isMember\":1"));
            if (path.StartsWith("/streaks")) return J.Parse("{\"currentStreak\":4,\"longestStreak\":11,\"dailyGoal\":25,\"puttsToday\":9,\"qualifyingPutts\":10,\"qualifiedToday\":false}");
            if (path.StartsWith("/games/recommended")) return J.Parse(Games(new[] { "Dead Center", "Ladder", "Survival", "Lag King" }, false));
            if (path.StartsWith("/games/ALL")) return J.Parse("[{\"id\":100,\"title\":\"On Fire\",\"gameType\":\"ARCADESTREAK\",\"multiPlayer\":0,\"putts\":0,\"last30\":900},{\"id\":101,\"title\":\"Free Putt\",\"gameType\":\"OPEN\",\"multiPlayer\":0,\"putts\":0,\"last30\":1200},{\"id\":102,\"title\":\"Center Cut\",\"gameType\":\"CENTERCUT\",\"multiPlayer\":3,\"putts\":9,\"skills\":\"angle\",\"last30\":800},{\"id\":103,\"title\":\"Major League\",\"gameType\":\"MAJORLEAGUE\",\"multiPlayer\":3,\"putts\":27,\"skills\":\"angle,pressure\",\"last30\":600},{\"id\":104,\"title\":\"Speed Demon\",\"gameType\":\"SPEEDDEMON\",\"multiPlayer\":0,\"putts\":10,\"skills\":\"speed\",\"last30\":500},{\"id\":105,\"title\":\"Cricket\",\"gameType\":\"CRICKET\",\"multiPlayer\":2,\"putts\":30,\"skills\":\"angle\",\"last30\":300},{\"id\":106,\"title\":\"Ladder\",\"gameType\":\"LADDER\",\"multiPlayer\":0,\"putts\":9,\"skills\":\"distance\",\"last30\":200}]");
            if (path.StartsWith("/games/")) return J.Parse(Games(new[] { "On Fire", "Free Putt", "Center Cut", "Tracks", "Around the Clock", "Cricket", "Derby", "Balloon Pop" }, false));
            return new Dictionary<string, object>();
        }

        // A year's putting as user_stats stores it: a JSON string of {count, date}, most days played.
        static string CalendarJson()
        {
            var days = new List<string>();
            System.DateTime today = System.DateTime.Now.Date;
            for (int back = 200; back >= 0; back--)
            {
                int putts = (back * 37) % 9 < 5 ? 12 + (back * 53) % 70 : 0;
                if (putts > 0) days.Add("{\"count\":" + putts + ",\"date\":\"" + today.AddDays(-back).ToString("yyyy-MM-dd") + "T00:00:00.000Z\"}");
            }
            return "[" + string.Join(",", days) + "]";
        }

        // Putts as the API stores them, made from the SDK's own putt maker: paths and holes as JSON strings, rollout as a string.
        // Six putts of a game from the SDK's putt maker: makes in on the left, dead centre, the right and the back
        // door, a miss each side. Entry off the tee's line, as the game view reads it.
        static string GameViewPutts()
        {
            var shots = new (float line, float speed, float heading, int distance, int tee, float rollout)[]
            {
                (0f, 34f, 0f, 5, 2, 8f), (0.9f, 30f, 0f, 7, 1, 5f), (-0.9f, 30f, 0f, 6, 3, 5f), (0.3f, 52f, 0f, 9, 2, 14f), (3.4f, 40f, 0f, 8, 2, 11f), (-3.2f, -14f, 0f, 4, 2, -6f),
            };
            var rows = new List<string>();
            int n = 0;
            foreach (var s in shots)
            {
                // Saved 10 s into the recording, then every 8 s: the replay's putt clips (GameReplay.Clips).
                string at = new System.DateTime(2026, 11, 15, 18, 0, 10 + 8 * n++, System.DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.000Z", System.Globalization.CultureInfo.InvariantCulture);
                var path = Puttr.PuttrSamples.Synthesize(s.line, s.speed, s.heading, Puttr.PuttrSamples.SampleHole);
                bool made = Puttr.PuttrAnalysis.IsMade(path, Puttr.PuttrSamples.SampleHole);
                rows.Add("{\"distance\":" + s.distance + ",\"tee\":" + s.tee + ",\"made\":" + (made ? 1 : 0) + ",\"ballPath\":" + J.Write(RoundSaver.PathJson(path)) +
                         ",\"created_at\":\"" + at + "\",\"holeBounds\":\"[320,100,110]\",\"rollout\":\"" + s.rollout.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "\"}");
            }
            return "[" + string.Join(",", rows) + "]";
        }

        /// <summary>Six lab putts at 5 ft (lab_session_putts): ball paths from the bottom of the frame to a hole near the top, four in, two out.</summary>
        static string LabPutts()
        {
            var rows = new List<string>();
            int[] ends = { -6, 4, 22, -40, 12, 0 };          // where each path finishes across the hole, in pixels
            double?[] rollouts = { 14, -6, 9, -15.2, 5.6, 3.1 };
            for (int i = 0; i < ends.Length; i++)
            {
                bool made = System.Math.Abs(ends[i]) < 30;
                var pts = new List<string>();
                for (int k = 0; k <= 24; k++)
                {
                    double t = k / 24.0, y = 330 - t * (made ? 268 : 300), x = 240 + ends[i] * t * t;
                    pts.Add("[" + (int)x + "," + (int)y + "," + (int)(26 - t * 8) + "]");
                }
                string entry = made ? "[" + (240 + ends[i]) + ",70," + (ends[i] * -2) + ",\\\"FRONT " + (ends[i] < 0 ? "LEFT" : "RIGHT") + "\\\"]" : "0";
                rows.Add("{\"id\":" + (7000 + i) + ",\"distance\":5,\"tee\":2,\"ballPath\":\"[" + string.Join(",", pts) + "]\",\"holeBounds\":\"[240,60,60]\",\"entry\":\"" + entry + "\",\"rollout\":" + rollouts[i].Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
            }
            return "[" + string.Join(",", rows) + "]";
        }

        static string AnalysisPutts()
        {
            var dice = new System.Random(4);
            var rows = new List<string>();
            System.DateTime now = System.DateTime.UtcNow;
            for (int i = 0; i < 160; i++)
            {
                bool good = dice.Next(10) < 6;
                float line = good ? (float)(dice.NextDouble() * 1.6 - 0.8) : (float)(dice.NextDouble() * 7 - 3.5);
                float speed = 26f + (float)dice.NextDouble() * 34f;
                var path = Puttr.PuttrSamples.Synthesize(line, speed, (float)(dice.NextDouble() * 4 - 2), Puttr.PuttrSamples.SampleHole);
                bool made = Puttr.PuttrAnalysis.IsMade(path, Puttr.PuttrSamples.SampleHole);
                float rollout = speed * speed / 128f - 9f;
                rows.Add("{\"distance\":" + (3 + dice.Next(9)) + ",\"tee\":" + (1 + dice.Next(3)) + ",\"made\":" + (made ? 1 : 0) +
                         ",\"ballPath\":" + J.Write(RoundSaver.PathJson(path)) + ",\"holeBounds\":\"[320,100,110]\",\"rollout\":\"" + rollout.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                         "\",\"created_at\":\"" + now.AddDays(-(i * 0.35)).ToString("yyyy-MM-ddTHH:mm:ss.000Z") + "\"}");
            }
            return "[" + string.Join(",", rows) + "]";
        }

        // A four week season with the current week live.
        static string Season()
        {
            System.DateTime monday = System.DateTime.Now.Date.AddDays(-(((int)System.DateTime.Now.DayOfWeek + 6) % 7));
            var weeks = new List<string>();
            for (int w = 0; w < 4; w++)
            {
                System.DateTime start = monday.AddDays(7 * (w - 1));
                weeks.Add("{\"weekNumber\":" + (w + 1) + ",\"gameId\":472,\"gameType\":\"CENTERCUT\",\"gameTitle\":\"Center Cut\",\"startsAt\":\"" + start.ToString("yyyy-MM-dd") + "T00:00:00\",\"endsAt\":\"" + start.AddDays(6).ToString("yyyy-MM-dd") + "T23:59:59\"}");
            }
            return "{\"id\":\"s4-2026\",\"number\":4,\"weeks\":[" + string.Join(",", weeks) + "]}";
        }

        static string Games(string[] titles, bool recent)
        {
            var rows = new List<string>();
            for (int i = 0; i < titles.Length; i++)
                rows.Add("{\"id\":" + (100 + i) + ",\"title\":\"" + titles[i] + "\",\"description\":\"A made-up description for the fixture screens.\",\"gameType\":\"" + (titles[i] == "On Fire" ? "ARCADESTREAK" : titles[i] == "Free Putt" ? "OPEN" : "FIXTURE") +
                         "\",\"putts\":" + (i % 2 == 0 ? 12 : 0) + ",\"distance\":" + (3 + i % 9) + ",\"difficulty\":" + (2 + i % 3) + ",\"multiPlayer\":" + (i % 2 == 0 ? 0 : 3) +
                         (recent ? ",\"lastScore\":" + (40 + i * 7) : ",\"recommendReason\":\"Works on your " + (5 + i) + " footers\"") + "}");
            return "[" + string.Join(",", rows) + "]";
        }

        static string Feed()
        {
            string[] names = { "Ava Chen", "Marcus Bell", "Priya Nair", "Tom Ellery", "Jo Park", "Dana Ortiz", "Lee Howard", "Nina Rossi" };
            var rows = new List<string>();
            for (int i = 0; i < 40; i++)
                rows.Add("{\"displayName\":\"" + names[i % names.Length] + "\",\"photoURL\":\"\",\"title\":\"" + (i % 3 == 0 ? "On Fire" : i % 3 == 1 ? "Center Cut" : "Tracks") + "\",\"score\":" + (18 + (i * 13) % 60) + ",\"putts\":12}");
            return "[" + string.Join(",", rows) + "]";
        }

        /// <summary>The AI chats on ai.puttr.co (Chat.cs): made-up replies and one saved coach conversation.</summary>
        public static object Chat(string path)
        {
            if (path.StartsWith("/v2/caddie/chat")) return J.Parse("{\"conversationId\":\"fx-caddie\",\"answer\":\"Open **Settings**, then **Camera Check**. Set the phone on the stand so the whole mat is in view, then tap **Calibrate**. It takes about ten seconds.\\n\\n- Good light helps\\n- Keep the hole cup clear\",\"escalated\":false}");
            if (path.StartsWith("/v2/lab/session/")) return J.Parse("{\"insights\":[{\"fault\":\"weight_right\",\"label\":\"Weight favoring the right side\",\"confidence\":\"medium\",\"evidence\":{\"body\":\"weight favoring the right side on the setup capture (putts 4, 5, 6)\"},\"lessonIds\":[]},{\"fault\":\"stance_wide\",\"label\":\"Stance too wide\",\"confidence\":\"medium\",\"evidence\":{\"body\":\"stance too wide on the setup capture (putts 4, 5, 6)\"}}]}");
            if (path.StartsWith("/v2/coach/narrate")) return J.Parse("{\"lines\":null}");
            if (path.StartsWith("/v2/coach/analyze")) return J.Parse("{\"conversationId\":\"fx-coach\",\"answer\":\"## Square your shoulders\\n\\nYour misses this session started right of the line. At setup, lay a club across your shoulders and check it points at the target.\\n\\n- Hold the finish for a count of two\\n- **Center Cut** from 4 feet, ten in a row\",\"recommendedLessons\":[{\"id\":11,\"title\":\"Grip pressure\"}]}");
            if (path.StartsWith("/v2/coach/chat")) return J.Parse("{\"conversationId\":\"fx-coach\",\"answer\":\"Short putts that slide past the high side usually come from a closed face at impact. Try **Center Cut** from 4 feet with a gate two balls wide, and hold your finish for a count of two.\",\"recommendedLessons\":[{\"id\":11,\"title\":\"Grip pressure\"}],\"recommendedGames\":[]}");
            if (path.StartsWith("/v2/coach/conversations/")) return J.Parse("{\"conversationId\":\"fx-coach\",\"messages\":[{\"role\":\"assistant\",\"text\":\"Hey Sam, what part of your putting can I help with today?\",\"at\":\"2026-11-16T15:02:00.000Z\"},{\"role\":\"user\",\"text\":\"I keep leaving my lag putts short.\",\"at\":\"2026-11-16T15:03:00.000Z\"},{\"role\":\"user\",\"text\":\"Mostly from 20 feet and out.\",\"at\":\"2026-11-16T15:03:20.000Z\"},{\"role\":\"assistant\",\"text\":\"That is a pace habit. Aim to finish **17 inches past** the hole: your rollout on Puttr shows it. Make the backstroke longer rather than hitting harder.\",\"at\":\"2026-11-16T15:03:40.000Z\"}]}");
            if (path.StartsWith("/v2/coach/conversations")) return J.Parse("{\"conversations\":[{\"conversationId\":\"fx-coach\",\"title\":\"Lag putts coming up short\",\"preview\":\"That is a pace habit.\",\"previewRole\":\"assistant\",\"lastTurnAt\":\"2026-11-16T15:03:40.000Z\"}],\"hasMore\":false}");
            return null;
        }
    }
}
