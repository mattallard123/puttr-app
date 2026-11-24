// Placeholders while a screen's data loads: flat grey shapes (surface-2, no pulse, no sheen) where its cards will be, so the page has its real
// shape from the first frame (and scrolls) instead of an empty screen and a spinner. Skeleton.Hold puts one in a
// section's slot; it goes as soon as the section draws anything else (or after 15 seconds, so an empty result never
// leaves a placeholder behind). Skeleton.Page is a whole page of them, for App.Swap's guard on screens with nothing yet.
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Skeleton
    {
        /// <summary>A bar: width as a share of its row (0..1), height in units.</summary>
        public static VisualElement Bar(float share, float height = 12f)
        {
            VisualElement b = Ui.Box("sk-bar");
            b.style.width = Length.Percent(share * 100f);
            b.style.height = height;
            return b;
        }

        public static VisualElement Circle(float size)
        {
            VisualElement c = Ui.Box("sk-bar");
            c.style.width = c.style.height = size;
            // An avatar is a rounded square (radius 4, the class's own); only a dot of 14 or less stays round.
            if (size <= 14f) c.style.borderTopLeftRadius = c.style.borderTopRightRadius = c.style.borderBottomLeftRadius = c.style.borderBottomRightRadius = size / 2f;
            c.style.flexShrink = 0;
            return c;
        }

        /// <summary>A card-shaped placeholder: a title bar and a few lines, about this tall.</summary>
        public static VisualElement Card(float height, bool title = true)
        {
            VisualElement card = Ui.Box("card sk-card");
            card.style.minHeight = height;
            if (title) card.Add(Bar(0.4f, 14f));
            card.Add(Bar(0.85f).Margin(12));
            card.Add(Bar(0.6f).Margin(8));
            return card;
        }

        /// <summary>A section heading and a card, the shape most sections have.</summary>
        public static VisualElement Section(float height) => Ui.Box("pad", Bar(0.35f, 16f), Card(height, false).Margin(12));

        /// <summary>A row of small cards that would scroll sideways (challenges, achievements).</summary>
        public static VisualElement Strip(float height, int count = 4)
        {
            VisualElement row = Ui.Box("row sk-strip");
            for (int i = 0; i < count; i++)
            {
                VisualElement c = Card(height);
                c.style.width = 170; c.style.marginRight = 12; c.style.flexShrink = 0;
                row.Add(c);
            }
            return Ui.Box("pad", Bar(0.3f, 16f), row.Margin(12));
        }

        /// <summary>A Player header: the photo, the name and two lines, and a row of numbers.</summary>
        public static VisualElement Header()
        {
            VisualElement nums = Ui.Box("row sk-nums");
            for (int i = 0; i < 3; i++) nums.Add(Ui.Box("grow sk-num", Bar(0.5f, 20f), Bar(0.7f, 10f).Margin(8)));
            return Ui.Box("sk-header", Ui.Box("row", Circle(72), Ui.Box("grow sk-lines", Bar(0.6f, 20f), Bar(0.45f).Margin(8), Bar(0.3f).Margin(8))), nums.Margin(16));
        }

        /// <summary>Puts this placeholder in the slot until the slot draws something of its own.</summary>
        public static void Hold(VisualElement slot, VisualElement placeholder)
        {
            if (slot == null || placeholder == null) return;
            if (slot.childCount > 0) return;   // already drawn (cached data)
            placeholder.AddToClassList("sk");
            slot.Add(placeholder);
            float since = Time.realtimeSinceStartup;
            placeholder.schedule.Execute(() =>
            {
                bool other = false;
                foreach (VisualElement child in slot.Children()) if (child != placeholder) { other = true; break; }
                if (other || Time.realtimeSinceStartup - since > 15f) placeholder.RemoveFromHierarchy();
            }).Every(120).Until(() => placeholder.parent == null);
        }

        /// <summary>A page of placeholders (a heading, a big card, a strip, two cards), for a screen that has nothing yet.</summary>
        public static VisualElement Page()
        {
            VisualElement page = Ui.Box("sk-page", Ui.Box("pad", Bar(0.45f, 26f)), Section(170), Strip(110), Section(130), Section(130));
            page.AddToClassList("sk");
            return page;
        }

        /// <summary>
        /// The placeholder for a picture that is still on its way (a course still being painted): the host is a flat
        /// surface-2 block until it comes. No band of light sweeps it (docs/design-guidelines.md: no sheen, no
        /// shimmer). Call the returned stop when the picture is in; calling it twice does no harm.
        /// </summary>
        public static System.Action Sheen(VisualElement host)
        {
            host.AddToClassList("sk-art");
            void Stop() { host.RemoveFromClassList("sk-art"); }
            return Stop;
        }
    }
}
