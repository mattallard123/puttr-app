// What the analytics dashboard is cut by (the kind of device, its OS), how a game's start and end are read off
// the play records, and when the feedback card may come up.
using NUnit.Framework;
using UnityEngine;

namespace PuttrApp.Tests
{
    public class AnalyticsTests
    {
        [Test]
        public void DeviceClassIsTvThenTabletThenMobile()
        {
            Assert.AreEqual("tv", Analytics.DeviceClass(true, true, true));
            Assert.AreEqual("tablet", Analytics.DeviceClass(false, true, true));
            Assert.AreEqual("mobile", Analytics.DeviceClass(false, false, true));
            Assert.AreEqual("desktop", Analytics.DeviceClass(false, false, false));
        }

        [Test]
        public void PlatformIsNamedAsThePuttrAppNamesIt()
        {
            Assert.AreEqual("ios", Analytics.PlatformName("", RuntimePlatform.IPhonePlayer));
            Assert.AreEqual("android", Analytics.PlatformName("", RuntimePlatform.Android));
            Assert.AreEqual("androidtv", Analytics.PlatformName("androidtv", RuntimePlatform.Android));
            Assert.AreEqual("tvos", Analytics.PlatformName("tvos", RuntimePlatform.tvOS));
            Assert.AreEqual("web", Analytics.PlatformName("web", RuntimePlatform.WebGLPlayer));
        }

        [Test]
        public void OsIsNamedFromWhatTheDeviceSays()
        {
            Assert.AreEqual("iOS", Analytics.OsName("ios", "iOS 27.1", "iPhone17,1"));
            Assert.AreEqual("iPadOS", Analytics.OsName("ios", "iPadOS 27.1", "iPad16,3"));
            Assert.AreEqual("iPadOS", Analytics.OsName("ios", "iOS 17.0", "iPad13,1"));
            Assert.AreEqual("tvOS", Analytics.OsName("tvos", "tvOS 27.0", "AppleTV14,1"));
            Assert.AreEqual("Android", Analytics.OsName("android", "Android OS 14 / API-34 (UP1A/123)", "Google Pixel 8"));
            Assert.AreEqual("Android TV", Analytics.OsName("androidtv", "Android OS 12 / API-31", "Sony BRAVIA 4K"));
            Assert.AreEqual("Fire OS", Analytics.OsName("androidtv", "Android OS 9 / API-28", "Amazon AFTKA"));
            Assert.AreEqual("Fire OS", Analytics.OsName("android", "Android OS 11 / API-30", "Amazon KFTRWI"));
            Assert.AreEqual("Tizen", Analytics.OsName("web", "", ""));
            Assert.AreEqual("macOS", Analytics.OsName("macos", "Mac OS X 15.1.0", "Mac15,6"));
        }

        [Test]
        public void OsVersionIsTheFirstNumber()
        {
            Assert.AreEqual("27.1", Analytics.OsVersion("iOS 27.1"));
            Assert.AreEqual("14", Analytics.OsVersion("Android OS 14 / API-34 (UP1A/123)"));
            Assert.AreEqual("15.1", Analytics.OsVersion("Mac OS X 15.1.0"));
            Assert.AreEqual("", Analytics.OsVersion(null));
        }

        [Test]
        public void ScreensGoByTheirClassLessScreen()
        {
            Assert.AreEqual("Settings", Analytics.ScreenName(typeof(SettingsScreen)));
            Assert.AreEqual("Tournaments", Analytics.ScreenName(typeof(TournamentsScreen)));
        }

        [Test]
        public void PlayRecordsOpenAndCloseAGame()
        {
            Assert.AreEqual("started", Analytics.PhaseOf("POST", "/game/new/"));
            Assert.AreEqual("started", Analytics.PhaseOf("POST", "/tournaments/game/new/"));
            Assert.AreEqual("completed", Analytics.PhaseOf("POST", "/game/over/"));
            Assert.AreEqual("completed", Analytics.PhaseOf("POST", "/game/multiplayer/over"));
            Assert.AreEqual("completed", Analytics.PhaseOf("POST", "/tournaments/game/end/"));
            Assert.AreEqual("abandoned", Analytics.PhaseOf("POST", "/game/abandon"));
            Assert.IsNull(Analytics.PhaseOf("POST", "/game/savePutt/"));
            Assert.IsNull(Analytics.PhaseOf("GET", "/game/new/"));
            Assert.IsNull(Analytics.PhaseOf("POST", null));
        }

