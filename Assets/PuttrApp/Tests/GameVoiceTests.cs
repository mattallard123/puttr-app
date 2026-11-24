// The games' commentary table and rules (puttr-app common/SoundFunctionsEnglish.js and its twins, and the games' calls
// into them), and Practice's session read (games/Open.js sessionStats). Nothing here plays a sound or touches the network.
using System.Collections.Generic;
using NUnit.Framework;
using Puttr;

namespace PuttrApp.Tests
{
    public class GameVoiceTests
    {
        [Test]
        public void EveryRecordingTheAppLoadsIsInTheTable()
        {
            Assert.AreEqual(8, GameVoice.Takes("english", "start"));
            Assert.AreEqual(19, GameVoice.Takes("english", "made"));
            Assert.AreEqual(20, GameVoice.Takes("english", "miss"));
            Assert.AreEqual(7, GameVoice.Takes("english", "streak"));
            Assert.AreEqual(40, GameVoice.Takes("scottish", "made"));
            Assert.AreEqual(40, GameVoice.Takes("scottish", "miss"));
            Assert.AreEqual(26, GameVoice.Takes("metal", "made"));
            Assert.AreEqual(29, GameVoice.Takes("metal", "miss"));
            Assert.IsTrue(GameVoice.Known("english_call_your_shot"));
            Assert.IsTrue(GameVoice.Known("english_horse_gets_letter"));
            Assert.IsTrue(GameVoice.Known("scottish_distance_11_feet"));
            Assert.IsTrue(GameVoice.Known("metal_tee_right"));
            Assert.IsFalse(GameVoice.Known("english_made_20"));
        }

        [Test]
        public void ACallIsTheDistanceThenTheTee()
        {
            Assert.AreEqual("english_distance_8_feet", GameVoice.DistanceClip("english", 8));
            Assert.AreEqual("english_distance_3_feet", GameVoice.DistanceClip("english", 1), "held to the mat");
            Assert.AreEqual("english_distance_11_feet", GameVoice.DistanceClip("english", 14));
            Assert.AreEqual("english_tee_left", GameVoice.TeeClip("english", 1));
            Assert.AreEqual("scottish_tee_right", GameVoice.TeeClip("scottish", 3));
            Assert.AreEqual("metal_tee_center", GameVoice.TeeClip("metal", 0), "no tee is the center");
        }

        [Test]
        public void SeatsAreNumberedFromOne()
        {
            Assert.AreEqual("english_player_2", GameVoice.PlayerClip("english", 1));
            Assert.AreEqual("english_game_over_player_1", GameVoice.WinnerClip("english", 0));
            Assert.IsNull(GameVoice.WinnerClip("english", -1), "a tie says nothing");
            Assert.IsNull(GameVoice.PlayerClip("english", 4));
        }

        [Test]
        public void ATakeIsPickedFromTheVoicesOwn()
        {
            Assert.AreEqual("english_made_1", GameVoice.Pick("english", "made", n => 0));
            Assert.AreEqual("english_made_19", GameVoice.Pick("english", "made", n => n - 1));
            Assert.AreEqual("scottish_streak_8", GameVoice.Pick("scottish", "streak", n => 99), "held to the takes there are");
            Assert.IsNull(GameVoice.Pick("english", "nothing", n => 0));
        }

        [Test]
        public void AClipIsFetchedWithItsHash()
        {
            Assert.AreEqual("https://storage.googleapis.com/images.puttr.co/games/commentary/english_made_1.mp3?v=a1ce03d6", GameVoice.Url("english_made_1"));
            Assert.AreEqual("8f582221_english_tee_left.mp3", GameVoice.CacheName("english_tee_left"));
        }

        [Test]
        public void EachGameSpeaksInItsOwnVoice()
        {
            Assert.AreEqual("english", GameVoice.StyleFor("TOURJUICE").Voice);
            Assert.AreEqual("metal", GameVoice.StyleFor("METAL").Voice);
            Assert.AreEqual("scottish", GameVoice.StyleFor("REDUCER").Voice);
            Assert.AreEqual("scottish", GameVoice.StyleFor("OPEN").Voice);
            Assert.IsTrue(GameVoice.StyleFor("CENTERCUT").Streaks);
            Assert.IsFalse(GameVoice.StyleFor("DOORS").CallEveryTurn, "Doors calls the spot when it changes");
            Assert.IsTrue(GameVoice.StyleFor("OHONE").AlwaysCalls, "Oh-One always calls the next spot");
            Assert.IsTrue(GameVoice.StyleFor("DEADCENTER").Silent, "the app gave Dead Center no voice");
            Assert.IsTrue(GameVoice.StyleFor("PENDULUM").Silent);
            Assert.IsTrue(GameVoice.StyleFor("SWITCHBACK").Silent, "Switchback has its sounds and no commentary");
        }

        [Test]
        public void AStreakIsTheSeatsOwnMakesInARow()
        {
            var g = new TourJuiceGame();
            void Add(int seat, bool good) => g.Shots.Add(new Shot { Player = seat, Good = good });
            Add(0, true); Add(1, false); Add(0, false); Add(1, true); Add(0, true); Add(1, true); Add(0, true);
            Assert.AreEqual(2, GameVoice.MakesInARow(g, 0));
            Assert.AreEqual(2, GameVoice.MakesInARow(g, 1));
        }

        [Test]
        public void TheLabReadsItsPaceAloud()
        {
            Assert.AreEqual("dead center", GameVoice.SpokenRollout(0.3));
            Assert.AreEqual("1 inch past the hole", GameVoice.SpokenRollout(1.2));
            Assert.AreEqual("4 inches short of the hole", GameVoice.SpokenRollout(-3.6));
            Assert.AreEqual("unreadable", GameVoice.SpokenRollout(null));
            Assert.AreEqual("6 footer, Center tee", GameVoice.Footer(6, 2));
        }

