// Hosting (Event Mode, Venue Mode): the way in from Settings, the choice of mode, and the brand kit editor.
//   Settings > Host > Event Mode / Venue Mode Puttr Club only; a non-member gets the Club page. Sets the kit's hostMode
//   BrandKitScreen                            the kit, with the 3D stage dressed in it at the top
//   BrandGamesScreen                          which games the host offers, in what order, and the featured one
// The kit itself (loading, saving, the mirror) is BrandKit.cs; the worlds are dressed through PuttrArcade.Brand.
// Host mode itself (the attract screen, the host's game picker, the staff pad) is at the bottom of this file.
using System;
using System.Collections.Generic;
using System.Linq;
using PuttrArcade;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Host
    {
        /// <summary>Kid profiles never host. Everyone else sees the row; Club decides what it opens.</summary>
        public static bool Offered(object profile) => profile != null && !Kid.Managed(profile) && !Kid.IsKid(profile);

        /// <summary>A Settings row: a member goes straight to the brand kit in that mode; anyone else to the Club page.</summary>
        public static void Open(App app, string mode)
        {
            if (!Club.Member(app.Profile)) { app.Push(new ClubScreen(app, null)); return; }
            App.Run(Opening(app, mode));
        }

        static System.Collections.IEnumerator Opening(App app, string mode)
        {
            if (!BrandKit.Loaded) yield return BrandKit.Load();
            if (BrandKit.Str("hostMode") != mode) BrandKit.Set("hostMode", mode);
            app.Push(new BrandKitScreen(app));
        }

        /// <summary>Settings' Host section: Event Mode and Venue Mode, each with what it is for.</summary>
        public static VisualElement[] SettingsRows(App app)
        {
            VisualElement Row(string icon, string title, string words, string mode)
            {
                Label club = Ui.Text("CLUB", "chip chip-green", Ui.Bold);
                VisualElement row = Ui.Box("set-row", Ui.Icon(icon, 20, Ui.Faint),
                    Ui.Box("grow set-words", Ui.Text(title, "feed-name", Ui.Medium), Ui.Text(words, "muted wrap", Ui.Regular)),
                    Ui.Box("row set-right", club, Ui.Icon("CaretRight", 16, Ui.Faint)));
                return row.OnTap(() => Open(app, mode));
            }
            return new[]
            {
                Row("Megaphone", "Event Mode", "A one-off day: Players type a name and play, one leaderboard per event.", BrandKit.Event),
                Row("Trophy", "Venue Mode", "Every night: members scan in to play as themselves; tonight, week and all-time boards.", BrandKit.Venue),
            };
        }

        public static string ModeName(string mode) => mode == BrandKit.Venue ? "Venue" : "Event";
    }

    // ── The brand kit ────────────────────────────────────────────────────────

    public sealed class BrandKitScreen : AppScreen
    {
        static readonly string[] Swatches = { "#7AD446", "#5687FF", "#FFB300", "#FF5252", "#FF4FA3", "#3FD7FF", "#E2BD6B", "#B36BFF", "#FFFFFF" };

        readonly App _app;
        readonly VisualElement _body;
        Live3D _preview;
        bool _busy;

        public BrandKitScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Brand kit", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown()
        {
            BrandKit.Dressing = true;                 // the preview is the real stage, dressed
            Brand.Changed += Repaint;
            if (BrandKit.Loaded) Draw(); else App.Run(BrandKit.Load(Draw));
        }

        public override void Hidden()
        {
            Brand.Changed -= Repaint;
            // The Player's own games stay Puttr: the worlds are only dressed here and in host mode.
            if (!(_app.Front is BrandGamesScreen)) BrandKit.Dressing = false;
        }

        void Repaint() { if (_preview != null) _preview.Rebuild(BrandStage.ForMode); }

        void Draw()
        {
            _body.Clear();
            _preview = new Live3D(BrandStage.ForMode) { Name = "brand kit", Fps = 20 };
            _preview.style.height = 210;
            // The stage in a window: a bordered rectangle, nothing written over it.
            _body.Add(Ui.Box("pad", Ui.Box("window", _preview)));

            _body.Add(Ui.Section("Text"));
            _body.Add(Ui.Box("pad",
                Labelled("Title", Field("Seven Putt Challenge", "title", 40)),
                Labelled("Line under it", Field("Seven putts. One leaderboard.", "tagline", 80)),
                Labelled("Leaderboard title", Field("Leaderboard", "leaderboardTitle", 40))));

            _body.Add(Ui.Section("Logos"));
            _body.Add(Rows.Card(
                LogoRow("Brand logo", "Wide, see-through PNG works best", "logo", () => BrandKit.LogoUrl, url => BrandKit.Set("logoUri", url)),
                LogoRow("Co-host logo", "Shown beside yours on the attract screen", "cohostLogo", () => BrandKit.CohostLogoUrl, url => BrandKit.Set("cohostLogoUri", url))));

            _body.Add(Ui.Section("Colours"));
            _body.Add(Ui.Box("pad",
                ColourPicker("Primary", "headerColor", BrandKit.DefaultPrimary),
                ColourPicker("Secondary", "secondaryColor", BrandKit.DefaultSecondary).Margin(16)));

            _body.Add(Ui.Section("Partner logos"));
            var partnerRows = new List<VisualElement>();
            List<string> partners = BrandKit.PartnerLogos;
            for (int i = 0; i < partners.Count; i++)
            {
                int at = i;
                partnerRows.Add(Ui.Box("set-row", Thumb(partners[i]), Ui.Box("grow set-words", Ui.Text("Partner " + (i + 1), "feed-name", Ui.Medium)),
                    Ui.Box("host-hit", Ui.Icon("Trash", 20, Ui.Faint)).OnTap(() => { List<string> left = BrandKit.PartnerLogos; left.RemoveAt(at); BrandKit.SetPartners(left); Draw(); })));
            }
            if (partners.Count < Brand.MaxPartners)
                partnerRows.Add(Rows.Row("Plus", "Add a partner", () => PickLogo("partner", url => { List<string> all = BrandKit.PartnerLogos; all.Add(url); BrandKit.SetPartners(all); })));
            _body.Add(Rows.Card(partnerRows.ToArray()));
            _body.Add(Ui.Box("pad", Ui.Text("Up to " + Brand.MaxPartners + ". They take turns on one board in three. The last board in every row is always Puttr.", "muted wrap", Ui.Regular).Margin(8)));

            _body.Add(Ui.Section("QR"));
            _body.Add(Ui.Box("pad",
                Labelled("Link", Field("Your web leaderboard", "qrUrl", 200)),
                Labelled("Message beside it", Field("Follow the leaderboard", "qrText", 60))));

            _body.Add(Ui.Section("Games"));
            _body.Add(Rows.Card(Rows.Row("GameController", "Games and featured game", () => _app.Push(new BrandGamesScreen(_app)))));

            _body.Add(Ui.Section("Staff"));
            _body.Add(Ui.Box("pad", PinField(), Ui.Text("Four digits. Staff hold the logo and enter it to leave host mode, remove a score or start a new event.", "muted wrap", Ui.Regular)));

            _body.Add(Form.Bottom(
                Ui.Button("Start " + Host.ModeName(BrandKit.Mode) + " Mode", () => HostMode.Start(_app)),
                Ui.Text(BrandKit.LocalOnly ? "A kit from a file: nothing here is saved." : "Changes save to your profile as you make them.", "muted center", Ui.Regular).Margin(12)));
        }

        static VisualElement Labelled(string label, VisualElement field)
        {
            return Ui.Box("host-stack", Ui.Caps(label, "caps host-label"), field);
        }

        // A line of the kit: saved as it is typed (BrandKit.Set waits a moment before sending).
        static TextField Field(string placeholder, string key, int max)
        {
            var f = new TextField { value = BrandKit.Str(key), maxLength = max };
            f.textEdition.placeholder = placeholder;
            f.textEdition.hidePlaceholderOnFocus = false;
            f.AddToClassList("field");
            f.RegisterValueChangedCallback(e => BrandKit.Set(key, e.newValue.Trim().Length == 0 ? null : e.newValue));
            return f.Face(Ui.Medium);
        }

        VisualElement PinField()
        {
            var f = new TextField { value = "", maxLength = 4, isPasswordField = true };
            f.textEdition.placeholder = BrandKit.HasPin ? "PIN is set. Type four digits to change it" : "Staff PIN";
            f.textEdition.hidePlaceholderOnFocus = false;
            f.textEdition.keyboardType = TouchScreenKeyboardType.NumberPad;
            f.AddToClassList("field");
            f.RegisterValueChangedCallback(e =>
            {
                string digits = System.Text.RegularExpressions.Regex.Replace(e.newValue ?? "", "[^0-9]", "");
                if (digits != e.newValue) { f.SetValueWithoutNotify(digits); }
                if (digits.Length == 4) { BrandKit.SetPin(digits); _app.Toast("Staff PIN saved."); }
            });
            return f.Face(Ui.Medium);
        }

        static VisualElement Thumb(string url)
        {
            VisualElement box = Ui.Box("host-thumb");
            if (!string.IsNullOrEmpty(url))
            {
                var image = new Image { scaleMode = ScaleMode.ScaleToFit };
                image.style.flexGrow = 1; image.style.marginLeft = image.style.marginRight = 4; image.style.marginTop = image.style.marginBottom = 4;
                Images.Load(url, t => image.image = t);
                box.Add(image);
            }
            return box;
        }

        VisualElement LogoRow(string title, string hint, string slot, Func<string> current, Action<string> set)
        {
            string url = current();
            var right = new VisualElement();
            right.AddToClassList("row");
            if (!string.IsNullOrEmpty(url)) right.Add(Ui.Box("host-hit", Ui.Icon("Trash", 20, Ui.Faint)).OnTap(() => { set(null); Draw(); }));
            else right.Add(Ui.Text("Add", "sec-link"));
            VisualElement row = Ui.Box("set-row", Thumb(url), Ui.Box("grow set-words", Ui.Text(title, "feed-name", Ui.Medium), Ui.Text(hint, "muted", Ui.Regular)), right);
            row.OnTap(() => PickLogo(slot, set));
            return row;
        }

        void PickLogo(string slot, Action<string> set)
        {
            if (_busy) return;
            if (BrandKit.LocalOnly) { _app.Toast("A kit from a file cannot be changed."); return; }
            PhotoPicker.Pick(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _busy = true;
                _app.Toast("Uploading...");
                App.Run(BrandKit.Upload(slot, path, url =>
                {
                    _busy = false;
                    if (url == null) { _app.Toast("Could not upload that image. PNG, JPG or WebP up to 8 MB."); return; }
                    set(url);
                    Draw();
                }));
            });
        }

        // A row of ready colours and a hex field for the brand's own.
        VisualElement ColourPicker(string title, string key, string fallback)
        {
            string now = BrandKit.Str(key, fallback);
            var hex = new TextField { value = now, maxLength = 7 };
            hex.AddToClassList("field");
            hex.style.width = 120;
            hex.Face(Ui.Medium);
            var dots = new VisualElement();
            dots.AddToClassList("row");
            dots.style.flexWrap = Wrap.Wrap;
            var marks = new List<(VisualElement dot, string value)>();
            Action<string> show = value =>
            {
                foreach (var m in marks)
                {
                    m.dot.EnableInClassList("host-swatch-on", string.Equals(m.value, value, StringComparison.OrdinalIgnoreCase));
                }
            };
            foreach (string swatch in Swatches)
            {
                string value = swatch;
                // A swatch: a rounded square in the colour itself (the host's choice, not an interface colour).
                VisualElement dot = Ui.Box("host-swatch");
                dot.style.backgroundColor = Brand.Parse(value, Color.white);
                dot.OnTap(() => { hex.SetValueWithoutNotify(value); BrandKit.Set(key, value); show(value); });
                marks.Add((dot, value));
                dots.Add(dot);
            }
            show(now);
            hex.RegisterValueChangedCallback(e =>
            {
                string typed = (e.newValue ?? "").Trim();
                if (!typed.StartsWith("#", StringComparison.Ordinal)) typed = "#" + typed;
                if (typed.Length != 7 || Brand.Hex(Brand.Parse(typed, Color.clear)) != typed.ToUpperInvariant()) return;   // not a colour yet
                BrandKit.Set(key, typed.ToUpperInvariant());
                show(typed);
            });
            return Ui.Box("host-stack", Ui.Caps(title, "caps host-label"), dots, hex);
        }
    }

    // ── The host's games ─────────────────────────────────────────────────────

    public sealed class BrandGamesScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        List<object> _catalogue, _custom;

        public BrandGamesScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Games", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown()
        {
            // Back from making a custom game: the list is read again.
            int waiting = 2;
            Action arrived = () => { if (--waiting == 0) Draw(); };
            App.Run(Api.Kept("/games/ALL", d => { _catalogue = J.List(d); arrived(); }, _ => { _catalogue = _catalogue ?? new List<object>(); arrived(); }));
            App.Run(Api.Get("/creator/games/", d => { _custom = GameCards.MarkCustom(J.List(d)); arrived(); }, _ => { _custom = _custom ?? new List<object>(); arrived(); }));
        }

        public override void Hidden() { if (!(_app.Front is BrandKitScreen)) BrandKit.Dressing = false; }

        /// <summary>
        /// A catalogue game a host can offer: one this app plays, but not the Vertex sensor games (a guest has no
        /// sensor on their putter), Free putt (practice, not a game) or Compare (it compares putters, not Players).
        /// </summary>
        public static bool Hostable(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            if (J.Str(game, "gameCat").ToUpperInvariant() == "SENSOR" || System.Array.IndexOf(SensorCatalog.Types, type) >= 0) return false;
            if (type == FreePuttScreen.GameType || type == "FITTING") return false;
            return GameBridge.Offered(game);
        }

        /// <summary>The Puttr games a host can offer: every game this app plays except those above, in the host's order.</summary>
        public static List<object> Offered(Dictionary<string, object> kit, List<object> catalogue)
        {
            var playable = new List<object>();
            // The arcade games brought over from Puttr Arcade are not in the API's list on purpose (unpublished): the app adds them, as the Play tab does.
            var all = new List<object>(catalogue);
            foreach (object extra in ArcadeExtras.All) if (!all.Exists(g => J.Str(g, "gameType") == J.Str(extra, "gameType"))) all.Add(extra);
            foreach (object g in all) if (Hostable(g)) playable.Add(g);
            return BrandKit.Ordered(kit, playable);
        }

        // Puttr's games first, as their cards, every one on to start: most hosts leave them all on, or keep just a
        // few. A press on a card switches it. The host's custom games are a list under them, with their own All and
        // None. Any game, library or custom, can be the featured one (its Feature button).
        void Draw()
        {
            _body.Clear();
            string featured = BrandKit.FeaturedGameId();
            List<object> games = Offered(BrandKit.Kit, _catalogue);

            // What featuring does, and which game is featured (it may be far down the page).
            object star = games.Concat(_custom).FirstOrDefault(g => J.Str(g, "id") == featured);
            string about = star != null ? J.Str(star, "title", "Your featured game") + " is featured: it leads the attract screen and the game picker."
                : "Feature one game to lead the attract screen and the game picker: press Feature on its card or row.";
            _body.Add(Ui.Box("pad", Ui.Box("glass row", Ui.Icon("Trophy", 24, star != null ? Ui.Gold : Ui.Faint), Ui.Box("grow set-words", Ui.Text(about, "sub wrap", Ui.Medium)))).Margin(8));

            // The arcade games are on to start; the golf games are there for a golf crowd to switch on.
            var arcade = games.Where(BrandKit.IsArcade).ToList();
            var golf = games.Where(g => !BrandKit.IsArcade(g)).ToList();
            VisualElement order = Ui.Button(_reorder ? "Done" : "Reorder", () => { _reorder = !_reorder; Draw(); }, _reorder ? "btn" : "btn btn-ghost host-quick");
            if (_reorder) _body.Add(Ui.Box("pad", Ui.Text("Use the arrows to move a game. The room sees them in this order, the featured one first.", "sub wrap", Ui.Regular), order.Margin(12)).Margin(8));
            Group("Arcade games", "On to start: the games a party plays.", arcade, games, featured, _reorder ? null : order);
            Group("Golf games", "Off to start: switch them on for a golf crowd.", golf, games, featured, null);

            _body.Add(Ui.Section("Your custom games"));
            var rows = new List<VisualElement>();
            if (_custom.Count > 0)
            {
                int customOn = _custom.Count(g => BrandKit.GameOn(g));
                _body.Add(Ui.Box("pad", Ui.Text(customOn + " of " + _custom.Count + " on.", "sub wrap", Ui.Regular), AllOrNone(_custom, customOn).Margin(12)));
            }
            foreach (object g in _custom)
            {
                string id = J.Str(g, "id");
                string type = J.Str(g, "gameType").ToUpperInvariant();
                string kind = type == "CENTERCUT" ? "Center cut" : type == "STREAK" ? "Streak" : "Standard";
                rows.Add(GameRow(g, kind + " · " + J.Int(g, "putts") + " putts", BrandKit.GameOn(g), featured == id, value => BrandKit.SetGame(id, value)));
            }
            rows.Add(Rows.Row("Plus", "New custom game", () => CreatorScreen.NewGame(_app, Root)));
            _body.Add(Rows.Card(rows.ToArray()));
        }

        // All on / None for one group of games (the library, or the custom games): the others keep their switches.
        VisualElement AllOrNone(List<object> group, int on)
        {
            Action<bool> set = value => { BrandKit.SetGames(group.Select(g => J.Str(g, "id")), value); Draw(); };
            // Two plain secondary buttons: how many are on is the line above them, so neither is lit.
            VisualElement all = Ui.Button("All on", () => set(true), "btn btn-ghost host-quick");
            VisualElement none = Ui.Button("None", () => set(false), "btn btn-ghost host-quick");
            none.style.marginLeft = 8;
            all.style.flexGrow = none.style.flexGrow = 1;
            return Ui.Box("row", all, none);
        }

        // The Feature chip: "Feature" with a trophy outline, or "Featured" in white (the chip's on state). A press features this
        // game (and switches it on: a featured game is one the room can play), or a second press stops featuring it.
        VisualElement FeatureButton(object game, bool star)
        {
            VisualElement button = Ui.Box(star ? "host-feature host-feature-on" : "host-feature",
                Ui.Icon("Trophy", 16, star ? Color.black : Color.white),
                Ui.Text(star ? "FEATURED" : "FEATURE", "host-feature-t", Ui.Bold));
            button.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            button.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            button.OnTap(() =>
            {
                if (star) BrandKit.Set("featuredGameId", null);
                else
                {
                    string id = J.Str(game, "id");
                    BrandKit.Set("featuredGameId", double.TryParse(id, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out double n) ? (object)n : id);
                    if (!BrandKit.GameOn(game)) BrandKit.SetGame(id, true);
                }
                Draw();
            });
            return button;
        }

        // -app2Reorder: the Games page opens reordering (for checking it from a screenshot).
        bool _reorder = Array.IndexOf(Environment.GetCommandLineArgs(), "-app2Reorder") >= 0;

        // Reordering: the card with an arrow each side to move it one place earlier or later.
        VisualElement Moving(List<object> games, int at, List<object> all)
        {
            VisualElement card = GameCards.Make(games[at], "", false, false);
            card.style.opacity = BrandKit.GameOn(games[at]) ? 1f : 0.5f;
            VisualElement Arrow(int by)
            {
                bool can = at + by >= 0 && at + by < games.Count;
                Image caret = Ui.Icon("CaretRight", 24, can ? Color.white : (Color)new Color32(48, 53, 58, 255));
                if (by < 0) caret.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
                VisualElement b = Ui.Box("host-arrow", caret);
                b.style.position = Position.Absolute;
                b.style.top = Length.Percent(50);
                b.style.marginTop = -24;
                if (by < 0) b.style.left = 8; else b.style.right = 8;
                if (can) b.OnTap(() => Move(games, at, by, all));
                return b;
            }
            card.Add(Arrow(-1));
            card.Add(Arrow(1));
            return card;
        }

        // Swaps a game with its neighbour in its group, in the whole list's order (the other group keeps its places).
        void Move(List<object> games, int at, int by, List<object> all)
        {
            var ids = all.Select(g => J.Str(g, "id")).ToList();
            int a = ids.IndexOf(J.Str(games[at], "id")), b = ids.IndexOf(J.Str(games[at + by], "id"));
            if (a < 0 || b < 0) return;
            (ids[a], ids[b]) = (ids[b], ids[a]);
            BrandKit.SetOrder(ids);
            Draw();
        }

        // One group of library games: its heading, how many are on, All on and None (and Reorder, on the first),
        // and its cards.
        void Group(string title, string note, List<object> group, List<object> all, string featured, VisualElement order)
        {
            if (group.Count == 0) return;
            int on = group.Count(BrandKit.GameOn);
            _body.Add(Ui.Section(title));
            if (!_reorder)
            {
                VisualElement quick = AllOrNone(group, on);
                quick.style.flexGrow = 2;
                VisualElement row = Ui.Box("row", quick);
                if (order != null) { order.style.flexGrow = 1; order.style.marginLeft = 8; row.Add(order); }
                _body.Add(Ui.Box("pad", Ui.Text(on + " of " + group.Count + " on. " + note, "sub wrap", Ui.Regular), row.Margin(12)));
            }
            VisualElement grid = Ui.Box("gcard-list");
            grid.RegisterCallback<GeometryChangedEvent>(_ => HostPickerScreen.Tile(grid));
            for (int i = 0; i < group.Count; i++) grid.Add(_reorder ? Moving(group, i, all) : Card(group[i], featured));
            _body.Add(Ui.Box("pad", grid).Margin(12));
        }

        // A game's card: a green tick on its picture when it is on, dimmed with an empty box when it is off, and its
        // Feature chip on the bar under the name (nothing is written over the picture). A press anywhere else on the
        // card switches it.
        VisualElement Card(object game, string featured)
        {
            string id = J.Str(game, "id");
            bool on = BrandKit.GameOn(game), star = featured == id;
            VisualElement card = GameCards.Make(game, "", false, false, GameTags.Who(game), GameTags.Length(game));
            VisualElement tick = Ui.Box(on ? "host-tick host-tick-on" : "host-tick", Ui.Icon("Check", 16, on ? Color.white : Color.clear));
            tick.pickingMode = PickingMode.Ignore;
            card.Add(tick);
            VisualElement feature = FeatureButton(game, star);
            feature.AddToClassList("host-feature-card");
            (card.Q(className: "gtile-bar") ?? card).Add(feature);
            card.style.opacity = on ? 1f : 0.38f;
            card.OnTap(() => { BrandKit.SetGame(id, !BrandKit.GameOn(game)); Draw(); });
            return card;
        }

        VisualElement GameRow(object game, string sub, bool on, bool featured, Action<bool> flip)
        {
            string id = J.Str(game, "id");
            VisualElement words = Ui.Box("grow set-words", Ui.Text(J.Str(game, "title", "Game"), "feed-name", Ui.Medium));
            if (sub != null) words.Add(Ui.Text(sub, "muted", Ui.Regular));
            VisualElement star = FeatureButton(game, featured);
            star.style.marginRight = 12;
            // A press anywhere on the row switches the game on or off (the Feature button keeps its own).
            VisualElement knob = new VisualElement();
            knob.AddToClassList("switch-knob");
            VisualElement track = Ui.Box(on ? "switch switch-on" : "switch", knob);
            VisualElement row = Ui.Box("set-row", words, star, track);
            bool state = on;
            row.OnTap(() => { state = !state; track.EnableInClassList("switch-on", state); words.style.opacity = state ? 1f : 0.45f; flip(state); });
            words.style.opacity = on ? 1f : 0.45f;
            return row;
        }

    }

    // ── The stage in the kit editor ──────────────────────────────────────────

}

