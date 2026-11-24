// The host's brand kit (Event Mode, Venue Mode): one JSON object per profile, the same one the legacy app's Party
// mode writes, so a kit made there carries over as it is.
//   GET  /party/brandkit            { kit, updatedAt }
//   POST /party/brandkit            { kit }   a full replace: always send the whole kit
//   POST /party/brandkit/asset      multipart file + slot -> { url }
//
// Keys this app reads: title, tagline, brandName, logoUri, cohostLogoUri, headerColor (primary), secondaryColor,
// leaderboardTitle, qrUrl, qrText, gameOrder, featuredGameId.
// Keys it adds (the legacy app keeps what it does not know): hostMode, partnerLogos, staffPinHash, gameChoices
// (the host's own on/off for each game; gamesOff, an older list of games switched off, is still read).
// The legacy app's enabledGames and includeCustomGames are left as they are and not read: they were chosen from the
// legacy app's games, so every game this app plays starts on, and the host switches off what they do not want here.
// Keys it leaves alone: the legacy background photos and their dim levels (a 3D world has no wallpaper), and the
// legacy copy lines. They are never removed, so the legacy app still shows them.
//
// The kit is mirrored on the device (PlayerPrefs) so a venue keeps its look with no wifi. Newest wins between the
// mirror and the server: a stale server row never wipes a fresher edit made here, and an edit made offline is
// pushed on the next load. The worlds are dressed through PuttrArcade.Brand (ApplyToWorlds).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PuttrArcade;
using UnityEngine;
using UnityEngine.Networking;

namespace PuttrApp
{
    public static class BrandKit
    {
        public const string Event = "event", Venue = "venue";
        public const string DefaultPrimary = "#7AD446", DefaultSecondary = "#5687FF";
        const string KitKey = "puttr.kit.json", StampKey = "puttr.kit.savedAt", DirtyKey = "puttr.kit.dirty", OwnerKey = "puttr.kit.uid";

        static Dictionary<string, object> _kit = new Dictionary<string, object>();
        static bool _pushing, _pushAgain;

        /// <summary>The kit as loaded and edited. Never null. Read through the getters below.</summary>
        public static Dictionary<string, object> Kit => _kit;
        /// <summary>True once Load has answered (from the server or, with no connection, from the mirror).</summary>
        public static bool Loaded { get; private set; }
        /// <summary>Raised after a load or an edit.</summary>
        public static event Action Changed;

        // ── Reading ─────────────────────────────────────────────────────────

        public static string Str(string key, string fallback = "") { string s = J.Str(_kit, key); return string.IsNullOrEmpty(s) ? fallback : s; }
        public static string Title => Str("title");
        public static string Tagline => Str("tagline");
        public static string Mode => ForceVenue || Str("hostMode") == Venue ? Venue : Event;
        /// <summary>-app2HostVenue: Venue Mode for this run (for checking its screens).</summary>
        public static bool ForceVenue;
        public static string LogoUrl => Str("logoUri");
        public static string CohostLogoUrl => Str("cohostLogoUri");
        public static Color Primary => Brand.Parse(Str("headerColor"), Brand.Parse(DefaultPrimary, Color.green));
        public static Color Secondary => Brand.Parse(Str("secondaryColor"), Brand.Parse(DefaultSecondary, Color.blue));
        public static bool HasPin => Str("staffPinHash").Length > 0;

        public static List<string> PartnerLogos => Strings(_kit, "partnerLogos");

        /// <summary>True when the host has dressed the kit at all (a logo, a title or a colour of their own).</summary>
        public static bool HasContent(Dictionary<string, object> kit)
        {
            if (kit == null) return false;
            return J.Str(kit, "logoUri").Length > 0 || J.Str(kit, "title").Length > 0 || J.Str(kit, "brandName").Length > 0
                || (J.Str(kit, "headerColor").Length > 0 && !string.Equals(J.Str(kit, "headerColor"), DefaultPrimary, StringComparison.OrdinalIgnoreCase));
        }

        static List<string> Strings(object kit, string key)
        {
            var list = new List<string>();
            foreach (object o in J.List(J.Get(kit, key))) if (o is string s && s.Length > 0) list.Add(s);
            return list;
        }

        // ── Games ───────────────────────────────────────────────────────────

        /// <summary>The arcade games (the games table's isArcade): the ones a host offers to start with. Golf-minded
        /// hosts switch on the others. Until the API sends isArcade with the catalogue, these game types are it.</summary>
        public static readonly HashSet<string> ArcadeTypes = new HashSet<string>
        {
            "BOWLING", "MAJORLEAGUE", "ARCADESTREAK", "BALLOONPOP", "ROCK", "HIGHSTRIKER", "BEERPONG", "MATCH", "DERBY", "TARGET",
            "CRICKET", "HORSE", "DEADWEIGHT", "SENDIT", "SLINGSHOT", "SPOTKICK", "TUGOFWAR", "TURFWAR", "UNDERTHECUP", "WINDMILL", "PUTTBALL",
        };

