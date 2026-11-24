// The coach's written plan, as puttr-app's coach/AnalysisCoach does it: a row with the coach's face and name that,
// tapped, has the coach read the Player's putts ("Analyzing ... takes about 30 seconds") and answers in a card with
// "Continue conversation", which carries the analysis into the coach chat (PuttrProChat continueFromAnalysis).
//
//   AnalysisBar   Full analysis: pinned under the page, "Want a plan to fix your weak spots?". The app's v2 path:
//                 POST ai.puttr.co/v2/coach/analyze {personaId, days} (signed in; the server reads the putts itself)
//                 -> {conversationId, answer, recommendedLessons}. The card says "Last N days".
//   GameRow       a played game: "<coach> - Ready to analyze •••". The app's legacy path, which GameView still uses
//                 (v2 has no explicit putt ids yet): the game's putts (GET /profile/game/putts/:id) summarised on the
//                 phone (CoachFunctions getPuttAssessment, getUserPrompt, getVertexAssessment), then
//                 POST ai.puttr.co/ask-coach {query, conversationId: "", user, userPrompt} -> {answer: [{content: [{text}]}], conversationId}.
//                 The card says "Session analysis".
// Both are Puttr Club (the paywall otherwise) and off for Kid profiles, whose chat is off.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>The words the legacy /ask-coach path sends, built from putt rows as the app builds them (pure, so they can be checked).</summary>
    public static class CoachPrompt
    {
        public sealed class Assessment
        {
            public int PercentMade, Left, Right, High, Low, PushPullPercent;
            public string PushPull = "none", SpeedConsistency = "consistent";
        }

        // RolloutFunctions: arrival-speed IQR cutoffs (in/tick) and the fewest putts that say anything.
        const double VeryConsistentIqr = 0.18, NotConsistentIqr = 0.50;
        const int MinPuttsForConsistency = 3;

        /// <summary>quantile: linear interpolation, p in [0, 1].</summary>
        public static double Quantile(List<double> values, double p)
        {
            var s = new List<double>(values);
            s.Sort();
            double pos = (s.Count - 1) * p;
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            return s[lo] + (s[hi] - s[lo]) * (pos - lo);
        }

        public static double Iqr(List<double> values) => values == null || values.Count < 2 ? 0 : Quantile(values, 0.75) - Quantile(values, 0.25);

        /// <summary>paceConsistency over arrival speeds.</summary>
        public static string PaceConsistency(List<double> arrivals)
        {
            if (arrivals.Count < MinPuttsForConsistency) return "consistent";
            double spread = Iqr(arrivals);
            if (spread < VeryConsistentIqr) return "very consistent";
            if (spread >= NotConsistentIqr) return "not consistent";
            return "consistent";
        }

        /// <summary>getPuttAssessment: made %, which way the ball goes (misses by side, makes by entry against the tee's line), high and low side on breaking makes, pace consistency.</summary>
        public static Assessment Assess(List<object> putts, string handedness)
        {
            var a = new Assessment();
            int made = 0;
            var arrivals = new List<double>();
            foreach (object p in putts)
            {
                int[][] bp; double[] hb;
                try { bp = AppPutt.Path(J.Str(p, "ballPath")); hb = AppPutt.Hole(J.Str(p, "holeBounds")); }
                catch (Exception) { continue; }
                if (hb == null || bp.Length == 0) { if (J.Int(p, "made") != 0) made++; continue; }
                double? v = AppPutt.ArrivalSpeed(AppPutt.AnalyzeRollout(bp, hb));
                if (v.HasValue) arrivals.Add(v.Value);
                if (J.Int(p, "made") == 0)
                {
                    string m = AppPutt.MissResult(bp, hb);
                    if (m == "RIGHT") a.Right++; else if (m == "LEFT") a.Left++;
                }
                else
                {
                    double entry = AppPutt.HoleEntry(bp, hb, out _);
                    int tee = J.Int(p, "tee");
                    double diff = entry - AppPutt.TeeEntry(J.Num(p, "distance"), tee);
                    if (diff > 0) a.Right++; else if (diff < 0) a.Left++;
                    if (tee == 1) { if (diff < 0) a.High++; else if (diff > 0) a.Low++; }
                    else if (tee == 3) { if (diff > 0) a.High++; else if (diff < 0) a.Low++; }
                    made++;
                }
            }
            a.PercentMade = putts.Count > 0 ? (int)AppPutt.JsRound(made * 100.0 / putts.Count) : 0;
            a.SpeedConsistency = PaceConsistency(arrivals);
            int both = a.Left + a.Right;
            bool lefty = handedness == "LEFTY", righty = handedness == "RIGHTY";
            if ((righty || lefty) && a.Left != a.Right)
            {
                bool leftWins = a.Left > a.Right;
                a.PushPull = leftWins == righty ? "pull" : "push";
                a.PushPullPercent = (int)AppPutt.JsRound((leftWins ? a.Left : a.Right) * 100.0 / both);
            }
            return a;
        }

        /// <summary>getUserPrompt: who the coach is helping, from the profile.</summary>
        public static string User(object profile)
        {
            var sb = new StringBuilder("You are assisting ");
            string first = J.Str(profile, "firstName");
            if (first.Length > 0) sb.Append(first).Append(", ");
            string hand = J.Str(profile, "handedness");
            if (hand == "RIGHTY") sb.Append("a right handed golfer ");
            else if (hand == "LEFTY") sb.Append("a left handed golfer ");
            string hcp = J.Str(profile, "realHandicap");
            sb.Append(hcp.Length == 0 || hcp == "NONE" ? "with no handicap. " : "with a " + hcp + " handicap. ");
            string grip = J.Str(J.Parse(J.Str(profile, "putterData")), "grip");
            if (grip.Length > 0) sb.Append("and uses a ").Append(grip).Append(" putting grip style ");
            return sb.ToString();
        }

        /// <summary>getVertexAssessment: the Vertex sensor's stroke table for the putts that carry sensorData, or null.</summary>
        public static string Vertex(List<object> putts)
        {
            var rows = new List<VertexPutt>();
            foreach (object p in putts) if (J.Str(p, "sensorData").Length > 0 && VertexSummary.Read(p) is VertexPutt v) rows.Add(v);
            if (rows.Count == 0) return null;
            CultureInfo inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("\n\n--- VERTEX PUTTER SENSOR DATA ---\n");
            sb.Append("The following stroke metrics were captured by a Vertex sensor attached to the putter:\n\n");
            sb.Append("Putt | Tempo (bpm) | Rhythm | Speed (m/s) | Face Δ (°) | Back (s) | Fwd (s) | Total (s) | Lie (°)\n");
            sb.Append("-----|-------------|--------|-------------|------------|----------|---------|-----------|--------\n");
            double tempo = 0, rhythm = 0, speed = 0, face = 0, back = 0, fwd = 0, total = 0, lie = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                VertexPutt m = rows[i];
                double t = m.BackTime + m.ForwardTime;
                sb.Append(i + 1).Append(" | ").Append(m.Tempo.ToString("0.#", inv)).Append(" | ").Append(m.Rhythm.ToString("0.##", inv)).Append(" | ").Append(m.Speed.ToString("0.##", inv))
                  .Append(" | ").Append(m.FaceChange.ToString("0.0", inv)).Append(" | ").Append(m.BackTime.ToString("0.##", inv)).Append(" | ").Append(m.ForwardTime.ToString("0.##", inv))
                  .Append(" | ").Append(t.ToString("0.##", inv)).Append(" | ").Append(m.Lie.ToString("0.#", inv)).Append('\n');
                tempo += m.Tempo; rhythm += m.Rhythm; speed += m.Speed; face += Math.Abs(m.FaceChange); back += m.BackTime; fwd += m.ForwardTime; total += t; lie += m.Lie;
            }
            int n = rows.Count;
            if (n > 1)
                sb.Append("AVG | ").Append((tempo / n).ToString("0.0", inv)).Append(" | ").Append((rhythm / n).ToString("0.00", inv)).Append(" | ").Append((speed / n).ToString("0.00", inv))
                  .Append(" | ").Append((face / n).ToString("0.0", inv)).Append(" | ").Append((back / n).ToString("0.00", inv)).Append(" | ").Append((fwd / n).ToString("0.00", inv))
                  .Append(" | ").Append((total / n).ToString("0.00", inv)).Append(" | ").Append((lie / n).ToString("0.0", inv)).Append('\n');
            sb.Append("\nInterpretation guide:\n");
            sb.Append("- Tempo: 60-80 bpm is ideal for most putters\n");
            sb.Append("- Rhythm (back/fwd ratio): 2:1 to 3:1 is optimal\n");
            sb.Append("- Face change: closer to 0° means square face through impact\n");
            sb.Append("- Speed consistency across putts indicates good touch\n");
            sb.Append("- Lie angle consistency indicates stable setup\n");
            sb.Append("\nPlease incorporate this sensor data into your coaching analysis.\n");
            return sb.ToString();
        }

        /// <summary>AnalysisCoach askCoach's query: the tendencies in words, then the sensor table, then the question.</summary>
        public static string Query(Assessment p, string vertex)
        {
            var sb = new StringBuilder("Here is an analysis of tendencies based on their putt data: ");
            sb.Append("They made ").Append(p.PercentMade).Append(" % of their recent putts. ");
            if (p.PushPullPercent != 0) sb.Append("Their push / pull tendency is ").Append(p.PushPull).Append(" which they do ").Append(p.PushPullPercent).Append("% of the time. ");
            else sb.Append("They have no push / pull tendency meaning they push the same as they pull. ");
            sb.Append("Their putt speed consistency is ").Append(p.SpeedConsistency).Append(". ");
            if (p.High != 0 && p.Low != 0) sb.Append("For breaking putts, they made ").Append(p.High).Append(" putts on the high side and ").Append(p.Low).Append(" putts on the low side. ");
            string feedback = (vertex ?? "") + "Based on this data, what are some things they should focus on to improve? ";
            return sb + " " + feedback;
        }

        /// <summary>The words in an /ask-coach answer (answer[0].content[0].text, or the first text found).</summary>
        public static string AskCoachText(object data)
        {
            foreach (object item in J.List(J.Get(data, "answer")))
                foreach (object part in J.List(J.Get(item, "content")))
                {
                    string text = J.Str(part, "text");
                    if (text.Length > 0) return text;
                }
            return "";
        }
    }

    public static class CoachPlan
    {
        static StyleSheet Sheet => Resources.Load<StyleSheet>("PuttrApp/Improve");

        /// <summary>
        /// The Full analysis bar, pinned under the page: the coach's face and name, "Want a plan to fix your weak spots?" and
        /// a round arrow. days is the range the page shows, which the coach analyses (v2Days). Hidden for a Kid profile.
        /// </summary>
        public static VisualElement AnalysisBar(App app, Func<int> days, out Action refresh)
        {
            var row = new AskRow(app, "Want a plan to fix your weak spots?", "Analyzing your recent putts... takes about 30 seconds", false, true);
            refresh = row.Refresh;
            row.Ask = () => App.Run(Recent(app, row, days()));
            VisualElement bar = Ui.Box("cp-bar", Ui.Box("cp-card cp-card-green", row.Root));
            if (Sheet != null) bar.styleSheets.Add(Sheet);
            if (Kid.IsKid(app.Profile)) bar.style.display = DisplayStyle.None;
            return bar;
        }

        /// <summary>
        /// GameView's coach row for a played game: "&lt;coach&gt; - Ready to analyze •••". Tapped, the coach reads that game's
        /// putts and answers in a card with "Continue conversation". Hidden for a Kid profile. Place it in a padded column.
        /// </summary>
        public static VisualElement GameRow(App app, string gameId)
        {
            var row = new AskRow(app, "Ready to analyze", "Analyzing your session... takes about 30 seconds", true, false);
            row.Ask = () => App.Run(Game(app, row, gameId));
            VisualElement card = Ui.Box("cp-card", row.Root);
            if (Sheet != null) card.styleSheets.Add(Sheet);
            // The coach the Player picked, once the settings are read (the game page may be the first to need them).
            App.Run(Api.Get("/coach/settings", d => { CoachVoice.Load(d); row.Refresh(); }, _ => { }));
            if (Kid.IsKid(app.Profile)) card.style.display = DisplayStyle.None;
            return card;
        }

        /// <summary>One row: the face (a spinner while the coach works), the name over the line, and the green arrow.</summary>
        sealed class AskRow
        {
            public readonly VisualElement Root;
            public Action Ask;
            public bool Busy;
            readonly App _app;
            readonly VisualElement _lead;
            readonly Label _name, _line, _dots;
            readonly string _intro, _working;

            public AskRow(App app, string intro, string working, bool dots, bool compact)
            {
                _app = app;
                _intro = intro;
                _working = working;
                _lead = Ui.Box("cp-lead");
                _name = Ui.Text("", "cp-name", Ui.Bold);
                _line = Ui.Text(intro, "cp-line", Ui.Regular);
                _dots = dots ? Ui.Text("•••", "cp-dots", Ui.Regular) : null;
                VisualElement go = Ui.Box("cp-go", Ui.Icon("CaretRight", 16, Color.white));
                Root = Ui.Box(compact ? "cp-row cp-row-compact" : "cp-row", _lead, Ui.Box("grow cp-words", _name, Ui.Box("row cp-line-row", _line, _dots)), go)
                    .OnTap(() => { if (Busy) return; Club.Require(_app, "Your putting coach", () => { if (!Busy) Ask?.Invoke(); }); });
                Refresh();
            }

            /// <summary>The coach's face and name again (after /coach/settings), or the spinner and the working line.</summary>
            public void Refresh()
            {
                _lead.Clear();
                Coaches.Persona coach = CoachVoice.Persona;
                _name.text = coach != null ? coach.Name : "Putting Coach";
                if (Busy) _lead.Add(Ui.Box("cp-spin", Spinner.Small()));
                else
                {
                    VisualElement face = coach != null ? Coaches.Avatar(coach) : Ui.Avatar("Putting Coach", null);
                    face.AddToClassList("cp-face");
                    _lead.Add(face);
                }
                _line.text = Busy ? _working : _intro;
                if (_dots != null) _dots.style.display = Busy ? DisplayStyle.None : DisplayStyle.Flex;
            }

            public void Working(bool on) { Busy = on; Refresh(); }
        }

        // ── The two asks ─────────────────────────────────────────────────────

        static IEnumerator Recent(App app, AskRow row, int days)
        {
            row.Working(true);
            var body = new Dictionary<string, object> { { "personaId", CoachVoice.Persona.Id } };
            if (days > 0) body["days"] = days;
            long status = 0; string text = null;
            yield return ChatUi.Post(ChatUi.Ai + "/v2/coach/analyze", body, (s, t) => { status = s; text = t; }, 90);
            row.Working(false);
            object data = J.Parse(text);
            if (status == 200 && J.Str(data, "answer").Length > 0)
            {
                Show(app, row.Root, days == 1 ? "Last 24 hours" : days > 0 ? "Last " + days + " days" : "Recent putts", J.Str(data, "answer"), J.Str(data, "conversationId"), data);
                yield break;
            }
            string code = J.Str(data, "error");
            if (status == 400 && (code == "no_putts" || code == "no_valid_putts"))
                Say(row.Root, "Almost ready", "I don't have analyzable putts for this session yet. If it just finished recording, give it a minute to process, then try again.");
            else Say(row.Root, "Coach unavailable", "I couldn't analyze your putts right now. Please try again in a moment.");
        }

        static IEnumerator Game(App app, AskRow row, string gameId)
        {
            List<object> putts = null;
            yield return Api.Get("/profile/game/putts/" + gameId, d => putts = J.List(d), _ => putts = new List<object>());
            // As the app: nothing to read, nothing asked.
            if (putts == null || putts.Count == 0) yield break;
            row.Working(true);
            object profile = app.Profile;
            string query = CoachPrompt.Query(CoachPrompt.Assess(putts, J.Str(profile, "handedness")), CoachPrompt.Vertex(putts));
            var body = new Dictionary<string, object> { { "query", query }, { "conversationId", "" }, { "user", J.Str(profile, "userId") }, { "userPrompt", CoachPrompt.User(profile) } };
            long status = 0; string text = null;
            yield return ChatUi.Post(ChatUi.Ai + "/ask-coach", body, (s, t) => { status = s; text = t; }, 90);
            row.Working(false);
            object data = J.Parse(text);
            string answer = status == 200 ? CoachPrompt.AskCoachText(data) : "";
            if (answer.Length == 0) { Say(row.Root, "Coach unavailable", "I couldn't analyze your putts right now. Please try again in a moment."); yield break; }
            Show(app, row.Root, "Session analysis", answer, J.Str(data, "conversationId"), null);
        }

        // ── The answer ───────────────────────────────────────────────────────

        /// <summary>The coach's card: face, name and what was read, the written analysis with any lessons it recommends, and Continue conversation.</summary>
        static void Show(App app, VisualElement over, string scope, string answer, string conversationId, object recs)
        {
            VisualElement dim = null;
            void Close() { dim?.RemoveFromHierarchy(); }
            Coaches.Persona coach = CoachVoice.Persona;
            VisualElement face = Coaches.Avatar(coach);
            face.AddToClassList("cp-modal-face");
            VisualElement head = Ui.Box("row cp-modal-head", face, Ui.Box("grow", Ui.Text(coach.Name, "cp-modal-name", Ui.Bold), Ui.Text(scope, "cp-modal-scope", Ui.Regular)));
            ScrollView scroll = Ui.Scroll();
            scroll.AddToClassList("cp-modal-scroll");
            Label words = Ui.Text(ChatUi.Rich(answer), "cp-answer", Ui.Regular);
            words.enableRichText = true;
            scroll.Add(words);
            VisualElement cards = recs != null ? ChatScreenRecs(app, recs, Close) : null;
            if (cards != null) scroll.Add(cards);
            VisualElement go = Ui.Button("Continue conversation", () => { Close(); CoachChatScreen.Continue(app, conversationId, answer); }, "btn cp-continue");
            go.Insert(0, Ui.Icon("ChatCircle", 18, Color.white));
            VisualElement card = Ui.Box("modal cp-modal", head, Ui.Box("cp-divider"), scroll, go);
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(Close);
            if (Sheet != null) dim.styleSheets.Add(Sheet);
            App.Layer(over).Add(dim);
        }

        static VisualElement ChatScreenRecs(App app, object recs, Action close) => CoachChatScreen.RecCards(app, recs, close);

        /// <summary>The app's Alert: a title, the words and OK.</summary>
        static void Say(VisualElement over, string title, string text)
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Title(title, "title-20"), Ui.Text(text, "sub", Ui.Regular).Margin(8), Ui.Button("OK", () => dim.RemoveFromHierarchy()));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
        }
    }
}