namespace PuttrApp
{
    // ── Host mode ────────────────────────────────────────────────────────────

    /// <summary>
    /// The device is running as a host's Puttr (Event Mode or Venue Mode): it starts on the attract screen, offers
    /// the kit's games to Players who type a name, posts scores to the host's own leaderboard, and goes back to the
    /// attract screen when nobody is there. Staff leave it with the PIN (StaffPad).
    /// </summary>
    public static class HostMode
    {
        const string OnKey = "puttr.host.on";
        public const float IdleSeconds = 90f;
        static bool _forced;
        static float _touched;

        /// <summary>-app2Host: host mode for this run, without the switch being kept (for checking the screens).</summary>
        public static void Force() => _forced = true;

        public static bool On => _forced || (PlayerPrefs.GetInt(OnKey, 0) == 1 && (Auth.SignedIn || Fixtures.On));

        /// <summary>
        /// The leaderboard the host's games post to. It is written into the kit the first time host mode starts and
        /// never changes after that, so renaming the profile later cannot move the board. It starts as the legacy
        /// app's Party board (the club a club admin runs, else the display name), so a host who moves over keeps
        /// their scores and their web leaderboard link.
        /// </summary>
        public static string ClubIdFor(object profile, Dictionary<string, object> kit)
        {
            string kept = J.Str(kit, "hostId").Trim();
            if (kept.Length > 0) return kept;
            string club = J.Str(profile, "isClubAdmin").Trim();
            if (club.Length == 0 && J.Get(profile, "isClubAdmin") is double n && n != 0) club = ((long)n).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (club.Length > 0 && club != "0" && club != "false") return club;
            string name = J.Str(profile, "displayName").Trim();
            return name.Length > 0 ? name : Puttr;
        }