        public static bool IsArcade(object game)
        {
            if (J.Get(game, "isArcade") != null) return J.Flag(game, "isArcade");
            return ArcadeTypes.Contains(J.Str(game, "gameType").ToUpperInvariant());
        }

        /// <summary>The host's switch for a game. What the host chose (gameChoices) stands; otherwise an arcade game
        /// is on and every other game (a golf game, a custom game) is off.</summary>
        public static bool GameOn(Dictionary<string, object> kit, object game)
        {
            string id = J.Str(game, "id");
            object choices = J.Get(kit, "gameChoices");
            object chosen = J.Get(choices, id);
            if (chosen != null) return chosen is bool b ? b : J.Flag(choices, id);
            foreach (object o in J.List(J.Get(kit, "gamesOff"))) if (IdOf(o) == id) return false;
            return !GameCards.Custom(game) && IsArcade(game);
        }

        static string IdOf(object o) => o is string s ? s : o == null ? "" : Convert.ToInt64(o, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

        public static bool GameOn(object game) => GameOn(_kit, game);

        public static string FeaturedGameId(Dictionary<string, object> kit)
        {
            object id = J.Get(kit, "featuredGameId");
            return id == null ? "" : id is string s ? s : Convert.ToInt64(id, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        public static string FeaturedGameId() => FeaturedGameId(_kit);

        /// <summary>Games in the host's order (the kit's gameOrder, ids). Games the order does not name keep their
        /// place after the ordered ones, as in the legacy app.</summary>
        public static List<object> Ordered(Dictionary<string, object> kit, List<object> games)
        {
            var order = new List<string>();
            foreach (object o in J.List(J.Get(kit, "gameOrder"))) order.Add(o is string s ? s : Convert.ToInt64(o, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            if (order.Count == 0) return new List<object>(games);
            var byId = new Dictionary<string, object>();
            foreach (object g in games) byId[J.Str(g, "id")] = g;
            var result = new List<object>();
            var used = new HashSet<string>();
            foreach (string id in order) if (byId.TryGetValue(id, out object g) && used.Add(id)) result.Add(g);
            foreach (object g in games) if (!used.Contains(J.Str(g, "id"))) result.Add(g);
            return result;
        }

        // ── Editing ─────────────────────────────────────────────────────────

        /// <summary>Change one key, keep the mirror, redress the worlds and send the kit (a moment later, so typing
        /// a title is one save and not twelve).</summary>
        public static void Set(string key, object value)
        {
            if (value == null) _kit.Remove(key); else _kit[key] = value;
            Touch();
        }

        public static void SetGame(string gameId, bool on) => SetGames(new[] { gameId }, on);

        /// <summary>The host's choice for these games: on or off (All on, None, a tap on a card).</summary>
        public static void SetGames(IEnumerable<string> ids, bool on)
        {
            var choices = J.Obj(J.Get(_kit, "gameChoices")) ?? new Dictionary<string, object>();
            foreach (string id in ids) if (!string.IsNullOrEmpty(id)) choices[id] = on;
            _kit["gameChoices"] = choices;
            Touch();
        }

        public static void SetOrder(List<string> ids)
        {
            var order = new List<object>();
            foreach (string id in ids) order.Add(long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? (object)(double)n : id);
            _kit["gameOrder"] = order;
            Touch();
        }

        public static void SetPartners(List<string> urls)
        {
            var list = new List<object>();
            foreach (string u in urls) if (!string.IsNullOrEmpty(u) && list.Count < Brand.MaxPartners) list.Add(u);
            _kit["partnerLogos"] = list;
            Touch();
        }

        /// <summary>The staff PIN is kept as a hash salted with the host's id, never as the digits.</summary>
        public static void SetPin(string pin) => Set("staffPinHash", string.IsNullOrEmpty(pin) ? null : PinHash(pin, Auth.Uid));
        public static bool PinIs(string pin) => HasPin && Str("staffPinHash") == PinHash(pin, Auth.Uid);

        public static string PinHash(string pin, string uid)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes("puttr-host-pin:" + (uid ?? "") + ":" + (pin ?? "")));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        static void Touch()
        {
            WriteMirror(true);
            ApplyToWorlds();
            Changed?.Invoke();
            if (App.I != null && !LocalOnly) App.Run(PushSoon());
        }

        // ── The mirror ──────────────────────────────────────────────────────

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        static void WriteMirror(bool dirty)
        {
            if (LocalOnly) return;
            PlayerPrefs.SetString(KitKey, J.Write(_kit));
            PlayerPrefs.SetString(StampKey, Now.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.SetInt(DirtyKey, dirty ? 1 : 0);
            PlayerPrefs.SetString(OwnerKey, Auth.Uid ?? "");
            PlayerPrefs.Save();
        }

        static Dictionary<string, object> ReadMirror(out long savedAt, out bool dirty)
        {
            savedAt = 0; dirty = false;
            if (PlayerPrefs.GetString(OwnerKey, "") != (Auth.Uid ?? "")) return null;   // another account's kit: not ours to show or push
            long.TryParse(PlayerPrefs.GetString(StampKey, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out savedAt);
            dirty = PlayerPrefs.GetInt(DirtyKey, 0) == 1;
            return J.Obj(J.Parse(PlayerPrefs.GetString(KitKey, "")));
        }

        public enum Winner { Server, Mirror, MirrorAndPush, Nothing }

        /// <summary>Which copy to use, newest wins. An edit made here that the server has not had (dirty) wins unless
        /// the server's row is newer still (an edit on another device); a clean mirror gives way to any newer row.</summary>
        public static Winner Decide(bool hasServer, long serverAt, bool hasMirror, long mirrorAt, bool mirrorDirty)
        {
            if (!hasServer && !hasMirror) return Winner.Nothing;
            if (!hasMirror) return Winner.Server;
            if (!hasServer) return Winner.MirrorAndPush;
            if (mirrorDirty) return mirrorAt >= serverAt ? Winner.MirrorAndPush : Winner.Server;
            return serverAt > mirrorAt ? Winner.Server : Winner.Mirror;
        }

        /// <summary>The kit with the names this app reads: kits saved before the legacy app's rename carry
        /// eventName and sponsorName, which become title and brandName. Nothing is removed.</summary>
        public static Dictionary<string, object> Carry(Dictionary<string, object> saved)
        {
            var kit = saved != null ? new Dictionary<string, object>(saved) : new Dictionary<string, object>();
            if (!kit.ContainsKey("title") && kit.ContainsKey("eventName")) kit["title"] = kit["eventName"];
            if (!kit.ContainsKey("brandName") && kit.ContainsKey("sponsorName")) kit["brandName"] = kit["sponsorName"];
            return kit;
        }

        public static long ParseStamp(object updatedAt)
        {
            if (updatedAt is string s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)) return at.ToUnixTimeMilliseconds();
            if (updatedAt is double d) return (long)d;
            return 0;
        }

        // ── Loading and saving ──────────────────────────────────────────────

        /// <summary>-app2Kit file.json: a kit from a file, for looking at the worlds dressed without the API (its
        /// image keys may be file:// or https:// addresses). Nothing is saved or sent while it is on.</summary>
        public static bool LocalOnly { get; private set; }

        public static bool LoadFile(string path)
        {
            try
            {
                Dictionary<string, object> kit = J.Obj(J.Parse(File.ReadAllText(path)));
                if (kit == null) return false;
                _kit = Carry(J.Obj(J.Get(kit, "kit")) ?? kit);
                LocalOnly = true;
                Loaded = true;
                _dressing = true;
                ApplyToWorlds();
                Changed?.Invoke();
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Loads the kit for the signed-in profile: the server's row and the mirror, newest wins.</summary>
        public static IEnumerator Load(Action done = null)
        {
            if (LocalOnly) { done?.Invoke(); yield break; }
            Dictionary<string, object> mirror = ReadMirror(out long mirrorAt, out bool dirty);
            object answer = null;
            bool failed = false;
            yield return Api.Get("/party/brandkit", a => answer = a, _ => failed = true);

            Dictionary<string, object> server = failed ? null : J.Obj(J.Get(answer, "kit"));
            long serverAt = failed ? 0 : ParseStamp(J.Get(answer, "updatedAt"));
            // With no connection the mirror stands as it is, and stays dirty if it was.
            Winner winner = failed ? (mirror != null ? Winner.Mirror : Winner.Nothing) : Decide(server != null, serverAt, mirror != null, mirrorAt, dirty);
            switch (winner)
            {
                case Winner.Server: _kit = Carry(server); WriteMirror(false); break;
                case Winner.Mirror: _kit = Carry(mirror); break;
                case Winner.MirrorAndPush: _kit = Carry(mirror); break;
                default: _kit = new Dictionary<string, object>(); break;
            }
            Loaded = true;
            ApplyToWorlds();
            Changed?.Invoke();
            if (winner == Winner.MirrorAndPush && HasContent(_kit)) yield return Push();
            done?.Invoke();
        }

        static IEnumerator PushSoon()
        {
            if (_pushing) { _pushAgain = true; yield break; }
            _pushing = true;
            do
            {
                _pushAgain = false;
                yield return new WaitForSecondsRealtime(1.2f);
                if (_pushAgain) continue;
                yield return Push();
            } while (_pushAgain);
            _pushing = false;
        }

        /// <summary>Sends the whole kit (the route replaces the row). On success the mirror is clean.</summary>
        public static IEnumerator Push(Action<bool> done = null)
        {
            if (LocalOnly) { done?.Invoke(false); yield break; }
            bool ok = false;
            string sent = J.Write(_kit);
            yield return Api.Post("/party/brandkit", new Dictionary<string, object> { { "kit", _kit } }, _ => ok = true, _ => ok = false);
            // Only clean if nothing was edited while it was on its way.
            if (ok && sent == J.Write(_kit)) { PlayerPrefs.SetInt(DirtyKey, 0); PlayerPrefs.Save(); }
            done?.Invoke(ok);
        }

        /// <summary>Uploads a logo (PNG, JPG or WebP, as picked: a PNG keeps its see-through ground) and answers
        /// its address, or null. slot names the file only: logo, cohostLogo, partner.</summary>
        public static IEnumerator Upload(string slot, string path, Action<string> done)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); } catch (Exception) { bytes = null; }
            if (bytes == null || bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024) { done(null); yield break; }
            string type = MimeOf(bytes);
            if (type == null)
            {
                // Something else (HEIC from the photo library): send it as a JPG.
                try { bytes = PhotoPicker.Shrink(bytes, 1800, 92); } catch (Exception) { bytes = null; }
                if (bytes == null) { done(null); yield break; }
                type = "image/jpeg";
            }
            string token = null;
            yield return Auth.Token(t => token = t);
            string ext = type == "image/png" ? "png" : type == "image/webp" ? "webp" : "jpg";
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("slot", slot),
                new MultipartFormFileSection("file", bytes, slot + "." + ext, type),
            };
            using (UnityWebRequest req = UnityWebRequest.Post(Config.ApiUrl + "/party/brandkit/asset", form))
            {
                if (token != null) req.SetRequestHeader("Authorization", "Bearer " + token);
                req.timeout = 60;
                yield return req.SendWebRequest();
                string url = req.result == UnityWebRequest.Result.Success ? J.Str(J.Parse(req.downloadHandler.text), "url") : "";
                done(string.IsNullOrEmpty(url) ? null : url);
            }
        }

        /// <summary>The image type by its first bytes, for the three the route takes; null for anything else.</summary>
        public static string MimeOf(byte[] b)
        {
            if (b == null || b.Length < 12) return null;
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
            if (b[0] == 0xFF && b[1] == 0xD8) return "image/jpeg";
            if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
            return null;
        }

        // ── Dressing the worlds ─────────────────────────────────────────────

        static int _applying;
        static bool _dressing;

        /// <summary>Whether the worlds wear the kit. Off in the Player's own app: a host's everyday games stay
        /// Puttr. On while the kit editor is open (its preview), in host mode, and under -app2Kit.</summary>
        public static bool Dressing
        {
            get => _dressing;
            set { if (_dressing == value) return; _dressing = value; ApplyToWorlds(); }
        }

        /// <summary>Hands the kit to the 3D worlds (PuttrArcade.Brand): colours at once, logos as they arrive (from
        /// the image cache on the device when there is one, so it works with no wifi). A kit with nothing in it
        /// leaves the worlds as Puttr.</summary>
        public static void ApplyToWorlds()
        {
            int run = ++_applying;
            if (!_dressing || !HasContent(_kit)) { Brand.Clear(); return; }
            string logoUrl = LogoUrl;
            List<string> partnerUrls = PartnerLogos;
            Color primary = Primary, secondary = Secondary;
            string title = Title;
            Texture2D logo = null;
            var partners = new Texture2D[partnerUrls.Count];
            int waiting = (logoUrl.Length > 0 ? 1 : 0) + partnerUrls.Count;
            Action finish = () =>
            {
                if (run != _applying) return;   // a newer edit is on its way
                Brand.Apply(logo, partners, primary, secondary, title);
            };
            if (waiting == 0) { finish(); return; }
            Action arrived = () => { if (--waiting == 0) finish(); };
            if (logoUrl.Length > 0) Images.Load(logoUrl, t => { logo = t; arrived(); });
            for (int i = 0; i < partnerUrls.Count; i++)
            {
                int at = i;
                Images.Load(partnerUrls[at], t => { partners[at] = t; arrived(); });
            }
        }

        /// <summary>Signed out, or another profile: the worlds go back to Puttr and the kit is forgotten in memory
        /// (the mirror stays for when its owner signs back in).</summary>
        public static void Forget()
        {
            _kit = new Dictionary<string, object>();
            Loaded = false;
            LocalOnly = false;
            _dressing = false;
            _applying++;
            Brand.Clear();
        }
    }
}
