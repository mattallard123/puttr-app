// Difficulty and game names: the distances a level deals, which level posts to the leaderboard, the card meter and
// the Play tab's filter (puttr-app common/GameTile.js, PlayScreen.js), and the name a rule game shows.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class DifficultyTests
    {
        static readonly AppPutt.Verdict Missed = new AppPutt.Verdict { Count = 0, Rollout = 14 };

        static RuleGame Make(string type, string title = null)
        {
            var row = new Dictionary<string, object> { { "gameType", type } };
            if (title != null) row["title"] = title;
            return RuleCatalog.Make(row);
        }

        // Every distance the game deals in one round at a level, in the order dealt (missing every putt).
        static List<int> Dealt(string type, int level)
        {
            RuleGame g = Make(type);
            g.Difficulty = level;
            g.Start();
            var feet = new List<int>();
            while (!g.Over && feet.Count < 200) { feet.Add(g.Feet); g.Putt(Missed); }
            return feet;
        }

        [Test]
        public void EasyAndHardDealTheShortAndLongRangesAndMediumTheGamesOwn()
        {
            Assert.AreEqual((3, 6), GameLevels.Band(GameLevels.Easy));
            Assert.AreEqual((3, 11), GameLevels.Band(GameLevels.Medium));
            Assert.AreEqual((8, 11), GameLevels.Band(GameLevels.Hard));
            Assert.AreEqual((6, 9), GameLevels.Band(GameLevels.Medium, 6, 9), "Medium keeps the range the game gives");
            Assert.AreEqual((3, 6), GameLevels.Band(-4), "a level below Easy is Easy");
            Assert.AreEqual((8, 11), GameLevels.Band(9), "a level above Hard is Hard");
        }

        [Test]
        public void NinePuttsAreOneOfEachDistanceOnMediumAndStayInRangeOtherwise()
        {
            CollectionAssert.AreEqual(Enumerable.Range(3, 9).ToList(), Scoring.NineFeet(GameLevels.Medium));
            List<int> easy = Scoring.NineFeet(GameLevels.Easy), hard = Scoring.NineFeet(GameLevels.Hard);
            Assert.AreEqual(9, easy.Count);
            Assert.AreEqual(9, hard.Count);
            Assert.IsTrue(easy.All(f => f >= 3 && f <= 6));
            Assert.IsTrue(hard.All(f => f >= 8 && f <= 11));
            for (int f = 3; f <= 6; f++) Assert.GreaterOrEqual(easy.Count(x => x == f), 2, f + " ft on Easy");
            for (int f = 8; f <= 11; f++) Assert.GreaterOrEqual(hard.Count(x => x == f), 2, f + " ft on Hard");
        }

        [Test]
        public void TheGamesThatDealTheirOwnDistancesFollowTheLevel()
        {
            foreach (string type in new[] { "HIGHSTRIKER", "HIGHSIDE", "SWITCHBACK" })
            {
                Assert.IsNotNull(Make(type).DifficultyNotes, type + " offers Easy, Medium and Hard");
                Assert.AreEqual(3, Make(type).DifficultyNotes.Length, type);
                List<int> easy = Dealt(type, GameLevels.Easy), medium = Dealt(type, GameLevels.Medium), hard = Dealt(type, GameLevels.Hard);
                Assert.Greater(easy.Count, 0, type);
                Assert.AreEqual(medium.Count, easy.Count, type + ": the same number of putts at every level");
                Assert.AreEqual(medium.Count, hard.Count, type + ": the same number of putts at every level");
                Assert.IsTrue(easy.All(f => f >= 3 && f <= 6), type + " on Easy: " + string.Join(",", easy));
                Assert.IsTrue(medium.All(f => f >= 3 && f <= 11), type + " on Medium: " + string.Join(",", medium));
                Assert.IsTrue(hard.All(f => f >= 8 && f <= 11), type + " on Hard: " + string.Join(",", hard));
            }
        }

        [Test]
        public void MediumIsTheGameAsItWasDealt()
        {
            foreach (string type in new[] { "TOURJUICE", "CENTERCUT", "SPEEDDEMON", "HIGHSTRIKER" })
                CollectionAssert.AreEqual(Enumerable.Range(3, 9).ToList(), Dealt(type, GameLevels.Medium).OrderBy(f => f).ToList(), type + ": one putt from each distance");
            Assert.AreEqual(GameLevels.Medium, Make("CENTERCUT").Difficulty, "a game nobody set a level on plays Medium");
        }

        [Test]
        public void OnlyMediumPostsToTheLeaderboard()
        {
            Assert.IsFalse(GameLevels.Posts(GameLevels.Easy));
            Assert.IsTrue(GameLevels.Posts(GameLevels.Medium));
            Assert.IsFalse(GameLevels.Posts(GameLevels.Hard));
            RuleGame g = Make("HIGHSTRIKER");
            Assert.IsTrue(g.PostsBoard);
            g.Difficulty = GameLevels.Easy;
            Assert.IsFalse(g.PostsBoard);
            g.Difficulty = GameLevels.Hard;
            Assert.IsFalse(g.PostsBoard);
            Assert.AreEqual("Easy and Hard scores are not posted to the leaderboard.", GameLevels.NotPosted(GameLevels.Names));
        }

        [Test]
        public void AGameWithFixedPuttsOrItsOwnDifficultyHasNoLevelAndAlwaysPosts()
        {
            foreach (string type in new[] { "FULLWORKOUT", "ASSESSMENT", "DOORS", "CLOCK", "REDUCER", "LAB", "LEAPFROG", "GEARS", "GROOVE", "TOURJUICE", "CENTERCUT", "SPEEDDEMON", "METAL" })
            {
                RuleGame g = Make(type);
                Assert.IsNull(g.DifficultyNotes, type);
                g.Difficulty = GameLevels.Hard;   // nothing sets it for these, and it would change nothing if something did
                Assert.IsTrue(g.PostsBoard, type);
            }
            CollectionAssert.AreEqual(Dealt("FULLWORKOUT", GameLevels.Medium).OrderBy(f => f).ToList(), Dealt("FULLWORKOUT", GameLevels.Hard).OrderBy(f => f).ToList(), "Full Workout's 27 putts are the same at any level");
            foreach (string type in new[] { "TOURJUICE", "CENTERCUT", "SPEEDDEMON" })
                CollectionAssert.AreEqual(Enumerable.Range(3, 9).ToList(), Dealt(type, GameLevels.Hard).OrderBy(f => f).ToList(), type + ": the nine from 3 to 11 ft, whatever the level");
            Assert.IsTrue(Dealt("METAL", GameLevels.Hard).All(f => f >= 3 && f <= 11), "Metal Play deals 3 to 11 ft whatever the level");
        }

        [Test]
        public void TheCardMeterLightsOneTwoOrThreeBars()
        {
            Assert.AreEqual(1, GameLevels.Bars(0));
            Assert.AreEqual(1, GameLevels.Bars(3));
            Assert.AreEqual(2, GameLevels.Bars(3.1));
            Assert.AreEqual(2, GameLevels.Bars(6));
            Assert.AreEqual(3, GameLevels.Bars(6.5));
            Assert.AreEqual(3, GameLevels.Bars(10));
            Assert.IsFalse(GameLevels.Rated(new Dictionary<string, object> { { "title", "Tracks" } }), "a row with no rating shows no meter");
            var row = new Dictionary<string, object> { { "difficulty", "8.7" } };
            Assert.IsTrue(GameLevels.Rated(row));
            Assert.AreEqual(8.7, GameLevels.Rating(row), 0.0001, "the API sends the rating as text");
        }

        [Test]
        public void TheDifficultyFilterPutsEveryRatingInOneBand()
        {
            foreach (double rating in new[] { 0, 1.5, 3, 3.5, 4, 6, 7, 7.5, 8, 10 })
            {
                Assert.IsTrue(GameLevels.InFilter(rating, 0), "All levels shows everything");
                int bands = Enumerable.Range(1, 3).Count(f => GameLevels.InFilter(rating, f));
                Assert.AreEqual(1, bands, "a game rated " + rating + " is in exactly one band");
            }
            Assert.IsTrue(GameLevels.InFilter(3, 1));
            Assert.IsTrue(GameLevels.InFilter(3.5, 1), "between the app's bands: Beginner");
            Assert.IsTrue(GameLevels.InFilter(4, 2));
            Assert.IsTrue(GameLevels.InFilter(7, 2));
            Assert.IsTrue(GameLevels.InFilter(7.5, 2), "between the app's bands: Intermediate");
            Assert.IsTrue(GameLevels.InFilter(8, 3));
            Assert.AreEqual(4, GameLevels.Filters.Length);
        }

        [Test]
        public void ArcadeGamesGetTheirOwnKindOfDifficulty()
        {
            Assert.AreEqual(GameLevels.Kind.None, GameLevels.ArcadeKind("On Fire"), "On Fire's Distance is its difficulty");
            Assert.AreEqual(GameLevels.Kind.None, GameLevels.ArcadeKind(null));
            Assert.AreEqual(GameLevels.Kind.Distance, GameLevels.ArcadeKind("Balloon Pop"));
            Assert.AreEqual(GameLevels.Kind.Distance, GameLevels.ArcadeKind("Beer Pong"));
            foreach (string name in new[] { "Archery", "Cricket", "Derby", "Major League", "Lanes", "Putt Star", "Dead Weight", "Send It", "Slingshot", "Spot Kick", "Tug of War", "Turf War", "Under the Cup", "Windmill", "Putt-ball" })
            {
                Assert.AreEqual(GameLevels.Kind.Levels, GameLevels.ArcadeKind(name), name);
                Assert.AreEqual(3, GameLevels.ArcadeNotes(name).Length, name);
                Assert.AreEqual(3, GameLevels.Words(name).Length, name);
            }
            CollectionAssert.AreEqual(new[] { "Rookie", "Pro", "All-Star" }, GameLevels.Words("Major League"));
            CollectionAssert.AreEqual(new[] { "Easy", "Standard", "Hard" }, GameLevels.Words("Putt Star"));
            CollectionAssert.AreEqual(new[] { "Easy", "Medium", "Hard" }, GameLevels.Words("Derby"));
        }

        [Test]
        public void LevelsFollowTheirPlayersToTheirSeats()
        {
            // Three Players on Easy, Medium and Hard; the second goes first, so sits first.
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, GameLevels.Seated(new List<int> { 0, 1, 2 }, 1));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, GameLevels.Seated(new List<int> { 0, 1, 2 }, 0));
            Assert.AreEqual("Difficulty: Pro", GameLevels.PauseNote(GameLevels.Words("Major League"), new List<int> { 1, 1 }, new List<string> { "Ana", "Ben" }));
            Assert.AreEqual("Ana: Easy   ·   Ben: Hard", GameLevels.PauseNote(GameLevels.Names, new List<int> { 0, 2 }, new List<string> { "Ana", "Ben" }));
            Assert.IsNull(GameLevels.PauseNote(GameLevels.Names, null, null));
        }

        [Test]
        public void ARuleGameShowsTheCatalogueRowsTitleElseItsOwn()
        {
            Assert.AreEqual("Center Cut", Make("CENTERCUT").Title);
            Assert.AreEqual("Sunday Special", Make("CENTERCUT", "Sunday Special").Title, "a renamed or custom row shows its own name");
            Assert.AreEqual("Center Cut", Make("CENTERCUT", "CENTERCUT").Title, "a row titled only with its type (a script's) has no name of its own");
            Assert.AreEqual("Center Cut", Make("CENTERCUT", "  ").Title);
            Assert.AreEqual("Horse", Make("HORSE").Title);
            Assert.AreEqual("Oh-One", Make("OHONE").Title);
            Assert.AreEqual("Metal Play", Make("METAL").Title);
            Assert.AreEqual("Match Play", Make("MATCH").Title);
            Assert.AreEqual("High Striker", Make("HIGHSTRIKER").Title);
            Assert.AreEqual("Around the Clock", Make("CLOCK").Title);
            foreach (string type in RuleCatalog.Types) Assert.IsNotEmpty(Make(type).Title, type);
        }

        [Test]
        public void ARuleGamesOwnDifficultyIsNamedForThePauseCard()
        {
            Assert.AreEqual("Difficulty: Medium", GameLevels.PauseNote(Make("HIGHSTRIKER")));
            Assert.IsNull(GameLevels.PauseNote(Make("TOURJUICE")), "no Easy / Medium / Hard: nothing to say");
            Assert.AreEqual("Difficulty: Standard (3 doors)", GameLevels.PauseNote(Make("DOORS")));
            Assert.AreEqual("Difficulty: Standard (5 sectors)", GameLevels.PauseNote(Make("CLOCK")));
            Assert.AreEqual("Difficulty: 120° window", GameLevels.PauseNote(Make("REDUCER")));
            Assert.AreEqual("Difficulty: Start 301", GameLevels.PauseNote(Make("OHONE")));
            Assert.IsNull(GameLevels.PauseNote(Make("FULLWORKOUT")), "fixed putts: nothing to say");
        }
    }
}