        public const string Puttr = "PUTTR";

        /// <summary>
        /// The board the kit's games post to now. Venue Mode has one board. Event Mode has one for each event: the
        /// first event is the host's own id (so the legacy board and its web link carry on), and every event after
        /// it is the id, a tilde and the event's number ("KatieLABGolf~2"). The API reads the part before the tilde
        /// as the host (puttr-api hostBoard.js). Starting a new event only changes the number: the old event's
        /// scores stay where they are, readable at their own address.
        /// </summary>
        public static string BoardFor(object profile, Dictionary<string, object> kit)
        {
            string id = ClubIdFor(profile, kit);
            int no = EventNo(kit);
            return J.Str(kit, "hostMode") != BrandKit.Venue && no > 0 && id != Puttr ? id + "~" + no.ToString(System.Globalization.CultureInfo.InvariantCulture) : id;
        }

        public static int EventNo(Dictionary<string, object> kit) => Mathf.Max(0, J.Int(kit, "eventNo"));

        /// <summary>The club every play record and board read carries: the host's in host mode, PUTTR otherwise.</summary>
        public static string ClubId => On && App.I != null ? BoardFor(App.I.Profile, BrandKit.Kit) : Puttr;

        /// <summary>Event Mode: the next event starts with an empty board. The one that just ended keeps its scores.</summary>
        public static void NewEvent()
        {
            BrandKit.Set("eventNo", (double)(EventNo(BrandKit.Kit) + 1));
            Board.Forget();
        }

