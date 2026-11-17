// Putter brands: the brand mark next to a Player's name on the league and tournament leaderboards,
// and the sheet that opens when it is tapped (who they are and what they game). A port of
// puttr-app's components/common/putterBrands.js, PutterBrandBadge.js and PutterDetailModal.js.
//
// Marks maps whatever string is stored in user_putters.brand (Players have typed "Ping", "PING",
// "LAB", "L.A.B. Golf", "Sik Golf"...) to a white mark in Resources/PuttrApp/Brands (rasterised
// from puttr-app/assets/images/brands, 256 tall), with its width/height ratio so wordmarks are laid
// out without measuring. Titleist -> Scotty Cameron and Callaway / Toulon -> Odyssey are parent-brand
// aliases. Order matters: L.A.B. before LA GOLF, and PING is anchored so "Pinnacle" never matches.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class PutterBrands
    {
        /// <summary>The brand chips, in the order puttr-app offers them (user_putters.brand spellings).</summary>
        public static readonly string[] Names =
        {
            "Scotty Cameron", "L.A.B. Golf", "TaylorMade", "Odyssey", "PING", "Bettinardi", "PXG", "EVNROLL", "Cleveland", "Cobra", "Edel",
            "LA GOLF", "Mizuno", "Performance Golf", "SeeMore", "Sik Golf", "Sub 70", "Swag", "Top Flite", "US Kids Golf", "Vice", "Wilson", "Other",
        };

        public sealed class Mark
        {
            public string Name, File;
            public Regex Re;
            public float Ratio, Scale = 1f, MaxRatio;
        }

        static Mark M(string name, string pattern, string file, float ratio, float scale = 1f, float maxRatio = 0f) =>
            new Mark { Name = name, Re = new Regex(pattern, RegexOptions.IgnoreCase), File = file, Ratio = ratio, Scale = scale, MaxRatio = maxRatio };

        // `Scale` equalises ink (solid marks carry 2-3x the ink of the thin LAB ring in the same box);
        // `MaxRatio` lets a pure wordmark run wider than the default cap. Same numbers as puttr-app.
        public static readonly Mark[] Marks =
        {
            M("Scotty Cameron",   @"scotty|cameron|titleist",  "ScottyCameron",   515f / 308f),
            M("Odyssey",          @"odyssey|callaway|toulon",  "Odyssey",         50.8f / 47.3f, 0.86f),
            M("TaylorMade",       @"taylor\s*made",            "TaylorMade",      202.9f / 195.3f, 0.74f),
            M("PING",             @"^\s*ping\b",               "Ping",            539f / 688f, 0.9f),
            M("Bettinardi",       @"bettinardi",               "Bettinardi",      465f / 404f, 0.84f),
            M("L.A.B. Golf",      @"\bL\.?A\.?B\.?\b",         "Lab",             50f / 49f),
            M("PXG",              @"pxg|parsons",              "PXG",             129.2f / 56.9f, 0.94f),
            M("EVNROLL",          @"evn\s*roll",               "Evnroll",         1f, 0.72f),
            M("Cleveland",        @"cleveland",                "Cleveland",       224.7f / 253.8f, 0.72f),
            M("Cobra",            @"cobra",                    "Cobra",           668f / 443f, 0.9f),
            M("Edel",             @"\bedel\b",                 "Edel",            1882f / 991f),
            M("LA GOLF",          @"\bla\s*golf\b|^\s*la\s*$", "LAGolf",          570f / 339f, 0.78f),
            M("Mizuno",           @"mizuno",                   "Mizuno",          289f / 111.8f),
            M("Performance Golf", @"performance\s*golf",       "PerformanceGolf", 88f / 32f, 0.9f),
            M("SeeMore",          @"see\s*more",               "SeeMore",         624.6f / 624.4f, 0.82f),
            M("Sik Golf",         @"\bsik\b",                  "Sik",             875f / 533f),
            M("Sub 70",           @"sub\s*70",                 "Sub70",           369f / 380f, 0.72f),
            M("Swag",             @"\bswag\b",                 "Swag",            1576f / 481f, 1f, 2.5f),
            M("Top Flite",        @"top[\s-]*flite",           "TopFlite",        2279.41f / 427.505f, 1f, 2.5f),
            M("US Kids Golf",     @"u\.?\s?s\.?\s*kids",       "USKidsGolf",      458f / 249f, 0.82f),
            M("Vice",             @"\bvice\b",                 "Vice",            427.5f / 292.8f),
            M("Wilson",           @"wilson",                   "Wilson",          258f / 202.5f, 0.72f),
        };

        const float DefaultMaxRatio = 1.5f;
        static readonly Dictionary<string, Texture2D> Art = new Dictionary<string, Texture2D>();

        /// <summary>True when a stored brand names an actual brand: not empty, not a bare "Other".</summary>
        public static bool Has(string brand)
        {
            if (string.IsNullOrWhiteSpace(brand)) return false;
            return !string.Equals(brand.Trim(), "other", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The mark for a stored brand string, or null when there is none for it.</summary>
        public static Mark Find(string brand)
        {
            if (string.IsNullOrWhiteSpace(brand)) return null;
            string raw = brand.Trim();
            foreach (Mark mark in Marks) if (mark.Re.IsMatch(raw)) return mark;
            return null;
        }

        static string Clean(object v)
        {
            if (v == null) return null;
            string s = v is double d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : v is bool b ? (b ? "true" : "false") : v as string ?? v.ToString();
            return s == "" || s == "null" || s == "undefined" ? null : s;
        }

        /// <summary>A raw profile.putterData (JSON string or object) as {brand, model, length, head, shaft, grip}, or null when empty.</summary>
        public static Dictionary<string, object> ParsePutter(object raw)
        {
            if (raw == null) return null;
            object obj = raw is string s ? J.Parse(s) : raw;
            if (!(obj is Dictionary<string, object> map)) return null;
            var putter = new Dictionary<string, object>();
            foreach (string key in new[] { "brand", "model", "length", "head", "shaft", "grip" })
                putter[key] = Clean(J.Get(map, key));
            return putter.Values.Any(v => v != null) ? putter : null;
        }

        /// <summary>
        /// Who a leaderboard row is, for the sheet. League rows nest the Player under `player` (or, in
        /// older payloads, a JSON string in `userName`) with `putter` and `putterBrand`; tournament rows are
        /// flat with `putter` and the raw `putterData`.
        /// </summary>
        public sealed class Player
        {
            public string Name, Photo, HomeTown, Brand;
            public Dictionary<string, object> Putter;
        }

        public static Player Resolve(object row)
        {
            object p = J.Get(row, "player");
            if (!(p is Dictionary<string, object>))
            {
                object userName = J.Get(row, "userName");
                p = userName is string s ? J.Parse(s) : userName;
                if (!(p is Dictionary<string, object>)) p = row;
            }
            Dictionary<string, object> putter = ParsePutter(J.Get(p, "putter")) ?? ParsePutter(J.Get(p, "putterData"))
                ?? (p != row ? ParsePutter(J.Get(row, "putter")) ?? ParsePutter(J.Get(row, "putterData")) : null);
            string brand = Clean(J.Get(putter, "brand")) ?? Clean(J.Get(p, "putterBrand")) ?? Clean(J.Get(row, "putterBrand"));
            return new Player
            {
                Name = J.Str(p, "displayName", J.Str(p, "username", J.Str(p, "firstName", "Player"))),
                Photo = J.Str(p, "photoURL", J.Str(p, "photoUrl", J.Str(p, "photo"))),
                HomeTown = J.Str(p, "homeTown"),
                Brand = brand,
                Putter = putter,
            };
        }

        static Texture2D Load(string file)
        {
            if (!Art.TryGetValue(file, out Texture2D tex)) Art[file] = tex = Resources.Load<Texture2D>("PuttrApp/Brands/" + file);
            return tex;
        }

        /// <summary>
        /// The brand's mark, `size` tall. Width follows the mark's ratio, capped at maxRatio x size
        /// (leaderboards pass 1 so nothing is wider than the LAB square). A brand with no mark gets a
        /// ring with its first letter when monogram is set; otherwise, and for "Other" or nothing, null.
        /// </summary>
        public static VisualElement Badge(string brand, float size = 32f, float maxRatio = 0f, bool monogram = false)
        {
            Mark mark = Find(brand);
            Texture2D tex = mark != null ? Load(mark.File) : null;
            if (tex == null)
            {
                if (!monogram || !Has(brand)) return null;
                float ring = size * 0.86f, stroke = Mathf.Max(1.5f, size * 0.06f);
                Label letter = Ui.Text(brand.Trim().Substring(0, 1).ToUpperInvariant(), "brand-letter", Ui.Bold);
                letter.style.fontSize = ring * 0.5f;
                VisualElement circle = Ui.Box("brand-ring", letter);
                circle.style.width = circle.style.height = ring;
                circle.style.borderTopLeftRadius = circle.style.borderTopRightRadius = circle.style.borderBottomLeftRadius = circle.style.borderBottomRightRadius = ring / 2f;
                circle.style.borderTopWidth = circle.style.borderBottomWidth = circle.style.borderLeftWidth = circle.style.borderRightWidth = stroke;
                VisualElement holder = Ui.Box("brand-badge", circle);
                holder.style.height = size;
                return holder;
            }
            float ratio = mark.Ratio > 0f ? mark.Ratio : 1f, box = size * mark.Scale;
            float cap = maxRatio > 0f ? maxRatio : mark.MaxRatio > 0f ? mark.MaxRatio : DefaultMaxRatio;
            float width, height;
            if (ratio >= 1f) { width = Mathf.Min(ratio, cap) * box; height = width / ratio; }
            else { height = box; width = box * ratio; }
            // Stretch, not fit: the importer rounds a texture to a power of two, so its own aspect is not the mark's.
            var image = new Image { image = tex, scaleMode = ScaleMode.StretchToFill };
            image.AddToClassList("brand-mark");
            image.style.width = width;
            image.style.height = height;
            VisualElement badge = Ui.Box("brand-badge", image);
            badge.style.height = size;
            return badge;
        }

        /// <summary>
        /// The tappable mark for a leaderboard row: the Player's brand, opening their putter sheet. Null
        /// when they have no brand on file, so the row stays clean. A tap here does not open the row.
        /// </summary>
        public static VisualElement RowBadge(object row, float size = 32f)
        {
            Player player = Resolve(row);
            if (!Has(player.Brand)) return null;
            VisualElement badge = Badge(player.Brand, size, 1f, true);
            if (badge == null) return null;
            VisualElement hit = Ui.Box("brand-hit", badge);
            hit.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            hit.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            hit.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            return hit.OnTap(() => Show(hit, player));
        }

        /// <summary>The putter sheet, from the bottom over a dimmed page; a tap on the dim closes it.</summary>
        public static void Show(VisualElement from, Player p)
        {
            Dictionary<string, object> putter = p.Putter;
            string brand = Clean(J.Get(putter, "brand")) ?? (Has(p.Brand) ? p.Brand : null);
            string model = Clean(J.Get(putter, "model"));
            string length = Clean(J.Get(putter, "length"));
            var specs = new List<string>();
            if (length != null) specs.Add(length + "\"");
            foreach (string key in new[] { "head", "shaft", "grip" }) { string v = Clean(J.Get(putter, key)); if (v != null) specs.Add(v); }

            VisualElement dim = null;
            Action close = () => dim?.RemoveFromHierarchy();

            VisualElement who = Ui.Box("grow putter-sheet-who",
                Ui.Text("GAMER", "putter-sheet-kick", Ui.Regular),
                Ui.Text(string.IsNullOrEmpty(p.Name) ? "Player" : p.Name, "putter-sheet-name", Ui.Bold));
            if (!string.IsNullOrEmpty(p.HomeTown)) who.Add(Ui.Text(p.HomeTown, "putter-sheet-town", Ui.Regular));
            VisualElement shut = Ui.Box("putter-sheet-close", Ui.Icon("XCircle", 20, new Color32(181, 181, 181, 255))).OnTap(close);

            VisualElement disc = Ui.Box("putter-sheet-disc", brand != null ? Badge(brand, 40f, 1f, true) : Ui.Text("?", "putter-sheet-unknown", Ui.Bold));
            VisualElement words = Ui.Box("grow putter-sheet-words", Ui.Text(brand ?? "No putter on file", "putter-sheet-brand", Ui.Bold));
            if (model != null) words.Add(Ui.Text(model, "putter-sheet-model", Ui.Medium));
            if (specs.Count > 0) words.Add(Ui.Text(string.Join(" · ", specs).ToUpperInvariant(), "putter-sheet-specs", Ui.Regular));

            VisualElement sheet = Ui.Box("putter-sheet",
                Ui.Box("putter-sheet-grab-row", Ui.Box("putter-sheet-grab")),
                Ui.Box("putter-sheet-head", Ui.Avatar(p.Name, p.Photo), who, shut),
                Ui.Box("putter-sheet-card", disc, words));
            float inset = App.I != null ? App.I.SafeInsets().y : 0f;
            sheet.style.paddingBottom = 20f + inset;
            sheet.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            sheet.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());

            dim = Ui.Box("putter-sheet-dim", sheet);
            // Close on the click (down and up on the dim), so the release never lands on the page beneath.
            dim.RegisterCallback<ClickEvent>(e => { if (e.target == dim) { e.StopPropagation(); close(); } });
            App.Layer(from).Add(dim);

            // Slide up from the bottom, as the RN Modal's animationType="slide".
            sheet.style.translate = new Translate(0, Length.Percent(100));
            sheet.schedule.Execute(() => { sheet.style.translate = new Translate(0, 0); Perf.Wake(0.5f); }).ExecuteLater(20);
            Perf.Wake(0.6f);
        }
    }
}
