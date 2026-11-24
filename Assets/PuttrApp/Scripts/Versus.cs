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

        /// <summary>
        /// A game a Player can challenge another to: any game on the Play tab that ends in a score a Player makes on
        /// their own, where the higher score wins (the API picks the winner that way). So: not the retired Standard
        /// and Streak catalogue or a custom game, not Practice or Compare, not a game for two or more only, not a
        /// sensor game (the other Player may have no sensor), and not one where the lower score wins. (Matt,
        /// 2026-11-24: "all the games in the play tab that have a final score... remove the legacy games".)
        /// </summary>
        public static bool Eligible(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            if (type == "STANDARD" || type == "STREAK") return false;
            if (type == FreePuttScreen.GameType || type == "FITTING" || type == "LAB" || type == "ASSESSMENT" || type == "FULLWORKOUT") return false;
            if (J.Str(game, "gameCat") == "SENSOR") return false;
            if (J.Str(game, "leaderboardOrder") == "asc") return false;
            int who = J.Int(game, "multiPlayer");
            if (who != 0 && who != 3) return false;   // 1 and 2 are for two or more only
            if (GameBridge.Retired(game) || !GameBridge.Playable(game)) return false;
            RuleGame rules = RuleCatalog.Make(game);
            return rules == null || (!rules.LowerIsBetter && rules.MinPlayers <= 1);
        }

        /// <summary>The games to offer, as the Play tab lists them: the catalogue and the arcade's own, most played first.</summary>
        public static List<object> Games(List<object> catalogue)
        {
            var all = new List<object>(catalogue ?? new List<object>());
            foreach (object extra in ArcadeExtras.All)
                if (!all.Exists(g => J.Str(g, "gameType") == J.Str(extra, "gameType"))) all.Add(extra);
            List<object> games = all.FindAll(Eligible);
            games.Sort((x, y) => J.Num(y, "last30").CompareTo(J.Num(x, "last30")));
            return games;
        }

        /// <summary>Plays a Versus turn: the game's own row is found in the catalogue (a Versus row only names it),
        /// then it plays as it does from the Play tab, with the turn's score sent when it is over.</summary>
        public static void Play(App app, string gameId, string versusId, object fallback)
        {
            App.Run(Api.Kept("/games/ALL", data =>
            {
                var all = new List<object>(J.List(data));
                all.AddRange(ArcadeExtras.All);
                object game = all.Find(g => J.Str(g, "id") == gameId) ?? fallback;
                if (game == null || !GameBridge.Playable(game)) { app.Toast("That game is not in this build yet."); return; }
                // Under -app2HoldPlay (Api.Held: /versus/) the turn can be played, but its score will not reach the opponent.
                if (Api.Holds("/versus/")) app.Toast(Api.NotSaved + ": your turn will not be sent.");
                app.Games.PlayVersus(game, versusId);
            }, _ => app.Toast("Could not load the game. Check your connection.")));
        }

        /// <summary>Opens a row: my turn plays it, anything else shows the result.</summary>
        public static void Open(App app, object row)
        {
            if (MyTurn(row)) Play(app, J.Str(row, "gameId"), J.Str(row, "id"), J.Get(row, "game"));
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
            _body.Add(Ui.Box("pad", Ui.Button("Challenge a Player", () => Club.Require(_app, "Versus", () => _app.Push(new VersusPickerScreen(_app))))));
            if (rows.Count == 0) { _body.Add(Page.Note("No Versus games yet. Challenge a Player to play the same putts and beat your score.")); return; }
            rows.Sort((a, b) => string.CompareOrdinal(J.Str(b, "createdAt"), J.Str(a, "createdAt")));
            Rivals(rows);
            // Two blocks of rows. Each row says where it stands (Your turn, Waiting, the result, Expired), so the
            // headers do not say it again: games still in play (my turn first), then the finished ones.
            (string title, Predicate<object>[] which)[] sections =
            {
                ("In play", new Predicate<object>[] { VersusMath.MyTurn, r => !VersusMath.Done(r) && !VersusMath.MyTurn(r) }),
                ("Finished", new Predicate<object>[] { r => J.Str(r, "status") == "COMPLETE", r => J.Str(r, "status") == "EXPIRED" }),
            };
            foreach (var s in sections)
            {
                var mine = new List<object>();
                foreach (Predicate<object> which in s.which) mine.AddRange(rows.FindAll(which));
                if (mine.Count == 0) continue;
                _body.Add(Ui.Section(s.title + "   ·   " + mine.Count));
                VisualElement block = Ui.Box("card vs-list");
                for (int i = 0; i < mine.Count; i++) block.Add(Row(mine[i], i == 0));
                _body.Add(Ui.Box("pad", block));
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
                Label record = Ui.Caps(w + l + t > 0 ? w + "W · " + l + "L · " + t + "T" : "In progress", "caps center vs-record");
                if (w + l + t > 0) record.style.color = w > l ? Ui.Bright : l > w ? Ui.Red : Ui.Faint;
                string opponent = order[i], gameId = J.Str(latest, "gameId");
                VisualElement card = Ui.Box("card rival-card", Ui.Avatar(name, J.Str(latest, "opponentPhoto"), true), Ui.Text(name, "vs-name vs-name-mid", Ui.Bold), record,
                    Ui.Button(w + l + t > 0 ? "Rematch" : "Challenge", () => Club.Require(_app, "Versus", () => App.Run(VersusSetupScreen.Send(_app, J.Get(latest, "game") ?? latest, opponent, "3D", null))), "btn btn-ghost vs-small"));
                card.OnTap(() => _app.Push(new VersusResultsScreen(_app, J.Str(latest, "id"))));
                if (i == 0) card.style.marginLeft = 16;
                strip.Add(card);
            }
            _body.Add(strip);
        }

        // One row of the block: who, the game and which way it went, and on the right where it stands.
        VisualElement Row(object r, bool first)
        {
            string name = J.Str(r, "opponentName", "A Player");
            bool sent = J.Flag(r, "sent") || J.Str(r, "challengerId") == Auth.Uid;
            VisualElement words = Ui.Box("vs-words", Ui.Text(name, "vs-name", Ui.Bold), Ui.Text(J.Str(r, "gameTitle") + "  ·  " + (sent ? "Sent" : "Received"), "body-2", Ui.Regular));
            if (J.Str(r, "message").Length > 0) words.Add(Ui.Text("\"" + J.Str(r, "message") + "\"", "muted", Ui.Regular));
            VisualElement side = Ui.Box("vs-side");
            string outcome = VersusMath.Outcome(r, out Color colour);
            if (outcome != null) { Label o = Ui.Caps(outcome, "caps"); o.style.color = colour; side.Add(o); }
            else if (VersusMath.Done(r)) side.Add(Ui.Caps("Expired", "caps"));
            else
            {
                side.Add(VersusMath.MyTurn(r) ? Ui.Caps("Your turn", "caps caps-green") : Ui.Caps("Waiting", "caps"));
                if (VersusMath.Left(r).Length > 0) side.Add(Ui.Text(VersusMath.Left(r), "muted", Ui.Medium).Margin(4));
            }
            object row = r;
            return Ui.Box(first ? "vs-row vs-row-first" : "vs-row", Ui.Avatar(name, J.Str(r, "opponentPhoto")), words, side).OnTap(() => VersusMath.Open(_app, row));
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
            _body.Add(Ui.Box("pad", Ui.Box("card center-box", Ui.Title(banner, "title center"), Ui.Text(J.Str(v, "gameTitle"), "body-2 center", Ui.Regular).Margin(4))));
            VisualElement Side(string who, string label)
            {
                string name = label ?? J.Str(v, who + "Name", "Opponent");
                string game = J.Str(v, who + "UserGameId");
                VisualElement card = Ui.Box("card vs-half center-box", Ui.Caps(label != null ? "You" : name, "caps vs-half-name"), Ui.Avatar(name, J.Str(v, who + "Photo"), true).Margin(8),
                    Ui.Num(J.Get(v, who + "Score") != null && game.Length > 0 ? VersusMath.Fmt(J.Num(v, who + "Score")) : "–", "num-44 center").Margin(8));
                if (game.Length > 0 && game != "0") card.Add(Ui.Button("View game", () => _app.Push(new GameViewScreen(_app, game)), "btn btn-ghost vs-small"));
                return card;
            }
            _body.Add(Ui.Box("pad", Ui.Box("row", Side(me, J.Str(_app.Profile, "displayName", "You")), FreePuttScreen.Space(0).Width(12), Side(them, null))).Margin(12));
            if (J.Str(v, "message").Length > 0) _body.Add(Page.Note("\"" + J.Str(v, "message") + "\""));
            if (VersusMath.MyTurn(v)) _body.Add(Ui.Box("pad", Ui.Button("Play your turn", () => VersusMath.Open(_app, v))).Margin(12));
            if (complete)
            {
                string opponent = J.Str(v, them + "Id");
                _body.Add(Ui.Box("pad", Ui.Button("Rematch", () => Club.Require(_app, "Versus", () => App.Run(VersusSetupScreen.Send(_app, J.Get(v, "game") ?? v, opponent, "3D", null))))).Margin(12));
            }
            // Putt by putt: mine against theirs, matched by distance.
            List<object> mine = J.List(J.Get(data, me + "Putts")), theirs = new List<object>(J.List(J.Get(data, them + "Putts")));
            if (mine.Count + theirs.Count == 0) return;
            // Putt by putt: each putt's two compact PuttCards side by side, yours then theirs, each opening its detail.
            if (PuttCards.Sheet != null && !Root.styleSheets.Contains(PuttCards.Sheet)) Root.styleSheets.Add(PuttCards.Sheet);
            _body.Add(Ui.Section("Putt by putt"));
            VisualElement list = Ui.Box("pc-list");
            string theirName = J.Str(v, them + "Name", "Opponent").ToUpperInvariant();
            int n = Mathf.Max(mine.Count, theirs.Count);
            for (int i = 0; i < n; i++)
            {
                object a = i < mine.Count ? mine[i] : null;
                object b = null;
                if (a != null) { b = theirs.Find(p => J.Int(p, "distance") == J.Int(a, "distance")); if (b != null) theirs.Remove(b); }
                else if (theirs.Count > 0) { b = theirs[0]; theirs.RemoveAt(0); }
                VisualElement Half(object p, string who, string classes) => Ui.Box(classes, Ui.Text(who, "pc-side-who", Ui.Bold), p != null ? PuttCards.Card(p, i, Root, null, true) : Ui.Box("pc-card pc-compact pc-none", Ui.Text("–", "pc-break", Ui.Regular)));
                list.Add(Ui.Text("PUTT " + (i + 1), "kick pc-pair-head", Ui.Bold));
                list.Add(Ui.Box("pc-pair", Half(a, "YOU", "pc-side pc-side-left"), Half(b, theirName, "pc-side")));
            }
            _body.Add(Ui.Box("pad", list));
        }
    }

    /// <summary>Which game to challenge with: the Versus games this build can play, most played first.</summary>
    public sealed class VersusPickerScreen : AppScreen
    {
        /// <param name="friendId">A Player already chosen (a profile's Challenge to Versus): the setup opens with them picked.</param>
        /// <param name="friend">Their profile row, so they can be shown even when you do not follow them.</param>
        public VersusPickerScreen(App app, string friendId = null, object friend = null)
        {
            string name = J.Str(friend, "displayName");
            Root = Page.Make(app, string.IsNullOrEmpty(friendId) ? "Challenge a Player" : "Challenge " + (name.Length > 0 ? name : "a Player"), out VisualElement body);
            body.Add(Ui.Box("pad", Ui.Text("Pick a game. You play first and set the score to beat.", "sub", Ui.Regular)));
            VisualElement list = Ui.Box("pad");
            body.Add(list);
            list.Add(Spinner.Small());
            App.Run(Api.Get("/games/ALL", data =>
            {
                list.Clear();
                List<object> games = VersusMath.Games(J.List(data));
                if (games.Count == 0) { list.Add(Ui.Text("No Versus games are in this build yet.", "sub", Ui.Regular)); return; }
                // One bordered block of rows, not a card for each game.
                VisualElement block = Ui.Box("card vs-list");
                for (int i = 0; i < games.Count; i++)
                {
                    object game = games[i];
                    block.Add(Ui.Box(i == 0 ? "vs-row vs-row-first" : "vs-row", Ui.Box("vs-words", Ui.Text(J.Str(game, "title"), "vs-name", Ui.Bold), Ui.Text(GameTags.Skill(game) + " · " + GameTags.Length(game), "body-2", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                        .OnTap(() => app.Push(new VersusSetupScreen(app, game, friendId, friend))));
                }
                list.Add(block.Margin(12));
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
        readonly object _seed;
        string _friend;
        int _duration = 1;

        /// <param name="friendId">Picked already (from a profile's Challenge to Versus).</param>
        /// <param name="friend">That Player's row, shown first in the strip even when you do not follow them.</param>
        public VersusSetupScreen(App app, object game, string friendId = null, object friend = null)
        {
            _app = app;
            _game = game;
            _friend = friendId;
            if (!string.IsNullOrEmpty(friendId))
                _seed = new Dictionary<string, object> { { "userId", friendId }, { "displayName", J.Str(friend, "displayName", "A Player") }, { "photoURL", J.Str(friend, "photoURL") } };
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
            App.Run(Api.Get("/profile/following/", data => Friends(J.List(data)), _ => Friends(new List<object>())));
        }

        void Friends(List<object> people)
        {
            _friends.Clear();
            // The Player chosen before this page leads the strip (added when they are not someone you follow).
            if (_seed != null && !people.Exists(p => J.Str(p, "userId") == _friend)) people.Insert(0, _seed);
            if (people.Count == 0) { _friends.Add(Ui.Text("Follow Players to challenge them.", "sub", Ui.Regular)); _friends.Add(Ui.Secondary("Find Players", () => _app.Push(new FindPlayersScreen(_app)))); return; }
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
            // Under -app2HoldPlay /versus/create is answered on the device: no challenge exists, so the friend hears nothing.
            if (Api.IsLocal(J.Str(created, "id"))) app.Toast(Api.NotSaved + ": the challenge was not sent.");
            // The challenger plays first: the game as the Play tab plays it, with the turn's score sent at the end.
            app.Games.PlayVersus(game, J.Str(created, "id"));
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
                    VisualElement card = Ui.Box("card versus-card", Ui.Box("row", Ui.Avatar(name, J.Str(r, "opponentPhoto")), Ui.Box("vs-words", Ui.Text(name, "vs-name", Ui.Bold), Ui.Text(J.Str(r, "gameTitle") + (VersusMath.Done(r) ? "" : "   ·   " + VersusMath.Left(r)), "muted", Ui.Regular))),
                        Ui.Caps(chip, VersusMath.MyTurn(r) ? "caps caps-green" : "caps").Margin(12));
                    card.OnTap(() => VersusMath.Open(app, r));
                    if (i == 0) card.style.marginLeft = 16;
                    strip.Add(card);
                }
                slot.Add(strip);
            }));
        }
    }
}
