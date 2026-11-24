// Greenlands (Greenlands.cs): the catalogue of eight worlds, a stage's rules (balls are lives, stars by misses, the
// gauntlet, the gate, pace, the mill's lamp, the bosses, the bonus green), what opens what, and how stars are kept.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class GreenlandsTests
    {
        static readonly AppPutt.Verdict Miss = new AppPutt.Verdict { Count = 0, Rollout = 14 };
        double _now;

        /// <summary>A make that comes in off the tee's line by this many degrees, rolling this far past.</summary>
        static AppPutt.Verdict Make(StageGame g, double off = 0, double? rollout = 12) => new AppPutt.Verdict { Count = 2, HasEntry = true, Entry = AppPutt.TeeEntry(g.Feet, g.Tee) + off, Rollout = rollout };

        static StageGame Start(string code)
        {
            var g = new StageGame(Greenlands.ByCode(code));
            g.Start();
            return g;
        }

        static void Clear(string code, int stars = 1) => GreenProgress.Record(Greenlands.ByCode(code), stars, 0, false);
        static void ClearWorld(int world) { foreach (GreenStage s in Greenlands.World(world).Path) GreenProgress.Record(s, 1, 0, false); }

        [SetUp] public void Clean() { GreenProgress.Reset(); GreenProgress.Read(""); _now = 100; StageGame.Clock = () => _now; }
        [TearDown] public void Restore() { StageGame.Clock = () => UnityEngine.Time.realtimeSinceStartupAsDouble; }

        // ── The catalogue ────────────────────────────────────────────────────

        [Test]
        public void EightWorldsOfSixStagesABossAndASecretAllInsideThreeToElevenFeet()
        {
            Assert.AreEqual(8, Greenlands.Worlds.Count);
            Assert.AreEqual(64, Greenlands.All.Count());
            Assert.AreEqual(64, Greenlands.All.Select(s => s.Code).Distinct().Count(), "every stage has its own code");
            foreach (GreenWorld w in Greenlands.Worlds)
            {
                Assert.AreEqual(8, w.Stages.Count, w.Name);
                Assert.AreEqual(6, w.Path.Count(s => s.Kind != StageKind.Boss), w.Name);
                Assert.AreEqual(StageKind.Boss, w.Path.Last().Kind, w.Name);
                Assert.AreEqual(StageKind.Bonus, w.Stages.Last().Kind, w.Name);
                Assert.AreEqual((2000 + w.Num).ToString(), w.QuestId);
                CollectionAssert.AreEqual(Enumerable.Range(1, 8), w.Stages.Select(s => s.LevelNum), w.Name + ": one quest_levels row a stage");
                foreach (GreenStage s in w.Stages)
                {
                    Assert.AreEqual(w.Num, s.World, s.Code);
                    Assert.That(s.NearFeet, Is.GreaterThanOrEqualTo(3), s.Code);
                    Assert.That(s.FarFeet, Is.LessThanOrEqualTo(11), s.Code);
                    Assert.That(s.Tees.All(t => t >= 1 && t <= 3), s.Code);
                    Assert.That(s.Bag, Is.GreaterThan(0), s.Code);
                    Assert.That(s.Goal, Is.GreaterThan(0), s.Code);
                    Assert.That(s.Rule, Is.Not.Empty, s.Code);
                }
            }
        }

        [Test]
        public void EveryTrailLeavesTheTopLeftOfTheMapForTheLegend()
        {
            // A node is about 30 map units across, with a star pill under it and a marker over it.
            foreach (GreenStage s in Greenlands.All)
                Assert.IsFalse(s.At.x - 30 < Greenlands.LegendRight && s.At.y - 50 < Greenlands.LegendBottom, s.Code + " at " + s.At + " is under the legend");
        }

        [Test]
        public void OnlyWorldOneIsOutsidePuttrClub()
        {
            Assert.IsFalse(Greenlands.World(1).Club);
            for (int w = 2; w <= 8; w++) Assert.IsTrue(Greenlands.World(w).Club, "world " + w);
        }

        [Test]
        public void EveryStageCanBeClearedWithCleanMakes()
        {
            foreach (GreenStage s in Greenlands.All)
            {
                var g = new StageGame(s);
                g.Start();
                int putts = 0;
                // A center-cut make at a pace inside whatever the stage asks, on a green lamp.
                while (!g.Over && putts++ < 40)
                {
                    double pace = s.PaceMin > 0 ? s.PaceMin + 4 : s.PaceMax > 0 ? s.PaceMax - 4 : 12;
                    g.Putt(Make(g, 0, pace));
                }
                Assert.IsTrue(g.Won, s.Code);
                Assert.AreEqual(3, g.Stars, s.Code);
                Assert.AreEqual(s.Goal, putts, s.Code + ": one putt a make");
            }
        }

        // ── The bag and the stars ────────────────────────────────────────────

        [Test]
        public void ACleanStageIsThreeStarsAndAMakeCostsNoBall()
        {
            StageGame g = Start("1-1");
            for (int i = 0; i < 3; i++) g.Putt(Make(g));
            Assert.IsTrue(g.Won);
            Assert.AreEqual(3, g.Stars);
            Assert.AreEqual(3, g.Balls, "makes are free");
        }

        [Test]
        public void EachMissCostsABallAndAStarDownToOne()
        {
            StageGame g = Start("1-1");
            g.Putt(Miss);
            Assert.AreEqual(2, g.Balls);
            Assert.AreEqual(2, g.StarsLive);
            g.Putt(Miss);
            Assert.AreEqual(1, g.StarsLive);
            for (int i = 0; i < 3; i++) g.Putt(Make(g));
            Assert.IsTrue(g.Won);
            Assert.AreEqual(1, g.Stars);
        }

        [Test]
        public void AnEmptyBagEndsTheStageWithNoStars()
        {
            StageGame g = Start("1-1");
            g.Putt(Make(g));
            for (int i = 0; i < 3; i++) g.Putt(Miss);
            Assert.IsTrue(g.Over);
            Assert.IsTrue(g.Lost);
            Assert.AreEqual(0, g.Stars);
            Assert.AreEqual("OUT OF BALLS", g.Last.Title);
        }

        [Test]
        public void TheTeeChangesOnlyOnAMake()
        {
            StageGame g = Start("1-2");
            Assert.AreEqual(1, g.Tee);
            g.Putt(Miss);
            Assert.AreEqual(1, g.Tee, "a miss is putted again from the same tee");
            g.Putt(Make(g));
            Assert.AreEqual(2, g.Tee);
            g.Putt(Make(g));
            Assert.AreEqual(3, g.Tee);
        }

        // ── What a make may have to pass ─────────────────────────────────────

        [Test]
        public void AGauntletMissStartsTheRunAgain()
        {
            StageGame g = Start("1-4");
            g.Putt(Make(g)); g.Putt(Make(g));
            g.Putt(Miss);
            Assert.AreEqual(0, g.Makes);
            Assert.AreEqual("RUN BROKEN", g.Last.Title);
            for (int i = 0; i < 3; i++) g.Putt(Make(g));
            Assert.IsTrue(g.Won);
            Assert.AreEqual(2, g.Stars);
        }

        [Test]
        public void AGateMakeThroughTheSideDoesNotCountAndCostsABall()
        {
            StageGame g = Start("1-6");
            g.Putt(Make(g, 40));
            Assert.AreEqual(0, g.Makes);
            Assert.AreEqual(3, g.Balls);
            Assert.AreEqual("SIDE DOOR", g.Last.Title);
            Assert.AreEqual("wide", g.Why);
            g.Putt(Make(g, 29));
            Assert.AreEqual(1, g.Makes, "inside half the window counts");
        }

        [Test]
        public void AFirmStageTurnsAwayASoftMake()
        {
            StageGame g = Start("3-2");   // 10 in. past or more
            g.Putt(Make(g, 0, 6));
            Assert.AreEqual(0, g.Makes);
            Assert.AreEqual("TOO SOFT", g.Last.Title);
            g.Putt(Make(g, 0, 10));
            Assert.AreEqual(1, g.Makes);
        }

        [Test]
        public void ASoftStageTurnsAwayAFirmMake()
        {
            StageGame g = Start("4-2");   // 20 in. past or less
            g.Putt(Make(g, 0, 26));
            Assert.AreEqual(0, g.Makes);
            Assert.AreEqual("TOO FIRM", g.Last.Title);
            g.Putt(Make(g, 0, 20));
            Assert.AreEqual(1, g.Makes);
        }

        [Test]
        public void APaceTheCameraCouldNotReadIsNotHeldAgainstTheMake()
        {
            StageGame g = Start("3-2");
            g.Putt(Make(g, 0, null));
            Assert.AreEqual(1, g.Makes);
        }

        [Test]
        public void TheLampBlocksAMakeOnRedAndForgivesOneJustAfterGreen()
        {
            StageGame g = Start("2-2");   // green 5 s, red 2.5 s, from the start
            GreenStage s = g.Stage;
            _now = 100 + 1;
            g.Putt(Make(g));
            Assert.AreEqual(1, g.Makes, "on green");
            _now = 100 + s.LampOpen + Greenlands.LampGrace - 0.1;
            g.Putt(Make(g));
            Assert.AreEqual(2, g.Makes, "just after it turned red: the ball was on its way");
            _now = 100 + s.LampOpen + Greenlands.LampGrace + 0.2;
            g.Putt(Make(g));
            Assert.AreEqual(2, g.Makes);
            Assert.AreEqual("BLOCKED", g.Last.Title);
            _now = 100 + s.LampOpen + s.LampShut + 0.5;
            g.Putt(Make(g));
            Assert.IsTrue(g.Won, "green again a cycle later");
        }

        // ── The bosses ───────────────────────────────────────────────────────

        [Test]
        public void ThreePuttBacksAwayAFootAHitAndFallsOnTheThird()
        {
            StageGame g = Start("1-B");
            Assert.AreEqual(3, g.Feet);
            g.Putt(Make(g));
            Assert.AreEqual("HIT", g.Last.Title);
            Assert.AreEqual(4, g.Feet);
            g.Putt(Make(g));
            Assert.AreEqual(5, g.Feet);
            g.Putt(Make(g));
            Assert.IsTrue(g.Won);
            Assert.AreEqual("BEATEN", g.Last.Title);
        }

        [Test]
        public void TheLipsWindowNarrowsWithEveryHit()
        {
            StageGame g = Start("5-B");
            Assert.AreEqual(60, g.WindowNow);
            g.Putt(Make(g, 28));
            Assert.AreEqual(50, g.WindowNow);
            g.Putt(Make(g, 28));
            Assert.AreEqual(50, g.WindowNow, "28 degrees is outside 25: no hit, the window stays");
            Assert.AreEqual(1, g.Makes);
            g.Putt(Make(g, 10));
            Assert.AreEqual(40, g.WindowNow);
        }

        [Test]
        public void HotHandIsBackToFullAfterAMiss()
        {
            StageGame g = Start("6-B");
            g.Putt(Make(g)); g.Putt(Make(g));
            g.Putt(Miss);
            Assert.AreEqual(0, g.Makes);
            StringAssert.Contains("back to full", g.Last.Sub);
        }

        // ── Markers and the bonus green ──────────────────────────────────────

        [Test]
        public void ACenterCutDropsAMarkerAndAWideMakeDoesNot()
        {
            StageGame g = Start("1-1");
            g.Putt(Make(g, 2));
            g.Putt(Make(g, 20));
            Assert.AreEqual(1, g.Markers);
        }

        [Test]
        public void TheBonusGreenIsOnePuttStarredByItsEntry()
        {
            StageGame a = Start("1-S"); a.Putt(Make(a, 3)); Assert.AreEqual(3, a.Stars);
            StageGame b = Start("1-S"); b.Putt(Make(b, 12)); Assert.AreEqual(2, b.Stars);
            StageGame c = Start("1-S"); c.Putt(Make(c, 40)); Assert.AreEqual(1, c.Stars);
            StageGame d = Start("1-S"); d.Putt(Miss); Assert.IsTrue(d.Lost);
        }

        // ── What opens what ──────────────────────────────────────────────────

        [Test]
        public void StagesOpenInOrderTheBossAfterAllSixAndTheSecretOnThreeStarsOnTheFourth()
        {
            GreenWorld w = Greenlands.World(1);
            Assert.IsTrue(GreenProgress.Open(Greenlands.ByCode("1-1")));
            Assert.IsFalse(GreenProgress.Open(Greenlands.ByCode("1-2")));
            Clear("1-1");
            Assert.IsTrue(GreenProgress.Open(Greenlands.ByCode("1-2")));
            Assert.AreEqual("1-2", GreenProgress.NextIn(w).Code);

            foreach (string code in new[] { "1-2", "1-3", "1-4", "1-5" }) Clear(code, 2);
            Assert.IsFalse(GreenProgress.Open(w.Boss), "five of six is not enough");
            Assert.IsFalse(GreenProgress.Open(w.Secret), "two stars on the fourth stage is not enough");
            Clear("1-6");
            Assert.IsTrue(GreenProgress.Open(w.Boss));
            Clear("1-4", 3);
            Assert.IsTrue(GreenProgress.Open(w.Secret));
        }

        [Test]
        public void AWorldOpensWhenTheBossBeforeItIsBeaten()
        {
            Assert.AreEqual(1, GreenProgress.Furthest);
            Assert.IsFalse(GreenProgress.WorldOpen(2));
            Assert.IsFalse(GreenProgress.Open(Greenlands.ByCode("2-1")), "nothing in a shut world is open");
            StringAssert.Contains("Three-Putt", GreenProgress.LockedWhy(Greenlands.ByCode("2-1")));
            ClearWorld(1);
            Assert.IsTrue(GreenProgress.WorldOpen(2));
            Assert.IsTrue(GreenProgress.Open(Greenlands.ByCode("2-1")));
            Assert.IsFalse(GreenProgress.WorldOpen(3));
            Assert.AreEqual(2, GreenProgress.Furthest);
            Assert.AreEqual("2-1", GreenProgress.NextIn(Greenlands.World(2)).Code);
        }

        [Test]
        public void TheReadsComeWithTheirBosses()
        {
            Assert.IsFalse(GreenProgress.HasMetronome);
            Clear("1-B");
            Assert.IsTrue(GreenProgress.HasMetronome, "the Metronome comes with Three-Putt");
            Assert.IsFalse(GreenProgress.HasGauge);
            Clear("5-B");
            Assert.IsTrue(GreenProgress.HasGauge, "the Entry gauge comes with The Lip");
        }

        // ── Keeping the stars ────────────────────────────────────────────────

        [Test]
        public void StarsOnlyGoUpAndMarkersAddUpAndBothReadBack()
        {
            GreenStage s = Greenlands.ByCode("1-1");
            GreenProgress.Record(s, 3, 2, false);
            GreenProgress.Record(s, 1, 3, false);
            GreenProgress.Record(Greenlands.ByCode("2-B"), 2, 0, false);
            Assert.AreEqual(3, GreenProgress.For(s));
            Assert.AreEqual(5, GreenProgress.Markers);
            Assert.AreEqual(5, GreenProgress.Total);
            Assert.AreEqual(192, GreenProgress.Possible);
            Assert.AreEqual("1-1:3,2-B:2|5", GreenProgress.Written());
        }

        [Test]
        public void TheCatalogMakesAStageByItsCodeAndTheSceneIsRegistered()
        {
            var row = new Dictionary<string, object> { { "gameType", Greenlands.GameType }, { "stage", "4-5" } };
            var g = RuleCatalog.Make(row) as StageGame;
            Assert.IsNotNull(g);
            Assert.AreEqual("4-5", g.Stage.Code);
            Assert.IsTrue(RuleViews.Has(Greenlands.GameType));
        }
    }
}