        [Test]
        public void FeedbackIsAskedAfterAFewGamesAndNotAgainForAWhile()
        {
            long now = 2000000000, day = Feedback.Day;
            // Three games finished and a day with the app.
            Assert.IsTrue(Feedback.Should(now, now - 2 * day, 0, 0, 3, false));
            Assert.IsFalse(Feedback.Should(now, now - 2 * day, 0, 0, 2, false), "too few games");
            Assert.IsFalse(Feedback.Should(now, now - day / 2, 0, 0, 9, false), "installed today");
            Assert.IsFalse(Feedback.Should(now, 0, 0, 0, 9, false), "first launch not known");
            Assert.IsFalse(Feedback.Should(now, now - 2 * day, 0, 0, 9, true), "already asked this run");
            // Not now: 30 days. Answered: 90.
            Assert.IsFalse(Feedback.Should(now, now - 200 * day, now - 29 * day, 0, 9, false));
            Assert.IsTrue(Feedback.Should(now, now - 200 * day, now - 31 * day, 0, 9, false));
            Assert.IsFalse(Feedback.Should(now, now - 200 * day, now - 89 * day, now - 89 * day, 9, false));
            Assert.IsTrue(Feedback.Should(now, now - 200 * day, now - 91 * day, now - 91 * day, 9, false));
        }

        [Test]
        public void TheFollowUpFitsTheRating()
        {
            Assert.AreEqual("What should we fix first?", Feedback.FollowUp(1));
            Assert.AreEqual("What should we fix first?", Feedback.FollowUp(3));
            Assert.AreEqual("What would make it a 5?", Feedback.FollowUp(4));
            Assert.AreEqual("What do you like most?", Feedback.FollowUp(5));
        }

        [Test]
        public void AGameRatingAsksWhyOnlyUnderFiveAndNeverOfATvOrAKid()
        {
            Assert.IsFalse(Feedback.AsksWhy(5, false, false), "five stars sends at once");
            Assert.IsTrue(Feedback.AsksWhy(4, false, false));
            Assert.IsTrue(Feedback.AsksWhy(1, false, false));
            Assert.IsFalse(Feedback.AsksWhy(3, true, false), "nothing to type with a remote");
            Assert.IsFalse(Feedback.AsksWhy(3, false, true), "a Kid profile types no free text");
        }

        [Test]
        public void AGameRatingGoesToTheFeedbackRouteAsAGameOverAnswer()
        {
            var body = Feedback.GameBody("On Fire", 4, "More levels", true);
            Assert.AreEqual(4, body["rating"]);
            Assert.AreEqual("More levels", body["comment"]);
            Assert.AreEqual("game_over", body["trigger"]);
            Assert.AreEqual(Feedback.GamePrompt, body["prompt"]);
            Assert.AreEqual("On Fire", ((System.Collections.Generic.Dictionary<string, object>)body["context"])["game"]);
            Assert.IsFalse(Feedback.GameBody("On Fire", 5, "", false).ContainsKey("comment"), "no words, no comment field");
            Assert.AreEqual(5, Feedback.GameBody("On Fire", 9, null, false)["rating"], "a rating is 1 to 5");
        }

        [Test]
        public void EveryPlayCategoryButTracksHasItsSentence()
        {
            foreach (string tab in new[] { "Local", "Online", "Quests", "Tournaments", "League" })
            {
                Assert.IsTrue(PlayScreen.Intros.ContainsKey(tab), tab);
                Assert.That(PlayScreen.Intros[tab].Length, Is.InRange(40, 140), tab);
            }
            Assert.IsFalse(PlayScreen.Intros.ContainsKey("Tracks"), "Tracks says its own on its page");
        }

        [Test]
        public void NoGameStartsWithoutAPuttrUnlessThereAreTestPutts()
        {
            Assert.IsTrue(ConnectGate.Blocks(true, false), "no Puttr, no test putts: blocked");
            Assert.IsFalse(ConnectGate.Blocks(true, true), "a tester who picked test putts on the card plays on them");
            Assert.IsFalse(ConnectGate.Blocks(false, false), "connected");
        }

        [Test]
        public void TheHoleWarningIsForAConnectedPuttrWhoseOwnReadIsOutOfRange()
        {
            Assert.IsTrue(ConnectGate.BadHole(true, true, false, false));
            Assert.IsFalse(ConnectGate.BadHole(true, true, false, true), "a good read");
            Assert.IsFalse(ConnectGate.BadHole(true, true, true, false), "a hole the app assumed is not the Puttr's read");
            Assert.IsFalse(ConnectGate.BadHole(true, false, false, false), "no hole read yet");
            Assert.IsFalse(ConnectGate.BadHole(false, true, false, false), "not connected");
        }
    }
}
