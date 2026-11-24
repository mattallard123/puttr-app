// League Night's week-to-game rules, which nobody sees go wrong until a score is refused or lands on the wrong
// game: which way a week's game is played, which putts it plays, and what score is entered.
using System.Collections.Generic;
using NUnit.Framework;

namespace PuttrApp.Tests
{
    public class LeagueTests
    {
        static object Game(string type, string putts = null)
        {
            var row = new Dictionary<string, object> { { "id", 472.0 }, { "gameType", type }, { "title", type } };
            if (putts != null) row["puttData"] = putts;
            return row;
        }

        [Test]
        public void AWeekIsPlayedAsItsOwnGameOrNotAtAll()
        {
            // Center Cut is a rule game here: the week plays as the game itself.
            Assert.AreEqual(LeagueRound.Way.Rules, LeagueRound.WayFor(Game("CENTERCUT"), out RuleGame rules));
            Assert.IsInstanceOf<CenterCutGame>(rules);
            // A list of putts with no rule game: its own putts on the ramp. Without putts there is nothing to play.
            Assert.AreEqual(LeagueRound.Way.List, LeagueRound.WayFor(Game("STANDARD", "[{\"id\":1,\"distance\":5,\"tee\":2}]"), out rules));
            Assert.IsNull(rules);
            Assert.AreEqual(LeagueRound.Way.None, LeagueRound.WayFor(Game("STANDARD"), out _));
            // An arcade game (On Fire), a game for two, a game where fewer is better: never scored as Center Cut.
            Assert.AreEqual(LeagueRound.Way.None, LeagueRound.WayFor(Game("ARCADESTREAK"), out _));
            Assert.AreEqual(LeagueRound.Way.None, LeagueRound.WayFor(Game("HORSE"), out _));
            Assert.AreEqual(LeagueRound.Way.None, LeagueRound.WayFor(Game("DEADCENTER"), out _));
        }

        [Test]
        public void TheWeeksGameIsFoundByItsId()
        {
            object week = J.Parse("{\"weekNumber\":2,\"gameId\":472,\"gameType\":\"CENTERCUT\",\"gameTitle\":\"Center Cut\"}");
            List<object> catalog = J.List(J.Parse("[{\"id\":102,\"gameType\":\"CENTERCUT\",\"title\":\"Another\"},{\"id\":472,\"gameType\":\"CENTERCUT\",\"title\":\"Center Cut\",\"puttData\":\"[{\\\"id\\\":1,\\\"distance\\\":7,\\\"tee\\\":3}]\"}]"));
            object row = LeagueRound.Row(catalog, week);
            Assert.AreEqual("Center Cut", J.Str(row, "title"));
            List<TournamentMath.Dealt> putts = LeagueRound.Putts(row, new System.Random(1));
            Assert.AreEqual(1, putts.Count);
            Assert.AreEqual(7, putts[0].Feet);
            // Not in the catalog: a row made from the week, which sets no putts, so nine are dealt.
            object stub = LeagueRound.Row(new List<object>(), week);
            Assert.AreEqual("472", J.Str(stub, "id"));
            Assert.AreEqual("CENTERCUT", J.Str(stub, "gameType"));
            Assert.AreEqual(9, LeagueRound.Putts(stub, new System.Random(1)).Count);
            // A week with no gameType is taken for Center Cut, as the app takes it.
            Assert.AreEqual("CENTERCUT", LeagueRound.TypeOf(J.Parse("{\"weekNumber\":1,\"gameId\":472}")));
            Assert.AreEqual("ARCADESTREAK", LeagueRound.TypeOf(J.Parse("{\"gameType\":\"arcadestreak\"}")));
        }

        [Test]
        public void CenterCutPlaysTheRoundsPutts()
        {
            var putts = new List<TournamentMath.Dealt> { new TournamentMath.Dealt { Feet = 9, Tee = 1 }, new TournamentMath.Dealt { Feet = 4, Tee = 3 } };
            var game = new CenterCutGame { Fixed = putts };
            game.Start();
            Assert.AreEqual(9, game.Feet);
            Assert.AreEqual(1, game.Tee);
            Assert.AreEqual("PUTT 1 OF 2", game.Progress);
            // Left alone it deals its own nine.
            var own = new CenterCutGame();
            own.Start();
            Assert.AreEqual("PUTT 1 OF 9", own.Progress);
        }

        [Test]
        public void TheScoreEnteredIsTheGamesOwn()
        {
            var session = new RoundSession();
            var game = new CenterCutGame();
            game.Start();
            var round = new CompetitionRound(CompetitionRound.Kind.League, session, "472") { Rules = game };
            // The game's putts go on the round's tally as they are called, with the putt added to the list when none was set.
            session.Record(null, 6.5, 7, 2);
            session.Record(null, 0, 4, 1);
            Assert.AreEqual(2, session.Index);
            Assert.AreEqual(2, session.Putts.Count);
            Assert.AreEqual(6.5, session.Total, 1e-9);
            // What is entered is the game's total for the Player, to two places.
            game.Score[0] = 41.2349;
            Assert.AreEqual(41.23, round.Score, 1e-9);
            Assert.AreEqual(41.23, J.Num(J.Parse(round.StateJson()), "score"), 1e-9);
            // A putt-list round enters the putts added up.
            Assert.AreEqual(6.5, new CompetitionRound(CompetitionRound.Kind.League, session, "472").Score, 1e-9);
        }

        [Test]
        public void TeamsComeFromTheApiOverTheBundledSix()
        {
            Assert.IsNull(League.TeamOf(null));
            Assert.AreEqual(6, League.Teams.Count);
            try
            {
                // What the API leaves out of a team is filled from the bundled team of the same id.
                League.Learn(J.Parse("{\"teams\":[{\"id\":\"lions\",\"name\":\"The Pride\",\"color\":\"#112233\"},{\"id\":\"mounties\"}]}"));
                Assert.AreEqual(2, League.Teams.Count);
                Assert.AreEqual("The Pride", League.TeamOf("lions").Name);
                Assert.AreEqual("United Kingdom", League.TeamOf("lions").Region);
                Assert.AreEqual(0x33 / 255f, League.TeamOf("lions").Colour.b, 1e-3);
                Assert.AreEqual("The Mounties", League.TeamOf("mounties").Name);
                // A team the API's list leaves out is still known when a row names it.
                Assert.AreEqual("The Mavericks", League.TeamOf("mavericks").Name);
                // An answer with no teams changes nothing.
                League.Learn(J.Parse("{\"id\":\"s4-2026\"}"));
                Assert.AreEqual(2, League.Teams.Count);
            }
            finally
            {
                League.Learn(J.Parse("{\"teams\":[{\"id\":\"lions\"},{\"id\":\"mounties\"},{\"id\":\"noreasters\"},{\"id\":\"renegades\"},{\"id\":\"blizzards\"},{\"id\":\"mavericks\"}]}"));
            }
            Assert.AreEqual(6, League.Teams.Count);
            Assert.AreEqual("The Lions", League.TeamOf("lions").Name);
        }
    }
}
