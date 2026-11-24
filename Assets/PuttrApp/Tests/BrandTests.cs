// The host's brand (Event Mode, Venue Mode): whose logo each board carries, that Puttr always keeps one, how a
// legacy Party kit carries over, which copy of the kit wins between the device and the server, and the staff PIN.
using System.Collections.Generic;
using NUnit.Framework;
using PuttrArcade;
using UnityEngine;

namespace PuttrApp.Tests
{
    public class BrandTests
    {
        Texture2D _logo, _partner;

        [SetUp]
        public void MakeLogos()
        {
            Brand.Clear();
            _logo = new Texture2D(300, 100);
            _partner = new Texture2D(200, 200);
        }

        [TearDown]
        public void Clear()
        {
            Brand.Clear();
            Object.DestroyImmediate(_logo);
            Object.DestroyImmediate(_partner);
        }

        [Test]
        public void WithNoKitEveryBoardIsPuttr()
        {
            Assert.IsFalse(Brand.Active);
            for (int i = 0; i < 7; i++) Assert.AreEqual(Brand.Slot.Puttr, Brand.SlotAt(i, 7));
            Assert.AreSame(Brand.Puttr, Brand.Logo);
            Assert.AreSame(Brand.PuttrMark, Brand.Mark);
            Assert.IsTrue(Brand.MarkIsTinted);
            Assert.AreEqual(Make.Bright, Brand.Primary);
        }

        [Test]
        public void TheLastBoardOfARowIsAlwaysPuttr()
        {
            Brand.Apply(_logo, new[] { _partner }, Color.red, Color.blue, "Open");
            foreach (int count in new[] { 2, 4, 6, 7, 12 })
            {
                Assert.AreEqual(Brand.Slot.Puttr, Brand.SlotAt(count - 1, count), "row of " + count);
                Assert.AreSame(Brand.Puttr, Brand.Panel(count - 1, count));
                int hosts = 0;
                for (int i = 0; i < count - 1; i++)
                {
                    Assert.AreNotEqual(Brand.Slot.Puttr, Brand.SlotAt(i, count));
                    if (Brand.SlotAt(i, count) == Brand.Slot.Host) hosts++;
                }
                Assert.Greater(hosts, 0, "the host is on a row of " + count);
            }
        }

        [Test]
        public void EveryThirdBoardIsAPartnersWhenTheKitHasAny()
        {
            Brand.Apply(_logo, new[] { _partner }, Color.red, Color.blue);
            // brand, brand, partner, brand, brand, Puttr
            var six = new[] { Brand.Slot.Host, Brand.Slot.Host, Brand.Slot.Partner, Brand.Slot.Host, Brand.Slot.Host, Brand.Slot.Puttr };
            for (int i = 0; i < 6; i++) Assert.AreEqual(six[i], Brand.SlotAt(i, 6), "board " + i);
            Assert.AreSame(_partner, Brand.Panel(2, 6));
            Assert.AreSame(_logo, Brand.Panel(0, 6));

            Brand.Apply(_logo, null, Color.red, Color.blue);   // no partners: the host's on those boards
            Assert.AreEqual(Brand.Slot.Host, Brand.SlotAt(2, 6));
            Assert.AreEqual(Brand.Slot.Puttr, Brand.SlotAt(5, 6));
        }

        [Test]
        public void ASingleSignIsTheHosts()
        {
            Brand.Apply(_logo, null, Color.red, Color.blue);
            Assert.AreEqual(Brand.Slot.Host, Brand.SlotAt(0, 1));
            Assert.AreSame(_logo, Brand.Logo);
            Assert.IsFalse(Brand.MarkIsTinted);
        }

        [Test]
        public void ColoursOnlyKitKeepsPuttrLogos()
        {
            Brand.Apply(null, null, Color.red, Color.blue);
            Assert.IsTrue(Brand.Active);
            Assert.AreSame(Brand.Puttr, Brand.Logo);
            Assert.AreEqual(Brand.Slot.Puttr, Brand.SlotAt(0, 6));
            Assert.AreEqual(Color.red, Brand.Primary);
        }

        [Test]
        public void ApplyAndClearMoveTheVersion()
        {
            int before = Brand.Version, heard = 0;
            System.Action hear = () => heard++;
            Brand.Changed += hear;
            Brand.Apply(_logo, null, Color.red, Color.blue);
            Brand.Clear();
            Brand.Clear();   // already Puttr: nothing to say
            Brand.Changed -= hear;
            Assert.AreEqual(before + 2, Brand.Version);
            Assert.AreEqual(2, heard);
        }

