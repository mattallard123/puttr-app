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

        /// <summary>What a public leaderboard row carries for this profile (puttr-app utils/family.js publicPlayer):
        /// display name, id, photo, and home town unless the profile is a Kid's.</summary>
        public static string PublicPlayer(object profile)
        {
            var who = new System.Collections.Generic.Dictionary<string, object> { { "displayName", J.Str(profile, "displayName", "Player") }, { "userId", Auth.Uid } };
            string town = IsKid(profile) ? "" : J.Str(profile, "homeTown");
            if (town.Length > 0) who["homeTown"] = town;
            string photo = J.Str(profile, "photoURL");
            if (photo.Length > 0) who["photoURL"] = photo;
            return J.Write(who);
        }

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
            body.Add(Ui.Box("pad", Ui.Box("card prof-face", Ui.Icon("User", 32, Ui.Amber), Ui.Title(title, "title-20 prof-name"), Ui.Text(words, "body-2 center", Ui.Regular),
                Ui.Button("Got it", app.Pop, "btn btn-ghost connect-done").Margin(16))));
            return true;
        }

        /// <summary>
        /// kidGated for a screen that is opened rather than built here: when this profile cannot open it, the notice is
        /// pushed in its place and true comes back (the caller returns). One line at the caller:
        /// <c>if (Kid.Gate(app, Kid.Social)) return;</c>
        /// </summary>
        public static bool Gate(App app, string kind)
        {
            bool blocked = kind == Owner ? Managed(app.Profile) : IsKid(app.Profile);
            if (!blocked) return false;
            app.Push(new KidBlockedScreen(app, kind));
            return true;
        }
    }

    /// <summary>KidLocked as a page of its own: the header's back, and the notice for this kind.</summary>
    public sealed class KidBlockedScreen : AppScreen
    {
        public KidBlockedScreen(App app, string kind)
        {
            Root = Page.Make(app, "", out VisualElement body);
            Kid.Blocked(app, kind, body);
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
            VisualElement a = Images.Cover(Ui.Avatar(name, J.Str(player, "photoURL"), size >= 56));
            // A Kid's photo lives on this phone only (family-photos/<userId>.jpg).
            string local = PhotoPicker.KidPhotoPath(J.Str(player, "userId"));
            if (J.Flag(player, "isKid") && System.IO.File.Exists(local))
            {
                var t = new Texture2D(2, 2);
                if (t.LoadImage(System.IO.File.ReadAllBytes(local))) { a.style.backgroundImage = new StyleBackground(t); if (a.childCount > 0) a[0].style.display = DisplayStyle.None; }
            }
            a.style.width = a.style.height = size;
            // An avatar is a rounded square (docs/design-guidelines.md), whatever its size.
            a.style.borderTopLeftRadius = a.style.borderTopRightRadius = a.style.borderBottomLeftRadius = a.style.borderBottomRightRadius = 4;
            return a;
        }

        /// <summary>switchPlayer: a one-time token for the other profile, then signed in as them and the app starts again.
        /// <paramref name="switched"/> runs once the new profile is signed in, before the tabs are built again: it closes
        /// whatever "Switching to" card the caller put up (those live on the app's stage, which the rebuild leaves alone).</summary>
        public static System.Collections.IEnumerator Switch(App app, object player, string pin, Action<string> failed, Action switched = null)
        {
            string token = null, why = null;
            var body = pin != null ? new Dictionary<string, object> { { "pin", pin } } : new Dictionary<string, object>();
            yield return Api.Post("/family/token/" + J.Str(player, "userId"), body, data => token = J.Str(data, "token"), w => why = w);
            if (string.IsNullOrEmpty(token)) { failed(why == "http_401" ? "pin_required" : "Couldn't switch. Check your connection and try again."); yield break; }
            string problem = null;
            yield return Auth.SignInWithCustomToken(token, p => problem = p);
            if (problem != null) { failed("Couldn't switch. Check your connection and try again."); yield break; }
            switched?.Invoke();
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
            VisualElement card = Ui.Box("modal", Ui.Title("Who's putting?", "title-20 who-title"), grid);
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
            // The one spinner turns in the middle of the screen, which is the middle of this card: room for it under the title.
            grid.Add(Ui.Box("player-loading", Spinner.Small()));
            Load(family =>
            {
                grid.Clear();
                if (family == null) { grid.Add(Ui.Text("Couldn't load profiles.", "body-2", Ui.Regular)); return; }
                bool owner = J.Flag(family, "isOwner");
                List<object> players = J.List(J.Get(family, "players"));
                foreach (object p in players)
                {
                    object player = p;
                    bool me = J.Flag(p, "isMe");
                    VisualElement tile = Ui.Box(me ? "player-tile player-me" : "player-tile", Face(p, 58), Ui.Text(J.Str(p, "name"), "who-name center", Ui.Bold),
                        Ui.Text(J.Flag(p, "isOwner") ? "OWNER" : J.Flag(p, "isKid") ? "KID" : "", "kick center who-mark", Ui.Bold));
                    if (!me) tile.OnTap(() =>
                    {
                        // The owner's profile may need the owner's PIN.
                        if (J.Flag(player, "isOwner") && J.Flag(family, "pinSet")) { dim.RemoveFromHierarchy(); OwnerPin(app, over, player, null); return; }
                        grid.Clear();
                        grid.Add(Ui.Box("player-loading", Spinner.Small()));
                        grid.Add(Ui.Text("Switching to " + J.Str(player, "name") + "...", "body-2 center", Ui.Medium));
                        // The sheet sits on the app's stage, above the tabs: SwitchedPlayer rebuilds the tabs under it and
                        // leaves it there, so the switch itself closes it once the new profile is signed in (and on failure).
                        App.Run(Switch(app, player, null, w => { dim.RemoveFromHierarchy(); app.Toast(w); }, () => dim.RemoveFromHierarchy()));
                    });
                    grid.Add(tile);
                }
                if (owner && players.Count < J.Int(family, "maxPlayers", 5))
                    grid.Add(Ui.Box("player-tile", Ui.Box("player-add", Ui.Icon("Plus", 24, Color.white)), Ui.Text("Add", "who-name center", Ui.Bold)).OnTap(() => { dim.RemoveFromHierarchy(); app.Push(new AddPlayerScreen(app)); }));
                if (owner) card.Add(Ui.Button(players.Count > 1 ? "Manage profiles" : "Add profiles", () => { dim.RemoveFromHierarchy(); app.Push(new ProfilesScreen(app)); }, "btn btn-ghost").Margin(12));
            });
        }

        /// <summary>Switching back to the owner when a PIN is set: a wrong PIN asks again ("That PIN is not right"), and
        /// "Forgot PIN?" says how to get back in without it.</summary>
        static void OwnerPin(App app, VisualElement over, object owner, string error)
        {
            string name = J.Str(owner, "name", "the account owner");
            Pin(app, over, "Enter " + name + "'s PIN", pin =>
            {
                // The pad closes as the fourth digit goes in: the same "Switching to" card stands in until the switch ends.
                VisualElement busy = Busy(over, name);
                App.Run(Switch(app, owner, pin, w =>
                {
                    busy.RemoveFromHierarchy();
                    if (w == "pin_required") OwnerPin(app, over, owner, "That PIN is not right");
                    else app.Toast(w);
                }, () => busy.RemoveFromHierarchy()));
            }, null, error, () => ForgotSwitchPin(app, over, name));
        }

        /// <summary>"Switching to Sam...": a card that cannot be tapped away, closed by the switch when it ends.</summary>
        static VisualElement Busy(VisualElement over, string name)
        {
            VisualElement card = Ui.Box("modal", Ui.Box("player-loading", Spinner.Small()), Ui.Text("Switching to " + name + "...", "body-2 center", Ui.Medium));
            VisualElement dim = Ui.Box("modal-dim", card);
            App.Layer(over).Add(dim);
            return dim;
        }

        /// <summary>
        /// The owner's PIN only guards switching from a player's profile. The owner's own sign-in (email, Apple or Google)
        /// never asks for it, so a forgotten PIN is: sign out here, sign in as the owner, then turn the PIN off or set a new
        /// one in Profiles (which asks for the old one: see Forgot on that pad).
        /// </summary>
        public static void ForgotSwitchPin(App app, VisualElement over, string owner) =>
            Dialog.Ask(over, "Forgot the PIN?", "Sign out, then sign in with " + owner + "'s own email, Apple or Google account. That never asks for the PIN. Then you can turn the PIN off in Profiles.", "Sign out", app.SignedOut);

        /// <summary>A four-digit PIN pad (PinPad.js): a title, an optional line under it, the four dots, a red line for what
        /// went wrong, the keys, Cancel, and "Forgot PIN?" when there is a way back.</summary>
        public static void Pin(App app, VisualElement over, string title, Action<string> entered, string subtitle = null, string error = null, Action forgot = null)
        {
            VisualElement dim = null;
            string pin = "";
            Label dots = Ui.Text("", "pin-dots ital", Ui.Bold);
            VisualElement pad = Ui.Box("pin-pad");
            void Show() => dots.text = new string('•', pin.Length) + new string('-', 4 - pin.Length);
            foreach (string k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "", "0", "<" })
            {
                string key = k;
                VisualElement b = Ui.Box(k.Length == 0 ? "pin-key pin-blank" : "pin-key", k == "<" ? Ui.Caps("Del") : Ui.Num(k, "num-20"));
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
            VisualElement card = Ui.Box("modal", Ui.Title(title, "title-20 who-title"),
                string.IsNullOrEmpty(subtitle) ? null : Ui.Text(subtitle, "body-2 center pin-sub", Ui.Regular),
                dots, Ui.Text(error ?? "", "pin-error", Ui.Medium), pad,
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"),
                forgot == null ? null : Ui.Text("Forgot PIN?", "pin-forgot", Ui.Medium).OnTap(() => { dim.RemoveFromHierarchy(); forgot(); }));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            // The layer is the app's stage, outside any page: the pad brings its own styles.
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Home");
            if (sheet != null) dim.styleSheets.Add(sheet);
            App.Layer(over).Add(dim);
        }

        /// <summary>What a failed POST /family/create means (AddPlayer.js ERRORS), from its status: the API answers 400
        /// bad_name, 403 ask_account_owner, 409 family_full.</summary>
        public static (string title, string body) AddError(string why)
        {
            switch (why)
            {
                case "http_400": return ("Check the name", "Use a first name of up to 20 letters.");
                case "http_403": return ("Ask the account owner", "Only the account owner can add players.");
                case "http_409": return ("All profiles are in use", "Remove a player to add another.");
                default: return ("Couldn't add the player", "Check your connection and try again.");
            }
        }

        /// <summary>The PIN steps (FamilyScreen onPinDigits): a new PIN, then the same again; a mismatch starts over.
        /// Returns the next step: "confirm" with the first PIN kept, "save" when both match, "new" with an error.</summary>
        public static string NextPinStep(string step, string first, string digits, out string error)
        {
            error = null;
            if (step == "new") return "confirm";
            if (step == "confirm" && digits == first) return "save";
            error = "Those did not match. Try again.";
            return "new";
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
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Home");
            if (sheet != null) Root.styleSheets.Add(sheet);
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
                : "This profile is on " + ownerName + "'s account. " + ownerName + " adds and manages profiles.", "body-2 prof-intro", Ui.Regular)));
            VisualElement list = Ui.Box("glass set-card");
            foreach (object p in players)
            {
                object player = p;
                string sub = J.Flag(p, "isOwner") ? "Account owner" : J.Flag(p, "isKid") ? "Kid profile   ·   " + J.Str(p, "displayName") : "Player";
                VisualElement right = J.Flag(p, "isMe") ? Ui.Text("You", "chip chip-green", Ui.Bold) : J.Flag(p, "isKid") ? Ui.Text("Kid", "chip chip-amber", Ui.Bold) : null;
                VisualElement row = Ui.Box("set-row", Profiles.Face(p, 44), Ui.Box("grow set-words", Ui.Text(J.Str(p, "name"), "set-title", Ui.Bold), Ui.Text(sub, "set-sub", Ui.Regular)), right);
                if (owner && !J.Flag(p, "isOwner")) row.OnTap(() => _app.Push(new PlayerScreen(_app, player)));
                list.Add(row);
            }
            if (list.childCount > 0) list[list.childCount - 1].AddToClassList("set-row-last");
            _body.Add(Ui.Box("pad", list));
            if (owner)
            {
                // Full: the button stays "Add player", off, with the way out under it (FamilyScreen), using the API's own maximum.
                bool full = players.Count >= max;
                VisualElement add = Ui.Button("Add player", () => { if (!full) _app.Push(new AddPlayerScreen(_app)); }, full ? "btn btn-off" : "btn");
                add.SetEnabled(!full);
                _body.Add(Ui.Box("pad", add, full ? Ui.Text("You have all " + max + " players. Remove one to add another.", "body-2 center profiles-full", Ui.Regular) : null));
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
                    board.Add(Ui.Box(i == ranked.Count - 1 ? "board-row set-row-last" : "board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Profiles.Face(p, 36),
                        Ui.Box("grow set-words", Ui.Text(J.Str(p, "name"), "set-title", Ui.Bold), Ui.Text(putts > 0 ? Mathf.RoundToInt((float)(J.Num(p, "totalMade") / putts * 100)) + "% made" : "No putts yet", "set-sub", Ui.Regular)),
                        Ui.Num(ProfileMath.Number(J.Num(p, "totalMade")), "num-20 set-num")));
                }
                _body.Add(Ui.Box("pad", board, Ui.Text("Putts made. Only the profiles on this account see this, with first names.", "body-2", Ui.Regular).Margin(8)));
            }
            if (owner && players.Count > 1)
            {
                _body.Add(Ui.Section("Security"));
                bool pinSet = J.Flag(family, "pinSet");
                _body.Add(Rows.Card(Rows.Row("GearSix", "Require a PIN to switch to " + ownerName, null, Rows.Switch(pinSet, on =>
                {
                    if (on) NewPin(null, null);
                    else ClearPin(null);
                }))));
                _body.Add(Ui.Box("pad", Ui.Text("Keeps other players out of purchases and settings.", "body-2", Ui.Regular)).Margin(8));
            }
        }

        // Setting the PIN: choose it, then enter it again; a mismatch starts over with "Those did not match".
        // Cancel at any step leaves the PIN as it was (the switch is drawn again from the API's answer).
        void NewPin(string first, string error)
        {
            string step = first == null ? "new" : "confirm";
            Profiles.Pin(_app, Root, step == "new" ? "Choose a 4-digit PIN" : "Enter it again", digits =>
            {
                string next = Profiles.NextPinStep(step, first, digits, out string why);
                if (next == "confirm") { NewPin(digits, null); return; }
                if (next == "new") { NewPin(null, why); return; }
                App.Run(Api.Post("/family/pin", new Dictionary<string, object> { { "pin", digits } }, _ => { _app.Toast("PIN set."); Shown(); },
                    _ => { Dialog.Ask(_app.Toasts, "Couldn't save the PIN", "Check your connection and try again.", "OK", () => { }); Shown(); }));
            }, step == "new" ? "You will need it to switch back to your own profile." : null, error);
            // The switch flipped on as it was tapped: drawn again from the API so a cancelled PIN shows it off.
            Shown();
        }

        // Turning it off needs the current PIN; a wrong one asks again. Forgot: the API cannot clear it without the PIN.
        void ClearPin(string error)
        {
            Profiles.Pin(_app, Root, "Enter your PIN to turn it off", digits =>
                App.Run(Api.Post("/family/pin", new Dictionary<string, object> { { "pin", null }, { "current", digits } }, _ => { _app.Toast("PIN off."); Shown(); },
                    why => { if (why == "http_401") ClearPin("That PIN is not right"); else { Dialog.Ask(_app.Toasts, "Couldn't turn the PIN off", "Check your connection and try again.", "OK", () => { }); Shown(); } })),
                null, error, ForgotOwnPin);
            Shown();
        }

        // There is no reset without the PIN in the API (POST /family/pin checks `current`), so support resets it.
        void ForgotOwnPin() =>
            Dialog.Ask(_app.Toasts, "Forgot your PIN?", "The PIN can only be turned off with the PIN. Your caddie can reset it for you: message them and say which account it is for.", "Message your caddie", () => _app.Push(new CaddieScreen(_app)));
    }

    /// <summary>AddPlayer: a first name, which hand, and kid or not. A kid picks a character next.</summary>
    public sealed class AddPlayerScreen : AppScreen
    {
        readonly App _app;
        readonly TextField _name;
        Label _go, _kidNote;
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
            VisualElement go = Ui.Button("Add player", Add);
            _go = go.Q<Label>(className: "btn-text");
            _kidNote = Ui.Text("Kid profiles play every game under a made-up public name, with video, chat and purchases off.", "body-2", Ui.Regular);
            _kidNote.style.display = DisplayStyle.None;
            body.Add(Ui.Box("pad",
                Ui.Text("They get their own stats, streaks and leaderboard spot. No email or password needed.", "body-2", Ui.Regular),
                _name.Face(Ui.Medium).Margin(12),
                Ui.Caps("Putts", "caps caps-green").Margin(4), Pick(new[] { "Right-handed", "Left-handed" }, 0, i => _left = i == 1).Margin(8),
                Ui.Caps("Who is this for?", "caps caps-green").Margin(16), Pick(new[] { "A kid, under 13", "Teen or adult" }, 1, PickWho).Margin(8),
                _kidNote.Margin(8),
                go.Margin(16)));
        }

        // The two-way choice, with the chosen side lit as it is tapped (FreePuttScreen.Choice draws it once and leaves it).
        static VisualElement Pick(string[] names, int chosen, Action<int> pick)
        {
            VisualElement row = Ui.Box("choice");
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                row.Add(Ui.Box(i == chosen ? "choice-item choice-on" : "choice-item", Ui.Text(names[i], "choice-text", Ui.Bold)).OnTap(() =>
                {
                    for (int k = 0; k < row.childCount; k++) row[k].EnableInClassList("choice-on", k == index);
                    pick(index);
                }));
            }
            return row;
        }

        // A kid picks a character next (AddPlayer: the note, and the button says so).
        void PickWho(int choice)
        {
            _kid = choice == 0;
            _kidNote.style.display = _kid ? DisplayStyle.Flex : DisplayStyle.None;
            _go.text = _kid ? "NEXT: PICK A CHARACTER" : "ADD PLAYER";
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
            }, why =>
            {
                _busy = false;
                (string title, string words) = Profiles.AddError(why);
                Dialog.Ask(_app.Toasts, title, words, "OK", () => { });
            }));
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
            if (_setup) _body.Add(Ui.Box("pad", Ui.Title("Who is " + name + " going to be?", "title-20"), Ui.Text("Hand " + name + " the phone for this part.", "body-2", Ui.Regular).Margin(4)));
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
            _body.Add(Ui.Box("pad", Ui.Caps("Pick your character", "caps caps-green").Margin(16), grid.Margin(8)));
            VisualElement colours = Ui.Box("row");
            for (int i = 1; i <= 6; i++)
            {
                int c = i;
                VisualElement swatch = Ui.Box(c == _colour ? "swatch swatch-on" : "swatch");
                swatch.style.backgroundColor = Profiles.Colours[c - 1];
                swatch.OnTap(() => { _colour = c; Save(); Draw(); });
                colours.Add(swatch);
            }
            _body.Add(Ui.Box("pad", Ui.Caps("Color", "caps caps-green").Margin(8), colours.Margin(8)));
            _body.Add(Ui.Box("pad", Ui.Button(_setup ? "That's me!" : "Done", Done)).Margin(16));
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
            body.Add(Ui.Box("pad prof-face", Profiles.Face(player, 72).OnTap(() => app.Push(new CharacterScreen(app, player, false))), Ui.Title(name, "title-20 prof-name"),
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
            body.Add(Rows.Card(rows.ToArray()).Margin(16));
            if (kid) body.Add(Ui.Box("pad", Ui.Box("card", Ui.Text("WHAT " + name.ToUpperInvariant() + " CAN DO", "caps caps-green", Ui.Bold),
                Ui.Text("Play every game, keep their own stats and streaks, climb the leaderboards under a made-up name.", "body", Ui.Regular).Margin(8),
                Ui.Text("Off: video and replays, online play, friends and the feed, chat, leagues.", "body-2", Ui.Regular).Margin(8))).Margin(12));
            body.Add(Ui.Box("pad", Ui.Button("Switch to " + name, () => App.Run(Profiles.Switch(app, player, null, w => app.Toast(w)))).Margin(16),
                Ui.Button(kid ? "Delete profile and all data" : "Remove " + name, () => Dialog.Ask(body, "Remove " + name + "?", "All of " + name + "'s putts, games and stats are deleted for good.", "Delete",
                    () => App.Run(Api.Post("/family/remove/" + J.Str(player, "userId"), new Dictionary<string, object>(), _ => { app.Toast(name + " was removed."); app.Pop(); }, _ => app.Toast("Couldn't remove them."))), true), "btn btn-red").Margin(12)));
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
            body.Add(Ui.Box("pad", Ui.Text("The account owner can download or delete everything from the player's page.", "body-2", Ui.Regular).Margin(8)));
        }
    }
}
