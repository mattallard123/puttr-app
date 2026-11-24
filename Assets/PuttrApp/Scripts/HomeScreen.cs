// The Home tab, from the live API, in the order puttr-app's MainScreen.js uses: greeting, the coach's
// session (CoachCallout), Jump back in, what you compete in, challenges, You might like, then what the
// Players you follow (or the community) have been playing (FriendActivity).
//
// The lead card is the coach's TODAY'S SESSION, drawn as the guidelines' session card: the coach, today's focus,
// today's putts and the streak on one row, this week as seven bars, and Start session (Home's one main button), which opens the session brief (CoachSession's brief stage) and then plays the same
// session the Improve tab starts. Today's putts against the daily goal and the streak live inside that card, once.
// A Kid profile has no AI coach (legacy hides CoachCallout), so a Kid's card is the plain daily goal with Free putt.
// Styles in Resources/PuttrApp/Home.uss.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class HomeScreen : AppScreen
    {
        readonly App _app;
        readonly Label _greeting, _statusText;
        readonly VisualElement _lead, _track, _compete, _challenges, _live, _versus, _greetRow;
        readonly VisualElement _recent, _recentSection, _picks, _picksSection, _feed, _feedHead, _feedSection, _status;
        /// <summary>Home shows this many of the feed; See all opens the rest (FriendActivity MAX_ITEMS).</summary>
        public const int FeedShown = 5;
        int _pending;
        bool _marked, _profileCounted, _profileSetUp;
        readonly Action _forget;

        /// <summary>Home is being thrown away (sign-out, another profile): let go of what it listens to.</summary>
        public void Forget() => _forget?.Invoke();

        public HomeScreen(App app)
        {
            _app = app;
            // New coach settings (goal, coach) are read again the next time Home shows.
            // (Taken off again when Home leaves for good: a sign-out or another profile makes a new Home.)
            Action stale = () => Stale = true;
            CoachVoice.SettingsChanged += stale;
            _forget = () => CoachVoice.SettingsChanged -= stale;
            var logo = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoHeader"), scaleMode = ScaleMode.ScaleToFit };
            logo.AddToClassList("logo");
            _status = Ui.Box("dot");
            _statusText = Ui.Caps("Looking for your Puttr", "caps home-pill-t");
            // The one header every tab has: the logo, then the green's status and the notifications bell.
            VisualElement pill = Ui.Box("pill home-pill", _status, _statusText);
            pill.OnTap(() => ConnectSheet.Open(app, pill));
            VisualElement top = Ui.TabHeader(logo, pill, Inbox.Bell(app));
            top.schedule.Execute(ShowConnection).Every(1000);
            // The console is made the first time it is asked for: ask soon after launch, so the app looks for the
            // Puttr from the start (as the legacy app) and not only once the connect card is opened. Not in this
            // frame: starting the radio is kept out of the app's first frames.
            if (Application.isPlaying && !Fixtures.On) top.schedule.Execute(() => { _ = Green.Console; }).StartingIn(700);

            _greeting = Ui.Title(Greeting(DateTime.Now.Hour, null), "title-20 home-greet");
            // The lead card: drawn by Lead() whenever one of its answers arrives (streak, week, coach, analysis).
            _lead = Ui.Box("card home-lead");
            _track = new VisualElement();
            _compete = new VisualElement();
            _challenges = new VisualElement();
            _live = new VisualElement();
            _versus = new VisualElement();
            // The greeting gives way (it wraps); the profile switch beside it keeps its size, so the two never overlap.
            _greetRow = Ui.Box("row home-greet-row", Ui.Box("home-greet-words", _greeting));

            _recent = new VisualElement();
            _recentSection = Ui.Box("hidden", Ui.Section("Jump back in", "See all", () => _app.ShowTabs("play")), _recent);
            _picks = new VisualElement();
            _picksSection = Ui.Box("hidden", Ui.Section("You might like"), _picks);
            _feed = Ui.Box("card home-feed");
            _feedHead = new VisualElement();
            _feedSection = Ui.Box("hidden", _feedHead, Ui.Box("pad", _feed));

            ScrollView scroll = Ui.Scroll();
            scroll.AddToClassList("home");   // Home.uss restyles the cards other files build (.home ...) on this tab only
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Home");
            if (sheet != null) scroll.styleSheets.Add(sheet);
            // Chips are caps (docs/design-guidelines.md). The cards from other files (Live, Versus, League Night,
            // Tracks, tournaments, challenges) still word theirs in sentence case, so Home puts them in caps here:
            // as they change (the League clock ticks) and a few times a second for a card that was just drawn.
            // To go once those files write their chips with ToUpperInvariant().
            scroll.RegisterCallback<ChangeEvent<string>>(e => { if (e.target is Label changed) CapsChip(changed); });
            scroll.Add(top);
            scroll.Add(Ui.Box("pad", _greetRow, _lead));
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
            Lead();
            Load();
        }

        static void CapsChip(Label label)
        {
            if (!label.ClassListContains("chip") && !label.ClassListContains("chip-join")) return;
            string caps = label.text.ToUpperInvariant();
            if (caps != label.text) label.text = caps;
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
            // A new Player sets up their profile first, whenever Home comes to the front.
            OnboardingScreen.Lead(_app);
            if (!Stale) return;
            Stale = false;
            Data();
        }

        void Load()
        {
            _pending = 5;
            ReadProfile(0);
            Data();
        }

        // A first sign-in makes the profile (ProfileSetup); then the onboarding guide, straight away, while there is
        // no handedness. A brand new account's first reads can fail while it settles, so they are tried again.
        void ReadProfile(int tries)
        {
            if (Fixtures.On) { App.Run(ProfileSetup.Ensure(profile => GotProfile(profile, tries))); return; }
            // The profile as it was last time, at once, then as it is now. An account with no row yet (a first
            // sign-in) is made by ProfileSetup.
            bool making = false;
            App.Run(Api.Kept("/profile/", data =>
            {
                object profile = J.First(data);
                if (profile != null) { GotProfile(profile, tries); return; }
                if (making) return;
                making = true;
                App.Run(ProfileSetup.Ensure(made => GotProfile(made, tries)));
            }, _ => GotProfile(null, tries)));
        }

        void GotProfile(object profile, int tries)
        {
            if (profile == null && Auth.SignedIn && tries < 4) { App.Run(Later(2.5f, () => ReadProfile(tries + 1))); return; }
            if (profile == null)
            {
                if (_profileCounted) { if (!Auth.SignedIn) _app.SignedOut(); return; }
                _profileCounted = true;
                Failed(Auth.SignedIn ? "offline" : "signed_out");
                return;
            }
            _app.Profile = profile;
            OnboardingScreen.Lead(_app);
            _app.ProfileRead();
            // Once signed in: push registration, IAPHUB's user, and any link or notification that opened the app.
            // Once, though the profile can arrive twice (as it was kept, then as it is now).
            if (!_profileSetUp)
            {
                _profileSetUp = true;
                Push.Register(_app);
                Iap.Login(profile);
                DeepLinks.Ready();
                Push.Flush(_app);
            }
            _greeting.text = Greeting(DateTime.Now.Hour, FirstName(_app.Profile)).ToUpperInvariant();
            // Profiles: who is putting, beside the greeting, when there is anyone to switch to.
            while (_greetRow.childCount > 1) _greetRow.RemoveAt(1);
            if (Profiles.HasSwitch(_app.Profile)) _greetRow.Add(Profiles.Switcher(_app));
            LiveCard.Fill(_app, _live);
            VersusCard.Fill(_app, _versus);
            // A Kid profile: no coach on the lead card, and no League Night or prize tournaments on Home.
            if (Kid.IsKid(_app.Profile)) _compete.Clear();
            Lead();
            if (!_profileCounted) { _profileCounted = true; Arrived(); }
        }

        /// <summary>One of Home's reads: what it answered last time is shown at once, then what it answers now.
        /// counted: one of the answers the loader waits for (told once, whichever comes first).</summary>
        void Read(string path, Action<object> show, bool counted = false, Action none = null)
        {
            bool told = false;
            App.Run(Api.Kept(path, data =>
            {
                show(data);
                if (counted && !told) { told = true; Arrived(); }
            }, why =>
            {
                none?.Invoke();
                if (counted && !told) { told = true; Failed(why); }
                else if (why == "signed_out") _app.SignedOut();
            }));
        }

        // Everything Home shows that changes with play. Each part is replaced when its answer arrives.
        void Data()
        {
            Read("/streaks/", data => { _streak = data; Lead(); }, counted: true);
            // Not waited for by the loader: the rest of Home is usable without them.
            Read("/profile/userStats", data => { _calendar = ProfileMath.Calendar(J.First(data)); Lead(); });
            // The coach and the last 30 days, which build today's session exactly as the Improve tab builds it.
            Read("/coach/settings", data => { _coach = data ?? new Dictionary<string, object>(); Lead(); }, none: () => { _coach = new Dictionary<string, object>(); Lead(); });
            Read("/profile/analysis/30", data => { _grid = AnalysisMath.Grid(J.List(data)); Lead(); }, none: () => { _grid = AnalysisMath.Grid(new List<object>()); Lead(); });
            Read("/challenges/userChallenges", data => Cards.Challenges(_app, _challenges, J.List(data), joinedFirst: true));
            // Legacy MainScreen: League Night and tournaments are not offered to a Kid profile on Home.
            if (Kid.IsKid(_app.Profile)) _compete.Clear();
            else Compete.Fill(_app, _compete);
            Read("/tracks/featured", data => TrackOfTheWeek.Fill(_app, _track, data));
            Read("/profile/recentGames/", data => ShowRecent(J.List(data)), counted: true);
            Read("/games/recommended/", data => ShowPicks(J.List(data)), counted: true);
            _members = _community = null;
            _followsNoOne = false;
            bool feedTold = false;
            void FeedTold() { if (feedTold) return; feedTold = true; Arrived(); }
            App.Run(Api.Kept("/profile/memberGames/", data =>
            {
                _members = Feed.Clean(J.List(data));
                if (_members.Count > 0) { ShowFeed(); FeedTold(); return; }
                // Nobody followed yet: the community's games instead, as the app does, and "+ Following" when the
                // Player follows no one at all.
                LoadCommunity(FeedTold);
                Read("/profile/following/", f => { _followsNoOne = J.List(f).Count == 0; ShowFeed(); });
            }, why => { if (!feedTold) { feedTold = true; Failed(why); } }));
        }

        static System.Collections.IEnumerator Later(float seconds, Action then)
        {
            yield return new WaitForSecondsRealtime(seconds);
            then();
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

        // ── The lead card (CoachCallout) ─────────────────────────────────────

        object _streak, _coach;
        List<AnalysisMath.Cell> _grid;
        Dictionary<string, int> _calendar;

        void Lead()
        {
            _lead.Clear();
            _lead.OnTapOnce(null);   // only the coach states open Improve from the card body

            int current = J.Int(_streak, "currentStreak"), today = J.Int(_streak, "puttsToday"), goal = Mathf.Max(1, J.Int(_streak, "dailyGoal", 25));
            // One row: today's putts against the goal (said here and nowhere else), and the streak in amber, no fill.
            VisualElement streak = current > 0 ? Ui.Box("home-streak", Ui.Icon("FillFire", 16, Ui.Amber), Ui.Caps(current + " day streak", "caps home-streak-t")) : null;
            VisualElement stats = Ui.Box("home-lead-stats", Ui.Box("home-lead-progress", Ui.Num(today.ToString()), Ui.Caps("of " + goal + " putts", "caps home-lead-cap")), streak);
            VisualElement week = Week(today);

            // A Kid profile has no AI coach (MainScreen hides CoachCallout): today's goal and Free putt.
            if (Kid.IsKid(_app.Profile))
            {
                // The numbers are on the row below: the title does not count the putts again.
                string title = today >= goal ? "Goal met" : today == 0 ? "Let's get putting" : "Keep it going";
                _lead.Add(Ui.Caps("Today", "caps caps-green"));
                _lead.Add(Ui.Title(title).Margin(8));
                _lead.Add(stats);
                _lead.Add(week);
                _lead.Add(Ui.Button("Start putting", () => FreePuttScreen.Open(_app), "btn home-lead-btn"));
                return;
            }

            // Still reading the coach: the card's shape, no flash of the wrong state.
            if (_coach == null || _grid == null)
            {
                _lead.Add(Ui.Box("home-lead-head", Ui.Box("sk-bar lead-face"), Ui.Box("home-lead-eyebrow grow", Skeleton.Bar(0.4f, 12f))));
                _lead.Add(Skeleton.Bar(0.7f, 24f).Margin(12));
                _lead.Add(stats);
                _lead.Add(week);
                return;
            }

            // No coach picked yet: "Meet your AI coach" and Choose your coach (opens Improve, where the picker leads).
            if (J.Get(_coach, "exists") != null && !J.Flag(_coach, "exists"))
            {
                VisualElement faces = Ui.Box("lead-faces");
                foreach (Coaches.Persona p in Coaches.All) faces.Add(Coaches.Avatar(p));
                _lead.Add(Ui.Title("Meet your AI coach"));
                _lead.Add(faces.Margin(12));
                _lead.Add(stats);
                _lead.Add(week);
                _lead.Add(Ui.Button("Choose your coach", () => _app.ShowTabs("improve"), "btn home-lead-btn"));
                _lead.OnTapOnce(() => _app.ShowTabs("improve"));
                return;
            }

            Coaches.Persona persona = Coaches.Of(J.Str(_coach, "personaId", "maya"));
            List<TournamentMath.Dealt> putts = TodaysSession.Build(_grid, _coach, DateTime.Now, out string focus, out string why);
            bool done = TodaysSession.DoneToday(_coach, DateTime.Now);
            VisualElement face = Coaches.Avatar(persona);
            face.AddToClassList("lead-face");
            _lead.Add(Ui.Box("home-lead-head", face, Ui.Caps(done ? "Done for today" : "Today's session", "caps caps-green home-lead-eyebrow")));
            _lead.Add(Ui.Title(done ? "Nice work today" : focus).Margin(12));
            _lead.Add(stats);
            _lead.Add(week);
            Action start = () => _app.Push(new SessionBriefScreen(_app, persona, putts, focus, why));
            // Home's one main button; once today's session is done it steps down to the secondary.
            _lead.Add(Ui.Button(done ? "Practice again" : "Start session", start, done ? "btn btn-ghost home-lead-btn" : "btn home-lead-btn"));
            // The card body opens the coach home (Improve); the button is the express lane into the session.
            _lead.OnTapOnce(() => _app.ShowTabs("improve"));
        }

        // This week, Sunday first as CoachCallout's week (moment's startOf('week')): seven 4 px bars, filled on a day
        // with putts, the day's letter under each (today's in white).
        VisualElement Week(int puttsToday)
        {
            VisualElement row = Ui.Box("home-week");
            bool[] active = WeekDots(_calendar, DateTime.Now.Date, puttsToday, out int todayIndex, true);
            const string letters = "SMTWTFS";
            for (int i = 0; i < 7; i++)
                row.Add(Ui.Box(i == 6 ? "home-day home-day-last" : "home-day",
                    Ui.Box(active[i] ? "home-day-bar home-day-bar-on" : "home-day-bar"),
                    Ui.Caps(letters[i].ToString(), i == todayIndex ? "caps home-day-letter home-day-today" : "caps home-day-letter")));
            return row;
        }

        /// <summary>Which days of this week (Monday first) had putts; today counts as soon as there is one.</summary>
        public static bool[] WeekDots(Dictionary<string, int> calendar, DateTime today, int puttsToday, out int todayIndex) =>
            WeekDots(calendar, today, puttsToday, out todayIndex, false);

        /// <summary>Which days of this week had putts, Monday first or (sundayFirst, as the app's week strip) Sunday first.
        /// Today counts as soon as there is one.</summary>
        public static bool[] WeekDots(Dictionary<string, int> calendar, DateTime today, int puttsToday, out int todayIndex, bool sundayFirst)
        {
            todayIndex = sundayFirst ? (int)today.DayOfWeek : ((int)today.DayOfWeek + 6) % 7;
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
            string link = Fixtures.Connected ? "Puttr" : Green.Link;
            _status.EnableInClassList("dot-on", link != null);
            _statusText.text = link != null ? "PUTTR CONNECTED" : "NO PUTTR YET";
        }

        void ShowRecent(List<object> rows)
        {
            // One card a game: the list is every finished game, newest first.
            var seen = new HashSet<string>();
            var games = new List<object>();
            foreach (object row in rows)
            {
                // Only what the Play lists offer and this build can play (GameBridge.Offered): a game last played in the
                // legacy app that is retired here or not brought over yet (chipping, a custom game with no putts) gets no tile.
                // A Tracks round is played on its own screens, not through GameBridge: its course card opens the courses.
                if (!GameBridge.Offered(row) && !IsTracks(row)) continue;
                string key = J.Str(row, "id") + ":" + J.Str(row, "courseId");
                if (seen.Add(key)) games.Add(row);
                if (games.Count == 6) break;
            }
            // The app's row ends with Practice. Free putt keeps no score, so it never says "Last score".
            bool hasFreePutt = false;
            foreach (object game in games) if (J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) hasFreePutt = true;
            if (!hasFreePutt) games.Add(J.Parse("{\"title\":\"Free putt\",\"description\":\"A driving range for putting\",\"gameType\":\"OPEN\",\"putts\":0}"));
            Fill(_recentSection, _recent, games, "gcard-first", game =>
            {
                if (J.Str(game, "gameType").ToUpperInvariant() == FreePuttScreen.GameType) return GameTile(_app, game, "Free play");
                double score = J.Num(game, "lastScore", double.NaN);
                return GameTile(_app, game, double.IsNaN(score) ? null : "Last score " + score.ToString("0.#"));
            });
        }

        void ShowPicks(List<object> rows)
        {
            rows = rows.FindAll(g => !GameBridge.Retired(g));   // the Puttr streak and standard games stay in the legacy app
            if (rows.Count > 8) rows = rows.GetRange(0, 8);
            Fill(_picksSection, _picks, rows, "gcard-first", Pick);
        }

        // A suggestion is a game tile, the same as Jump back in (Matt, 2026-11-24), with why it was picked as its line.
        VisualElement Pick(object game) => GameTile(_app, game, J.Str(game, "recommendReason", null));

        void Fill(VisualElement section, VisualElement into, List<object> games, string firstClass, Func<object, VisualElement> make)
        {
            into.Clear();
            section.EnableInClassList("hidden", games.Count == 0);
            if (games.Count == 0) return;
            ScrollView row = Ui.Scroll(true);
            for (int i = 0; i < games.Count; i++)
            {
                VisualElement tile = make(games[i]);
                if (i == 0) tile.AddToClassList(firstClass);
                row.Add(tile);
            }
            var end = new VisualElement();
            end.style.width = 16;
            row.Add(end);
            into.Add(row);
        }

        // ── The activity feed (FriendActivity) ───────────────────────────────

        List<object> _members, _community;
        bool _followsNoOne, _showCommunity, _communityLoading;

        readonly List<Action> _communityWaiting = new List<Action>();

        // Read once at a time: whoever asks while it is being read is told when it is in.
        void LoadCommunity(Action then)
        {
            if (then != null) _communityWaiting.Add(then);
            if (_communityLoading) return;
            _communityLoading = true;
            void Done(List<object> rows)
            {
                _communityLoading = false;
                _community = rows;
                ShowFeed();
                var waiting = new List<Action>(_communityWaiting);
                _communityWaiting.Clear();
                foreach (Action a in waiting) a();
            }
            App.Run(Api.Kept("/profile/communityGames/", data => Done(Feed.Clean(J.List(data))), _ => Done(new List<object>())));
        }

        void ShowFeed()
        {
            bool friends = _members != null && _members.Count > 0;
            // With friend activity the section is a Following | Community toggle (Following first); without it, the community only.
            bool community = !friends || _showCommunity;
            if (community && _community == null) LoadCommunity(null);
            List<object> rows = community ? _community : _members;
            _feedHead.Clear();
            bool seeCommunity = community;
            Action seeAll = () => _app.Push(new FeedScreen(_app, seeCommunity));
            if (friends)
            {
                // The section header, then the two chips that pick whose games are listed.
                _feedHead.Add(Ui.Section("Activity", "See all", seeAll));
                _feedHead.Add(Ui.Box("pad row home-feed-tabs",
                    FeedTab("Following", !_showCommunity, () => { _showCommunity = false; ShowFeed(); }),
                    FeedTab("Community", _showCommunity, () => { _showCommunity = true; ShowFeed(); })));
            }
            else
            {
                _feedHead.Add(Ui.Section("Community", "See all", seeAll));
                // Not following anyone yet: a chip that opens Find players.
                if (_followsNoOne) _feedHead.Add(Ui.Box("pad row home-feed-tabs", FeedTab("+ Find Players", false, () => _app.Push(new FindPlayersScreen(_app)))));
            }

            _feed.Clear();
            if (rows == null) { for (int i = 0; i < FeedShown; i++) _feed.Add(Ui.Box(i < FeedShown - 1 ? "feed-line" : "feed-line feed-line-last", Ui.Box("sk-bar home-feed-sk"), Ui.Box("grow feed-words", Skeleton.Bar(0.7f, 12f), Skeleton.Bar(0.4f, 8f).Margin(8)))); }
            else
            {
                int shown = Mathf.Min(rows.Count, FeedShown);
                for (int i = 0; i < shown; i++) _feed.Add(FeedLine(rows[i], i == shown - 1));
            }
            _feedSection.EnableInClassList("hidden", friends == false && _community != null && _community.Count == 0);
        }

        static VisualElement FeedTab(string label, bool on, Action tap)
        {
            VisualElement tab = Ui.Box(on ? "fchip fchip-on" : "fchip", Ui.Text(label.ToUpperInvariant(), "fchip-t", Ui.Bold));
            return tap != null ? tab.OnTap(tap) : tab;
        }

        // A row: avatar, "Name scored 18 on Major League" (or "played Practice"), how long ago, and Play. The row opens
        // the game that was played; Play opens that game to play it.
        VisualElement FeedLine(object row, bool last)
        {
            string name = J.Str(row, "displayName", "A Player");
            string gameId = J.Str(row, "gameId");
            string when = Ago.Words(ProfileMath.Date(row, "updated_at"), DateTime.UtcNow);
            VisualElement words = Ui.Box("grow feed-words",
                Ui.Text(name + "  <b>" + Feed.What(row) + "</b>", "feed-what", Ui.Medium),
                when.Length > 0 ? Ui.Text(when, "feed-when", Ui.Medium) : null);
            VisualElement play = Ui.Box("feed-play", Ui.Caps("Play", "caps caps-green")).OnTap(() => PlayFrom(row));
            VisualElement line = Ui.Box(last ? "feed-line feed-line-last" : "feed-line", Ui.Avatar(name, J.Str(row, "photoURL")), words, play);
            return line.OnTap(() => { if (gameId.Length > 0) _app.Push(new GameViewScreen(_app, gameId)); });
        }

        // FriendActivity playGame: the row carries the game's catalogue columns (games.*), so it opens that game.
        void PlayFrom(object row)
        {
            if (IsTracks(row)) { _app.Push(new TracksCoursesScreen(_app)); return; }
            if (J.Str(row, "gameType").Length == 0) { string gameId = J.Str(row, "gameId"); if (gameId.Length > 0) _app.Push(new GameViewScreen(_app, gameId)); return; }
            if (!GameBridge.Offered(row)) { _app.Toast(J.Str(row, "title", "That game") + " is not in this build yet."); return; }
            _app.Push(new GameScreen(_app, row));
        }

        // ── The game card both Home and Play use (puttr-app's GameTile) ──────

        public static bool Playable(object game) => GameBridge.Playable(game);

        public static VisualElement GameTile(App app, object game, string note) =>
            GameCards.Make(game, TileLine(game, note), note != null, false).OnTap(() => OpenGame(app, game));

        /// <summary>A tile's one line: the note it was given, else the game's putts and distance.</summary>
        static string TileLine(object game, string note)
        {
            int putts = J.Int(game, "putts"), feet = J.Int(game, "distance");
            return note ?? (putts > 0 ? putts + " putts" + (feet >= 3 && feet <= 11 ? " · " + feet + " ft" : "") : "");
        }

        static void OpenGame(App app, object game)
        {
            if (IsTracks(game)) app.Push(new TracksCoursesScreen(app)); else app.Push(new GameScreen(app, game));
        }

        static bool IsTracks(object game) => J.Str(game, "gameType").ToUpperInvariant() == "TRACKS";
    }

    /// <summary>The Home feed's words, kept apart from the screen so they can be checked.</summary>
    public static class Feed
    {
        /// <summary>Coach sessions are not community activity: dropped from both feeds (FriendActivity dropCoach).</summary>
        public static List<object> Clean(List<object> rows) => rows.FindAll(r => J.Str(r, "gameType") != "COACHSESSION");

        /// <summary>"scored 18 on Major League" for a game with a leaderboard and a score, else "played Practice".</summary>
        public static string What(object row)
        {
            bool scored = J.Int(row, "hasLeaderboard") == 1 && J.Get(row, "score") != null;
            string title = J.Str(row, "title", "a game");
            return scored ? FeedScreen.Phrase(row) + " on " + title : "played " + title;
        }
    }

    /// <summary>
    /// Today's coach session, built exactly as the Improve tab builds it (ImproveScreen.Today): the coach's daily goal
    /// (3 to 30, default 10) putts dealt by AnalysisMath.Session from the last 30 days, seeded by the day of the year,
    /// so Home and Improve start the same putts on the same day.
    /// </summary>
    public static class TodaysSession
    {
        public static int Count(object coach) => Mathf.Clamp(J.Int(coach, "dailyGoal", 10), 3, 30);

        public static List<TournamentMath.Dealt> Build(List<AnalysisMath.Cell> grid, object coach, DateTime now, out string title, out string why) =>
            AnalysisMath.Session(grid, Count(coach), new System.Random(now.DayOfYear), out title, out why);

        /// <summary>The coach's lastPracticeDate (a date or a full datetime) is today.</summary>
        public static bool DoneToday(object coach, DateTime now) => J.Str(coach, "lastPracticeDate").StartsWith(now.ToString("yyyy-MM-dd"));

        /// <summary>The brief's putt preview: the plan grouped by spot, in the order each spot first comes up.</summary>
        public static List<(int feet, int tee, int count)> Groups(List<TournamentMath.Dealt> putts)
        {
            var groups = new List<(int feet, int tee, int count)>();
            foreach (TournamentMath.Dealt p in putts)
            {
                int at = groups.FindIndex(g => g.feet == p.Feet && g.tee == p.Tee);
                if (at >= 0) groups[at] = (p.Feet, p.Tee, groups[at].count + 1);
                else groups.Add((p.Feet, p.Tee, 1));
            }
            return groups;
        }

        /// <summary>CoachSession's break words: tee 1 breaks left to right, tee 3 right to left, tee 2 straight.</summary>
        public static string Break(int tee) => tee == 1 ? "left to right" : tee == 3 ? "right to left" : "straight";

        /// <summary>The brief's Distance tile: one distance in feet, else Varied.</summary>
        public static string Distance(List<TournamentMath.Dealt> putts)
        {
            if (putts.Count == 0) return "Varied";
            foreach (TournamentMath.Dealt p in putts) if (p.Feet != putts[0].Feet) return "Varied";
            return putts[0].Feet + " ft";
        }

        /// <summary>The brief's Break tile: one break (L→R, Straight, R→L), else Mixed.</summary>
        public static string BreakTile(List<TournamentMath.Dealt> putts)
        {
            if (putts.Count == 0) return "Mixed";
            foreach (TournamentMath.Dealt p in putts) if (p.Tee != putts[0].Tee) return "Mixed";
            return putts[0].Tee == 1 ? "L→R" : putts[0].Tee == 3 ? "R→L" : "Straight";
        }
    }

    /// <summary>
    /// The session brief before putting (CoachSession's 'brief' stage): the coach and "Today's focus", the focus card,
    /// Distance / Break / Putts, "I'm watching: misses and speed", the putt preview, and a footer that stays in view with
    /// "Connect your Puttr to begin." and Start putting, which plays the session (GameBridge.PlayCoach, as Improve).
    /// </summary>
    public sealed class SessionBriefScreen : AppScreen
    {
        public SessionBriefScreen(App app, Coaches.Persona persona, List<TournamentMath.Dealt> putts, string title, string why)
        {
            ScrollView scroll = Ui.Scroll();
            scroll.AddToClassList("grow");
            scroll.Add(Ui.Header("", app.Pop));
            VisualElement face = Coaches.Avatar(persona);
            face.AddToClassList("lead-face");
            VisualElement body = Ui.Box("pad",
                Ui.Box("brief-who", face, Ui.Text("Today's focus", "h2 brief-who-t", Ui.Bold)),
                Ui.Box("glass brief-card", Ui.Text(title, "h1 wrap", Ui.Bold), string.IsNullOrEmpty(why) ? null : Ui.Text(why, "sub wrap", Ui.Regular).Margin(7)),
                Ui.Box("brief-tiles", Tile(TodaysSession.Distance(putts), "Distance"), Tile(TodaysSession.BreakTile(putts), "Break"), Tile(putts.Count.ToString(), "Putts", true)),
                Ui.Box("glass brief-watch", Ui.Icon("Target", 16, Ui.Bright), Ui.Text("I'm watching: <b>misses and speed</b>", "sub brief-watch-t", Ui.Regular)));
            VisualElement preview = Ui.Box("glass brief-preview", Ui.Text("PUTT PREVIEW", "kick brief-label", Ui.Bold));
            foreach ((int feet, int tee, int count) g in TodaysSession.Groups(putts))
                preview.Add(Ui.Box("brief-line", Ui.Text(g.feet + " ft, " + TodaysSession.Break(g.tee), "sub grow", Ui.Regular), Ui.Text("×" + g.count, "brief-count", Ui.Bold)));
            body.Add(preview);
            scroll.Add(body);
            scroll.Add(FreePuttScreen.Space(24));

            // The start stays reachable however long the preview gets.
            Label connect = Ui.Text("Connect your Puttr to begin.", "muted center brief-connect", Ui.Medium);
            void Tick() => connect.style.display = Green.Link == null ? DisplayStyle.Flex : DisplayStyle.None;
            Tick();
            connect.schedule.Execute(Tick).Every(1000);
            VisualElement foot = Ui.Box("pad brief-foot", connect, Ui.Button("Start putting", () => { app.Pop(); app.Games.PlayCoach(putts, title); }, "btn brief-btn"));
            Root = Ui.Box("brief-page", scroll, foot);
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Home");
            if (sheet != null) Root.styleSheets.Add(sheet);
        }

        static VisualElement Tile(string value, string label, bool last = false) =>
            Ui.Box(last ? "brief-tile brief-tile-last" : "brief-tile", Ui.Text(value, "brief-tile-v", Ui.Bold), Ui.Text(label.ToUpperInvariant(), "brief-tile-l", Ui.Medium));
    }

    static class TapOnce
    {
        /// <summary>A tap on a card that is drawn again and again: one Clickable however many times it is drawn.</summary>
        public static void OnTapOnce(this VisualElement e, Action tap)
        {
            if (e.userData is Clickable old) { e.RemoveManipulator(old); e.userData = null; }
            if (tap == null) return;
            var click = new Clickable(() => { Perf.Wake(); tap?.Invoke(); });
            e.AddManipulator(click);
            e.userData = click;
        }
    }
}
