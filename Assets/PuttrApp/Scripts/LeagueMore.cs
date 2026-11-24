// League Night's deeper pages (puttr-app components/league/screens): a team's page, a week's recap
// reel, past seasons, and a season's finale.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>
    /// A team's page, as puttr-app's LeagueTeamDetail: the usual header, the crest, name and region, one strip of
    /// Rank, Total and Members, the week picker (Season or a week; not for a past season), then the Players' rows:
    /// a week's individual scores (the top five tagged, they count) or the season's totals. Built once and kept: coming
    /// back shows the last answer at once and the fresh one replaces it in place.
    /// </summary>
    public sealed class LeagueTeamScreen : AppScreen
    {
        readonly App _app;
        readonly League.Team _team;
        readonly string _season;
        readonly object _live;      // the current season (with its weeks), or null for a past season
        readonly Label _badge;
        readonly VisualElement _stats, _pickers, _body;
        int _week; // 0: the season
        int _draws;

        public LeagueTeamScreen(App app, League.Team team, string seasonId, int week)
        {
            _app = app;
            _team = team;
            _season = seasonId;
            _live = League.LastSeason != null && J.Str(League.LastSeason, "id") == seasonId ? League.LastSeason : null;
            _week = _live != null ? week : 0;

            ScrollView scroll = Ui.Scroll();
            Root = scroll;
            if (League.Sheet != null) Root.styleSheets.Add(League.Sheet);

            // The header every pushed page has; then the crest, the name once, the region, the scope; the three numbers.
            scroll.Add(Ui.Header("", app.Pop));
            _badge = Ui.Caps("", "caps lg-scope-badge");
            _stats = Ui.Box("lg-stats");
            scroll.Add(Ui.Box("lg-team-head", League.Crest(team, 96), Ui.Title(team.Name, "title center lg-team-name"), Ui.Caps(team.Region, "caps center lg-team-region"), _badge, _stats));
            _pickers = Ui.Box("lg-pickers");
            scroll.Add(_pickers);
            _body = Ui.Box("lg-team-body");
            scroll.Add(_body);
            scroll.Add(FreePuttScreen.Space(32));
            Draw();
        }

        public override void Shown() { if (_built) Draw(); _built = true; }
        bool _built;

        void Stats(string rank, string total, string members)
        {
            _stats.Clear();
            _stats.Add(Ui.StatStrip((rank, "Rank"), (total, "Total"), (members, _week == 0 ? "Members" : "Scored")));
        }

        void Pickers()
        {
            _pickers.Clear();
            if (_live == null) return;
            ScrollView scopes = Ui.Scroll(true);
            DateTime now = DateTime.Now;
            scopes.Add(League.Pill("Season", _week == 0, false, false, () => { _week = 0; Draw(); }));
            foreach (object w in J.List(J.Get(_live, "weeks")))
            {
                int number = J.Int(w, "weekNumber");
                DateTime? starts = League.Time(w, "startsAt");
                scopes.Add(League.Pill("Week " + number, _week == number, League.Live(w, now), starts != null && starts.Value > now, () => { _week = number; Draw(); }));
            }
            _pickers.Add(scopes);
        }

        object WeekRow(int number)
        {
            foreach (object w in J.List(J.Get(_live, "weeks"))) if (J.Int(w, "weekNumber") == number) return w;
            return null;
        }

        void Draw()
        {
            int draw = ++_draws;
            bool season = _week == 0;
            _badge.text = season ? "SEASON" : "WEEK " + _week;
            Pickers();
            // The week picker already says which week is shown: the badge only when there is no picker (a past season).
            _badge.style.display = _live == null ? DisplayStyle.Flex : DisplayStyle.None;

            object week = season ? null : WeekRow(_week);
            DateTime? starts = week != null ? League.Time(week, "startsAt") : null;
            if (starts != null && starts.Value > DateTime.Now)
            {
                // A week still to come: nothing to read.
                Stats("\u2013", "\u2013", "\u2013");
                _body.Clear();
                _body.Add(Ui.Box("pad", Ui.Box("card lg-future", Ui.Icon("Clock", 24, Ui.Faint),
                    Ui.Text("Week " + _week + " hasn't started", "body lg-future-t", Ui.Bold), Ui.Text("It opens " + starts.Value.ToString("ddd MMM d", CultureInfo.InvariantCulture) + ".", "body-2 center", Ui.Medium))));
                return;
            }

            string rank = "\u2013", total = "\u2013", members = "\u2013";
            void Head() { if (draw == _draws) Stats(rank, total, members); }
            Head();

            // The team's rank and total.
            string standings = "/league/season/" + _season + (season ? "" : "/week/" + _week) + "/standings";
            League.Board(standings, new VisualElement(), rows =>
            {
                object row = rows.Find(r => J.Str(r, "team") == _team.Id);
                object at = row == null ? null : season ? (J.Get(row, "seasonRank") ?? J.Get(row, "weekRank")) : J.Get(row, "weekRank");
                rank = at == null ? "\u2013" : "#" + (int)Puttr.PuttrJson.Number(at, 0);
                double sum = row == null ? 0 : season ? J.Num(row, "seasonTotal", J.Num(row, "total")) : J.Num(row, "total");
                total = row == null ? "\u2013" : League.Points(sum);
                Head();
            }, () => { }, () => draw == _draws);

            // The Players: a week's individual scores, or the whole roster by season total.
            string path = season
                ? "/league/season/" + _season + "/leaderboard?scope=global&limit=100&team=" + _team.Id
                : "/league/season/" + _season + "/week/" + _week + "/team/" + _team.Id + "/scores";
            // The Players are rows in one bordered block.
            VisualElement list = Ui.Box("lg-board");
            _body.Clear();
            _body.Add(Ui.Section(season ? "Season totals" : "Individual scores"));
            _body.Add(Ui.Box("pad", list,
                Ui.Text(season ? "Season total = each week's best score, summed. Top 5 count toward the team each week." : "The top 5 scores count toward the team's total.", "body-2 center lg-caption", Ui.Medium)));
            League.Board(path, list, rows =>
            {
                list.Clear();
                rows = rows.FindAll(r => J.Str(r, "team", _team.Id) == _team.Id);
                members = rows.Count.ToString();
                Head();
                if (rows.Count == 0) { list.Add(League.Empty("No " + _team.Name.Replace("The ", "") + " have played yet " + (season ? "this season." : "this week."))); return; }
                string me = J.Str(_app.Profile, "userId", Auth.Uid);
                for (int i = 0; i < rows.Count; i++)
                {
                    object e = rows[i], player = League.PlayerOf(e);
                    string userId = J.Str(player, "userId", J.Str(e, "userId")), game = J.Str(e, "userGameId");
                    string place = season ? (i + 1).ToString() : J.Get(e, "rank") != null ? J.Int(e, "rank").ToString() : (i + 1).ToString();
                    bool replay = !season && game.Length > 0;
                    VisualElement line = League.PlayerRow(e, _team, place, userId.Length > 0 && userId == me, false, !season && i < 5, null, replay);
                    if (replay) line.OnTap(() => _app.Push(new GameViewScreen(_app, game)));
                    else { object who = player; line.OnTap(() => ProfileScreen.Open(_app, userId, who)); }
                    list.Add(line);
                }
                League.FirstRow(list);
            }, () => { list.Clear(); list.Add(League.Empty("Could not load the team's scores.")); }, () => draw == _draws);
        }
    }

    public sealed class LeagueRecapScreen : AppScreen
    {
        public LeagueRecapScreen(App app, string seasonId, int week)
        {
            Root = Page.Make(app, "Week " + week + " recap", out VisualElement body);
            if (League.Sheet != null) Root.styleSheets.Add(League.Sheet);
            body.Add(Page.Loading());
            App.Run(Api.Get("/league/season/" + seasonId + "/week/" + week + "/recap", data =>
            {
                body.Clear();
                string reel = J.Str(data, "reelUrl");
                if (J.Get(data, "weekNumber") == null) { body.Add(Page.Note("Week " + week + " results drop Monday at 8pm.")); return; }
                if (!reel.StartsWith("http")) { body.Add(Page.Note("The reel is still being produced. Check back soon.")); return; }
                body.Add(Ui.Box("pad", Ui.Box("card", Ui.Caps("The tape", "caps caps-green"), Ui.Text("Week " + week + "'s best moments, on film.", "body", Ui.Medium).Margin(4),
                    Ui.Button("Watch the recap", () => SetupGuide.Play(body, reel)))));
                // LeagueRecap's "DOWNLOAD THE TAPE" (saveReelToCameraRoll): the reel is fetched to the phone, then handed
                // to the share sheet, where Save Video puts it in Photos.
                body.Add(Ui.Box("pad", Tape.Button(app, body, reel, "week-" + J.Int(data, "weekNumber") + "-recap", "league recap")));
            }, _ => { body.Clear(); body.Add(Page.Note("Recap not available yet.")); }));
        }
    }

    /// <summary>
    /// Saving a reel to the phone (puttr-app common/saveReelToCameraRoll): the mp4 is downloaded into the cache, then
    /// opened in the system share sheet (Share.File), whose Save Video (iOS) or Gallery/Files (Android) keeps it.
    /// The button reads DOWNLOADING... while the file comes down and cannot be pressed twice. A secondary button: the
    /// page's main one is Watch.
    /// </summary>
    public static class Tape
    {
        public static VisualElement Button(App app, VisualElement over, string url, string fileName, string label)
        {
            VisualElement button = null;
            Label text = null;
            bool busy = false;
            button = Ui.Button("DOWNLOAD THE TAPE", () =>
            {
                if (busy) return;
                busy = true;
                text.text = "DOWNLOADING\u2026";
                button.AddToClassList("btn-off");
                App.Run(Fetch(url, fileName, path =>
                {
                    busy = false;
                    text.text = "DOWNLOAD THE TAPE";
                    button.RemoveFromClassList("btn-off");
                    if (path == null) { Dialog.Ask(over, "Couldn't download", "The reel failed to save. Please check your connection and try again.", "OK", () => { }); return; }
                    Share.File(path, "video/mp4");
                }));
            }, "btn btn-ghost row lg-tape");
            text = button.Q<Label>(className: "btn-text");
            Image icon = Ui.Icon("Tray", 18, Color.white);
            icon.style.marginRight = 8;
            button.Insert(0, icon);
            return button;
        }

        /// <summary>The reel into temporaryCachePath/league-recap/{fileName}.mp4; done(path), or done(null) when it failed.</summary>
        static System.Collections.IEnumerator Fetch(string url, string fileName, Action<string> done)
        {
            string folder = Path.Combine(Application.temporaryCachePath, "league-recap");
            string path = Path.Combine(folder, fileName + ".mp4");
            try { Directory.CreateDirectory(folder); if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { done(null); yield break; }
            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                req.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };
                req.timeout = 120;
                yield return req.SendWebRequest();
                Perf.Wake(0.5f);
                if (req.result != UnityWebRequest.Result.Success) { Perf.Note("Tape download failed: " + req.error); done(null); yield break; }
            }
            done(File.Exists(path) ? path : null);
        }
    }

    public sealed class LeagueSeasonsScreen : AppScreen
    {
        static VisualElement TileButton(string icon, string label, Action tap) =>
            Ui.Box("lg-tile", Ui.Icon(icon, 24, Color.white), Ui.Caps(label, "caps lg-tile-t")).OnTap(tap);

        public LeagueSeasonsScreen(App app)
        {
            Root = Page.Make(app, "Past seasons", out VisualElement body);
            if (League.Sheet != null) Root.styleSheets.Add(League.Sheet);
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
                    VisualElement card = Ui.Box("card",
                        Ui.Box("row", League.Crest(champ, 52), Ui.Box("grow crest-words", Ui.Title("Season " + J.Int(s, "number"), "title-20"), Ui.Caps(dates, "caps lg-season-dates"),
                            Ui.Text(champ != null ? champ.Name + " Champions" : "", "body-2", Ui.Medium).Margin(4))).OnTap(() => app.Push(new LeagueFinaleScreen(app, sid))));
                    // LeagueSeasons' three tiles: Season film, Final results, and the season's Leaderboard.
                    string film = J.Str(s, "seasonReelUrl");
                    object season = s;
                    VisualElement buttons = Ui.Box("row lg-tiles");
                    if (film.StartsWith("http")) buttons.Add(TileButton("Play", "Season film", () => SetupGuide.Play(body, film)));
                    buttons.Add(TileButton("Trophy", "Final results", () => app.Push(new LeagueFinaleScreen(app, sid))));
                    buttons.Add(TileButton("ListNumbers", "Leaderboard", () => app.Push(new LeagueScreen(app, season))));
                    buttons[buttons.childCount - 1].style.marginRight = 0;
                    card.Add(buttons.Margin(12));
                    ScrollView weeks = Ui.Scroll(true);
                    foreach (object w in J.List(J.Get(s, "weeks")))
                    {
                        if (!J.Flag(w, "hasRecap")) continue;
                        int number = J.Int(w, "weekNumber");
                        weeks.Add(League.Pill("Week " + number + "   ·   " + J.Str(w, "gameTitle"), false, false, false, () => app.Push(new LeagueRecapScreen(app, sid, number))));
                    }
                    if (weeks.contentContainer.childCount > 0) { card.Add(Ui.Caps("Weekly recaps", "caps").Margin(16)); card.Add(weeks.Margin(8)); }
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
            if (League.Sheet != null) Root.styleSheets.Add(League.Sheet);
            body.Add(Page.Loading());
            App.Run(Api.Get("/league/season/" + seasonId + "/finale", data =>
            {
                body.Clear();
                League.Learn(data);   // a past season's own teams, if the API sends them with it
                League.Team champ = League.TeamOf(J.Str(data, "champion"));
                if (champ == null) { body.Add(Page.Note("Results aren't ready yet.")); return; }
                Label crown = Ui.Caps("Season champions", "caps");
                crown.style.color = Ui.Gold;   // gold: a trophy
                body.Add(Ui.Box("pad", Ui.Box("card center-box", crown, League.Crest(champ, 96).Margin(12), Ui.Title(champ.Name, "title center").Margin(12),
                    Ui.Caps(champ.Region, "caps center").Margin(4), Ui.Text("\"" + champ.Motto + "\"", "body-2 center", Ui.Medium).Margin(8))));
                string film = J.Str(data, "seasonReelUrl");
                if (film.StartsWith("http")) body.Add(Ui.Box("pad", Ui.Button("Watch the season film", () => SetupGuide.Play(body, film))));
                object mvp = J.Get(data, "mvp");
                if (mvp != null)
                {
                    League.Team mvpTeam = League.TeamOf(J.Str(mvp, "team"));
                    body.Add(Ui.Section("Commissioner's Cup"));
                    body.Add(Ui.Box("pad", Ui.Box("card", Ui.Caps("Season MVP", "caps caps-green"), Ui.Title(J.Str(J.Get(mvp, "player"), "username", "MVP"), "title-20").Margin(4),
                        Ui.Text((mvpTeam != null ? mvpTeam.Name + "   ·   " : "") + J.Num(mvp, "totalScore").ToString("0.#", CultureInfo.InvariantCulture) + " season pts", "body-2", Ui.Medium).Margin(4))));
                }
                body.Add(Ui.Section("Final standings"));
                VisualElement card = Ui.Box("lg-board");
                foreach (object r in J.List(J.Get(data, "finalStandings")))
                {
                    League.Team t = League.TeamOf(J.Str(r, "team"));
                    if (t == null) continue;
                    int won = J.Int(r, "weeksWon");
                    card.Add(Ui.Box("lg-row lg-team-row", Ui.Num(J.Int(r, "seasonRank").ToString(), "lg-team-rank"), League.Crest(t, 42),
                        Ui.Box("grow lg-words", Ui.Text(t.Name, "lg-name", Ui.Bold), Ui.Text(won + (won == 1 ? " week won" : " weeks won"), "lg-sub", Ui.Medium)),
                        Ui.Box("lg-right", Ui.Num(J.Num(r, "seasonTotal").ToString("0.#", CultureInfo.InvariantCulture), "lg-team-total"), Ui.Caps("pts", "caps lg-delta")),
                        Ui.Icon("CaretRight", 14, Ui.Faint)).OnTap(() => app.Push(new LeagueTeamScreen(app, t, seasonId, 0))));
                }
                League.FirstRow(card);
                body.Add(Ui.Box("pad", card));
            }, _ => { body.Clear(); body.Add(Page.Note("Results aren't ready yet.")); }));
        }
    }
}
