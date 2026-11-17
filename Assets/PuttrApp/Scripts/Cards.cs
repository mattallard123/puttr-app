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
                slot.Add(Ui.Box("pad", Ui.Box("glass", Ui.Text("No active challenges right now.", "sub", Ui.Regular))));
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
                ProfileScreen.Look(J.Str(challenge, "cType"), out string icon, out Color tint);
                // The icon and the title in a row; the title wraps to two lines and never runs off the card.
                VisualElement card = Ui.Box("glass challenge",
                    Ui.Box("row challenge-head", Ui.Bubble(icon, tint), Ui.Text(J.Str(challenge, "title", "Challenge"), "tile-title challenge-title grow", Ui.Bold)),
                    Ui.Text(ProfileMath.TimeLeft(ProfileMath.Date(challenge, "endDate"), DateTime.UtcNow), "muted", Ui.Regular));
                if (joined && levels.Count > 0)
                {
                    card.Add(Ui.Progress(percent / 100f));
                    card.Add(Ui.Text(percent + "%", "sec-link", Ui.Bold).Margin(6));
                }
                else card.Add(Ui.Text("Join", "chip-join", Ui.Bold).Margin(8));
                if (i == 0) card.style.marginLeft = 16;
                object tapped = rows[i];
                row.Add(card.OnTap(() => app.Push(new ChallengeScreen(app, tapped))));
            }
            var end = new VisualElement();
            end.style.width = 16;
            row.Add(end);
            slot.Add(row);
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