        // ── Practice's read (Open.js sessionStats) ──

        static PracticePutt Putt(bool made, int feet = 6, int tee = 2, double score = 60, double? rollout = 4, string entry = "CENTER CUT", double off = 0, string miss = "LEFT") => new PracticePutt
        {
            Feet = feet, Tee = tee, Made = made, Score = score, Rollout = rollout,
            EntryWords = made ? entry : null, OffLine = made ? off : 0, MissDir = made ? null : miss,
        };

        [Test]
        public void TheStatsAreThoseFreePuttShows()
        {
            var putts = new List<PracticePutt>
            {
                Putt(true, 6, 2, 80, 4, "CENTER CUT", 5),
                Putt(false, 6, 1, 20, 10, miss: "LEFT"),
                Putt(true, 8, 3, 70, 2, "LEFT CENTER", -30),
                Putt(true, 8, 3, 64, 6, "RIGHT SIDE", 60),
            };
            PracticeStats s = PracticeStats.Of(putts);
            Assert.AreEqual(4, s.Total);
            Assert.AreEqual(3, s.Makes);
            Assert.AreEqual(1, s.Misses);
            Assert.AreEqual(75, s.MakePercent);
            Assert.AreEqual(2, s.CurrentStreak);
            Assert.IsTrue(s.StreakOfMakes);
            Assert.AreEqual(2, s.LongestMakeStreak);
            Assert.AreEqual(59, s.AvgScore);               // 234 / 4 = 58.5, rounded up as JavaScript does
            Assert.AreEqual(AppPutt.Grade(59), s.AvgGrade);
            Assert.AreEqual(80, s.BestScore);
            Assert.AreEqual(1, s.CenterCuts);
            Assert.AreEqual(1, s.LeftEntries);
            Assert.AreEqual(1, s.RightEntries);
            Assert.AreEqual(32, s.AvgEntryAngle);           // (5 + 30 + 60) / 3
            Assert.AreEqual(5.5, s.AvgSpeed.Value, 1e-9);
            Assert.AreEqual(2.0, s.MinSpeed, 1e-9);
            Assert.AreEqual(10.0, s.MaxSpeed, 1e-9);
            Assert.AreEqual(2, s.Distances.Count);
            Assert.AreEqual(6, s.Distances[0].feet);
            Assert.AreEqual(50, s.Distances[0].pct);
            Assert.AreEqual(85, s.Distances[0].pga);
            Assert.AreEqual(3, s.Breaks.Count, "a row for every tee putted from");
            Assert.AreEqual("Right break", s.Breaks[2].label);
            Assert.AreEqual(100, s.Breaks[2].pct);
            Assert.IsNull(PracticeStats.Of(new List<PracticePutt>()));
        }

        [Test]
        public void MissesLeanOneWayAfterThree()
        {
            var putts = new List<PracticePutt> { Putt(false, miss: "LEFT"), Putt(false, miss: "LEFT"), Putt(false, miss: "LEFT"), Putt(false, miss: "RIGHT") };
            PracticeStats s = PracticeStats.Of(putts);
            Assert.AreEqual("Pulling left", s.Tendency);
            Assert.IsFalse(s.StreakOfMakes);
            Assert.AreEqual(4, s.CurrentStreak);
            Assert.AreEqual("Neutral", PracticeStats.Of(new List<PracticePutt> { Putt(false, miss: "RIGHT"), Putt(false, miss: "LEFT") }).Tendency, "under three misses is not a tendency");
        }

        [Test]
        public void TheGoalsTargetsAreTheApps()
        {
            CollectionAssert.AreEqual(new[] { 5, 10, 15, 20 }, PracticeSession.TargetsFor(PracticeGoal.Streak));
            CollectionAssert.AreEqual(new[] { 25, 50, 100 }, PracticeSession.TargetsFor(PracticeGoal.Total));
            Assert.AreEqual(10, PracticeSession.DefaultTarget(PracticeGoal.Streak));
            Assert.AreEqual(50, PracticeSession.DefaultTarget(PracticeGoal.Total));
        }

        [Test]
        public void ThePuttsResultReadsAsTheAppsCard()
        {
            Assert.AreEqual("Left center", Putt(true, entry: "LEFT CENTER").ResultText);
            Assert.AreEqual("Missed right", Putt(false, miss: "RIGHT").ResultText);
            Assert.AreEqual("Right to left", PracticePutt.BreakLabel(3));
            Assert.AreEqual(1.0, PracticeStats.Iqr(new List<double> { 1, 2, 3 }), 1e-9);
            Assert.AreEqual(0.0, PracticeStats.Iqr(new List<double> { 1 }));
        }

        [Test]
        public void APathEntersTheHoleAtItsEdge()
        {
            var pts = new[] { new UnityEngine.Vector2(0, 20), new UnityEngine.Vector2(0, 5), new UnityEngine.Vector2(0, 0) };
            UnityEngine.Vector2? at = PuttPath.Entry(pts, UnityEngine.Vector2.zero, 10f);
            Assert.IsTrue(at.HasValue);
            Assert.AreEqual(10f, at.Value.y, 1e-4);
            Assert.IsNull(PuttPath.Entry(new[] { new UnityEngine.Vector2(30, 0), new UnityEngine.Vector2(40, 0) }, UnityEngine.Vector2.zero, 10f));
        }
    }
}
