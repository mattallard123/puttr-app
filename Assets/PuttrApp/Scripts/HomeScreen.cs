// The Home tab, from the live API, in the order puttr-app's MainScreen.js uses: greeting, today's
// card (streak), Jump back in, You might like, then what the Players you follow have been playing.
// The last of those is the long list with remote pictures that the spike exists to measure.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class HomeScreen : AppScreen
    {
        readonly App _app;
        readonly Label _greeting, _heroKick, _heroTitle, _heroSub, _statusText;
        readonly VisualElement _week, _track, _compete, _challenges, _live, _versus, _greetRow;
        readonly VisualElement _heroBar, _recent, _recentSection, _picks, _picksSection, _feed, _feedSection, _status;
        int _pending;
        bool _marked;

        public HomeScreen(App app)
        {
            _app = app;
            var logo = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoHeader"), scaleMode = ScaleMode.ScaleToFit };
            logo.AddToClassList("logo");
            _status = Ui.Box("dot");
            _statusText = Ui.Text("Looking for your Puttr", "", Ui.Medium);
            // The green's status and the notifications bell, as puttr-app's main header.
            VisualElement pill = Ui.Box("pill", _status, _statusText);
            pill.OnTap(() => ConnectSheet.Open(app, pill));
            VisualElement top = Ui.Box("top", logo, Ui.Box("row", pill, Inbox.Bell(app)));
            top.schedule.Execute(ShowConnection).Every(1000);

            _greeting = Ui.Text(Greeting(DateTime.Now.Hour, null), "h1", Ui.Bold);
            _heroKick = Ui.Text("TODAY", "kick", Ui.Bold);
            _heroTitle = Ui.Text("", "h2", Ui.Bold);
            _heroTitle.style.marginTop = 4;
            _heroSub = Ui.Text("", "sub", Ui.Regular);
            _heroBar = new VisualElement();
            _week = Ui.Box("week");
            VisualElement hero = Ui.Box("glass hero", _heroKick, _heroTitle, _heroSub, _heroBar, _week, Ui.Button("Start putting", () => FreePuttScreen.Open(_app)));
            _track = new VisualElement();
            _compete = new VisualElement();
            _challenges = new VisualElement();
            _live = new VisualElement();
            _versus = new VisualElement();
            _greetRow = Ui.Box("row", Ui.Box("grow", _greeting));

            _recent = new VisualElement();
            _recentSection = Ui.Box("hidden", Ui.Section("Jump back in", "See all", () => _app.ShowTabs("play")), _recent);
            _picks = new VisualElement();
            _picksSection = Ui.Box("hidden", Ui.Section("You might like"), _picks);
            _feed = new VisualElement();
            _feedSection = Ui.Box("hidden", Ui.Section("Following", "See all", () => _app.Push(new FeedScreen(_app, _feedSection.Q<Label>(className: "sec-title").text != "Following"))), _feed);

            ScrollView scroll = Ui.Scroll();
            scroll.Add(top);
            scroll.Add(Ui.Box("pad", _greetRow, hero));
            scroll.Add(_live);
            scroll.Add(_versus);
            // The app's order: today, jump back in, what you compete in, challenges, something new, your people.
            scroll.Add(_recentSection);
            scroll.Add(_track);
            scroll.Add(_compete);
            scroll.Add(_challenges);
            scroll.Add(_picksSection);
            scroll.Add(_feedSection);
            var foot = new VisualElement();
            foot.style.height = 24;
            scroll.Add(foot);
            Root = scroll;
            Load();
        }

        public static string Greeting(int hour, string firstName)
        {
            string part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            return string.IsNullOrWhiteSpace(firstName) ? part : part + ", " + firstName.Trim();
        }

        /// <summary>The name to greet with: firstName, else the first word of displayName.</summary>
        public static string FirstName(object profile)
        {
            string first = J.Str(profile, "firstName").Trim();
            if (first.Length > 0) return first;
            string display = J.Str(profile, "displayName").Trim();
            int space = display.IndexOf(' ');
            return space > 0 ? display.Substring(0, space) : display;
        }

        /// <summary>Set when a game was just played: Home fetches again in place (no reload look), the last data on screen meanwhile.</summary>
        public bool Stale;

        public override void Shown()
        {
            if (!Stale) return;
            Stale = false;
            Data();
        }

        void Load()
        {
            _pending = 5;
            // A first sign-in makes the profile (ProfileSetup); then the onboarding guide while there is no handedness.
            App.Run(ProfileSetup.Ensure(profile =>
            {
                if (profile == null) { Failed(Auth.SignedIn ? "offline" : "signed_out"); return; }
                _app.Profile = profile;
                if (ProfileSetup.NeedsOnboarding(profile) && !(_app.Top is OnboardingScreen)) _app.Push(new OnboardingScreen(_app));
                // Once signed in: push registration, IAPHUB's user, and any link or notification that opened the app.
                _app.ProfileRead();
                Push.Register(_app);
                Iap.Login(profile);
                DeepLinks.Ready();
                Push.Flush(_app);
                _greeting.text = Greeting(DateTime.Now.Hour, FirstName(_app.Profile));
                // Profiles: who is putting, beside the greeting, when there is anyone to switch to.
                while (_greetRow.childCount > 1) _greetRow.RemoveAt(1);
                if (Profiles.HasSwitch(_app.Profile)) _greetRow.Add(Profiles.Switcher(_app));
                LiveCard.Fill(_app, _live);
                VersusCard.Fill(_app, _versus);
                Arrived();
            }));
            Data();
        }

        // Everything Home shows that changes with play. Each part is replaced when its answer arrives.
        void Data()
        {
            App.Run(Api.Get("/streaks/", ShowStreak, Failed));
            // Not waited for by the loader: the rest of Home is usable without them.
            App.Run(Api.Get("/profile/userStats", data => { _calendar = ProfileMath.Calendar(J.First(data)); ShowWeek(); }));
            App.Run(Api.Get("/challenges/userChallenges", data => Cards.Challenges(_app, _challenges, J.List(data), joinedFirst: true)));
            Compete.Fill(_app, _compete);
            App.Run(Api.Get("/tracks/featured", data => TrackOfTheWeek.Fill(_app, _track, data), null));
            App.Run(Api.Get("/profile/recentGames/", data => { ShowRecent(J.List(data)); Arrived(); }, Failed));
            App.Run(Api.Get("/games/recommended/", data => { ShowPicks(J.List(data)); Arrived(); }, Failed));
            App.Run(Api.Get("/profile/memberGames/", data =>
            {
                List<object> rows = J.List(data);
                if (rows.Count > 0) { ShowFeed(rows, "Following"); Arrived(); return; }
                // Nobody followed yet: the community's games instead, as the app does.
                App.Run(Api.Get("/profile/communityGames/", more => { ShowFeed(J.List(more), "Community"); Arrived(); }, Failed));
            }, Failed));
        }

        void Arrived()
        {
            _pending--;
            if (_marked) return;   // a refresh: no loader, the page just updates
            _app.Loading(0.3f + 0.7f * (5 - Mathf.Max(0, _pending)) / 5f);
            if (_pending > 0) return;
            _app.Loaded();
            _marked = true;
            Perf.Mark("home data on screen");
        }

        void Failed(string why)
        {
            if (why == "signed_out") { _app.SignedOut(); return; }
            if (why == "offline") _app.Toast("No connection. Showing what loaded.");
            Arrived();
        }

        void ShowStreak(object streak)
        {
            int current = J.Int(streak, "currentStreak"), today = J.Int(streak, "puttsToday"), goal = Mathf.Max(1, J.Int(streak, "dailyGoal", 25));
            _heroKick.text = current > 0 ? "TODAY · " + current + " DAY STREAK" : "TODAY";
            _heroTitle.text = today >= goal ? "Goal met. Nice work today" : today + " of " + goal + " putts today";
            _heroSub.text = today >= goal ? "Keep going, or come back tomorrow to add a day." : current > 0 ? "Putt today to keep your streak alive." : "Hole a few to start a streak.";
            _heroBar.Clear();
            _heroBar.Add(Ui.Progress((float)today / goal));
            _puttsToday = today;
            ShowWeek();
            Arrived();
        }

        // This week, a dot a day from Monday: filled for a day with putts, ringed for today.
        Dictionary<string, int> _calendar;
        int _puttsToday;

        void ShowWeek()
        {
            _week.Clear();
            DateTime today = DateTime.Now.Date;
            bool[] active = WeekDots(_calendar, today, _puttsToday, out int todayIndex);
            string letters = "MTWTFSS";
            for (int i = 0; i < 7; i++)
            {
                VisualElement dot = Ui.Box(active[i] ? "week-dot week-on" : i == todayIndex ? "week-dot week-today" : "week-dot", Ui.Text(letters[i].ToString(), "week-letter", Ui.Bold));
                _week.Add(Ui.Box("week-day", dot));
            }
        }

        /// <summary>Which days of this week (Monday first) had putts; today counts as soon as there is one.</summary>
        public static bool[] WeekDots(Dictionary<string, int> calendar, DateTime today, int puttsToday, out int todayIndex)
        {
            todayIndex = ((int)today.DayOfWeek + 6) % 7; // Monday 0 ... Sunday 6
            var active = new bool[7];
            for (int i = 0; i <= todayIndex; i++)
            {
                DateTime day = today.AddDays(i - todayIndex);
                active[i] = calendar != null && calendar.TryGetValue(day.ToString("yyyy-MM-dd"), out int putts) && putts > 0;
            }
            if (puttsToday > 0) active[todayIndex] = true;
            return active;
        }

        void ShowConnection()
        {
            // Wi-Fi only is a tester switch: a non-tester on this phone goes back to Bluetooth.
            if (_app.Profile != null && !J.Flag(_app.Profile, "isTester") && Green.WifiOnly) Green.WifiOnly = false;
            string link = Green.Link;
            _status.EnableInClassList("dot-on", link != null);
            _statusText.text = link != null ? "Puttr connected" : "No Puttr yet";
        }

        void ShowRecent(List<object> rows)
        {
            // One card a game: the list is every finished game, newest first.
            var seen = new HashSet<string>();
            var games = new List<object>();
            foreach (object row in rows)
            {
                string key = J.Str(row, "id") + ":" + J.Str(row, "courseId");
                if (seen.Add(key)) games.Add(row);
                if (games.Count == 6) break;
            }
            // The app's row ends with Practice. Free putt keeps no score, so it never says "Last score".
            bool hasFreePutt = false;
            foreach (object game in games) if (J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) hasFreePutt = true;
            if (!hasFreePutt) games.Add(J.Parse("{\"title\":\"Free putt\",\"description\":\"A driving range for putting\",\"gameType\":\"OPEN\",\"putts\":0}"));
            Fill(_recentSection, _recent, games, game =>
            {
                if (J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) return "Free play";
                double score = J.Num(game, "lastScore", double.NaN);
                return double.IsNaN(score) ? null : "Last score " + score.ToString("0.#");
            });
        }

        void ShowPicks(List<object> rows)
        {
            if (rows.Count > 8) rows = rows.GetRange(0, 8);
            Fill(_picksSection, _picks, rows, game => J.Str(game, "recommendReason", null));
        }

        void Fill(VisualElement section, VisualElement into, List<object> games, Func<object, string> note)
        {
            into.Clear();
            section.EnableInClassList("hidden", games.Count == 0);
            if (games.Count == 0) return;
            ScrollView row = Ui.Scroll(true);
            for (int i = 0; i < games.Count; i++)
            {
                VisualElement tile = GameTile(_app, games[i], note(games[i]));
                if (i == 0) tile.AddToClassList("gcard-first");
                row.Add(tile);
            }
            var end = new VisualElement();
            end.style.width = 16;
            row.Add(end);
            into.Add(row);
        }

        void ShowFeed(List<object> rows, string title)
        {
            _feed.Clear();
            _feedSection.EnableInClassList("hidden", rows.Count == 0);
            _feedSection.Q<Label>(className: "sec-title").text = title;
            int shown = Mathf.Min(rows.Count, 60);
            for (int i = 0; i < shown; i++)
            {
                object row = rows[i];
                string name = J.Str(row, "displayName", "A Player");
                string gameId = J.Str(row, "gameId");
                bool scored = J.Int(row, "hasLeaderboard") == 1 && J.Get(row, "score") != null;
                _feed.Add(Ui.Box("feed-row",
                    Ui.Avatar(name, J.Str(row, "photoURL")),
                    Ui.Box("grow", Ui.Text(name, "feed-name", Ui.Bold), Ui.Text((scored ? FeedScreen.Phrase(row) + " on " : "played ") + J.Str(row, "title", "a game"), "muted", Ui.Regular)),
                    scored ? Ui.Text(Page.Score(row), "feed-score", Ui.Bold) : null).OnTap(() => { if (gameId.Length > 0) _app.Push(new GameViewScreen(_app, gameId)); }));
            }
        }

        // ── The game card both Home and Play use (puttr-app's GameTile) ──────

        public static bool Playable(object game) => GameBridge.Playable(game);

        public static VisualElement GameTile(App app, object game, string note)
        {
            int putts = J.Int(game, "putts"), feet = J.Int(game, "distance");
            string line = note ?? (putts > 0 ? putts + " putts" + (feet >= 3 && feet <= 11 ? " · " + feet + " ft" : "") : "");
            return GameCards.Make(game, line, note != null, false).OnTap(() => app.Push(new GameScreen(app, game)));
        }
    }
}
