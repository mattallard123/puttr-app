// The app's rule games (puttr-app components/games/*.js): each is a set of rules over the mat's putts, played on the
// 3D ramp (FreePuttMode) with a panel under it, the way a tournament round is (RoundPanel).
//
//   RuleGame         one game's rules: who putts, from where, what a putt scores, when it is over
//   GameScreen (GameScreens.cs) sets these up: how it plays, how many Players (1 to 4 where the game allows), the game's own options, Start
//   RulePanel        the score bug(s), the tee sign, the call on the last putt, the game's board, the putt read
//   RuleFinalScreen  the winner or the score, the putts, Play again
//
// GameBridge.PlayRule runs one. The first Player's putts are saved to their account (RoundSaver, the game's
// gameType), and the first Player's final score ends the round; the other Players are guests on this account.
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
        public string Title, Sub;      // the call: "BULLSEYE", "+3"
        public AppPutt.Verdict V;
    }

    public abstract class RuleGame
    {
        public abstract string Title { get; }
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
        sealed class Seat { public object Profile; public string Typed = ""; public bool Puttr; public string Brand; }

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
            else _seats.Add(new Seat { Profile = app.Profile });
            _rows = Ui.Box("");
            _hint = Ui.Text("", "muted wrap", Ui.Regular);
            Root = Ui.Box("", Form.Label(putters ? "Putters" : "Players"), _rows, _hint.Margin(6));
            Draw();
            if (Profiles.Family == null) Profiles.Load(_ => Draw());
        }

        public int Count => _seats.Count;
        public bool Ready => _seats.Count >= _min;
        /// <summary>Who putts first: an index into Names.</summary>
        public int First => Mathf.Clamp(_first, 0, _seats.Count - 1);
        /// <summary>Puttr's seat, or -1.</summary>
        public int PuttrSeat => _seats.FindIndex(x => x.Puttr);
        static string PutterName(Dictionary<string, object> p) => (J.Str(p, "brand") + " " + J.Str(p, "model")).Trim();

        public string Need => _putters ? (_min - _seats.Count == 1 ? "Add another putter to compare." : "Add " + (_min - _seats.Count) + " putters to compare.")
            : _min - _seats.Count == 1 ? "Add a Player" + (_puttr ? " or Puttr" : "") + " to play." : "Add " + (_min - _seats.Count) + " more Players to play.";

        public List<string> Names => Enumerable.Range(0, _seats.Count).Select(i => _seats[i].Puttr ? "Puttr" : _seats[i].Profile != null ? Short(_seats[i].Profile) : _seats[i].Typed.Trim().Length > 0 ? _seats[i].Typed.Trim() : (_putters ? "Putter " : "Player ") + (i + 1)).ToList();

        // Everyone on this account who can be added: the other profiles not already playing.
        List<object> Choices()
        {
            var list = new List<object>();
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
                    who = Ui.Box("row grow", Ui.Box("avatar picker-puttr", mark), Ui.Box("grow", Ui.Text("Puttr", "feed-name", Ui.Bold), Ui.Text("Plays its own putts", "muted", Ui.Regular)));
                }
                else if (seat.Profile != null)
                    who = Ui.Box("row grow", Ui.Avatar(Short(seat.Profile), J.Str(seat.Profile, "photoURL")), Ui.Box("grow", Ui.Text(Short(seat.Profile), "feed-name", Ui.Bold), Ui.Text(i == 0 ? "You" : J.Str(seat.Profile, "homeTown"), "muted", Ui.Regular)));
                else
                {
                    var field = new TextField { value = seat.Typed };
                    field.textEdition.placeholder = _putters ? "Putter " + (i + 1) : "Player name";
                    field.textEdition.hidePlaceholderOnFocus = false;
                    field.AddToClassList("field");
                    field.AddToClassList("grow");
                    field.RegisterValueChangedCallback(e => seat.Typed = e.newValue);
                    who = field.Face(Ui.Medium);
                    if (_putters) who = Ui.Box("row grow", seat.Brand != null && PutterBrands.Has(seat.Brand) ? PutterBrands.Badge(seat.Brand, 28f) : Ui.Icon("Target", 22, Ui.Faint), FreePuttScreen.Space(8).Width(8), who);
                }
                bool first = _seats.Count > 1 && at == First;
                // Putters are played in the order listed: no "Go first".
                VisualElement order = _seats.Count > 1 && !_putters
                    ? Ui.Box(first ? "picker-first picker-first-on" : "picker-first", Ui.Text(first ? "1st" : "Go first", "picker-first-t", Ui.Bold)).OnTap(() => { _first = at; Draw(); })
                    : null;
                VisualElement row = Ui.Box("glass row picker-row", swatch, who, order);
                if (at > 0 || (_putters && _seats.Count > 1)) row.Add(Ui.Box("picker-remove", Ui.Text("\u2212", "picker-remove-t", Ui.Bold)).OnTap(() => { _seats.RemoveAt(at); if (_first >= _seats.Count || _first == at) _first = 0; else if (_first > at) _first--; Draw(); }));
                _rows.Add(row.Margin(8));
            }
            if (_seats.Count < _max) _rows.Add(Ui.Button(_putters ? "+  Add putter" : "+  Add player", _putters ? (Action)AddPutter : Add, "btn btn-ghost picker-add-btn").Margin(8));
            _hint.text = !Ready ? Need : _putters ? "You putt the same putts with each putter, one after another." : _seats.Count > 1 ? "Pass the putter between turns. Only your own putts are saved to your account." : "";
            _hint.style.color = Ready ? (Color)Ui.Faint : Ui.Amber;
            _hint.style.display = _hint.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Compare's Add putter sheet: pick the brand, then name the model in its row.
        void AddPutter()
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Text("Add a putter", "h2", Ui.Bold));
            ScrollView list = Ui.Scroll();
            list.style.maxHeight = 420;
            foreach (string b in PutterBrands.Names)
            {
                string brand = b;
                VisualElement badge = brand != "Other" && PutterBrands.Has(brand) ? PutterBrands.Badge(brand, 26f) : Ui.Icon("Target", 22, Ui.Bright);
                list.Add(Ui.Box("set-row", badge, FreePuttScreen.Space(10).Width(10), Ui.Text(brand, "feed-name grow", Ui.Bold)).OnTap(() =>
                {
                    dim.RemoveFromHierarchy();
                    _seats.Add(new Seat { Brand = brand == "Other" ? null : brand, Typed = brand == "Other" ? "" : brand + " " });
                    Draw();
                }));
            }
            card.Add(list);
            card.Add(Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        // The app's Select Player sheet: the profiles on this account, Puttr, or a guest who types a name.
        void Add()
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Text("Add a Player", "h2", Ui.Bold));
            void Pick(Seat seat) { dim.RemoveFromHierarchy(); _seats.Add(seat); Draw(); }
            foreach (object c in Choices())
            {
                object person = c;
                card.Add(Ui.Box("set-row", Ui.Avatar(Short(person), J.Str(person, "photoURL")), FreePuttScreen.Space(10).Width(10), Ui.Text(Short(person), "feed-name grow", Ui.Bold)).OnTap(() => Pick(new Seat { Profile = person })));
            }
            if (_puttr && PuttrSeat < 0)
            {
                var mark = new Image { image = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite"), tintColor = Ui.Bright, scaleMode = ScaleMode.ScaleToFit };
                mark.style.width = mark.style.height = 22;
                card.Add(Ui.Box("set-row", Ui.Box("avatar picker-puttr", mark), Ui.Box("grow", Ui.Text("Puttr", "feed-name", Ui.Bold), Ui.Text("Play against Puttr", "muted", Ui.Regular))).OnTap(() => Pick(new Seat { Puttr = true })));
            }
            card.Add(Ui.Box("set-row", Ui.Icon("UserPlus", 22, Ui.Bright), FreePuttScreen.Space(10).Width(10), Ui.Text("Guest", "feed-name grow", Ui.Bold)).OnTap(() => Pick(new Seat())));
            card.Add(Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
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
        readonly VisualElement _over, _sheet;
        readonly ScrollView _scroll;
        bool _wide, _laidOut;

        public RulePanel(App app, RuleGame game, Action leave)
        {
            _app = app; _game = game; _leave = leave;
            Root = Ui.Box("frame");
            Root.pickingMode = PickingMode.Ignore;
            Root.Face(Ui.Medium);
            _over = Ui.Box("frame-over");
            _over.pickingMode = PickingMode.Ignore;
            _sheet = Ui.Box("sheet");
            _scroll = Ui.Scroll();
            _sheet.Add(_scroll);
            Root.Add(_over);
            Root.Add(_sheet);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Root.schedule.Execute(Place).Every(300);
            Show();
        }

        void Place()
        {
            bool wide = Screen.width > Screen.height;
            if (_laidOut && wide == _wide) return;
            _laidOut = true; _wide = wide;
            Vector4 safe = _app.SafeInsets();
            float picture = wide ? PuttrArcade.FreePuttMode.WideShare : PuttrArcade.FreePuttMode.UprightShare;
            _sheet.EnableInClassList("sheet-wide", wide);
            _sheet.style.left = wide ? Length.Percent(picture * 100f) : 0;
            _sheet.style.top = wide ? 0 : Length.Percent(picture * 100f);
            _sheet.style.right = 0; _sheet.style.bottom = 0;
            _sheet.style.paddingBottom = safe.y;
            _sheet.style.paddingRight = wide ? safe.z : 0f;
            _sheet.style.paddingTop = wide ? Mathf.Max(safe.w, 64f) : 0f;
            _over.style.left = wide ? safe.x : 0;
            _over.style.top = Mathf.Max(safe.w, 0f) + (wide ? 44f : 52f);
            _over.style.width = wide ? Length.Percent(picture * 100f) : Length.Percent(100f);
            _over.style.height = wide ? Length.Percent(60f) : Length.Percent(picture * 100f - 8f);
            Show();
        }

        public void Show()
        {
            Perf.Wake(3f);
            RuleGame g = _game;
            _over.Clear();
            string photo = g.P == 0 ? J.Str(_app.Profile, "photoURL") : null;
            _over.Add(Ui.Box("frame-bug", Frame.ScoreBug(g.Names[g.P], photo, g.ScoreText(g.P), g.Over ? "FINAL" : g.Progress)));
            if (!g.Over) _over.Add(Frame.TeeSign(g.Feet, g.Tee));

            _scroll.Clear();
            Shot last = g.Shots.Count > 0 ? g.Shots[g.Shots.Count - 1] : null;
            string turn = g.Over ? (g.Players > 1 ? g.Outcome() : "Game over") : g.Players > 1 && g.Names[g.P] != "You" ? g.Names[g.P] + "'s putt" : "Your putt";
            _scroll.Add(Ui.Box("sheet-card row",
                Ui.Box("grow", Ui.Text(g.Title.ToUpperInvariant(), "kick", Ui.Bold), Ui.Text(turn, "feed-name", Ui.Bold).Margin(2)),
                Ui.Box("btn-small", Ui.Text(g.Over ? "Results" : "Leave", "btn-text", Ui.Bold)).OnTap(_leave)));
            if (Fixtures.On || Green.RealPutts == false) _scroll.Add(Ui.Text("Test putts: nothing is saved.", "sheet-note", Ui.Bold));

            if (last != null)
            {
                Label call = Ui.Text(last.Title, "rule-call", Ui.Bold);
                call.style.color = last.Good ? g.Accent : (Color)Ui.Red;
                VisualElement callCard = Ui.Box("sheet-card rule-callcard", call);
                if (!string.IsNullOrEmpty(last.Sub)) callCard.Add(Ui.Text(last.Sub, "sub wrap", Ui.Medium));
                _scroll.Add(callCard);
            }
            VisualElement board = g.Board();
            if (board != null) _scroll.Add(board);
            if (g.Players > 1)
            {
                VisualElement scores = Ui.Box("sheet-card");
                scores.Add(Ui.Text("PLAYERS", "kick", Ui.Bold));
                for (int i = 0; i < g.Players; i++)
                {
                    Label value = Ui.Text(g.ScoreText(i), "feed-score", Ui.Bold);
                    VisualElement row = Ui.Box("board-row", Ui.Box("rule-swatch"), Ui.Box("grow", Ui.Text(g.Names[i], "feed-name", Ui.Bold)), value);
                    row.Q(className: "rule-swatch").style.backgroundColor = PlayerColour(i);
                    if (i == g.P && !g.Over) row.AddToClassList("rule-now");
                    scores.Add(row);
                }
                _scroll.Add(scores);
            }
            if (last != null) _scroll.Add(Frame.PuttRead(last.V, last.Points, !(g is SensorGame), g.Words));   // a sensor game scores the stroke: its points are in the call
            else _scroll.Add(Ui.Box("sheet-card", Ui.Text("Putt when you are ready. After each putt its read appears here: line, pace and entry.", "sub", Ui.Regular)));
            _scroll.Add(FreePuttScreen.Space(16));
            _scroll.scrollOffset = Vector2.zero;
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
            scroll.Add(Ui.Header("", app.Pop));
            int made = g.Shots.Count(s => s.Player == 0 && s.V != null && s.V.Made), mine = g.Shots.Count(s => s.Player == 0);
            VisualElement top = Ui.Box("pad", Ui.Text("FINAL   ·   " + g.Title.ToUpperInvariant(), "kick center", Ui.Bold));
            if (g.Players == 1)
            {
                top.Add(Ui.Text(g.ScoreText(0), "score-big", Ui.Bold));
                top.Add(Ui.Text(g.Words.ToLowerInvariant() + "   ·   " + made + " of " + mine + " holed", "sub center", Ui.Regular));
            }
            else
            {
                top.Add(Ui.Text(g.Outcome(), "display center wrap", Ui.Bold));
                VisualElement table = Ui.Box("glass");
                foreach (int i in Enumerable.Range(0, g.Players).OrderBy(i => g.LowerIsBetter ? g.Score[i] : -g.Score[i]))
                {
                    VisualElement row = Ui.Box("board-row", Ui.Box("rule-swatch"), Ui.Box("grow", Ui.Text(g.Names[i], "feed-name", Ui.Bold)), Ui.Text(g.ScoreText(i), "feed-score", Ui.Bold));
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
                Label value = Ui.Text(g.FinalCall(s), "feed-score", Ui.Bold);
                value.style.color = s.Good ? g.Accent : (Color)Ui.Faint;
                VisualElement right = value;
                string points = g.FinalPoints(s);
                if (!string.IsNullOrEmpty(points))
                {
                    Label earned = Ui.Text(points, "sub", Ui.Medium);
                    earned.style.fontSize = 12;
                    earned.style.marginLeft = 10;
                    earned.style.unityTextAlign = TextAnchor.MiddleRight;
                    right = Ui.Box("", value, earned);
                    right.style.alignItems = Align.FlexEnd;
                }
                list.Add(Ui.Box("board-row", Ui.Text(n.ToString(), "board-place", Ui.Bold),
                    Ui.Box("grow", Ui.Text((g.Players > 1 ? g.Names[s.Player] + "   ·   " : "") + s.Feet + " ft   ·   " + PracticeSession.TeeName(s.Tee) + " tee", "feed-name", Ui.Medium)), right));
            }
            top.Add(list.Margin(16));
            scroll.Add(top);
            VisualElement buttons = Ui.Box("pad");
            if (again != null) buttons.Add(Ui.Button("Play again", () => { app.Pop(); again(); }));
            buttons.Add(Ui.Button("Exit", app.ExitToTab, again != null ? "btn btn-ghost" : "btn"));
            scroll.Add(buttons);
            Root = scroll;
        }
    }
}
