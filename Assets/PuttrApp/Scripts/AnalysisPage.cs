// Full analysis, the page body (puttr-app analysis/UserAnalysis), for the signed-in Player or for any Player a coach
// may coach (UserAnalysis coachMode, as the lesson workspace and Player Review show it):
//
//   the signed-in Player   GET /profile/analysis/:days (the 27 spots) and GET /profile/analysis/putts/:days (each putt)
//   another Player         POST /coaching/analysis/putts {userId, days} (each putt; the spots are counted from them,
//                          analysisUtils buildAnalysisGrid). Create drill is the Player's own and is left out.
//
// The range chips, the period and putt count, best and worst spot, Pace, Hole entry, Start lines, Distance breakdown
// (a tap on a spot opens its totals and its own start-line fan), Make % by break and Worst to first. The host scrolls:
// this adds plain rows to it. AnalysisScreen (ImproveTab.cs) hosts it for the signed-in Player with the coach's plan
// pinned under it (CoachPlan.AnalysisBar).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class AnalysisPage
    {
        static readonly int[] Ranges = { 1, 7, 14, 30, 60, 90, 180, 365 };
        // UserAnalysis getFilterLabel: 24h, then days, and 1y.
        static readonly string[] RangeNames = { "24h", "7d", "14d", "30d", "60d", "90d", "180d", "1y" };

        readonly App _app;
        readonly string _userId;
        readonly bool _self;
        string _handedness;
        readonly VisualElement _host, _pills, _body;
        int _range = 3;
        int _asked;

        /// <summary>The range the page shows, in days.</summary>
        public int Days => Ranges[_range];

        /// <summary>Raised each time a range has loaded and been drawn.</summary>
        public event Action Loaded;

        /// <param name="userId">The Player to show; null or the signed-in Player's own id reads their own analysis.</param>
        /// <param name="handedness">RIGHTY or LEFTY, for pushes and pulls; the profile's for the signed-in Player.</param>
        public AnalysisPage(App app, VisualElement host, string userId, string handedness = null)
        {
            _app = app;
            _host = host;
            string me = J.Str(app.Profile, "userId");
            _self = string.IsNullOrEmpty(userId) || userId == me;
            _userId = _self ? me : userId;
            _handedness = handedness ?? (_self ? J.Str(app.Profile, "handedness") : "");
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Improve");
            if (sheet != null && !host.styleSheets.Contains(sheet)) host.styleSheets.Add(sheet);
            _pills = new VisualElement();
            _body = new VisualElement();
            host.Add(Ui.Box("pad", _pills));
            host.Add(_body);
            Load();
        }

        void Load()
        {
            _pills.Clear();
            ScrollView row = Ui.Scroll(true);
            for (int i = 0; i < Ranges.Length; i++)
            {
                int index = i;
                row.Add(Ui.Box(i == _range ? "scope scope-on" : "scope", Ui.Text(RangeNames[i], "scope-text", Ui.Bold)).OnTap(() => { if (index == _range) return; _range = index; Load(); }));
            }
            _pills.Add(row);
            _body.Clear();
            _body.Add(Ui.Box("center-box", Spinner.Small(), Ui.Text(_asked == 0 ? "Analyzing your putts" : "Updating...", "body-2 center", Ui.Medium).Margin(8)).Margin(32));
            int asked = ++_asked, days = Ranges[_range];
            if (_self)
            {
                List<AnalysisMath.Cell> grid = null;
                List<object> rows = null;
                void Both() { if (asked == _asked && grid != null && rows != null) Draw(grid, rows, days); }
                App.Run(Api.Get("/profile/analysis/" + days, data => { grid = AnalysisMath.Grid(J.List(data)); Both(); }, _ => { grid = AnalysisMath.Grid(new List<object>()); Both(); }));
                App.Run(Api.Get("/profile/analysis/putts/" + days, data => { rows = J.List(data); Both(); }, _ => { rows = new List<object>(); Both(); }));
            }
            else
            {
                var body = new Dictionary<string, object> { { "userId", _userId }, { "days", days } };
                void Got(List<object> rows) { if (asked == _asked) Draw(AnalysisMath.Grid(PerPutt(rows)), rows, days); }
                App.Run(Api.Post("/coaching/analysis/putts", body, data => Got(J.List(data)), _ => Got(new List<object>())));
            }
        }

        /// <summary>buildAnalysisGrid: each putt as a one-putt spot row, so AnalysisMath.Grid adds them up.</summary>
        public static List<object> PerPutt(List<object> putts)
        {
            var rows = new List<object>();
            foreach (object p in putts)
                rows.Add(new Dictionary<string, object> { { "distance", (double)J.Int(p, "distance") }, { "tee", (double)J.Int(p, "tee") }, { "total", 1.0 }, { "made", J.Int(p, "made") == 1 ? 1.0 : 0.0 } });   // doubles: J reads numbers as the parser makes them
            return rows;
        }

        void Draw(List<AnalysisMath.Cell> grid, List<object> rows, int days)
        {
            _body.Clear();
            List<AnalysisMath.Putt> putts = AnalysisMath.ReadAll(rows);
            int total = AnalysisMath.Putts(grid);
            string period = days == 1 ? "Last 24 hours" : "Last " + days + " days";
            _body.Add(Ui.Box("pad", Ui.Box("row", Ui.Text(period, "body an-row-words", Ui.Bold), Ui.Text(ProfileMath.Number(total) + " putts analyzed", "body-2", Ui.Medium)).Margin(12)));
            if (total == 0) { _body.Add(Ui.Box("pad", Ui.Box("card", Ui.Text("No putts in this range. Try a longer one.", "body-2", Ui.Medium)).Margin(12))); Loaded?.Invoke(); return; }

            AnalysisMath.Extremes(grid, out AnalysisMath.Cell best, out AnalysisMath.Cell worst);
            _body.Add(Ui.Box("pad", Ui.Box("row spots", Spot("Best spot", best, Ui.Bright), Spot("Worst spot", worst, Ui.Red)).Margin(12)));

            // Pace
            AnalysisMath.Pace(putts, out int shortOnes, out int ideal, out int firm, out float median);
            int paced = shortOnes + ideal + firm;
            if (paced >= 5)
            {
                // The split in the guidelines' colours: short amber, ideal green, too firm red.
                VisualElement pace = Ui.Box("card");
                pace.Add(Ui.Box("row", Zone("Short", shortOnes, paced, Ui.Amber), Zone("Ideal pace", ideal, paced, Ui.Bright), Zone("Too firm", firm, paced, Ui.Red)));
                pace.Add(Ui.Text("Typical: " + AnalysisMath.Rollout(median) + ".   " + (shortOnes >= paced * 0.3f ? "Too many finish short: a putt that does not reach cannot drop." : firm >= paced * 0.3f ? "You are often too firm: aim to finish within a foot past." : "Your pace is dialed in."), "body-2 an-gap", Ui.Medium));
                bool firstBand = true;
                foreach ((string name, int from, int to) band in new[] { ("Short putts, 3 to 5 ft", 3, 5), ("Mid putts, 6 to 8 ft", 6, 8), ("Long putts, 9 to 11 ft", 9, 11) })
                {
                    float at = AnalysisMath.PaceAt(putts, band.from, band.to);
                    VisualElement row = Ui.Box("an-row", Ui.Text(band.name, "body an-row-words", Ui.Medium), Ui.Text(float.IsNaN(at) ? "No putts" : AnalysisMath.Rollout(at), "body-2 an-row-end", Ui.Medium));
                    if (firstBand) row.AddToClassList("an-gap");
                    firstBand = false;
                    pace.Add(row);
                }
                Section("Pace", pace);
            }

            // Hole entry
            int[] bands = AnalysisMath.Entries(putts, out int backDoor, out int holed);
            if (holed > 0)
            {
                VisualElement entry = Ui.Box("card", new EntryArc(bands), Ui.Text("Back door " + Mathf.RoundToInt(100f * backDoor / holed) + "% of makes", "body-2 center", Ui.Medium));
                AnalysisMath.Misses(putts, out int left, out int right);
                if (left + right > 0)
                {
                    int l = Mathf.RoundToInt(100f * left / (left + right));
                    entry.Add(Ui.Caps("Miss tendency").Margin(16));
                    entry.Add(Split(l, 100 - l, "left", "right"));
                }
                bool lefty = (_handedness ?? "").ToUpperInvariant().StartsWith("L");
                AnalysisMath.PushPull(putts, lefty, out int push, out int pull);
                if (push + pull > 0)
                {
                    int p = Mathf.RoundToInt(100f * push / (push + pull));
                    entry.Add(Ui.Caps("Push or pull").Margin(16));
                    entry.Add(p == 50 ? Ui.Text("Balanced, no tendency", "body-2 an-gap-4", Ui.Medium) : Split(p, 100 - p, "push", "pull"));
                }
                Section("Hole entry", entry);
            }

            // Start lines: one fan per tee over every distance (UserAnalysis renderStartLineCard).
            _body.Add(Ui.Box("pad sl-section", StartLines.TeeCard(rows, period)));

            // The 27 spots; a tap opens the spot.
            // Each spot is a flat square: its number in red, amber or green says how it stands, not a tinted ground.
            VisualElement spots = Ui.Box("card");
            spots.Add(Ui.Box("row sl-grid-head", Ui.Text("Tap a spot for start lines", "body-2 an-row-words", Ui.Medium), Ui.Text("Weighted vs PGA avg", "body-2 an-row-end", Ui.Medium)));
            spots.Add(Ui.Box("row", Ui.Box("spot-feet"), Ui.Caps("Left", "caps grow center"), Ui.Caps("Center", "caps grow center"), Ui.Caps("Right", "caps grow center")));
            for (int feet = 3; feet <= 11; feet++)
            {
                VisualElement row = Ui.Box("row spot-row", Ui.Text(feet + " ft", "spot-feet body", Ui.Bold));
                for (int tee = 1; tee <= 3; tee++)
                {
                    AnalysisMath.Cell cell = grid[(feet - 3) * 3 + tee - 1];
                    Label value = Ui.Text(cell.Total > 0 ? cell.Weighted.ToString() : "–", "spot-value", Ui.Bold);
                    value.style.color = cell.Total > 0 ? Heat(cell.Weighted) : Ui.Faint;
                    row.Add(Ui.Box("grow spot-cell", Ui.Box("spot-dot", value)).OnTap(() => SpotCard(cell, rows)));
                }
                spots.Add(row);
            }
            spots.Add(Ui.Text("Make % against the PGA Tour's at each spot: 100 is Tour level.", "body-2 an-gap-8", Ui.Medium));
            Section("Distance breakdown", spots);

            // Make % by break, from the grid
            var rings = Ui.Box("row");
            for (int tee = 1; tee <= 3; tee++)
            {
                int made = 0, all = 0;
                foreach (AnalysisMath.Cell c in grid) if (c.Tee == tee) { made += c.Made; all += c.Total; }
                rings.Add(Ui.Box("grow ring-item", new Ring(all > 0 ? Mathf.RoundToInt(100f * made / all) : -1), Ui.Caps(ProfileMath.BreakNames[tee - 1], "caps center an-gap-4")));
            }
            Section("Make % by break", Ui.Box("card", rings));

            // Worst to first; Create drill is the Player's own (hidden when a coach is looking).
            List<AnalysisMath.Cell> order = AnalysisMath.WorstFirst(grid);
            if (order.Count > 0)
            {
                // One bordered block of rows; the page's one main button under it.
                VisualElement list = Ui.Box("card");
                foreach (AnalysisMath.Cell c in order)
                {
                    Label value = Ui.Num(c.Weighted.ToString(), "num-20 an-row-end");
                    value.style.color = c.Weighted >= 100 ? Ui.Bright : Ui.Red;
                    list.Add(Ui.Box(list.childCount == 0 ? "an-row an-row-first" : "an-row", Ui.Box("an-row-words", Ui.Text(c.Name, "body", Ui.Bold), Ui.Text(c.Made + " of " + c.Total + " made", "body-2 an-gap-4", Ui.Medium)), value));
                }
                Section("Worst to first", list);
                if (_self)
                {
                    var drill = new List<TournamentMath.Dealt>();
                    for (int i = 0; i < Math.Min(9, order.Count); i++) drill.Add(new TournamentMath.Dealt { Feet = order[i].Feet, Tee = order[i].Tee });
                    _body.Add(Ui.Box("pad", Ui.Button("Create drill", () => _app.Games.PlayDrill(drill), "btn an-drill")));
                }
            }
            Loaded?.Invoke();
        }

        /// <summary>The spot modal: "6ft Center", TOTAL | ACTUAL | WEIGHTED, that spot's start-line fan, and Done.</summary>
        void SpotCard(AnalysisMath.Cell cell, List<object> rows)
        {
            VisualElement dim = null;
            void Close() => dim?.RemoveFromHierarchy();
            Label weighted = Ui.Text(cell.Weighted + "%", "sl-stat-v", Ui.Bold);
            weighted.style.color = cell.Weighted >= 100 ? Ui.Bright : Ui.Red;
            VisualElement Stat(VisualElement value, string label) => Ui.Box("sl-stat", value, Ui.Text(label, "sl-stat-l", Ui.Medium));
            VisualElement card = Ui.Box("modal sl-modal",
                Ui.Box("sl-target", Ui.Icon("Target", 24, Ui.Bright)),
                Ui.Text(cell.Feet + "ft " + AnalysisMath.TeeNames[cell.Tee - 1], "sl-modal-title", Ui.Bold),
                Ui.Box("row sl-stats",
                    Stat(Ui.Text(cell.Total.ToString(), "sl-stat-v", Ui.Bold), "TOTAL"), Ui.Box("sl-stat-div"),
                    Stat(Ui.Text(cell.Percent + "%", "sl-stat-v", Ui.Bold), "ACTUAL"), Ui.Box("sl-stat-div"),
                    Stat(weighted, "WEIGHTED")),
                StartLines.Spot(rows, cell.Feet, cell.Tee),
                Ui.Button("Done", Close, "btn btn-ghost sl-done"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(Close);
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Improve");
            if (sheet != null) dim.styleSheets.Add(sheet);
            App.Layer(_host).Add(dim);
        }

        void Section(string title, VisualElement card)
        {
            _body.Add(Ui.Section(title));
            _body.Add(Ui.Box("pad", card));
        }

        static VisualElement Spot(string label, AnalysisMath.Cell cell, Color colour)
        {
            // A flat card: the make % as the numeral (best in bright green, worst in red), then where and how many.
            Label value = Ui.Num(cell != null ? cell.Percent + "%" : "–", "num an-gap-4");
            value.style.color = cell != null ? colour : Ui.Faint;
            return Ui.Box("card grow spot-card", Ui.Caps(label), value,
                Ui.Text(cell != null ? cell.Name : "No data", "body an-gap-4", Ui.Bold),
                Ui.Text(cell != null ? cell.Total + " putts" : "", "body-2 an-gap-4", Ui.Medium));
        }

        static VisualElement Zone(string name, int count, int total, Color colour)
        {
            Label value = Ui.Num(Mathf.RoundToInt(100f * count / total) + "%", "num center");
            value.style.color = colour;
            return Ui.Box("an-zone", value, Ui.Caps(name, "caps center an-gap-4"));
        }

        static VisualElement Split(int a, int b, string aName, string bName)
        {
            VisualElement left = Ui.Box("split-a");
            left.style.width = Length.Percent(a);
            Color quiet = new Color32(48, 53, 58, 255);   // surface-3
            left.style.backgroundColor = a >= b ? Ui.Red : quiet;
            VisualElement right = Ui.Box("grow split-b");
            right.style.backgroundColor = b > a ? Ui.Red : quiet;
            return Ui.Box("", Ui.Box("row split", left, right), Ui.Box("row", Ui.Text(a + "% " + aName, "body-2 an-row-words", Ui.Medium), Ui.Text(b + "% " + bName, "body-2 an-row-end", Ui.Medium)).Margin(4));
        }

        // A spot's number: red under half the Tour's rate, amber up to it, bright green at Tour level and above.
        static Color Heat(int weighted) => weighted >= 100 ? Ui.Bright : weighted >= 50 ? Ui.Amber : Ui.Red;
    }
}