        [Test]
        public void AtMostFivePartners()
        {
            var many = new Texture2D[8];
            for (int i = 0; i < many.Length; i++) many[i] = _partner;
            Brand.Apply(_logo, many, Color.red, Color.blue);
            Assert.AreEqual(Brand.MaxPartners, Brand.Partners.Count);
        }

        [Test]
        public void ALogoFitsItsBoard()
        {
            Vector2 wide = Brand.Fit(_logo, 90f, 40f);          // 3:1
            Assert.AreEqual(90f, wide.x, 0.001f);
            Assert.AreEqual(30f, wide.y, 0.001f);
            Vector2 square = Brand.Fit(_partner, 90f, 40f);     // 1:1 is held by the height
            Assert.AreEqual(40f, square.x, 0.001f);
            Assert.AreEqual(40f, square.y, 0.001f);
        }

        [Test]
        public void HexColours()
        {
            Assert.AreEqual("#E2BD6B", Brand.Hex(Brand.Parse("#E2BD6B", Color.black)));
            Assert.AreEqual("#E2BD6B", Brand.Hex(Brand.Parse("e2bd6b", Color.black)));
            Assert.AreEqual("#FFFFFF", Brand.Hex(Brand.Parse("#fff", Color.black)));
            Assert.AreEqual(Color.black, Brand.Parse("not a colour", Color.black));
            Assert.AreEqual(Color.black, Brand.Parse("", Color.black));
        }
    }

    public class BrandKitTests
    {
        // A kit as the legacy app's Party mode saved it (the LAB Golf shape), with the fields this app ignores.
        const string Legacy = "{\"title\":\"Zero Torque Challenge\",\"tagline\":\"Ten putts. One leaderboard.\",\"welcomeMessage\":\"Underrated Golf Tour\"," +
            "\"logoUri\":\"https://images.puttr.co/brandkits/u/logo-1.png\",\"cohostLogoUri\":\"https://images.puttr.co/brandkits/u/cohost-1.png\"," +
            "\"headerColor\":\"#FFFFFF\",\"secondaryColor\":\"#6E6E6E\",\"attractBgUri\":\"https://images.puttr.co/brandkits/u/attractBg-1.jpg\",\"attractScrim\":0.35," +
            "\"qrUrl\":\"https://app.puttr.co/leaderboard/KatieLABGolf\",\"leaderboardTitle\":\"Tour Stop Leaderboard\"," +
            "\"enabledGames\":{\"12\":true,\"40\":false},\"gameOrder\":[40,12],\"includeCustomGames\":true,\"featuredGameId\":1462}";

        static Dictionary<string, object> Kit(string json) => BrandKit.Carry(J.Obj(J.Parse(json)));

        [Test]
        public void ALegacyKitCarriesOverWithNothingLost()
        {
            Dictionary<string, object> kit = Kit(Legacy);
            Assert.AreEqual("Zero Torque Challenge", J.Str(kit, "title"));
            Assert.AreEqual("#FFFFFF", J.Str(kit, "headerColor"));
            Assert.AreEqual("1462", BrandKit.FeaturedGameId(kit));
            Assert.IsTrue(BrandKit.HasContent(kit));
            // What this app does not show is still there for the legacy app after a save from here.
            Dictionary<string, object> again = J.Obj(J.Parse(J.Write(kit)));
            Assert.AreEqual("https://images.puttr.co/brandkits/u/attractBg-1.jpg", J.Str(again, "attractBgUri"));
            Assert.AreEqual(0.35, J.Num(again, "attractScrim"), 0.0001);
            Assert.AreEqual("Underrated Golf Tour", J.Str(again, "welcomeMessage"));
            Assert.AreEqual(J.Obj(J.Parse(Legacy)).Count, again.Count);
        }

        [Test]
        public void KitsFromBeforeTheRenameAreRead()
        {
            Dictionary<string, object> kit = Kit("{\"eventName\":\"Toronto\",\"sponsorName\":\"Four Seasons\"}");
            Assert.AreEqual("Toronto", J.Str(kit, "title"));
            Assert.AreEqual("Four Seasons", J.Str(kit, "brandName"));
            Assert.AreEqual("Toronto", J.Str(kit, "eventName"));   // kept
            // A kit that already has the new names keeps them.
            kit = Kit("{\"eventName\":\"Old\",\"title\":\"New\"}");
            Assert.AreEqual("New", J.Str(kit, "title"));
        }

