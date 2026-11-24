// The session beside the picture, as puttr-app's Practice (games/Open.js renderPlaying): where to putt from, the tempo
// metronome, the goal, the numbers (Makes, Misses, Made, the streak and the quality), the session's read in the order
// its focus asks for, and every putt. After each putt a result card (the ball's path into the hole, the entry, the
// grade, the speed) comes up over everything and goes again after three seconds.
//
// It is one of the app's own panels, drawn over the part of the screen the 3D game leaves free: the bottom of an
// upright phone, the right of one on its side.
using System;
using System.Collections.Generic;
using System.Globalization;
using PuttrArcade;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class PracticePanel
    {
        // Open.js's colours: COLORS.primaryGreen, the app's red, the pace bar's blue (short) and orange (long).
        static readonly Color Win = Ui.Green, LightGreen = Ui.Bright, Red = Ui.Red, Blue = Ui.Blue;
        // The guidelines' colours: amber where the app had orange, text-2 for a quiet icon, surface-3 for the PGA Tour's bar.
        static readonly Color Orange = Ui.Amber;
        static readonly Color Dim = Ui.Faint;
        static readonly Color Tour = new Color32(48, 53, 58, 255);
        // Brand green is a fill; as text or an icon it is the bright green.
        static Color AsText(Color colour) => colour == Win ? LightGreen : colour;

        static StyleSheet _sheet;
        /// <summary>Practice.uss: the setup tiles, the session's cards, the putt history and the result card.</summary>
        public static StyleSheet Sheet => _sheet != null ? _sheet : _sheet = Resources.Load<StyleSheet>("PuttrApp/Practice");

        public readonly VisualElement Root;
        readonly App _app;
        readonly PracticeSession _session;
        readonly Action _end;
        readonly ScrollView _scroll;
        bool _wide, _laidOut, _offline;

        public PracticePanel(App app, PracticeSession session, Action end)
        {
            _app = app;
            _session = session;
            _end = end;
            Root = Ui.Box("sheet");
            Root.Face(Ui.Medium);
            if (Sheet != null) Root.styleSheets.Add(Sheet);
            _scroll = Ui.Scroll();
            // Above the cards and never scrolled away: why a putt did not count, "Puttr not connected", and a tester's test putt buttons.
            Root.Add(new PuttStrip(app).Root);
            Root.Add(_scroll);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Root.schedule.Execute(Place).Every(300); // the phone may be turned at any time
            Show();
        }

        // Under the picture when upright, beside it when wide: the shares are the game's own.
        void Place()
        {
            bool wide = Screen.width > Screen.height;
            // The Puttr coming or going changes the first line of the sheet.
            if (_laidOut && PuttStrip.SaysOffline(_app.Profile) != _offline) Show();
            if (_laidOut && wide == _wide) return;
            _laidOut = true;
            _wide = wide;
            Vector4 safe = _app.SafeInsets(); // left, bottom, right, top
            Root.EnableInClassList("sheet-wide", wide);
            Root.style.left = wide ? StyleKeyword.Auto : 0;
            Root.style.top = wide ? 0 : StyleKeyword.Auto;
            Root.style.right = 0;
            Root.style.bottom = 0;
            Root.style.width = wide ? Length.Percent((1f - FreePuttMode.WideShare) * 100f) : StyleKeyword.Auto;
            Root.style.height = wide ? StyleKeyword.Auto : Length.Percent((1f - FreePuttMode.UprightShare) * 100f);
            Root.style.paddingBottom = safe.y;
            Root.style.paddingRight = wide ? safe.z : 0f;
            Root.style.paddingTop = wide ? Mathf.Max(safe.w, 64f) : 0f; // wide: clear of the arcade's pause button, top right
            Show();
        }

        /// <summary>A putt has been added to the session: the panel again, and the putt's result card over it.</summary>
        public void Putted(bool goalJustMet)
        {
            Show();
            if (_session.Total > 0) ShowResult(_session.Putts[_session.Total - 1], _session.Total - 1, true);
        }

        /// <summary>Kept for callers that only redraw: the goal's own card says when it is met.</summary>
        public void Show(bool goalJustMet) => Show();

        /// <summary>Draws the session as it stands.</summary>
        public void Show()
        {
            Perf.Wake(3f);
            _scroll.Clear();
            _offline = PuttStrip.SaysOffline(_app.Profile);
            PracticeStats stats = _session.Stats();

            // Where to putt from: the tee marker (the distance, the ball on its tee) and one button to change it.
            // Ending the session is pause, then Exit, as in every game.
            VisualElement buttons = Ui.Box("row");
            if (!_session.RandomTees) buttons.Add(Ui.Box("icon-btn", Ui.Icon("Target", 22, Ui.Bright)).OnTap(OpenTees));
            VisualElement where = Ui.Box("sheet-card", Ui.Box("row tee-from", Frame.TeeMarker(_session.Feet, _session.Tee), buttons));
            string range = _session.RangeMin == _session.RangeMax ? _session.RangeMin + " ft" : _session.RangeMin + "–" + _session.RangeMax + " ft";
            where.Add(Ui.Text(_session.RandomTees ? "Random tees · " + range : "Tap the target to change tee", "pr-caption", Ui.Medium));
            _scroll.Add(where);

            // The tempo trainer, under the tee (Open.js TempoMetronome).
            _scroll.Add(Ui.Box("sheet-card pr-metronome", Metronome.Pill()));

            VisualElement goal = Goal(stats);
            if (goal != null) _scroll.Add(goal);

            if (stats == null)
            {
                _scroll.Add(Ui.Box("pr-empty",
                    Ui.Text(_offline ? "Connect your Puttr to begin" : "No putts yet", "pr-empty-title", Ui.Medium),
                    Ui.Text(_offline ? "Every putt is replayed above and read here." : "Roll a ball to get started", "pr-empty-sub", Ui.Regular)));
            }
            else
            {
                _scroll.Add(Primary(stats));
                _scroll.Add(StreakAndQuality(stats));
                if (stats.Total >= 2)
                {
                    var read = new List<VisualElement>();
                    VisualElement speed = SpeedAnalysis(stats), tendency = MissTendency(stats), entry = EntryQuality(stats), distance = DistanceBreakdown(stats), breaks = BreakAnalysis(stats);
                    // Open.js renderFocusAnalytics: Speed puts pace first, Entry puts the entry first; Overall is the same order as Speed.
                    if (_session.Focus == PracticeFocus.Entry) read.AddRange(new[] { entry, distance, breaks, speed, tendency });
                    else read.AddRange(new[] { speed, tendency, entry, distance, breaks });
                    _scroll.Add(Section("Session analytics"));
                    foreach (VisualElement card in read) if (card != null) _scroll.Add(card);
                }
                _scroll.Add(Section("Putt history"));
                // Newest first, numbered as the app numbers its cards (the newest is 1).
                for (int i = _session.Total - 1, n = 0; i >= 0 && n < 100; i--, n++)
                {
                    PracticePutt p = _session.Putts[i];
                    int shown = n;
                    _scroll.Add(HistoryRow(p, shown).OnTap(() => ShowResult(p, shown, false)));
                }
            }
            _scroll.Add(FreePuttScreen.Space(16));
            _scroll.scrollOffset = Vector2.zero;
        }

        // ── The cards ────────────────────────────────────────────────────────

        static Label Coloured(string text, string classes, Font font, Color colour)
        {
            Label l = Ui.Text(text, classes, font);
            l.style.color = AsText(colour);
            return l;
        }

        static VisualElement Section(string title) => Ui.Caps(title, "caps pr-section");

        // A small icon and its label, the head of a card ("Streak", "Quality", "Speed analysis").
        static VisualElement Head(string icon, Color tint, string label, VisualElement right = null)
        {
            VisualElement head = Ui.Box("pr-head", Ui.Icon(icon, 14, AsText(tint)), Ui.Text(label, "pr-head-label grow", Ui.Medium));
            if (right != null) head.Add(right);
            return head;
        }

        // A thin bar: share 0 to 1 of its width in a colour.
        static VisualElement Thin(float share, Color colour, string classes = "pr-thin")
        {
            VisualElement fill = Ui.Box("pr-thin-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100f);
            fill.style.backgroundColor = colour;
            return Ui.Box(classes, fill);
        }

        // renderGoalProgress
        VisualElement Goal(PracticeStats stats)
        {
            if (_session.Goal == PracticeGoal.Free || stats == null) return null;
            bool met = _session.GoalMet;
            int now = _session.Goal == PracticeGoal.Total ? stats.Total : stats.StreakOfMakes ? stats.CurrentStreak : 0;
            float share = Mathf.Min(now / (float)Mathf.Max(1, _session.Target), 1f);
            string label = _session.Goal == PracticeGoal.Total ? now + " / " + _session.Target + " putts" : now + " / " + _session.Target + " consecutive makes";
            Label title = Coloured(met ? "Goal reached!" : _session.Goal == PracticeGoal.Streak ? "Make streak goal" : "Putt volume goal", "pr-goal-title grow", Ui.Bold, met ? Win : Color.white);
            VisualElement card = Ui.Box(met ? "sheet-card pr-goal-met" : "sheet-card",
                Ui.Box("row", met ? Ui.Icon("FillFire", 16, Orange) : Ui.Icon("Target", 14, Dim), title, Ui.Text(label, "pr-goal-count", Ui.Medium)),
                Thin(met ? 1f : share, Win));
            return card;
        }

        // renderPrimaryStats: Makes, Misses, Made.
        static VisualElement Primary(PracticeStats s)
        {
            VisualElement Tile(string value, string label, Color colour) => Ui.Box("pr-stat", Coloured(value, "pr-stat-num", Ui.Bold, colour), Ui.Text(label, "pr-stat-label", Ui.Medium));
            VisualElement last = Tile(s.MakePercent + "%", "Made", s.MakePercent >= 50 ? Win : Color.white);
            last.style.marginRight = 0;
            return Ui.Box("pr-stats", Tile(s.Makes.ToString(), "Makes", Color.white), Tile(s.Misses.ToString(), "Misses", Color.white), last);
        }

        // renderStreakAndScore: the run going now, and the session's quality (its average grade and best putt).
        static VisualElement StreakAndQuality(PracticeStats s)
        {
            bool hot = s.StreakOfMakes && s.CurrentStreak >= 3;
            VisualElement streak = Ui.Box("pr-card pr-half",
                Head("FillFire", hot ? Orange : Dim, "Streak"),
                Coloured(s.CurrentStreak.ToString(), "pr-big", Ui.Bold, hot ? Orange : Color.white),
                Ui.Text((s.StreakOfMakes ? "makes" : "misses") + " in a row", "pr-small", Ui.Regular));
            if (s.LongestMakeStreak >= 3) streak.Add(Ui.Text("Best: " + s.LongestMakeStreak + " makes", "pr-best", Ui.Medium));
            Color grade = s.AvgScore >= 70 ? Win : s.AvgScore >= 40 ? Color.white : Red;
            VisualElement quality = Ui.Box("pr-card pr-half pr-last",
                Head("Target", Dim, "Quality"),
                Ui.Box("pr-baseline", Coloured(s.AvgGrade, "pr-big", Ui.Bold, grade), Ui.Text("avg", "pr-avg", Ui.Regular)),
                Ui.Text("Best: " + s.BestGrade + " (" + s.BestScore + ")", "pr-best", Ui.Medium));
            return Ui.Box("pr-stats", streak, quality);
        }

        // renderSpeedAnalysis: the average pace and how consistent it was.
        static VisualElement SpeedAnalysis(PracticeStats s)
        {
            if (s.Total < 2 || s.AvgSpeed == null) return null;
            double avg = s.AvgSpeed.Value;
            Color colour = avg >= -2 && avg <= 12 ? Win : avg < -2 ? Blue : Orange;
            Color steady = s.SpeedConsistency == "Very consistent" ? Win : s.SpeedConsistency == "Consistent" ? Orange : Red;
            return Ui.Box("sheet-card",
                Head("Lightning", colour, "Speed analysis"),
                Ui.Box("row pr-top",
                    Ui.Box("pr-col", Coloured(AppPutt.FmtRollout(avg), "pr-mid", Ui.Bold, colour), Ui.Text("Avg pace", "pr-small", Ui.Regular)),
                    Ui.Box("pr-col", Coloured(s.SpeedConsistency, "pr-word", Ui.Bold, steady),
                        Ui.Text(s.SpeedSpread.ToString("0.0", CultureInfo.InvariantCulture) + " in/s spread · " + AppPutt.FmtRollout(s.MinSpeed) + " to " + AppPutt.FmtRollout(s.MaxSpeed), "pr-small wrap", Ui.Regular))));
        }

        // renderMissTendency: where the misses go, once there are three.
        static VisualElement MissTendency(PracticeStats s)
        {
            if (s.TotalMisses < 3) return null;
            int left = (int)AppPutt.JsRound(100.0 * s.MissLeft / s.TotalMisses), right = 100 - left;
            VisualElement bar = Ui.Box("pr-split");
            VisualElement l = Ui.Box("pr-split-part"), r = Ui.Box("pr-split-part");
            l.style.flexGrow = left; l.style.backgroundColor = Blue;
            r.style.flexGrow = right; r.style.backgroundColor = Orange;
            if (left > 0) bar.Add(l);
            if (right > 0) bar.Add(r);
            return Ui.Box("sheet-card",
                Head("Target", Dim, "Miss tendency", Coloured(s.Tendency, "pr-head-right", Ui.Bold, s.Tendency == "Neutral" ? Win : Orange)),
                bar,
                Ui.Box("row", Coloured("Left " + left + "%", "pr-small-strong grow", Ui.Medium, Blue), Coloured("Right " + right + "%", "pr-small-strong", Ui.Medium, Orange)));
        }

        // renderEntryQuality: how the makes went in, once there are three.
        static VisualElement EntryQuality(PracticeStats s)
        {
            if (s.Makes < 3) return null;
            int centre = (int)AppPutt.JsRound(100.0 * s.CenterCuts / s.Makes);
            VisualElement card = Ui.Box("sheet-card", Head("Target", Dim, "Entry quality", Coloured(centre + "% center", "pr-head-right", Ui.Bold, centre >= 50 ? Win : Color.white)));
            void Row(string label, int count, Color colour)
            {
                VisualElement dot = Ui.Box("pr-dot");
                dot.style.backgroundColor = colour;
                card.Add(Ui.Box("pr-entry", dot, Ui.Text(label, "pr-entry-label grow", Ui.Medium), Ui.Text(count.ToString(), "pr-entry-count", Ui.Bold)));
            }
            Row("Center cut", s.CenterCuts, Win);
            Row("Left side", s.LeftEntries, Blue);
            Row("Right side", s.RightEntries, Orange);
            if (s.Backdoors > 0) Row("Backdoor", s.Backdoors, Red);
            return card;
        }

        // renderDistanceBreakdown: make % at each distance against the PGA Tour's.
        static VisualElement DistanceBreakdown(PracticeStats s)
        {
            if (s.Distances.Count < 2) return null;
            VisualElement card = Ui.Box("sheet-card", Head("ListNumbers", Dim, "Make % by distance"));
            foreach ((int feet, int makes, int total, int pct, int? pga) d in s.Distances)
            {
                bool beating = d.pga != null && d.pct >= d.pga.Value;
                card.Add(Ui.Box("pr-dist",
                    Ui.Box("row", Ui.Text(d.feet + "ft", "pr-dist-ft grow", Ui.Bold), Coloured(d.pct + "%", "pr-dist-pct", Ui.Bold, beating ? Win : Color.white), Ui.Text("(" + d.makes + "/" + d.total + ")", "pr-dist-of", Ui.Regular)),
                    Thin(Mathf.Max(d.pct, 5) / 100f, beating ? Win : Color.white)));
                if (d.pga != null) card.Add(Thin(Mathf.Max(d.pga.Value, 5) / 100f, Tour, "pr-thin pr-pga"));
            }
            VisualElement you = Ui.Box("pr-key"); you.style.backgroundColor = Color.white;
            VisualElement tour = Ui.Box("pr-key pr-key-thin"); tour.style.backgroundColor = Tour;
            card.Add(Ui.Box("row pr-legend", you, Ui.Text("You", "pr-legend-text", Ui.Regular), tour, Ui.Text("PGA Tour", "pr-legend-text", Ui.Regular)));
            return card;
        }

        // renderBreakAnalysis: make % from each tee.
        static VisualElement BreakAnalysis(PracticeStats s)
        {
            if (s.Breaks.Count < 2) return null;
            VisualElement row = Ui.Box("pr-stats");
            for (int i = 0; i < s.Breaks.Count; i++)
            {
                (int tee, string label, int makes, int total, int pct) b = s.Breaks[i];
                Color colour = b.pct >= 60 ? Win : b.pct >= 40 ? Color.white : Red;
                VisualElement tile = Ui.Box("pr-stat pr-break", Coloured(b.pct + "%", "pr-break-pct", Ui.Bold, colour), Ui.Text(b.label, "pr-stat-label", Ui.Medium), Ui.Text(b.makes + "/" + b.total, "pr-break-of", Ui.Regular));
                if (i == s.Breaks.Count - 1) tile.style.marginRight = 0;
                row.Add(tile);
            }
            return row;
        }

        // ── A putt (PuttCard.js computePuttDisplay) ──────────────────────────

        // The pace bar: green from dying at the center to a foot past, blue short, orange long; drawn from 15 in short to 30 in past.
        static Color PaceColour(double? r) => r != null && r.Value >= -2 && r.Value <= 12 ? Win : r != null && r.Value < -2 ? Blue : Orange;
        static float PaceShare(double? r) => r == null ? 0.05f : Mathf.Clamp((float)((r.Value + 15) / 45), 0.05f, 1f);

        // PuttCard: one row of the history. The grade and score, the putt's number, distance and break, the pace bar, the entry.
        static VisualElement HistoryRow(PracticePutt p, int index)
        {
            int score = (int)AppPutt.JsRound(p.Score);
            Color grade = score < 50 ? Red : score < 70 ? Color.white : LightGreen;
            Color accent = p.Made ? Win : Red;
            VisualElement edge = Ui.Box("pr-hist-edge");
            edge.style.backgroundColor = accent;
            VisualElement badge = Ui.Box("pr-badge", Ui.Text((index + 1).ToString(), "pr-badge-text", Ui.Bold));
            badge.style.backgroundColor = accent;
            Color paceColour = PaceColour(p.Rollout);
            Color resultColour = p.Made ? LightGreen : Red;
            VisualElement result = Ui.Box("row pr-hist-result");
            if (p.Made) { VisualElement dot = Ui.Box("pr-dot pr-dot-small"); dot.style.backgroundColor = resultColour; result.Add(dot); }
            else result.Add(Ui.Icon("X", 10, Red));
            result.Add(Coloured(p.ResultText + (p.Made ? " · " + Math.Abs(p.OffLine).ToString("0.0", CultureInfo.InvariantCulture) + "°" : ""), "pr-hist-result-text", Ui.Medium, resultColour));
            return Ui.Box("pr-hist",
                edge,
                Ui.Box("pr-hist-grade", Coloured(p.Grade ?? AppPutt.Grade(p.Score), "pr-hist-letter", Ui.Bold, grade), Coloured(score.ToString(), "pr-hist-score", Ui.Regular, grade)),
                Ui.Box("pr-hist-main",
                    Ui.Box("row", badge, Ui.Text(p.Feet + "ft", "pr-hist-ft", Ui.Bold), Ui.Text(PracticePutt.BreakLabel(p.Tee), "pr-hist-break", Ui.Regular)),
                    Ui.Box("row pr-hist-pace", Ui.Text("Pace", "pr-tiny grow", Ui.Medium), Coloured(p.Pace ?? "no read", "pr-tiny-strong", Ui.Bold, paceColour)),
                    Thin(PaceShare(p.Rollout), paceColour),
                    result));
        }

        // ── The result card (Open.js renderReplayModalContent) ───────────────

        VisualElement _result;
        IVisualElementScheduledItem _closing;

        // Over everything: the path, the entry or the miss, the grade, the speed, Dismiss. After a putt it goes by itself
        // after three seconds, as the app's; opened from the history it stays until dismissed.
        void ShowResult(PracticePutt p, int index, bool auto)
        {
            CloseResult();
            Perf.Wake(4f);
            int score = (int)AppPutt.JsRound(p.Score);
            Color grade = score >= 70 ? Win : score >= 40 ? Color.white : Red;
            Color paceColour = PaceColour(p.Rollout);
            VisualElement card = Ui.Box("modal pr-result",
                new PuttPath(p),
                Ui.Box("row pr-result-top",
                    Ui.Box("grow", Coloured(p.ResultText, "pr-result-text", Ui.Bold, p.Made ? Win : Red), Ui.Text(p.Feet + "ft · " + PracticePutt.BreakLabel(p.Tee), "pr-result-sub", Ui.Regular)),
                    Coloured(p.Grade ?? AppPutt.Grade(p.Score), "pr-result-grade", Ui.Bold, grade)),
                Ui.Box("pr-result-speed",
                    Ui.Box("row", Ui.Text("Speed", "pr-tiny grow", Ui.Medium), Coloured(p.Pace ?? "no read", "pr-tiny-strong", Ui.Bold, paceColour)),
                    Thin(PaceShare(p.Rollout), paceColour)),
                Ui.Button("Dismiss", CloseResult, "btn btn-ghost pr-dismiss"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation()); // a tap on the card is not a tap outside it
            _result = Ui.Box("modal-dim", card).OnBackdropTap(CloseResult);
            _result.Face(Ui.Medium);
            if (Sheet != null) _result.styleSheets.Add(Sheet);
            (Root.parent ?? Root).Add(_result);
            if (auto) _closing = _result.schedule.Execute(CloseResult).StartingIn(3000);
        }

        void CloseResult()
        {
            _closing?.Pause();
            _closing = null;
            if (_result == null) return;
            _result.RemoveFromHierarchy();
            _result = null;
        }

        // ── Changing the tee ─────────────────────────────────────────────────

        // The mat's 27 spots (Frame.TeeSheet, as Open.js's playing screen opens Green.js): pick where the next putt is from.
        void OpenTees()
        {
            Perf.Wake(3f);
            Frame.TeeSheet(_session.Feet, _session.Tee, (feet, tee) =>
            {
                _session.Feet = feet;
                _session.Tee = tee;
                // The game and the SDK are told where the next putt comes from.
                FreePuttMode.Feet = _session.Feet;
                FreePuttMode.TeeNow = _session.Tee;
                Green.Console.distanceFt = _session.Feet;
                Green.Console.tee = _session.Tee;
                Show();
            }, Root.parent ?? Root);   // over everything, the picture included
        }
    }

    /// <summary>
    /// A putt's path into the hole as the camera saw it (PuttCard.js EnhancedPath, computePathGeometry): the camera's
    /// 475 x 350 picture, the hole moved to its middle, a faint grid, the path brightening toward the hole, a green
    /// dot where a make dropped in or a red cross where a miss stopped. Solid colours only.
    /// </summary>
    public sealed class PuttPath : VisualElement
    {
        static readonly Color Ground = new Color32(10, 30, 15, 255);
        static readonly Color Grid = new Color32(20, 39, 25, 255);
        static readonly Color Rim = new Color32(35, 52, 40, 255);
        static readonly Color Cup = new Color32(4, 12, 6, 255);
        static readonly Color CupEdge = new Color32(72, 86, 76, 255);
        readonly PracticePutt _putt;

        public PuttPath(PracticePutt putt)
        {
            _putt = putt;
            AddToClassList("pr-path");
            style.backgroundColor = Ground;
            generateVisualContent += Draw;
            // The camera's picture is 475 by 350: the height follows the width.
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float want = resolvedStyle.width * 350f / 475f;
                if (want > 0f && Mathf.Abs(resolvedStyle.height - want) > 0.5f) style.height = want;
            });
        }

        void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0f || h <= 0f) return;
            Painter2D p = context.painter2D;
            float mult = w / 475f, shift = 82.5f * mult;

            p.strokeColor = Grid;
            p.lineWidth = 1f;
            foreach (float at in new[] { 0.25f, 0.5f, 0.75f })
            {
                p.BeginPath(); p.MoveTo(new Vector2(0, h * at)); p.LineTo(new Vector2(w, h * at)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(w * at, 0)); p.LineTo(new Vector2(w * at, h)); p.Stroke();
            }

            double[] hb = _putt.Hole;
            float holeXRaw = 320f, holeY = 100f * mult, holeRad = 60f * mult, xDiff = 0f;
            if (hb != null && hb.Length >= 3) { xDiff = 320f - (float)hb[0]; holeY = (float)hb[1] * mult; holeRad = (float)hb[2] / 2f * mult; }
            float holeX = holeXRaw * mult - shift;
            var hole = new Vector2(holeX, holeY);

            // The cup: its rim, then the hole.
            p.strokeColor = Rim; p.lineWidth = 1f;
            p.BeginPath(); p.Arc(hole, holeRad + 2f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.fillColor = Cup;
            p.BeginPath(); p.Arc(hole, holeRad, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.strokeColor = CupEdge; p.lineWidth = 1.5f;
            p.BeginPath(); p.Arc(hole, holeRad, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();

            int[][] bp = _putt.Path;
            if (bp == null || bp.Length < 2) return;
            var pts = new Vector2[bp.Length];
            for (int i = 0; i < bp.Length; i++) pts[i] = new Vector2((bp[i][0] + xDiff) * mult - shift, bp[i][1] * mult);

            // The path, one solid colour (no gradient: docs/design-guidelines.md).
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            for (int i = 1; i < pts.Length; i++)
            {
                p.strokeColor = Ui.Soft;
                p.BeginPath(); p.MoveTo(pts[i - 1]); p.LineTo(pts[i]); p.Stroke();
            }

            // Where it set off.
            p.fillColor = new Color32(48, 64, 52, 255);
            p.BeginPath(); p.Arc(pts[0], 6f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            p.fillColor = new Color32(132, 142, 135, 255);
            p.BeginPath(); p.Arc(pts[0], 3f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();

            if (_putt.Made)
            {
                Vector2? entry = Entry(pts, hole, holeRad);
                if (entry != null)
                {
                    p.fillColor = Ui.Green;
                    p.BeginPath(); p.Arc(entry.Value, 5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                }
            }
            else
            {
                Vector2 end = pts[pts.Length - 1];
                p.fillColor = Ui.Red;
                p.BeginPath(); p.Arc(end, 5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                p.strokeColor = Ui.Red; p.lineWidth = 2f;
                p.BeginPath(); p.MoveTo(end + new Vector2(-3, -3)); p.LineTo(end + new Vector2(3, 3)); p.Stroke();
                p.BeginPath(); p.MoveTo(end + new Vector2(3, -3)); p.LineTo(end + new Vector2(-3, 3)); p.Stroke();
            }
        }

        /// <summary>Where the path first crosses the hole's edge (the app's entryEdgePoint), or null if it never does.</summary>
        public static Vector2? Entry(Vector2[] pts, Vector2 hole, float rad)
        {
            for (int i = 1; i < pts.Length; i++)
            {
                if ((pts[i] - hole).magnitude > rad) continue;
                Vector2 prev = pts[i - 1], v = pts[i] - prev, o = prev - hole;
                float a = Vector2.Dot(v, v), b = 2f * Vector2.Dot(o, v), c = Vector2.Dot(o, o) - rad * rad;
                float disc = b * b - 4f * a * c;
                if (disc >= 0f && a > 0f)
                {
                    float t = (-b - Mathf.Sqrt(disc)) / (2f * a);
                    return t >= 0f && t <= 1f ? prev + t * v : pts[i];
                }
                return pts[i];
            }
            return null;
        }
    }
}
