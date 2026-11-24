// The Putt Quality Score's explainer (members/PuttScore.js helpModal), shared by every PQS card: the Improve tab's ring
// and another Player's profile. The legacy wording, with its bold phrases in light green as the app's Markdown draws
// them, and a small picture under it: the 65 / 35 split as two bars, and what the Make index and Accuracy tiles mean.
// The legacy text says "0-100", but a Player above Tour average scores over 100 (110 has been seen), so it says that.
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class PqsHelp
    {
        // PuttScore.js draws **strong** in lightGreen (constants/Colors.js #7ad446).
        const string Green = "#7AD446";
        static readonly Color Track = new Color32(0x24, 0x28, 0x2A, 0xFF);

        static string Hi(string words) => "<color=" + Green + ">" + words + "</color>";

        /// <summary>The "?" beside a PQS card's title: what the score is and how it is worked out.</summary>
        public static void Explain(App app) => Explain(app.Toasts);

        public static void Explain(VisualElement over)
        {
            VisualElement dim = null;
            // The text is long for a phone held sideways: it scrolls inside the card, which never runs off the screen.
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic,
            };
            scroll.style.flexShrink = 1;
            scroll.Add(Para(Hi("PQS (Putting Quality Score)") + " is a simple score that shows how well you're putting overall. " + Hi("100 = PGA Tour average") + "; great putters score above 100.", 0));
            scroll.Add(Para("It combines:", 10));
            scroll.Add(Para("•  " + Hi("How often you make putts") + " (compared to PGA Tour averages for each distance)", 6));
            scroll.Add(Para("•  " + Hi("How dead center the ball enters the hole") + " on putts you make", 4));

            scroll.Add(Para("How it's calculated:", 12));
            scroll.Add(Bar("Make Performance", "Make index", 65, Ui.Bright).Margin(8));
            scroll.Add(Para("Your make rate at each distance, normalized so 100 = PGA Tour average. Above the Tour's make rate, it goes over 100.", 4, "muted"));
            scroll.Add(Bar("Putt Entry", "Accuracy", 35, Ui.Blue).Margin(10));
            scroll.Add(Para("How accurate your made putts are, based on how close to center they enter the hole. 100 = every make dead center.", 4, "muted"));

            scroll.Add(Para("Missed putts affect your make performance, but only made putts are used for putt entry.", 12));
            scroll.Add(Para("The big number is your " + Hi("last " + PqsModel.HeroDays + " days") + ". Each point on the chart is a practice day with " + PqsModel.MinPutts +
                "+ putts; longer ranges roll days up into weeks or months. Your score is " + Hi("provisional") + " until you've hit " + PqsModel.Calibration + " putts.", 10));

            VisualElement card = Ui.Box("modal", scroll, Ui.Button("Got it", () => dim.RemoveFromHierarchy()).Margin(14));
            card.style.maxHeight = Length.Percent(92);
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
        }

        static Label Para(string text, float top, string classes = "sub")
        {
            Label l = Ui.Text(text, classes, Ui.Regular);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = top;
            if (classes == "sub") l.style.color = Color.white;
            return l;
        }

        /// <summary>One part of the score: its name and share, the tile it shows as, and a bar filled to its share.</summary>
        static VisualElement Bar(string part, string tile, int percent, Color colour)
        {
            VisualElement dot = Ui.Box("");
            dot.style.width = dot.style.height = 8;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 4;
            dot.style.backgroundColor = colour;
            dot.style.marginRight = 6;

            Label name = Ui.Text(Hi(part) + "  <color=#8C8D8E>·  the " + tile + " tile</color>", "sub", Ui.Medium);
            name.style.flexGrow = 1;
            name.style.flexShrink = 1;
            Label share = Ui.Text(percent + "%", "sub", Ui.Bold);
            share.style.color = colour;
            VisualElement head = Ui.Box("", dot, name, share);
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;

            VisualElement fill = Ui.Box("");
            fill.style.width = Length.Percent(percent);
            fill.style.height = Length.Percent(100);
            fill.style.backgroundColor = colour;
            fill.style.borderTopLeftRadius = fill.style.borderBottomLeftRadius = fill.style.borderTopRightRadius = fill.style.borderBottomRightRadius = 4;
            VisualElement track = Ui.Box("", fill);
            track.style.height = 8;
            track.style.marginTop = 6;
            track.style.backgroundColor = Track;
            track.style.borderTopLeftRadius = track.style.borderTopRightRadius = track.style.borderBottomLeftRadius = track.style.borderBottomRightRadius = 4;
            track.style.overflow = Overflow.Hidden;

            return Ui.Box("", head, track);
        }
    }
}
