// Tracks, as puttr-app's tracks/TracksCourses.js, TracksLeaderboard.js and TracksLeaderboardScreen.js:
//   TracksBoard           GET /tracks/leaderboard[/:slug]: the last 30 days, each Player's best round at each level,
//                         Major, then Pro, then Amateur; across every track each row names its track. A row opens the round.
//   TracksLeaderboardScreen  that board as a page (from a track's setup and its scorecard).
//   AddTrackSheet         GET /tracks/search?q= (OpenStreetMap), POST /tracks/import: a course mapped with its holes
//                         becomes the Player's own community track. One track per course: a course already added opens it.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class TracksBoard
    {
        static readonly string[] Levels = { "major", "pro", "amateur" };
        static readonly Dictionary<string, string> LevelName = new Dictionary<string, string> { { "major", "Major" }, { "pro", "Pro" }, { "amateur", "Amateur" } };
        static readonly Dictionary<string, string> SetName = new Dictionary<string, string> { { "front9", "Front 9" }, { "back9", "Back 9" }, { "all18", "18 holes" } };
        // The last answer per board (slug, or "*" for every track): coming back shows the rows at once and refreshes quietly.
        static readonly Dictionary<string, object> Cache = new Dictionary<string, object>();

        /// <summary>Fills into with the board for one track (slug), or every track (slug null): a section per level, each
        /// its header (Ui.Section) and one bordered block of rows. into is a plain box with no padding of its own.</summary>
        public static void Fill(App app, VisualElement into, string slug)
        {
            string key = string.IsNullOrEmpty(slug) ? "*" : slug;
            if (Cache.TryGetValue(key, out object cached)) Draw(app, into, cached, slug == null || slug.Length == 0);
            else { into.Clear(); into.Add(Page.Loading()); }
            App.Run(Api.Get("/tracks/leaderboard" + (string.IsNullOrEmpty(slug) ? "" : "/" + UnityEngine.Networking.UnityWebRequest.EscapeURL(slug)),
                d => { Cache[key] = d; Draw(app, into, d, string.IsNullOrEmpty(slug)); },
                _ => { if (!Cache.ContainsKey(key)) { into.Clear(); into.Add(Ui.Box("pad", Ui.Text("The leaderboard is not available right now.", "body-2 center", Ui.Regular)).Margin(16)); } }));
        }

        static void Draw(App app, VisualElement into, object data, bool allTracks)
        {
            into.Clear();
            List<object> rows = J.List(J.Get(data, "rows"));
            string me = J.Str(app.Profile, "userId", Auth.Uid);
            bool any = false;
            foreach (string level in Levels)
            {
                List<object> mine = rows.FindAll(r => J.Str(r, "level") == level);
                if (mine.Count == 0) continue;
                into.Add(Ui.Section(LevelName[level]));
                any = true;
                VisualElement block = Ui.Box("card tb-card");
                foreach (object r in mine) block.Add(Row(app, r, me, allTracks));
                TracksResultScreen.LastRow(block);
                into.Add(Ui.Box("pad", block));
            }
            if (!any) into.Add(Ui.Box("pad", Ui.Text("No rounds in the last 30 days. Finish one and you are on the board.", "body-2 center", Ui.Regular)).Margin(16));
        }

        static VisualElement Row(App app, object r, string me, bool allTracks)
        {
            bool mine = me.Length > 0 && J.Str(r, "userId") == me;
            int rank = J.Int(r, "rank");
            Label place = Ui.Text(rank.ToString(), "tb-rank", Ui.Bold);
            if (rank <= 3) place.style.color = Ui.Gold;
            string name = J.Str(r, "displayName", "Player");
            VisualElement words = Ui.Box("grow tb-words", Ui.Text(mine ? "You" : name, "tb-name", Ui.Bold));
            if (allTracks) words.Add(Ui.Text(J.Str(r, "trackName", J.Str(r, "trackSlug")), "tb-track", Ui.Medium));
            string set = J.Str(r, "holeSet");
            var meta = new List<string>();
            if (J.Str(r, "homeTown").Length > 0) meta.Add(J.Str(r, "homeTown"));
            if (DateTime.TryParse(J.Str(r, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime when)) meta.Add(when.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
            words.Add(Ui.Box("row tb-meta", Ui.Text(SetName.TryGetValue(set, out string s) ? s : set, "chip tb-set", Ui.Bold), Ui.Text(string.Join(" · ", meta), "tb-when", Ui.Regular)));
            int toPar = J.Int(r, "toPar");
            Label score = Ui.Num(TracksEngine.ToPar(toPar), "num-20");
            score.style.color = toPar < 0 ? Ui.Bright : toPar > 0 ? Ui.Red : Color.white;
            VisualElement right = Ui.Box("tb-right", score, Ui.Text(J.Int(r, "strokes") + " on par " + J.Int(r, "par"), "tb-par", Ui.Medium));
            VisualElement row = Ui.Box(mine ? "tb-row tb-me" : "tb-row", place, Ui.Avatar(name, J.Str(r, "photoURL")), words, right);
            string game = J.Str(r, "userGameId");
            if (game.Length > 0) row.OnTap(() => app.Push(new GameViewScreen(app, game)));
            return row;
        }
    }

    /// <summary>One track's board, as a page.</summary>
    public sealed class TracksLeaderboardScreen : AppScreen
    {
        public TracksLeaderboardScreen(App app, string slug, string name)
        {
            Root = Page.Make(app, "Leaderboard", out VisualElement body);
            body.Add(Ui.Box("pad", TrackLabels.Inspired(name, "title"), Ui.Text("Last 30 days. Each Player's best round at each level.", "body-2 tb-intro", Ui.Regular)));
            VisualElement board = new VisualElement();
            body.Add(board);
            TracksBoard.Fill(app, board, slug);
        }
    }

    /// <summary>Add a track: search the map by name, pick a course, confirm, and it is imported as your own track.</summary>
    public static class AddTrackSheet
    {
        public static void Open(App app, VisualElement over, Action<object> added, Action<string> showExisting)
        {
            VisualElement dim = null;
            bool importing = false;
            TextField field = Form.Field("Search a course name", "");
            Label error = Ui.Text("", "body-2 tracks-add-error", Ui.Medium);
            error.style.display = DisplayStyle.None;
            ScrollView results = Ui.Scroll();
            results.AddToClassList("tracks-add-results");
            VisualElement confirm = Ui.Box("tracks-add-confirm");
            confirm.style.display = DisplayStyle.None;
            void Close() { if (!importing) dim.RemoveFromHierarchy(); }
            void Error(string text) { error.text = text ?? ""; error.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex; }

            VisualElement card = Ui.Box("modal tracks-add",
                Ui.Box("row", Ui.Title("Add a track", "title-20 grow"), Ui.Box("tracks-add-x", Ui.Icon("X", 18, Ui.Faint)).OnTap(Close)),
                Ui.Text("Any course mapped on OpenStreetMap with its holes drawn. It becomes your track, and the shapes are the real ones. Each course can be added once.", "body-2", Ui.Regular).Margin(4),
                Ui.Box("tracks-add-field", field).Margin(12), error, confirm, results);
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim tracks-add-dim", card).OnBackdropTap(Close);
            App.Layer(over).Add(dim);
            field.schedule.Execute(() => field.Focus()).StartingIn(50);

            int asked = 0;
            IVisualElementScheduledItem pending = null;
            field.RegisterValueChangedCallback(e =>
            {
                pending?.Pause();
                string q = e.newValue.Trim();
                int ask = ++asked;
                if (q.Length < 3) { results.Clear(); Error(null); return; }
                // As the app: wait for a pause in the typing, then search.
                pending = field.schedule.Execute(() =>
                {
                    results.Clear();
                    results.Add(Ui.Box("pad", Spinner.Small()));
                    Error(null);
                    App.Run(Api.Get("/tracks/search?q=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(q),
                        d => { if (ask == asked) Results(J.List(d), q); },
                        _ => { if (ask != asked) return; results.Clear(); Error("Search is not available right now."); }));
                }).StartingIn(450);
            });

            void Results(List<object> hits, string q)
            {
                results.Clear();
                foreach (object hit in hits)
                {
                    object h = hit;
                    object existing = J.Get(h, "existing");
                    VisualElement words = Ui.Box("grow", Ui.Text(J.Str(h, "name"), "body", Ui.Bold));
                    if (J.Str(h, "place").Length > 0) words.Add(Ui.Text(J.Str(h, "place"), "body-2", Ui.Regular));
                    VisualElement row = Ui.Box("tracks-add-row", words,
                        existing != null ? Ui.Text(J.Flag(existing, "mine") ? "YOUR TRACK" : "ALREADY ADDED", "chip chip-green tracks-add-tag") : null,
                        Ui.Icon("CaretRight", 16, Ui.Faint));
                    row.OnTap(() => { if (existing != null) { dim.RemoveFromHierarchy(); showExisting(J.Str(existing, "name")); } else Pick(h); });
                    results.Add(row);
                }
                if (hits.Count == 0) results.Add(Ui.Text("No courses found with that name.", "body-2", Ui.Regular).Margin(12));
            }

            void Pick(object hit)
            {
                Error(null);
                results.style.display = DisplayStyle.None;
                field.parent.style.display = DisplayStyle.None;
                confirm.Clear();
                confirm.style.display = DisplayStyle.Flex;
                VisualElement words = Ui.Box("grow", Ui.Text(J.Str(hit, "name"), "body", Ui.Bold));
                if (J.Str(hit, "place").Length > 0) words.Add(Ui.Text(J.Str(hit, "place"), "body-2", Ui.Regular));
                VisualElement x = Ui.Box("tracks-add-x", Ui.Icon("X", 18, Ui.Faint)).OnTap(() =>
                {
                    if (importing) return;
                    confirm.style.display = DisplayStyle.None;
                    results.style.display = DisplayStyle.Flex;
                    field.parent.style.display = DisplayStyle.Flex;
                    Error(null);
                });
                confirm.Add(Ui.Box("tracks-add-picked", words, x));
                Label note = Ui.Text("", "body-2 center", Ui.Regular);
                VisualElement button = null;
                button = Ui.Button("Add track", () =>
                {
                    if (importing) return;
                    importing = true;
                    button.SetEnabled(false);
                    ((Label)button.Q(className: "btn-text")).text = "Reading the map";
                    note.text = "Building the holes from the map takes a few seconds.";
                    var body = new Dictionary<string, object> { { "osmType", J.Str(hit, "osmType") }, { "osmId", J.Get(hit, "osmId") }, { "name", J.Str(hit, "name") }, { "location", J.Str(hit, "place") } };
                    App.Run(Api.Post("/tracks/import", body, d =>
                    {
                        importing = false;
                        dim.RemoveFromHierarchy();
                        added(d);
                    }, _ =>
                    {
                        importing = false;
                        object why = Api.LastFailure;
                        // Someone (maybe you, a moment ago) already added this course: show theirs.
                        if (J.Str(why, "error") == "already_added" && J.Str(why, "slug").Length > 0) { dim.RemoveFromHierarchy(); showExisting(J.Str(why, "name", J.Str(hit, "name"))); return; }
                        button.SetEnabled(true);
                        ((Label)button.Q(className: "btn-text")).text = "Add track";
                        note.text = "";
                        Error(J.Str(why, "message").Length > 0 ? J.Str(why, "message") : J.Str(why, "error") == "import_unavailable" ? "Importing is not available right now." : "That course could not be imported.");
                    }));
                });
                confirm.Add(button.Margin(12));
                confirm.Add(note.Margin(8));
            }
        }
    }
}