        /// <summary>The web page of the host's leaderboard, or the kit's own link when the host set one.</summary>
        public static string BoardLink => BrandKit.Str("qrUrl", "https://app.puttr.co/leaderboard/" + Uri.EscapeDataString(ClubId));

        // ── Who is playing (for the board's name) ──

        /// <summary>The names typed on the game page, in seat order, and whether the game's lowest score wins.</summary>
        public static List<string> Players = new List<string>();
        public static bool LowWins;
        static string _boardName;

        /// <summary>The seat whose score goes on the host's board: the best of the game (a board keeps one row a game).</summary>
        public static int Winner(IList<double> scores, bool lowWins)
        {
            int best = 0;
            for (int i = 1; i < scores.Count; i++) if (lowWins ? scores[i] < scores[best] : scores[i] > scores[best]) best = i;
            return best;
        }

        static string _boardMember;

        /// <summary>Called as a round ends in host mode, with the names (and who each seat saves as) as the game
        /// had them: remembers whose name the board row carries, and whether they are a signed-in member.</summary>
        public static void Scored(IList<string> names, int seat, IList<string> ids = null)
        {
            IList<string> from = names != null && names.Count > 0 ? names : Players;
            _boardName = from != null && seat >= 0 && seat < from.Count ? from[seat] : null;
            string id = ids != null && seat >= 0 && seat < ids.Count ? ids[seat] : null;
            _boardMember = !string.IsNullOrEmpty(id) && id != RoundSaver.Guest ? id : null;
        }

