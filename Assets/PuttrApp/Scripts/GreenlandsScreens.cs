// Greenlands screens: the world map (the level select) and the card after a stage.
//
//   GreenlandsScreen    the Quests page: the world to play now as the hero, the other worlds as cards
//   GreenWorldScreen    one world's map: the path, its nodes, the stage picked and its Play button
//   WorldArt            a world's picture for a card
//   GreenMap            the map itself: land and trail painted in the world's colours, the nodes as elements over it
//   StageResultScreen   stage clear (stars, markers, what opened, the boss's reward) or out of balls
//
// The rules and the catalogue are in Greenlands.cs; the 3D stage is PuttrArcade MeadowScene.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>The Quests page: the world to play now as the hero, every other world as a card under it.</summary>
    public sealed class GreenlandsScreen : AppScreen
    {
        /// <summary>The page on screen, so a result can refresh it when it is in a tab (which is not told it came back).</summary>
        public static GreenlandsScreen Live;
        /// <summary>A script's pick of a world to open straight away (-app2Open playquests:3). Taken once.</summary>
        public static int OpenWorld;

        readonly App _app;
        readonly VisualElement _body = new VisualElement();
        bool _wide, _loadedAll;

        public GreenlandsScreen(App app)
        {
            _app = app;
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("Quests", app.Pop));
            scroll.Add(_body);
            scroll.Add(FreePuttScreen.Space(28));
            Root = scroll;
            Draw();
            scroll.RegisterCallback<GeometryChangedEvent>(_ => { if (Wide() != _wide) Draw(); });
        }

        bool Wide()
        {
            float w = Root != null ? Root.resolvedStyle.width : float.NaN;
            return !float.IsNaN(w) && w >= 760f;
        }

        public override void Shown()
        {
            Live = this;
            Draw();
            // Every world's stars: the cards' totals, and which worlds are open.
            if (!_loadedAll) { _loadedAll = true; App.Run(GreenProgress.LoadAll(Draw)); }
            else App.Run(GreenProgress.Load(Greenlands.World(GreenProgress.Furthest), Draw));
            // A script's world opens a frame later: the tab is still being put up when it is first shown.
            if (OpenWorld > 0) { int world = OpenWorld; OpenWorld = 0; App.Run(Later(() => Open(Greenlands.World(world)))); }
        }

        public void Refresh() { _picked = null; _tapped = false; Draw(); }

        static IEnumerator Later(Action act) { yield return null; yield return null; act(); }

        void Open(GreenWorld world) => _app.Push(new GreenWorldScreen(_app, world));

        void Draw()
        {
            _body.Clear();
            _wide = Wide();
            // The hero is the world to play now: the first, or the next one once the worlds before it are beaten.
            GreenWorld now = Greenlands.World(GreenProgress.Furthest);

            _body.Add(Ui.Box("pad row gl-home-head",
                Ui.Box("grow", Ui.Text("GREENLANDS", "kick", Ui.Bold)),
                Ui.Box("row gl-totals",
                    new StarRow(1, 16f, 1), Ui.Text(GreenProgress.Total + " / " + GreenProgress.Possible, "gl-total", Ui.Bold),
                    Ui.Box("gl-marker"), Ui.Text(GreenProgress.Markers.ToString(), "gl-total", Ui.Bold))));
            _body.Add(Ui.Box("pad", Hero(now)));

            VisualElement grid = Ui.Box("gl-cards");
            // Four across on a wide screen, two on a phone: every card the same width, the last row not stretched.
            foreach (GreenWorld w in Greenlands.Worlds)
            {
                if (w == now) continue;
                VisualElement slot = Ui.Box("gl-cardslot", WorldCard(w));
                slot.style.width = Length.Percent(_wide ? 25f : 50f);
                grid.Add(slot);
            }
            _body.Add(Ui.Box("pad", grid));
        }

        GreenStage _picked;
        bool _tapped;

        /// <summary>The world to play now, as its map: tap a stage on it and the card beside it is that stage.</summary>
        VisualElement Hero(GreenWorld w)
        {
            // Until a node is tapped the card follows the stage to play next (the stars arrive after the first draw).
            if (_picked == null || !_tapped || _picked.World != w.Num) _picked = GreenProgress.NextIn(w);
            VisualElement title = Ui.Box("",
                Ui.Box("row", Ui.Box("grow", Ui.Text("WORLD " + w.Num + " OF " + Greenlands.WorldCount + "   \u00B7   " + w.Tests.ToUpperInvariant(), "kick", Ui.Bold)),
                    new StarRow(1, 16f, 1), Ui.Text(GreenProgress.TotalIn(w) + " / " + w.Stages.Count * 3, "gl-total", Ui.Bold)),
                Ui.Title(w.Name, "title gl-hero-name"));
            VisualElement map = Ui.Box("gl-hero-art", new GreenMap(w, _picked, s => { _picked = s; _tapped = true; Draw(); }, _wide ? 400f : 260f));
            if (_wide) return Ui.Box("glass gl-hero gl-hero-wide", map, Ui.Box("gl-hero-words", title, GreenWorldScreen.StageCard(_app, w, _picked, Draw)));
            // A phone (Matt, 2026-11-22): the map on top with the world's details over its clear top left corner
            // (Greenlands.Trails keep it empty), then the stage's title, then Play, then what the stage asks.
            VisualElement meta = Ui.Box("gl-hero-meta",
                Ui.Text("WORLD " + w.Num + " OF " + Greenlands.WorldCount, "kick wrap", Ui.Bold),
                Ui.Text(w.Name.ToUpperInvariant(), "gl-hero-meta-name wrap", Ui.Bold),
                Ui.Box("row gl-hero-meta-stars", new StarRow(1, 13f, 1), Ui.Text(GreenProgress.TotalIn(w) + " / " + w.Stages.Count * 3, "gl-total gl-total-small", Ui.Bold)),
                Ui.Box("chips", Ui.Text(_picked.FeetWords, "chip"), Ui.Text(_picked.BagWords, "chip")));
            meta.pickingMode = PickingMode.Ignore;
            map.Add(meta);
            // A stage's extra test goes under the rule: a third chip would take the corner's panel down onto the trail.
            string test = _picked.TestWords;
            return Ui.Box("glass gl-hero",
                map,
                Ui.Box("gl-hero-words gl-hero-phone",
                    GreenWorldScreen.StageStart(_app, w, _picked, Draw),
                    Ui.Text(_picked.Rule, "sub wrap gl-hero-rule", Ui.Regular),
                    test != null ? Ui.Box("chips", Ui.Text(test, "chip")) : null));
        }

        /// <summary>A world as a card: its picture, its name, what it tests, its stars, and whether it is open.</summary>
        VisualElement WorldCard(GreenWorld w)
        {
            bool open = GreenProgress.WorldOpen(w.Num), beaten = GreenProgress.HasReward(w.Num), club = w.Club && !Club.Member(_app.Profile);
            string state = beaten ? "Beaten" : !open ? "Beat " + Greenlands.World(w.Num - 1).Boss.Name + " to open" : club ? "Puttr Club" : "Open";
            // One chip style; colour only as text, for meaning: green once beaten, amber for Puttr Club.
            Label tag = Ui.Text(state, "chip gl-card-state" + (beaten ? " chip-green" : !open ? "" : club ? " chip-amber" : ""), Ui.Bold);
            VisualElement card = Ui.Box("glass gl-worldcard" + (open ? "" : " gl-worldcard-shut"),
                Ui.Box("gl-worldcard-art", new WorldArt(w)),
                Ui.Box("gl-worldcard-words",
                    Ui.Text("WORLD " + w.Num + "   ·   " + w.Tests.ToUpperInvariant(), "kick wrap", Ui.Bold),
                    Ui.Text(w.Name.ToUpperInvariant(), "gl-worldcard-name wrap", Ui.Bold),
                    Ui.Box("row gl-worldcard-stars", new StarRow(1, 13f, 1), Ui.Text(GreenProgress.TotalIn(w) + " / " + w.Stages.Count * 3, "gl-total gl-total-small", Ui.Bold)),
                    Ui.Box("chips", tag)));
            GreenWorld tapped = w;
            return card.OnTap(() => Open(tapped));
        }
    }

    /// <summary>One world: its map, the stage picked and its Play button.</summary>
    public sealed class GreenWorldScreen : AppScreen
    {
        /// <summary>The world screen at the front, if one is: a result goes back to it rather than opening another.</summary>
        public static GreenWorldScreen Front;

        readonly App _app;
        readonly VisualElement _body = new VisualElement();
        GreenWorld _world;
        GreenStage _picked;
        bool _tapped, _wide;

        public GreenWorldScreen(App app, GreenWorld world)
        {
            _app = app;
            _world = world;
            // The header, then the page: on a wide screen the map fills everything under the header; on a phone the
            // map, then the legend under it, in a scroll.
            Root = Ui.Box("gl-worldpage", Ui.Header("Quests", app.Pop), _body);
            _body.AddToClassList("gl-worldbody");
            Draw();
            Root.RegisterCallback<GeometryChangedEvent>(_ => { if (Wide() != _wide) Draw(); });
        }

        bool Wide()
        {
            float w = Root != null ? Root.resolvedStyle.width : float.NaN, h = Root != null ? Root.resolvedStyle.height : float.NaN;
            // Room for the legend in the corner: a wide page that is also tall enough (a phone on its side is not).
            return !float.IsNaN(w) && !float.IsNaN(h) && w >= 900f && h >= 640f;
        }

        public override void Shown()
        {
            Front = this;
            Draw();
            App.Run(GreenProgress.Load(_world, Draw));
        }

        public override void Hidden() { if (Front == this) Front = null; }

        /// <summary>Back from a stage: the world it was in (the next one after a boss), and the stage to play next.</summary>
        public void Show(GreenWorld world) { _world = world; _picked = null; _tapped = false; Draw(); App.Run(GreenProgress.Load(world, Draw)); }

        GreenMap _map;
        VisualElement _legend;

        void Draw()
        {
            GreenWorld world = _world;
            // Until a node is tapped the card follows the stage to play next (the stars arrive after the first draw).
            if (_picked == null || !_tapped || _picked.World != world.Num) _picked = GreenProgress.NextIn(world);
            _body.Clear();
            _map = null; _legend = null;

            // The legend: the world, its stars, and the stage picked with its Play button.
            VisualElement head = Ui.Box("gl-head",
                Ui.Box("row", Ui.Box("grow", Ui.Text("WORLD " + world.Num + " OF " + Greenlands.WorldCount + "   \u00B7   " + world.Tests.ToUpperInvariant(), "kick", Ui.Bold)),
                    new StarRow(1, 16f, 1), Ui.Text(GreenProgress.TotalIn(world) + " / " + world.Stages.Count * 3, "gl-total", Ui.Bold)),
                Ui.Title(world.Name, "title gl-world"));

            _wide = Wide();
            if (_wide)
            {
                // The map is the whole page, and the legend sits over its top left corner: every trail is drawn to leave
                // that corner empty (Greenlands.Trails), so no stage, star pill or marker is under it.
                _map = new GreenMap(world, _picked, Pick, 300f, true);
                _legend = Ui.Box("gl-legend", head, StageCard(_app, world, _picked, Draw));
                _legend.style.width = LegendWide;
                _legend.style.left = LegendInset; _legend.style.top = LegendInset;
                _body.Add(_map);
                _body.Add(_legend);
                return;
            }
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Box("pad", head));
            scroll.Add(Ui.Box("pad", StageStart(_app, world, _picked, Draw)));
            scroll.Add(Ui.Box("pad", new GreenMap(world, _picked, Pick)).Margin(10));
            scroll.Add(Ui.Box("pad", StageMore(_picked)));
            scroll.Add(FreePuttScreen.Space(28));
            _body.Add(scroll);
        }

        const float LegendWide = 340f, LegendInset = 16f;

        void Pick(GreenStage stage) { _picked = stage; _tapped = true; Draw(); }

        /// <summary>A stage as a card: its rule, what it asks, and Play (or why it is shut). Shared with the Quests page's hero.</summary>
        public static VisualElement StageCard(App app, GreenWorld world, GreenStage s, Action redraw)
        {
            VisualElement card = Ui.Box("glass gl-card", StageTitle(s));
            foreach (VisualElement part in StageDetails(s)) card.Add(part);
            card.Add(StageButton(app, world, s, redraw));
            return card;
        }

        /// <summary>A phone: the stage's name and its Play button, to go above the map so Play is never under the fold
        /// (Matt, 2026-11-22). The rule and the chips follow the map (StageMore).</summary>
        public static VisualElement StageStart(App app, GreenWorld world, GreenStage s, Action redraw) => Ui.Box("gl-start", StageTitle(s), StageButton(app, world, s, redraw));

        /// <summary>A phone: what the stage asks, under the map.</summary>
        public static VisualElement StageMore(GreenStage s)
        {
            VisualElement more = Ui.Box("glass gl-card gl-more");
            foreach (VisualElement part in StageDetails(s)) more.Add(part);
            return more;
        }

        static VisualElement StageTitle(GreenStage s)
        {
            int stars = GreenProgress.For(s);
            return Ui.Box("",
                Ui.Box("row",
                    Ui.Box("grow", Ui.Text((s.KindWord + "   \u00B7   " + s.Code).ToUpperInvariant(), "kick", Ui.Bold)),
                    stars > 0 ? new StarRow(stars, 16f) : null),
                Ui.Title(s.Name, "title-20 gl-card-name"));
        }

        /// <summary>A stage's distance, bag and test as chips.</summary>
        public static VisualElement StageChips(GreenStage s)
        {
            string test = s.TestWords;
            return Ui.Box("chips", Ui.Text(s.FeetWords, "chip"), Ui.Text(s.BagWords, "chip"), test != null ? Ui.Text(test, "chip") : null);
        }

        static List<VisualElement> StageDetails(GreenStage s)
        {
            var parts = new List<VisualElement>
            {
                Ui.Text(s.Rule, "sub wrap", Ui.Regular).Margin(4),
                StageChips(s),
            };
            if (GreenProgress.Open(s))
                parts.Add(Ui.Text(s.Kind == StageKind.Bonus
                    ? "Three stars inside " + Greenlands.CenterCut.ToString("0") + "\u00B0 of center, two inside 15\u00B0, one for any make."
                    : "Three stars for no misses, two for one miss, one for any clear.", "muted wrap gl-starline", Ui.Medium));
            return parts;
        }

        /// <summary>Play, or why not: the stage is shut, or the world is for Puttr Club members.</summary>
        static VisualElement StageButton(App app, GreenWorld world, GreenStage s, Action redraw)
        {
            if (!GreenProgress.Open(s)) return Ui.Text(GreenProgress.LockedWhy(s), "sub wrap gl-locked-why", Ui.Medium);
            if (world.Club && !Club.Member(app.Profile))
                return Ui.Box("",
                    Ui.Text("Worlds 2 to 8 are for Puttr Club members.", "sub wrap gl-locked-why", Ui.Medium),
                    Ui.Button("Join Puttr Club", () => Club.Require(app, "Greenlands", redraw)));
            VisualElement play = Ui.Button(GreenProgress.For(s) > 0 ? "Replay " + s.Code : "Play " + s.Code, () => Greenlands.Play(app, s));
            // A TV: the Player is at the green, so a putt is the Play button (TvPutt).
            if (!Tv.On) return play;
            TvPutt.Does(play, () => () => Greenlands.Play(app, s));
            return Ui.Box("", play, TvPutt.Hint("Or hit a putt to play"));
        }
    }

    /// <summary>A world's picture for a card: its land, its strip and its trail, each stage a dot (green once cleared,
    /// white while open, dark while shut). Painted like GreenMap, with nothing to tap.</summary>
    public sealed class WorldArt : VisualElement
    {
        const float W = 1000f, H = 520f;
        readonly GreenWorld _world;
        readonly GreenMap.Paints _paints;

        public WorldArt(GreenWorld world)
        {
            _world = world;
            _paints = GreenMap.PaintsFor(world.Num);
            AddToClassList("gl-art");
            style.backgroundColor = _paints.Land;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Paint;
        }

        void Paint(MeshGenerationContext context)
        {
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;
            // The whole map, cropped to fill the card's shape.
            float s = Mathf.Max(r.width / W, r.height / H);
            Vector2 shift = new Vector2((r.width - W * s) * 0.5f, (r.height - H * s) * 0.5f);
            Vector2 P(Vector2 v) => v * s + shift;
            Painter2D p = context.painter2D;
            void Dot(Vector2 at, float radius, Color colour) { p.fillColor = colour; p.BeginPath(); p.Arc(P(at), radius * s, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill(); }

            Dot(new Vector2(230, 230), 150, _paints.Patch);
            Dot(new Vector2(700, 420), 170, _paints.Patch);
            Dot(new Vector2(860, 190), 120, _paints.Patch);
            p.strokeColor = _paints.Strip; p.lineWidth = 30f * s; p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(P(new Vector2(352, -20)));
            p.BezierCurveTo(P(new Vector2(392, 120)), P(new Vector2(282, 230)), P(new Vector2(322, 330)));
            p.BezierCurveTo(P(new Vector2(362, 420)), P(new Vector2(302, 470)), P(new Vector2(332, 540)));
            p.Stroke();

            bool light = _paints.Land.r * 0.3f + _paints.Land.g * 0.59f + _paints.Land.b * 0.11f > 0.62f;
            Color trail = light ? (Color)new Color32(0x1E, 0x21, 0x24, 0xFF) : (Color)new Color32(0xF3, 0xE8, 0xBE, 0xFF), ink = new Color32(0x09, 0x0B, 0x0D, 0xFF);
            List<GreenStage> path = _world.Path;
            for (int i = 1; i < path.Count; i++)
            {
                float length = Vector2.Distance(path[i - 1].At, path[i].At);
                for (float d = 14f; d < length; d += 28f) Dot(Vector2.Lerp(path[i - 1].At, path[i].At, d / length), 6f, trail);
            }
            foreach (GreenStage stage in path)
            {
                bool boss = stage.Kind == StageKind.Boss;
                Dot(stage.At, boss ? 34f : 26f, ink);
                Dot(stage.At, boss ? 27f : 20f, GreenProgress.Cleared(stage) && GreenProgress.WorldOpen(stage.World) ? (Color)Ui.Bright : boss ? (Color)Ui.Red : GreenProgress.Open(stage) ? Color.white : (Color)new Color32(0x20, 0x23, 0x25, 0xFF));
            }
        }
    }

    /// <summary>A world from above. Painted in the map's own 1000 x 520 space, scaled to the element.</summary>
    public sealed class GreenMap : VisualElement
    {
        const float W = 1000f, H = 520f;
        static readonly Color Cream = new Color32(0xF3, 0xE8, 0xBE, 0xFF), Ink = new Color32(0x1E, 0x21, 0x24, 0xFF), Plank = new Color32(0x8A, 0x5A, 0x2B, 0xFF);
        static readonly Vector2 Start = new Vector2(-10, 300);
        static readonly Vector2[] Blobs = { new Vector2(70, 60), new Vector2(98, 82), new Vector2(190, 482), new Vector2(430, 40), new Vector2(456, 60), new Vector2(700, 486), new Vector2(728, 468), new Vector2(620, 60), new Vector2(880, 470), new Vector2(40, 470) };

        /// <summary>A world's colours on the map: the land, the lighter patches on it, the strip that crosses it
        /// (a river, a ridge, a lava flow), and the dots that stand for its trees or rocks.</summary>
        public struct Paints { public Color Land, Patch, Strip, Blob; }

        public static Paints PaintsFor(int world)
        {
            Color C(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xFF);
            switch (world)
            {
                case 2: return new Paints { Land = C(0x3A9A4A), Patch = C(0x4BB25C), Strip = C(0x4AA8FF), Blob = C(0x226F30) };
                case 3: return new Paints { Land = C(0xD9B866), Patch = C(0xE6CB85), Strip = C(0xC79A45), Blob = C(0x6E9A4A) };
                case 4: return new Paints { Land = C(0xDDE8F2), Patch = C(0xF2F7FB), Strip = C(0x8FCBEA), Blob = C(0x2F6B5A) };
                case 5: return new Paints { Land = C(0x8A4A42), Patch = C(0xA35B4F), Strip = C(0x4B2F3B), Blob = C(0x5B2F3A) };
                case 6: return new Paints { Land = C(0x2A2020), Patch = C(0x3A2A28), Strip = C(0xFF6A2B), Blob = C(0x171212) };
                case 7: return new Paints { Land = C(0x7CC0F2), Patch = C(0xFFFFFF), Strip = C(0xB9DCF7), Blob = C(0xFFFFFF) };
                case 8: return new Paints { Land = C(0x2A2A3A), Patch = C(0x36364A), Strip = C(0x6A4AB8), Blob = C(0x1A1A26) };
                default: return new Paints { Land = C(0x2F8F43), Patch = C(0x3DA552), Strip = C(0x4AA8FF), Blob = C(0x1F6A2C) };
            }
        }

        static bool Light(Color c) => c.r * 0.3f + c.g * 0.59f + c.b * 0.11f > 0.62f;

        readonly List<(VisualElement spot, VisualElement node, Label word, VisualElement extra)> _spots = new List<(VisualElement, VisualElement, Label, VisualElement)>();
        readonly GreenWorld _world;
        readonly Paints _paints;
        readonly Vector2 _gate;
        float _laid, _laidHigh;
        readonly float _minHeight;
        readonly bool _fill;   // the map takes the height it is given (a full page) instead of setting its own

        /// <summary>Room kept clear on the left for a legend over the map: the trail is laid out to the right of it.</summary>
        readonly float _inset;
        readonly List<float> _xs = new List<float>();   // each spot's place across the map, in map units

        public GreenMap(GreenWorld world, GreenStage picked, Action<GreenStage> pick, float minHeight = 300f, bool fill = false, float insetLeft = 0f)
        {
            _world = world;
            _minHeight = minHeight;
            _fill = fill;
            _inset = insetLeft;
            if (fill) AddToClassList("gl-map-fill");
            _paints = PaintsFor(world.Num);
            AddToClassList("gl-map");
            style.backgroundColor = _paints.Land;
            generateVisualContent += Paint;
            GreenStage next = GreenProgress.NextIn(world);
            int n = 0;
            foreach (GreenStage s in world.Stages)
            {
                n++;
                bool open = GreenProgress.Open(s), done = GreenProgress.Cleared(s) && GreenProgress.WorldOpen(s.World);
                // The secret green is off the map until its route opens: a question mark on a dotted trail.
                string word = s.Kind == StageKind.Boss ? "B" : s.Kind == StageKind.Bonus ? (open ? "S" : "?") : n.ToString();
                Label label = Ui.Text(open || s.Kind == StageKind.Bonus ? word : "", "gl-node-word", Ui.Bold);
                VisualElement node = Ui.Box("gl-node " + (done ? "gl-node-done" : open ? "gl-node-open" : "gl-node-shut"), label);
                if (s.Kind == StageKind.Gauntlet) { node.AddToClassList("gl-node-diamond"); node.style.rotate = new Rotate(45f); label.style.rotate = new Rotate(-45f); }
                else if (s.Kind == StageKind.Gate) node.AddToClassList("gl-node-square");
                else if (s.Kind == StageKind.Boss) node.AddToClassList("gl-node-boss");
                else if (s.Kind == StageKind.Bonus) node.AddToClassList("gl-node-secret");
                if (s == picked) { node.AddToClassList("gl-node-picked"); node.AddToClassList("tv-path-entry"); }
                if (!open && s.Kind != StageKind.Bonus) node.Add(Ui.Box("gl-lock"));
                VisualElement extra = null;
                if (done) extra = Ui.Box("gl-pill", new StarRow(GreenProgress.For(s), 11f));
                else if (s == next && open) extra = Ui.Box("gl-pin", Ui.Box("gl-pin-ball"));
                VisualElement spot = Ui.Box("gl-spot", node);
                if (extra != null) spot.Add(extra);
                _xs.Add(s.At.x);
                spot.style.top = Length.Percent(s.At.y / H * 100f);
                GreenStage tapped = s;
                // A TV: OK on the stage already picked plays it (the first OK picks it, the second goes).
                node.OnTap(() => { if (Tv.On && tapped == picked && open && App.I != null) Greenlands.Play(App.I, tapped); else pick(tapped); });
                Add(spot);
                _spots.Add((spot, node, label, extra));
            }
            // A TV's remote follows the trail: right is the next stage, left the one before, and the secret green
            // is up (or down) from the stage its route leaves from.
            if (Tv.On)
            {
                var trail = new List<VisualElement>();
                foreach (GreenStage s in world.Path) { int at = world.Stages.IndexOf(s); if (at >= 0) trail.Add(_spots[at].node); }
                Tv.Path(trail);
                int key = world.Stages.IndexOf(world.SecretKey), secret = world.Stages.IndexOf(world.Secret);
                if (key >= 0 && secret >= 0 && key != secret && !world.Path.Contains(world.Secret)) Tv.Branch(_spots[key].node, _spots[secret].node, world.Secret.At.y < world.SecretKey.At.y);
            }
            // The way on to the next world, shut until this one's boss is beaten. The last world has none.
            Vector2 boss = world.Boss.At;
            _gate = new Vector2(Mathf.Min(boss.x + 54f, 946f), Mathf.Clamp(boss.y + (boss.y > 300f ? -66f : 66f), 60f, 460f));
            if (world.Num < Greenlands.WorldCount)
            {
                bool through = GreenProgress.HasReward(world.Num);
                Label word = Ui.Text("WORLD " + (world.Num + 1), "gl-gate-word", Ui.Bold);
                word.style.color = Light(_paints.Land) ? Ink : (Color)Color.white;
                VisualElement gate = Ui.Box("gl-spot", Ui.Box("gl-node gl-node-world " + (through ? "gl-node-done" : "gl-node-shut"), through ? null : Ui.Box("gl-lock")), word);
                _xs.Add(_gate.x);
                gate.style.top = Length.Percent(_gate.y / H * 100f);
                Add(gate);
                _spots.Add((gate, gate[0], null, gate[1]));
            }
            RegisterCallback<GeometryChangedEvent>(_ => Lay());
        }

        // The map keeps its shape on a wide screen and stands taller on a phone, where its nodes shrink to fit.
        void Lay()
        {
            float w = resolvedStyle.width, high = resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0f) return;
            if (Mathf.Abs(w - _laid) < 0.5f && (!_fill || Mathf.Abs(high - _laidHigh) < 0.5f)) return;
            _laid = w; _laidHigh = high;
            if (!_fill) style.height = Mathf.Clamp(w * H / W, _minHeight, 560f);
            // On a full page the nodes are sized by whichever side is the tighter.
            float across = _fill && !float.IsNaN(high) && high > 0f ? Mathf.Min(w, high * W / H) : w;
            float size = Mathf.Clamp(across * 0.058f, 30f, 58f);
            for (int i = 0; i < _spots.Count; i++) _spots[i].spot.style.left = _inset + _xs[i] / W * (w - _inset);
            foreach (var s in _spots)
            {
                bool boss = s.node.ClassListContains("gl-node-boss"), small = s.node.ClassListContains("gl-node-world") || s.node.ClassListContains("gl-node-secret");
                float d = boss ? size * 1.25f : small ? size * 0.8f : size;
                s.node.style.width = d; s.node.style.height = d;
                s.node.style.left = -d / 2f; s.node.style.top = -d / 2f;
                if (s.word != null) s.word.style.fontSize = d * 0.44f;
                if (s.extra == null) continue;
                if (s.extra.ClassListContains("gl-pin")) { s.extra.style.left = -size * 0.28f; s.extra.style.top = -d / 2f - size * 0.72f; s.extra.style.width = size * 0.56f; s.extra.style.height = size * 0.56f; }
                else if (s.extra.ClassListContains("gl-pill")) { s.extra.style.left = -24f; s.extra.style.top = d / 2f + 3f; }
                else { s.extra.style.left = -66f; s.extra.style.top = d / 2f + 4f; }
            }
            MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext context)
        {
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;
            float sx = (r.width - _inset) / W, sy = r.height / H, s = Mathf.Min(sx, sy);
            Vector2 P(Vector2 v) => new Vector2(_inset + v.x * sx, v.y * sy);
            Painter2D p = context.painter2D;

            void Dot(Vector2 at, float radius, Color colour)
            {
                p.fillColor = colour;
                p.BeginPath(); p.Arc(P(at), radius * s, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
            }
            void Dots(Vector2 from, Vector2 to, float gap, float radius, Color colour)
            {
                float length = Vector2.Distance(from, to);
                for (float d = gap * 0.5f; d < length; d += gap) Dot(Vector2.Lerp(from, to, d / length), radius, colour);
            }

            Dot(new Vector2(230, 230), 150, _paints.Patch);
            Dot(new Vector2(700, 420), 170, _paints.Patch);
            Dot(new Vector2(860, 190), 120, _paints.Patch);
            // The next world's land shows at the right edge.
            if (_world.Num < Greenlands.WorldCount)
            {
                p.fillColor = PaintsFor(_world.Num + 1).Land;
                p.BeginPath(); p.MoveTo(P(new Vector2(1000, 280))); p.BezierCurveTo(P(new Vector2(962, 370)), P(new Vector2(986, 466)), P(new Vector2(942, 520))); p.LineTo(P(new Vector2(1000, 520))); p.ClosePath(); p.Fill();
            }

            // The strip that crosses the land: a river, a ridge, a flow of lava.
            p.strokeColor = _paints.Strip; p.lineWidth = 30f * s; p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(P(new Vector2(352, -20)));
            p.BezierCurveTo(P(new Vector2(392, 120)), P(new Vector2(282, 230)), P(new Vector2(322, 330)));
            p.BezierCurveTo(P(new Vector2(362, 420)), P(new Vector2(302, 470)), P(new Vector2(332, 540)));
            p.Stroke();

            foreach (Vector2 blob in Blobs) Dot(blob, 15, _paints.Blob);

            // A bridge where the trail crosses the strip, between the second and third stages.
            List<GreenStage> path = _world.Path;
            Vector2 a = path[1].At, b = path[2].At, dir = (b - a).normalized, across = new Vector2(-dir.y, dir.x);
            // Where that leg meets the strip: the strip runs near x = 322 through the middle of the map.
            float t = Mathf.Abs(b.x - a.x) > 1f ? Mathf.Clamp01((322f - a.x) / (b.x - a.x)) : 0.5f;
            Vector2 mid = Vector2.Lerp(a, b, t);
            p.fillColor = Plank;
            p.BeginPath();
            p.MoveTo(P(mid - dir * 30 - across * 13)); p.LineTo(P(mid + dir * 30 - across * 13)); p.LineTo(P(mid + dir * 30 + across * 13)); p.LineTo(P(mid - dir * 30 + across * 13));
            p.ClosePath(); p.Fill();

            // The trail: walked where the stage it leads to is open, faint where it is still shut. Dark on light land.
            bool light = Light(_paints.Land);
            Color walked = light ? Ink : Cream, shut = Color.Lerp(_paints.Land, light ? Color.gray : Color.black, 0.4f);
            Vector2 from = new Vector2(Start.x, path[0].At.y + 40f);
            foreach (GreenStage stage in path)
            {
                Dots(from, stage.At, 20, 5.5f, GreenProgress.Open(stage) ? walked : shut);
                from = stage.At;
            }
            if (_world.Num < Greenlands.WorldCount) Dots(from, _gate, 20, 5.5f, GreenProgress.HasReward(_world.Num) ? walked : shut);
            Dots(_world.SecretKey.At, _world.Secret.At, 15, 3.2f, GreenProgress.Open(_world.Secret) ? (Color)Ui.Amber : shut);
        }
    }

    public sealed class StageResultScreen : AppScreen
    {
        public StageResultScreen(App app, StageGame game, int markers, List<GreenStage> opened, bool reward, bool real)
        {
            GreenStage s = game.Stage;
            GreenWorld world = Greenlands.WorldOf(s);
            bool boss = s.Kind == StageKind.Boss;
            // The stage's own block under its name: the stars and what it opened, or how the bag ran out.
            VisualElement own = Ui.Box("gl-result");
            if (game.Won)
            {
                own.Add(Ui.Box("gl-result-stars", new StarRow(game.Stars, 54f)));
                own.Add(Ui.Text(s.Kind == StageKind.Bonus ? (game.Stars == 3 ? "Top of the stick" : game.Stars == 2 ? "Halfway up" : "On the stick")
                    : game.Misses == 0 ? "No ball lost" : game.Misses == 1 ? "One ball lost" : game.Misses + " balls lost", "body-2 center", Ui.Medium));

                VisualElement list = Ui.Box("card gl-result-list");
                if (markers > 0) list.Add(Row("MARKERS", "+" + markers, "Center cuts"));
                if (reward) list.Add(Row("REWARD", world.Reward, world.RewardNote));
                // What opened in this world; the next world is its own line.
                foreach (GreenStage o in opened.Where(o => o.World == s.World))
                    list.Add(Row(o.Kind == StageKind.Bonus ? "SECRET ROUTE" : o.Kind == StageKind.Boss ? "BOSS" : "OPENED", o.Code + " " + o.Name, o.KindWord));
                if (boss && s.World < Greenlands.WorldCount)
                {
                    GreenWorld next = Greenlands.World(s.World + 1);
                    list.Add(Row("WORLD " + next.Num, next.Name, Club.Member(app.Profile) ? "Open: " + next.Tests.ToLowerInvariant() : "For Puttr Club members"));
                }
                if (list.childCount > 0) { list[list.childCount - 1].style.borderBottomWidth = 0; own.Add(list.Margin(16)); }
            }
            else own.Add(Ui.Text(boss ? s.Name + " wins this one. " + game.Makes + " of " + s.Goal + " hits." : s.InARow ? "The run needs " + s.Goal + " in a row." : "Made " + game.Makes + " of " + s.Goal + ".", "body-2 center", Ui.Medium));

            // The shared game over page: the stage's name, its block, then what next, the rating and the putts.
            GreenStage after = game.Won ? NextAfter(s) : null;
            var actions = new List<(string, Action, bool)>();
            if (after != null) actions.Add(("Play " + after.Code + " " + after.Name, () => Again(app, after), true));
            actions.Add((game.Won ? "Replay" : "Try again", () => Again(app, s), after == null));
            actions.Add(("Map", () => ToMap(app, game.Won && boss && s.World < Greenlands.WorldCount ? s.World + 1 : s.World), false));
            var spec = new GameOverSpec
            {
                Eyebrow = (game.Won ? (boss ? "Boss beaten" : "Stage clear") : "Out of balls") + "   ·   " + s.Code,
                Outcome = s.Name,
                Extra = own,
                RateAs = "Greenlands " + s.Code + " " + s.Name,
                XpUnpaid = real && game.Won,   // the stars were just sent: the XP panel asks what they paid
                Actions = actions,
                Exit = () => ToMap(app, s.World),
                Note = real || Fixtures.Connected ? null : "Test putts: this is kept until you close the app, and saved nowhere.",
                ActionsHint = Tv.On ? "Or hit a putt to " + (after != null ? "play " + after.Code : game.Won ? "replay" : "try again") : null,
                Putts = new List<GameOverPutt>(),
            };
            int n = 0;
            foreach (Shot shot in game.Shots)
            {
                n++;
                spec.Putts.Add(new GameOverPutt { Number = n.ToString(), From = GameOver.From(shot.Feet, shot.Tee), Call = game.FinalCall(shot), Points = game.FinalPoints(shot), Good = shot.Good });
            }
            Root = GameOver.Build(app, spec);
            // A TV: a putt goes on, to the next stage after a clear, to this one again after an empty bag.
            if (Tv.On) TvPutt.Does(Root, () => actions[0].Item2);
        }

        /// <summary>The stage after this one, if it is open now: the next on the path, or the first of the next world
        /// after a boss.</summary>
        static GreenStage NextAfter(GreenStage s)
        {
            GreenWorld world = Greenlands.WorldOf(s);
            List<GreenStage> path = world.Path;
            int at = path.IndexOf(s);
            if (at < 0) return null;
            GreenStage next = at + 1 < path.Count ? path[at + 1] : s.World < Greenlands.WorldCount ? Greenlands.World(s.World + 1).Path[0] : null;
            return next != null && GreenProgress.Open(next) ? next : null;
        }

        static VisualElement Row(string kick, string value, string note) => Ui.Box("board-row",
            Ui.Box("grow", Ui.Text(kick, "kick", Ui.Bold), Ui.Text(note, "muted wrap", Ui.Medium)),
            Ui.Text(value, "body gl-row-v", Ui.Bold));

        /// <summary>Back to the world's map: the one the stage was played from if it is there, else a new one over the
        /// Quests page (a stage played from the hero card).</summary>
        static void ToMap(App app, int world)
        {
            app.Pop();
            GreenlandsScreen.Live?.Refresh();
            GreenWorld to = Greenlands.World(world);
            if (GreenWorldScreen.Front != null) GreenWorldScreen.Front.Show(to);
            else app.Push(new GreenWorldScreen(app, to));
        }

        static void Again(App app, GreenStage stage)
        {
            app.Pop();
            GreenlandsScreen.Live?.Refresh();
            if (GreenWorldScreen.Front != null) GreenWorldScreen.Front.Show(Greenlands.WorldOf(stage));
            App.Run(After(() => Greenlands.Play(app, stage)));
        }

        static IEnumerator After(Action act) { yield return null; act(); }
    }
}
