// Free putt: "a driving range for putting". Its setup (your own tees or random ones, a goal or none) is on
// the game page like every game (GameScreen); Start hands over to the 3D game (PuttrArcade.FreePuttMode: the
// ramp, the hole and every putt replayed on it) with the session's numbers beside it (PracticePanel).
//
// Not brought over yet: the Vertex sensor and the watch.
using System.Collections.Generic;
using Puttr;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    /// <summary>
    /// Practice (free putt): its row, its setup on the game page (GameScreen), and small pieces other screens share.
    /// </summary>
    public static class FreePuttScreen
    {
        public const string GameType = "OPEN";

        /// <summary>Practice as a game row, for the entries that are not a catalog card (Home's Start putting, Improve).</summary>
        public static object Game => J.Parse("{\"title\":\"Practice\",\"gameType\":\"OPEN\",\"description\":\"A driving range for putting. No score, no pressure: every putt is measured and saved to your stats.\",\"multiPlayer\":0}");

        /// <summary>Opens Practice's page.</summary>
        public static void Open(App app) => app.Push(new GameScreen(app, Game));

        /// <summary>
        /// Open.js's setup, redrawn into the given box: SESSION FOCUS (what the session's read leads with), SESSION GOAL
        /// (none, makes in a row, total putts, and its target), and TEES (a fixed tee you pick, or a new spot every putt).
        /// </summary>
        public static void Setup(VisualElement into, PracticeSession session)
        {
            into.Clear();
            if (PracticePanel.Sheet != null && !into.styleSheets.Contains(PracticePanel.Sheet)) into.styleSheets.Add(PracticePanel.Sheet);
            void Again() => Setup(into, session);

            into.Add(Form.Label("Session focus"));
            into.Add(Tiles(new[] { ("Target", "Overall", "All stats"), ("Lightning", "Speed", "Pace control"), ("RadioButton", "Entry", "Accuracy") },
                (int)session.Focus, i => { session.Focus = (PracticeFocus)i; PracticeSession.LastFocus = session.Focus; Again(); }));

            into.Add(Form.Label("Session goal"));
            into.Add(Tiles(new[] { ("Fire", "Free play", "No target"), ("ListNumbers", "Make streak", "Consecutive"), ("Target", "Total putts", "Volume") },
                (int)session.Goal, i =>
                {
                    var goal = (PracticeGoal)i;
                    if (goal != session.Goal && goal != PracticeGoal.Free) session.Target = PracticeSession.DefaultTarget(goal);
                    session.Goal = goal;
                    Again();
                }));
            if (session.Goal != PracticeGoal.Free)
            {
                int[] targets = PracticeSession.TargetsFor(session.Goal);
                if (System.Array.IndexOf(targets, session.Target) < 0) session.Target = PracticeSession.DefaultTarget(session.Goal);
                VisualElement row = Ui.Box("pr-targets");
                for (int i = 0; i < targets.Length; i++)
                {
                    int t = targets[i];
                    VisualElement cell = Ui.Box(t == session.Target ? "pr-target pr-target-on" : "pr-target",
                        Ui.Text(t.ToString(), "pr-target-num", Ui.Bold), Ui.Text(session.Goal == PracticeGoal.Streak ? "in a row" : "putts", "pr-target-sub", Ui.Regular))
                        .OnTap(() => { session.Target = t; Again(); });
                    if (i == targets.Length - 1) cell.style.marginRight = 0;
                    row.Add(cell);
                }
                into.Add(row);
            }

            into.Add(Form.Label("Tees"));
            into.Add(Tiles(new[] { ("Target", "Fixed tee", "You pick the spot"), ("Cube", "Random tees", "New spot every putt") },
                session.RandomTees ? 1 : 0, i => { session.RandomTees = i == 1; Again(); }));
            if (session.RandomTees)
            {
                into.Add(Ui.Text("A new distance and tee after every putt, between:", "sub", Ui.Regular).Margin(12));
                into.Add(Ui.Box("row",
                    Stepper(() => session.RangeMin + " ft", d => { session.RangeMin = Mathf.Clamp(session.RangeMin + d, PracticeSession.MinFeet, session.RangeMax); }),
                    Ui.Text("and", "sub center", Ui.Regular).Width(48),
                    Stepper(() => session.RangeMax + " ft", d => { session.RangeMax = Mathf.Clamp(session.RangeMax + d, session.RangeMin, PracticeSession.MaxFeet); })));
            }
            else into.Add(TeePicker(session, Again));
        }

        // Open.js PillRow: a row of tiles, each an icon, a word and a line under it, one lit.
        internal static VisualElement Tiles((string icon, string label, string sub)[] options, int chosen, System.Action<int> pick)
        {
            VisualElement row = Ui.Box("pr-tiles");
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                bool on = i == chosen;
                VisualElement icon = Ui.Icon(options[i].icon, 20, on ? Color.white : (Color)Ui.Faint);
                icon.AddToClassList("pr-tile-icon");
                VisualElement tile = Ui.Box(on ? "pr-tile pr-tile-on" : "pr-tile", icon, Ui.Text(options[i].label, "pr-tile-label", Ui.Bold), Ui.Text(options[i].sub, "pr-tile-sub", Ui.Regular))
                    .OnTap(() => pick(index));
                if (i == options.Length - 1) tile.style.marginRight = 0;
                row.Add(tile);
            }
            return row;
        }

        // ── Small pieces ─────────────────────────────────────────────────────

        internal static VisualElement Space(float height)
        {
            var space = new VisualElement();
            space.style.height = height;
            return space;
        }

        internal static VisualElement Stat(string value, string label) => Ui.Box("grow stat", Ui.Num(value, "stat-value"), Ui.Caps(label, "caps center"));

        // A row of options, one lit. The light follows the tap itself, so a caller that only reloads its list (and never
        // draws the row again) still shows which tab is on.
        internal static VisualElement Choice(string[] names, int chosen, System.Action<int> pick)
        {
            VisualElement row = Ui.Box("choice");
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                row.Add(Ui.Box(i == chosen ? "choice-item choice-on" : "choice-item", Ui.Text(names[i], "choice-text", Ui.Bold)).OnTap(() =>
                {
                    for (int k = 0; k < row.childCount; k++) row[k].EnableInClassList("choice-on", k == index);
                    pick(index);
                }));
            }
            return row;
        }

        // [ - ]  value  [ + ]
        internal static VisualElement Stepper(System.Func<string> value, System.Action<int> step)
        {
            Label label = Ui.Text(value(), "stepper-value", Ui.Bold);
            VisualElement Button(string sign, int by) => Ui.Box("stepper-button", Ui.Text(sign, "stepper-sign", Ui.Bold)).OnTap(() => { step(by); label.text = value(); });
            return Ui.Box("stepper grow", Button("−", -1), label, Button("+", 1));
        }

        // The distance and the tee in one row; tapping it opens the mat's 27 spots (Frame.SpotRow, as Open.js).
        internal static VisualElement TeePicker(PracticeSession session, System.Action changed)
        {
            VisualElement row = Frame.SpotRow(session.Feet, session.Tee, (f, t) => { session.Feet = f; session.Tee = t; changed(); });
            row.style.marginTop = 10;
            return row;
        }
    }

    /// <summary>
    /// A putt seen from above: the last few inches of the mat, the hole, and the ball's path over
    /// the camera's frames. The SDK gives the path in inches from the hole's centre, x to the
    /// Player's right and y back toward the Player, which is how it is drawn.
    /// </summary>
    public sealed class PuttView : VisualElement
    {
        const float Before = 10.5f, Past = 4.5f; // inches shown before the hole's centre and past it
        readonly PracticePutt _putt;

        public PuttView(PracticePutt putt)
        {
            _putt = putt;
            AddToClassList("putt-view");
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0f || h <= 0f) return;
            // The mat is far wider than this window onto it, so it fills the picture.
            float scale = h / (Before + Past);
            Vector2 hole = new Vector2(w * 0.5f, Past * scale);
            Vector2 At(Vector2 inches) => hole + new Vector2(inches.x, inches.y) * scale;
            Painter2D p = context.painter2D;

            // The mat, then the hole.
            p.fillColor = new Color32(20, 74, 34, 255);
            p.BeginPath();
            p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(w, 0)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(0, h));
            p.ClosePath(); p.Fill();
            p.fillColor = new Color32(5, 6, 6, 255);
            p.BeginPath(); p.Arc(hole, PuttrAnalysis.HoleDiameterIn * 0.5f * scale, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();

            Vector2[] path = _putt.PathInches;
            if (path == null || path.Length == 0) return;
            Color line = _putt.Made ? Ui.Bright : Ui.Red;
            p.strokeColor = line;
            p.lineWidth = Mathf.Max(2f, 0.35f * scale);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(At(path[0]));
            for (int i = 1; i < path.Length; i++) p.LineTo(At(path[i]));
            p.Stroke();
            // The ball, where the camera last saw it.
            p.fillColor = Color.white;
            p.BeginPath(); p.Arc(At(path[path.Length - 1]), PuttrAnalysis.BallDiameterIn * 0.5f * scale, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
        }
    }

    static class Sizing
    {
        public static T Width<T>(this T element, float width) where T : VisualElement
        {
            element.style.width = width;
            element.style.flexShrink = 0;
            return element;
        }

        public static T Right<T>(this T element) where T : VisualElement
        {
            element.style.unityTextAlign = TextAnchor.MiddleRight;
            return element;
        }
    }
}
