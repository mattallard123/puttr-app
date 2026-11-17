// The session beside the picture: where to putt from, the last putt, and how the session is
// going. It is one of the app's own panels, drawn over the part of the screen the 3D game leaves
// free: the bottom of an upright phone, the right of one on its side.
using System;
using System.Collections.Generic;
using PuttrArcade;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class PracticePanel
    {
        public readonly VisualElement Root;
        readonly App _app;
        readonly PracticeSession _session;
        readonly Action _end;
        readonly ScrollView _scroll;
        bool _wide, _laidOut;

        public PracticePanel(App app, PracticeSession session, Action end)
        {
            _app = app;
            _session = session;
            _end = end;
            Root = Ui.Box("sheet");
            Root.Face(Ui.Medium);
            _scroll = Ui.Scroll();
            Root.Add(_scroll);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Root.schedule.Execute(Place).Every(300); // the phone may be turned at any time
            Show(false);
        }

        // Under the picture when upright, beside it when wide: the shares are the game's own.
        void Place()
        {
            bool wide = Screen.width > Screen.height;
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
            Show(false);
        }

        /// <summary>Draws the session as it stands.</summary>
        public void Show(bool goalJustMet)
        {
            Perf.Wake(3f);
            _scroll.Clear();
            PracticePutt last = _session.Total > 0 ? _session.Putts[_session.Total - 1] : null;

            // Where to putt from: the tee as it stands, and one button to change it.
            VisualElement from = Ui.Box("sheet-card row");
            from.Add(Ui.Box("grow", Ui.Text(_session.RandomTees ? "PUTT FROM" : "PUTTING FROM", "kick", Ui.Bold), Ui.Text(_session.Feet + " ft   ·   " + PracticeSession.TeeName(_session.Tee) + " tee", "h2", Ui.Bold).Margin(2)));
            if (!_session.RandomTees) from.Add(Ui.Box("icon-btn", Ui.Icon("Target", 22, Ui.Bright)).OnTap(OpenTees));
            from.Add(Ui.Box("btn-small", Ui.Text("End", "btn-text", Ui.Bold)).OnTap(_end));
            _scroll.Add(from);

            // The last putt, as the frame's putt read (no points: Free putt keeps no score).
            VisualElement lastCard = _session.LastVerdict != null ? Frame.PuttRead(_session.LastVerdict, 0, false)
                : Ui.Box("sheet-card", Ui.Text("Putt when you are ready. Every putt is replayed above and read here: line, pace and entry.", "sub", Ui.Regular));
            _scroll.Add(lastCard);

            if (_session.Goal != PracticeGoal.Free)
            {
                string what = _session.Goal == PracticeGoal.Streak ? _session.Streak + " of " + _session.Target + " in a row" : Mathf.Min(_session.Total, _session.Target) + " of " + _session.Target + " putts";
                _scroll.Add(Ui.Box("sheet-card", Ui.Text(_session.GoalMet ? "GOAL MET" : "GOAL", "kick", Ui.Bold), Ui.Text(what, "feed-name", Ui.Bold).Margin(2), Ui.Progress(_session.Progress)));
            }

            if (_session.Total > 0)
            {
                _scroll.Add(Ui.Box("sheet-card row",
                    FreePuttScreen.Stat(_session.Total.ToString(), "Putts"), FreePuttScreen.Stat(_session.MakePercent + "%", "Made"),
                    FreePuttScreen.Stat(_session.Streak.ToString(), "In a row"), FreePuttScreen.Stat(_session.BestStreak.ToString(), "Best run")));

                _session.Misses(out int left, out int shortOnes, out int right);
                if (left + shortOnes + right > 0)
                {
                    VisualElement misses = Ui.Box("sheet-card");
                    misses.Add(Ui.Text("WHERE THE MISSES WENT", "kick", Ui.Bold));
                    misses.Add(Ui.Box("row", FreePuttScreen.Stat(left.ToString(), "Left"), FreePuttScreen.Stat(shortOnes.ToString(), "Short"), FreePuttScreen.Stat(right.ToString(), "Right")).Margin(6));
                    _scroll.Add(misses);
                }

                List<(int feet, int makes, int total)> distances = _session.ByDistance();
                if (distances.Count > 1)
                {
                    VisualElement card = Ui.Box("sheet-card");
                    card.Add(Ui.Text("BY DISTANCE", "kick", Ui.Bold));
                    foreach ((int feet, int makes, int total) row in distances)
                        card.Add(Ui.Box("board-row",
                            Ui.Text(row.feet + " ft", "feed-name", Ui.Bold).Width(56),
                            Ui.Box("grow", Ui.Progress(row.makes / (float)row.total)),
                            Ui.Text(row.makes + " of " + row.total, "sub", Ui.Regular).Width(72).Right()));
                    _scroll.Add(card);
                }

                VisualElement history = Ui.Box("sheet-card");
                history.Add(Ui.Text("PUTTS", "kick", Ui.Bold));
                for (int i = _session.Total - 1; i >= Mathf.Max(0, _session.Total - 12); i--)
                {
                    PracticePutt p = _session.Putts[i];
                    Label result = Ui.Text(p.Headline, "sub", Ui.Medium);
                    result.style.color = p.Made ? Ui.Bright : Ui.Red;
                    history.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Ui.Text(p.Feet + " ft " + PracticeSession.TeeName(p.Tee), "feed-name", Ui.Medium).Width(112), Ui.Box("grow", result)));
                }
                _scroll.Add(history);
            }
            _scroll.Add(FreePuttScreen.Space(16));
            _scroll.scrollOffset = Vector2.zero;
            if (goalJustMet) Banner(_session.Goal == PracticeGoal.Streak ? _session.Target + " in a row. Goal met." : _session.Target + " putts. Goal met.");
        }

        // ── Changing the tee ─────────────────────────────────────────────────

        VisualElement _modal;

        // Over everything, the picture included: the distance, 3 to 11 feet, and which of the three tees.
        void OpenTees()
        {
            CloseTees();
            Perf.Wake(3f);
            VisualElement distances = Ui.Box("tee-grid");
            for (int feet = PracticeSession.MinFeet; feet <= PracticeSession.MaxFeet; feet++)
            {
                int pick = feet;
                distances.Add(Ui.Box(feet == _session.Feet ? "tee-cell tee-on" : "tee-cell", Ui.Text(feet + " ft", "tee-number", Ui.Bold))
                    .OnTap(() => { _session.Feet = pick; OpenTees(); }));
            }
            VisualElement tees = FreePuttScreen.Choice(new[] { "Left", "Center", "Right" }, _session.Tee - 1, i => { _session.Tee = i + 1; OpenTees(); });
            VisualElement card = Ui.Box("modal",
                Ui.Text("Where are you putting from?", "h2", Ui.Bold),
                Ui.Text("Pick the distance and the tee your ball is on, so your stats are right.", "sub", Ui.Regular).Margin(4),
                Ui.Text("DISTANCE", "kick", Ui.Bold).Margin(14), distances,
                Ui.Text("TEE", "kick", Ui.Bold).Margin(12), tees,
                Ui.Button("Done", CloseTees));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation()); // a tap on the card is not a tap outside it
            _modal = Ui.Box("modal-dim", card).OnTap(CloseTees);
            _modal.Face(Ui.Medium);
            (Root.parent ?? Root).Add(_modal);
        }

        void CloseTees()
        {
            if (_modal == null) return;
            _modal.RemoveFromHierarchy();
            _modal = null;
            // The game and the SDK are told where the next putt comes from.
            FreePuttMode.Feet = _session.Feet;
            FreePuttMode.TeeNow = _session.Tee;
            Green.Console.distanceFt = _session.Feet;
            Green.Console.tee = _session.Tee;
            Show(false);
        }

        // The app's toast is hidden while a game has the screen: say it in the panel.
        void Banner(string text)
        {
            Label note = Ui.Text(text, "sheet-note", Ui.Bold);
            _scroll.Insert(0, note);
            note.schedule.Execute(note.RemoveFromHierarchy).StartingIn(4000);
        }
    }
}
