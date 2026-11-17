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
        public static Texture2D Picture(object game)
        {
            string type = J.Str(game, "gameType").ToUpperInvariant();
            if (type == "ONFIRE") type = "ARCADESTREAK";
            if (type == "OPEN") type = "FREEPUTT";
            if (type == "STREAK" || type == "PRACTICE" || type == "") type = "STANDARD";
            if (Known.TryGetValue(type, out Texture2D t)) return t;
            t = Resources.Load<Texture2D>("PuttrApp/Cards/" + type) ?? Resources.Load<Texture2D>("PuttrApp/Cards/STANDARD");
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

        /// <summary>A card: the picture, chips top right, the name and a line bottom left. big is the Play list's wide card.</summary>
        public static VisualElement Make(object game, string line, bool lineLit, bool big, params string[] chips)
        {
            VisualElement card = Ui.Box(big ? "gcard gcard-big" : "gcard");
            card.style.backgroundImage = new StyleBackground(Picture(game));
            var shade = new Image { image = FadeTexture(), scaleMode = ScaleMode.StretchToFill };
            shade.AddToClassList("gcard-shade");
            shade.pickingMode = PickingMode.Ignore;
            card.Add(shade);
            VisualElement top = Ui.Box("gcard-top");
            foreach (string c in chips) if (!string.IsNullOrEmpty(c)) top.Add(Ui.Text(c, "gcard-chip", Ui.Bold));
            card.Add(top);
            Label name = Ui.Text(J.Str(game, "title", "Game").ToUpperInvariant(), big ? "gcard-name gcard-name-big" : "gcard-name", Ui.Bold);
            VisualElement bottom = Ui.Box("gcard-bottom", name);
            if (!string.IsNullOrEmpty(line))
            {
                Label l = Ui.Text(line, "gcard-line", Ui.Medium);
                if (lineLit) l.style.color = Ui.Bright;
                bottom.Add(l);
            }
            card.Add(bottom);
            return card;
        }
    }
}
