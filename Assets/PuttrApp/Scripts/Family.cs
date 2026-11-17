// Profiles (puttr-app components/family, utils/familySwitch.js): up to five players on one account,
// each with their own stats, on one membership. The UI says "Profiles" and "players", never "Family"
// (the code and API keep the family names). Kid profiles play every game but have video, online play,
// the social side, leagues and chat switched off, and show a public alias instead of their name.
//
//   GET  /family/                    {ownerId, isOwner, pinSet, maxPlayers, players[]}
//   POST /family/create              {firstName, handedness Right|Left, isKid}
//   POST /family/update/:uid         {firstName | handedness | animal+color | newAlias | makeStandard}
//   POST /family/remove/:uid
//   POST /family/token/:uid {pin?}   {token}: then Firebase signInWithCustomToken. Nothing is kept.
//   POST /family/pin {pin | pin:null, current}
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>KidGate.js: what a Kid profile (or any managed player) cannot open, and what it says instead.</summary>
    public static class Kid
    {
        public const string Video = "video", Online = "online", Social = "social", Party = "party", Chat = "chat", League = "league", Lessons = "lessons", Owner = "owner";

        public static bool IsKid(object profile) => J.Flag(profile, "isKid");
        public static bool Managed(object profile) => J.Str(profile, "managedBy").Length > 0;

        static (string title, string body) Copy(string kind, string owner)
        {
            switch (kind)
            {
                case Video: return ("Video is off for Kid profiles", "Putting Lab, replays and stroke film use the camera. You can still play every game.");
                case Online: return ("Online play is off for Kid profiles", "You can play everyone on this account and climb the leaderboards.");
                case Social: return ("This is off for Kid profiles", "Friends, messages and the community feed are for standard profiles.");
                case Party: return ("Hosting is off for Kid profiles", "Parties and events are hosted from a standard profile. You can still play every game.");
                case Chat: return ("Chat is off for Kid profiles", "You can still play every game and see your stats.");
                case League: return ("Leagues are off for Kid profiles", "You can still play every game and climb the leaderboards.");
                case Lessons: return ("Lessons are off for Kid profiles", "Ask " + owner + " about lessons.");
                default: return ("Ask " + owner, "Purchases, lessons and account changes are handled from " + owner + "'s profile.");
            }
        }

        /// <summary>True (and the screen's body replaced by the notice) when this profile cannot open it.</summary>
        public static bool Blocked(App app, string kind, VisualElement body)
        {
            object p = app.Profile;
            bool blocked = kind == Owner ? Managed(p) : IsKid(p);
            if (!blocked) return false;
            string owner = J.Str(p, "familyOwnerName", "the account owner");
            (string title, string words) = Copy(kind, owner);
            body.Clear();
            body.Add(Ui.Box("pad", Ui.Box("glass center-box", Ui.Bubble("FillUser", Ui.Amber), Ui.Text(title, "h2 center", Ui.Bold).Margin(10), Ui.Text(words, "sub center", Ui.Regular).Margin(6),
                Ui.Button("Got it", app.Pop, "btn btn-ghost").Margin(12))));
            return true;
        }
    }

    public static class Profiles
    {
        // constants/CrewArt.js: the Crew, in the API's order, and their six colours (1 to 6).
        public static readonly string[] Animals = { "bluebird", "eagle", "tiger", "gopher", "bear", "fox", "panda", "otter", "frog", "owl", "penguin", "rabbit" };
        public static readonly Color[] Colours = { new Color32(0x17, 0xB3, 0xA3, 0xFF), new Color32(0x56, 0x87, 0xFF, 0xFF), new Color32(0xC2, 0x64, 0xE8, 0xFF), new Color32(0xF5, 0x9E, 0x0B, 0xFF), new Color32(0xFF, 0x52, 0x52, 0xFF), new Color32(0x2D, 0x8C, 0x3C, 0xFF) };
        public static string AvatarUrl(string animal, int colour) => "https://storage.googleapis.com/images.puttr.co/avatars/" + animal + "-" + colour + ".png";

        public static object Family;

        public static void Load(Action<object> done) => App.Run(Api.Get("/family/", data => { Family = data; done?.Invoke(data); }, _ => done?.Invoke(null)));

        /// <summary>Shown when there is anyone to switch to: a managed player, or an owner with players.</summary>
        public static bool HasSwitch(object profile) => Kid.Managed(profile) || J.Int(profile, "familyCount") > 0;

        public static VisualElement Face(object player, float size)
        {
            string name = J.Str(player, "name", J.Str(player, "displayName", "?"));
            VisualElement a = Ui.Avatar(name, J.Str(player, "photoURL"), size >= 56);
            // A Kid's photo lives on this phone only (family-photos/<userId>.jpg).
            string local = PhotoPicker.KidPhotoPath(J.Str(player, "userId"));
            if (J.Flag(player, "isKid") && System.IO.File.Exists(local))
            {
                var t = new Texture2D(2, 2);
                if (t.LoadImage(System.IO.File.ReadAllBytes(local))) { a.style.backgroundImage = new StyleBackground(t); if (a.childCount > 0) a[0].style.display = DisplayStyle.None; }
            }
            a.style.width = a.style.height = size;
            a.style.borderTopLeftRadius = a.style.borderTopRightRadius = a.style.borderBottomLeftRadius = a.style.borderBottomRightRadius = size / 2;
            return a;
        }

        /// <summary>switchPlayer: a one-time token for the other profile, then signed in as them and the app starts again.</summary>
        public static System.Collections.IEnumerator Switch(App app, object player, string pin, Action<string> failed)
        {
            string token = null, why = null;
            var body = pin != null ? new Dictionary<string, object> { { "pin", pin } } : new Dictionary<string, object>();
            yield return Api.Post("/family/token/" + J.Str(player, "userId"), body, data => token = J.Str(data, "token"), w => why = w);
            if (string.IsNullOrEmpty(token)) { failed(why == "http_401" ? "pin_required" : "Couldn't switch. Check your connection and try again."); yield break; }
            string problem = null;
            yield return Auth.SignInWithCustomToken(token, p => problem = p);
            if (problem != null) { failed("Couldn't switch. Check your connection and try again."); yield break; }
            app.SwitchedPlayer();
            app.Toast("Playing as " + J.Str(player, "name", "them") + ".");
        }

        /// <summary>The Home greeting's switch: who is putting.</summary>
        public static VisualElement Switcher(App app)
        {
            VisualElement chip = Ui.Box("player-switch", Face(app.Profile, 32), Ui.Text("Switch", "muted", Ui.Medium));
            return chip.OnTap(() => Sheet(app, chip));
        }

        /// <summary>SwitchPlayerSheet: every player, the current one ticked; the owner can add and manage.</summary>
        public static void Sheet(App app, VisualElement over)
        {
            VisualElement dim = null, grid = Ui.Box("player-grid");
            VisualElement card = Ui.Box("modal", Ui.Text("Who's putting?", "h2 center", Ui.Bold), grid);
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
            grid.Add(Spinner.Small());
            Load(family =>
            {
                grid.Clear();
                if (family == null) { grid.Add(Ui.Text("Couldn't load profiles.", "sub", Ui.Regular)); return; }
                bool owner = J.Flag(family, "isOwner");
                List<object> players = J.List(J.Get(family, "players"));
                foreach (object p in players)
                {
                    object player = p;
                    bool me = J.Flag(p, "isMe");
                    VisualElement tile = Ui.Box(me ? "player-tile player-me" : "player-tile", Face(p, 58), Ui.Text(J.Str(p, "name"), "feed-name center", Ui.Bold),
                        Ui.Text(J.Flag(p, "isOwner") ? "OWNER" : J.Flag(p, "isKid") ? "KID" : "", "kick center", Ui.Bold));
                    if (!me) tile.OnTap(() =>
                    {
                        // The owner's profile may need the owner's PIN.
                        if (J.Flag(player, "isOwner") && J.Flag(family, "pinSet")) { dim.RemoveFromHierarchy(); Pin(app, over, "Enter " + J.Str(player, "name") + "'s PIN", pin => App.Run(Switch(app, player, pin, w => app.Toast(w == "pin_required" ? "That PIN is not right." : w)))); return; }
                        grid.Clear();
                        grid.Add(Ui.Text("Switching to " + J.Str(player, "name") + "...", "sub center", Ui.Medium));
                        App.Run(Switch(app, player, null, w => { dim.RemoveFromHierarchy(); app.Toast(w); }));
                    });
                    grid.Add(tile);
                }
                if (owner && players.Count < J.Int(family, "maxPlayers", 5))
                    grid.Add(Ui.Box("player-tile", Ui.Box("player-add", Ui.Text("+", "h1 center", Ui.Bold)), Ui.Text("Add", "feed-name center", Ui.Bold)).OnTap(() => { dim.RemoveFromHierarchy(); app.Push(new AddPlayerScreen(app)); }));
                if (owner) card.Add(Ui.Button(players.Count > 1 ? "Manage profiles" : "Add profiles", () => { dim.RemoveFromHierarchy(); app.Push(new ProfilesScreen(app)); }, "btn btn-ghost").Margin(10));
            });
        }

        /// <summary>A four-digit PIN pad.</summary>
        public static void Pin(App app, VisualElement over, string title, Action<string> entered)
        {
            VisualElement dim = null;
            string pin = "";
            Label dots = Ui.Text("", "h1 center", Ui.Bold);
            VisualElement pad = Ui.Box("pin-pad");
            void Show() => dots.text = new string('•', pin.Length) + new string('-', 4 - pin.Length);
            foreach (string k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "", "0", "<" })
            {
                string key = k;
                VisualElement b = Ui.Box(k.Length == 0 ? "pin-key pin-blank" : "pin-key", Ui.Text(k == "<" ? "Del" : k, "btn-text", Ui.Bold));
                if (k.Length > 0) b.OnTap(() =>
                {
                    if (key == "<") { if (pin.Length > 0) pin = pin.Substring(0, pin.Length - 1); }
                    else if (pin.Length < 4) pin += key;
                    Show();
                    if (pin.Length == 4) { dim.RemoveFromHierarchy(); entered(pin); }
                });
                pad.Add(b);
            }
            Show();
            VisualElement card = Ui.Box("modal", Ui.Text(title, "h2 center", Ui.Bold), dots.Margin(10), pad, Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(over).Add(dim);
        }
    }

    /// <summary>FamilyScreen: every player, a leaderboard of the account, and the owner's PIN.</summary>
    public sealed class ProfilesScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;

        public ProfilesScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Profiles", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => Profiles.Load(Draw);

        void Draw(object family)
        {
            _body.Clear();
            if (family == null) { _body.Add(Page.Note("Couldn't load profiles. Check your connection and try again.")); return; }
            bool owner = J.Flag(family, "isOwner");
            List<object> players = J.List(J.Get(family, "players"));
            int max = J.Int(family, "maxPlayers", 5);
            string ownerName = J.Str(players.Find(p => J.Flag(p, "isOwner")), "name", "the account owner");
            _body.Add(Ui.Box("pad", Ui.Text(owner ? players.Count + " of " + max + " players. Everyone here plays with the same membership as you and keeps their own stats."
                : "This profile is on " + ownerName + "'s account.", "sub", Ui.Regular)));
            VisualElement list = Ui.Box("glass set-card");
            foreach (object p in players)
            {
                object player = p;
                string sub = J.Flag(p, "isOwner") ? "Account owner" : J.Flag(p, "isKid") ? "Kid profile   ·   " + J.Str(p, "displayName") : "Player";
                VisualElement right = J.Flag(p, "isMe") ? Ui.Text("You", "chip chip-green", Ui.Bold) : J.Flag(p, "isKid") ? Ui.Text("Kid", "chip chip-amber", Ui.Bold) : null;
                VisualElement row = Ui.Box("note-row", Profiles.Face(p, 42), Ui.Box("grow set-words", Ui.Text(J.Str(p, "name"), "feed-name", Ui.Bold), Ui.Text(sub, "muted", Ui.Regular)), right);
                if (owner && !J.Flag(p, "isOwner")) row.OnTap(() => _app.Push(new PlayerScreen(_app, player)));
                list.Add(row);
            }
            _body.Add(Ui.Box("pad", list));
            if (owner)
            {
                VisualElement add = Ui.Button(players.Count >= max ? "You have all " + max + " players" : "Add player", () => _app.Push(new AddPlayerScreen(_app)));
                add.SetEnabled(players.Count < max);
                _body.Add(Ui.Box("pad", add).Margin(10));
            }
            if (players.Count > 1)
            {
                var ranked = new List<object>(players);
                ranked.Sort((a, b) => J.Num(b, "totalMade").CompareTo(J.Num(a, "totalMade")));
                _body.Add(Ui.Section("Leaderboard"));
                VisualElement board = Ui.Box("glass set-card");
                for (int i = 0; i < ranked.Count; i++)
                {
                    object p = ranked[i];
                    double putts = J.Num(p, "totalPutts");
                    board.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Profiles.Face(p, 36),
                        Ui.Box("grow set-words", Ui.Text(J.Str(p, "name"), "feed-name", Ui.Bold), Ui.Text(putts > 0 ? Mathf.RoundToInt((float)(J.Num(p, "totalMade") / putts * 100)) + "% made" : "No putts yet", "muted", Ui.Regular)),
                        Ui.Text(ProfileMath.Number(J.Num(p, "totalMade")), "feed-score", Ui.Bold)));
                }
                _body.Add(Ui.Box("pad", board, Ui.Text("Putts made. Only the profiles on this account see this, with first names.", "muted", Ui.Regular).Margin(6)));
            }
            if (owner && players.Count > 1)
            {
                _body.Add(Ui.Section("Security"));
                bool pinSet = J.Flag(family, "pinSet");
                _body.Add(Rows.Card(Rows.Row("GearSix", "Require a PIN to switch to " + ownerName, null, Rows.Switch(pinSet, on =>
                {
                    if (on) Profiles.Pin(_app, Root, "Choose a 4-digit PIN", pin => App.Run(Api.Post("/family/pin", new Dictionary<string, object> { { "pin", pin } }, _ => { _app.Toast("PIN set."); Shown(); }, _ => { _app.Toast("Couldn't set the PIN."); Shown(); })));
                    else Profiles.Pin(_app, Root, "Enter your PIN to turn it off", pin => App.Run(Api.Post("/family/pin", new Dictionary<string, object> { { "pin", null }, { "current", pin } }, _ => { _app.Toast("PIN off."); Shown(); }, _ => { _app.Toast("That PIN is not right."); Shown(); })));
                }))));
            }
        }
    }

    /// <summary>AddPlayer: a first name, which hand, and kid or not. A kid picks a character next.</summary>
    public sealed class AddPlayerScreen : AppScreen
    {
        readonly App _app;
        readonly TextField _name;
        bool _left, _kid, _busy;

        public AddPlayerScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Add player", out VisualElement body);
            if (Kid.Blocked(app, Kid.Owner, body)) return;
            _name = new TextField { maxLength = 20 };
            _name.textEdition.placeholder = "First name";
            _name.textEdition.hidePlaceholderOnFocus = false;
            _name.AddToClassList("field");
            body.Add(Ui.Box("pad",
                Ui.Text("They get their own stats, streaks and leaderboard spot. No email or password needed.", "sub", Ui.Regular),
                _name.Face(Ui.Medium).Margin(12),
                Ui.Text("PUTTS", "kick", Ui.Bold).Margin(6), FreePuttScreen.Choice(new[] { "Right-handed", "Left-handed" }, 0, i => _left = i == 1).Margin(6),
                Ui.Text("WHO IS THIS FOR?", "kick", Ui.Bold).Margin(14), FreePuttScreen.Choice(new[] { "A kid, under 13", "Teen or adult" }, 1, i => _kid = i == 0).Margin(6),
                Ui.Text("A Kid profile has video, online play, chat and leagues off, and shows a made-up name everywhere but this account.", "muted", Ui.Regular).Margin(8),
                Ui.Button("Add player", Add).Margin(14)));
        }

        void Add()
        {
            string name = _name.value.Trim();
            if (name.Length == 0) { _app.Toast("Enter their first name."); return; }
            if (_busy) return;
            _busy = true;
            App.Run(Api.Post("/family/create", new Dictionary<string, object> { { "firstName", name }, { "handedness", _left ? "Left" : "Right" }, { "isKid", _kid } }, data =>
            {
                _busy = false;
                object player = J.Get(data, "player");
                _app.Pop();
                if (_kid) _app.Push(new CharacterScreen(_app, player, true));
                else Dialog.Ask(_app.Toasts, name + " is ready", "Switch to " + name + " now, or stay as you are.", "Switch to " + name, () => App.Run(Profiles.Switch(_app, player, null, w => _app.Toast(w))));
            }, why => { _busy = false; _app.Toast(why == "http_409" ? "You have all 5 players." : "Couldn't add them. Try again."); }));
        }
    }

    /// <summary>CrewPicker: one of the twelve characters, in one of six colours. Every pick saves.</summary>
    public sealed class CharacterScreen : AppScreen
    {
        readonly App _app;
        readonly object _player;
        readonly VisualElement _body;
        readonly bool _setup;
        string _animal;
        int _colour = 1;

        public CharacterScreen(App app, object player, bool setup)
        {
            _app = app;
            _player = player;
            _setup = setup;
            Root = Page.Make(app, setup ? "Pick a character" : "Character", out _body);
            object avatar = J.Get(player, "avatar");
            _animal = J.Str(avatar, "animal", Profiles.Animals[0]);
            _colour = Mathf.Clamp(J.Int(avatar, "color", 1), 1, 6);
            Draw();
        }

        void Draw()
        {
            _body.Clear();
            string name = J.Str(_player, "name", "them");
            if (_setup) _body.Add(Ui.Box("pad", Ui.Text("Who is " + name + " going to be?", "h2", Ui.Bold), Ui.Text("Hand " + name + " the phone for this part.", "sub", Ui.Regular)));
            VisualElement preview = Ui.Box("crew-preview");
            Images.Into(preview, Profiles.AvatarUrl(_animal, _colour));
            _body.Add(Ui.Box("pad center-box", preview));
            VisualElement grid = Ui.Box("crew-grid");
            foreach (string a in Profiles.Animals)
            {
                string animal = a;
                VisualElement pick = Ui.Box(a == _animal ? "crew-pick crew-on" : "crew-pick");
                Images.Into(pick, Profiles.AvatarUrl(a, _colour));
                pick.OnTap(() => { _animal = animal; Save(); Draw(); });
                grid.Add(pick);
            }
            _body.Add(Ui.Box("pad", Ui.Text("PICK YOUR CHARACTER", "kick", Ui.Bold), grid.Margin(8)));
            VisualElement colours = Ui.Box("row");
            for (int i = 1; i <= 6; i++)
            {
                int c = i;
                VisualElement swatch = Ui.Box(c == _colour ? "swatch swatch-on" : "swatch");
                swatch.style.backgroundColor = Profiles.Colours[c - 1];
                swatch.OnTap(() => { _colour = c; Save(); Draw(); });
                colours.Add(swatch);
            }
            _body.Add(Ui.Box("pad", Ui.Text("COLOR", "kick", Ui.Bold), colours.Margin(8)));
            _body.Add(Ui.Box("pad", Ui.Button(_setup ? "That's me!" : "Done", Done)).Margin(14));
        }

        void Save() => App.Run(Api.Post("/family/update/" + J.Str(_player, "userId"), new Dictionary<string, object> { { "animal", _animal }, { "color", _colour } }, null, _ => _app.Toast("Couldn't save that.")));

        void Done()
        {
            _app.Pop();
            if (!_setup) return;
            string name = J.Str(_player, "name", "them");
            Dialog.Ask(_app.Toasts, name + " is ready", "Switch to " + name + " now, or stay as you are.", "Switch to " + name, () => App.Run(Profiles.Switch(_app, _player, null, w => _app.Toast(w))));
        }
    }

    /// <summary>PlayerDetail: a managed player, for the owner.</summary>
    public sealed class PlayerScreen : AppScreen
    {
        public PlayerScreen(App app, object player)
        {
            string name = J.Str(player, "name", "Player");
            bool kid = J.Flag(player, "isKid");
            Root = Page.Make(app, name, out VisualElement body);
            body.Add(Ui.Box("pad center-box", Profiles.Face(player, 84).OnTap(() => app.Push(new CharacterScreen(app, player, false))), Ui.Text(name, "h2", Ui.Bold).Margin(8),
                Ui.Text(kid ? "Kid profile" : "Player", kid ? "chip chip-amber" : "chip", Ui.Bold)));
            var rows = new List<VisualElement> { Rows.Row("FillUser", "Character", () => app.Push(new CharacterScreen(app, player, false))) };
            if (kid)
            {
                rows.Add(Rows.Row("Target", "Public name: " + J.Str(player, "displayName"), () => App.Run(Api.Post("/family/update/" + J.Str(player, "userId"), new Dictionary<string, object> { { "newAlias", true } },
                    data => { app.Pop(); app.Push(new PlayerScreen(app, J.Get(data, "player") ?? player)); }, _ => app.Toast("Couldn't change it.")))));
                rows.Add(Rows.Row("Question", "What Puttr saves for " + name, () => app.Push(new KidPrivacyScreen(app))));
                rows.Add(Rows.Row("FillUser", "Move to a standard profile", () => Dialog.Ask(body, "Move " + name + " to a standard profile?", "Video, online play, chat and leagues turn on, and " + name + "'s name shows instead of the made-up one. This cannot be undone.", "Move",
                    () => App.Run(Api.Post("/family/update/" + J.Str(player, "userId"), new Dictionary<string, object> { { "makeStandard", true } }, _ => { app.Toast(name + " is now a standard profile."); app.Pop(); }, _ => app.Toast("Couldn't change it."))))));
            }
            // CrewPicker: a photo from the front camera (a Kid's stays on this phone), or back to the initial.
            string uid = J.Str(player, "userId"), local = PhotoPicker.KidPhotoPath(uid);
            bool hasPhoto = kid ? System.IO.File.Exists(local) : J.Str(player, "photoURL").Length > 0;
            rows.Add(Rows.Row("VideoCamera", hasPhoto ? "Retake photo" : kid ? "Add a photo" : "Take a photo", () => PhotoPicker.TakePhoto(path =>
            {
                if (path == null) return;
                if (path == "nocamera") { Dialog.Ask(body, "Camera not available", "This device has no camera to use.", "OK", () => { }); return; }
                byte[] jpg;
                try { jpg = PhotoPicker.Square(System.IO.File.ReadAllBytes(path), 400, 90); } catch (Exception) { jpg = null; }
                if (jpg == null) { Dialog.Ask(body, "Couldn't save the photo", "Please try again.", "OK", () => { }); return; }
                if (kid) { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(local)); System.IO.File.WriteAllBytes(local, jpg); app.Toast("Saved on this phone only."); app.Pop(); app.Push(new PlayerScreen(app, player)); }
                else App.Run(PhotoPicker.UploadFor(uid, jpg, ok => app.Toast(ok ? "Photo saved." : "Couldn't save the photo. Please try again.")));
            })));
            if (kid && hasPhoto) rows.Add(Rows.Row("Trash", "Remove photo", () => { System.IO.File.Delete(local); app.Pop(); app.Push(new PlayerScreen(app, player)); }));
            // Their data, as a file to keep (GET /family/export/:uid).
            rows.Add(Rows.Row("FilmReel", "Download " + name + "'s data", () => App.Run(Api.Get("/family/export/" + uid, data =>
            {
                string file = System.IO.Path.Combine(Application.temporaryCachePath, "puttr-" + System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9]", "") + ".json");
                System.IO.File.WriteAllText(file, J.Write(data));
                Share.File(file, "application/json");
            }, _ => Dialog.Ask(body, "Couldn't prepare the download", "Check your connection and try again.", "OK", () => { })))));
            body.Add(Rows.Card(rows.ToArray()));
            if (kid) body.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("WHAT " + name.ToUpperInvariant() + " CAN DO", "kick", Ui.Bold),
                Ui.Text("Play every game, keep their own stats and streaks, climb the leaderboards under a made-up name.", "sub", Ui.Regular).Margin(6),
                Ui.Text("Off: video and replays, online play, friends and the feed, chat, leagues.", "muted", Ui.Regular).Margin(6))).Margin(10));
            body.Add(Ui.Box("pad", Ui.Button("Switch to " + name, () => App.Run(Profiles.Switch(app, player, null, w => app.Toast(w)))).Margin(14),
                Ui.Button(kid ? "Delete profile and all data" : "Remove " + name, () => Dialog.Ask(body, "Remove " + name + "?", "All of " + name + "'s putts, games and stats are deleted for good.", "Delete",
                    () => App.Run(Api.Post("/family/remove/" + J.Str(player, "userId"), new Dictionary<string, object>(), _ => { app.Toast(name + " was removed."); app.Pop(); }, _ => app.Toast("Couldn't remove them."))), true), "btn btn-ghost")));
        }
    }

    public sealed class KidPrivacyScreen : AppScreen
    {
        public KidPrivacyScreen(App app)
        {
            Root = Page.Make(app, "Kid profile privacy", out VisualElement body);
            (string title, string words)[] parts =
            {
                ("What is saved", "Putts and games: distance, tee, made or missed, the ball's path on the mat. A first name, a made-up public name and a character."),
                ("What is never collected", "No email, phone number, birthday, location or photos. Video is off. A photo taken for the profile stays on this phone."),
                ("Who can see it", "Only the profiles on this account see the first name. Everyone else sees the made-up name on leaderboards."),
            };
            foreach (var p in parts) { body.Add(Ui.Section(p.title)); body.Add(Page.Note(p.words)); }
            body.Add(Ui.Box("pad", Ui.Text("The account owner can download or delete everything from the player's page.", "muted", Ui.Regular).Margin(8)));
        }
    }
}
