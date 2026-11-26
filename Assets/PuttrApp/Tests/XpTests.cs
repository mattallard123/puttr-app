// XP: the level and rank a total stands for. The awards themselves are the API's (puttr-api test/xp.test.js).
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class XpTests
    {
        [Test]
        public void AThousandXpIsALevel()
        {
            Assert.AreEqual(1, Xp.Level(0));
            Assert.AreEqual(1, Xp.Level(999));
            Assert.AreEqual(2, Xp.Level(1000));
            Assert.AreEqual(14, Xp.Level(13760));
            Assert.AreEqual(760, Xp.Into(13760));
            Assert.AreEqual(240, Xp.ToNext(13760));
            Assert.AreEqual(1, Xp.Level(-5), "never below level 1");
        }

        [Test]
        public void ARankStartsAtARoundLevel()
        {
            int[] first = { 1, 5, 10, 20, 50, 100, 150, 250, 500 };
            for (int rank = 0; rank < first.Length; rank++) Assert.AreEqual(first[rank], Xp.FirstLevel(rank), Xp.Ranks[rank]);
            Assert.AreEqual("Rookie", Xp.RankName(1));
            Assert.AreEqual("Rookie", Xp.RankName(4));
            Assert.AreEqual("Dance Floor", Xp.RankName(5));
            Assert.AreEqual("Weekender", Xp.RankName(10));
            Assert.AreEqual("Weekender", Xp.RankName(Xp.Level(13760)));
            Assert.AreEqual("Scratch", Xp.RankName(20));
            Assert.AreEqual("Club Champ", Xp.RankName(50));
            Assert.AreEqual("State Am", Xp.RankName(100));
            Assert.AreEqual("Tour", Xp.RankName(150));
            Assert.AreEqual("Tour", Xp.RankName(249));
            Assert.AreEqual("Major", Xp.RankName(250));
            Assert.AreEqual("Major", Xp.RankName(499));
            Assert.AreEqual("Legend", Xp.RankName(500));
            Assert.AreEqual("Legend", Xp.RankName(8020), "Legend has no ceiling");
        }

        [Test]
        public void TheLadderAddsUp()
        {
            Assert.AreEqual(9, Xp.Ranks.Length);
            Assert.AreEqual(0, Xp.XpToReach(0));
            Assert.AreEqual(4000, Xp.XpToReach(1));
            Assert.AreEqual(499000, Xp.XpToReach(8));
            Assert.AreEqual(6, Xp.LevelsToNextRank(14));
            Assert.AreEqual(4, Xp.LevelsToNextRank(1));
            Assert.AreEqual(100, Xp.LevelsToNextRank(400));
            Assert.AreEqual(0, Xp.LevelsToNextRank(700));
            // Each rank starts above the one before, and its first level stands in it.
            for (int rank = 1; rank < Xp.Ranks.Length; rank++)
            {
                Assert.Greater(Xp.XpToReach(rank), Xp.XpToReach(rank - 1), Xp.Ranks[rank]);
                Assert.AreEqual(rank, Xp.Rank(Xp.FirstLevel(rank)), Xp.Ranks[rank]);
                Assert.AreEqual(rank - 1, Xp.Rank(Xp.FirstLevel(rank) - 1), Xp.Ranks[rank]);
            }
        }

        [Test]
        public void EveryRankHasItsOwnColour()
        {
            var seen = new System.Collections.Generic.HashSet<UnityEngine.Color>();
            for (int rank = 0; rank < Xp.Ranks.Length; rank++) Assert.IsTrue(seen.Add(Xp.Colour(Xp.FirstLevel(rank))), Xp.Ranks[rank]);
        }
    }
}
