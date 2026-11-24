// Quests (puttr-app QuestsScreen, QuestLevels, QuestGame): quests by difficulty, ten levels each,
// up to three stars a level. A level unlocks with a star on the one before; a Puttr Club quest
// (questType CLUB) is members only. Levels play on the 3D ramp (GameBridge.PlayQuest).
//
//   GET  /quests/                    quests with totalStars, completed_levels
//   GET  /quests/levels/:questId     levels with puttData, levelStars, the Player's best result
//   POST /quests/savePutt/           every putt (Rounds.cs)
//   POST /quests/level/finish        the stars (Rounds.cs)
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class QuestMath
    {
        // QuestsSection. Difficulty 2 belongs to none in the app (a bug): here it is Beginner.
        public static int Tier(object quest) { int d = J.Int(quest, "difficulty"); return d <= 2 ? 0 : d < 8 ? 1 : 2; }
        public static readonly string[] Tiers = { "Beginner", "Advanced", "Difficult" };
        public static readonly string[] TierWords = { "Start your putting journey", "Test your skills", "For serious putters" };
        public static readonly Color[] TierColours = { Ui.Bright, Ui.Bright, Ui.Amber };   // text only: the harder tier reads amber
        public static bool Locked(App app, object quest) => J.Str(quest, "questType") == "CLUB" && !Club.Member(app.Profile);

        /// <summary>Three stars, filled up to n (gold), as dots of text.</summary>
        public static VisualElement Stars(int n, float size)
        {
            VisualElement row = Ui.Box("row star-row");
            for (int i = 0; i < 3; i++)
            {
                VisualElement star = Ui.Box("star");
                star.style.width = star.style.height = size;
                star.style.backgroundColor = i < n ? Ui.Amber : Ui.Surface2;
                row.Add(star);
            }
            return row;
        }

        public static void Summary(List<object> quests, out int stars, out int levels, out int complete)
        {
            stars = levels = complete = 0;
            foreach (object q in quests)
            {
                stars += J.Int(q, "totalStars");
                levels += J.Int(q, "completed_levels");
                if (J.Int(q, "completed_levels") >= J.Int(q, "levels") && J.Int(q, "levels") > 0) complete++;
            }
        }
    }

    public sealed class QuestsScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;

        public QuestsScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Quests", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/quests/", data => Draw(J.List(data)), _ => { _body.Clear(); _body.Add(Page.Note("Could not load quests.")); }));

        void Draw(List<object> quests)
        {
            _body.Clear();
            QuestMath.Summary(quests, out int stars, out int levels, out int complete);
            if (stars + levels > 0)
                _body.Add(Ui.Box("pad", Ui.StatStrip((stars.ToString(), "Stars earned"), (levels.ToString(), "Levels completed"), (complete.ToString(), "Quests completed"))));
            for (int tier = 0; tier < 3; tier++)
            {
                List<object> mine = quests.FindAll(q => QuestMath.Tier(q) == tier);
                if (mine.Count == 0) continue;
                _body.Add(Ui.Section(QuestMath.Tiers[tier] + " quests"));
                _body.Add(Ui.Box("pad", Ui.Text(QuestMath.TierWords[tier], "body-2", Ui.Regular)));
                ScrollView row = Ui.Scroll(true);
                row.style.marginTop = 8;
                foreach (object q in mine) row.Add(Card(q, tier));
                if (row.contentContainer.childCount > 0) row.contentContainer[0].style.marginLeft = 16;
                row.Add(FreePuttScreen.Space(0).Width(16));
                _body.Add(row);
            }
        }

        VisualElement Card(object quest, int tier)
        {
            int levels = J.Int(quest, "levels"), stars = J.Int(quest, "totalStars");
            bool locked = QuestMath.Locked(_app, quest);
            // A flat head: the tier in its colour as an eyebrow, the quest's name as a title. No tinted panel.
            Label eyebrow = Ui.Caps(QuestMath.Tiers[tier], "caps");
            eyebrow.style.color = QuestMath.TierColours[tier];
            VisualElement head = Ui.Box("quest-head", eyebrow, Ui.Title(J.Str(quest, "title"), "title-20 quest-title"));
            int filled = levels > 0 ? Mathf.FloorToInt(stars * 3f / (levels * 3f) + 0.001f) : 0;
            VisualElement card = Ui.Box("card quest-card", head,
                Ui.Box("chips", Ui.Text(levels + " levels", "chip"), locked ? Ui.Text("Puttr Club", "chip chip-amber") : null),
                Ui.Text(J.Str(quest, "description"), "body-2 quest-desc", Ui.Regular),
                Ui.Box("row", QuestMath.Stars(filled, 12), Ui.Text("  " + stars + "/" + levels * 3, "body-2", Ui.Medium)).Margin(8));
            object tapped = quest;
            return card.OnTap(() => { if (QuestMath.Locked(_app, tapped)) Club.Require(_app, "Quests", () => { }); else _app.Push(new QuestLevelsScreen(_app, tapped)); });
        }
    }

    public sealed class QuestLevelsScreen : AppScreen
    {
        readonly App _app;
        readonly object _quest;
        readonly VisualElement _grid;

        public QuestLevelsScreen(App app, object quest)
        {
            _app = app;
            _quest = quest;
            Root = Page.Make(app, J.Str(quest, "title", "Quest"), out VisualElement body);
            body.Add(Ui.Box("pad", Ui.Text(J.Str(quest, "description"), "body-2 center", Ui.Regular)));
            _grid = Ui.Box("level-grid");
            body.Add(Ui.Box("pad", _grid).Margin(12));
            _grid.Add(Spinner.Small());
        }

        public override void Shown() => App.Run(Api.Get("/quests/levels/" + J.Str(_quest, "id"), data => Draw(J.List(data))));

        void Draw(List<object> levels)
        {
            _grid.Clear();
            for (int i = 0; i < levels.Count; i++)
            {
                object level = levels[i];
                bool open = i == 0 || J.Int(levels[i - 1], "result") > 0;
                string type = J.Str(level, "levelType").ToUpperInvariant();
                VisualElement tile = Ui.Box(open ? "card level-tile" : "card level-tile level-locked",
                    Ui.Num(J.Int(level, "levelNum").ToString(), "num level-num"),
                    open ? QuestMath.Stars(J.Int(level, "result"), 14) : Ui.Caps("Locked", "caps"),
                    Ui.Caps(type == "REDUCER" ? "Precision" : type == "STREAK" ? "Streak" : "Standard", "caps").Margin(4));
                if (open) tile.OnTap(() => _app.Games.PlayQuest(_quest, level));
                _grid.Add(tile);
            }
        }
    }
}
