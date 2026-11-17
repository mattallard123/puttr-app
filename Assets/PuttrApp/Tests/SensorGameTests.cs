// The Vertex sensor games: the app's numbers (puttr-app games/*.js) on strokes made to order.
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class SensorGameTests
    {
        static readonly AppPutt.Verdict Made = new AppPutt.Verdict { Count = 2, Rollout = 1 }, Missed = new AppPutt.Verdict { Count = 0, Rollout = 14 };

        static SensorStroke Good(double tempo = 72, double rhythm = 2.0, double face = 0, double lie = 0, double mm = 280) => new SensorStroke
        {
            Tempo = tempo, Rhythm = rhythm, Face = face, Lie = lie, LengthMm = mm, Speed = 1.5,
            Total = 60 / tempo, Back = 60 / tempo * rhythm / (rhythm + 1), Forward = 60 / tempo / (rhythm + 1), BackRot = -1.5, FwdRot = face + 1.5,
        };

        static Shot Play(SensorGame g, SensorStroke s, AppPutt.Verdict v) { g.Pending = s; return g.Putt(v); }

        static RuleGame Make(string type) => RuleCatalog.Make(new Dictionary<string, object> { { "gameType", type } });

        [Test]
        public void EveryGamePlaysToTheEndOnMadeUpStrokes()
        {
            var dice = new Random(3);
            foreach (string type in SensorCatalog.Types)
            {
                var g = Make(type) as SensorGame;
                Assert.IsNotNull(g, type);
                Assert.Greater(g.HowTo.Length, 5, type + " has its briefing");
                g.Start();
                RuleViews.State(g);
                int n = 0;
                while (!g.Over && n < 600)
                {
                    Shot s = Play(g, SensorStroke.Make(dice), dice.NextDouble() < 0.5 ? Made : Missed);
                    Assert.IsNotNull(s.Title, type);
                    StringAssert.DoesNotContain("—", s.Title + s.Sub, type + ": no em dashes");
                    var view = new Dictionary<string, object>();
                    g.Describe(view);
                    Assert.IsTrue((bool)view["has"], type);
                    n++;
                }
                Assert.IsTrue(g.Over, type + " ends");
                Assert.IsNotNull(g.Board() ?? (object)"none");
            }
        }

        [Test]
        public void PendulumPocketStreakAndMakeBonus()
        {
            var g = new PendulumGame { Target = 72 };
            g.Start();
            int feet = g.Feet;
            Shot a = Play(g, Good(tempo: 73), Missed);
            Assert.AreEqual(feet * 10, a.Points);
            Assert.AreEqual("IN THE POCKET ×1", a.Title);
            int feet2 = g.Feet;
            Shot b = Play(g, Good(tempo: 70), Made);
            Assert.AreEqual(Math.Round(feet2 * 10 * 2 * 1.5, MidpointRounding.AwayFromZero), b.Points);
            int feet3 = g.Feet;
            Shot c = Play(g, Good(tempo: 78.5), Missed);   // 6.5 off: half credit, streak gone
            Assert.AreEqual(Math.Round(feet3 * 10 * (10 - 6.5) / 7, MidpointRounding.AwayFromZero), c.Points);
            Assert.AreEqual("TOO QUICK", c.Title);
            Assert.AreEqual(0, Play(g, Good(tempo: 60), Missed).Points);
        }

        [Test]
        public void WaltzDoublesTheThirdInTime()
        {
            var g = new WaltzGame { Target = 2.0 };
            g.Start();
            Assert.AreEqual(72, g.ClickBpm);
            Assert.AreEqual(100, Play(g, Good(rhythm: 2.1), Missed).Points);
            Assert.AreEqual(125, Play(g, Good(rhythm: 1.9), Made).Points);
            Shot w = Play(g, Good(rhythm: 2.0), Missed);
            Assert.AreEqual(200, w.Points);
            Assert.AreEqual("WALTZ ×2", w.Title);
            Assert.AreEqual(66, g.ClickBpm, "round 2 clicks slower");
            Assert.AreEqual(50, Play(g, Good(rhythm: 2.45), Missed).Points);
            Assert.IsFalse(g.Arm(false) && g.Pending != null && g.Pending.Rhythm <= 0);
        }

        [Test]
        public void PurePurityIsTheScoreAndAMakeDoublesIt()
        {
            var g = new PureGame();
            g.Start();
            Shot a = Play(g, Good(), Made);
            Assert.AreEqual(200, a.Points);
            Assert.AreEqual("PURE STRIKE, MADE ×2", a.Title);
            // face 1.4° off scores 61, rhythm 2.7 scores 61, lie 2° off scores 61
            Assert.AreEqual(61, SensorMath.Bell(1.4, 0, 1.4));
            Shot b = Play(g, Good(face: 1.4, rhythm: 2.7, lie: 2), Missed);
            Assert.AreEqual(61, b.Points);
            Assert.AreEqual("MISS", b.Title);
        }

        [Test]
        public void CarbonCopyClonesTheSignature()
        {
            var g = new CarbonCopyGame();
            g.Start();
            Assert.AreEqual("SIGNATURE RECORDED", Play(g, Good(), Missed).Title);
            Shot c = Play(g, Good(), Missed);
            Assert.AreEqual(200, c.Points);
            Assert.AreEqual("CARBON COPY ×2", c.Title);
            Shot d = Play(g, Good(tempo: 78), Missed);   // one tolerance off on tempo: 61, others 100
            Assert.AreEqual(90, d.Points / 2);
            for (int i = 0; i < 12; i++) Play(g, Good(), Missed);
            Assert.IsTrue(g.Over);
            Assert.AreEqual(0.45, CarbonCopyGame.TolFor(13));
        }

        [Test]
        public void SquareUpGateFromYourNormal()
        {
            var g = new SquareUpGame();
            g.Start();
            Play(g, Good(face: 1.0), Missed);
            Play(g, Good(face: 3.0), Missed);
            Assert.AreEqual("GATE SET", Play(g, Good(face: 1.2), Missed).Title);
            Assert.AreEqual(1.2, g.Center.Value, 1e-9);
            Assert.AreEqual("SQUARE ENOUGH", Play(g, Good(face: 3.1), Missed).Title);
            Play(g, Good(face: 0), Missed);
            Assert.AreEqual("LEVEL CLEARED", Play(g, Good(face: 1.2), Made).Title);
            Assert.AreEqual(1.75, g.Gate, 1e-9);
            Assert.AreEqual("TOO CLOSED", Play(g, Good(face: 3.0), Made).Title);
            Play(g, Good(face: -1), Missed);
            Assert.AreEqual("TOO OPEN", Play(g, Good(face: -1), Missed).Title);
            Assert.IsTrue(g.Over);
            Assert.AreEqual(3, g.Saved);
            Assert.AreEqual(0.5, SquareUpGame.GateFor(20));
        }

        [Test]
        public void StretchBandTempoLockAndElastic()
        {
            Assert.AreEqual((9.0, 13.0, 11.0), StretchGame.BandFor(6));
            Assert.AreEqual((11.5, 15.5, 13.5), StretchGame.BandFor(11));
            var g = new StretchGame();
            g.Start();
            Assert.That(g.Feet, Is.InRange(3, 6));
            for (int i = 0; i < 3; i++)
            {
                double mid = StretchGame.BandFor(g.Feet).center;
                Shot s = Play(g, Good(tempo: 70, mm: mid * 25.4), new AppPutt.Verdict { Count = 0, Rollout = 0 });
                Assert.AreEqual(i < 2 ? 20 : 60, s.Points, "putt " + (i + 1));
            }
            Assert.AreEqual(70, g.Lock.Value);
            Assert.That(g.Feet, Is.InRange(8, 11));
            Shot off = Play(g, Good(tempo: 80, mm: 4 * 25.4), new AppPutt.Verdict { Count = 0, Rollout = 6 });
            Assert.AreEqual(5, off.Points);
            Assert.AreEqual("OUT OF BAND", off.Title);
        }

        [Test]
        public void LockdownTightensAndEndsOnThreeBreaks()
        {
            var g = new LockdownGame();
            g.Start();
            for (int i = 0; i < 3; i++) Play(g, Good(), Missed);
            Shot a = Play(g, Good(), Missed);
            Assert.AreEqual("LOCKDOWN ×1", a.Title);
            Assert.AreEqual(30, a.Points);
            Assert.AreEqual(0.95, g.Band, 1e-9);
            Shot b = Play(g, Good(), Missed);
            Assert.AreEqual(60, b.Points);
            for (int i = 0; i < 3; i++) Assert.IsFalse(Play(g, Good(tempo: 90), Missed).Good);
            Assert.IsTrue(g.Over);
        }

        [Test]
        public void TheYipsDamageStrikesAndStages()
        {
            var g = new TheYipsGame();
            g.Start();
            int feet = g.Feet;
            Shot a = Play(g, Good(), Made);
            Assert.AreEqual(feet * 2 * 2, a.Points);
            Assert.IsTrue(a.Title.StartsWith("CRIT"));
            Shot b = Play(g, Good(face: 3, rhythm: 1.2), Missed);
            Assert.AreEqual(Math.Min(20, 13) + 8, g.LastTaken);
            Assert.AreEqual(1, TheYipsGame.StageFor(200));
            Assert.AreEqual(2, TheYipsGame.StageFor(150));
            Assert.AreEqual(3, TheYipsGame.StageFor(82));
            g.BossHp = 1;
            Play(g, Good(), Made);
            Assert.IsTrue(g.Over && g.Won);
            Assert.AreEqual(250 + g.PlayerHp, g.Saved);
        }

        [Test]
        public void RawBurstSavesAsTheAppDoes()
        {
            var packets = new byte[6][];
            for (int i = 0; i < 6; i++) { packets[i] = new byte[20]; packets[i][0] = (byte)i; }
            string json = SensorMath.RawJson(packets);
            StringAssert.StartsWith("[[0,0,0", json);
            Assert.AreEqual(6, J.List(J.Parse(json)).Count);
            Assert.AreEqual(11.0, SensorMath.MmToIn(-280));
        }
    }
}
