// League Night's deeper pages (puttr-app components/league/screens): a team's page, a week's recap
// reel, past seasons, and a season's finale.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class LeagueTeamScreen : AppScreen
    {
        readonly App _app;
        readonly League.Team _team;
        readonly string _season;
        readonly VisualElement _head, _body;
        int _week; // 0: the season

        public LeagueTeamScreen(App app, League.Team team, string seasonId, int week)
        {
            _app = app;
            _team = team;
            _season = seasonId;
            _week = week;
            Root = Page.Make(app, team.Name, out VisualElement body);
            _head = new VisualElement(); _body = new VisualElement();
            body.Add(_head);
            body.Add(_body);
            Draw();
        }

        static string Fmt(double v) => v % 1 == 0 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.00", CultureInfo.InvariantCulture);

        void Draw()
        {
            _head.Clear();
            _body.Clear();
            _body.Add(Page.Loading());
            string sid = _season;
            int asked = _week;
            App.Run(Api.Get("/league/season/" + sid + (_week > 0 ? "/week/" + _week : "") + "/standings", data =>
            {
                if (asked != _week) return;
                object row = J.List(data).Find(r => J.Str(r, "team") == _team.Id);
                string rank = row == null ? "–" : "#" + (_week > 0 ? J.Int(row, "weekRank") : J.Int(row, "seasonRank"));
                double total = row == null ? 0 : _week > 0 ? J.Num(row, "total") : J.Num(row, "seasonTotal", J.Num(row, "total"));
                VisualElement card = Ui.Box("glass team-head", Ui.Text(_week > 0 ? "WEEK " + _week : "SEASON", "kick", Ui.Bold),
                    Ui.Box("row", League.Crest(_team, 64), Ui.Box("grow crest-words", Ui.Text(_team.Name, "h2", Ui.Bold), Ui.Text(_team.Region, "sub", Ui.Regular))).Margin(8),
                    Ui.Box("row", FreePuttScreen.Stat(rank, "Rank"), FreePuttScreen.Stat(Fmt(total), "Points")).Margin(12));
                card.style.borderLeftColor = _team.Colour;
                card.style.borderLeftWidth = 4;
                _head.Clear();
                _head.Add(Ui.Box("pad", card));
            }));
            string path = _week > 0
                ? "/league/season/" + sid + "/week/" + _week + "/team/" + _team.Id + "/scores"
                : "/league/season/" + sid + "/leaderboard?scope=global&limit=100&team=" + _team.Id;
            App.Run(Api.Get(path, data =>
            {
                if (asked != _week) return;
                _body.Clear();
                List<object> rows = J.List(data).FindAll(r => J.Str(r, "team", _team.Id) == _team.Id);
                _body.Add(Ui.Section(_week > 0 ? "Individual scores" : "Season totals"));
                if (rows.Count == 0) { _body.Add(Page.Note(_week > 0 ? "No scores this week yet." : "No scores yet this season.")); return; }
                VisualElement card = Ui.Box("glass set-card");
                string me = J.Str(_app.Profile, "userId");
                for (int i = 0; i < rows.Count; i++)
                {
                    object e = rows[i], player = J.Get(e, "player") ?? J.Parse(J.Str(e, "userName", "{}"));
                    string name = J.Str(player, "displayName", J.Str(player, "username", "A Player")), userId = J.Str(player, "userId"), game = J.Str(e, "userGameId");
                    VisualElement words = Ui.Box("grow", Ui.Text(name + (userId == me ? " · you" : ""), "feed-name", Ui.Bold), Ui.Text(J.Str(player, "homeTown"), "muted", Ui.Regular));
                    if (_week > 0 && i < 5) words.Add(Ui.Text("TOP 5", "chip chip-green game-tag", Ui.Bold));
                    VisualElement line = Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Ui.Avatar(name, J.Str(player, "photoURL")), words,
                        PutterBrands.RowBadge(e), // their putter's brand: a tap opens the putter sheet
                        Ui.Text(Fmt(J.Num(e, "score", J.Num(e, "total"))), "feed-score", Ui.Bold));
                    if (_week > 0 && game.Length > 0) line.OnTap(() => _app.Push(new GameViewScreen(_app, game)));
                    else line.OnTap(() => ProfileScreen.Open(_app, userId, player));
                    card.Add(line);
                }
                _body.Add(Ui.Box("pad", card));
            }, _ => { _body.Clear(); _body.Add(Page.Note("Could not load the team's scores.")); }));
        }
    }

    public sealed class LeagueRecapScreen : AppScreen
    {
        public LeagueRecapScreen(App app, string seasonId, int week)
        {
            Root = Page.Make(app, "Week " + week + " recap", out VisualElement body);
            body.Add(Page.Loading());
            App.Run(Api.Get("/league/season/" + seasonId + "/week/" + week + "/recap", data =>
            {
                body.Clear();
                string reel = J.Str(data, "reelUrl");
                if (J.Get(data, "weekNumber") == null) { body.Add(Page.Note("Week " + week + " results drop Monday at 8pm.")); return; }
                if (!reel.StartsWith("http")) { body.Add(Page.Note("The reel is still being produced. Check back soon.")); return; }
                body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("THE TAPE", "kick", Ui.Bold), Ui.Text("Week " + week + "'s best moments, on film.", "sub", Ui.Regular).Margin(4),
                    Ui.Button("Watch the recap", () => SetupGuide.Play(body, reel)))));
            }, _ => { body.Clear(); body.Add(Page.Note("Recap not available yet.")); }));
        }
    }

    public sealed class LeagueSeasonsScreen : AppScreen
    {
        public LeagueSeasonsScreen(App app)
        {
            Root = Page.Make(app, "Past seasons", out VisualElement body);
            body.Add(Page.Loading());
            App.Run(Api.Get("/league/seasons/past", data =>
            {
                body.Clear();
                List<object> seasons = J.List(data);
                if (seasons.Count == 0) { body.Add(Page.Note("No past seasons yet.")); return; }
                foreach (object s in seasons)
                {
                    string sid = J.Str(s, "id");
                    League.Team champ = League.TeamOf(J.Str(s, "champion"));
                    DateTime? start = ProfileMath.Date(s, "startDate"), end = ProfileMath.Date(s, "endDate");
                    string dates = start != null && end != null ? start.Value.ToString("MMM d", CultureInfo.InvariantCulture) + " – " + end.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : "";
                    VisualElement card = Ui.Box("glass",
                        Ui.Box("row", League.Crest(champ, 52), Ui.Box("grow crest-words", Ui.Text("Season " + J.Int(s, "number"), "h2", Ui.Bold), Ui.Text(dates, "muted", Ui.Regular),
                            Ui.Text(champ != null ? champ.Name + " Champions" : "", "sub", Ui.Medium))).OnTap(() => app.Push(new LeagueFinaleScreen(app, sid))));
                    string film = J.Str(s, "seasonReelUrl");
                    VisualElement buttons = Ui.Box("row");
                    if (film.StartsWith("http")) { buttons.Add(Ui.Button("Season film", () => SetupGuide.Play(body, film), "btn btn-ghost grow")); buttons.Add(FreePuttScreen.Space(0).Width(8)); }
                    buttons.Add(Ui.Button("Final results", () => app.Push(new LeagueFinaleScreen(app, sid)), "btn btn-ghost grow"));
                    card.Add(buttons.Margin(10));
                    ScrollView weeks = Ui.Scroll(true);
                    foreach (object w in J.List(J.Get(s, "weeks")))
                    {
                        if (!J.Flag(w, "hasRecap")) continue;
                        int number = J.Int(w, "weekNumber");
                        weeks.Add(Ui.Box("scope", Ui.Text("Week " + number + "   ·   " + J.Str(w, "gameTitle"), "scope-text", Ui.Bold)).OnTap(() => app.Push(new LeagueRecapScreen(app, sid, number))));
                    }
                    if (weeks.contentContainer.childCount > 0) { card.Add(Ui.Text("WEEKLY RECAPS", "kick", Ui.Bold).Margin(12)); card.Add(weeks.Margin(6)); }
                    card.style.marginBottom = 12;
                    body.Add(Ui.Box("pad", card));
                }
            }, _ => { body.Clear(); body.Add(Page.Note("Could not load past seasons.")); }));
        }
    }

    public sealed class LeagueFinaleScreen : AppScreen
    {
        public LeagueFinaleScreen(App app, string seasonId)
        {
            Root = Page.Make(app, "Season finale", out VisualElement body);
            body.Add(Page.Loading());
            App.Run(Api.Get("/league/season/" + seasonId + "/finale", data =>
            {
                body.Clear();
                League.Team champ = League.TeamOf(J.Str(data, "champion"));
                if (champ == null) { body.Add(Page.Note("Results aren't ready yet.")); return; }
                body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Text("SEASON CHAMPIONS", "kick", Ui.Bold), League.Crest(champ, 96).Margin(10), Ui.Text(champ.Name, "h1", Ui.Bold),
                    Ui.Text(champ.Region, "sub", Ui.Regular), Ui.Text("\"" + champ.Motto + "\"", "muted", Ui.Regular).Margin(4))));
                string film = J.Str(data, "seasonReelUrl");
                if (film.StartsWith("http")) body.Add(Ui.Box("pad", Ui.Button("Watch the season film", () => SetupGuide.Play(body, film))).Margin(10));
                object mvp = J.Get(data, "mvp");
                if (mvp != null)
                {
                    League.Team mvpTeam = League.TeamOf(J.Str(mvp, "team"));
                    body.Add(Ui.Section("Commissioner's Cup"));
                    body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("SEASON MVP", "kick", Ui.Bold), Ui.Text(J.Str(J.Get(mvp, "player"), "username", "MVP"), "h2", Ui.Bold),
                        Ui.Text((mvpTeam != null ? mvpTeam.Name + "   ·   " : "") + J.Num(mvp, "totalScore").ToString("0.#", CultureInfo.InvariantCulture) + " season pts", "sub", Ui.Regular))));
                }
                body.Add(Ui.Section("Final standings"));
                VisualElement card = Ui.Box("glass set-card");
                foreach (object r in J.List(J.Get(data, "finalStandings")))
                {
                    League.Team t = League.TeamOf(J.Str(r, "team"));
                    if (t == null) continue;
                    int won = J.Int(r, "weeksWon");
                    card.Add(Ui.Box("board-row", Ui.Text(J.Int(r, "seasonRank").ToString(), "board-place", Ui.Bold), League.Crest(t, 40), FreePuttScreen.Space(0),
                        Ui.Box("grow crest-words", Ui.Text(t.Name, "feed-name", Ui.Bold), Ui.Text(won + (won == 1 ? " week won" : " weeks won"), "muted", Ui.Regular)),
                        Ui.Text(J.Num(r, "seasonTotal").ToString("0.#", CultureInfo.InvariantCulture) + " pts", "feed-score", Ui.Bold)).OnTap(() => app.Push(new LeagueTeamScreen(app, t, seasonId, 0))));
                }
                body.Add(Ui.Box("pad", card));
            }, _ => { body.Clear(); body.Add(Page.Note("Results aren't ready yet.")); }));
        }
    }
}