        /// <summary>
        /// The board row's name, as the API reads it (a JSON object with displayName). A typed name for a guest. A
        /// member who scanned in carries their own name, picture and user id: every row is posted by the host's
        /// account (so the host can clear their board), and the user id inside the name is what marks it a member's.
        /// </summary>
        public static string BoardName()
        {
            string name = !string.IsNullOrEmpty(_boardName) ? _boardName : Players.Count > 0 ? Players[0] : "Guest";
            var who = new Dictionary<string, object> { { "displayName", name.Trim().Length > 0 ? name.Trim() : "Guest" } };
            object member = _boardMember != null ? HostSession.Member(_boardMember) : null;
            if (member != null)
            {
                who["userId"] = _boardMember;
                string photo = J.Str(member, "photoURL");
                if (photo.Length > 0) who["photoURL"] = photo;
            }
            return J.Write(who);
        }

        /// <summary>
        /// Who a seat's putts and game are saved under in host mode: a member who scanned in under their own user
        /// id, everyone else under no account (Guest), Puttr under no one at all (null). Never the host, unless the
        /// host scanned in to play. ids are the game's seats; a game for one that carries none uses the game page's.
        /// </summary>
        public static string SaveAs(IList<string> ids, int seat)
        {
            IList<string> from = ids != null && ids.Count > 0 ? ids : SeatIds;
            if (from == null || seat < 0 || seat >= from.Count) return RoundSaver.Guest;
            string id = from[seat];
            if (id == null) return null;
            return id.Length == 0 ? RoundSaver.Guest : id;
        }

        /// <summary>Who each seat on the game page saves as, in step with Players.</summary>
        public static List<string> SeatIds = new List<string>();

