// Cards more than one screen shows.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Cards
    {
        /// <summary>
        /// The active challenges as a row of cards, from GET /challenges/userChallenges
        /// ({challenge, challengeLevels, userResults}). Joined ones carry their progress.
        /// </summary>
        public static void Challenges(App app, VisualElement slot, List<object> rows, bool joinedFirst)
        {
            slot.Clear();
            if (rows.Count == 0)
            {
                if (joinedFirst) return; // Home says nothing when there is nothing; Profile says so
                slot.Add(Ui.Section("Challenges"));
                slot.Add(Ui.Box("pad", Ui.Box("card", Ui.Text("No active challenges right now.", "body-2", Ui.Regular))));
                return;
            }
            if (joinedFirst)
            {
                rows = new List<object>(rows);
                rows.Sort((a, b) => Joined(b).CompareTo(Joined(a)));
            }
            slot.Add(Ui.Section("Challenges", "See all", () => app.Push(new ChallengesScreen(app))));
            ScrollView row = Ui.Scroll(true);
            for (int i = 0; i < rows.Count; i++)
            {
                object challenge = J.Get(rows[i], "challenge");
                List<object> levels = J.List(J.Get(rows[i], "challengeLevels"));
                bool joined = Joined(rows[i]) == 1;
                double score = J.Get(rows[i], "userResults") != null ? J.Num(rows[i], "userResults") : J.Num(challenge, "currentScore");
                int percent = ChallengeMath.Percent(score, levels, out _, out _);
                ProfileScreen.Look(J.Str(challenge, "cType"), out string icon, out _);
                // The icon and the title in a row; the title wraps to two lines and never runs off the card.
                // Flat (docs/design-guidelines.md): a plain stroke icon (no tinted tile), the time left in amber caps,
                // a 4 px bar, and Join as the quiet action.
                VisualElement card = Ui.Box("glass challenge",
                    Ui.Box("row challenge-head", Ui.Icon(Stroke(icon), 24, Color.white), Ui.Text(J.Str(challenge, "title", "Challenge"), "tile-title challenge-title grow", Ui.Bold)),
                    Ui.Caps(ProfileMath.TimeLeft(ProfileMath.Date(challenge, "endDate"), DateTime.UtcNow), "caps challenge-time"));
                if (joined && levels.Count > 0)
                {
                    VisualElement bar = Ui.Progress(percent / 100f);
                    bar.AddToClassList("challenge-bar");
                    card.Add(bar);
                    card.Add(Ui.Caps(percent + "%", "caps challenge-pct"));
                }
                else card.Add(Ui.Text("Join", "chip-join", Ui.Bold).Margin(12));
                if (i == 0) card.style.marginLeft = 16;
                object tapped = rows[i];
                row.Add(card.OnTap(() => app.Push(new ChallengeScreen(app, tapped))));
            }
            var end = new VisualElement();
            end.style.width = 16;
            row.Add(end);
            slot.Add(row);
        }

        /// <summary>The stroke icon where the set has one (FillTrophy to Trophy): icons are plain strokes.</summary>
        public static string Stroke(string icon)
        {
            string stroke = icon != null && icon.StartsWith("Fill") ? icon.Substring(4) : icon;
            return stroke != icon && Resources.Load<Texture2D>("PuttrArcade/Icons/Icon" + stroke) == null ? icon : stroke;
        }

        static int Joined(object row) => J.Get(J.Get(row, "challenge"), "userJoined") != null ? 1 : 0;
    }

    /// <summary>What the Player competes in, on Home: League Night and tournaments. Filled in by Compete.Fill.</summary>
    public static partial class Compete
    {
        static partial void FillCards(App app, VisualElement slot);

        public static void Fill(App app, VisualElement slot) => FillCards(app, slot);
    }
}