        [Test]
        public void AnEmptyKitIsNotABrand()
        {
            Assert.IsFalse(BrandKit.HasContent(null));
            Assert.IsFalse(BrandKit.HasContent(Kit("{}")));
            Assert.IsFalse(BrandKit.HasContent(Kit("{\"headerColor\":\"#7ad446\",\"hostMode\":\"venue\"}")));   // Puttr's own green
            Assert.IsTrue(BrandKit.HasContent(Kit("{\"headerColor\":\"#E2BD6B\"}")));
            Assert.IsTrue(BrandKit.HasContent(Kit("{\"logoUri\":\"https://x/logo.png\"}")));
        }

        [Test]
        public void GamesAreOnUnlessSwitchedOff()
        {
            object bowling = J.Parse("{\"id\":12,\"gameType\":\"BOWLING\"}"), lab = J.Parse("{\"id\":40,\"gameType\":\"LAB\"}");
            object flagged = J.Parse("{\"id\":41,\"gameType\":\"LAB\",\"isArcade\":1}"), unflagged = J.Parse("{\"id\":42,\"gameType\":\"BOWLING\",\"isArcade\":0}");
            object custom = J.Parse("{\"id\":1462,\"gameType\":\"STANDARD\",\"custom\":true}");
            // The legacy app's choices (enabledGames) were made from its own games: here arcade games start on, golf games and custom games off.
            Dictionary<string, object> kit = Kit(Legacy);
            Assert.IsTrue(BrandKit.GameOn(kit, bowling));
            Assert.IsFalse(BrandKit.GameOn(kit, lab));
            Assert.IsFalse(BrandKit.GameOn(kit, custom));
            // The games table's isArcade wins over the built-in list when the API sends it.
            Assert.IsTrue(BrandKit.GameOn(kit, flagged));
            Assert.IsFalse(BrandKit.GameOn(kit, unflagged));
            // The host's own choices stand.
            kit = Kit("{\"gameChoices\":{\"12\":false,\"40\":true,\"1462\":true}}");
            Assert.IsFalse(BrandKit.GameOn(kit, bowling));
            Assert.IsTrue(BrandKit.GameOn(kit, lab));
            Assert.IsTrue(BrandKit.GameOn(kit, custom));
            // The older list of games switched off is still read.
            Assert.IsFalse(BrandKit.GameOn(Kit("{\"gamesOff\":[12]}"), bowling));
        }

        [Test]
        public void GamesComeInTheHostsOrder()
        {
            var games = new List<object>();
            foreach (string id in new[] { "12", "7", "40" }) games.Add(new Dictionary<string, object> { { "id", id } });
            List<object> ordered = BrandKit.Ordered(Kit(Legacy), games);   // gameOrder [40, 12]; 7 is new to the kit
            Assert.AreEqual("40", J.Str(ordered[0], "id"));
            Assert.AreEqual("12", J.Str(ordered[1], "id"));
            Assert.AreEqual("7", J.Str(ordered[2], "id"));
            ordered = BrandKit.Ordered(Kit("{}"), games);
            Assert.AreEqual("12", J.Str(ordered[0], "id"));
        }

        [Test]
        public void NewestCopyWins()
        {
            // (has server, server time, has mirror, mirror time, mirror dirty)
            Assert.AreEqual(BrandKit.Winner.Nothing, BrandKit.Decide(false, 0, false, 0, false));
            Assert.AreEqual(BrandKit.Winner.Server, BrandKit.Decide(true, 100, false, 0, false), "a new device takes the server's kit");
            Assert.AreEqual(BrandKit.Winner.MirrorAndPush, BrandKit.Decide(false, 0, true, 100, false), "a kit the server never had is sent");
            Assert.AreEqual(BrandKit.Winner.MirrorAndPush, BrandKit.Decide(true, 100, true, 200, true), "an edit made offline is sent");
            Assert.AreEqual(BrandKit.Winner.Server, BrandKit.Decide(true, 300, true, 200, true), "a newer edit on another device wins over an older offline one");
            Assert.AreEqual(BrandKit.Winner.Server, BrandKit.Decide(true, 300, true, 200, false));
            Assert.AreEqual(BrandKit.Winner.Mirror, BrandKit.Decide(true, 100, true, 200, false), "a stale server row never wipes the device's kit");
        }

