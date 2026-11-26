// The game card, as Puttr Arcade draws it: the game's own picture filling the card (Resources/PuttrApp/Cards/<TYPE>,
// the arcade's 3D renders or docs/cards/make_cards.py), dark at the bottom, the name and what to know over it.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class GameCards
    {
        static readonly Dictionary<string, Texture2D> Known = new Dictionary<string, Texture2D>();
        static Texture2D _fade;

        /// <summary>The picture for a game: its type's card, else the putt-list card its kind plays like.</summary>
        /// <summary>A game a Player made (the game creator), not one of Puttr's.</summary>
        public static bool Custom(object game) => J.Flag(game, "custom");

        /// <summary>Marks rows from GET /creator/games/ as the Player's own games (a recent game row carries the
        /// Player's userId too, so that cannot tell them apart).</summary>
        public static List<object> MarkCustom(List<object> games)
        {
            foreach (object g in games) if (g is Dictionary<string, object> row) row["custom"] = true;
            return games;
        }

        public static Texture2D Picture(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            if (type == "ONFIRE") type = "ARCADESTREAK";
            if (type == "OPEN") type = "FREEPUTT";
            // A Player's own game (GET /creator/games/, marked by MarkCustom) is putted on the Puttr, as Practice is:
            // it shows the Puttr, whatever its type (Matt: not the neon target).
            if (Custom(game)) type = "FREEPUTT";
            // A putt-list game with no scene of its own (a tournament round such as Watch It Drop, the Puttr standard and
            // streak games, any type without a card) is putted on the Puttr too: it shows the Puttr, never the generic
            // neon target (Matt, 2026-11-24). A recent game row is not marked custom, so the type decides.
            if (type == "STREAK" || type == "PRACTICE" || type == "STANDARD" || type == "") type = "FREEPUTT";
            if (Known.TryGetValue(type, out Texture2D t)) return t;
            t = Resources.Load<Texture2D>("PuttrApp/Cards/" + type) ?? Resources.Load<Texture2D>("PuttrApp/Cards/FREEPUTT");
            Known[type] = t;
            return t;
        }

        /// <summary>Clear at the top, near black at the bottom, so words over a picture can be read.</summary>
        public static Texture2D Fade => FadeTexture();
        static Texture2D FadeTexture()
        {
            if (_fade != null) return _fade;
            _fade = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
            {
                float k = Mathf.Clamp01(1f - y / 44f); // y 0 is the bottom
                _fade.SetPixel(0, y, new Color(0.03f, 0.035f, 0.04f, Mathf.SmoothStep(0f, 0.95f, k)));
            }
            _fade.Apply();
            return _fade;
        }

        /// <summary>
        /// The raised card every game card is: the footprint (.gcard .gcard-3d, so the Play grid sizes it and Home's
        /// first-card margin lands on it) holding a shadow, the lip (the card's thickness) and the face on top, 8 px
        /// shorter, so the card stands off the page. The face comes back with its picture layer (art) under shade.
        /// </summary>
        public static VisualElement Raised(string classes, Texture2D shade, out VisualElement face, out VisualElement art)
        {
            VisualElement card = Ui.Box("gcard gcard-3d " + classes);
            card.Add(Layer("gcard-shadow"));
            card.Add(Layer("gcard-lip"));
            face = Ui.Box("gcard-face");
            art = Layer("gcard-art");
            face.Add(art);
            VisualElement over = Layer("gcard-fade");
            over.style.backgroundImage = new StyleBackground(shade);
            face.Add(over);
            card.Add(face);
            return card;
        }

        /// <summary>
        /// A game's tile (docs/design-guidelines.md, Game tile): the picture in a window on top, then a solid bar with
        /// the name, a line (your best) and the tags as plain caps text. Nothing is written over the picture and the
        /// tile has no raised edge. big is the Play list's wide tile; the small one (Jump back in) has no tags.
        /// </summary>
        public static VisualElement Make(object game, string line, bool lineLit, bool big, params string[] chips)
        {
            VisualElement card = Ui.Box(big ? "gcard gtile gcard-big gtile-big" : "gcard gtile");
            VisualElement art = Layer("gtile-art");
            card.Add(art);
            if (J.Str(game, "gameType").ToUpperInvariant() == "TRACKS")
            {
                // A Tracks card is its course (recentGames gives the course slug as courseId), painted by TracksArt: a
                // loading sheen until the still is in, never the generic Tracks picture (Matt: no green map pin).
                System.Action stop = Skeleton.Sheen(art);
                string slug = J.Str(game, "courseId");
                if (slug.Length > 0)
                    TracksArt.Show(card, new Dictionary<string, object> { { "slug", slug } }, TracksArt.CardW, TracksArt.CardH,
                        still => { art.style.backgroundImage = new StyleBackground(still.Tex); stop(); });
            }
            else
            {
                Texture2D picture = Picture(game);
                if (picture != null) art.style.backgroundImage = new StyleBackground(picture);
            }

            VisualElement bar = Layer("gtile-bar");
            Label name = Ui.Text(J.Str(game, "title", "Game").ToUpperInvariant(), big ? "gtile-name gtile-name-big ital" : "gtile-name ital", Ui.Bold);
            Label l = null;
            if (!string.IsNullOrEmpty(line))
            {
                l = Ui.Text(line, big ? "gtile-line gtile-line-big" : "gtile-line", lineLit ? Ui.Bold : Ui.Medium);
                if (lineLit) l.style.color = Ui.Bright;
            }
            VisualElement meter = Meter(game, big);
            string tags = "";
            foreach (string c in chips) if (!string.IsNullOrEmpty(c)) tags += (tags.Length > 0 ? "  \u00b7  " : "") + c.ToUpperInvariant();
            if (big)
            {
                // Name left, your best right; under them the tags, with the difficulty meter at the right.
                bar.Add(Ui.Box("gtile-row", name, l));
                VisualElement under = Ui.Box("gtile-row gtile-under");
                under.Add(Ui.Text(tags, "gtile-tags", Ui.Bold));
                if (meter != null) under.Add(meter);
                if (tags.Length > 0 || meter != null) bar.Add(under);
            }
            else
            {
                bar.Add(name);
                VisualElement under = Ui.Box("gtile-row");
                if (l != null) under.Add(l);
                if (meter != null) under.Add(meter);
                if (l != null || meter != null) bar.Add(under);
            }
            card.Add(bar);
            Press(card);
            return card;
        }

        static VisualElement Layer(string cls)
        {
            VisualElement e = Ui.Box(cls);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        static Texture2D _cardShade;

        /// <summary>
        /// The game card's shade: the top half of the picture left as it is, then a smooth fade to near black at the
        /// bottom so the name reads. Black over a picture only darkens it, so it blends cleanly in linear colour.
        /// </summary>
        public static Texture2D CardShade
        {
            get
            {
                if (_cardShade != null) return _cardShade;
                const int n = 128;
                var tex = new Texture2D(1, n, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "gcard-fade" };
                var px = new Color32[n];
                for (int y = 0; y < n; y++)
                {
                    float t = 1f - y / (n - 1f);                   // texture row 0 is the bottom of the card: t = 1 there
                    float a = Mathf.Lerp(0.04f, 0.9f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, t)));
                    px[y] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                return _cardShade = tex;
            }
        }


        /// <summary>
        /// A small press: the face sinks onto the card's lip while a finger is on the card (.gcard-pressed in
        /// App.uss). It clears on lift, on leaving, on cancel and once the finger moves far enough to be a scroll.
        /// </summary>
        static void Press(VisualElement card)
        {
            Vector3 from = Vector3.zero;
            void Off() => card.RemoveFromClassList("gcard-pressed");
            card.RegisterCallback<PointerDownEvent>(e => { from = e.position; card.AddToClassList("gcard-pressed"); }, TrickleDown.TrickleDown);
            card.RegisterCallback<PointerMoveEvent>(e => { if ((e.position - from).sqrMagnitude > 64f) Off(); }, TrickleDown.TrickleDown);
            card.RegisterCallback<PointerUpEvent>(e => Off(), TrickleDown.TrickleDown);
            card.RegisterCallback<PointerLeaveEvent>(e => Off());
            card.RegisterCallback<PointerCancelEvent>(e => Off(), TrickleDown.TrickleDown);
            card.RegisterCallback<PointerCaptureOutEvent>(e => Off());
        }

        static readonly Color MeterOff = new Color32(48, 53, 58, 255);   // surface-3

        /// <summary>
        /// The app's three-bar difficulty meter (common/GameTile.js): one bar lit for a rating up to 3, two up to 6,
        /// three above, all in the level's colour (green, amber, red). It sits at the right of the tile's bar, level
        /// with the tags. Null for a row with no rating
        /// (the Tracks card on Home), so a game nobody rated is not shown as an easy one.
        /// </summary>
        public static VisualElement Meter(object game, bool big)
        {
            if (!GameLevels.Rated(game)) return null;
            int lit = GameLevels.Bars(GameLevels.Rating(game));
            Color colour = lit == 1 ? Ui.Bright : lit == 2 ? Ui.Amber : Ui.Red;
            var meter = new VisualElement { pickingMode = PickingMode.Ignore };
            meter.style.flexDirection = FlexDirection.Row;
            meter.style.flexShrink = 0;
            meter.style.marginLeft = 8;
            for (int i = 1; i <= 3; i++)
            {
                var bar = new VisualElement();
                bar.style.width = big ? 16 : 12;
                bar.style.height = big ? 4 : 3;
                bar.style.marginLeft = 2;
                bar.style.borderTopLeftRadius = bar.style.borderTopRightRadius = bar.style.borderBottomLeftRadius = bar.style.borderBottomRightRadius = 1;
                bar.style.backgroundColor = i <= lit ? colour : MeterOff;
                meter.Add(bar);
            }
            return meter;
        }
    }
}