        /// <summary>
        /// Where a venue's "Tonight" and "This week" boards start, as unix seconds: today at 6 in the morning (or
        /// yesterday's, before 6: a late night is still tonight), and Monday at 6. In the venue's own time.
        /// </summary>
        public static long Since(DateTime now, bool week)
        {
            DateTime start = now.Date.AddHours(6);
            if (now < start) start = start.AddDays(-1);
            if (week) start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
            return new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Local)).ToUnixTimeSeconds();
        }

        public static bool Venue => On && BrandKit.Mode == BrandKit.Venue;

        // ── On and off ──

        public static void Start(App app)
        {
            if (!Club.Member(app.Profile)) { app.Push(new ClubScreen(app, null)); return; }
            if (!BrandKit.HasPin) { app.Toast("Set a staff PIN first: it is the only way back out."); return; }
            // The board is fixed from the first start (see ClubIdFor).
            if (BrandKit.Str("hostId").Length == 0) BrandKit.Set("hostId", ClubIdFor(app.Profile, BrandKit.Kit));
            PlayerPrefs.SetInt(OnKey, 1);
            PlayerPrefs.Save();
            app.ShowHost(true);
        }

        public static void Stop(App app)
        {
            _forced = false;
            EndSession();
            PlayerPrefs.SetInt(OnKey, 0);
            PlayerPrefs.Save();
            BrandKit.Dressing = false;
            app.ShowTabs("home");
        }

        // ── Nobody there ──

        /// <summary>Everyone is signed out of this Puttr, and the code they scanned is retired.</summary>
        public static void EndSession()
        {
            bool had = HostSession.Members.Count > 0;
            HostSession.Clear();
            if (had || Venue) VenueJoin.Retire();
        }

        /// <summary>The QR to the host's web leaderboard (or the kit's own link): the code alone, on white.</summary>
        public static VisualElement Qr(float size)
        {
            var code = new Image { image = QrCode.Texture(BoardLink, 6), scaleMode = ScaleMode.ScaleToFit };
            code.style.width = code.style.height = size;
            code.style.backgroundColor = Color.white;
            code.style.borderTopLeftRadius = code.style.borderTopRightRadius = code.style.borderBottomLeftRadius = code.style.borderBottomRightRadius = 4;
            code.style.paddingLeft = code.style.paddingRight = code.style.paddingTop = code.style.paddingBottom = 4;
            return code;
        }

        /// <summary>Someone touched the screen or putted.</summary>
        public static void Touch() => _touched = Time.realtimeSinceStartup;
        /// <summary>A putt arrived with no game on: the attract screen steps aside.</summary>
        public static event Action Woken;
        public static void Putted() { Touch(); Woken?.Invoke(); }
        public static bool Idle => Time.realtimeSinceStartup - _touched > IdleSeconds;
    }

    // ── The staff pad ────────────────────────────────────────────────────────

    public static class StaffPad
    {
        /// <summary>True when the newest of these tap times (seconds) is the fifth inside three seconds: the legacy
        /// app's way out of Party mode, which hosts already know.</summary>
        public static bool FiveTaps(List<float> taps, float now)
        {
            taps.Add(now);
            taps.RemoveAll(t => now - t > 3f);
            if (taps.Count < 5) return false;
            taps.Clear();
            return true;
        }

        /// <summary>Tapping this five times in three seconds, or holding it for a second and a half, asks for the staff PIN.</summary>
        public static T Hold<T>(T element, App app) where T : VisualElement
        {
            IVisualElementScheduledItem timer = null;
            var taps = new List<float>();
            element.RegisterCallback<PointerDownEvent>(_ =>
            {
                timer?.Pause();
                if (FiveTaps(taps, Time.realtimeSinceStartup)) { Ask(app); return; }
                timer = element.schedule.Execute(() => Ask(app)).StartingIn(1500);
            });
            element.RegisterCallback<PointerUpEvent>(_ => timer?.Pause());
            element.RegisterCallback<PointerLeaveEvent>(_ => timer?.Pause());
            return element;
        }

        public static void Ask(App app)
        {
            VisualElement dim = null;
            var field = new TextField { value = "", maxLength = 4, isPasswordField = true };
            field.textEdition.placeholder = "Staff PIN";
            field.textEdition.hidePlaceholderOnFocus = false;
            field.textEdition.keyboardType = TouchScreenKeyboardType.NumberPad;
            field.AddToClassList("field");
            field.Face(Ui.Medium);
            Label wrong = Ui.Text("", "body-2", Ui.Regular);
            wrong.style.color = Ui.Red;
            field.RegisterValueChangedCallback(e =>
            {
                if ((e.newValue ?? "").Length < 4) { wrong.text = ""; return; }
                if (BrandKit.PinIs(e.newValue)) { dim.RemoveFromHierarchy(); Menu(app); }
                else { wrong.text = "That is not the PIN."; field.SetValueWithoutNotify(""); }
            });
            VisualElement card = Ui.Box("modal", Ui.Title("Staff", "title-20"), field.Margin(12), wrong, Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(app.Front != null ? app.Front.Root : card).Add(dim);
            field.schedule.Execute(() => field.Focus()).StartingIn(50);
            // The on-screen keyboard covers the bottom of the screen: the card rides above it (Matt, 2026-11-21: the PIN
            // field was hidden under it). As Chat lifts its composer: the keyboard's height less the safe area.
            dim.schedule.Execute(() => KeepAboveKeyboard(dim)).Every(100);
        }

        static void KeepAboveKeyboard(VisualElement dim)
        {
            if (dim.panel == null || Screen.width <= 0) return;
            float px = Application.isMobilePlatform && TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0f;
            float k = dim.panel.visualTree.layout.width / Screen.width;
            if (float.IsNaN(k) || k <= 0f) return;
            float want = 16f + Mathf.Max(0f, (px - Screen.safeArea.yMin) * k);
            if (Mathf.Abs(dim.resolvedStyle.paddingBottom - want) < 0.5f) return;
            dim.style.paddingBottom = want;
            Perf.Wake(0.6f);
        }

        static void Menu(App app)
        {
            VisualElement dim = null;
            Action close = () => dim.RemoveFromHierarchy();
            // No main button here: nothing on the staff pad is the usual next step. Clearing is the destructive one.
            VisualElement card = Ui.Box("modal", Ui.Title("Staff", "title-20"),
                Ui.Button("Clear the leaderboard", () =>
                {
                    close();
                    Dialog.Ask(app.Front.Root, "Clear the leaderboard?", "Every score on " + HostMode.ClubId + "'s leaderboard is removed. This cannot be undone.", "Clear", () =>
                        App.Run(Api.Post("/club/leaderboard/clear", new Dictionary<string, object> { { "clubId", HostMode.ClubId } }, _ => { Board.Forget(); app.Toast("Leaderboard cleared."); }, _ => app.Toast("Could not clear the leaderboard."))), true);
                }, "btn btn-red"),
                BrandKit.Mode == BrandKit.Event ? Ui.Button("Start a new event", () =>
                {
                    close();
                    Dialog.Ask(app.Front.Root, "Start a new event?", "The leaderboard starts empty. This event's scores are kept at their own web address.", "Start", () =>
                    {
                        HostMode.NewEvent();
                        app.Toast("New event started.");
                        app.ShowHost(true);
                    });
                }, "btn btn-ghost").Margin(12) : null,
                Ui.Button("Leave host mode", () => { close(); HostMode.Stop(app); }, "btn btn-ghost").Margin(12),
                Ui.Button("Cancel", close, "btn btn-ghost").Margin(12));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(close);
            App.Layer(app.Front != null ? app.Front.Root : card).Add(dim);
        }
    }

    // ── The host's home ──────────────────────────────────────────────────────

    /// <summary>
    /// The host's one screen: the arena full screen (BrandStage), the brand top left (tap it five times, or hold it,
    /// for the staff pad), the QR to the web leaderboard bottom right, and the games in a row along the bottom: tap
    /// one to set it up and play. There is no way back to the Player's app but the staff pad. It comes back after
    /// every game, and after 90 seconds with nobody there (when the people signed in are signed out).
    /// </summary>
    public sealed class HostPickerScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _brand, _session, _bottom, _games, _qr;
        ScrollView _row;
        List<object> _catalogue, _custom;
        Live3D _stage;

        public HostPickerScreen(App app)
        {
            _app = app;
            Root = new VisualElement();
            Root.style.flexGrow = 1;
            Root.style.backgroundColor = Ui.Base;

            _brand = Overlay();
            _brand.style.left = 24; _brand.style.top = 24;
            _session = Overlay();
            _session.style.right = 24; _session.style.top = 24;

            // The bottom: the row of games and the QR. The cards are solid tiles, so they read over the arena with no
            // shade behind them (docs/design-guidelines.md: no gradients).
            _bottom = Overlay();
            _bottom.style.left = 0; _bottom.style.right = 0; _bottom.style.bottom = 0;
            _bottom.style.paddingTop = 16; _bottom.style.paddingBottom = 16;
            _games = new VisualElement();
            _games.style.flexGrow = 1;
            _games.style.flexShrink = 1;
            _games.style.minWidth = 0;
            _qr = new VisualElement();
            _qr.style.flexShrink = 0;
            _bottom.Add(_games);
            _bottom.Add(_qr);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        static readonly float[] ArenaEye = ReadEye();
        static float[] ReadEye()
        {
            var args = new List<string>(Environment.GetCommandLineArgs());
            string extra = Environment.GetEnvironmentVariable("PUTTR_ARGS");
            if (!string.IsNullOrEmpty(extra)) args.AddRange(extra.Split(' '));
            int at = args.IndexOf("-app2ArenaEye");
            if (at < 0 || at + 1 >= args.Count) return null;
            string[] parts = args[at + 1].Split(',');
            if (parts.Length != 6) return null;
            var v = new float[6];
            for (int i = 0; i < 6; i++) if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return null;
            return v;
        }

        static VisualElement Overlay()
        {
            var e = new VisualElement();
            e.style.position = Position.Absolute;
            e.pickingMode = PickingMode.Position;
            return e;
        }

        public bool AtRest => HostSession.Empty && (_row == null || _row.scrollOffset.x < 1f);

        public override void Shown()
        {
            HostMode.Touch();
            App.Run(Ready());
        }

        // The profile (is this still a member?), the kit, then the games.
        System.Collections.IEnumerator Ready()
        {
            if (_app.Profile == null && !Fixtures.On)
            {
                bool failed = false;
                yield return Api.Get("/profile/", data => _app.Profile = J.First(data), _ => failed = true);
                if (failed && _app.Profile == null) { Draw(); Message("No connection. Host mode starts again when the Puttr is online."); yield break; }
            }
            if (!BrandKit.Loaded) yield return BrandKit.Load();
            // Club lapsed: back to the Player's own app. The kit is kept for when they renew.
            if (_app.Profile != null && !Club.Member(_app.Profile) && !Fixtures.On)
            {
                _app.Toast("Host mode needs Puttr Club.");
                HostMode.Stop(_app);
                yield break;
            }
            BrandKit.Dressing = true;
            Draw();
            int waiting = 2;
            Action arrived = () => { if (--waiting == 0) Cards(); };
            App.Run(Api.Kept("/games/ALL", d => { _catalogue = J.List(d); arrived(); }, _ => { _catalogue = _catalogue ?? new List<object>(); arrived(); }));
            App.Run(Api.Get("/creator/games/", d => { _custom = GameCards.MarkCustom(J.List(d)); arrived(); }, _ => { _custom = _custom ?? new List<object>(); arrived(); }));
        }

        void Draw()
        {
            Root.Clear();
            // The arena, behind everything: a slow broadcast move behind the mat (narrow screens stand further back).
            _stage = new Live3D(s =>
            {
                BrandStage.ForMode(s);
                Func<float, bool> life = s.Tick;
                s.Tick = dt =>
                {
                    life?.Invoke(dt);
                    // Upright (a phone) the picture is narrow: the same place behind the mat, a little higher, and a
                    // wider lens, so the Puttr stays the size it is and the crowd and scoreboard still fit above it.
                    // -app2ArenaEye x,y,z,tx,ty,tz: the camera held there (for checking a part of the arena from a screenshot).
                    if (ArenaEye != null) { s.Look(new Vector3(ArenaEye[0], ArenaEye[1], ArenaEye[2]), new Vector3(ArenaEye[3], ArenaEye[4], ArenaEye[5])); return true; }
                    bool upright = s.Camera.aspect < 1f;
                    s.Camera.fieldOfView = upright ? 64f : 48f;
                    // Venue Mode, landscape: the lounge's camera sways slowly from the Puttr across the room to the bar
                    // and back (BrandStage.LoungePan).
                    if (!upright && BrandKit.Mode == BrandKit.Venue) { BrandStage.LoungePan(s.Time, out Vector3 at, out Vector3 to); s.Look(at, to); return true; }
                    float drift = Mathf.Sin(s.Time * 0.07f);
                    Vector3 eye = BrandStage.EyeForMode + new Vector3(drift * (upright ? 18f : 46f), Mathf.Sin(s.Time * 0.05f) * 6f + (upright ? 26f : 0f), upright ? -20f : 0f);
                    s.Look(eye, BrandStage.TargetForMode + new Vector3(drift * 12f, upright ? -14f : 0f, 0));
                    return true;
                };
            }) { Name = "arena", Fps = 30, MaxScale = 1.5f };
            _stage.style.position = Position.Absolute;
            _stage.style.left = _stage.style.right = _stage.style.top = _stage.style.bottom = 0;
            Root.Add(_stage);

            // The brand top left: the host's logo (or Puttr's), the title and the line under it.
            _brand.Clear();
            VisualElement logo = HostBrand.Lockup(58f);
            logo.style.alignSelf = Align.FlexStart;
            logo.style.width = 260;
            logo.style.marginTop = 0;
            _brand.Add(StaffPad.Hold(logo, _app));
            string title = BrandKit.Title, line = BrandKit.Tagline;
            // The title and the line under it on a solid plate, so they read over the arena without a text shadow.
            if (title.Length > 0 || line.Length > 0)
            {
                VisualElement plate = Ui.Box("host-plate");
                if (title.Length > 0) plate.Add(Ui.Title(title, "title"));
                if (line.Length > 0) plate.Add(Ui.Text(line, "sub wrap", Ui.Medium).Margin(title.Length > 0 ? 4 : 0));
                _brand.Add(plate);
            }
            _brand.style.maxWidth = 520;
            Root.Add(_brand);

            // Venue Mode: who is signed in, and the way to sign them all out.
            _session.Clear();
            if (!HostSession.Empty)
            {
                VisualElement end = Ui.Text("End session", "sec-link host-link", Ui.Bold).OnTap(() => { HostMode.EndSession(); _app.Toast("Everyone is signed out."); Draw(); Cards(); });
                _session.Add(Ui.Box("glass row", Ui.Text("Playing: " + HostSession.Names(), "body", Ui.Medium), end));
            }
            Root.Add(_session);

            // The QR to the web leaderboard is on each game's setup page (GameScreen), over its leaderboard.
            _qr.Clear();
            Root.Add(_bottom);
            if (_catalogue != null) Cards();
            Layout();
        }

        void Message(string words)
        {
            _games.Clear();
            _games.Add(Ui.Box("host-plate", Ui.Text(words, "sub wrap", Ui.Regular)));
        }

        /// <summary>The games a host offers: the featured one first, then the host's custom games, then every Puttr
        /// game this app plays (no sensor games), less any the host switched off.</summary>
        public static List<object> Games(Dictionary<string, object> kit, List<object> catalogue, List<object> custom)
        {
            var games = new List<object>();
            foreach (object g in custom) if (BrandKit.GameOn(kit, g) && GameBridge.Playable(g)) games.Add(g);
            foreach (object g in BrandGamesScreen.Offered(kit, catalogue)) if (BrandKit.GameOn(kit, g)) games.Add(g);
            string featured = BrandKit.FeaturedGameId(kit);
            int at = games.FindIndex(g => J.Str(g, "id") == featured);
            if (at > 0) { object first = games[at]; games.RemoveAt(at); games.Insert(0, first); }
            return games;
        }

        // The row of games along the bottom: tap one to set it up (who is playing) and play.
        void Cards()
        {
            _games.Clear();
            List<object> games = Games(BrandKit.Kit, _catalogue ?? new List<object>(), _custom ?? new List<object>());
            if (games.Count == 0) { Message("No games are switched on. Staff: open the brand kit's Games page."); return; }
            _row = Ui.Scroll(true);
            _row.style.flexGrow = 1;
            string featured = BrandKit.FeaturedGameId();
            bool small = Root.resolvedStyle.width < 700f;
            foreach (object g in games)
            {
                object game = g;
                bool star = J.Str(game, "id") == featured;
                VisualElement card = GameCards.Make(game, star ? "Featured" : "", star, false, GameTags.Who(game), GameTags.Length(game));
                // As tall as the tile is (its picture, then its bar): a fixed height cut the bar off.
                card.style.width = small ? 168 : 214;
                card.style.flexShrink = 0;
                card.style.marginLeft = 0;
                card.style.marginRight = 12;
                _row.Add(card.OnTap(() => _app.Push(new GameScreen(_app, game))));
            }
            _games.Add(_row);
        }

        /// <summary>The cards tile across a wide screen (three, or two), one to a row on a phone: the Play tab's rule.</summary>
        public static void Tile(VisualElement list)
        {
            float w = list.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) return;
            int cols = w >= 840 ? 3 : w >= 560 ? 2 : 1;
            float gap = 12f, each = (w - gap * (cols - 1)) / cols;
            int at = 0;
            foreach (VisualElement card in list.Children())
            {
                if (!card.ClassListContains("gcard")) continue;
                card.style.width = cols == 1 ? Length.Percent(100) : new StyleLength(each);
                card.style.flexGrow = cols == 1 ? 1 : 0;
                card.style.marginLeft = cols == 1 || at % cols == 0 ? 0 : gap;
                card.style.marginBottom = gap;
                at++;
            }
        }

        // The games run the whole width of the screen; the QR sits under them, in the bottom right corner.
        void Layout()
        {
            float w = Root.resolvedStyle.width, h = Root.resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0) return;
            _bottom.style.flexDirection = FlexDirection.Column;
            _bottom.style.alignItems = Align.Stretch;
            _bottom.style.paddingLeft = 16;
            _bottom.style.paddingRight = 16;
            _bottom.style.paddingBottom = 16;
            _qr.style.marginLeft = 0;
            _qr.style.marginTop = 12;
            _qr.style.alignSelf = Align.FlexEnd;
            _brand.style.maxWidth = w > h && w >= 700f ? 520 : w - 48;
        }

        /// <summary>Back to the start: the row scrolled home (after nobody has been there for a while).</summary>
        public void Rest() { if (_row != null) _row.scrollOffset = Vector2.zero; }
    }

    /// <summary>The brand as the host's screens show it: the host's logo, or the Puttr lockup with no logo in the kit.</summary>
    public static class HostBrand
    {
        public static VisualElement Lockup(float height)
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit };
            image.style.height = height;
            image.style.marginTop = 10; image.style.marginBottom = 6;
            image.style.alignSelf = Align.Center;
            image.style.width = Length.Percent(70);
            string url = BrandKit.LogoUrl;
            if (url.Length > 0) Images.Load(url, t => image.image = t != null ? t : Brand.Puttr);
            else image.image = Brand.Puttr;
            return image;
        }
    }
}

