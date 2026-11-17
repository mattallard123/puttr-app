// A round in play, on the shared game frame (GameFrame.cs): the score bug and the tee sign over
// the scene, and beside it the putt read for the last putt and the putts so far. Under the
// scene on an upright phone, to its right on one held sideways.
using System;
using PuttrArcade;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class RoundPanel
    {
        /// <summary>Covers the whole screen; only its pieces take touches.</summary>
        public readonly VisualElement Root;
        readonly App _app;
        readonly CompetitionRound _round;
        readonly Action _leave;
        readonly VisualElement _over, _sheet;
        readonly ScrollView _scroll;
        bool _wide, _laidOut;
        string _waiting;

        public RoundPanel(App app, CompetitionRound round, Action leave)
        {
            _app = app;
            _round = round;
            _leave = leave;
            Root = Ui.Box("frame");
            Root.pickingMode = PickingMode.Ignore;
            Root.Face(Ui.Medium);
            _over = Ui.Box("frame-over");
            _over.pickingMode = PickingMode.Ignore;
            _sheet = Ui.Box("sheet");
            _scroll = Ui.Scroll();
            _sheet.Add(_scroll);
            Root.Add(_over);
            Root.Add(_sheet);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Root.schedule.Execute(Tick).Every(300);
            Show();
        }

        void Tick()
        {
            Place();
            // The camera's state changes on its own: redraw only when what is said about it does.
            string waiting = Capture.One.Waiting();
            if (waiting != _waiting) Show();
        }

        void Place()
        {
            bool wide = Screen.width > Screen.height;
            if (_laidOut && wide == _wide) return;
            _laidOut = true;
            _wide = wide;
            Vector4 safe = _app.SafeInsets(); // left, bottom, right, top
            float picture = wide ? FreePuttMode.WideShare : FreePuttMode.UprightShare;

            _sheet.EnableInClassList("sheet-wide", wide);
            _sheet.style.left = wide ? Length.Percent(picture * 100f) : 0;
            _sheet.style.top = wide ? 0 : Length.Percent(picture * 100f);
            _sheet.style.right = 0;
            _sheet.style.bottom = 0;
            _sheet.style.paddingBottom = safe.y;
            _sheet.style.paddingRight = wide ? safe.z : 0f;
            _sheet.style.paddingTop = wide ? Mathf.Max(safe.w, 64f) : 0f;

            // Over the scene, below the arcade's own top bar (logo and pause).
            _over.style.left = wide ? safe.x : 0;
            _over.style.top = Mathf.Max(safe.w, 0f) + (wide ? 44f : 52f);
            _over.style.width = wide ? Length.Percent(picture * 100f) : Length.Percent(100f);
            _over.style.height = wide ? Length.Percent(60f) : Length.Percent(picture * 100f - 8f);

            // The Player's own picture, small, in the bottom corner of the scene: 9 by 16, as the camera is.
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 0.46f;
            Capture.One.Place(wide ? new Rect(picture - 0.11f, 0.62f, 0.10f, 0.10f * aspect * 16f / 9f) : new Rect(0.74f, picture - 0.23f * aspect * 16f / 9f - 0.01f, 0.23f, 0.23f * aspect * 16f / 9f));
            Show();
        }

        public void Show()
        {
            Perf.Wake(3f);
            RoundSession session = _round.Session;
            _waiting = Capture.One.Waiting();
            string name = J.Str(_app.Profile, "displayName", "You");
            int first = name.IndexOf(' ');
            if (first > 0) name = name.Substring(0, first);

            _over.Clear();
            _over.Add(Ui.Box("frame-bug", Frame.ScoreBug(name, J.Str(_app.Profile, "photoURL"), RoundSession.Points(session.Total),
                session.Done ? "FINAL" : (session.Index + 1) + " OF " + session.Putts.Count)));
            if (!session.Done) _over.Add(Frame.TeeSign(session.Now.Feet, session.Now.Tee));

            _scroll.Clear();
            // The top bar names the game ("Round 1", "League Night"); this says what it is part of.
            _scroll.Add(Ui.Box("sheet-card row",
                Ui.Box("grow", Ui.Text((session.Detail.Length > 0 ? session.Detail : session.Title).ToUpperInvariant(), "kick", Ui.Bold),
                    Ui.Text(session.Done ? (_round.Finished ? "Round complete" : "Entering your score...") : session.Made + " of " + session.Index + " made", "feed-name", Ui.Bold).Margin(2)),
                Ui.Box("btn-small", Ui.Text(session.Done ? "Results" : "Leave", "btn-text", Ui.Bold)).OnTap(_leave)));

            // A ghost opponent: their total through the same putts.
            if (_round.Ghost != null)
            {
                double mine = session.Total, theirs = _round.GhostTotal(session.Index);
                string lead = session.Index == 0 ? "Your putt first" : mine > theirs ? "You lead" : mine < theirs ? _round.GhostName + " leads" : "All square";
                if (session.Done) lead = mine > theirs ? "You won!" : mine < theirs ? _round.GhostName + " won" : "A tie";
                _scroll.Add(Ui.Box("sheet-card row", Ui.Avatar(_round.GhostName, _round.GhostPhoto), Ui.Box("grow", Ui.Text("vs " + _round.GhostName, "feed-name", Ui.Bold), Ui.Text(lead, "muted", Ui.Regular)),
                    Ui.Text(RoundSession.Points(theirs), "score-mid", Ui.Bold)));
            }

            // Anything that stops a putt counting comes first.
            if (!session.Done)
            {
                if (_waiting != null) _scroll.Add(Note(_waiting, true));
                else if (_round.How == CompetitionRound.Mode.Rehearsal) _scroll.Add(Note("Rehearsal: this round is not entered.", false));
                else if (_round.How == CompetitionRound.Mode.Local) _scroll.Add(Note("Test putts: nothing is saved or entered.", false));
            }

            if (session.Seen.Count > 0)
                _scroll.Add(Frame.PuttRead(session.Seen[session.Seen.Count - 1], session.LastScore, true, session.Rule == RoundRule.Feet ? "FEET" : "POINTS"));
            else
                _scroll.Add(Ui.Box("sheet-card", Ui.Text("Putt when you are ready. After each putt its read appears here: line, pace and entry.", "sub", Ui.Regular)));

            if (session.Index > 0) _scroll.Add(List(session));
            _scroll.Add(FreePuttScreen.Space(16));
            _scroll.scrollOffset = Vector2.zero;
        }

        static VisualElement Note(string text, bool blocking)
        {
            Label note = Ui.Text(text, "sheet-note", Ui.Bold);
            if (blocking) note.AddToClassList("sheet-warn");
            return note;
        }

        // Every putt played, first to last, with what it scored.
        static VisualElement List(RoundSession session)
        {
            VisualElement card = Ui.Box("sheet-card");
            card.Add(Ui.Text("PUTTS", "kick", Ui.Bold));
            for (int i = 0; i < session.Index; i++)
            {
                double points = session.Scores[i];
                Label value = Ui.Text(RoundSession.Points(points), "feed-score", Ui.Bold);
                value.style.color = points > 0 ? Ui.Bright : Ui.Faint;
                card.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold),
                    Ui.Box("grow", Ui.Text(session.Putts[i].Feet + " ft   ·   " + PracticeSession.TeeName(session.Putts[i].Tee) + " tee", "feed-name", Ui.Medium)), value));
            }
            return card;
        }
    }
}
