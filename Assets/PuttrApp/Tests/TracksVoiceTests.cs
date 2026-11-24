// The Tracks booth's table and rules (puttr-app components/games/tracks/audio.js and audioManifest.json): which file a
// call or a line is, where it is fetched from, and which take is said next. Nothing here plays a sound or touches the network.
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class TracksVoiceTests
    {
        [Test]
        public void ACallIsTheFileForItsFeetAndTee()
        {
            Assert.AreEqual("vo_usa_call_7_2.mp3", TracksVoice.CallFile(7, 2));
            Assert.AreEqual("vo_usa_call_11_1.mp3", TracksVoice.CallFile(11, 1));
            Assert.AreEqual("vo_usa_call_3_3.mp3", TracksVoice.CallFile(3, 3));
        }

        [Test]
        public void ACallOutsideTheMatIsHeldToIt()
        {
            Assert.AreEqual("vo_usa_call_3_2.mp3", TracksVoice.CallFile(1, 2), "under 3 ft is the 3 ft call");
            Assert.AreEqual("vo_usa_call_11_3.mp3", TracksVoice.CallFile(14, 3), "over 11 ft is the 11 ft call");
            Assert.AreEqual("vo_usa_call_6_2.mp3", TracksVoice.CallFile(6, 0), "no tee is the center tee");
            Assert.AreEqual("vo_usa_call_6_2.mp3", TracksVoice.CallFile(6, 4));
        }

        [Test]
        public void AClipIsFetchedWithItsHash()
        {
            Assert.AreEqual("https://storage.googleapis.com/images.puttr.co/games/tracks/audio/v5/vo_usa_call_7_2.mp3?v=79751b95", TracksVoice.Url(TracksVoice.CallFile(7, 2)));
            Assert.AreEqual("https://storage.googleapis.com/images.puttr.co/games/tracks/audio/v5/vo_scot_score_good_5.mp3?v=994ad5f7", TracksVoice.Url(TracksVoice.LineFile("score_good", "scot", 5)));
            Assert.AreEqual("98fdb67b_vo_usa_round_start_1.mp3", TracksVoice.CacheName("vo_usa_round_start_1.mp3"));
        }

        [Test]
        public void TheTableIsTheManifest()
        {
            Assert.AreEqual(31, TracksVoice.Categories.Count);
            int lines = 0;
            foreach (string c in TracksVoice.Categories)
            {
                Assert.AreEqual(TracksVoice.Takes(c, "usa"), TracksVoice.Takes(c, "scot"), c + " has as many takes in each voice");
                Assert.GreaterOrEqual(TracksVoice.Takes(c, "usa"), 1, c);
                lines += TracksVoice.Takes(c, "usa") + TracksVoice.Takes(c, "scot");
            }
            Assert.AreEqual(242, lines);
            Assert.AreEqual(6, TracksVoice.Takes("score_good", "usa"));
            Assert.AreEqual(5, TracksVoice.Takes("tee_fairway", "scot"));
            Assert.AreEqual(2, TracksVoice.Takes("round_even", "usa"));
            Assert.AreEqual(0, TracksVoice.Takes("tee_fairway", "welsh"));
            Assert.IsNull(TracksVoice.LineFile("round_even", "usa", 3));
            Assert.IsNull(TracksVoice.LineFile("round_even", "usa", 0));
        }

        [Test]
        public void AnUnknownCategoryIsNotOne()
        {
            Assert.IsTrue(TracksVoice.IsCategory("putt_lip"));
            Assert.IsFalse(TracksVoice.IsCategory("putt_lipped"));
            Assert.IsFalse(TracksVoice.IsCategory(""));
            Assert.IsFalse(TracksVoice.IsCategory(null));
            Assert.AreEqual(0, TracksVoice.Takes("nonsense", "usa"));
            Assert.IsNull(TracksVoice.Pick("nonsense", "usa", n => 0));
        }

        [Test]
        public void NoTakeIsSaidTwiceRunning()
        {
            var dice = new Random(11);
            foreach (string c in TracksVoice.Categories)
                foreach (string voice in TracksVoice.Voices)
                {
                    int takes = TracksVoice.Takes(c, voice);
                    if (takes < 2) continue;
                    TracksVoice.Forget();
                    string last = null, before = null;
                    for (int i = 0; i < 60; i++)
                    {
                        string file = TracksVoice.Pick(c, voice, n => dice.Next(n));
                        StringAssert.StartsWith("vo_" + voice + "_" + c + "_", file);
                        Assert.AreNotEqual(last, file, c + " " + voice + " said the same take twice running");
                        if (takes >= 3) Assert.AreNotEqual(before, file, c + " " + voice + " said a take from the last two heard");
                        before = last; last = file;
                    }
                }
        }

        [Test]
        public void EveryTakeGetsItsTurn()
        {
            var dice = new Random(5);
            TracksVoice.Forget();
            var heard = new HashSet<string>();
            for (int i = 0; i < 40; i++) heard.Add(TracksVoice.Pick("score_good", "usa", n => dice.Next(n)));
            Assert.AreEqual(6, heard.Count);
        }

        [Test]
        public void TheOtherCommentatorUsuallyTakesTheNextLine()
        {
            Assert.AreEqual("scot", TracksVoice.ChooseVoice("usa", 0.1f, n => 0), "seven times in ten it is the other voice");
            Assert.AreEqual("usa", TracksVoice.ChooseVoice("scot", 0.69f, n => 1));
            Assert.AreEqual("usa", TracksVoice.ChooseVoice("usa", 0.9f, n => 0), "otherwise either, the same one included");
            Assert.AreEqual("scot", TracksVoice.ChooseVoice(null, 0.1f, n => 1), "nobody has spoken yet: either");
        }

        [Test]
        public void ScoresWaitAndShotChatterDoesNot()
        {
            foreach (string big in new[] { "round_start", "round_under", "round_even", "round_over", "score_good", "score_bad", "score_pickup", "putt_made_long" }) Assert.IsTrue(TracksVoice.IsBig(big), big);
            foreach (string small in new[] { "tee_fairway", "approach_on", "putt_lip", "gimme", "lag_good" }) Assert.IsFalse(TracksVoice.IsBig(small), small);
        }

        [Test]
        public void GoodAndBadMomentsGetAReaction()
        {
            Assert.AreEqual("react_good", TracksVoice.Reaction("approach_tight"));
            Assert.AreEqual("react_good", TracksVoice.Reaction("score_good"));
            Assert.AreEqual("react_bad", TracksVoice.Reaction("tee_water"));
            Assert.AreEqual("react_bad", TracksVoice.Reaction("putt_lip"));
            Assert.IsNull(TracksVoice.Reaction("tee_rough"));
            Assert.IsNull(TracksVoice.Reaction("gimme"));
            Assert.IsNull(TracksVoice.Reaction("react_good"), "a reaction is not reacted to");
        }

        [Test]
        public void PreloadFetchesTheCallsThenTheLines()
        {
            Assert.AreEqual(27, TracksVoice.PreloadList(null, false).Count, "without the lines, only the calls");
            Assert.AreEqual(27 + 242, TracksVoice.PreloadList(null, true).Count);
            List<string> usa = TracksVoice.PreloadList("usa", true);
            Assert.AreEqual(27 + 121, usa.Count);
            Assert.AreEqual("vo_usa_call_3_1.mp3", usa[0]);
            Assert.IsFalse(usa.Exists(f => f.StartsWith("vo_scot_")));
            Assert.AreEqual(27 + 121, TracksVoice.PreloadList("scot", true).Count);
        }
    }
}
