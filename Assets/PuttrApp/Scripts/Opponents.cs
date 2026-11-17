// Playing someone who is not here (puttr-app games/common/OpponentSelect.js, multiplayer/GameLobby.js):
//
//   GhostOpponentScreen  GET /games/challenge/players/:gameId, a slot-machine shuffle (30 ms, x1.2 a tick, stops at
//                        400 ms), "New opponent" or "Accept" -> GET /games/challenge/getGame/:opp/:gameId -> PlayGhost
//   LiveVideo            a live 1v1's video call: POST /video/agora/token {channelName: "MP_<gameId>", uid: profile.id},
//                        the opponent large, yourself small at the top right with a mute button
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class GhostOpponentScreen : AppScreen
    {
        public GhostOpponentScreen(App app, object game)
        {
            Root = Page.Make(app, "Random opponent", out VisualElement body);
            if (Kid.Blocked(app, Kid.Online, body)) return;
            body.Add(new GhostPicker(app, game, () => app.Pop(), true).Root);
        }
    }

    /// <summary>
    /// Pick a random opponent where you are (the game page shows it inline under its buttons): a slot-machine
    /// shuffle of the Players who have played this game, then Accept or New opponent.
    /// </summary>
    public sealed class GhostPicker
    {
        public readonly VisualElement Root;
        readonly App _app;
        readonly object _game;
        readonly System.Action _close;
        readonly bool _page;
        List<object> _pool;
        object _pick;
        bool _spinning, _starting;

        /// <param name="close">Cancel (inline) or leave (page) before a match starts.</param>
        /// <param name="page">On its own page: Accept pops it before the match starts.</param>
        public GhostPicker(App app, object game, System.Action close, bool page = false)
        {
            _app = app; _game = game; _close = close; _page = page;
            Root = Ui.Box("ghost-inline");
            Root.Add(Ui.Box("pad", Spinner.Small()));
            App.Run(Api.Get("/games/challenge/players/" + J.Str(game, "id"), d => { _pool = J.List(d).FindAll(p => J.Str(p, "userId") != Auth.Uid); Draw(); if (_pool.Count > 0) App.Run(Shuffle()); },
                _ => { _pool = new List<object>(); Draw(); }));
        }

        IEnumerator Shuffle()
        {
            _spinning = true;
            float wait = 0.03f;
            while (wait < 0.4f)
            {
                _pick = _pool[Random.Range(0, _pool.Count)];
                Draw();
                yield return new WaitForSecondsRealtime(wait);
                wait *= 1.2f;
            }
            _spinning = false;
            Draw();
        }

        void Draw()
        {
            Root.Clear();
            if (_pool.Count == 0)
            {
                Root.Add(Ui.Box("glass center-box", Ui.Text("No opponents yet", "h2 center wrap", Ui.Bold), Ui.Text("Be the first to play this game online!", "sub center", Ui.Regular).Margin(6)));
                if (!_page) Root.Add(Ui.Button("Close", _close, "btn btn-ghost").Margin(8));
                return;
            }
            object p = _pick;
            string hcp = J.Str(p, "handicap");
            if (hcp.StartsWith("-")) hcp = "+" + hcp.Substring(1);
            VisualElement card = Ui.Box("glass center-box", Ui.Avatar(J.Str(p, "displayName"), J.Str(p, "photoURL"), true), Ui.Text(J.Str(p, "displayName"), "h2 center wrap", Ui.Bold).Margin(8), Ui.Text(J.Str(p, "homeTown"), "muted center", Ui.Regular));
            if (_spinning) card.Add(Ui.Text("Searching…", "muted center", Ui.Regular).Margin(6));
            else if (hcp.Length > 0) card.Add(AdminLabels.Pill(hcp + " HCP", Ui.Bright).Margin(6));
            if (_starting) card.Add(Ui.Text("Setting up your match", "muted center", Ui.Regular).Margin(10));
            Root.Add(card);
            if (_spinning || _starting) return;
            Root.Add(Ui.Button("Accept", Accept).Margin(10));
            VisualElement more = Ui.Box("row", Ui.Button("New opponent", () => App.Run(Shuffle()), "btn btn-ghost grow"));
            if (!_page) { more.Add(FreePuttScreen.Space(8).Width(8)); more.Add(Ui.Button("Cancel", _close, "btn btn-ghost grow")); }
            Root.Add(more.Margin(8));
        }

        void Accept()
        {
            object opponent = _pick;
            _starting = true; Draw();
            App.Run(Api.Get("/games/challenge/getGame/" + J.Str(opponent, "userId") + "/" + J.Str(_game, "id"), d =>
            {
                if (_page) _app.Pop();
                else { _starting = false; _close(); }
                _app.Games.PlayGhost(_game, opponent, d);
            }, _ => { _starting = false; Draw(); _app.Toast("Couldn't load their game. Try another opponent."); }));
        }
    }

    /// <summary>The video in a live 1v1 (GameLobby): Agora on channel MP_{game id}, as the numeric profile id.</summary>
    public sealed class LiveVideo
    {
        public readonly VisualElement Root;
        readonly Label _status;
        bool _muted, _on;

        public LiveVideo(string gameId)
        {
            _status = Ui.Text("Connecting video", "muted center", Ui.Regular);
            Label mic = Ui.Text("Mute", "scope-text", Ui.Bold);
            VisualElement mute = Ui.Box("scope", mic);
            mute.style.position = Position.Absolute; mute.style.left = 8; mute.style.bottom = 8;
            mute.OnTap(() => { _muted = !_muted; Capture.One.Mute(_muted); mic.text = _muted ? "Unmute" : "Mute"; mute.style.backgroundColor = _muted ? (StyleColor)(Color)Ui.Red : new StyleColor(StyleKeyword.Null); });
            Root = Ui.Box("sheet-card", _status);
            Root.style.height = 220;
            Root.style.justifyContent = Justify.Center;
            Root.Add(mute);
            if (!Capture.Supported || Kid.IsKid(App.I.Profile)) { _status.text = Capture.Supported ? "" : "Video needs the phone app."; Root.style.display = Capture.Supported ? DisplayStyle.None : DisplayStyle.Flex; return; }
            int uid = J.Int(App.I.Profile, "id");
            if (uid <= 0) uid = Random.Range(1, 10000000);
            Capture.One.BeginCall("MP_" + gameId, uid);
            _on = true;
            Capture.One.Changed += Place;
            Root.RegisterCallback<GeometryChangedEvent>(_ => Place());
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            Root.schedule.Execute(Place).Every(1000).Until(() => !_on);
        }

        // The native views sit over the app: the opponent fills this card, you sit small in its top right.
        void Place()
        {
            if (!_on || Root.panel == null) return;
            Rect all = Root.panel.visualTree.layout, r = Root.worldBound;
            if (all.width <= 0 || all.height <= 0) return;
            Rect share = new Rect(r.x / all.width, r.y / all.height, r.width / all.width, r.height / all.height);
            bool joined = Capture.One.RemoteUid != 0;
            _status.text = Capture.One.Now == Capture.State.Failed ? Capture.One.Problem : joined ? "" : "Waiting on opponent...";
            Capture.One.PlaceRemote(joined ? share : new Rect(0, 0, 0, 0));
            float w = share.width * 0.3f, h = w * (all.width / all.height) * (177f / 123f);
            Capture.One.Place(new Rect(share.xMax - w - 0.02f, share.y + 0.01f, w, Mathf.Min(h, share.height * 0.9f)));
        }

        void Stop()
        {
            if (!_on) return;
            _on = false;
            Capture.One.Changed -= Place;
            Capture.One.End();
        }
    }
}
