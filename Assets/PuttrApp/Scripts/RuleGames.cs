// The app's rule games (puttr-app components/games/*.js): each is a set of rules over the mat's putts, played on the
// 3D ramp (FreePuttMode) with a panel under it, the way a tournament round is (RoundPanel).
//
//   RuleGame         one game's rules: who putts, from where, what a putt scores, when it is over
//   GameScreen (GameScreens.cs) sets these up: how it plays, how many Players (1 to 4 where the game allows), the game's own options, Start
//   RulePanel        the score bug(s), the tee sign, the call on the last putt, the game's board, the putt read
//   RuleFinalScreen  the winner or the score, the putts, Play again
//
// GameBridge.PlayRule runs one. It is saved as the app saves a game (RoundSaver, the game's gameType): each Player's
// putts under that Player (a profile on this account under its own user id, a guest under none), Puttr's never,
// and with more than one Player the round closes with every Player's score (POST /game/multiplayer/over).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>One putt as a game called it.</summary>
    public sealed class Shot
    {
        public int Player, Feet, Tee;
        public double Points;
        public bool Good;              // the game counts it (a make, a hit, a door)
        public bool? Holed;            // the putt itself went in, where Good means something else (Match Play: the hole was won); the voice reacts to this
        public string Title, Sub;      // the call: "BULLSEYE", "+3"
        public AppPutt.Verdict V;
    }

    public abstract class RuleGame
    {
        /// <summary>The game's own name ("Center Cut"), as the Puttr catalogue has it.</summary>
        protected abstract string OwnTitle { get; }
        /// <summary>The title of the catalogue row the game was opened from, when it has one of its own (a renamed row, a
        /// custom game sharing the gameType). RuleCatalog.Make sets it.</summary>
        public string RowTitle;
        /// <summary>The name the Player sees everywhere the game names itself (the top bar, the panel, the pause card, the
        /// final screen): the catalogue row's title when it was opened from a row, else the game's own.</summary>
        public string Title => string.IsNullOrEmpty(RowTitle) ? OwnTitle : RowTitle;
        public abstract string GameType { get; }
        /// <summary>Three short lines for the setup screen.</summary>
        public abstract string[] HowTo { get; }
        public virtual int MaxPlayers => 1;
        public virtual int MinPlayers => 1;
        public virtual bool LowerIsBetter => false;
        /// <summary>What the score is counted in, for the putt read and the final screen.</summary>
        public virtual string Words => "POINTS";
        public virtual string Icon => "Target";
        public virtual Color Accent => Ui.Bright;

        public List<string> Names = new List<string> { "You" };
        /// <summary>Who each seat saves as (PlayerPicker.UserIds): a profile's user id, RoundSaver.Guest, or null for Puttr.
        /// Null (a game started without the picker) saves the first seat as the signed-in Player and the rest as guests.</summary>
        public List<string> UserIds;
        /// <summary>Who putts first (the setup page's choice). Player 1 is always the signed-in profile: their putts are saved.</summary>
        public int First;
        /// <summary>The seat Puttr plays (its putts are simulated and never saved), or -1.</summary>
        public int Bot = -1;
        /// <summary>Before Puttr's putt: anything Puttr must choose first (HORSE's called spot).</summary>
        public virtual void BotPrepare(System.Random dice) { }
        public int Players => Names.Count;
        /// <summary>Who is putting now.</summary>
        public int P;
        public double[] Score = new double[1];
        /// <summary>Where the putt now is to be played from.</summary>
        public int Feet = 6, Tee = 2;
        public readonly List<Shot> Shots = new List<Shot>();
        readonly System.Random _dice = new System.Random();
        protected int Roll(int from, int to) => _dice.Next(from, to + 1);

        /// <summary>The library sound for a putt the game just called (SoundDrop slots). Null for none.</summary>
        public virtual string SoundFor(Shot s) => Over ? "rule_win" : s.Good ? "rule_make" : "rule_miss";
        /// <summary>The game's ambience under the ramp (a *_loop slot), or null for the room's own.</summary>
        public virtual string Bed => null;

        /// <summary>The game's state for its 3D scene (PuttrArcade.ShotView.Data): see RuleViews.cs and Views/.</summary>
        public virtual void Describe(Dictionary<string, object> data) { }
        /// <summary>The putt just scored, for scenes that need the game's own detail about it.</summary>
        public Shot Last => Shots.Count > 0 ? Shots[Shots.Count - 1] : null;

        /// <summary>The game's own options on the setup screen (a target, a level). Null for none.</summary>
        public virtual VisualElement Options() => null;

        // ── Difficulty: the game page's Easy / Medium / Hard (GameLevels, GameScreens.cs) ──
        // Medium is the game exactly as it has always dealt, so a game started by anything that sets no level (League
        // Night, a script) plays as before. Easy deals every putt from 3 to 6 ft and Hard from 8 to 11 ft: the arcade's
        // own short and long ranges. Only Medium goes on the game's leaderboard (PostsBoard): the board has one key a
        // game, and a round of short putts beside a round of long ones would not be the same contest.

        /// <summary>Set by the game page: an option changed whether the page should offer a Difficulty (Dead Center's
        /// Random putts), so it redraws that part.</summary>
        public Action OptionsChanged;
        /// <summary>GameLevels.Easy, Medium or Hard, set on the game page before Start: the putting Player's own, where
        /// each has one (Levels).</summary>
        public int Difficulty
        {
            get => Levels != null && Levels.Length > 0 ? Levels[Mathf.Clamp(P, 0, Levels.Length - 1)] : _difficulty;
            set => _difficulty = value;
        }
        int _difficulty = GameLevels.Medium;
        /// <summary>puttr-app-2: each Player's own level, in the order of Names, when two or more people play (Matt,
        /// 2026-11-21: a parent on Hard, a child on Easy). Null: everyone plays Difficulty.</summary>
        public int[] Levels;
        /// <summary>What each level deals in this game, a line for Easy, Medium and Hard, shown under the game page's
        /// Difficulty pills. Null for a game the page offers no Easy / Medium / Hard: its putts are fixed by its rules,
        /// the Player picks the spot, or it has a difficulty of its own among its Options.</summary>
        public virtual string[] DifficultyNotes => null;
        /// <summary>The difficulty being played, for the pause card ("Hard", "Diabolical (7 doors)"). Null for none.</summary>
        public virtual string DifficultyWord => DifficultyNotes == null ? null
            : Levels != null && Levels.Length > 1 && Levels.Distinct().Count() > 1 ? string.Join(", ", Enumerable.Range(0, Math.Min(Levels.Length, Names.Count)).Select(i => Names[i] + " " + GameLevels.Names[GameLevels.Clamp(Levels[i])]))
            : GameLevels.Names[GameLevels.Clamp(Levels != null && Levels.Length > 0 ? Levels[0] : _difficulty)];
        /// <summary>False when this game's score is not comparable with the scores on the game's one leaderboard, so it
        /// is saved to the account but not posted there.</summary>
        public virtual bool PostsBoard => DifficultyNotes == null || GameLevels.Posts(Levels != null && Levels.Length > 0 ? Levels[0] : _difficulty);
        /// <summary>The shortest and longest putt this level deals, where Medium deals from..to.</summary>
        protected int FeetFrom(int from = 3) => GameLevels.Band(Difficulty, from, 11).lo;
        protected int FeetTo(int to = 11) => GameLevels.Band(Difficulty, 3, to).hi;
        /// <summary>A distance from the level's range (3 to 11 ft on Medium).</summary>
        protected int RollFeet() => Roll(FeetFrom(), FeetTo());

        public void Start()
        {
            Score = new double[Players];
            First = Mathf.Clamp(First, 0, Players - 1);
            P = First;
            Shots.Clear();
            Begin();
            P = First;
            Deal();
        }

        /// <summary>Set up the game's own state for Players players.</summary>
        protected virtual void Begin() { }
        /// <summary>Set Feet and Tee for the putt P is about to play.</summary>
        protected abstract void Deal();
        /// <summary>Score the putt P just played, change the game's state, and say what happened.</summary>
        protected abstract Shot Take(AppPutt.Verdict v);
        /// <summary>The next Player (skipping anyone who is out). Called after every putt that does not end the game.</summary>
        protected virtual void Next() => P = (P + 1) % Players;
        public abstract bool Over { get; }

        /// <summary>The putt being scored, for a game that reads the ball's path itself (High Striker).</summary>
        protected PuttResult Raw;
        /// <summary>Set by the host: the game changed where to putt from (a shot called, a cup picked).</summary>
        public Action Changed;
        protected void Redeal() { Deal(); Changed?.Invoke(); }

        public Shot Putt(AppPutt.Verdict v, PuttResult raw = null)
        {
            Raw = raw;
            int feet = Feet, tee = Tee, who = P;
            Shot s = Take(v);
            s.Player = who; s.Feet = feet; s.Tee = tee; s.V = v;
            Shots.Add(s);
            if (!Over) { Next(); Deal(); }
            return s;
        }

        /// <summary>"PUTT 3 OF 10", "ROUND 2". Empty for none.</summary>
        public virtual string Progress => "";
        public virtual string ScoreText(int p) => RoundSession.Points(Score[p]);
        /// <summary>The score that is saved for the first Player (/game/over).</summary>
        public virtual int Saved => (int)Math.Round(Score[0]);
        /// <summary>The score saved for seat p when more than one plays (/game/multiplayer/over): the first Player's is Saved.</summary>
        public virtual int SavedFor(int p) => p == 0 ? Saved : p < Score.Length ? (int)Math.Round(Score[p]) : 0;
        /// <summary>The winner, or -1 for a tie. A game played alone has no winner to name.</summary>
        public virtual int Winner
        {
            get
            {
                double best = LowerIsBetter ? Score.Min() : Score.Max();
                int[] at = Enumerable.Range(0, Players).Where(i => Score[i] == best).ToArray();
                return at.Length == 1 ? at[0] : -1;
            }
        }
        /// <summary>The game's state for the panel: letters, cups, a target. Null for none.</summary>
        public virtual VisualElement Board() => null;
        /// <summary>False for a game whose panel is its Board alone: no call card, Players card or putt read (Oh-One's hoppers).</summary>
        public virtual bool ShowsRead => true;
        /// <summary>A big card over the picture after each putt (Oh-One): the large figure and a line under it. False for none.</summary>
        public virtual bool Splash(Shot s, out string big, out string small) { big = small = null; return false; }
        /// <summary>A putt's call in the final screen's list (its title by default).</summary>
        public virtual string FinalCall(Shot s) => s.Title;
        /// <summary>The points a putt earned, for the final screen's list ("8.4 of 10"). Null when the call already says it.</summary>
        public virtual string FinalPoints(Shot s) => null;
        /// <summary>The headline on the final screen.</summary>
        public virtual string Outcome()
        {
            if (Players == 1) return ScoreText(0) + " " + Words.ToLowerInvariant();
            int w = Winner;
            return w < 0 ? "A tie" : Names[w] + (w == 0 && Names[0] == "You" ? " win" : " wins");
        }

        // ── helpers for the games ──

        protected static Shot Call(double points, bool good, string title, string sub = "") => new Shot { Points = points, Good = good, Title = title, Sub = sub };
        protected static string Ft(double inches) => Math.Abs(inches).ToString("0.#", CultureInfo.InvariantCulture) + " in";
        /// <summary>A row of chips: one per item, lit or not.</summary>
        protected static VisualElement Pips(int count, Func<int, bool> on, Func<int, string> label = null, Color? colour = null)
        {
            VisualElement row = Ui.Box("rule-pips");
            for (int i = 0; i < count; i++)
            {
                Label pip = Ui.Text(label != null ? label(i) : "", on(i) ? "rule-pip rule-pip-on" : "rule-pip", Ui.Bold);
                if (on(i) && colour.HasValue) pip.style.backgroundColor = colour.Value;
                row.Add(pip);
            }
            return row;
        }
        protected static VisualElement Card(string kick, params VisualElement[] parts)
        {
            VisualElement card = Ui.Box("sheet-card", Ui.Text(kick, "kick", Ui.Bold));
            foreach (VisualElement p in parts) if (p != null) card.Add(p);
            return card;
        }
    }

    // ── Setup ──

    /// <summary>
    /// Who is playing (puttr-app games/common/PlayerNames.js): you first in the list (your putts are the ones saved), then
    /// anyone added with Add player: a profile on this account, Puttr, or a guest typing a name. Any of them can go first.
    /// </summary>
    public sealed class PlayerPicker
    {
        sealed class Seat { public object Profile; public string Typed = ""; public bool Puttr; public string Brand; public int Level = GameLevels.Medium; }

        public readonly VisualElement Root;
        readonly App _app;
        readonly List<Seat> _seats = new List<Seat>();
        readonly VisualElement _rows;
        readonly Label _hint;
        readonly int _min, _max;
        readonly bool _puttr, _putters;
        int _first;

        public static string Short(object person)
        {
            string name = J.Str(person, "name");
            if (name.Length == 0) name = HomeScreen.FirstName(person);
            return name.Length > 0 ? name : "You";
        }

        /// <param name="puttr">Puttr can be added as a Player (its putts are simulated).</param>
        public PlayerPicker(App app, int min, int max, bool puttr = true) : this(app, min, max, puttr, false) { }

        /// <summary>Compare: the seats are putters, not Players. You putt every one of them, in order.</summary>
        public static PlayerPicker Putters(App app, int min, int max) => new PlayerPicker(app, min, max, false, true);

        PlayerPicker(App app, int min, int max, bool puttr, bool putters)
        {
            _app = app; _min = Math.Max(1, min); _max = Math.Max(_min, max); _puttr = puttr && _max > 1 && !putters; _putters = putters;
            if (putters)
            {
                // Your own putter (profile putterData) first, when you have one.
                Dictionary<string, object> mine = PutterBrands.ParsePutter(J.Get(app.Profile, "putterData"));
                if (mine != null) _seats.Add(new Seat { Brand = J.Str(mine, "brand"), Typed = PutterName(mine) });
            }
            // Host mode (Host.cs): Players type their names, and in Venue Mode members scan in. The host's own
            // profile is never put in a seat. Whoever is here from the last game is seated again (HostSession).
            else if (HostMode.On)
            {
                foreach (object member in HostSession.Members) if (_seats.Count < _max) _seats.Add(new Seat { Profile = member });
                foreach (string guest in HostSession.Guests) if (_seats.Count < _max && guest.Trim().Length > 0) _seats.Add(new Seat { Typed = guest });
                if (_seats.Count == 0) _seats.Add(new Seat());
            }
            else _seats.Add(new Seat { Profile = app.Profile });
            _rows = Ui.Box("");
            _hint = Ui.Text("", "muted wrap", Ui.Regular);
            Root = Ui.Box("", Form.Label(putters ? "Putters" : "Players"), _rows, _hint.Margin(8));
            // Venue Mode: a member scans this to take a seat under their own name (VenueJoin).
            if (HostMode.Venue && !putters) Root.Add(VenueJoin.Block(Joined).Margin(12));
            Draw();
            if (Profiles.Family == null) Profiles.Load(_ => Draw());
        }

        public int Count => _seats.Count;
        public bool Ready => _seats.Count >= _min;

        // A member scanned in (Venue Mode): they take the first seat nobody has typed a name into, or a new one.
        void Joined(object member)
        {
            HostSession.Join(member);
            string id = J.Str(member, "userId");
            if (_seats.Any(x => x.Profile != null && J.Str(x.Profile, "userId") == id)) return;
            Seat empty = _seats.FirstOrDefault(x => x.Profile == null && !x.Puttr && x.Typed.Trim().Length == 0);
            if (empty != null) empty.Profile = member;
            else if (_seats.Count < _max) _seats.Add(new Seat { Profile = member, Level = Level });
            else { _app.Toast(Short(member) + " is signed in. This game is full."); return; }
            Draw();
        }

        /// <summary>The names typed into seats (not the members'): kept for the next game in host mode.</summary>
        public List<string> TypedNames => _seats.Where(x => x.Profile == null && !x.Puttr && x.Typed.Trim().Length > 0).Select(x => x.Typed.Trim()).ToList();

        // ── A level for each Player (the arcade games: Easy, Medium or Hard for every seat) ──
        // With one Player the game page shows the choice as its own Difficulty pills. With more, each Player's row
        // carries a chip that steps through the levels, so a strong putter and a beginner can play the same game.

        string[] _levelWords;
        /// <summary>The seats or a level changed: the game page redraws what depends on them.</summary>
        public Action Changed;
        /// <summary>Turns on a level for every seat: the three words for it ("Rookie", "Pro", "All-Star") and the signed-in Player's.</summary>
        public void ShowLevels(string[] words, int mine) { _levelWords = words; foreach (Seat seat in _seats) seat.Level = GameLevels.Clamp(mine); Draw(); }
        /// <summary>Turns the per-seat level off again (a rule game option took the game's Difficulty away).</summary>
        public void HideLevels() { if (_levelWords == null) return; _levelWords = null; Draw(); }
        /// <summary>True when the seats carry levels at all.</summary>
        public bool HasLevels => _levelWords != null;
        /// <summary>How many seats are people (Puttr is not).</summary>
        public int People => _seats.Count(x => !x.Puttr);
        /// <summary>True while every Player's row shows its own level chip.</summary>
        public bool LevelChips => _levelWords != null && People > 1;
        /// <summary>The signed-in Player's level (the first seat): the one remembered for the game.</summary>
        public int Level { get => _seats.Count > 0 ? _seats[0].Level : GameLevels.Medium; set { if (_seats.Count > 0) _seats[0].Level = GameLevels.Clamp(value); } }
        /// <summary>Each seat's level, in the order of Names.</summary>
        public List<int> Levels => _seats.Select(x => x.Level).ToList();
        static Color LevelColour(int level) => level == GameLevels.Easy ? Ui.Bright : level == GameLevels.Hard ? Ui.Red : Ui.Amber;
        /// <summary>Who putts first: an index into Names.</summary>
        public int First => Mathf.Clamp(_first, 0, _seats.Count - 1);
        /// <summary>Puttr's seat, or -1.</summary>
        public int PuttrSeat => _seats.FindIndex(x => x.Puttr);
        static string PutterName(Dictionary<string, object> p) => (J.Str(p, "brand") + " " + J.Str(p, "model")).Trim();

        public string Need => _putters ? (_min - _seats.Count == 1 ? "Add another putter to compare." : "Add " + (_min - _seats.Count) + " putters to compare.")
            : _min - _seats.Count == 1 ? "Add a Player" + (_puttr ? " or Puttr" : "") + " to play." : "Add " + (_min - _seats.Count) + " more Players to play.";

        /// <summary>
        /// Who each seat saves as, in the order of Names (puttr-app's playerNames[i].userId): the signed-in Player and
        /// the other profiles on this account under their own user ids, a guest under RoundSaver.Guest (no account, as
        /// the app's 0), Puttr under null (never saved). Compare's seats are putters: the app saves those putts under
        /// no one (Fitting.js sends 0), so they stay out of the Player's stats.
        /// </summary>
        public List<string> UserIds => Enumerable.Range(0, _seats.Count).Select(i =>
        {
            Seat seat = _seats[i];
            if (seat.Puttr) return null;
            if (_putters || seat.Profile == null) return RoundSaver.Guest;
            // In host mode the first seat is whoever sat there (a member under their own id), not the host.
            string id = i == 0 && !HostMode.On ? Auth.Uid : J.Str(seat.Profile, "userId");
            return string.IsNullOrEmpty(id) ? RoundSaver.Guest : id;
        }).ToList();

        public List<string> Names => Enumerable.Range(0, _seats.Count).Select(i => _seats[i].Puttr ? "Puttr" : _seats[i].Profile != null ? Short(_seats[i].Profile) : _seats[i].Typed.Trim().Length > 0 ? _seats[i].Typed.Trim() : (_putters ? "Putter " : "Player ") + (i + 1)).ToList();

        // Everyone on this account who can be added: the other profiles not already playing.
        List<object> Choices()
        {
            var list = new List<object>();
            if (HostMode.On) return list;   // the profiles on the host's account are not offered to the room
            foreach (object pl in J.List(J.Get(Profiles.Family, "players"))) list.Add(pl);
            return list.Where(x => J.Str(x, "userId") != J.Str(_app.Profile, "userId") && !_seats.Any(q => q.Profile != null && J.Str(q.Profile, "userId") == J.Str(x, "userId"))).ToList();
        }

        void Draw()
        {
            _rows.Clear();
            for (int i = 0; i < _seats.Count; i++)
            {
                int at = i;
                Seat seat = _seats[i];
                VisualElement swatch = Ui.Box("rule-swatch");
                swatch.style.backgroundColor = RulePanel.PlayerColour(i);
                VisualElement who;
                if (seat.Puttr)
                {
                    var mark = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite"), tintColor = Ui.Bright, scaleMode = ScaleMode.ScaleToFit };
                    mark.style.width = mark.style.height = 26;
                    who = Ui.Box("row grow", Ui.Box("avatar picker-puttr", mark), Ui.Box("grow", Ui.Text("Puttr", "g-name", Ui.Bold), Ui.Text("Plays its own putts", "muted", Ui.Regular)));
                }
                else if (seat.Profile != null)
                    who = Ui.Box("row grow", Ui.Avatar(Short(seat.Profile), J.Str(seat.Profile, "photoURL")), Ui.Box("grow", Ui.Text(Short(seat.Profile), "g-name", Ui.Bold), Ui.Text(HostMode.On ? "Puttr member" : i == 0 ? "You" : J.Str(seat.Profile, "homeTown"), "muted", Ui.Regular)));
                else
                {
                    string placeholder = _putters ? "Putter " + (i + 1) : "Player name";
                    if (Tv.CanType) who = Tv.TextEntry(seat.Typed, placeholder, typed => seat.Typed = typed);   // a text field is out of the remote's reach
                    else
                    {
                        var field = new TextField { value = seat.Typed };
                        field.textEdition.placeholder = placeholder;
                        field.textEdition.hidePlaceholderOnFocus = false;
                        field.AddToClassList("field");
                        field.AddToClassList("grow");
                        field.RegisterValueChangedCallback(e => seat.Typed = e.newValue);
                        who = field.Face(Ui.Medium);
                    }
                    if (_putters) who = Ui.Box("row grow", seat.Brand != null && PutterBrands.Has(seat.Brand) ? PutterBrands.Badge(seat.Brand, 28f) : Ui.Icon("Target", 22, Ui.Faint), FreePuttScreen.Space(8).Width(8), who);
                }
                bool first = _seats.Count > 1 && at == First;
                // Putters are played in the order listed: no "Go first".
                VisualElement order = _seats.Count > 1 && !_putters
                    ? Ui.Box(first ? "picker-first picker-first-on" : "picker-first", Ui.Text(first ? "1st" : "Go first", "picker-first-t", Ui.Bold)).OnTap(() => { _first = at; Draw(); })
                    : null;
                VisualElement row = Ui.Box("glass row picker-row", swatch, who, order);
                if (LevelChips && !seat.Puttr)
                {
                    // Tap to step Easy, Medium, Hard (as the chip on the arcade's own setup screen does).
                    Label word = Ui.Text(_levelWords[GameLevels.Clamp(seat.Level)], "picker-first-t", Ui.Bold);
                    word.style.color = LevelColour(seat.Level);
                    row.Add(Ui.Box("picker-first", word).OnTap(() => { seat.Level = (GameLevels.Clamp(seat.Level) + 1) % 3; Draw(); }));
                }
                if (at > 0 || (_putters && _seats.Count > 1) || (HostMode.On && seat.Profile != null)) row.Add(Ui.Box("picker-remove", Ui.Text("\u2212", "picker-remove-t", Ui.Bold)).OnTap(() => { if (HostMode.On && seat.Profile != null) HostSession.Leave(J.Str(seat.Profile, "userId")); _seats.RemoveAt(at); if (_seats.Count == 0) _seats.Add(new Seat()); if (_first >= _seats.Count || _first == at) _first = 0; else if (_first > at) _first--; Draw(); }));
                _rows.Add(row.Margin(8));
            }
            if (_seats.Count < _max) _rows.Add(Ui.Button(_putters ? "+  Add putter" : "+  Add player", _putters ? (Action)AddPutter : Add, "btn btn-ghost picker-add-btn").Margin(8));
            _hint.text = !Ready ? Need : _putters ? "You putt the same putts with each putter, one after another." : _seats.Count > 1 ? "Pass the putter between turns. " + SavedHint() : "";
            _hint.style.color = Ready ? (Color)Ui.Faint : Ui.Amber;
            _hint.style.display = _hint.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            Changed?.Invoke();
        }

        // Where the putts of a game for these seats go: to each profile's account (nowhere under -app2HoldPlay).
        string SavedHint()
        {
            if (Api.HoldsPlay) return Api.NotSaved + ".";
            bool profiles = false, guests = false;
            for (int i = 1; i < _seats.Count; i++) { if (_seats[i].Profile != null) profiles = true; else if (!_seats[i].Puttr) guests = true; }
            if (profiles && guests) return "Each profile's putts are saved to its own account. A guest's are not saved to any account.";
            if (profiles) return "Each profile's putts are saved to its own account.";
            if (guests) return "Your putts are saved to your account. A guest's are not saved to any account.";
            return "Your putts are saved to your account. Puttr's are not saved.";
        }

        // Compare's Add putter sheet: pick the brand, then name the model in its row.
        void AddPutter()
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Title("Add a putter", "title-20"));
            ScrollView list = Ui.Scroll();
            list.style.maxHeight = 420;
            foreach (string b in PutterBrands.Names)
            {
                string brand = b;
                VisualElement badge = brand != "Other" && PutterBrands.Has(brand) ? PutterBrands.Badge(brand, 26f) : Ui.Icon("Target", 22, Ui.Bright);
                list.Add(Ui.Box("set-row", badge, FreePuttScreen.Space(10).Width(10), Ui.Text(brand, "g-name grow", Ui.Bold)).OnTap(() =>
                {
                    dim.RemoveFromHierarchy();
                    _seats.Add(new Seat { Brand = brand == "Other" ? null : brand, Typed = brand == "Other" ? "" : brand + " " });
                    Draw();
                }));
            }
            card.Add(list);
            card.Add(Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        // The app's Select Player sheet: the profiles on this account, Puttr, or a guest who types a name.
        void Add()
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Title("Add a Player", "title-20"));
            void Pick(Seat seat) { dim.RemoveFromHierarchy(); seat.Level = Level; _seats.Add(seat); Draw(); }   // a new Player starts at the first Player's level
            foreach (object c in Choices())
            {
                object person = c;
                card.Add(Ui.Box("set-row", Ui.Avatar(Short(person), J.Str(person, "photoURL")), FreePuttScreen.Space(10).Width(10), Ui.Text(Short(person), "g-name grow", Ui.Bold)).OnTap(() => Pick(new Seat { Profile = person })));
            }
            if (_puttr && PuttrSeat < 0)
            {
                var mark = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite"), tintColor = Ui.Bright, scaleMode = ScaleMode.ScaleToFit };
                mark.style.width = mark.style.height = 22;
                card.Add(Ui.Box("set-row", Ui.Box("avatar picker-puttr", mark), Ui.Box("grow", Ui.Text("Puttr", "g-name", Ui.Bold), Ui.Text("Play against Puttr", "muted", Ui.Regular))).OnTap(() => Pick(new Seat { Puttr = true })));
            }
            card.Add(Ui.Box("set-row", Ui.Icon("UserPlus", 22, Ui.Bright), FreePuttScreen.Space(10).Width(10), Ui.Text("Guest", "g-name grow", Ui.Bold)).OnTap(() => Pick(new Seat())));
            card.Add(Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }
    }

    // ── The panel under the ramp ──

    public sealed class RulePanel
    {
        public readonly VisualElement Root;
        readonly App _app;
        readonly RuleGame _game;
        readonly Action _leave;
        readonly VisualElement _over, _sheet, _tee = new VisualElement();
        // Wide: the game is the whole screen, as an arcade game is. Where to putt from is top left with the game's own
        // cards under it (_left); the arcade's ideal putt card is top right with the putt read under it (_sheet).
        readonly VisualElement _left, _leftTee = new VisualElement();
        readonly ScrollView _leftScroll;
        VisualElement _labTee;
        readonly ScrollView _scroll;
        bool _wide, _laidOut, _offline, _statusUp;
        Rect _aimAt;
        int _aimFeet, _aimTee;

        /// <summary>Whether the game takes test putts now; null for always. A League Night game played as this rule
        /// game sets it (GameBridge.PlayRuleRound): a game being entered takes real putts only.</summary>
        public Func<bool> TakesTestPutts;

        public RulePanel(App app, RuleGame game, Action leave)
        {
            _app = app; _game = game; _leave = leave;
            // The Lab on a tablet or TV: the tee and distance go under its trace, not over its readout (Matt, 2026-11-21).
            PuttrArcade.LabScene.TeeUnderTrace = game is LabGame && !Frame.TeeInBand;
            Root = Ui.Box("frame");
            Root.pickingMode = PickingMode.Ignore;
            Root.Face(Ui.Medium);
            _over = Ui.Box("frame-over");
            _over.pickingMode = PickingMode.Ignore;
            _sheet = Ui.Box("sheet");
            _scroll = Ui.Scroll();
            // Above the cards and never scrolled away: where to putt from (a phone), then why a putt did not count,
            // "Puttr not connected", and a tester's test putt buttons.
            _tee.pickingMode = PickingMode.Ignore;
            _sheet.Add(_tee);
            _sheet.Add(new PuttStrip(app, () => TakesTestPutts == null || TakesTestPutts()).Root);
            _sheet.Add(_scroll);
            _left = Ui.Box("sheet sheet-over sheet-dock");
            _left.pickingMode = PickingMode.Ignore;
            _leftTee.pickingMode = PickingMode.Ignore;
            _leftScroll = Ui.Scroll();
            _left.Add(_leftTee);
            _left.Add(_leftScroll);
            Root.Add(_over);
            Root.Add(_left);
            Root.Add(_sheet);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Root.schedule.Execute(Place).Every(300);
            // Wide, a rule game's world is the whole screen and these cards lie over it (the mat too, for a game with
            // no world of its own).
            Root.RegisterCallback<AttachToPanelEvent>(_ => PuttrArcade.FreePuttMode.Layered = true);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => { PuttrArcade.FreePuttMode.Layered = false; PuttrArcade.Arcade.Current?.HideAim(); });
            Show();
        }

        /// <summary>Wide: the arcade's ideal putt card, top right (center cut from the tee dealt), as an arcade game has it.</summary>
        void ShowAim(bool wide)
        {
            PuttrArcade.Arcade arcade = PuttrArcade.Arcade.Current;
            if (arcade == null) return;
            if (!wide || _game.Over) { if (_aimFeet != 0) { _aimFeet = 0; arcade.HideAim(); } return; }
            if (arcade.AimShowing && _aimFeet == _game.Feet && _aimTee == _game.Tee) return;
            _aimFeet = _game.Feet; _aimTee = _game.Tee;
            arcade.ShowAim(_game.Feet, _game.Tee, 0f, PuttrArcade.Arcade.HoleInches);
        }

        void Place()
        {
            bool wide = Screen.width > Screen.height;
            // The Puttr coming or going changes what the sheet says about saving.
            if (_laidOut && PuttStrip.SaysOffline(_app.Profile) != _offline) Show();
            ShowAim(wide);
            // The Lab's tee goes under its trace on an upright tablet only: wide, it is top left with every other game's.
            PuttrArcade.LabScene.TeeUnderTrace = _game is LabGame && !Frame.TeeInBand && !wide;
            // The arcade's "Looking for your Puttr" chip coming or going moves the score bug a row (upright).
            bool statusUp = PuttrArcade.Hud.StatusUp;
            PuttrArcade.Arcade arcade = PuttrArcade.Arcade.Current;
            Rect aim = wide && arcade != null && arcade.Hud != null ? arcade.Hud.AimCardPx : Rect.zero;
            if (_laidOut && wide == _wide && statusUp == _statusUp && aim == _aimAt) return;
            _laidOut = true; _wide = wide; _statusUp = statusUp; _aimAt = aim;
            Vector4 safe = _app.SafeInsets();
            float picture = PuttrArcade.FreePuttMode.UprightShare;
            float panelW = Root.resolvedStyle.width, k = panelW > 0f && Screen.width > 0 ? panelW / Screen.width : 1f;
            float dock = Mathf.Clamp(panelW * 0.23f, 230f, 330f);
            _sheet.EnableInClassList("sheet-wide", wide);
            _sheet.EnableInClassList("sheet-over", wide);   // no column: the cards float over the game's world
            _sheet.EnableInClassList("sheet-dock", wide);
            _left.style.display = wide ? DisplayStyle.Flex : DisplayStyle.None;
            if (wide)
            {
                // Right: under the ideal putt card, its right edge on the card's.
                float right = aim.width > 0f ? Mathf.Max(safe.z, (Screen.width - aim.xMax) * k) : safe.z + 12f;
                float top = aim.width > 0f ? (Screen.height - aim.yMin) * k + 4f : Mathf.Max(safe.w, 0f) + 64f;
                _sheet.style.left = StyleKeyword.Auto;
                _sheet.style.right = right;
                _sheet.style.width = Mathf.Max(dock, aim.width * k);
                _sheet.style.top = top;
                // Left: where to putt from, then the game's cards.
                _left.style.left = safe.x + 12f;
                _left.style.top = Mathf.Max(safe.w, 0f) + 76f;   // under the logo and the game's name
                _left.style.width = dock;
                _left.style.bottom = 0;
                _left.style.paddingBottom = safe.y;
            }
            else
            {
                _sheet.style.left = 0;
                _sheet.style.right = 0;
                _sheet.style.width = StyleKeyword.Auto;
                _sheet.style.top = Length.Percent(picture * 100f);
            }
            _sheet.style.bottom = 0;
            _sheet.style.paddingBottom = safe.y;
            _sheet.style.paddingRight = StyleKeyword.Null;
            _sheet.style.paddingTop = 0f;
            _over.style.left = 0;
            _over.style.top = Mathf.Max(safe.w, 0f) + (wide ? 44f : 52f) + (!wide && statusUp ? PuttrArcade.Hud.StatusRowPx : 0f);
            _over.style.width = Length.Percent(100f);
            _over.style.height = wide ? Length.Percent(60f) : Length.Percent(picture * 100f - 8f);
            Show();
        }

        public void Show()
        {
            Perf.Wake(3f);
            RuleGame g = _game;
            _offline = PuttStrip.SaysOffline(_app.Profile);
            _over.Clear();
            string photo = g.P == 0 ? J.Str(_app.Profile, "photoURL") : null;
            VisualElement bug = Ui.Box("frame-bug", Frame.ScoreBug(g.Names[g.P], photo, g.ScoreText(g.P), g.Over ? "FINAL" : g.Progress));
            // A game whose panel is its board alone (Oh-One's hoppers) says the score there, not twice (Matt, 2026-11-21).
            if (!g.ShowsRead) bug.style.display = DisplayStyle.None;
            _over.Add(bug);
            // The arcade's console chip ("Looking for your Puttr") goes under the score bug, not over it.
            bug.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                VisualElement top = bug.panel?.visualTree;
                if (top == null || top.layout.height <= 0f) return;
                PuttrArcade.Hud.StatusBelowPx = bug.worldBound.yMax * Screen.height / top.layout.height;
            });
            bug.RegisterCallback<DetachFromPanelEvent>(_ => PuttrArcade.Hud.StatusBelowPx = 0f);
            // A phone has the tee in a band under the picture: a sign over the scene covered the game and the ball's replay.
            _tee.Clear();
            _leftTee.Clear();
            _labTee?.RemoveFromHierarchy(); _labTee = null;
            bool wide = Screen.width > Screen.height;
            if (g.Over) { }
            else if (wide)
            {
                // Top left, as the arcade's tee sign is: the band on a phone, the sign on anything bigger.
                VisualElement sign = Frame.TeeInBand ? Frame.TeeBand(g.Feet, g.Tee) : Frame.TeeSign(g.Feet, g.Tee);
                sign.AddToClassList("tee-docked");
                _leftTee.Add(sign);
            }
            else if (Frame.TeeInBand) _tee.Add(Frame.TeeBand(g.Feet, g.Tee));
            else if (PuttrArcade.LabScene.TeeUnderTrace) Root.Add(_labTee = LabTee(g.Feet, g.Tee));
            else _over.Add(Frame.TeeSign(g.Feet, g.Tee));
            ShowAim(wide);

            _scroll.Clear();
            _leftScroll.Clear();
            // Wide, the game's own cards (the call, its board, the Players) are on the left; the putt read is on the right.
            VisualElement mine = wide ? _leftScroll : (VisualElement)_scroll;
            Shot last = g.Shots.Count > 0 ? g.Shots[g.Shots.Count - 1] : null;
            string turn = g.Over ? (g.Players > 1 ? g.Outcome() : "Game over") : g.Players > 1 && g.Names[g.P] != "You" ? g.Names[g.P] + "'s putt" : "Your putt";
            // While the game is on the sheet starts with the game itself (the call, the board): the name is in the
            // header, whose turn it is in the score bar, and leaving is always pause, then Exit (Matt, 2026-11-18: no
            // Leave button of a game's own). Over, this card is the way on to the results.
            if (g.Over)
                _scroll.Add(Ui.Box("sheet-card row",
                    Ui.Box("grow", Ui.Text(g.Title.ToUpperInvariant(), "kick", Ui.Bold), Ui.Text(turn, "g-name", Ui.Bold).Margin(4)),
                    Ui.Box("btn-small", Ui.Text("Results", "btn-text", Ui.Bold)).OnTap(_leave)));
            // With no Puttr and no test putts the strip says "Puttr not connected": "test putts" would promise a way to putt.
            if (!_offline && (Fixtures.On || Green.RealPutts == false)) _scroll.Add(Ui.Text("Test putts: nothing is saved.", "sheet-note", Ui.Bold));
            else if (Api.HoldsPlay) _scroll.Add(Ui.Text(Api.NotSaved + ".", "sheet-note", Ui.Bold));

            if (last != null && last != _splashed && g.Splash(last, out string bigText, out string smallText)) ShowSplash(last, bigText, smallText);
            if (last != null && g.ShowsRead)
            {
                Label call = Ui.Text(last.Title, "rule-call", Ui.Bold);
                call.style.color = last.Good ? (Color)Ui.Bright : Ui.Red;
                VisualElement callCard = Ui.Box("sheet-card rule-callcard", call);
                if (!string.IsNullOrEmpty(last.Sub)) callCard.Add(Ui.Text(last.Sub, "sub wrap", Ui.Medium));
                mine.Add(callCard);
            }
            VisualElement board = g.Board();
            if (board != null) mine.Add(board);
            if (g.Players > 1 && g.ShowsRead)
            {
                VisualElement scores = Ui.Box("sheet-card");
                scores.Add(Ui.Text("PLAYERS", "kick", Ui.Bold));
                for (int i = 0; i < g.Players; i++)
                {
                    Label value = Ui.Num(g.ScoreText(i), "g-score");
                    VisualElement row = Ui.Box("board-row", Ui.Box("rule-swatch"), Ui.Box("grow", Ui.Text(g.Names[i], "g-name", Ui.Bold)), value);
                    row.Q(className: "rule-swatch").style.backgroundColor = PlayerColour(i);
                    if (i == g.P && !g.Over) row.AddToClassList("rule-now");
                    scores.Add(row);
                }
                mine.Add(scores);
            }
            if (!g.ShowsRead) { }
            else if (last != null) _scroll.Add(Frame.PuttRead(last.V, last.Points, !(g is SensorGame), g.Words, Scoring.BackDoor(last.V, last.Feet, last.Tee)));   // a sensor game scores the stroke: its points are in the call
            else _scroll.Add(Ui.Box("sheet-card", Ui.Text(_offline ? "Connect your Puttr to begin. After each putt its read appears here: line, pace and entry." : "Putt when you are ready. After each putt its read appears here: line, pace and entry.", "sub", Ui.Regular)));
            _scroll.Add(FreePuttScreen.Space(16));
            _scroll.scrollOffset = Vector2.zero;
            _leftScroll.Add(FreePuttScreen.Space(16));
            _leftScroll.scrollOffset = Vector2.zero;
        }

        Shot _splashed;
        VisualElement _splash;

        /// <summary>The putt's score, big over the picture for a moment (Matt, 2026-11-21: Oh-One).</summary>
        void ShowSplash(Shot s, string big, string small)
        {
            _splashed = s;
            _splash?.RemoveFromHierarchy();
            bool wide = Screen.width > Screen.height;
            float pic = wide ? PuttrArcade.FreePuttMode.WideShare : PuttrArcade.FreePuttMode.UprightShare;
            Label figure = Ui.Text(big, "splash-big", Ui.Bold);
            figure.style.color = s.Good ? (Color)Ui.Bright : Ui.Red;
            VisualElement card = Ui.Box("splash-card", figure);
            if (!string.IsNullOrEmpty(small)) card.Add(Ui.Text(small, "splash-small", Ui.Bold));
            VisualElement layer = Ui.Box("splash", card);
            layer.pickingMode = PickingMode.Ignore;
            layer.style.width = Length.Percent(100f);   // wide, the picture is the whole screen
            layer.style.height = wide ? Length.Percent(100f) : Length.Percent(pic * 100f);
            Root.Add(layer);
            _splash = layer;
            layer.schedule.Execute(() => { if (_splash == layer) { layer.RemoveFromHierarchy(); _splash = null; } }).ExecuteLater(2600);
        }

        /// <summary>The Lab's tee and distance in the slot its scene leaves under the trace (LabScene.TeeSlot, fractions of
        /// the picture: the left of the screen when wide, the top when upright).</summary>
        VisualElement LabTee(int feet, int tee)
        {
            Rect s = PuttrArcade.LabScene.TeeSlot;
            bool wide = Screen.width > Screen.height;
            float sx = wide ? PuttrArcade.FreePuttMode.WideShare : 1f, sy = wide ? 1f : PuttrArcade.FreePuttMode.UprightShare;
            VisualElement band = Frame.TeeBand(feet, tee);
            band.AddToClassList("tee-band-lab");
            VisualElement slot = Ui.Box("tee-lab-slot", band);
            slot.pickingMode = PickingMode.Ignore;
            slot.style.left = Length.Percent(s.xMin * sx * 100f);
            slot.style.top = Length.Percent(s.yMin * sy * 100f);
            slot.style.width = Length.Percent(s.width * sx * 100f);
            slot.style.height = Length.Percent(s.height * sy * 100f);
            return slot;
        }

        /// <summary>puttr-app constants/PlayerColors.js: blue, orange, purple, green last.</summary>
        public static Color PlayerColour(int i) => i switch { 0 => new Color32(0x56, 0x87, 0xFF, 0xFF), 1 => new Color32(0xFF, 0x8A, 0x3D, 0xFF), 2 => new Color32(0xB3, 0x6B, 0xFF, 0xFF), _ => new Color32(0x7A, 0xD4, 0x46, 0xFF) };
    }

    // ── After the game ──

    public sealed class RuleFinalScreen : AppScreen
    {
        public RuleFinalScreen(App app, object game, RuleGame g, Action again)
        {
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("", app.ExitToTab));
            int made = g.Shots.Count(s => s.Player == 0 && s.V != null && s.V.Made), mine = g.Shots.Count(s => s.Player == 0);
            VisualElement top = Ui.Box("pad", Ui.Text("FINAL   ·   " + g.Title.ToUpperInvariant(), "kick center", Ui.Bold));
            if (g.Players == 1)
            {
                top.Add(Ui.Num(g.ScoreText(0), "num-44 center"));
                top.Add(Ui.Text(g.Words.ToLowerInvariant() + "   ·   " + made + " of " + mine + " holed", "sub center", Ui.Regular));
            }
            else
            {
                top.Add(Ui.Title(g.Outcome(), "title center wrap"));
                VisualElement table = Ui.Box("glass");
                foreach (int i in Enumerable.Range(0, g.Players).OrderBy(i => g.LowerIsBetter ? g.Score[i] : -g.Score[i]))
                {
                    VisualElement row = Ui.Box("board-row", Ui.Box("rule-swatch"), Ui.Box("grow", Ui.Text(g.Names[i], "g-name", Ui.Bold)), Ui.Num(g.ScoreText(i), "g-score"));
                    row.Q(className: "rule-swatch").style.backgroundColor = RulePanel.PlayerColour(i);
                    table.Add(row);
                }
                top.Add(table.Margin(16));
            }
            VisualElement list = Ui.Box("glass");
            list.Add(Ui.Text("PUTTS", "kick", Ui.Bold));
            int n = 0;
            foreach (Shot s in g.Shots)
            {
                n++;
                // The game's call for the putt and, for a game whose call doesn't already say it, the points it earned.
                Label value = Ui.Text(g.FinalCall(s), "g-score", Ui.Bold);
                value.style.color = s.Good ? (Color)Ui.Bright : Ui.Faint;
                VisualElement right = value;
                string points = g.FinalPoints(s);
                if (!string.IsNullOrEmpty(points))
                {
                    Label earned = Ui.Text(points, "sub", Ui.Medium);
                    earned.style.marginLeft = 12;
                    earned.style.unityTextAlign = TextAnchor.MiddleRight;
                    right = Ui.Box("", value, earned);
                    right.style.alignItems = Align.FlexEnd;
                }
                list.Add(Ui.Box("board-row", Ui.Text(n.ToString(), "board-place", Ui.Bold),
                    Ui.Box("grow", Ui.Text((g.Players > 1 ? g.Names[s.Player] + "   ·   " : "") + s.Feet + " ft   ·   " + PracticeSession.TeeName(s.Tee) + " tee", "g-name", Ui.Medium)), right));
            }
            top.Add(list.Margin(16));
            scroll.Add(top);
            // Rate this game: five stars, and a box to say why when it is under five (Feedback.GameCard).
            VisualElement rateGame = Feedback.GameCard(app, g.Title);
            if (rateGame != null) scroll.Add(Ui.Box("pad", rateGame));
            VisualElement buttons = Ui.Box("pad");
            if (again != null) buttons.Add(Ui.Button("Play again", () => { app.Pop(); again(); }));
            buttons.Add(Ui.Button("Exit", app.ExitToTab, again != null ? "btn btn-ghost" : "btn"));
            scroll.Add(buttons);
            Root = scroll;
        }
    }
}
