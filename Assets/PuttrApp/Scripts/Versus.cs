// Versus: a 1v1 played in turns (puttr-app components/versus). The challenger picks a friend and a
// game and plays first; the friend plays the same game's putts to beat the score. Nothing to accept:
// a challenge is taken by playing it. The API scores both turns and says who won.
//
//   POST /versus/create {gameId, challengedUserId, duration 1D|3D|1W, message}   -> the row (id)
//   GET  /versus/inbox                     {sent, received}, each row from the viewer's side
//   GET  /versus/:id                       {versus, challengerPutts, challengedPutts}
//   POST /versus/:id/complete {userGameId} after each turn's game is over (Rounds.cs)
//
// Sending a challenge is for Puttr Club members; taking one is for anyone. Off for Kid profiles.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class VersusMath
    {
        public static bool Done(object row) { string s = J.Str(row, "status"); return s == "COMPLETE" || s == "EXPIRED"; }

        /// <summary>The app's "my turn": the challenger until they have played, then the one challenged until they have.</summary>
        public static bool MyTurn(object row) => MyTurn(row, Auth.Uid);

        public static bool MyTurn(object row, string me)
        {
            if (Done(row)) return false;
            bool challenger = J.Str(row, "challengerId") == me;
            bool first = J.Str(row, "challengerUserGameId").Length > 0 && J.Str(row, "challengerUserGameId") != "0";
            bool second = J.Str(row, "challengedUserGameId").Length > 0 && J.Str(row, "challengedUserGameId") != "0";
            return challenger ? !first : first && !second;
        }

        public static string Outcome(object row, out Color colour)
        {
            colour = Ui.Faint;
            if (J.Str(row, "status") != "COMPLETE") return null;
            bool challenger = J.Str(row, "challengerId") == Auth.Uid;
            double mine = J.Num(row, challenger ? "challengerScore" : "challengedScore"), theirs = J.Num(row, challenger ? "challengedScore" : "challengerScore");
            string winner = J.Str(row, "winnerId");
            string score = Fmt(mine) + "–" + Fmt(theirs);
            if (winner.Length == 0) { colour = Ui.Amber; return "Tied " + score; }
            if (winner == Auth.Uid) { colour = Ui.Bright; return "You won " + score; }
            colour = Ui.Red;
            return "You lost " + score;
        }

        public static string Left(object row)
        {
            DateTime? ends = ProfileMath.Date(row, "expiresAt");
            if (ends == null) return "";
            TimeSpan left = ends.Value - DateTime.UtcNow;
            if (left.TotalSeconds <= 0) return "";
            return (left.TotalDays >= 1 ? (int)left.TotalDays + "d" : left.TotalHours >= 1 ? (int)left.TotalHours + "h" : Math.Max(1, (int)left.TotalMinutes) + "m") + " left";
        }

        public static string Fmt(double v) => v.ToString(v % 1 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);

        /// <summary>canVersus: the game's versusEnabled flag when set, else a scored game that is not a streak.</summary>
        public static bool Eligible(object game)
        {
            if (J.Get(game, "versusEnabled") != null) return J.Int(game, "versusEnabled") == 1;
            string type = J.Str(game, "gameType").ToUpperInvariant();
            return J.Int(game, "hasLeaderboard") == 1 && type != "STREAK" && type != "ARCADESTREAK";
        }

        /// <summary>Opens a row: my turn plays it, anything else shows the result.</summary>
        public static void Open(App app, object row)
        {
            if (MyTurn(row))
            {
                object game = J.Get(row, "game") ?? row;
                if (!GameBridge.Playable(game) || GameBridge.RuleFor(game) == null) { app.Toast(J.Str(row, "gameTitle", "That game") + " is not in this build yet."); return; }
                app.Games.PlayCatalog(game, J.Str(row, "id"));
            }
            else app.Push(new VersusResultsScreen(app, J.Str(row, "id")));
        }
    }

    public sealed class VersusInboxScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;

        public VersusInboxScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Versus", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown()
        {
            if (Kid.Blocked(_app, Kid.Online, _body)) return;
            App.Run(Api.Get("/versus/inbox", data =>
            {
                var rows = new List<object>();
                foreach (object r in J.List(J.Get(data, "sent"))) { if (r is Dictionary<string, object> d) d["sent"] = true; rows.Add(r); }
                rows.AddRange(J.List(J.Get(data, "received")));
                Draw(rows);
            }, _ => { _body.Clear(); _body.Add(Page.Note("Could not load your Versus games.")); }));
        }

        void Draw(List<object> rows)
        {
            _body.Clear();
            _body.Add(Ui.Box("pad", Ui.Button("Challenge a friend", () => Club.Require(_app, "Versus", () => _app.Push(new VersusPickerScreen(_app))))));
            if (rows.Count == 0) { _body.Add(Page.Note("No Versus games yet. Challenge a friend to play the same putts and beat your score.")); return; }
            rows.Sort((a, b) => string.CompareOrdinal(J.Str(b, "createdAt"), J.Str(a, "createdAt")));
            Rivals(rows);
            (string title, Predicate<object> which)[] sections =
            {
                ("Your turn", VersusMath.MyTurn),
                ("Waiting", r => !VersusMath.Done(r) && !VersusMath.MyTurn(r)),
                ("Completed", r => J.Str(r, "status") == "COMPLETE"),
                ("Expired", r => J.Str(r, "status") == "EXPIRED"),
            };
            foreach (var s in sections)
            {
                List<object> mine = rows.FindAll(s.which);
                if (mine.Count == 0) continue;
                _body.Add(Ui.Section(s.title + "   ·   " + mine.Count));
                VisualElement card = Ui.Box("glass set-card");
                foreach (object r in mine) card.Add(Row(r));
                _body.Add(Ui.Box("pad", card));
            }
        }

        // Rivals: the people played most, with the record against each and a rematch.
        void Rivals(List<object> rows)
        {
            var by = new Dictionary<string, List<object>>();
            foreach (object r in rows) { string id = J.Str(r, "opponentId"); if (!by.TryGetValue(id, out List<object> l)) by[id] = l = new List<object>(); l.Add(r); }
            var order = new List<string>(by.Keys);
            order.Sort((a, b) => by[b].Count.CompareTo(by[a].Count));
            if (order.Count == 0) return;
            _body.Add(Ui.Section("Rivals"));
            ScrollView strip = Ui.Scroll(true);
            for (int i = 0; i < Mathf.Min(10, order.Count); i++)
            {
                List<object> games = by[order[i]];
                object latest = games[0];
                int w = 0, l = 0, t = 0;
                foreach (object g in games)
                {
                    if (J.Str(g, "status") != "COMPLETE") continue;
                    string win = J.Str(g, "winnerId");
                    if (win.Length == 0) t++; else if (win == Auth.Uid) w++; else l++;
                }
                string name = J.Str(latest, "opponentName", "A Player");
                Label record = Ui.Text(w + l + t > 0 ? w + "W · " + l + "L · " + t + "T" : "In progress", "muted center", Ui.Medium);
                if (w + l + t > 0) record.style.color = w > l ? Ui.Bright : l > w ? Ui.Red : Ui.Faint;
                string opponent = order[i], gameId = J.Str(latest, "gameId");
                VisualElement card = Ui.Box("glass rival-card", Ui.Avatar(name, J.Str(latest, "opponentPhoto"), true), Ui.Text(name, "feed-name center", Ui.Bold), record,
                    Ui.Box("btn-small", Ui.Text(w + l + t > 0 ? "Rematch" : "Challenge", "btn-text", Ui.Bold)).OnTap(() => Club.Require(_app, "Versus", () => App.Run(VersusSetupScreen.Send(_app, J.Get(latest, "game") ?? latest, opponent, "3D", null)))));
                card.OnTap(() => _app.Push(new VersusResultsScreen(_app, J.Str(latest, "id"))));
                if (i == 0) card.style.marginLeft = 16;
                strip.Add(card);
            }
            _body.Add(strip);
        }

        VisualElement Row(object r)
        {
            string name = J.Str(r, "opponentName", "A Player");
            bool sent = J.Flag(r, "sent") || J.Str(r, "challengerId") == Auth.Uid;
            VisualElement words = Ui.Box("grow set-words", Ui.Box("row", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(sent ? "SENT" : "RECEIVED", "chip", Ui.Bold).Margin(0)), Ui.Text(J.Str(r, "gameTitle"), "sub", Ui.Regular));
            string outcome = VersusMath.Outcome(r, out Color colour);
            if (outcome != null) { Label o = Ui.Text(outcome, "sub", Ui.Bold); o.style.color = colour; words.Add(o); }
            if (J.Str(r, "message").Length > 0) words.Add(Ui.Text("\"" + J.Str(r, "message") + "\"", "muted", Ui.Regular));
            object row = r;
            return Ui.Box("note-row", Ui.Avatar(name, J.Str(r, "opponentPhoto")), words, VersusMath.Done(r) ? null : Ui.Text(VersusMath.Left(r), "muted", Ui.Medium)).OnTap(() => VersusMath.Open(_app, row));
        }
    }

    public sealed class VersusResultsScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        readonly string _id;

        public VersusResultsScreen(App app, string id)
        {
            _app = app;
            _id = id;
            Root = Page.Make(app, "Versus results", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/versus/" + _id, Draw, _ => { _body.Clear(); _body.Add(Page.Note("Could not load this Versus game.")); }));

        void Draw(object data)
        {
            _body.Clear();
            object v = J.Get(data, "versus") ?? data;
            bool challenger = J.Str(v, "challengerId") == Auth.Uid;
            string me = challenger ? "challenger" : "challenged", them = challenger ? "challenged" : "challenger";
            string winner = J.Str(v, "winnerId");
            bool complete = J.Str(v, "status") == "COMPLETE";
            string banner = !complete ? (J.Str(v, "status") == "EXPIRED" ? "Expired" : "In progress") : winner.Length == 0 ? "It's a tie" : winner == Auth.Uid ? "You won!" : "You lost";
            _body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Text(banner, "h1", Ui.Bold), Ui.Text(J.Str(v, "gameTitle"), "sub", Ui.Regular))));
            VisualElement Side(string who, string label)
            {
                string name = label ?? J.Str(v, who + "Name", "Opponent");
                string game = J.Str(v, who + "UserGameId");
                VisualElement card = Ui.Box("glass grow center-box", Ui.Text(label != null ? "YOU" : name.ToUpperInvariant(), "kick", Ui.Bold), Ui.Avatar(name, J.Str(v, who + "Photo"), true).Margin(6),
                    Ui.Text(J.Get(v, who + "Score") != null && game.Length > 0 ? VersusMath.Fmt(J.Num(v, who + "Score")) : "–", "score-mid", Ui.Bold));
                if (game.Length > 0 && game != "0") card.Add(Ui.Box("btn-small", Ui.Text("View game", "btn-text", Ui.Bold)).Margin(8).OnTap(() => _app.Push(new GameViewScreen(_app, game))));
                return card;
            }
            _body.Add(Ui.Box("pad", Ui.Box("row", Side(me, J.Str(_app.Profile, "displayName", "You")), FreePuttScreen.Space(0).Width(10), Side(them, null))).Margin(12));
            if (J.Str(v, "message").Length > 0) _body.Add(Page.Note("\"" + J.Str(v, "message") + "\""));
            if (VersusMath.MyTurn(v)) _body.Add(Ui.Box("pad", Ui.Button("Play your turn", () => VersusMath.Open(_app, v))).Margin(10));
            if (complete)
            {
                string opponent = J.Str(v, them + "Id");
                _body.Add(Ui.Box("pad", Ui.Button("Rematch", () => Club.Require(_app, "Versus", () => App.Run(VersusSetupScreen.Send(_app, J.Get(v, "game") ?? v, opponent, "3D", null))))).Margin(10));
            }
            // Putt by putt: mine against theirs, matched by distance.
            List<object> mine = J.List(J.Get(data, me + "Putts")), theirs = new List<object>(J.List(J.Get(data, them + "Putts")));
            if (mine.Count + theirs.Count == 0) return;
            _body.Add(Ui.Section("Putt by putt"));
            VisualElement list = Ui.Box("glass set-card");
            int n = Mathf.Max(mine.Count, theirs.Count);
            for (int i = 0; i < n; i++)
            {
                object a = i < mine.Count ? mine[i] : null;
                object b = null;
                if (a != null) { b = theirs.Find(p => J.Int(p, "distance") == J.Int(a, "distance")); if (b != null) theirs.Remove(b); }
                else if (theirs.Count > 0) { b = theirs[0]; theirs.RemoveAt(0); }
                string Cell(object p) => p == null ? "–" : J.Int(p, "made") == 1 ? "Made" : "Missed";
                list.Add(Ui.Box("board-row", Ui.Text("PUTT " + (i + 1), "kick", Ui.Bold).Width(64), Ui.Text((a ?? b) != null ? J.Int(a ?? b, "distance") + " ft" : "", "muted", Ui.Regular).Width(48),
                    Ui.Text(Cell(a), "feed-name grow", Ui.Medium), Ui.Text(Cell(b), "feed-name", Ui.Medium)));
            }
            _body.Add(Ui.Box("pad", list));
        }
    }

    /// <summary>Which game to challenge with: the Versus games this build can play, most played first.</summary>
    public sealed class VersusPickerScreen : AppScreen
    {
        public VersusPickerScreen(App app)
        {
            Root = Page.Make(app, "Challenge a friend", out VisualElement body);
            body.Add(Ui.Box("pad", Ui.Text("Pick a game. You play first and set the score to beat.", "sub", Ui.Regular)));
            VisualElement list = Ui.Box("pad");
            body.Add(list);
            list.Add(Spinner.Small());
            App.Run(Api.Get("/games/ALL", data =>
            {
                list.Clear();
                List<object> games = J.List(data).FindAll(g => VersusMath.Eligible(g) && GameBridge.RuleFor(g) != null);
                games.Sort((a, b) => J.Int(b, "last30").CompareTo(J.Int(a, "last30")));
                if (games.Count == 0) { list.Add(Ui.Text("No Versus games are in this build yet.", "sub", Ui.Regular)); return; }
                foreach (object g in games)
                {
                    object game = g;
                    list.Add(Ui.Box("glass list-row", Ui.Box("grow", Ui.Text(J.Str(g, "title"), "feed-name", Ui.Bold), Ui.Text(TournamentMath.Putts(g).Count + " putts", "muted", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                        .OnTap(() => app.Push(new VersusSetupScreen(app, game))));
                }
            }));
        }
    }

    /// <summary>Who to challenge, how long they have, and a message (VersusFriendPicker + VersusOptions).</summary>
    public sealed class VersusSetupScreen : AppScreen
    {
        static readonly string[] Durations = { "1D", "3D", "1W" };
        readonly App _app;
        readonly object _game;
        readonly VisualElement _friends;
        readonly TextField _message;
        string _friend;
        int _duration = 1;

        public VersusSetupScreen(App app, object game)
        {
            _app = app;
            _game = game;
            Root = Page.Make(app, J.Str(game, "title", "Versus"), out VisualElement body);
            _friends = Ui.Box("pad");
            _message = new TextField { maxLength = 100 };
            _message.textEdition.placeholder = "Message (optional)";
            _message.textEdition.hidePlaceholderOnFocus = false;
            _message.AddToClassList("field");
            body.Add(Ui.Section("Who"));
            body.Add(_friends);
            body.Add(Ui.Section("Time to respond"));
            body.Add(Ui.Box("pad", FreePuttScreen.Choice(new[] { "1 day", "3 days", "1 week" }, 1, i => _duration = i)));
            body.Add(Ui.Box("pad", _message.Face(Ui.Medium)).Margin(12));
            body.Add(Ui.Box("pad", Ui.Button("Let's play", Go)));
            _friends.Add(Spinner.Small());
            App.Run(Api.Get("/profile/following/", data => Friends(J.List(data))));
        }

        void Friends(List<object> people)
        {
            _friends.Clear();
            if (people.Count == 0) { _friends.Add(Ui.Text("Follow Players to challenge them.", "sub", Ui.Regular)); _friends.Add(Ui.Button("Find players", () => _app.Push(new FindPlayersScreen(_app)), "btn btn-ghost")); return; }
            ScrollView row = Ui.Scroll(true);
            foreach (object p in people)
            {
                string id = J.Str(p, "userId"), name = J.Str(p, "displayName", "A Player");
                VisualElement pick = Ui.Box(id == _friend ? "friend friend-on" : "friend", Ui.Avatar(name, J.Str(p, "photoURL"), true), Ui.Text(name, "muted center", Ui.Medium));
                pick.OnTap(() => { _friend = id; Friends(people); });
                row.Add(pick);
            }
            _friends.Add(row);
        }

        void Go()
        {
            if (string.IsNullOrEmpty(_friend)) { _app.Toast("Pick who to challenge."); return; }
            App.Run(Send(_app, _game, _friend, Durations[_duration], _message.value.Trim(), true));
        }

        /// <summary>Creates the challenge and starts the challenger's turn.</summary>
        public static System.Collections.IEnumerator Send(App app, object game, string friend, string duration, string message, bool popSetup = false)
        {
            object created = null;
            string failed = null;
            var body = new Dictionary<string, object> { { "gameId", J.Str(game, "gameId", J.Str(game, "id")) }, { "challengedUserId", friend }, { "duration", duration }, { "message", string.IsNullOrEmpty(message) ? null : message } };
            yield return Api.Post("/versus/create", body, data => created = data, why => failed = why);
            if (created == null) { app.Toast(failed == "offline" ? "No connection. Try again." : "Could not send the challenge."); yield break; }
            if (popSetup) app.Pop();
            object playable = J.Get(created, "game") ?? game;
            if (GameBridge.RuleFor(playable) == null) playable = game;
            app.Games.PlayCatalog(playable, J.Str(created, "id"));
        }
    }

    /// <summary>Home's Versus card: games touched in the last week, my turn first.</summary>
    public static class VersusCard
    {
        public static void Fill(App app, VisualElement slot)
        {
            if (J.Flag(app.Profile, "isKid")) return;
            App.Run(Api.Get("/versus/inbox", data =>
            {
                slot.Clear();
                var rows = new List<object>(J.List(J.Get(data, "sent")));
                rows.AddRange(J.List(J.Get(data, "received")));
                DateTime week = DateTime.UtcNow.AddDays(-7);
                rows = rows.FindAll(r => J.Str(r, "status") != "EXPIRED" && (ProfileMath.Date(r, "updatedAt") ?? ProfileMath.Date(r, "createdAt") ?? DateTime.MinValue) > week);
                if (rows.Count == 0) return;
                int Rank(object r) => VersusMath.MyTurn(r) ? 0 : VersusMath.Done(r) ? 2 : 1;
                rows.Sort((a, b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b)) : string.CompareOrdinal(J.Str(b, "createdAt"), J.Str(a, "createdAt")));
                slot.Add(Ui.Section("Versus", "See all", () => app.Push(new VersusInboxScreen(app))));
                ScrollView strip = Ui.Scroll(true);
                for (int i = 0; i < rows.Count; i++)
                {
                    object r = rows[i];
                    string name = J.Str(r, "opponentName", "A Player");
                    string chip = VersusMath.MyTurn(r) ? "Your turn" : VersusMath.Outcome(r, out _) ?? (J.Str(r, "challengerId") == Auth.Uid ? "Waiting" : "Pending");
                    VisualElement card = Ui.Box("glass versus-card", Ui.Box("row", Ui.Avatar(name, J.Str(r, "opponentPhoto")), Ui.Box("grow set-words", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text(J.Str(r, "gameTitle") + (VersusMath.Done(r) ? "" : "   ·   " + VersusMath.Left(r)), "muted", Ui.Regular))),
                        Ui.Text(chip, VersusMath.MyTurn(r) ? "chip chip-green" : "chip", Ui.Bold).Margin(8));
                    card.OnTap(() => VersusMath.Open(app, r));
                    if (i == 0) card.style.marginLeft = 16;
                    strip.Add(card);
                }
                slot.Add(strip);
            }));
        }
    }
}
