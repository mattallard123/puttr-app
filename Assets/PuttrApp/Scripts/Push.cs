// Push notifications, as puttr-app (UserNav setMessagingListeners, SettingsScreen confirmSignout):
//   once signed in: ask permission, get the FCM token, POST /profile/pushToken/ {token, deviceId, saveType: 'ADD'}
//   on sign-out:    POST /profile/pushToken/ {deviceId, token, saveType: 'REMOVE'}
//   a push while open: a toast, and the bell refreshed;  a push tapped: pushPressed's routing.
// deviceId is what DeviceInfo.getUniqueIdSync gives (identifierForVendor on iOS, ANDROID_ID on Android),
// so the API's dedupe by device keeps working across the update.
// Native: Plugins/iOS/PuttrPush.m, Plugins/Android PuttrPush.java + PuttrMessagingService.java.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PuttrApp
{
    public sealed class Push : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PuttrPush_Register(string target);
        [DllImport("__Internal")] static extern void PuttrPush_Badge(int count);
        static string DeviceId() => SystemInfo.deviceUniqueIdentifier; // identifierForVendor
        static string PendingTap() => null; // iOS hands a launching tap over itself
        public static bool Supported => true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaClass _java;
        static AndroidJavaClass Java => _java ?? (_java = new AndroidJavaClass("co.puttr.app2.PuttrPush"));
        static AndroidJavaObject Activity() { using (var p = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) return p.GetStatic<AndroidJavaObject>("currentActivity"); }
        static void PuttrPush_Register(string target)
        {
            // Android 13 and later ask; the app never did (the RN app's gap), so Players there finally get pushes.
            if (Puttr.AndroidPermissions.AndroidVersion() >= 33) Puttr.AndroidPermissions.Ask(new[] { "android.permission.POST_NOTIFICATIONS" }, () => Java.CallStatic("token", target));
            else Java.CallStatic("token", target);
        }
        static void PuttrPush_Badge(int count) { }
        static string DeviceId() => Java.CallStatic<string>("deviceId", Activity());
        static string PendingTap() => Java.CallStatic<string>("pendingTap", Activity());
        public static bool Supported => true;
#else
        static void PuttrPush_Register(string target) { }
        static void PuttrPush_Badge(int count) { }
        static string DeviceId() => SystemInfo.deviceUniqueIdentifier;
        static string PendingTap() => null;
        public static bool Supported => false;
#endif
        const string ObjectName = "PuttrPush";
        static Push _one;
        static string _token;
        static object _pendingTap;
        App _app;

        static string Device() { string id = DeviceId() ?? ""; return id.Length > 64 ? id.Substring(0, 64) : id; }

        /// <summary>Once signed in (and on every launch, as the app's UserNav mount does): permission, token, register.</summary>
        public static void Register(App app)
        {
            // Sample data (screen checks) asks for nothing: no permission prompt over the screen being checked.
            if (!Supported || Fixtures.On) return;
            if (_one == null) { var go = new GameObject(ObjectName); DontDestroyOnLoad(go); _one = go.AddComponent<Push>(); }
            _one._app = app;
            PuttrPush_Register(ObjectName);
            _one.CheckTap();
        }

        /// <summary>Before signing out: this phone stops getting this Player's pushes.</summary>
        public static void Unregister()
        {
            if (!Supported || _one == null) return;
            App.Run(Api.Post("/profile/pushToken/", new Dictionary<string, object> { { "deviceId", Device() }, { "token", _token ?? "" }, { "saveType", "REMOVE" } }, null, null));
            _token = null;
        }

        void OnPushToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token == _token) return;
            _token = token;
            App.Run(Api.Post("/profile/pushToken/", new Dictionary<string, object> { { "token", token }, { "deviceId", Device() }, { "saveType", "ADD" } },
                _ => Perf.Note("push: token saved"), why => Perf.Note("push: token not saved (" + why + ")")));
        }

        // A push while the app is open: the app's own toast, and the bell brought up to date.
        void OnPushForeground(string json)
        {
            object push = J.Parse(json);
            string title = J.Str(push, "title"), body = J.Str(push, "body");
            if (_app != null && (title.Length > 0 || body.Length > 0)) _app.Toast(title.Length > 0 && body.Length > 0 ? title + ": " + body : title + body);
            Inbox.Load();
        }

        void OnPushOpened(string json)
        {
            object data = J.Parse(json);
            if (_app == null || _app.Profile == null) { _pendingTap = data; return; }
            Route(_app, data);
        }

        void OnApplicationFocus(bool focus) { if (focus) { CheckTap(); Inbox.Load(); } }

        void CheckTap()
        {
            string tap = PendingTap();
            if (!string.IsNullOrEmpty(tap)) OnPushOpened(tap);
            else if (_pendingTap != null && _app != null && _app.Profile != null) { object t = _pendingTap; _pendingTap = null; Route(_app, t); }
        }

        /// <summary>A tap that came before the app was ready: once Home is up.</summary>
        public static void Flush(App app) { if (_one != null) { _one._app = app; _one.CheckTap(); } }

        /// <summary>UserNav pushPressed, for the screens this build has.</summary>
        public static void Route(App app, object data)
        {
            string type = J.Str(data, "messageType", J.Str(data, "type"));
            switch (type)
            {
                case "TOURNEY": case "TOURNAMENT": app.Push(new TournamentsScreen(app)); return;
                case "COACH_REMINDER": app.ShowTabs("improve"); return;
                case "MP": app.Push(new LiveLobbyScreen(app)); return;
                case "LEAGUE":
                    if (J.Str(data, "route") == "weekRecap" && J.Str(data, "seasonId").Length > 0 && int.TryParse(J.Str(data, "weekNumber"), out int week)) app.Push(new LeagueRecapScreen(app, J.Str(data, "seasonId"), week));
                    else app.Push(new LeagueScreen(app));
                    return;
                case "FISTBUMP": case "FOLLOW": if (J.Str(data, "userId").Length > 0) ProfileScreen.Open(app, J.Str(data, "userId")); return;
                case "GAME": if (J.Str(data, "gameId").Length > 0) app.Push(new GameViewScreen(app, J.Str(data, "gameId"))); return;
                case "VERSUS":
                    string id = J.Str(data, "versusId");
                    if (id.Length == 0) return;
                    if (J.Str(data, "subtype") == "EXPIRING") { app.Push(new VersusInboxScreen(app)); return; }
                    App.Run(Api.Get("/versus/" + id, v =>
                    {
                        object row = J.Get(v, "versus") ?? v;
                        if (J.Str(data, "subtype") == "COMPLETE" || J.Str(row, "status") == "COMPLETE") app.Push(new VersusResultsScreen(app, id));
                        else VersusMath.Open(app, row);
                    }, _ => app.Push(new VersusInboxScreen(app))));
                    return;
            }
            string booking = J.Str(data, "bookingId"), plan = J.Str(data, "planId");
            bool coachView = J.Str(data, "coachView") == "1" || J.Flag(data, "coachView");
            string[] coachTypes = { "LESSON_BOOKED", "SESSION_SUBMITTED", "LESSON_DUE_SOON", "LESSON_OVERDUE", "LESSON_EXPIRED", "LESSON_CANCELED", "LESSON_REVIEW_RECEIVED" };
            if (Array.IndexOf(coachTypes, type) >= 0 || (type.StartsWith("LESSON") && coachView))
            {
                if (booking.Length == 0) { app.Push(new LessonInboxScreen(app)); return; }
                App.Run(Api.Get("/lesson/booking/" + booking, b => Coach.OpenBooking(app, b), _ => app.Push(new LessonInboxScreen(app))));
                return;
            }
            if (type.StartsWith("LESSON")) { if (booking.Length > 0) LessonInfo.Open(app, booking); else app.Push(new MyLessonsScreen(app)); return; }
            if (type.StartsWith("PLAN") || type == "REBOOK_NUDGE")
            {
                if (coachView) { if (plan.Length > 0) app.Push(new PlayerMonitorScreen(app, plan)); else app.Push(new PlayerRosterScreen(app)); }
                else if (plan.Length > 0) app.Push(new PlanDetailScreen(app, plan)); else app.Push(new MyLessonsScreen(app));
                return;
            }
            if (type == "SESSION" || type == "COACH_VIDEO") { app.Push(new CoachVideosScreen(app)); return; }
            if (type == "COACH_APP_APPROVED" || type == "COACH_INVITE" || type == "CERT_RESULT" || type.StartsWith("COACH_APP")) { app.Push(new CoachApplicationStatusScreen(app)); return; }
            if (type.StartsWith("JUNIOR") || type == "PARENT_APPROVED") { app.Push(new JuniorStatusScreen(app)); return; }
            // FOLLOW, LAB, HOUSEHOLD, ENGAGE, TEST: opening the app is all, as the app.
        }
    }
}