namespace PuttrApp
{
    // ── Venue Mode: who is here ──────────────────────────────────────────────

    /// <summary>
    /// The people at the Puttr now: members who scanned in (they play under their own name, and their putts and
    /// games go to their own profile) and the names typed for everyone else. It lasts from game to game, so a group
    /// signs in once; it ends when they tap End session or the attract screen comes back.
    /// </summary>
    public static class HostSession
    {
        public static readonly List<object> Members = new List<object>();
        public static List<string> Guests = new List<string>();

        public static bool Empty => Members.Count == 0 && Guests.Count == 0;

        public static bool Has(string userId) => Members.Exists(m => J.Str(m, "userId") == userId);
        public static object Member(string userId) => Members.Find(m => J.Str(m, "userId") == userId);

        /// <summary>A member scanned in. False if they are already here.</summary>
        public static bool Join(object profile)
        {
            string id = J.Str(profile, "userId");
            if (id.Length == 0 || Has(id)) return false;
            Members.Add(profile);
            return true;
        }

        public static void Leave(string userId) => Members.RemoveAll(m => J.Str(m, "userId") == userId);

        public static void Clear() { Members.Clear(); Guests = new List<string>(); }

        /// <summary>"Renee, A. Okafor and Big Tom", for the host's home.</summary>
        public static string Names()
        {
            var names = new List<string>();
            foreach (object m in Members) names.Add(PlayerPicker.Short(m));
            foreach (string g in Guests) if (g.Trim().Length > 0) names.Add(g.Trim());
            if (names.Count == 0) return "";
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
        }
    }

