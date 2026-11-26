// The few building blocks every screen is made of. Layout and colour live in
// Resources/PuttrApp/App.uss; fonts are set here because a font is the one thing that is
// simpler to hand to an element than to name in a style sheet.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Ui
    {
        public static readonly Color Green = new Color32(0x2D, 0x8C, 0x3C, 0xFF);
        public static readonly Color Bright = new Color32(0x7A, 0xD4, 0x46, 0xFF);
        public static readonly Color Amber = new Color32(0xFF, 0xB3, 0x00, 0xFF);
        public static readonly Color Red = new Color32(0xFF, 0x52, 0x52, 0xFF);
        public static readonly Color Blue = new Color32(0x56, 0x87, 0xFF, 0xFF);
        public static readonly Color Fire = new Color32(0xFF, 0x7A, 0x3A, 0xFF);
        public static readonly Color Ink = new Color32(0x09, 0x0B, 0x0D, 0xFF);
        public static readonly Color Faint = new Color32(139, 147, 153, 255);   // text-2 in docs/design-guidelines.md
        // The rest of the guidelines' colour table, for anything drawn in code. Never invent another grey or green.
        public static readonly Color Base = new Color32(10, 12, 13, 255);
        public static readonly Color Surface = new Color32(21, 24, 27, 255);
        public static readonly Color Surface2 = new Color32(34, 38, 42, 255);
        public static readonly Color Surface3 = new Color32(48, 53, 58, 255);
        public static readonly Color Line = new Color32(38, 42, 46, 255);
        public static readonly Color Soft = new Color32(196, 201, 205, 255);
        public static readonly Color Gold = new Color32(255, 210, 74, 255);

        public static Font Bold, Medium, Regular;

        public static void Init()
        {
            Bold = Resources.Load<Font>("PuttrArcade/Fonts/Montserrat-Bold");
            Medium = Resources.Load<Font>("PuttrArcade/Fonts/Montserrat-Medium");
            Regular = Resources.Load<Font>("PuttrArcade/Fonts/Montserrat-Regular");
        }

        public static T Face<T>(this T element, Font font) where T : VisualElement
        {
            if (font != null) element.style.unityFontDefinition = FontDefinition.FromFont(font);
            return element;
        }

        public static VisualElement Box(string classes, params VisualElement[] children)
        {
            var box = new VisualElement();
            AddClasses(box, classes);
            foreach (VisualElement child in children) if (child != null) box.Add(child);
            return box;
        }

        /// <summary>Text. Bold for headings and numbers, medium for labels, regular for body copy.</summary>
        public static Label Text(string text, string classes, Font font = null)
        {
            // A chip is always in caps (docs/design-guidelines.md), wherever it is worded.
            string c = " " + classes + " ";
            if (text != null && (c.Contains(" chip ") || c.Contains(" chip-join "))) text = text.ToUpperInvariant();
            var label = new Label(text ?? "");
            AddClasses(label, classes);
            return label.Face(font ?? Medium);
        }

        /// <summary>
        /// The main button (classes "btn": brand green, slanted ends, caps) or the secondary one ("btn btn-ghost").
        /// At most one main button on a screen (docs/design-guidelines.md).
        /// </summary>
        public static VisualElement Button(string text, Action onClick, string classes = "btn", string icon = null)
        {
            // "btn-white" is the Apple / Google sign-in button: the same slanted shape in white, black words, the
            // provider's mark before them (icon).
            bool slant = (" " + classes + " ").Contains(" btn ");
            bool white = (" " + classes + " ").Contains(" btn-white ");
            VisualElement button = slant ? new Slant() : new VisualElement();
            AddClasses(button, classes);
            if (slant) button.AddToClassList("slant");
            Label label = Text(slant ? (text ?? "").ToUpperInvariant() : text, "btn-text", Bold);
            if (icon != null)
            {
                Image mark = Icon(icon, 20, white ? Color.black : Color.white);
                mark.style.marginRight = 8;
                button.Add(Box("row", mark, label));
            }
            else button.Add(label);
            button.AddManipulator(new Clickable(() => { Perf.Wake(); onClick?.Invoke(); }));
            Tv.Tappable(button, onClick);
            return button;
        }

        /// <summary>
        /// A sheet's dark backdrop: closes only on a tap that starts and ends on the backdrop itself. (A Clickable on
        /// the backdrop also fired for taps in the sheet that no child took, a text field's included: the sheet closed
        /// as the keyboard came up and the typing was lost.)
        /// </summary>
        public static T OnBackdropTap<T>(this T dim, Action close) where T : VisualElement
        {
            bool down = false;
            dim.RegisterCallback<PointerDownEvent>(e => down = e.target == dim);
            dim.RegisterCallback<PointerUpEvent>(e => { if (down && e.target == dim) { Perf.Wake(); close?.Invoke(); } down = false; });
            return dim;
        }

        public static T OnTap<T>(this T element, Action onClick) where T : VisualElement
        {
            element.AddManipulator(new Clickable(() => { Perf.Wake(); onClick?.Invoke(); }));
            Tv.Tappable(element, onClick);
            return element;
        }

        /// <summary>One of the Phosphor icons the Arcade ships (Resources/PuttrArcade/Icons/Icon*.png).</summary>
        public static Image Icon(string name, float size, Color tint)
        {
            var image = new Image { image = Resources.Load<Texture2D>("PuttrArcade/Icons/Icon" + name), tintColor = tint, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = size;
            image.style.height = size;
            return image;
        }

        /// <summary>A rounded square with an icon in it, in the game's colour.</summary>
        public static VisualElement Bubble(string icon, Color accent)
        {
            VisualElement bubble = Box("bubble", Icon(icon, 22, accent));
            bubble.style.backgroundColor = Color.Lerp(Ink, accent, 0.2f); // solid: see the note at the top of App.uss
            return bubble;
        }

        /// <summary>A Player's photo, or their initial until it loads (or if they have none).</summary>
        static readonly Color[] BrandColours =
        {
            new Color32(0x2D, 0x8C, 0x3C, 0xFF), new Color32(0x03, 0x6B, 0x36, 0xFF), new Color32(0x7A, 0xD4, 0x46, 0xFF), new Color32(0x1B, 0x6B, 0x2A, 0xFF),
            new Color32(0x4C, 0xAF, 0x50, 0xFF), new Color32(0x2E, 0x7D, 0x32, 0xFF), new Color32(0x38, 0x8E, 0x3C, 0xFF), new Color32(0x43, 0xA0, 0x47, 0xFF),
        };

        /// <summary>Avatar.js getColorForName: the same JavaScript string hash, so a name gets the same colour in both apps.</summary>
        public static Color ColourFor(string name)
        {
            long h = 0;
            foreach (char c in name) h = c + ((long)unchecked((int)h << 5)) - h;
            return BrandColours[(int)(Math.Abs(h) % BrandColours.Length)];
        }

        public static VisualElement Avatar(string name, string photoUrl, bool big = false)
        {
            string letter = string.IsNullOrEmpty(name) ? "P" : name.Trim().Substring(0, 1).ToUpperInvariant();
            Label initial = Text(letter, "avatar-letter", Bold);
            VisualElement avatar = Box(big ? "avatar avatar-big" : "avatar", initial);
            Images.Cover(avatar);   // a photo fills the circle, centred and cropped, never stretched
            // No photo: the letter on a brand green picked from the name, as puttr-app's Avatar.js.
            avatar.style.backgroundColor = string.IsNullOrEmpty(name) ? BrandColours[0] : ColourFor(name);
            initial.style.color = Color.white;
            if (!string.IsNullOrEmpty(photoUrl))
                Images.Load(photoUrl, texture =>
                {
                    if (texture == null) return;
                    avatar.style.backgroundImage = new StyleBackground(texture);
                    avatar.style.backgroundColor = Surface2;   // the letter's green would show at the rounded corners behind the photo
                    initial.style.display = DisplayStyle.None;
                    Perf.Wake(0.3f);
                });
            return avatar;
        }

        /// <summary>A clean button with an icon and a label (Chat with your coach, History, New chat).</summary>
        public static VisualElement IconButton(string icon, string label, Action onTap, bool primary = false)
        {
            VisualElement b = Box(primary ? "ibtn ibtn-primary" : "ibtn", Icon(icon, 16, Color.white), Text(label, "ibtn-t", Medium));
            return b.OnTap(onTap);
        }

        public static ScrollView Scroll(bool horizontal = false)
        {
            var scroll = new ScrollView(horizontal ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic,
            };
            scroll.AddToClassList(horizontal ? "hscroll" : "screen");
            // A page column: centred and at most 1000 wide when there is room (App.Layout .wide).
            if (!horizontal) { scroll.contentViewport.AddToClassList("page-viewport"); scroll.contentContainer.AddToClassList("page-column"); }
            return scroll;
        }

        public static VisualElement Secondary(string text, Action onClick) => Button(text, onClick, "btn btn-ghost");

        /// <summary>A title: screen names, hero titles, game names. Bold italic caps.</summary>
        public static Label Title(string text, string classes = "title") => Text((text ?? "").ToUpperInvariant(), classes + " ital", Bold);

        /// <summary>A label: section names, eyebrows, tags, stat captions. Wide-spaced caps.</summary>
        public static Label Caps(string text, string classes = "caps") => Text((text ?? "").ToUpperInvariant(), classes, Bold);

        /// <summary>A score or a stat.</summary>
        public static Label Num(string text, string classes = "num") => Text(text, classes + " ital", Bold);

        /// <summary>The one header every tab has: its name (or the logo) on the left, up to two actions on the right.</summary>
        public static VisualElement TabHeader(VisualElement left, params VisualElement[] actions)
        {
            VisualElement right = Box("tab-head-actions");
            foreach (VisualElement action in actions) if (action != null) right.Add(action);
            return Box("tab-head", left, right);
        }

        public static VisualElement TabHeader(string title, params VisualElement[] actions) => TabHeader(Title(title, "tab-head-title"), actions);

        /// <summary>A 44 point square icon button for a header.</summary>
        public static VisualElement IconSquare(string icon, Action onTap) => Box("icon-sq", Icon(icon, 22, Color.white)).OnTap(onTap);

        /// <summary>One bordered block of stats, split by dividers: a number over its caption.</summary>
        public static VisualElement StatStrip(params (string value, string caption)[] cells)
        {
            VisualElement strip = Box("strip");
            for (int i = 0; i < cells.Length; i++)
                strip.Add(Box(i == 0 ? "strip-cell strip-cell-first" : "strip-cell", Num(cells[i].value, "strip-num"), Caps(cells[i].caption, "strip-cap")));
            return strip;
        }

        /// <summary>A section's name: a green slash, the name in caps, and a quiet link on the right.</summary>
        public static VisualElement Section(string title, string link = null, Action onLink = null)
        {
            VisualElement row = Box("sec", Box("row", Text("/", "sec-slash ital", Bold), Text((title ?? "").ToUpperInvariant(), "sec-title", Bold)));
            if (link != null) row.Add(Text(link, "sec-link").OnTap(onLink));
            return row;
        }

        /// <summary>The bar at the top of a pushed screen: back, and what the screen is.</summary>
        public static VisualElement Header(string title, Action onBack, string backLabel = null)
        {
            // A chevron alone (Matt, 2026-11-16: no "Back" word); a label only where a screen asks for one.
            Label back = Text(backLabel == null ? "\u2039" : "\u2039  " + backLabel, backLabel == null ? "back back-chevron" : "back", Medium).OnTap(onBack);
            VisualElement top = Box("top", back, Text(title, "top-title", Bold), Box("back-balance"));
            // A screen that draws itself again by clearing its scroll and adding a new header (after joining a
            // challenge, picking a league team) would show two, since the first was pinned above the scroll
            // (App.PinHeader): the new one takes the pinned one's place instead.
            top.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                VisualElement page = top.parent?.GetFirstAncestorOfType<ScrollView>()?.parent;
                if (page == null || !page.ClassListContains("pinned-page") || top.parent.ClassListContains("pinned-top")) return;
                VisualElement bar = page.Q(className: "pinned-top");
                if (bar == null) return;
                top.style.display = DisplayStyle.None;   // not seen as a second header for the frame before it moves
                top.schedule.Execute(() =>
                {
                    top.RemoveFromHierarchy();
                    top.AddToClassList("page-column");
                    top.style.display = DisplayStyle.Flex;
                    bar.Clear();
                    bar.Add(top);
                });
            });
            return top;
        }

        public static VisualElement Progress(float share)
        {
            VisualElement fill = Box("bar-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100f);
            return Box("bar", fill);
        }

        static void AddClasses(VisualElement element, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (string name in classes.Split(' ')) if (name.Length > 0) element.AddToClassList(name);
        }
    }

    /// <summary>
    /// A button with slanted ends. Its colour is the style sheet's --fill, so hover, pressed and the grey secondary
    /// are all set in App.uss; with no --fill it draws nothing and the plain background shows.
    /// </summary>
    public sealed class Slant : VisualElement
    {
        static readonly CustomStyleProperty<Color> FillProperty = new CustomStyleProperty<Color>("--fill");
        Color _fill;
        bool _has;

        public Slant()
        {
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e => { _has = e.customStyle.TryGetValue(FillProperty, out _fill); MarkDirtyRepaint(); });
            // A TV: the remote's focus is a white edge that follows the slant (a square border would show only at its corners).
            if (Tv.On)
            {
                RegisterCallback<FocusInEvent>(_ => { _focused = true; MarkDirtyRepaint(); });
                RegisterCallback<FocusOutEvent>(_ => { _focused = false; MarkDirtyRepaint(); });
            }
        }

        bool _focused;

        void Draw(MeshGenerationContext context)
        {
            float w = layout.width, h = layout.height;
            if (!_has || w <= 0f || h <= 0f) return;
            float k = Mathf.Min(10f, w * 0.2f);
            Painter2D p = context.painter2D;
            p.fillColor = _fill;
            p.BeginPath();
            p.MoveTo(new Vector2(k, 0)); p.LineTo(new Vector2(w, 0)); p.LineTo(new Vector2(w - k, h)); p.LineTo(new Vector2(0, h));
            p.ClosePath();
            p.Fill();
            if (!_focused) return;
            p.strokeColor = Color.white; p.lineWidth = 3f; p.lineJoin = LineJoin.Miter;
            p.BeginPath();
            p.MoveTo(new Vector2(k + 1.5f, 1.5f)); p.LineTo(new Vector2(w - 2f, 1.5f)); p.LineTo(new Vector2(w - k - 1.5f, h - 1.5f)); p.LineTo(new Vector2(2f, h - 1.5f));
            p.ClosePath();
            p.Stroke();
        }
    }

    /// <summary>The four tab icons, drawn as strokes on a 24 unit grid so they stay sharp at any size.</summary>
    public sealed class TabIcon : VisualElement
    {
        readonly string _kind;
        Color _tint = Ui.Faint;

        public TabIcon(string kind)
        {
            _kind = kind;
            AddToClassList("tab-icon");
            generateVisualContent += Draw;
        }

        public void SetTint(Color tint) { _tint = tint; MarkDirtyRepaint(); }

        void Draw(MeshGenerationContext context)
        {
            float s = contentRect.width / 24f;
            if (s <= 0f) return;
            Painter2D p = context.painter2D;
            p.strokeColor = _tint;
            p.lineWidth = 1.9f * s;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            Vector2 P(float x, float y) => new Vector2(x * s, y * s);
            void Line(params float[] xy)
            {
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                p.Stroke();
            }
            switch (_kind)
            {
                case "home":
                    p.BeginPath();
                    p.MoveTo(P(3, 11)); p.LineTo(P(12, 3)); p.LineTo(P(21, 11)); p.LineTo(P(21, 20.5f)); p.LineTo(P(15, 20.5f));
                    p.LineTo(P(15, 14)); p.LineTo(P(9, 14)); p.LineTo(P(9, 20.5f)); p.LineTo(P(3, 20.5f)); p.ClosePath();
                    p.Stroke();
                    break;
                case "play":
                    p.BeginPath();
                    p.MoveTo(P(7.5f, 7)); p.LineTo(P(16.5f, 7));
                    p.Arc(P(16.5f, 12.5f), 5.5f * s, Angle.Degrees(-90), Angle.Degrees(90));
                    p.LineTo(P(7.5f, 18));
                    p.Arc(P(7.5f, 12.5f), 5.5f * s, Angle.Degrees(90), Angle.Degrees(270));
                    p.ClosePath();
                    p.Stroke();
                    Line(7.5f, 10.8f, 7.5f, 14.2f);
                    Line(5.8f, 12.5f, 9.2f, 12.5f);
                    Line(15.2f, 13.6f, 15.25f, 13.6f);
                    Line(17.6f, 11.4f, 17.65f, 11.4f);
                    break;
                case "improve":
                    Line(3, 20.5f, 21, 20.5f);
                    Line(4, 16, 9, 11, 13, 14, 20, 6);
                    Line(16, 6, 20, 6, 20, 10);
                    break;
                default: // profile
                    p.BeginPath(); p.Arc(P(12, 12), 9f * s, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
                    p.BeginPath(); p.Arc(P(12, 10), 3.2f * s, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
                    p.BeginPath(); p.Arc(P(12, 21.5f), 6.5f * s, Angle.Degrees(212), Angle.Degrees(328)); p.Stroke();
                    break;
            }
        }
    }
}