        [Test]
        public void ServerStamps()
        {
            Assert.AreEqual(0, BrandKit.ParseStamp(null));
            Assert.AreEqual(1756080000000L, BrandKit.ParseStamp("2025-08-25T00:00:00.000Z"));
            Assert.AreEqual(1756080000000L, BrandKit.ParseStamp(1756080000000d));
        }

        [Test]
        public void TheStaffPinIsKeptAsAHash()
        {
            string hash = BrandKit.PinHash("4821", "host-a");
            Assert.AreEqual(64, hash.Length);
            Assert.IsFalse(hash.Contains("4821"));
            Assert.AreEqual(hash, BrandKit.PinHash("4821", "host-a"));
            Assert.AreNotEqual(hash, BrandKit.PinHash("4822", "host-a"));
            Assert.AreNotEqual(hash, BrandKit.PinHash("4821", "host-b"), "salted with the host's id");
        }

        [Test]
        public void ImageTypesByTheirFirstBytes()
        {
            Assert.AreEqual("image/png", BrandKit.MimeOf(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0, 0, 0 }));
            Assert.AreEqual("image/jpeg", BrandKit.MimeOf(new byte[] { 0xFF, 0xD8, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
            Assert.AreEqual("image/webp", BrandKit.MimeOf(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' }));
            Assert.IsNull(BrandKit.MimeOf(new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c' }));
            Assert.IsNull(BrandKit.MimeOf(null));
        }

        [Test]
        public void KidProfilesNeverHost()
        {
            Assert.IsTrue(Host.Offered(new Dictionary<string, object> { { "userId", "a" } }));
            Assert.IsFalse(Host.Offered(new Dictionary<string, object> { { "userId", "k" }, { "isKid", 1.0 } }));
            Assert.IsFalse(Host.Offered(new Dictionary<string, object> { { "userId", "k" }, { "managedBy", "a" } }));
            Assert.IsFalse(Host.Offered(null));
        }

        [Test]
        public void HostsOfferOnlyGamesThisAppPlays()
        {
            var catalogue = new List<object>
            {
                new Dictionary<string, object> { { "id", "5" }, { "gameType", "BOWLING" }, { "title", "Ten Pin" } },
                new Dictionary<string, object> { { "id", "6" }, { "gameType", "STANDARD" }, { "title", "Retired catalogue Standard" } },
                new Dictionary<string, object> { { "id", "7" }, { "gameType", "NOTAGAME" }, { "title", "Not brought over" } },
            };
            List<object> offered = BrandGamesScreen.Offered(new Dictionary<string, object>(), catalogue);
            List<string> ids = offered.ConvertAll(g => J.Str(g, "id"));
            CollectionAssert.Contains(ids, "5");
            CollectionAssert.DoesNotContain(ids, "6");
            CollectionAssert.DoesNotContain(ids, "7");
            // The arcade games brought over from Puttr Arcade are not in the API's list: the app adds them.
            CollectionAssert.Contains(offered.ConvertAll(g => J.Str(g, "gameType")), "SPOTKICK");
            // Never the sensor games, Free putt or Compare.
            foreach (object sensor in SensorGames.All) Assert.IsFalse(BrandGamesScreen.Hostable(sensor), J.Str(sensor, "gameType"));
            Assert.IsFalse(BrandGamesScreen.Hostable(new Dictionary<string, object> { { "id", "3" }, { "gameType", "FITTING" } }));
            Assert.IsFalse(BrandGamesScreen.Hostable(new Dictionary<string, object> { { "id", "4" }, { "gameType", "OPEN" } }));
        }
    }
}

namespace PuttrApp.Tests
{
    public class HostModeTests
    {
        static Dictionary<string, object> Obj(string json) => J.Obj(J.Parse(json));

        [Test]
        public void FiveTapsInThreeSecondsAskForThePin()
        {
            var taps = new List<float>();
            for (int i = 0; i < 4; i++) Assert.IsFalse(StaffPad.FiveTaps(taps, 10f + i * 0.4f));
            Assert.IsTrue(StaffPad.FiveTaps(taps, 11.8f));
            Assert.IsFalse(StaffPad.FiveTaps(taps, 11.9f), "it starts again after opening");
            // Five taps spread over more than three seconds are just taps.
            taps.Clear();
            bool opened = false;
            for (int i = 0; i < 8; i++) opened |= StaffPad.FiveTaps(taps, 20f + i * 1f);
            Assert.IsFalse(opened);
        }

        [Test]
        public void TheBoardStartsAsTheLegacyPartyBoard()
        {
            var empty = new Dictionary<string, object>();
            // A club admin posts to their club; anyone else to their display name (the legacy app's rule).
            Assert.AreEqual("FOURSEASONS", HostMode.ClubIdFor(Obj("{\"displayName\":\"Matt\",\"isClubAdmin\":\"FOURSEASONS\"}"), empty));
            Assert.AreEqual("KatieLABGolf", HostMode.ClubIdFor(Obj("{\"displayName\":\" KatieLABGolf \",\"isClubAdmin\":0}"), empty));
            Assert.AreEqual("KatieLABGolf", HostMode.ClubIdFor(Obj("{\"displayName\":\"KatieLABGolf\",\"isClubAdmin\":\"0\"}"), empty));
            Assert.AreEqual("KatieLABGolf", HostMode.ClubIdFor(Obj("{\"displayName\":\"KatieLABGolf\"}"), empty));
            Assert.AreEqual("PUTTR", HostMode.ClubIdFor(Obj("{}"), empty));
        }

        [Test]
        public void OnceStartedTheBoardNeverMoves()
        {
            Dictionary<string, object> kit = Obj("{\"hostId\":\"KatieLABGolf\"}");
            Assert.AreEqual("KatieLABGolf", HostMode.ClubIdFor(Obj("{\"displayName\":\"Katie F\"}"), kit), "a rename does not move the board");
            Assert.AreEqual("KatieLABGolf", HostMode.ClubIdFor(Obj("{\"displayName\":\"Katie F\",\"isClubAdmin\":\"LAB\"}"), kit));
        }

        [Test]
        public void TheBestScoreOfTheGameGoesOnTheBoard()
        {
            Assert.AreEqual(1, HostMode.Winner(new List<double> { 4, 9, 7 }, false));
            Assert.AreEqual(0, HostMode.Winner(new List<double> { 4, 9, 7 }, true), "fewest wins");
            Assert.AreEqual(0, HostMode.Winner(new List<double> { 5, 5 }, false), "a tie goes to the earlier seat");
            Assert.AreEqual(0, HostMode.Winner(new List<double> { 3 }, false));
        }

        [Test]
        public void TheBoardCarriesTheTypedName()
        {
            HostMode.Players = new List<string> { "Dana W.", "Jo L." };
            HostMode.Scored(new[] { "Dana W.", "Jo L." }, 1);
            Assert.AreEqual("Jo L.", J.Str(J.Parse(HostMode.BoardName()), "displayName"));
            HostMode.Scored(null, 0);   // a game for one, with no names of its own: the name typed on the game page
            Assert.AreEqual("Dana W.", J.Str(J.Parse(HostMode.BoardName()), "displayName"));
            HostMode.Players = new List<string> { "  " };
            HostMode.Scored(null, 0);
            Assert.AreEqual("Guest", J.Str(J.Parse(HostMode.BoardName()), "displayName"));
            HostMode.Players = new List<string>();
        }

        [Test]
        public void OutsideHostModeEverythingIsPuttrs()
        {
            Assert.IsFalse(HostMode.On);
            Assert.AreEqual("PUTTR", HostMode.ClubId);
            StringAssert.StartsWith("/club/leaderboard/PUTTR/12/", Board.PathFor("12", new Dictionary<string, object>(), false));
            Assert.AreEqual("PUTTR", RoundSaver.BoardBody("12", "99", 5, 0)["clubId"]);
        }

        [Test]
        public void CustomGamesShowThePuttr()
        {
            var practice = GameCards.Picture(new Dictionary<string, object> { { "gameType", "OPEN" } });
            Assert.AreSame(practice, GameCards.Picture(new Dictionary<string, object> { { "gameType", "STANDARD" }, { "custom", true } }));
            Assert.AreSame(practice, GameCards.Picture(new Dictionary<string, object> { { "gameType", "CENTERCUT" }, { "custom", true } }));
            // A recent game carries the Player's userId: it keeps its own picture (Home's Jump back in).
            Assert.AreNotSame(practice, GameCards.Picture(new Dictionary<string, object> { { "gameType", "BOWLING" }, { "userId", "me" } }));
            Assert.AreNotSame(practice, GameCards.Picture(new Dictionary<string, object> { { "gameType", "CENTERCUT" } }), "Puttr's own Center Cut keeps its picture");
        }

        [Test]
        public void HostsOfferTheFeaturedGameFirstAndOnlyGamesThatAreOn()
        {
            var catalogue = new List<object>
            {
                new Dictionary<string, object> { { "id", "5" }, { "gameType", "BOWLING" }, { "title", "Ten Pin" } },
                new Dictionary<string, object> { { "id", "8" }, { "gameType", "CRICKET" }, { "title", "Cricket" } },
                new Dictionary<string, object> { { "id", "9" }, { "gameType", "DERBY" }, { "title", "Derby" } },
            };
            var custom = new List<object> { new Dictionary<string, object> { { "id", "1462" }, { "gameType", "CENTERCUT" }, { "title", "Zero Torque" }, { "userId", "host" } } };
            Dictionary<string, object> kit = Obj("{\"gameChoices\":{\"8\":false,\"1462\":true},\"featuredGameId\":9}");
            List<object> games = HostPickerScreen.Games(kit, catalogue, custom);
            Assert.AreEqual("9", J.Str(games[0], "id"), "featured first");
            CollectionAssert.DoesNotContain(games.ConvertAll(g => J.Str(g, "id")), "8");
            CollectionAssert.Contains(games.ConvertAll(g => J.Str(g, "id")), "1462");
            kit["gameChoices"] = new Dictionary<string, object> { { "8", false }, { "1462", false } };
            CollectionAssert.DoesNotContain(HostPickerScreen.Games(kit, catalogue, custom).ConvertAll(g => J.Str(g, "id")), "1462");
        }
    }
}

namespace PuttrApp.Tests
{
    public class EventBoardTests
    {
        static Dictionary<string, object> Obj(string json) => J.Obj(J.Parse(json));
        static readonly Dictionary<string, object> Katie = J.Obj(J.Parse("{\"displayName\":\"KatieLABGolf\"}"));

        [Test]
        public void TheFirstEventUsesTheHostsOwnBoard()
        {
            // So a board and a web link from the legacy app carry on.
            Assert.AreEqual("KatieLABGolf", HostMode.BoardFor(Katie, Obj("{\"hostMode\":\"event\"}")));
            Assert.AreEqual("KatieLABGolf", HostMode.BoardFor(Katie, Obj("{\"hostMode\":\"event\",\"eventNo\":0}")));
            Assert.AreEqual("KatieLABGolf", HostMode.BoardFor(Katie, Obj("{}")), "a kit with no mode is an event kit");
        }

        [Test]
        public void EveryLaterEventHasItsOwnBoard()
        {
            Assert.AreEqual("KatieLABGolf~1", HostMode.BoardFor(Katie, Obj("{\"hostMode\":\"event\",\"eventNo\":1}")));
            Assert.AreEqual("KatieLABGolf~12", HostMode.BoardFor(Katie, Obj("{\"hostMode\":\"event\",\"eventNo\":12,\"hostId\":\"KatieLABGolf\"}")));
            // The id fixed in the kit, not today's name.
            Assert.AreEqual("KatieLABGolf~2", HostMode.BoardFor(Obj("{\"displayName\":\"Katie F\"}"), Obj("{\"hostMode\":\"event\",\"eventNo\":2,\"hostId\":\"KatieLABGolf\"}")));
        }

        [Test]
        public void AVenueHasOneBoard()
        {
            Assert.AreEqual("Lucky Lie", HostMode.BoardFor(Obj("{\"displayName\":\"Lucky Lie\"}"), Obj("{\"hostMode\":\"venue\",\"eventNo\":4}")));
        }

        [Test]
        public void PuttrsBoardIsNeverAnEvent()
        {
            Assert.AreEqual("PUTTR", HostMode.BoardFor(Obj("{}"), Obj("{\"hostMode\":\"event\",\"eventNo\":3}")));
        }
    }
}

namespace PuttrApp.Tests
{
    public class VenueTests
    {
        const string Host = "host-uid";

        [TearDown]
        public void Clear() { HostSession.Clear(); HostMode.SeatIds = new List<string>(); HostMode.Players = new List<string>(); }

        [Test]
        public void AMembersPuttsAreTheirsAndAGuestsAreNoOnes()
        {
            var ids = new List<string> { "member-a", RoundSaver.Guest, "", null };
            Assert.AreEqual("member-a", HostMode.SaveAs(ids, 0));
            Assert.AreEqual(RoundSaver.Guest, HostMode.SaveAs(ids, 1));
            Assert.AreEqual(RoundSaver.Guest, HostMode.SaveAs(ids, 2), "a seat nobody named");
            Assert.IsNull(HostMode.SaveAs(ids, 3), "Puttr's putts are never saved");
            Assert.AreEqual(RoundSaver.Guest, HostMode.SaveAs(ids, 9));
            // A game for one that carries no seats of its own: the game page's.
            HostMode.SeatIds = new List<string> { "member-b" };
            Assert.AreEqual("member-b", HostMode.SaveAs(null, 0));
        }

        [Test]
        public void NoSeatIsTheHosts()
        {
            // Two members and a guest; the guest had the best score, so it is the one on the host's board.
            Dictionary<string, object> body = RoundSaver.HostedBody("77", new List<double> { 12, 9, 15 }, new List<string> { "member-a", "member-b", RoundSaver.Guest }, 2, Host);
            CollectionAssert.AreEqual(new object[] { "member-a", "member-b", 0, Host }, (List<object>)body["playerArray"]);
            CollectionAssert.AreEqual(new object[] { 12.0, 9.0, 15.0, 15.0 }, (List<object>)body["scoreArray"]);
            Assert.AreEqual("77", body["gameId"]);
        }

        [Test]
        public void AGameForOneMemberStillGivesThemTheirRound()
        {
            Dictionary<string, object> body = RoundSaver.HostedBody("77", new List<double> { 31 }, new List<string> { "member-a" }, 0, Host);
            CollectionAssert.AreEqual(new object[] { "member-a", Host }, (List<object>)body["playerArray"]);
            CollectionAssert.AreEqual(new object[] { 31.0, 31.0 }, (List<object>)body["scoreArray"]);
        }

        [Test]
        public void AllGuestsCloseOnlyTheHostsRound()
        {
            Dictionary<string, object> body = RoundSaver.HostedBody("77", new List<double> { 4, 6 }, new List<string> { RoundSaver.Guest, RoundSaver.Guest }, 1, Host);
            CollectionAssert.AreEqual(new object[] { 0, 0, Host }, (List<object>)body["playerArray"]);
            CollectionAssert.AreEqual(new object[] { 4.0, 6.0, 6.0 }, (List<object>)body["scoreArray"]);
        }

        [Test]
        public void AHostWhoScansInPlaysAsThemselvesOnce()
        {
            Dictionary<string, object> body = RoundSaver.HostedBody("77", new List<double> { 8, 5 }, new List<string> { Host, "member-a" }, 1, Host);
            CollectionAssert.AreEqual(new object[] { Host, "member-a" }, (List<object>)body["playerArray"], "their seat closes the round: no second entry");
            CollectionAssert.AreEqual(new object[] { 8.0, 5.0 }, (List<object>)body["scoreArray"]);
        }

        [Test]
        public void AMemberOnTheBoardCarriesTheirOwnNameAndId()
        {
            HostSession.Join(new Dictionary<string, object> { { "userId", "member-a" }, { "displayName", "Renee K." }, { "photoURL", "https://x/r.jpg" } });
            HostMode.Scored(new[] { "Renee K.", "Big Tom" }, 0, new[] { "member-a", RoundSaver.Guest });
            object row = J.Parse(HostMode.BoardName());
            Assert.AreEqual("Renee K.", J.Str(row, "displayName"));
            Assert.AreEqual("member-a", J.Str(row, "userId"));
            Assert.AreEqual("https://x/r.jpg", J.Str(row, "photoURL"));
            // A guest's row has a name and nothing else.
            HostMode.Scored(new[] { "Renee K.", "Big Tom" }, 1, new[] { "member-a", RoundSaver.Guest });
            row = J.Parse(HostMode.BoardName());
            Assert.AreEqual("Big Tom", J.Str(row, "displayName"));
            Assert.AreEqual("", J.Str(row, "userId"));
        }

        [Test]
        public void TheSessionSeatsAMemberOnce()
        {
            var renee = new Dictionary<string, object> { { "userId", "member-a" }, { "name", "Renee" } };
            Assert.IsTrue(HostSession.Join(renee));
            Assert.IsFalse(HostSession.Join(renee), "scanning twice is one seat");
            Assert.IsFalse(HostSession.Join(new Dictionary<string, object>()), "no user id, no seat");
            HostSession.Guests = new List<string> { "Big Tom", " " };
            Assert.AreEqual("Renee and Big Tom", HostSession.Names());
            HostSession.Leave("member-a");
            Assert.AreEqual("Big Tom", HostSession.Names());
            HostSession.Clear();
            Assert.IsTrue(HostSession.Empty);
        }

        [Test]
        public void TonightStartsAtSixAndTheWeekOnMonday()
        {
            System.DateTime Local(long unix) => System.DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            // Friday 20 Nov 2026, 9 pm: tonight began at 6 that morning; the week on Monday the 16th at 6.
            var friday = new System.DateTime(2026, 11, 20, 21, 0, 0);
            Assert.AreEqual(new System.DateTime(2026, 11, 20, 6, 0, 0), Local(HostMode.Since(friday, false)));
            Assert.AreEqual(new System.DateTime(2026, 11, 16, 6, 0, 0), Local(HostMode.Since(friday, true)));
            // 1 am on Saturday is still Friday night.
            var late = new System.DateTime(2026, 11, 21, 1, 0, 0);
            Assert.AreEqual(new System.DateTime(2026, 11, 20, 6, 0, 0), Local(HostMode.Since(late, false)));
            // 2 am on Monday is still Sunday night, and still last week.
            var monday = new System.DateTime(2026, 11, 23, 2, 0, 0);
            Assert.AreEqual(new System.DateTime(2026, 11, 22, 6, 0, 0), Local(HostMode.Since(monday, false)));
            Assert.AreEqual(new System.DateTime(2026, 11, 16, 6, 0, 0), Local(HostMode.Since(monday, true)));
        }

        [Test]
        public void AVenuesBoardAsksFromAMoment()
        {
            StringAssert.Contains("since=1795000000", Board.PathFor("12", new Dictionary<string, object>(), false, 1795000000));
            StringAssert.DoesNotContain("since", Board.PathFor("12", new Dictionary<string, object>(), true));
        }
    }
}

namespace PuttrApp.Tests
{
    public class HostCatalogueTests
    {
        /// <summary>-hostGames file: the live catalogue saved from GET /games/ALL. Lists what a host is offered from it.</summary>
        [Test]
        public void TheLiveCatalogueOffersEveryPlayableGameButSensorGames()
        {
            string file = System.Environment.GetEnvironmentVariable("PUTTR_GAMES_ALL");
            if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file)) Assert.Ignore("PUTTR_GAMES_ALL not set");
            List<object> catalogue = J.List(J.Parse(System.IO.File.ReadAllText(file)));
            List<object> offered = BrandGamesScreen.Offered(new Dictionary<string, object>(), catalogue);
            var names = offered.ConvertAll(g => J.Str(g, "title") + " (" + J.Str(g, "gameType") + ")");
            var left = new List<string>();
            foreach (object g in catalogue) if (!offered.Contains(g)) left.Add(J.Str(g, "title") + " (" + J.Str(g, "gameType") + ")" + (GameBridge.Retired(g) ? " retired" : GameBridge.Playable(g) ? "" : " not playable"));
            UnityEngine.Debug.Log("HOSTGAMES offered " + offered.Count + ": " + string.Join(", ", names));
            UnityEngine.Debug.Log("HOSTGAMES left out " + left.Count + ": " + string.Join(", ", left));
            Assert.Greater(offered.Count, 20);
        }
    }
}

namespace PuttrApp.Tests
{
    public class PlayListProbe
    {
        [Test]
        public void WhatThePlayTabListsFromTheLiveCatalogue()
        {
            string file = System.Environment.GetEnvironmentVariable("PUTTR_GAMES_ALL");
            if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file)) Assert.Ignore("PUTTR_GAMES_ALL not set");
            var all = new List<object>(J.List(J.Parse(System.IO.File.ReadAllText(file))));
            all.AddRange(SensorGames.All);
            foreach (object extra in ArcadeExtras.All) if (!all.Exists(g => J.Str(g, "gameType") == J.Str(extra, "gameType"))) all.Add(extra);
            var shown = new List<string>(); var hidden = new List<string>();
            foreach (object g in all)
            {
                string name = J.Str(g, "title") + " (" + J.Str(g, "gameType") + ")";
                bool sensor = J.Str(g, "gameCat") == "SENSOR";
                if (!sensor && !GameBridge.Retired(g) && J.Str(g, "gameType") != "FITTING") shown.Add(name + (GameBridge.Playable(g) ? "" : " [not playable]"));
                else hidden.Add(name + (sensor ? " sensor" : GameBridge.Retired(g) ? " retired" : " fitting"));
            }
            UnityEngine.Debug.Log("PLAYLIST shown " + shown.Count + ": " + string.Join(", ", shown));
            UnityEngine.Debug.Log("PLAYLIST hidden " + hidden.Count + ": " + string.Join(", ", hidden));
        }
    }
}