    /// <summary>
    /// The QR a member scans to take a seat (Venue Mode). It is the coach dashboard's studio check-in, reused: the
    /// Puttr shows a standing code, the member's own app confirms it, and this device learns their name and user id
    /// and nothing else (puttr-api coachCheckin.js, the /host/checkin routes).
    /// </summary>
    public static class VenueJoin
    {
        const string DeviceKey = "puttr.host.device";

        /// <summary>This Puttr, so two bays on one host account each keep their own code.</summary>
        public static string Device
        {
            get
            {
                string id = PlayerPrefs.GetString(DeviceKey, "");
                if (id.Length > 0) return id;
                id = Guid.NewGuid().ToString("N").Substring(0, 12);
                PlayerPrefs.SetString(DeviceKey, id);
                PlayerPrefs.Save();
                return id;
            }
        }

        /// <summary>The code and the words beside it. joined is called with each member as they scan in.</summary>
        public static VisualElement Block(Action<object> joined)
        {
            var qr = new Image { scaleMode = ScaleMode.ScaleToFit };
            qr.style.width = qr.style.height = 92;
            qr.style.marginRight = 12;
            qr.style.backgroundColor = Color.white;
            Label words = Ui.Text("Scan with the Puttr app to play as you", "body", Ui.Bold);
            Label sub = Ui.Text("Your putts and games save to your own profile.", "body-2", Ui.Regular);
            VisualElement block = Ui.Box("glass row", qr, Ui.Box("grow", words, sub.Margin(4)));
            string shown = null;
            bool asking = false;
            Action<object> show = session =>
            {
                string link = J.Str(session, "link");
                if (link.Length > 0 && link != shown) { shown = link; qr.image = QrCode.Texture(link, 6); }
            };
            Action poll = () =>
            {
                if (asking || block.panel == null) return;
                if (Fixtures.On) { show(new Dictionary<string, object> { { "link", "https://api.puttr.co/w/checkin/sample" } }); return; }
                asking = true;
                App.Run(Api.Get("/host/checkin/current?deviceId=" + Device, session =>
                {
                    asking = false;
                    show(session);
                    object player = J.Get(session, "player");
                    if (player == null || J.Str(player, "userId").Length == 0) return;
                    HostMode.Touch();
                    joined(player);
                    // The seat is taken: a fresh code for the next member.
                    App.Run(Api.Post("/host/checkin/clear", new Dictionary<string, object> { { "deviceId", Device } }, show, _ => { }));
                }, _ => asking = false));
            };
            block.RegisterCallback<AttachToPanelEvent>(_ => poll());
            block.schedule.Execute(poll).Every(2500);
            return block;
        }

        /// <summary>The session ended: the standing code is retired, so a photo of it cannot seat anyone later.</summary>
        public static void Retire()
        {
            if (Fixtures.On || !Auth.SignedIn) return;
            App.Run(Api.Post("/host/checkin/clear", new Dictionary<string, object> { { "deviceId", Device } }, _ => { }, _ => { }));
        }
    }
}
