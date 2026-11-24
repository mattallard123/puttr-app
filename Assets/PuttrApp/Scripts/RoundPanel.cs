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
        readonly VisualElement _over, _sheet, _tee = new VisualElement();
        readonly ScrollView _scroll;
        bool _wide, _laidOut, _offline, _statusUp;
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
            // Above the cards and never scrolled away: why a putt did not count, "Puttr not connected", and a
            // tester's test putt buttons (not in a tournament round or a League game that is being entered).
            _tee.pickingMode = PickingMode.Ignore;
            _sheet.Add(_tee);   // a phone: where to putt from, across the screen under the picture (Frame.TeeBand)
            _sheet.Add(new PuttStrip(app, () => TestPutts.RoundTakes(true, _round.What, _round.How)).Root);
            _sheet.Add(_scroll);
            Root.Add(_over);
            Root.Add(_sheet);
            Root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Root.schedule.Execute(Tick).Every(300);
            // The arcade's status chip keeps to the top row while this panel's score bug is up (Hud.PlaceStatus).
            Root.RegisterCallback<AttachToPanelEvent>(_ => PuttrArcade.Hud.StatusBelowPx = 1f);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => PuttrArcade.Hud.StatusBelowPx = 0f);
            Show();
        }

        void Tick()
        {
            Place();
            // The camera's state changes on its own: redraw only when what is said about it does.
            string waiting = Capture.One.Waiting();
            if (waiting != _waiting || PuttStrip.SaysOffline(_app.Profile) != _offline) Show();
        }

        void Place()
        {
            bool wide = Screen.width > Screen.height;
            // The arcade's "Looking for your Puttr" chip takes the row under the logo; the score bug steps down under it.
            bool statusUp = PuttrArcade.Hud.StatusUp;
            if (_laidOut && wide == _wide && statusUp == _statusUp) return;
            _statusUp = statusUp;
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
            _over.style.top = Mathf.Max(safe.w, 0f) + (wide ? 44f : 52f) + (!wide && statusUp ? PuttrArcade.Hud.StatusRowPx : 0f);
            _over.style.width = wide ? Length.Percent(picture * 100f) : Length.Percent(100f);
            _over.style.height = wide ? Length.Percent(60f) : Length.Percent(picture * 100f - 8f);

            // The Player's own picture while the round is filmed: a small rounded tile (r 12, drawn by the plugin) in
            // the bottom right corner of the scene, 9 by 16 as the camera is. Clear of the score bug (top centre), the
            // tee sign (top left) and the sheet; held sideways it sits above the home bar.
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 0.46f;
            if (wide)
            {
                float h = 0.10f * aspect * 16f / 9f;
                float bottom = Screen.height > 0 ? Screen.safeArea.y / Screen.height : 0f;
                Capture.One.Place(new Rect(picture - 0.11f, 1f - h - bottom - 0.03f, 0.10f, h));
            }
            else Capture.One.Place(new Rect(0.74f, picture - 0.23f * aspect * 16f / 9f - 0.01f, 0.23f, 0.23f * aspect * 16f / 9f));
            Show();
        }

        public void Show()
        {
            Perf.Wake(3f);
            RoundSession session = _round.Session;
            _waiting = Capture.One.Waiting();
            _offline = PuttStrip.SaysOffline(_app.Profile);
            string name = J.Str(_app.Profile, "displayName", "You");
            int first = name.IndexOf(' ');
            if (first > 0) name = name.Substring(0, first);

            _over.Clear();
            _over.Add(Ui.Box("frame-bug", Frame.ScoreBug(name, J.Str(_app.Profile, "photoURL"), RoundSession.Points(session.Total),
                session.Done ? "FINAL" : "PUTT " + (session.Index + 1) + " OF " + session.Putts.Count)));
            _tee.Clear();
            if (!session.Done && Frame.TeeInBand) _tee.Add(Frame.TeeBand(session.Now.Feet, session.Now.Tee));
            else if (!session.Done) _over.Add(Frame.TeeSign(session.Now.Feet, session.Now.Tee));

            _scroll.Clear();
            // The top bar names the game ("Round 1", "League Night"); this says what it is part of.
            // Before the first putt there is nothing made or missed to count: say how long the round is instead.
            string tally = session.Index == 0 ? session.Putts.Count + (session.Putts.Count == 1 ? " putt to play" : " putts to play") : session.Made + " of " + session.Index + " made";
            VisualElement head = Ui.Box("sheet-card",
                Ui.Box("row",
                    Ui.Box("grow", Ui.Caps(session.Detail.Length > 0 ? session.Detail : session.Title, "caps caps-green"),
                        Ui.Text(session.Done ? (_round.Finished ? "Round complete" : "Entering your score...") : tally, "body", Ui.Bold).Margin(4)),
                    // Leaving a round part way is pause, then Exit, as in every game: no Leave button here.
                    session.Done ? Ui.Button("Results", _leave, "btn btn-ghost round-results") : null),
                Frame.PuttProgress(session.Scores, session.Putts.Count));
            _scroll.Add(head);

            // A ghost opponent: their total through the same putts.
            if (_round.Ghost != null)
            {
                double mine = session.Total, theirs = _round.GhostTotal(session.Index);
                string lead = session.Index == 0 ? "Your putt first" : mine > theirs ? "You lead" : mine < theirs ? _round.GhostName + " leads" : "All square";
                if (session.Done) lead = mine > theirs ? "You won!" : mine < theirs ? _round.GhostName + " won" : "A tie";
                _scroll.Add(Ui.Box("sheet-card row", Ui.Avatar(_round.GhostName, _round.GhostPhoto), Ui.Box("grow", Ui.Text("vs " + _round.GhostName, "body", Ui.Bold), Ui.Text(lead, "body-2", Ui.Medium)),
                    Ui.Num(RoundSession.Points(theirs), "num")));
            }

            // Anything that stops a putt counting comes first.
            if (!session.Done)
            {
                if (_waiting != null) _scroll.Add(Note(_waiting, true));
                else if (_round.How == CompetitionRound.Mode.Rehearsal) _scroll.Add(Note("Rehearsal: this round is not entered.", false));
                // With no Puttr and no test putts the strip says "Puttr not connected": "test putts" would promise a way to putt.
                else if (_round.How == CompetitionRound.Mode.Local && !_offline) _scroll.Add(Note("Test putts: nothing is saved or entered.", false));
            }

            if (session.Seen.Count > 0)
                _scroll.Add(Frame.PuttRead(session.Seen[session.Seen.Count - 1], session.LastScore, true, session.Rule == RoundRule.Feet ? "FEET" : "POINTS"));
            else
                _scroll.Add(Ui.Box("sheet-card", Ui.Text(_offline ? "Connect your Puttr to begin. After each putt its read appears here: line, pace and entry." : "Putt when you are ready. After each putt its read appears here: line, pace and entry.", "body-2", Ui.Medium)));

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
            card.Add(Ui.Caps("Putts", "caps"));
            for (int i = 0; i < session.Index; i++)
            {
                double points = session.Scores[i];
                Label value = Ui.Num(RoundSession.Points(points), "num-20 round-points");
                value.style.color = points > 0 ? Ui.Bright : Ui.Faint;
                card.Add(Ui.Box(i == session.Index - 1 ? "board-row round-last" : "board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold),
                    Ui.Box("grow", Ui.Text(session.Putts[i].Feet + " ft   ·   " + PracticeSession.TeeName(session.Putts[i].Tee) + " tee", "body", Ui.Medium)), value));
            }
            return card;
        }
    }
}
