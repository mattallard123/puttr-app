// Custom games (puttr-app components/creator): a Player's own putt lists, played on the 3D ramp.
//   GET  /creator/games/                      my games
//   POST /creator/game/create/ {title, description, gameType}          -> [game]
//   POST /creator/game/save/ {gameId, title, description, gameType, difficulty, distance, putts, puttData}
//   GET  /creator/game/delete/:gameId
// Types: Standard (a make scores its distance), Streak (until a miss), Center cut. No save button:
// a change saves itself 0.8 s after the last edit, as EditGame. Off for Kid profiles (free-text titles).
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class CreatorMath
    {
        // Streak is no longer offered for new games (On Fire's Distance option plays a streak); old ones still edit and play.
        public static readonly (string type, string label)[] Types = { ("STANDARD", "Standard"), ("CENTERCUT", "Center cut") };

        /// <summary>recalcStats: difficulty is the mean of (distance - 2) + |tee - 2|, distance the sum of distances.</summary>
        public static void Stats(List<TournamentMath.Dealt> putts, out double difficulty, out int distance)
        {
            difficulty = 0; distance = 0;
            if (putts.Count == 0) return;
            double sum = 0;
            foreach (TournamentMath.Dealt p in putts) { sum += (p.Feet - 2) + Math.Abs(p.Tee - 2); distance += p.Feet; }
            difficulty = Math.Round(sum / putts.Count, 1);
        }

        public static string PuttJson(List<TournamentMath.Dealt> putts)
        {
            var rows = new List<object>();
            var dice = new System.Random();
            foreach (TournamentMath.Dealt p in putts) rows.Add(new Dictionary<string, object> { { "id", dice.Next(10000000) }, { "distance", p.Feet }, { "tee", p.Tee } });
            return J.Write(rows);
        }
    }

    /// <summary>CreatorScreen: My games, and New game.</summary>
    public sealed class CreatorScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _list;

        public CreatorScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "My games", out VisualElement body);
            if (Kid.Blocked(app, Kid.Social, body)) return;
            body.Add(Ui.Box("pad", Ui.Button("New game", () => NewGame(app, Root))));
            _list = Ui.Box("cols pad");
            body.Add(_list);
            _list.Add(Spinner.Small());
        }

        public override void Shown()
        {
            if (_list == null) return;
            App.Run(Api.Get("/creator/games/", d => Draw(J.List(d)), _ => { _list.Clear(); _list.Add(Ui.Text("Couldn't load your games.", "sub", Ui.Regular)); }));
        }

        void Draw(List<object> games)
        {
            _list.Clear();
            if (games.Count == 0) { _list.Add(Ui.Box("col-full", Ui.Text("No games yet", "h2", Ui.Bold), Ui.Text("Create your first custom game by tapping the button above", "sub", Ui.Regular))); return; }
            foreach (object g in games) _list.Add(Ui.Box("col", Card(_app, g, Shown)));
        }

        public static VisualElement Card(App app, object game, Action changed)
        {
            string type = J.Str(game, "gameType");
            int putts = J.Int(game, "putts"), distance = J.Int(game, "distance");
            double difficulty = J.Num(game, "difficulty");
            int level = difficulty <= 3 ? 1 : difficulty <= 6 ? 2 : 3;
            VisualElement dots = Ui.Box("row");
            for (int i = 1; i <= 3; i++) { VisualElement d = Ui.Box("diff-dot"); d.style.backgroundColor = i <= level ? (level == 1 ? Ui.Bright : level == 2 ? Ui.Amber : Ui.Red) : (Color)new Color32(44, 46, 48, 255); dots.Add(d); }
            object g = game;
            return Ui.Box("glass creator-card",
                Ui.Box("row", Ui.Bubble(type == "STREAK" ? "FillFire" : type == "CENTERCUT" ? "Target" : "FillJoystick", Ui.Bright), Ui.Box("grow"),
                    Ui.Box("btn-small", Ui.Text("Edit", "btn-text", Ui.Bold)).OnTap(() => app.Push(new EditGameScreen(app, g))), FreePuttScreen.Space(0).Width(6),
                    Ui.Box("btn-small", Ui.Text("Delete", "btn-text", Ui.Bold)).OnTap(() => Dialog.Ask(app.Toasts, "Delete game", "Are you sure you want to delete this game?", "Delete",
                        () => App.Run(Api.Get("/creator/game/delete/" + J.Str(g, "id"), _ => changed(), _ => app.Toast("Couldn't delete it."))), true))),
                Ui.Text(J.Str(game, "title"), "feed-name", Ui.Bold).Margin(8), Ui.Text(J.Str(game, "description"), "muted", Ui.Regular),
                Ui.Box("chips", Ui.Text(putts > 0 ? putts + " putts" : "Infinite", "chip"), distance > 0 ? Ui.Text(distance + " ft", "chip") : null, dots))
                .OnTap(() =>
                {
                    if (TournamentMath.Putts(g).Count == 0) { app.Toast("Add putts to play this game."); app.Push(new EditGameScreen(app, g)); return; }
                    app.Games.PlayCatalog(g, null);
                });
        }

        /// <summary>The New game sheet: a type, a title and a description, then the putts.</summary>
        public static void NewGame(App app, VisualElement over)
        {
            int type = 0;
            bool busy = false;
            VisualElement dim = null;
            var title = new TextField(); title.textEdition.placeholder = "e.g. 9 Hole Classic"; title.textEdition.hidePlaceholderOnFocus = false; title.AddToClassList("field");
            var desc = new TextField { multiline = true }; desc.textEdition.placeholder = "Describe your game..."; desc.textEdition.hidePlaceholderOnFocus = false; desc.AddToClassList("field");
            Label error = Ui.Text("", "sub", Ui.Medium);
            error.style.color = Ui.Red;
            VisualElement card = Ui.Box("modal", Ui.Text("New game", "h2", Ui.Bold), Ui.Text("Design your own putting challenge", "sub", Ui.Regular),
                Ui.Text("GAME TYPE", "kick", Ui.Bold).Margin(12), FreePuttScreen.Choice(Array.ConvertAll(CreatorMath.Types, t => t.label), 0, i => type = i).Margin(6),
                Ui.Text("TITLE", "kick", Ui.Bold).Margin(12), title.Face(Ui.Medium), Ui.Text("DESCRIPTION", "kick", Ui.Bold).Margin(4), desc.Face(Ui.Medium), error);
            card.Add(Ui.Button("Create game", () =>
            {
                if (busy) return;
                if (title.value.Trim().Length == 0 || desc.value.Trim().Length == 0) { error.text = "Title and description are required"; return; }
                busy = true;
                App.Run(Api.Post("/creator/game/create/", new Dictionary<string, object> { { "title", title.value.Trim() }, { "description", desc.value.Trim() }, { "gameType", CreatorMath.Types[type].type } }, d =>
                {
                    dim.RemoveFromHierarchy();
                    object game = J.First(d) ?? d;
                    app.Push(new EditGameScreen(app, game));
                }, _ => { busy = false; error.text = "Couldn't create the game. Try again."; }));
            }).Margin(12));
            card.Add(Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(over).Add(dim);
        }
    }

    /// <summary>EditGame: the title, description and type, and the putts in order. Saves itself.</summary>
    public sealed class EditGameScreen : AppScreen
    {
        readonly App _app;
        readonly object _game;
        readonly VisualElement _body;
        readonly TextField _title, _desc;
        readonly List<TournamentMath.Dealt> _putts;
        int _type;
        float _changedAt = -1f;
        bool _dirty;

        public EditGameScreen(App app, object game)
        {
            _app = app;
            _game = game;
            Root = Page.Make(app, "Edit game", out _body);
            if (Kid.Blocked(app, Kid.Social, _body)) return;
            _title = new TextField { value = J.Str(game, "title") }; _title.textEdition.placeholder = "Game title"; _title.AddToClassList("field");
            _desc = new TextField { value = J.Str(game, "description"), multiline = true }; _desc.textEdition.placeholder = "Game description"; _desc.AddToClassList("field");
            _title.RegisterValueChangedCallback(_ => Changed());
            _desc.RegisterValueChangedCallback(_ => Changed());
            _type = Math.Max(0, Array.FindIndex(CreatorMath.Types, t => t.type == J.Str(game, "gameType")));
            _putts = TournamentMath.Putts(game);
            Root.schedule.Execute(() => { if (_dirty && Time.realtimeSinceStartup - _changedAt > 0.8f) Save(); }).Every(200);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => { if (_dirty) Save(); });
            Draw();
        }

        void Changed() { _dirty = true; _changedAt = Time.realtimeSinceStartup; }

        void Draw()
        {
            _body.Clear();
            CreatorMath.Stats(_putts, out double difficulty, out int distance);
            _body.Add(Ui.Box("pad", _title.Face(Ui.Medium), _desc.Face(Ui.Medium),
                FreePuttScreen.Choice(Array.ConvertAll(CreatorMath.Types, t => t.label), _type, i => { _type = i; Changed(); }).Margin(6),
                Ui.Box("glass row", FreePuttScreen.Stat(difficulty.ToString("0.0", CultureInfo.InvariantCulture), "Difficulty"), FreePuttScreen.Stat(distance + " ft", "Distance"), FreePuttScreen.Stat(_putts.Count.ToString(), "Putts")).Margin(12),
                Ui.Button("Add putt", AddPutt).Margin(12)));
            _body.Add(Ui.Section("Putts"));
            VisualElement list = Ui.Box("glass set-card");
            if (_putts.Count == 0) list.Add(Ui.Text("No putts added yet", "sub", Ui.Regular).Margin(12));
            for (int i = 0; i < _putts.Count; i++)
            {
                int at = i;
                TournamentMath.Dealt p = _putts[i];
                VisualElement up = Ui.Box("btn-small", Ui.Text("↑", "btn-text", Ui.Bold)), down = Ui.Box("btn-small", Ui.Text("↓", "btn-text", Ui.Bold));
                if (i > 0) up.OnTap(() => { Swap(at, at - 1); }); else up.style.opacity = 0.3f;
                if (i < _putts.Count - 1) down.OnTap(() => { Swap(at, at + 1); }); else down.style.opacity = 0.3f;
                Label remove = Ui.Text("Remove", "sec-link", Ui.Medium);
                remove.style.color = Ui.Red;
                list.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Ui.Text(p.Feet + " ft · " + PracticeSession.TeeName(p.Tee), "feed-name grow", Ui.Medium),
                    up, FreePuttScreen.Space(0).Width(4), down, FreePuttScreen.Space(0).Width(10), remove.OnTap(() => { _putts.RemoveAt(at); Changed(); Draw(); })));
            }
            _body.Add(Ui.Box("pad", list));
            _body.Add(Ui.Box("pad", Ui.Text("Changes save as you go.", "muted center", Ui.Regular).Margin(10)));
        }

        void Swap(int a, int b) { TournamentMath.Dealt t = _putts[a]; _putts[a] = _putts[b]; _putts[b] = t; Changed(); Draw(); }

        // The Add putt sheet: the mat, 3 to 11 feet by three tees. Stays open to add several.
        void AddPutt()
        {
            int feet = 6, tee = 2;
            VisualElement dim = null, grid = Ui.Box("green-grid");
            Label count = Ui.Text("", "muted center", Ui.Regular);
            void DrawGrid()
            {
                grid.Clear();
                for (int f = 11; f >= 3; f--)
                {
                    VisualElement row = Ui.Box("row green-row", Ui.Text(f + " ft", "muted green-ft", Ui.Medium));
                    for (int t = 1; t <= 3; t++)
                    {
                        int ff = f, tt = t;
                        row.Add(Ui.Box(ff == feet && tt == tee ? "green-tee green-on" : "green-tee").OnTap(() => { feet = ff; tee = tt; DrawGrid(); }));
                    }
                    grid.Add(row);
                }
                count.text = feet + " ft, " + PracticeSession.TeeName(tee) + " tee   ·   " + _putts.Count + " putts";
            }
            DrawGrid();
            VisualElement card = Ui.Box("modal", Ui.Text("Add putt", "h2", Ui.Bold), grid.Margin(8), count,
                Ui.Button("Add putt", () => { _putts.Add(new TournamentMath.Dealt { Feet = feet, Tee = tee }); Changed(); DrawGrid(); Draw(); }).Margin(10),
                Ui.Button("Done", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        void Save()
        {
            _dirty = false;
            CreatorMath.Stats(_putts, out double difficulty, out int distance);
            var body = new Dictionary<string, object>
            {
                { "gameId", J.Str(_game, "id") }, { "title", _title.value.Trim() }, { "description", _desc.value.Trim() }, { "gameType", CreatorMath.Types[_type].type },
                { "difficulty", difficulty.ToString("0.0", CultureInfo.InvariantCulture) }, { "distance", distance }, { "putts", _putts.Count }, { "puttData", CreatorMath.PuttJson(_putts) },
            };
            if (_game is Dictionary<string, object> g) { g["title"] = body["title"]; g["gameType"] = body["gameType"]; g["puttData"] = body["puttData"]; g["putts"] = _putts.Count; }
            App.Run(Api.Post("/creator/game/save/", body, null, _ => _app.Toast("Couldn't save your changes. Check your connection.")));
        }
    }
}
