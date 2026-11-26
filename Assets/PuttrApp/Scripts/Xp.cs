using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>
    /// XP and levels (docs/design-guidelines.md: Rank colours, Rank badge, XP panel, Level card, Levels sheet, Level up;
    /// the drawings are docs/xp-levels/places.html). Level is effort, PQS is skill. 1,000 XP a level, flat; nine ranks,
    /// each reached at a total; the reward is status: a rank's colour beside the Player's name. The API owns every number
    /// (puttr-api src/xp.js): it pays XP for a saved game when the game over page asks (POST /xp/claim), and the app
    /// only draws what comes back. Until the API has the routes, every read fails quietly and nothing is shown.
    /// </summary>
    public static class Xp
    {
        public const int PerLevel = 1000;
        // The ladder (Matt, 2026-11-25): nine ranks, each starting at a round level (1, 5, 10, 20, 50, 100, 150, 250,
        // 500), the bands widening so that Legend is more than a year of heavy daily play. A level is still 1,000 XP,
        // so a rank is reached at (its first level - 1) x 1,000. puttr-api src/xpRules.js holds the same table.
        public static readonly string[] Ranks = { "Rookie", "Dance Floor", "Weekender", "Scratch", "Club Champ", "State Am", "Tour", "Major", "Legend" };
        static readonly long[] Reach = { 0, 4000, 9000, 19000, 49000, 99000, 149000, 249000, 499000 };

        // ── The numbers ──

        public static int Level(long xp) => (int)(Math.Max(0L, xp) / PerLevel) + 1;
        /// <summary>XP inside the current level: 0 to 999.</summary>
        public static int Into(long xp) => (int)(Math.Max(0L, xp) % PerLevel);
        public static int ToNext(long xp) => PerLevel - Into(xp);
        /// <summary>0 Rookie ... 8 Legend: the last rank whose total the level has reached.</summary>
        public static int Rank(int level)
        {
            long xp = (Math.Max(1, level) - 1) * (long)PerLevel;
            int rank = 0;
            for (int r = 1; r < Reach.Length; r++) if (xp >= Reach[r]) rank = r;
            return rank;
        }
        public static string RankName(int level) => Ranks[Rank(level)];
        /// <summary>The first level of a rank, and the XP it takes to stand there.</summary>
        public static int FirstLevel(int rank) => (int)(XpToReach(rank) / PerLevel) + 1;
        public static long XpToReach(int rank) => Reach[Mathf.Clamp(rank, 0, Reach.Length - 1)];
        /// <summary>Levels to the next rank; 0 for a Legend.</summary>
        public static int LevelsToNextRank(int level) => Rank(level) >= Ranks.Length - 1 ? 0 : FirstLevel(Rank(level) + 1) - level;

        public static string Number(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

        // ── The rank colours (the guidelines' table: the one place the palette grows) ──

        static readonly Color[] Colours =
        {
            Ui.Faint, Ui.Green, new Color32(0, 168, 150, 255), new Color32(86, 135, 255, 255), new Color32(160, 110, 255, 255),
            new Color32(232, 90, 170, 255), new Color32(255, 122, 58, 255), Color.white, Ui.Gold,
        };

        public static Color Colour(int level) => Colours[Rank(level)];
        /// <summary>White words on green, blue and purple; black on the rest.</summary>
        public static Color Ink(int level) { int r = Rank(level); return r == 1 || r == 3 || r == 4 ? Color.white : Color.black; }

        /// <summary>The rank badge: the level on the rank's colour. It follows a Player's name wherever the name is shown.</summary>
        public static VisualElement Badge(int level, bool big = false)
        {
            Label number = Ui.Num(level.ToString(CultureInfo.InvariantCulture), big ? "xp-badge xp-badge-big" : "xp-badge");
            number.style.backgroundColor = Colour(level);
            number.style.color = Ink(level);
            return number;
        }

        /// <summary>A 2 px ring of the rank's colour round an avatar.</summary>
        public static void Ring(VisualElement avatar, int level)
        {
            if (avatar == null) return;
            Color c = Colour(level);
            avatar.style.borderTopWidth = avatar.style.borderBottomWidth = avatar.style.borderLeftWidth = avatar.style.borderRightWidth = 2;
            avatar.style.borderTopColor = avatar.style.borderBottomColor = avatar.style.borderLeftColor = avatar.style.borderRightColor = c;
        }

        // ── What the API says ──

        /// <summary>The signed-in Player's XP, or null until it has been read (or when the API has no XP yet).</summary>
        public static long? Mine { get; private set; }
        /// <summary>The awards and their values, as the API pays them: rows of { group, label, note, xp }.</summary>
        public static List<object> Rules { get; private set; } = new List<object>();
        static string _for;
        static readonly Dictionary<string, int> Others = new Dictionary<string, int>();
        public static event Action Changed;

        static void Forget() { Mine = null; Rules = new List<object>(); Others.Clear(); _for = Auth.Uid; }

        /// <summary>Reads the Player's own XP and the award table. Quiet when it fails: nothing is shown.</summary>
        public static IEnumerator Load(Action then = null)
        {
            if (_for != Auth.Uid) Forget();
            return Api.Get("/xp/me", data =>
            {
                object row = J.First(data);
                if (row == null || J.Get(row, "xp") == null) return;
                Mine = (long)J.Num(row, "xp");
                Rules = J.List(J.Get(row, "rules"));
                Changed?.Invoke();
                then?.Invoke();
            }, _ => { });
        }

        /// <summary>Another Player's level, or null when it is not known yet (see Need).</summary>
        public static int? LevelOf(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            if (userId == Auth.Uid && Mine != null && _for == Auth.Uid) return Level(Mine.Value);
            return Others.TryGetValue(userId, out int level) ? level : (int?)null;
        }

        /// <summary>Reads the levels of the Players a list is about to show, once each; then() when any were new.</summary>
        public static void Need(IEnumerable<string> userIds, Action then)
        {
            if (_for != Auth.Uid) Forget();
            var ask = new List<object>();
            foreach (string id in userIds) if (!string.IsNullOrEmpty(id) && !Others.ContainsKey(id) && !ask.Contains(id)) ask.Add(id);
            if (ask.Count == 0) return;
            App.Run(Api.Post("/xp/levels", new Dictionary<string, object> { { "userIds", ask } }, data =>
            {
                object levels = J.Get(J.First(data), "xp");   // { userId: xp }
                if (levels == null) return;
                foreach (object id in ask) { string key = (string)id; Others[key] = Level((long)J.Num(levels, key)); }
                then?.Invoke();
            }, _ => { }));
        }

        /// <summary>A name with its badge after it, once the level is known (a list row's title).</summary>
        public static VisualElement Named(VisualElement name, string userId)
        {
            VisualElement row = Ui.Box("row xp-named", name);
            int? level = LevelOf(userId);
            if (level != null) row.Add(Badge(level.Value));
            return row;
        }

        // ── The pieces ──

        /// <summary>The 4 px bar: brand green for what the Player had, white for what was just added.</summary>
        static VisualElement Bar(long before, long after)
        {
            // Within the level the Player is now in: XP from before this level started counts as "had" only if it was in it.
            int level = Level(after);
            long floor = (level - 1) * (long)PerLevel;
            float had = Mathf.Clamp01((Math.Max(before, floor) - floor) / (float)PerLevel), now = Mathf.Clamp01((after - floor) / (float)PerLevel);
            VisualElement green = Ui.Box("xp-bar-had"), white = Ui.Box("xp-bar-new");
            green.style.width = Length.Percent(had * 100f);
            white.style.width = Length.Percent(Mathf.Max(0f, now - had) * 100f);
            return Ui.Box("xp-bar", green, white);
        }

        /// <summary>The level card (Profile, the Levels sheet): the rank's colour down its left edge.</summary>
        public static VisualElement LevelCard(long xp, bool total, Action onTap)
        {
            int level = Level(xp);
            VisualElement head = Ui.Box("row xp-card-head", Ui.Num(level.ToString(CultureInfo.InvariantCulture)), Ui.Title(RankName(level), "title-20 xp-card-rank"),
                Ui.Text(total ? Number(xp) + " XP" : Number(Into(xp)) + " / " + Number(PerLevel) + " XP", "body-2 xp-card-right", Ui.Medium));
            int ranks = LevelsToNextRank(level);
            VisualElement foot = Ui.Box("row xp-card-foot", Ui.Text(Number(ToNext(xp)) + " to Level " + (level + 1), "body-2", Ui.Medium),
                Ui.Text(ranks == 0 ? "" : ranks + (ranks == 1 ? " level to " : " levels to ") + Ranks[Rank(level) + 1], "body-2 xp-card-right", Ui.Medium));
            VisualElement card = Ui.Box("card xp-card", head, Bar(xp, xp), foot);
            card.style.borderLeftColor = Colour(level);
            if (onTap != null) card.OnTap(onTap);
            return card;
        }

        /// <summary>
        /// The XP panel on a game over page: asks the API what this saved game paid and draws it into the slot. Nothing
        /// is drawn when the game paid nothing or the API has no XP. levelled(level): the game carried the Player over.
        /// </summary>
        public static void Panel(App app, VisualElement into, string userGameId, bool unpaid, Action<int> levelled)
        {
            bool named = !string.IsNullOrEmpty(userGameId) && userGameId != "0" && !Api.IsLocal(userGameId);
            // unpaid: a page with no one saved game to name (a quest stage, a Tracks round) asks for whatever is unpaid (game 0).
            if (!named && !unpaid && !Fixtures.On) return;
            Claim(app, into, named ? userGameId : "0", levelled, true);
        }

        static void Claim(App app, VisualElement into, string userGameId, Action<int> levelled, bool again)
        {
            // (Under -app2Fixture the made-up answer is drawn whatever the game's id, so the panel can be checked from a script.)
            if (HostMode.On) return;
            if (!Fixtures.On && string.IsNullOrEmpty(Auth.Uid)) return;
            App.Run(Api.Post("/xp/claim", new Dictionary<string, object> { { "userGameId", userGameId } }, data =>
            {
                object reply = J.First(data);
                List<object> awards = J.List(J.Get(reply, "awards"));
                if (reply == null) return;
                if (awards.Count == 0)
                {
                    // Nothing yet: what the game saves last (a quest's stars, a League score) may still be on its way.
                    // Asked once more a moment later, while the page is still up.
                    if (again) into.schedule.Execute(() => { if (into.panel != null) Claim(app, into, userGameId, levelled, false); }).StartingIn(2000);
                    return;
                }
                long before = (long)J.Num(reply, "before"), after = (long)J.Num(reply, "after");
                if (_for != Auth.Uid) Forget();
                Mine = after;
                Changed?.Invoke();
                into.Clear();
                into.Add(PanelFor(app, awards, before, after));
                if (Level(after) > Level(before)) levelled?.Invoke(Level(after));
            }, _ => { }));
        }

        public static VisualElement PanelFor(App app, List<object> awards, long before, long after)
        {
            int level = Level(after);
            bool up = level > Level(before);
            VisualElement panel = Ui.Box("go-block go-gap xp-panel");
            panel.Add(Ui.Box("row xp-panel-head", Ui.Caps("XP earned", "caps caps-green"), Ui.Num("+" + Number(after - before), "num xp-panel-total")));
            foreach (object a in awards)
                panel.Add(Ui.Box("row xp-panel-row", Ui.Text(J.Str(a, "label"), "body-2 xp-panel-label", Ui.Medium), Ui.Text(Number((long)J.Num(a, "xp")), "body-2 xp-panel-value", Ui.Bold)));
            panel.Add(Bar(before, after));
            panel.Add(Ui.Box("row xp-card-foot", Ui.Text("Level " + level + "  ·  " + RankName(level), "body-2", Ui.Medium),
                Ui.Text(up ? "Level up" : Number(ToNext(after)) + " to Level " + (level + 1), "body-2 xp-card-right", Ui.Medium)));
            return panel.OnTap(() => app.Push(new LevelsScreen(app)));
        }
    }

    /// <summary>The Levels sheet: where the Player stands, the ranks, and every way to earn XP with its value.</summary>
    public sealed class LevelsScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;

        public LevelsScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Levels", out _body);
            Draw();
            App.Run(Xp.Load(Draw));
        }

        void Draw()
        {
            _body.Clear();
            if (Xp.Mine == null) { _body.Add(Page.Loading()); return; }
            long xp = Xp.Mine.Value;
            int level = Xp.Level(xp), mine = Xp.Rank(level);
            _body.Add(Ui.Box("pad", Xp.LevelCard(xp, true, null)));

            _body.Add(Ui.Section("The ranks"));
            VisualElement ranks = Ui.Box("glass set-card");
            for (int r = 0; r < Xp.Ranks.Length; r++)
            {
                int first = Xp.FirstLevel(r);
                int last = r == Xp.Ranks.Length - 1 ? first : Xp.FirstLevel(r + 1) - 1;
                string levels = r == Xp.Ranks.Length - 1 ? "Level " + first + " and up" : last == first ? "Level " + first : "Levels " + first + " to " + last;
                string line = r == 0 ? levels : levels + "  ·  " + Xp.Number(Xp.XpToReach(r)) + " XP";
                VisualElement right = r < mine ? (VisualElement)Ui.Icon("Check", 18, Ui.Bright) : r == mine ? Ui.Text("You", "body-2 xp-you", Ui.Bold) : new VisualElement();
                VisualElement row = Ui.Box("set-row xp-rank-row", Xp.Badge(first, true),
                    Ui.Box("grow", Ui.Text(Xp.Ranks[r], "body", Ui.Bold), Ui.Text(line, "body-2", Ui.Medium)), right);
                if (r == Xp.Ranks.Length - 1) row.AddToClassList("set-row-last");
                ranks.Add(row);
            }
            _body.Add(Ui.Box("pad", ranks));
            _body.Add(Page.Note("Your rank's colour sits beside your name on every board, in the feed and before every game. A rank never makes a putt easier."));

            // How to earn XP: the API's own table, in its groups and its order.
            string group = null;
            VisualElement block = null;
            foreach (object rule in Xp.Rules)
            {
                string g = J.Str(rule, "group");
                if (g != group)
                {
                    Close(block);
                    group = g;
                    _body.Add(Ui.Section(group));
                    block = Ui.Box("glass set-card");
                }
                var words = Ui.Box("grow", Ui.Text(J.Str(rule, "label"), "body", Ui.Bold));
                string note = J.Str(rule, "note");
                if (note.Length > 0) words.Add(Ui.Text(note, "body-2", Ui.Medium));
                block.Add(Ui.Box("set-row", words, Ui.Num(Xp.Number((long)J.Num(rule, "xp")), "num-20")));
            }
            Close(block);
        }

        void Close(VisualElement block)
        {
            if (block == null || block.childCount == 0) return;
            block[block.childCount - 1].AddToClassList("set-row-last");
            _body.Add(Ui.Box("pad", block));
        }
    }

    /// <summary>Level up: the new number (or, at the first level of a rank, the new rank in its colour) and the badge as others see it.</summary>
    public sealed class LevelUpScreen : AppScreen
    {
        public LevelUpScreen(App app, int level)
        {
            bool newRank = level > 1 && Xp.Rank(level) > Xp.Rank(level - 1);
            int toRank = Xp.LevelsToNextRank(level);
            VisualElement page = Ui.Box("screen xp-up");
            VisualElement middle = Ui.Box("xp-up-middle");
            middle.Add(Ui.Caps(newRank ? "New rank  ·  level " + level : "Level up", "caps caps-green go-center"));
            if (newRank)
            {
                Label rank = Ui.Title(Xp.RankName(level), "xp-up-rank");
                rank.style.color = Xp.Colour(level);
                middle.Add(rank);
            }
            else
            {
                middle.Add(Ui.Num(level.ToString(CultureInfo.InvariantCulture), "xp-up-number"));
                middle.Add(Ui.Title(Xp.RankName(level), "title go-center"));
            }
            if (toRank > 0) middle.Add(Ui.Text(toRank + (toRank == 1 ? " level to " : " levels to ") + Xp.Ranks[Xp.Rank(level) + 1], "body-2 go-center xp-up-next", Ui.Medium));

            // The Player's own row, as other Players now see it.
            string name = J.Str(app.Profile, "displayName", "You");
            VisualElement avatar = Ui.Avatar(name, J.Str(app.Profile, "photoURL"));
            Xp.Ring(avatar, level);
            string line = newRank ? "This colour is " + Xp.RankName(level) + ". It is on your name everywhere." : "How other Players see you now";
            middle.Add(Ui.Box("glass xp-up-me", avatar, Ui.Box("grow", Ui.Box("row xp-named", Ui.Text(name, "body", Ui.Bold), Xp.Badge(level)), Ui.Text(line, "body-2", Ui.Medium))));

            page.Add(middle);
            page.Add(Ui.Box("pad xp-up-actions", Ui.Button("Continue", app.Pop), Ui.Secondary("See the levels", () => { app.Pop(); app.Push(new LevelsScreen(app)); })));
            Root = page;
        }
    }
}
