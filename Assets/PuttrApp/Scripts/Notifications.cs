// The notifications inbox (puttr-app NotificationsScreen.js): GET /profile/messages/all, every one
// marked read on opening (the badge clears) while this visit still shows which were new, and a tap
// goes where the message is about: the inbox's own rows first, the rest wherever a tapped push of that
// type goes (Push.Route), so the two never disagree. The app icon's badge follows the unread count.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Inbox
    {
        public static List<object> Messages = new List<object>();
        public static event Action Changed;

        public static int Unread
        {
            get { int n = 0; foreach (object m in Messages) if (J.Int(m, "status") == 0) n++; return n; }
        }

        public static void Load(Action done = null)
        {
            App.Run(Api.Get("/profile/messages/all", data => { Messages = J.List(data); Push.Badge(Unread); Changed?.Invoke(); done?.Invoke(); }, _ => done?.Invoke()));
        }

        /// <summary>
        /// Opening the inbox reads everything (GET /profile/message/readAll/, as the app's readAllMessages).
        /// The messages held here are marked read as well, not fetched again: the list comes from the read
        /// replica, which can still say "unread" for a moment after the write and would bring the count back.
        /// </summary>
        public static void ReadAll()
        {
            App.Run(Api.Get("/profile/message/readAll/", _ =>
            {
                foreach (object m in Messages) if (J.Obj(m) is Dictionary<string, object> row) row["status"] = 1.0;
                Push.Badge(0);
                Changed?.Invoke();
            }));
        }

        /// <summary>The bell: filled with a count when there is something new.</summary>
        public static VisualElement Bell(App app)
        {
            Label count = Ui.Text("", "bell-count", Ui.Bold);
            VisualElement bell = Ui.Box("head-icon", Ui.Icon("BellSimple", 24, Color.white), count).OnTap(() => app.Push(new NotificationsScreen(app)));
            void Show()
            {
                int n = Unread;
                count.text = n > 99 ? "99+" : n.ToString();
                count.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // Listening while it is on screen: Home is taken off and put back on every tab switch, so the bell
            // listens again each time it returns (it used to stop for good after the first switch).
            bell.RegisterCallback<AttachToPanelEvent>(_ => { Changed -= Show; Changed += Show; Show(); });
            bell.RegisterCallback<DetachFromPanelEvent>(_ => Changed -= Show);
            Show();
            return bell;
        }

        public static string Type(object message) => J.Str(J.Parse(J.Str(message, "messageData", "{}")) ?? new Dictionary<string, object>(), "type");

        public static string Icon(string type)
        {
            switch (type)
            {
                case "GAME": return "FillJoystick";
                case "FOLLOW": case "FISTBUMP": return "UserPlus";
                case "TOURNAMENT": return "FillTrophy";
                case "LEAGUE": return "FillUsersThree";
                case "SESSION": return "Flask";
                case "LESSON_DUE_SOON": case "LESSON_OVERDUE": case "LESSON_EXPIRED": return "CalendarBlank";
            }
            if (type.StartsWith("LESSON") || type.StartsWith("PLAN") || type.StartsWith("COACH") || type == "SESSION_SUBMITTED" || type == "REBOOK_NUDGE" || type == "CERT_RESULT") return "GraduationCap";
            return "BellSimple";
        }

        /// <summary>Where a tap goes: the app's routing, for the screens this build has.</summary>
        public static void Open(App app, object message)
        {
            object data = J.Parse(J.Str(message, "messageData", "{}"));
            string type = J.Str(data, "type");
            switch (type)
            {
                case "GAME": if (J.Str(data, "gameId").Length > 0) app.Push(new GameViewScreen(app, J.Str(data, "gameId"))); else app.Push(new GamesScreen(app)); return;
                case "FOLLOW": case "FISTBUMP": ProfileScreen.Open(app, J.Str(data, "userId"), message); return;
                case "TOURNAMENT": app.Push(new TournamentsScreen(app)); return;
                case "LEAGUE":
                    if (J.Str(data, "route") == "weekRecap" && J.Str(data, "seasonId").Length > 0 && J.Int(data, "weekNumber") > 0) app.Push(new LeagueRecapScreen(app, J.Str(data, "seasonId"), J.Int(data, "weekNumber")));
                    else app.Push(new LeagueScreen(app));
                    return;
                // A household invite or its answer: the people on this account are on Profiles (the owner's page).
                case "HOUSEHOLD": case "HOUSEHOLD_RESPONSE": if (!Kid.Managed(app.Profile)) app.Push(new ProfilesScreen(app)); return;
                case "": return;
            }
            if (type == "VERSUS")
            {
                string id = J.Str(data, "versusId");
                if (id.Length == 0 || J.Str(data, "subtype") == "EXPIRING") { app.Push(new VersusInboxScreen(app)); return; }
                App.Run(Api.Get("/versus/" + id, v => VersusMath.Open(app, J.Get(v, "versus") ?? v), _ => app.Push(new VersusInboxScreen(app))));
            }
            // Lessons, plans, coach videos, the coach application and Junior: where a tapped push goes. A row's
            // data carries "type" where a push carries "messageType"; Route reads either.
            else Push.Route(app, data);
        }

        /// <summary>The app's body clean-up: long decimals to two places, the sender's name not said twice.</summary>
        public static string Body(object message)
        {
            string body = J.Str(message, "body");
            body = Regex.Replace(body, @"(\d+\.\d{2})\d+", "$1");
            string name = J.Str(message, "displayName");
            if (name.Length > 0 && body.StartsWith(name)) body = body.Substring(name.Length).TrimStart(' ', ':', ',');
            return body;
        }
    }

    public sealed class NotificationsScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _list;
        readonly HashSet<string> _new = new HashSet<string>();
        bool _loaded;

        public NotificationsScreen(App app)
        {
            _app = app;
            _list = new VisualElement();
            VisualElement header = Ui.Header("Notifications", app.Pop);
            header.RemoveAt(header.childCount - 1);
            header.Add(Ui.Box("back-balance social-find", Ui.IconSquare("GearSix", () => app.Push(new NotificationPrefsScreen(app)))));
            ScrollView scroll = Ui.Scroll();
            scroll.Add(header);
            scroll.Add(_list);
            scroll.Add(FreePuttScreen.Space(28));
            Root = scroll;
            Draw();
        }

        public override void Shown()
        {
            Inbox.Load(() =>
            {
                if (!_loaded)
                {
                    _loaded = true;
                    foreach (object m in Inbox.Messages) if (J.Int(m, "status") == 0) _new.Add(J.Str(m, "id"));
                    // Opening the inbox reads everything; this visit still shows what was new.
                    if (_new.Count > 0) Inbox.ReadAll();
                }
                Draw();
            });
        }

        void Draw()
        {
            _list.Clear();
            if (!_loaded) { _list.Add(Ui.Box("pad", Spinner.Small())); return; }
            if (Inbox.Messages.Count == 0)
            {
                _list.Add(Ui.Box("pad center-box note-empty", Ui.Icon("BellSimple", 40, Ui.Faint), Ui.Title("No notifications", "title-20 note-empty-title"), Ui.Text("You're all caught up! Game results, follows and competition news show up here.", "body-2 note-empty-text", Ui.Regular)));
                return;
            }
            VisualElement card = Ui.Box("glass set-card");
            foreach (object m in Inbox.Messages)
            {
                object message = m;
                string id = J.Str(m, "id");
                string type = Inbox.Type(m);
                string name = J.Str(m, "displayName");
                VisualElement face;
                if (name.Length > 0)
                {
                    face = Ui.Box("note-face");
                    string photo = J.Str(m, "photoURL");
                    if (photo.StartsWith("http")) Images.Into(face, photo);
                    else face.Add(Ui.Text(name.Substring(0, 1).ToUpperInvariant(), "note-initial", Ui.Bold));
                    face.Add(Ui.Box("note-badge", Ui.Icon(Cards.Stroke(Inbox.Icon(type)), 11, Color.white)));
                }
                else face = Ui.Box("note-face note-icon", Ui.Icon(Cards.Stroke(Inbox.Icon(type)), 24, Ui.Faint));
                VisualElement words = Ui.Box("grow set-words");
                if (name.Length > 0) words.Add(Ui.Text(name, "set-title", Ui.Bold));
                Label body = Ui.Text(Inbox.Body(m), name.Length > 0 ? "set-sub note-body" : "body note-body", Ui.Regular);
                words.Add(body);
                words.Add(Ui.Caps(Ago.Words(ProfileMath.Date(m, "created_at"), DateTime.UtcNow), "caps note-when"));
                VisualElement dot = new VisualElement();
                dot.AddToClassList("note-dot");
                dot.style.visibility = _new.Contains(id) ? Visibility.Visible : Visibility.Hidden;
                card.Add(Ui.Box("note-row", face, words, dot).OnTap(() =>
                {
                    if (_new.Remove(id)) { dot.style.visibility = Visibility.Hidden; App.Run(Api.Get("/profile/message/read/" + id, _ => { })); }
                    Inbox.Open(_app, message);
                }));
            }
            if (card.childCount > 0) card[card.childCount - 1].AddToClassList("set-row-last");
            _list.Add(Ui.Box("pad", card));
        }
    }
}
