// The coach's side (puttr-app components/coaching and lessons). A coach (isCoach or coachApproved) gets a
// Coach tab where Improve was:
//
//   CoachScreen               the dashboard: lessons to review, the go-live checklist, Lessons, My Players,
//                             Player Review, Video Library, Academy, Coaching Profile, games, affiliate, apply
//   LessonInboxScreen         GET /lesson/coaching/inbox: Needs review, Waiting on session, Delivered, Practice space
//   LessonWorkspaceScreen     the booking: the Player's full analysis | the goals, session and putts; REC; Start Review; Deliver Lesson
//   LessonDeliverScreen       the lesson video (the in-app recording or the camera roll, resumable upload), summary,
//                             key findings, practice plan, send
//   LessonDeliveredScreen     what was sent, watched or not, the rating, the plan's progress
//   CoachLessonSetupScreen    headline, bio, price, turnaround, specialties, accepting, Stripe payouts
//   AssignPlanScreen          games and drills, times a week, a cue; 1, 2 or 4 weeks
//   PlayerRosterScreen        active plans and the recently ended
//   PlayerMonitorScreen       a plan's progress, before and after, edit, one nudge a day
//   CoachingDashboardScreen   Player Review: followers and following, a Player's goals and notes, their sessions
//   CoachingVideosScreen      the coach's library and Player feedback videos; upload, preview, delete
//
// Recording the review (Recorder below): REC in the workspace or Player Review records the screen and the coach's
// voice while they move through the session page; Stop puts the file straight into Deliver's lesson-video slot.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Coach
    {
        public static bool Is(object profile) => J.Flag(profile, "isCoach") || J.Flag(profile, "coachApproved");

        /// <summary>The lessons waiting on the coach (the Coach tab's badge).</summary>
        public static int Pending;

        public static void RefreshPending(Action done = null) => App.Run(Api.Get("/lesson/coaching/pending", d => { Pending = J.Int(d, "needsReview"); done?.Invoke(); }, _ => done?.Invoke()));

        /// <summary>A card on the dashboard: icon, title, words, badge.</summary>
        public static VisualElement Card(string icon, string title, string words, int badge, Color badgeColour, Action tap)
        {
            VisualElement right = Ui.Box("row");
            if (badge > 0) right.Add(AdminLabels.Pill(badge > 9 ? "9+" : badge.ToString(), badgeColour));
            right.Add(Ui.Icon("CaretRight", 16, Ui.Faint));
            return Ui.Box("card coach-dash", Form.Glyph(icon), Ui.Box("grow set-words", Ui.Text(title, "body", Ui.Bold), Ui.Text(words, "body-2 wrap", Ui.Regular)), right).OnTap(tap);
        }

        /// <summary>Opens a booking where the coach works on it: delivered ones show what was sent.</summary>
        public static void OpenBooking(App app, object b)
        {
            int st = J.Int(b, "status");
            if (st == 4 || st == 5) app.Push(new LessonDeliveredScreen(app, J.Str(b, "id")));
            else app.Push(new LessonWorkspaceScreen(app, J.Str(b, "id")));
        }

        /// <summary>Grade from a session score (A 18+, B 15+, C 12+, else D).</summary>
        public static string Grade(double score) => score >= 18 ? "A" : score >= 15 ? "B" : score >= 12 ? "C" : "D";

        /// <summary>puttr-app's getPuttGrade (GameFunctions.js), the grade the lesson workspace shows on a session: F under 20,
        /// D, C, B, A by twenties from 20, each with a plus in its top few points (D+ over 35, C+ over 55, B+ over 75, A+ over 95).</summary>
        public static string PuttGrade(double score)
        {
            if (score < 20) return "F";
            if (score < 40) return score > 35 ? "D+" : "D";
            if (score < 60) return score > 55 ? "C+" : "C";
            if (score < 80) return score > 75 ? "B+" : "B";
            return score > 95 ? "A+" : "A";
        }

        /// <summary>A handicap as the workspace shows it (one decimal, "+" for a plus handicap), or "" when there is none.</summary>
        public static string Handicap(object row, string key)
        {
            string raw = J.Str(row, key);
            if (raw.Length == 0 || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double h)) return raw;
            return h < 0 ? "+" + (-h).ToString("0.0", CultureInfo.InvariantCulture) : h.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>A card of choices (puttr-app's showAlert with several buttons): the first is the green one.</summary>
        public static void Choose(string title, string text, params (string label, Action act)[] options)
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Title(title, "title-20"));
            if (!string.IsNullOrEmpty(text)) card.Add(Ui.Text(text, "sub wrap", Ui.Regular).Margin(8));
            for (int i = 0; i < options.Length; i++)
            {
                (string label, Action act) o = options[i];
                card.Add(Ui.Button(o.label, () => { dim.RemoveFromHierarchy(); o.act?.Invoke(); }, i == 0 ? "btn" : "btn btn-ghost"));
            }
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(App.I != null ? App.I.Toasts : card).Add(dim);
        }
    }

    /// <summary>
    /// Recording the lesson in the app (puttr-app LessonRecording + RecordPill): the screen and the coach's voice, from a
    /// REC button in the lesson workspace or Player Review. The take carries on while the coach moves into the session
    /// page and its video analysis; one red Stop with the running time floats over every screen until it ends. The file
    /// then goes straight to Deliver's lesson-video slot ("Use in lesson"), or for Player Review to a studio lesson or
    /// a plain video to the Player. iPhone: ReplayKit (Plugins/iOS/PuttrRecord.m), with an optional front-camera bubble.
    /// Android: MediaProjection and MediaRecorder (PuttrRecord.java) once its activity and service are in the manifest;
    /// until then the system screen recorder and a re-pick, as before.
    /// </summary>
    public sealed class Recorder : MonoBehaviour
    {
        const string ObjectName = "PuttrRecord";
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int PuttrRecord_Available();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int PuttrRecord_CameraAvailable();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrRecord_Begin(string target, string path, int camera);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrRecord_End();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrRecord_Cancel();
        public static bool Supported => PuttrRecord_Available() != 0;
        public static bool CameraSupported => PuttrRecord_CameraAvailable() != 0;
        static void NativeBegin(string path, bool camera) => PuttrRecord_Begin(ObjectName, path, camera ? 1 : 0);
        static void NativeEnd() => PuttrRecord_End();
        static void NativeCancel() => PuttrRecord_Cancel();
#elif UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaClass _java;
        static AndroidJavaClass Java => _java ?? (_java = new AndroidJavaClass("co.puttr.app2.PuttrRecord"));
        static int _available = -1;
        static void WithActivity(Action<AndroidJavaObject> act)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                act(activity);
        }
        public static bool Supported
        {
            get
            {
                if (_available < 0)
                {
                    try { bool ok = false; WithActivity(a => ok = Java.CallStatic<bool>("available", a)); _available = ok ? 1 : 0; }
                    catch (Exception) { _available = 0; }
                }
                return _available == 1;
            }
        }
        // A camera bubble on Android would need a camera preview layered over Unity's view: not built (see the report).
        public static bool CameraSupported => false;
        static void NativeBegin(string path, bool camera) => Puttr.AndroidPermissions.Ask(new[] { "android.permission.RECORD_AUDIO" }, () =>
        {
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.RECORD_AUDIO")) { Message("nomic"); return; }
            WithActivity(a => Java.CallStatic("start", a, ObjectName, path));
        });
        static void NativeEnd() => WithActivity(a => Java.CallStatic("stop", a));
        static void NativeCancel() => WithActivity(a => Java.CallStatic("cancel", a));
#else
        public static bool Supported => false;
        public static bool CameraSupported => false;
        static void NativeBegin(string path, bool camera) { }
        static void NativeEnd() { }
        static void NativeCancel() { }
#endif
        public enum Phase { Idle, Starting, Recording, Stopping }

        /// <summary>Where the take was started: the open lesson (Use in lesson), or the Player under review.</summary>
        public sealed class Context
        {
            public object Booking;            // an open booking (status 2 or 3): the take attaches to it
            public object Player;             // Player Review: who the take is about
            public Func<object> Session;      // Player Review: the session under review when the take ends
        }

        public static Phase State { get; private set; }
        static Recorder _one;
        static Context _context;
        static string _path;
        static float _since;
        static VisualElement _floating, _cover;
        static Label _floatingTime, _coverWords;

        public static float Seconds => State == Phase.Recording ? Time.realtimeSinceStartup - _since : 0f;

        /// <summary>The REC button for a header. Idle it reads REC; while a take runs the floating red Stop is the one indicator.</summary>
        public static VisualElement Pill(VisualElement over, Context context)
        {
            VisualElement dot = new VisualElement();
            dot.style.width = dot.style.height = 8;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 4;
            dot.style.backgroundColor = Ui.Red;
            dot.style.marginRight = 8;
            VisualElement pill = Ui.Box("scope row", dot, Ui.Text("REC", "scope-text", Ui.Bold));
            pill.style.flexShrink = 0;
            void Tick() => pill.style.display = State == Phase.Idle ? DisplayStyle.Flex : DisplayStyle.None;
            pill.schedule.Execute(Tick).Every(400);
            Tick();
            return pill.OnTap(() => Press(over, context));
        }

        /// <summary>REC was tapped: what will be captured, then Record (or Record with camera where the bubble is built).</summary>
        public static void Press(VisualElement over, Context context)
        {
            if (State != Phase.Idle) return;
            if (!Supported)
            {
                Dialog.Ask(over, "Record your lesson", "Use your phone's screen recorder from Quick Settings, turn on Record audio, narrate your review, then upload the recording in Deliver.", "Got it", () => { });
                return;
            }
            string what = Application.platform == RuntimePlatform.Android
                ? "Everything on screen is captured, along with your voice. Android asks you to allow the recording first."
                : "Everything on screen is captured, along with your voice. Notifications and other apps stay out of the recording." + (CameraSupported ? " The camera adds a movable bubble with you in it." : "");
            if (CameraSupported) Coach.Choose("Record your lesson", what, ("Record", () => Begin(context, false)), ("Record with camera", () => Begin(context, true)), ("Cancel", null));
            else Coach.Choose("Record your lesson", what, ("Record", () => Begin(context, false)), ("Cancel", null));
        }

        static void Begin(Context context, bool camera)
        {
            if (_one == null) { var go = new GameObject(ObjectName); DontDestroyOnLoad(go); _one = go.AddComponent<Recorder>(); }
            _context = context;
            _path = System.IO.Path.Combine(Application.temporaryCachePath, "lesson_rec_" + DateTime.UtcNow.Ticks + ".mp4");
            State = Phase.Starting;
            NativeBegin(_path, camera);
        }

        /// <summary>The red Stop: the file is written, then offered to the lesson.</summary>
        public static void Stop()
        {
            if (State != Phase.Recording) return;
            State = Phase.Stopping;
            HideFloating();
            Cover("Saving recording...");
            NativeEnd();
        }

        /// <summary>Throws the running take away.</summary>
        public static void Cancel()
        {
            if (State == Phase.Idle) return;
            NativeCancel();
            State = Phase.Idle;
            HideFloating();
            Uncover();
        }

        static void Message(string message) { if (_one != null) _one.OnRecord(message); }

        // From the plugins: started | stopped|<path> | failed|<reason> | nomic | cancelled.
        void OnRecord(string message)
        {
            string[] parts = (message ?? "").Split(new[] { '|' }, 2);
            string arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case "started":
                    State = Phase.Recording;
                    _since = Time.realtimeSinceStartup;
                    ShowFloating();
                    break;
                case "stopped":
                    {
                        // The take may also end from outside the app (Android's system chip): the time is taken now either way.
                        double seconds = State == Phase.Recording || State == Phase.Stopping ? Time.realtimeSinceStartup - _since : 0;
                        State = Phase.Idle;
                        HideFloating();
                        Uncover();
                        Finished(arg.Length > 0 ? arg : _path, seconds);
                        break;
                    }
                case "nomic":
                    State = Phase.Idle;
                    Uncover();
                    Dialog.Ask(App.I.Toasts, "Microphone needed", "Your voice is the lesson. Allow microphone access in Settings and try again.", "Open Settings", Dialog.OpenAppSettings);
                    break;
                case "cancelled":
                    State = Phase.Idle;
                    HideFloating();
                    Uncover();
                    break;
                default:
                    {
                        bool wasStarting = State == Phase.Starting;
                        State = Phase.Idle;
                        HideFloating();
                        Uncover();
                        Dialog.Ask(App.I.Toasts, wasStarting ? "Could not start recording" : "Recording failed", arg.Length > 0 ? arg : "Try again.", "OK", () => { });
                        break;
                    }
            }
        }

        // ── What happens to a finished take (LessonRecording.stop) ──────────

        static void Finished(string path, double seconds)
        {
            App app = App.I;
            Context c = _context ?? new Context();
            string length = Form.Duration(seconds);
            (string, Action) keep = ("Save or share", () => Share.File(path, "video/mp4"));
            (string, Action) discard = ("Discard", () => { try { System.IO.File.Delete(path); } catch (Exception) { } });
            int st = J.Int(c.Booking, "status");
            if (c.Booking != null && (st == 2 || st == 3))
            {
                object booking = c.Booking;
                Coach.Choose("Recording finished · " + length, "Attach it to the lesson now, or keep it for later.",
                    ("Use in lesson", () => UseInLesson(app, booking, path, seconds)), keep, discard);
                return;
            }
            object player = c.Player;
            object session = c.Session?.Invoke();
            bool canSend = player != null && J.Str(player, "userId").Length > 0 && J.Str(player, "userId") != J.Str(app.Profile, "userId");
            string first = canSend ? LessonInfo.First(J.Str(player, "displayName")) : null;
            var options = new List<(string, Action)>();
            if (canSend && session != null) options.Add(("Deliver as lesson", () => DeliverAsStudioLesson(app, player, session, path, seconds)));
            if (canSend) options.Add(("Send video to " + first, () => App.Run(SendToPlayer(app, player, path, seconds))));
            options.Add(keep);
            options.Add(discard);
            Coach.Choose("Recording finished · " + length, canSend ? "Deliver a full lesson (video, notes, practice plan), or just send " + first + " the video." : null, options.ToArray());
        }

        /// <summary>The open Deliver screen for this booking takes the file; otherwise a new one opens with it.</summary>
        static void UseInLesson(App app, object booking, string path, double seconds)
        {
            if (app.Top is LessonDeliverScreen open && open.BookingId == J.Str(booking, "id")) { open.UseRecording(path, seconds); return; }
            app.Push(new LessonDeliverScreen(app, booking, path, seconds));
        }

        // Start (or reopen) the free studio lesson for this Player and session, then hand the take to Deliver.
        static void DeliverAsStudioLesson(App app, object player, object session, string path, double seconds)
        {
            App.Run(Api.Post("/lesson/coaching/studio/start", new Dictionary<string, object> { { "userId", J.Str(player, "userId") }, { "sessionType", "LAB" }, { "sessionId", J.Get(session, "id") } },
                d =>
                {
                    object b = J.Get(d, "booking");
                    App.Run(Api.Get("/lesson/booking/" + J.Str(b, "id"), full => app.Push(new LessonDeliverScreen(app, full, path, seconds)), _ => app.Push(new LessonDeliverScreen(app, b, path, seconds))));
                },
                e => Coach.Choose("Could not start the lesson", Form.Why(e, "Something went wrong. You can still send the video directly."),
                    ("Send video instead", () => App.Run(SendToPlayer(app, player, path, seconds))), ("Save or share", () => Share.File(path, "video/mp4")), ("Discard", null))));
        }

        // The take to the Player outside a lesson: the same upload, then saveVideo without quiet, so they get the
        // NEW COACHING SESSION push and find it in their Improve tab.
        static IEnumerator SendToPlayer(App app, object player, string path, double seconds)
        {
            string file = "coaching_vid_" + UnityEngine.Random.Range(0, 100000000) + ".mp4";
            Cover("Sending... 0%");
            string url = null; bool done = false;
            yield return Uploads.Video(path, file, f => Cover("Sending... " + Mathf.RoundToInt(f * 100) + "%"), u => { url = u; done = true; });
            if (!done || url == null) { Uncover(); SendFailed(path); yield break; }
            object saved = null; string error = null;
            yield return Api.Post("/coaching/saveVideo", new Dictionary<string, object> { { "fileName", file }, { "userId", J.Str(player, "userId") }, { "coachId", J.Str(app.Profile, "userId", Auth.Uid) },
                { "videoTitle", "Coaching session" }, { "videoLength", Math.Round(seconds) }, { "videoWidth", 0 }, { "videoHeight", 0 } }, d => saved = d, e => error = e ?? "failed");
            Uncover();
            if (saved == null) { SendFailed(path); yield break; }
            Dialog.Ask(app.Toasts, "Sent to " + J.Str(player, "displayName", "your player"), "They got a notification and can watch it in their Improve tab.", "OK", () => { });
        }

        static void SendFailed(string path) => Coach.Choose("Send failed", "The video did not finish uploading. You can keep the recording and try again.",
            ("Save or share", () => Share.File(path, "video/mp4")), ("Discard", () => { try { System.IO.File.Delete(path); } catch (Exception) { } }));

        // ── The floating Stop and the busy cover ─────────────────────────────

        static void ShowFloating()
        {
            if (App.I == null) return;
            if (_floating == null)
            {
                VisualElement square = new VisualElement();
                square.style.width = square.style.height = 12;
                square.style.backgroundColor = Color.white;
                square.style.marginRight = 8;
                _floatingTime = Ui.Num("0:00", "num-20");
                _floating = Ui.Box("row", square, _floatingTime);
                _floating.style.position = Position.Absolute;
                _floating.style.top = 54;
                _floating.style.right = 16;
                _floating.style.height = 44;
                _floating.style.paddingLeft = _floating.style.paddingRight = 16;
                _floating.style.borderTopLeftRadius = _floating.style.borderTopRightRadius = _floating.style.borderBottomLeftRadius = _floating.style.borderBottomRightRadius = 4;
                _floating.style.backgroundColor = Ui.Red;
                _floating.style.justifyContent = Justify.Center;
                _floating.OnTap(Stop);
                _floating.schedule.Execute(() => { if (State == Phase.Recording) _floatingTime.text = Form.Duration(Seconds); }).Every(500);
            }
            _floatingTime.text = "0:00";
            App.I.Toasts.Add(_floating);
            _floating.BringToFront();
        }

        static void HideFloating() => _floating?.RemoveFromHierarchy();

        static void Cover(string words)
        {
            if (App.I == null) return;
            if (_cover == null)
            {
                _coverWords = Ui.Text("", "body center wrap", Ui.Bold);
                _cover = Ui.Box("", Spinner.Big(), _coverWords.Margin(16));
                _cover.style.position = Position.Absolute;
                _cover.style.left = _cover.style.top = _cover.style.right = _cover.style.bottom = 0;
                _cover.style.backgroundColor = Ui.Base;   // solid: the project is linear colour
                _cover.style.alignItems = Align.Center;
                _cover.style.justifyContent = Justify.Center;
                _cover.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            }
            _coverWords.text = words;
            if (_cover.parent == null) App.I.Toasts.Add(_cover);
            _cover.BringToFront();
        }

        static void Uncover() => _cover?.RemoveFromHierarchy();

        /// <summary>The recording guide (LessonWorkspace's renderRecordGuide): in-app steps where the app records, the system recorder's otherwise.</summary>
        public static void Guide(VisualElement over)
        {
            string intro;
            string[] steps;
            if (Supported && Application.platform == RuntimePlatform.Android)
            {
                intro = "Screen record while you review the session and narrate your feedback, then send it from Deliver Lesson. On Android:";
                steps = new[]
                {
                    "Tap the REC button at the top of the lesson.",
                    "Tap Record, then allow the recording when Android asks. Your voice is captured with the microphone.",
                    "Open the session and play through it, talking through your feedback.",
                    "Tap the red timer to stop, then Use in lesson. The video attaches and uploads on the Deliver screen.",
                };
            }
            else if (Supported)
            {
                intro = "Screen record while you review the session and narrate your feedback, then send it from Deliver Lesson. On iOS:";
                steps = new[]
                {
                    "Tap the REC button at the top of the lesson.",
                    CameraSupported ? "Choose Record, or Record with camera to appear in a corner bubble." : "Choose Record.",
                    "Open the session and play through it, talking through your feedback. Only the app and your voice are captured, notifications stay private.",
                    "Tap the red timer to stop, then Use in lesson. The video attaches and uploads on the Deliver screen.",
                };
            }
            else
            {
                intro = "Screen record while you review the session and narrate your feedback, then upload it in Deliver Lesson. On Android:";
                steps = new[]
                {
                    "Swipe down twice from the top to open Quick Settings.",
                    "Tap Screen record (add the tile first if you don't see it). Turn on Record audio (Microphone) so your voice is captured, then Start.",
                    "Come back here and play through the session, talking through your feedback.",
                    "Pull down the notification shade and tap Stop. The video saves to your gallery.",
                    "Open Deliver Lesson and upload the recording.",
                };
            }
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", Ui.Box("row", Ui.Icon("VideoCamera", 22, Ui.Bright), Ui.Title("Record your lesson", "title-20").Margin(0)), Ui.Text(intro, "sub wrap", Ui.Regular).Margin(8));
            for (int i = 0; i < steps.Length; i++)
            {
                // The step's number: a bright green numeral, no tinted disc behind it.
                Label n = Ui.Num((i + 1).ToString(), "num-20 center");
                n.style.color = Ui.Bright;
                VisualElement badge = Ui.Box("", n);
                badge.style.width = 24;
                badge.style.marginRight = 12;
                badge.style.flexShrink = 0;
                VisualElement row = Ui.Box("row", badge, Ui.Text(steps[i], "sub grow wrap", Ui.Regular).Margin(0));
                row.style.alignItems = Align.FlexStart;
                card.Add(row.Margin(12));
            }
            card.Add(Ui.Button("Got it", () => dim.RemoveFromHierarchy()));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(over).Add(dim);
        }
    }

    /// <summary>
    /// The key findings a coach can send with a lesson ({label, value} rows, as BookedLesson and LessonDelivered show
    /// them). puttr-app's Deliver sends findings: null; these are the workspace's own numbers (the submitted session and
    /// the Player's last 30 days, the same analysis the Analysis tab shows) for the coach to pick from.
    /// </summary>
    public static class LessonFindings
    {
        /// <summary>The 27 spots from raw user_putts rows (POST /coaching/analysis/putts), as puttr-app's buildAnalysisGrid.</summary>
        public static List<AnalysisMath.Cell> Grid(List<object> rawPutts)
        {
            var cells = new List<AnalysisMath.Cell>();
            for (int feet = 3; feet <= 11; feet++)
                for (int tee = 1; tee <= 3; tee++)
                    cells.Add(new AnalysisMath.Cell { Feet = feet, Tee = tee });
            foreach (object row in rawPutts ?? new List<object>())
            {
                int feet = J.Int(row, "distance"), tee = J.Int(row, "tee");
                if (feet < 3 || feet > 11 || tee < 1 || tee > 3) continue;
                AnalysisMath.Cell cell = cells[(feet - 3) * 3 + tee - 1];
                cell.Total++;
                if (J.Int(row, "made") == 1) cell.Made++;
            }
            return cells;
        }

        /// <summary>A made putt in a session's putt list (SessionPage's convention: entry 0 is a miss).</summary>
        public static bool SessionMade(object putt) { string e = J.Str(putt, "entry"); return e.Length > 0 && e != "0"; }

        /// <summary>Every finding there is data for, session first, in the order the coach sees them.</summary>
        public static List<(string label, string value)> Candidates(object session, List<object> sessionPutts, List<AnalysisMath.Cell> grid, List<AnalysisMath.Putt> putts, bool lefty)
        {
            var list = new List<(string, string)>();
            if (session != null && J.Get(session, "score") != null) list.Add(("Session grade", Coach.PuttGrade(J.Num(session, "score"))));
            if (sessionPutts != null && sessionPutts.Count > 0)
            {
                int made = 0;
                foreach (object p in sessionPutts) if (SessionMade(p)) made++;
                list.Add(("Session putts made", made + " of " + sessionPutts.Count));
            }
            if (grid != null && AnalysisMath.Putts(grid) > 0)
            {
                list.Add(("Make % (last 30 days)", AnalysisMath.MakePercent(grid) + "%"));
                AnalysisMath.Extremes(grid, out AnalysisMath.Cell best, out AnalysisMath.Cell worst);
                if (best != null) list.Add(("Best spot", best.Feet + "ft " + AnalysisMath.TeeNames[best.Tee - 1] + " · " + best.Percent + "% made"));
                if (worst != null && worst != best) list.Add(("Worst spot", worst.Feet + "ft " + AnalysisMath.TeeNames[worst.Tee - 1] + " · " + worst.Percent + "% made"));
            }
            if (putts != null && putts.Count > 0)
            {
                AnalysisMath.Pace(putts, out int shortOnes, out int ideal, out int firm, out float median);
                int paced = shortOnes + ideal + firm;
                if (paced >= 5)
                {
                    list.Add(("Typical pace", AnalysisMath.Rollout(median)));
                    list.Add(("Ideal pace", Mathf.RoundToInt(100f * ideal / paced) + "% of putts"));
                    list.Add(("Left short", Mathf.RoundToInt(100f * shortOnes / paced) + "% of putts"));
                }
                AnalysisMath.Misses(putts, out int left, out int right);
                if (left + right > 0)
                {
                    int l = Mathf.RoundToInt(100f * left / (left + right));
                    list.Add(("Miss tendency", l >= 50 ? l + "% left" : (100 - l) + "% right"));
                }
                AnalysisMath.PushPull(putts, lefty, out int push, out int pull);
                if (push + pull > 0)
                {
                    int p = Mathf.RoundToInt(100f * push / (push + pull));
                    list.Add(("Push or pull", p == 50 ? "Balanced" : p > 50 ? p + "% push" : (100 - p) + "% pull"));
                }
            }
            return list;
        }

        /// <summary>The body's findings: the picked rows as [{label, value}], or null when none are picked (the API then leaves the column alone).</summary>
        public static List<object> Payload(List<(string label, string value)> candidates, ICollection<string> picked)
        {
            var rows = new List<object>();
            foreach ((string label, string value) c in candidates)
                if (picked.Contains(c.label)) rows.Add(new Dictionary<string, object> { { "label", c.label }, { "value", c.value } });
            return rows.Count > 0 ? rows : null;
        }
    }

    // ── The dashboard ────────────────────────────────────────────────────────

    public sealed class CoachScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        object _application, _academy, _affiliate, _pending, _games;
        int _waiting;
        bool _showEarnings;

        public CoachScreen(App app)
        {
            _app = app;
            _body = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.TabHeader("Coach"));
            scroll.Add(_body);
            scroll.Add(FreePuttScreen.Space(32));
            Root = scroll;
        }

        public override void Shown()
        {
            _waiting = 5;
            void Done() { if (--_waiting <= 0) Draw(); }
            App.Run(Api.Get("/coach/application", d => { _application = J.Get(d, "application"); Done(); }, _ => Done()));
            App.Run(Api.Get("/coach/academy", d => { _academy = d; Done(); }, _ => Done()));
            App.Run(Api.Get("/coach/affiliate", d => { _affiliate = d; Done(); }, _ => { _affiliate = null; Done(); }));
            App.Run(Api.Get("/lesson/coaching/pending", d => { _pending = d; Coach.Pending = J.Int(d, "needsReview"); Done(); }, _ => Done()));
            App.Run(Api.Get("/creator/games/", d => { _games = d; Done(); }, _ => Done()));
        }

        void Draw()
        {
            _body.Clear();
            object goLive = J.Get(_academy, "goLive");
            bool live = J.Flag(_app.Profile, "isTester") || J.Flag(goLive, "live");
            int review = J.Int(_pending, "needsReview"), overdue = J.Int(_pending, "overdue");
            if (review > 0)
            {
                Color c = overdue > 0 ? Ui.Red : Ui.Bright;   // the icon carries the meaning; the card keeps its plain border
                VisualElement banner = Ui.Box("card row", Ui.Icon("Warning", 24, c), Ui.Box("grow set-words", Ui.Text(review + " lesson" + (review == 1 ? "" : "s") + " to review", "body", Ui.Bold),
                    Ui.Text(overdue > 0 ? overdue + " overdue. Deliver soon to avoid a refund." : live ? "Record and deliver to get paid." : "Record and deliver to your player.", "body-2 wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint));
                _body.Add(Ui.Box("pad", banner.OnTap(() => _app.Push(new LessonInboxScreen(_app)))));
            }
            if (goLive != null && !J.Flag(goLive, "live")) GoLive(goLive);
            VisualElement cols = Ui.Box("cols");
            void Add(VisualElement card) => cols.Add(Ui.Box("col pad", card));
            // The banner above carries the count; the card does not repeat it.
            Add(Coach.Card("Tray", "Lessons", live ? "Paid lesson bookings from players" : "Lessons you deliver to your players", 0, Ui.Bright, () => _app.Push(new LessonInboxScreen(_app))));
            Add(Coach.Card("UsersThree", "My Players", "Active practice plans and progress", 0, Ui.Bright, () => _app.Push(new PlayerRosterScreen(_app))));
            Add(Coach.Card("FilmReel", "Player Review", "Review player putting sessions", 0, Ui.Bright, () => _app.Push(new CoachingDashboardScreen(_app))));
            Add(Coach.Card("VideoCamera", "Video Library", "Drills, tips and player videos", 0, Ui.Bright, () => _app.Push(new CoachingVideosScreen(_app))));
            Add(Coach.Card("GraduationCap", "Coach Academy", goLive != null ? "Online coaching modules and certification" : "Training on the studio coaching toolset", 0, Ui.Bright, () => _app.Push(new CoachAcademyScreen(_app))));
            if (goLive != null || live)
                Add(Coach.Card("GearSix", "Coaching Profile & Payouts", J.Flag(goLive, "payoutsEnabled") ? "Headline, price, availability and Stripe payouts" : "Set your price and connect Stripe to get paid", 0, Ui.Bright, () => _app.Push(new CoachLessonSetupScreen(_app))));
            _body.Add(cols);

            List<object> games = J.List(_games);
            _body.Add(Ui.Section("Games & Drills", "Build", () => _app.Push(new CreatorScreen(_app))));
            if (games.Count == 0) _body.Add(Page.Note("No games yet. Build a custom drill for your players."));
            else
            {
                VisualElement grid = Ui.Box("cols");
                foreach (object g in games) { object game = g; grid.Add(Ui.Box("col pad", Ui.Box("card row", Form.Glyph("FillTarget"), Ui.Box("grow set-words", Ui.Text(J.Str(g, "title"), "body", Ui.Bold), Ui.Text(J.Int(g, "putts") + " putts", "body-2", Ui.Regular))).OnTap(() => _app.Push(new EditGameScreen(_app, game)))).Margin(8)); }
                _body.Add(grid);
            }
            Affiliate();
            OnlineCoach(live);
        }

        void GoLive(object gl)
        {
            bool academy = J.Flag(gl, "academyComplete"), cert = J.Flag(gl, "certPassed"), pending = J.Flag(gl, "certPending"), profile = J.Flag(gl, "profileComplete"), payouts = J.Flag(gl, "payoutsEnabled");
            int done = (academy ? 1 : 0) + (cert ? 1 : 0) + (profile ? 1 : 0) + (payouts ? 1 : 0);
            object p = J.Get(gl, "academyProgress");
            VisualElement card = Ui.Box("card", Ui.Text("You are approved. " + done + " of 4 steps done. Tap a step to complete it.", "body wrap", Ui.Bold));
            bool nextShown = false;
            void Row(bool ok, bool waiting, string title, string sub, Action tap)
            {
                bool next = !ok && !waiting && !nextShown;
                if (next) nextShown = true;
                VisualElement row = Ui.Box(next ? "set-row coach-on" : "set-row", Ui.Icon(ok ? "CheckCircle" : waiting ? "Clock" : "Circle", 20, ok ? Ui.Bright : waiting ? Ui.Amber : Ui.Faint), Ui.Box("grow set-words", Ui.Text(title, "body", Ui.Bold), Ui.Text(sub, "body-2 wrap", Ui.Regular)));
                if (tap != null && !ok) row.OnTap(tap);
                card.Add(row);
            }
            Row(academy, false, "Finish the Coach Academy", J.Int(p, "passed") + " of " + J.Int(p, "total") + " online modules done", () => _app.Push(new CoachAcademyScreen(_app)));
            Row(cert, pending, "Coach your certification lesson", cert ? "Passed" : pending ? "Delivered. Puttr is grading it." : academy ? "A practice lesson, graded by Puttr" : "Unlocks after the Academy", academy && !pending ? () => CoachAcademyScreen.BeginCertification(_app, Root) : (Action)null);
            Row(profile, false, "Complete your coaching profile", "Headline, bio, price and intro video", () => _app.Push(new CoachLessonSetupScreen(_app)));
            Row(payouts, false, "Set up payouts", payouts ? "Stripe connected" : "Connect Stripe to get paid for lessons", () => _app.Push(new CoachLessonSetupScreen(_app)));
            _body.Add(Ui.Box("pad", card));
        }

        void Affiliate()
        {
            if (_affiliate == null || (J.Get(_affiliate, "configured") != null && !J.Flag(_affiliate, "configured"))) return;
            VisualElement card = Ui.Box("card", Ui.Text("Affiliate Program", "body", Ui.Bold));
            if (!J.Flag(_affiliate, "enrolled"))
            {
                card.Add(Ui.Text("Earn commission when players you refer buy Puttr gear and memberships", "sub wrap", Ui.Regular).Margin(8));
                card.Add(Ui.Button("Enroll Now", () => App.Run(Api.Post("/coach/affiliate/enroll", new Dictionary<string, object>(), d =>
                {
                    _affiliate = d;
                    if (J.Flag(d, "created")) Dialog.Ask(Root, "Check your email", "Your affiliate account is set up. We sent you the details.", "OK", () => { });
                    Draw();
                }, e => _app.Toast(Form.Why(e, "Could not enroll. Try again."))))).Margin(12));
            }
            else
            {
                string link = J.Str(_affiliate, "link");
                card.Add(Form.Label("Your referral link"));
                card.Add(Ui.Box("coach-well row", Ui.Text(link, "sub grow wrap", Ui.Regular).Margin(0), Ui.Text("Copy", "sec-link coach-link", Ui.Bold).OnTap(() => { GUIUtility.systemCopyBuffer = link; _app.Toast("Copied to clipboard"); })));
                card.Add(Ui.Text(_showEarnings ? "Hide earnings" : "Show earnings", "sec-link", Ui.Medium).Margin(12).OnTap(() => { _showEarnings = !_showEarnings; Draw(); }));
                if (_showEarnings)
                {
                    object s = J.Get(_affiliate, "stats");
                    card.Add(Ui.StatStrip((Dollars(J.Num(s, "totalEarnings")), "Earned"), (Dollars(J.Num(s, "pendingAmount")), "Pending"), (Dollars(J.Num(s, "paidAmount")), "Paid out")).Margin(12));
                    if (J.Str(_affiliate, "portalUrl").Length > 0) card.Add(Ui.Button("Manage Account", () => Application.OpenURL(J.Str(_affiliate, "portalUrl")), "btn btn-ghost").Margin(12));
                }
            }
            _body.Add(Ui.Box("pad", card).Margin(12));
        }

        static string Dollars(double dollars) => "$" + dollars.ToString("0.00", CultureInfo.InvariantCulture);

        void OnlineCoach(bool live)
        {
            int st = _application != null ? J.Int(_application, "status") : -1;
            if (live || (st == 2 && J.Str(_application, "submittedAt").Length > 0)) return;
            string[] chips = { "SUBMITTED", "UNDER REVIEW", "APPROVED", "WAITLISTED", "NOT APPROVED" };
            VisualElement card = _application == null || J.Str(_application, "submittedAt").Length == 0
                ? Ui.Box("card", Ui.Text("Apply to Become a Puttr Online Coach", "body wrap", Ui.Bold), Ui.Text("Give paid video lessons to players anywhere. Apply, get approved, and open your lesson inbox.", "body-2 wrap", Ui.Regular).Margin(8))
                : Ui.Box("card", Ui.Box("row", Ui.Text("Puttr Online Coach", "body grow", Ui.Bold), AdminLabels.Pill(chips[Mathf.Clamp(st, 0, 4)], st == 2 ? Ui.Bright : st == 4 ? Ui.Red : Ui.Amber)));
            _body.Add(Ui.Box("pad", card.OnTap(() => CoachApplyScreen.Open(_app))).Margin(12));
        }
    }

    // ── Lessons ──────────────────────────────────────────────────────────────

    public sealed class LessonInboxScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        List<object> _rows;
        object _earnings, _stripe, _academy;

        public LessonInboxScreen(App app)
        {
            _app = app;
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Box("top", Ui.Text("‹", "back back-chevron", Ui.Medium).OnTap(app.Pop), Ui.Text("Lessons", "top-title", Ui.Bold),
                Ui.Box("row", Ui.IconSquare("Barbell", () => CoachAcademyScreen.OpenPractice(app, Root)), Ui.IconSquare("GearSix", () => app.Push(new CoachLessonSetupScreen(app))))));
            _body = new VisualElement();
            scroll.Add(_body);
            scroll.Add(FreePuttScreen.Space(32));
            Root = scroll;
            _body.Add(Page.Loading());
        }

        public override void Shown()
        {
            int waiting = 4;
            void Done() { if (--waiting == 0) Draw(); }
            App.Run(Api.Get("/lesson/coaching/inbox", d => { _rows = J.List(d); Done(); }, _ => { _rows = new List<object>(); Done(); }));
            App.Run(Api.Get("/lesson/coaching/earnings", d => { _earnings = d; Done(); }, _ => Done()));
            App.Run(Api.Get("/lesson/coaching/stripe/status", d => { _stripe = d; Done(); }, _ => Done()));
            App.Run(Api.Get("/coach/golive", d => { _academy = J.Get(d, "goLive"); Done(); }, _ => Done()));
            Coach.RefreshPending();
        }

        void Draw()
        {
            _body.Clear();
            bool live = J.Flag(_app.Profile, "isTester") || J.Flag(_academy, "live");
            int delivered = J.Int(_earnings, "lessons");
            if (live || delivered > 0) _body.Add(Ui.Box("pad", Ui.Text("$" + (J.Num(_earnings, "earned") / 100).ToString("0", CultureInfo.InvariantCulture) + " earned · " + delivered + " delivered", "body-2", Ui.Medium)));
            double pendingCents = J.Num(_stripe, "pendingCents");
            if (!J.Flag(_stripe, "payoutsEnabled") && (live || pendingCents > 0))
            {
                int n = J.Int(_stripe, "pendingCount");
                _body.Add(Ui.Box("pad", Ui.Box("card row", Ui.Icon("Warning", 24, Ui.Amber), Ui.Box("grow set-words", Ui.Text(pendingCents > 0 ? "Set up payouts to get paid" : "Set up payouts", "body", Ui.Bold),
                    Ui.Text(pendingCents > 0 ? LessonInfo.Money(pendingCents) + " from " + n + " delivered lesson" + (n == 1 ? "" : "s") + " is waiting. Connect your bank to receive it." : "You can accept lessons now. Connect your bank to receive your earnings.", "body-2 wrap", Ui.Regular))).OnTap(() => _app.Push(new CoachLessonSetupScreen(_app)))));
            }
            List<object> normal = _rows.FindAll(b => !J.Flag(b, "isTutorial"));
            Section("Needs review", normal.FindAll(b => J.Int(b, "status") == 2 || J.Int(b, "status") == 3));
            Section("Waiting on session", normal.FindAll(b => J.Int(b, "status") == 1));
            Section("Delivered", normal.FindAll(b => J.Int(b, "status") >= 4));
            Section("Practice space", _rows.FindAll(b => J.Flag(b, "isTutorial")));
            if (_rows.Count == 0)
            {
                _body.Add(Ui.Box("pad", Ui.Box("card center-box", Ui.Icon("Tray", 30, Ui.Faint),
                    Ui.Text(live ? "No lessons yet. Once your profile is live, new bookings land here." : "No lessons yet. Record a review of a player's session in Player Review and choose Deliver as lesson.", "sub center wrap", Ui.Regular).Margin(8),
                    Ui.Button(live ? "Set Up Your Profile" : "Open Player Review", () => _app.Push(live ? (AppScreen)new CoachLessonSetupScreen(_app) : new CoachingDashboardScreen(_app))).Margin(12))));
            }
        }

        void Section(string title, List<object> rows)
        {
            if (rows.Count == 0) return;
            _body.Add(Ui.Section(title.ToUpperInvariant() + " · " + rows.Count));
            VisualElement cols = Ui.Box("cols");
            foreach (object b in rows) cols.Add(Ui.Box("col pad", Row(b)).Margin(8));
            _body.Add(cols);
        }

        VisualElement Row(object b)
        {
            int st = J.Int(b, "status");
            string kind = J.Flag(b, "isCertification") ? "CERTIFICATION · GRADED BY PUTTR" : J.Int(b, "isStudio") == 1 ? "STUDIO · YOUR PLAYER" : J.Flag(b, "isTutorial") ? "TUTORIAL · PRACTICE SPACE" : null;
            string line = st == 1 ? "Waiting on their session" : st == 2 ? "Session submitted " + Form.Ago(b, "submittedAt") : st == 3 ? "You started this review" : st == 9 ? "Expired and refunded" : "Delivered " + Form.Ago(b, "deliveredAt");
            VisualElement right = null;
            if (st == 2 || st == 3)
            {
                if (Form.When(b, "dueAt") is DateTime due)
                {
                    double hours = (due - DateTime.Now).TotalHours;
                    right = hours < 0 ? AdminLabels.Pill("Overdue", Ui.Red) : hours < 24 ? AdminLabels.Pill("Due in " + Math.Max(1, (int)hours) + "h", Ui.Red) : AdminLabels.Pill("Due " + due.ToString("ddd h tt", CultureInfo.InvariantCulture), Ui.Amber);
                }
            }
            else if (st >= 4) right = J.Get(b, "rating") != null ? LessonInfo.StarText(J.Num(b, "rating"), "body-2") : J.Num(b, "price") > 0 ? (VisualElement)AdminLabels.Pill("+" + LessonInfo.Money(J.Num(b, "price") - J.Num(b, "platformFee")), Ui.Bright) : null;
            string hcp = J.Str(b, "playerHandicap");
            VisualElement card = Ui.Box("card",
                kind != null ? AdminLabels.Pill(kind, Ui.Bright) : null,
                Ui.Box("row", Ui.Avatar(J.Str(b, "playerName"), J.Str(b, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(J.Str(b, "playerName") + (hcp.Length > 0 ? "  ·  hcp " + hcp : ""), "body", Ui.Bold),
                    J.Str(b, "goals").Length > 0 ? Ui.Text(J.Str(b, "goals"), "body-2", Ui.Regular) : null, Ui.Text(line, "body-2", Ui.Regular)), right).Margin(kind != null ? 8 : 0));
            return card.OnTap(() => Coach.OpenBooking(_app, b));
        }
    }

    /// <summary>
    /// LessonWorkspace: the coach's review of one booking. Phone: a fixed top (the tutorial or certification banner, the
    /// Player header with REC, Analysis | Session) over the tab's content, and Start Review and Deliver Lesson pinned
    /// below. Tablet or landscape (700 wide and up): the Player's analysis and the session side by side, no tabs.
    /// The Analysis tab is the Player's full analysis page (AnalysisScreen.BuildInto), as puttr-app's PlayerStatsPanel.
    /// </summary>
    public sealed class LessonWorkspaceScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _top, _content, _foot, _analysis;
        object _booking, _session;
        List<object> _putts;
        bool _analysisTab = true, _wide, _starting, _reviewStarted, _analysisBuilt, _sessionTried;
        string _sessionFor;

        public LessonWorkspaceScreen(App app, string bookingId)
        {
            _app = app;
            _id = bookingId;
            Root = new VisualElement();
            Root.style.flexGrow = 1;
            _top = new VisualElement();
            _content = new VisualElement();
            _content.style.flexGrow = 1;
            _content.style.flexShrink = 1;
            _foot = new VisualElement();
            _analysis = new VisualElement();
            Root.Add(Ui.Header("Lesson", app.Pop));
            Root.Add(_top);
            Root.Add(_content);
            Root.Add(_foot);
            _content.Add(Page.Loading());
            // Two columns when there is room for both (legacy's 700-point split), redrawn only when that changes.
            Root.RegisterCallback<GeometryChangedEvent>(e =>
            {
                bool wide = e.newRect.width >= 700f;
                if (wide == _wide) return;
                _wide = wide;
                if (_booking != null) Draw();
            });
        }

        public override void Shown() => App.Run(Api.Get("/lesson/booking/" + _id, b =>
        {
            _booking = b;
            Draw();
            string sessionId = J.Str(b, "sessionId");
            if (sessionId.Length == 0 || sessionId == _sessionFor) return;
            _sessionFor = sessionId;
            App.Run(Api.Get("/coaching/user/sessions/" + J.Str(b, "userId"), d => { _session = J.List(d).Find(s => J.Str(s, "id") == sessionId); _sessionTried = true; Draw(); }, _ => { _sessionTried = true; Draw(); }));
            App.Run(Api.Get("/coaching/session/putts/" + sessionId, d => { _putts = J.List(d); Draw(); }, _ => { _putts = new List<object>(); Draw(); }));
        }, _ => { _content.Clear(); _content.Add(Page.Note("Couldn't load this lesson.")); }));

        int Status => J.Int(_booking, "status");
        bool CanWork => Status == 2 || Status == 3;
        string First => LessonInfo.First(J.Str(_booking, "playerName"));

        void Draw()
        {
            object b = _booking;
            if (!_analysisBuilt && (J.Str(b, "sessionId").Length == 0 || _sessionTried))
            {
                // Built once, so the range the coach picked survives switching tabs and redraws. It waits for the session
                // row, which carries the Player's handedness (bookings do not), so pushes and pulls read the right way round.
                _analysisBuilt = true;
                _analysis.Clear();
                string hand = J.Str(_session, "handedness");
                if (hand.Length > 0) AnalysisScreen.BuildInto(_app, _analysis, J.Str(b, "userId"), hand);
                else AnalysisScreen.BuildInto(_app, _analysis, J.Str(b, "userId"));
            }
            else if (!_analysisBuilt && _analysis.childCount == 0) _analysis.Add(Page.Loading());

            _top.Clear();
            VisualElement top = Ui.Box("pad");
            bool cert = J.Flag(b, "isCertification"), tutorial = J.Flag(b, "isTutorial");
            if (cert || tutorial)
            {
                Label title = Ui.Text(cert ? "Certification lesson" : "Tutorial mode", "body", Ui.Bold);
                VisualElement banner = Ui.Box("card", title, Ui.Text(cert ? "Coach this exactly like a real lesson. Your delivery goes to Puttr for grading, not to a player." : "A practice space with real tools and a sample session. Nothing you do here reaches a real player.", "body-2 wrap", Ui.Regular).Margin(4));
                top.Add(banner.Margin(4));
            }
            string hcp = Coach.Handicap(b, "playerHandicap");
            string sub = (hcp.Length > 0 ? "hcp " + hcp + " · " : "") + (Form.Date(b, "dueAt").Length > 0 && CanWork ? "due " + Form.Date(b, "dueAt", "ddd MMM d, h:mm tt") : J.Int(b, "isStudio") == 1 ? "studio lesson" : "$" + (J.Num(b, "price") / 100).ToString("0", CultureInfo.InvariantCulture) + " lesson");
            var context = new Recorder.Context { Booking = b };
            top.Add(Ui.Box("row", Ui.Avatar(J.Str(b, "playerName"), J.Str(b, "playerPhoto")), Ui.Box("grow set-words", Ui.Title(J.Str(b, "playerName"), "title-20"), Ui.Text(sub, "body-2", Ui.Regular)), CanWork ? Recorder.Pill(Root, context) : null).Margin(12));
            if (!_wide) top.Add(FreePuttScreen.Choice(new[] { "Analysis", "Session" }, _analysisTab ? 0 : 1, i => { _analysisTab = i == 0; Draw(); }));
            _top.Add(top);

            _content.Clear();
            _analysis.RemoveFromHierarchy();
            if (_wide)
            {
                ScrollView left = Ui.Scroll(), right = Ui.Scroll();
                left.style.flexGrow = 1; left.style.flexBasis = 0;
                right.style.width = 400; right.style.flexGrow = 0; right.style.flexShrink = 0;
                left.Add(_analysis);
                left.Add(FreePuttScreen.Space(24));
                right.Add(SessionTab());
                right.Add(FreePuttScreen.Space(24));
                VisualElement split = Ui.Box("row", left, right);
                split.style.flexGrow = 1;
                split.style.alignItems = Align.Stretch;
                _content.Add(split);
            }
            else
            {
                ScrollView scroll = Ui.Scroll();
                scroll.style.flexGrow = 1;
                scroll.Add(_analysisTab ? _analysis : SessionTab());
                scroll.Add(FreePuttScreen.Space(24));
                _content.Add(scroll);
            }

            _foot.Clear();
            if (!CanWork) return;
            bool showStart = Status == 2 && !_reviewStarted;
            bool inReview = _reviewStarted || Status == 3;
            VisualElement foot = Ui.Box("pad");
            foot.style.paddingTop = 8;
            foot.style.paddingBottom = 16;
            foot.style.borderTopWidth = 1;
            foot.style.borderTopColor = Ui.Line;
            foot.style.backgroundColor = Ui.Base;
            if (inReview) foot.Add(Ui.Text("How to screen record", "sec-link center", Ui.Medium).OnTap(() => Recorder.Guide(Root)));
            VisualElement buttons = Ui.Box("row");
            if (showStart)
            {
                VisualElement start = Ui.Button(_starting ? "Starting..." : "Start Review", StartReview, "btn btn-ghost");
                start.style.flexGrow = 1; start.style.flexBasis = 0; start.style.marginRight = 8;
                buttons.Add(start);
            }
            VisualElement deliver = Ui.Button("Deliver Lesson", () => _app.Push(new LessonDeliverScreen(_app, _booking)));
            deliver.style.flexGrow = showStart ? 1.4f : 1f; deliver.style.flexBasis = 0;
            buttons.Add(deliver);
            foot.Add(buttons);
            _foot.Add(foot);
        }

        void StartReview()
        {
            if (_starting) return;
            _starting = true;
            Draw();
            App.Run(Api.Post("/lesson/coaching/start", new Dictionary<string, object> { { "bookingId", _id } }, _ =>
            {
                // Start Review goes at once; the guide shows how to record; the booking reloads as in review.
                _starting = false;
                _reviewStarted = true;
                Draw();
                Recorder.Guide(Root);
                Shown();
            }, e => { _starting = false; Draw(); _app.Toast(Form.Why(e, "Could not start the review.")); }));
        }

        VisualElement SessionTab()
        {
            object b = _booking;
            VisualElement p = Ui.Box("pad");
            if (J.Str(b, "goals").Length > 0)
            {
                Label goals = Ui.Text("“" + J.Str(b, "goals") + "”", "sub wrap", Ui.Regular);
                goals.style.unityFontStyleAndWeight = FontStyle.Italic;
                p.Add(Ui.Box("card", Ui.Text(First + "'s goals", "body", Ui.Bold), goals.Margin(4)).Margin(8));
            }
            p.Add(Form.Label("Submitted session"));
            if (J.Str(b, "sessionId").Length == 0)
            {
                p.Add(Ui.Box("card", Ui.Text("Waiting on " + First + " to submit a session. Their turnaround clock has not started.", "sub wrap", Ui.Regular)));
                return p;
            }
            object s = _session;
            string when = s == null ? "Loading session..." : "Submitted " + (J.Str(b, "submittedAt").Length > 0 ? Form.Ago(b, "submittedAt") : Form.Ago(s, "created_at")) + (_putts != null && _putts.Count > 0 ? " · " + _putts.Count + " putts" : "");
            Label grade = null;
            if (s != null && J.Get(s, "score") != null) grade = AdminLabels.Pill(Coach.PuttGrade(J.Num(s, "score")), Ui.Bright);
            p.Add(Ui.Box("card row", Form.Glyph("VideoCamera"), Ui.Box("grow set-words", Ui.Text("Stroke Lab session", "body", Ui.Bold), Ui.Text(when, "body-2", Ui.Regular)), grade));
            if (_putts != null && _putts.Count > 0)
            {
                VisualElement list = Ui.Box("coach-list");
                for (int i = 0; i < _putts.Count; i++)
                {
                    object putt = _putts[i];
                    bool made = LessonFindings.SessionMade(putt);
                    Label name = Ui.Text("Putt " + (i + 1), "body", Ui.Medium);
                    name.style.width = 64;
                    name.style.marginLeft = 8;
                    string speed = J.Get(putt, "speed") != null && J.Str(putt, "speed").Length > 0 ? J.Num(putt, "speed").ToString("0.0", CultureInfo.InvariantCulture) + " speed" : "";
                    list.Add(Ui.Box("set-row", Ui.Icon(made ? "CheckCircle" : "XCircle", 16, made ? Ui.Bright : Ui.Red), name,
                        Ui.Text(J.Int(putt, "distance") + " ft · tee " + J.Int(putt, "tee"), "body-2 grow", Ui.Regular), speed.Length > 0 ? Ui.Text(speed, "body-2", Ui.Medium) : null));
                }
                p.Add(list.Margin(12));
            }
            else if (s != null && _putts == null) p.Add(Page.Loading());
            VisualElement watch = Ui.Box("card row", Form.Glyph("Play"), Ui.Box("grow set-words", Ui.Text("Watch the session", "body", Ui.Bold), Ui.Text(s != null ? "Replay, pose overlay, and drawing tools" : "Loading session...", "body-2", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint));
            if (s != null) watch.OnTap(() =>
            {
                // The coach's REC sits on the session screens too, attached to this lesson.
                CoachSessionScreen.HeaderExtra = () => Recorder.Pill(_app.Toasts, new Recorder.Context { Booking = b });
                _app.Push(LabSessionScreen.ForCoach(_app, J.Str(s, "id"), J.Str(b, "playerName")));
            });
            else watch.style.opacity = 0.6f;
            p.Add(watch.Margin(12));
            return p;
        }
    }

    public sealed class LessonDeliverScreen : AppScreen
    {
        readonly App _app;
        object _booking;
        readonly VisualElement _body, _video, _findings, _info, _send;
        string _summary = "", _videoId;
        double _length;
        bool _uploading, _sending, _drawn;
        float _progress;
        List<(string label, string value)> _candidates;
        readonly HashSet<string> _picked = new HashSet<string>();
        string _findingsFor;

        public string BookingId => J.Str(_booking, "id");

        /// <param name="recordedPath">A take from the in-app recorder ("Use in lesson"): uploaded at once, no picker.</param>
        public LessonDeliverScreen(App app, object booking, string recordedPath = null, double recordedSeconds = 0)
        {
            _app = app;
            _booking = booking;
            Root = Page.Make(app, "Deliver Lesson", out _body);
            _video = new VisualElement();
            _findings = new VisualElement();
            _info = new VisualElement();
            _send = new VisualElement();
            Draw();
            if (!string.IsNullOrEmpty(recordedPath)) UseRecording(recordedPath, recordedSeconds);
        }

        public override void Shown() => App.Run(Api.Get("/lesson/booking/" + BookingId, b =>
        {
            _booking = b;
            // The plan row and the info card follow the booking; the notes field is left alone so typing is never lost.
            Draw();
        }, _ => { }));

        /// <summary>The recorder's file goes straight into the lesson-video slot.</summary>
        public void UseRecording(string path, double seconds)
        {
            if (_uploading) { _app.Toast("Wait for the current upload to finish."); return; }
            App.Run(Uploads.Length(path, measured => Upload(path, measured > 0 ? measured : seconds)));
        }

        void Draw()
        {
            object b = _booking;
            string name = J.Str(b, "playerName"), first = LessonInfo.First(name);
            if (!_drawn)
            {
                // The page is built once; only its parts that change (video, findings, plan, send) redraw.
                _drawn = true;
                _body.Clear();
                VisualElement p = Ui.Box("pad", Ui.Text("to " + name, "sub", Ui.Regular));
                p.Add(Form.Label("1 · Lesson video"));
                p.Add(_video);
                p.Add(Form.Label("2 · Summary notes"));
                TextField notes = Form.Field("What you saw, what to work on, and what success looks like for " + first + ".", _summary, 4000, true);
                notes.RegisterValueChangedCallback(e => _summary = e.newValue);
                p.Add(notes);
                p.Add(_findings);
                p.Add(_info);
                _body.Add(p);
                _body.Add(_send);
            }
            Video();
            Findings();
            Info();
        }

        void Video()
        {
            _video.Clear();
            if (_uploading)
                _video.Add(Ui.Box("card", Ui.Box("row", Ui.Text("Uploading...", "body grow", Ui.Bold), Ui.Text(Mathf.RoundToInt(_progress * 100) + "%", "body", Ui.Bold)), Ui.Progress(_progress).Margin(8), Ui.Text("Keep the app open until the upload finishes.", "body-2", Ui.Regular).Margin(8)));
            else if (_videoId != null)
                _video.Add(Ui.Box("card row", Ui.Icon("CheckCircle", 24, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Video uploaded", "body", Ui.Bold), Ui.Text((_length > 0 ? Form.Duration(_length) + " · " : "") + "ready to send", "body-2", Ui.Regular)), Ui.Text("Replace", "sec-link", Ui.Medium).OnTap(Pick)));
            else
                _video.Add(Ui.Box("dashed", Ui.Icon("VideoCamera", 36, Ui.Faint), Ui.Text("Upload your lesson video", "body", Ui.Bold).Margin(12), Ui.Text("Tap REC while reviewing their session to record your breakdown in the app, or pick a video from your camera roll.", "body-2 center wrap", Ui.Regular).Margin(4)).OnTap(Pick));
        }

        // Key findings: the workspace's numbers (the submitted session, the Player's last 30 days) for the coach to pick.
        void Findings()
        {
            string userId = J.Str(_booking, "userId");
            if (_findingsFor != userId && userId.Length > 0)
            {
                _findingsFor = userId;
                App.Run(LoadFindings(userId, J.Str(_booking, "sessionId")));
            }
            _findings.Clear();
            if (_candidates == null || _candidates.Count == 0) return;
            _findings.Add(Form.Label("Key findings · optional"));
            _findings.Add(Ui.Text("Tap the numbers to send with the lesson.", "body-2 wrap", Ui.Regular));
            VisualElement card = Ui.Box("coach-list");
            foreach ((string label, string value) c in _candidates)
            {
                string key = c.label;
                bool on = _picked.Contains(key);
                VisualElement row = Ui.Box(on ? "set-row coach-on" : "set-row", Ui.Icon(on ? "CheckCircle" : "Circle", 20, on ? Ui.Bright : Ui.Faint), Ui.Text(c.label, "sub grow", Ui.Regular).Margin(0), Ui.Text(c.value, "body", Ui.Bold));
                card.Add(row.OnTap(() => { if (!_picked.Remove(key)) _picked.Add(key); Findings(); }));
            }
            _findings.Add(card.Margin(8));
        }

        IEnumerator LoadFindings(string userId, string sessionId)
        {
            object session = null;
            List<object> sessionPutts = null, raw = null;
            int waiting = sessionId.Length > 0 ? 3 : 1;
            if (sessionId.Length > 0)
            {
                App.Run(Api.Get("/coaching/user/sessions/" + userId, d => { session = J.List(d).Find(s => J.Str(s, "id") == sessionId); waiting--; }, _ => waiting--));
                App.Run(Api.Get("/coaching/session/putts/" + sessionId, d => { sessionPutts = J.List(d); waiting--; }, _ => waiting--));
            }
            App.Run(Api.Post("/coaching/analysis/putts", new Dictionary<string, object> { { "userId", userId }, { "days", 30 } }, d => { raw = J.List(d); waiting--; }, _ => waiting--));
            while (waiting > 0) yield return null;
            bool lefty = J.Str(session, "handedness").ToUpperInvariant().StartsWith("L");
            _candidates = LessonFindings.Candidates(session, sessionPutts, raw != null ? LessonFindings.Grid(raw) : null, raw != null ? AnalysisMath.ReadAll(raw) : null, lefty);
            Findings();
        }

        void Info()
        {
            object b = _booking;
            string first = LessonInfo.First(J.Str(b, "playerName"));
            bool cert = J.Flag(b, "isCertification"), tutorial = J.Flag(b, "isTutorial"), studio = J.Int(b, "isStudio") == 1;
            _info.Clear();
            _info.Add(Form.Label("3 · Practice plan"));
            bool hasPlan = J.Str(b, "planId").Length > 0;
            _info.Add(Ui.Box("card row", Form.Glyph(hasPlan ? "ListChecks" : "Plus"), Ui.Box("grow set-words", Ui.Text(hasPlan ? "Practice plan attached" : "Add a practice plan", "body", Ui.Bold), Ui.Text(hasPlan ? "Tap to edit games, targets, and duration" : "Assign games and drills. Strongly recommended.", "body-2 wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                .OnTap(() => _app.Push(hasPlan ? new AssignPlanScreen(_app, null, null, J.Str(b, "playerName"), J.Str(b, "planId")) : new AssignPlanScreen(_app, J.Str(b, "id"), null, J.Str(b, "playerName"), null))));
            if (cert || tutorial || studio)
                _info.Add(Ui.Box("card", Ui.Text(cert ? "Graded by Puttr" : studio ? "Studio lesson" : "Practice run", "body", Ui.Medium),
                    Ui.Text(cert ? "This delivery goes to the Puttr team, not a player. You hear back within 3 business days." : studio ? "Delivered free to your own player. They get a notification and can rewatch it any time." : "Nothing is sent to anyone. This is the full delivery flow with the training wheels on.", "body-2 wrap", Ui.Regular).Margin(4)).Margin(16));
            else
            {
                Label earn = Ui.Num(LessonInfo.Money(J.Num(b, "price") - J.Num(b, "platformFee")), "num-20");
                earn.style.color = Ui.Bright;
                _info.Add(Ui.Box("card row", Ui.Box("grow set-words", Ui.Text("You earn", "body", Ui.Medium), Ui.Text("Paid through Stripe after delivery", "body-2 wrap", Ui.Regular)), earn).Margin(16));
            }
            _send.Clear();
            VisualElement send = Ui.Button(_sending ? "Sending..." : cert ? "Submit for Grading" : tutorial ? "Finish Practice Delivery" : "Send Lesson to " + first, () => App.Run(Send(cert, tutorial, studio, first)));
            if (_sending || _uploading) send.AddToClassList("btn-off");
            _send.Add(Form.Bottom(send));
        }

        void Pick()
        {
            if (_uploading) return;
            PhotoPicker.PickVideo(path =>
            {
                if (path == null) return;
                App.Run(Uploads.Length(path, seconds => Upload(path, seconds)));
            });
        }

        // Upload a local video and register it (quiet: the Player hears on Send, not now). Shared by the camera-roll
        // picker and the in-app recording.
        void Upload(string path, double seconds)
        {
            if (_uploading) return;
            _length = Math.Max(0, seconds);
            _uploading = true; _progress = 0; Video(); Info();
            string file = "lesson_vid_" + BookingId + "_" + UnityEngine.Random.Range(0, 100000000) + ".mp4";
            App.Run(Uploads.Video(path, file, f => { _progress = f; Video(); }, url =>
            {
                if (url == null) { _uploading = false; Video(); Info(); Dialog.Ask(Root, "Upload failed", "Your video did not finish uploading. Please try again.", "OK", () => { }); return; }
                var save = new Dictionary<string, object> { { "fileName", file }, { "userId", J.Str(_booking, "userId") }, { "coachId", J.Str(_app.Profile, "userId", Auth.Uid) }, { "videoTitle", "Lesson for " + J.Str(_booking, "playerName", "player") },
                    { "videoLength", Math.Round(_length) }, { "videoWidth", 0 }, { "videoHeight", 0 }, { "quiet", true } };
                App.Run(Api.Post("/coaching/saveVideo", save, r => { _uploading = false; _videoId = J.Str(r, "insertId"); Video(); Info(); }, e => { _uploading = false; Video(); Info(); _app.Toast(Form.Why(e, "Could not save the video.")); }));
            }));
        }

        IEnumerator Send(bool cert, bool tutorial, bool studio, string first)
        {
            if (_sending || _uploading) yield break;
            if (_videoId == null) { Dialog.Ask(Root, "Add your lesson video", "Upload the lesson video before sending.", "OK", () => { }); yield break; }
            if (_summary.Trim().Length == 0) { Dialog.Ask(Root, "Add a summary", "Write a few sentences so " + J.Str(_booking, "playerName", "the player") + " knows what to focus on.", "OK", () => { }); yield break; }
            _sending = true; Info();
            object done = null; string error = null;
            var body = new Dictionary<string, object> { { "bookingId", BookingId }, { "videoId", _videoId }, { "coachSummary", _summary.Trim() }, { "findings", _candidates != null ? LessonFindings.Payload(_candidates, _picked) : null } };
            yield return Api.Post("/lesson/coaching/deliver", body, d => done = d, e => error = e ?? "failed");
            _sending = false;
            if (done == null) { Info(); Dialog.Ask(Root, "Could not send", Form.Why(error, "Something went wrong sending the lesson. Please try again."), "OK", () => { }); yield break; }
            Coach.RefreshPending();
            string player = J.Str(_booking, "playerName", "Your player");
            string title = cert ? "Submitted for grading" : tutorial ? "Practice delivery complete" : "Lesson sent!";
            string words = cert ? "Nice work. Puttr grades certification lessons within 3 business days, and you will get a push and an email with the result."
                : tutorial ? "That is the whole flow, exactly what a real lesson feels like. Nothing was sent to anyone. Reopen tutorial mode any time from your lesson inbox."
                : studio ? player + " has been notified. They can watch it and follow their plan in the app."
                : player + " has been notified. You earn " + LessonInfo.Money(J.Num(_booking, "price") - J.Num(_booking, "platformFee")) + ".";
            Dialog.Ask(_app.Toasts, title, words, "Done", () => { });
            while (_app.Top != null && !(_app.Top is LessonInboxScreen)) _app.Pop();
        }
    }

    public sealed class LessonDeliveredScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;

        public LessonDeliveredScreen(App app, string bookingId)
        {
            _app = app;
            _id = bookingId;
            Root = Page.Make(app, "Lesson", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/lesson/booking/" + _id, Draw, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load this lesson.")); }));

        void Draw(object b)
        {
            _body.Clear();
            string first = LessonInfo.First(J.Str(b, "playerName"));
            bool studio = J.Int(b, "isStudio") == 1, special = J.Flag(b, "isCertification") || J.Flag(b, "isTutorial");
            string sub = "Delivered " + Form.Ago(b, "deliveredAt") + (studio ? " · studio lesson" : J.Num(b, "price") > 0 ? " · you earned " + LessonInfo.Money(J.Num(b, "price") - J.Num(b, "platformFee")) : "");
            _body.Add(Ui.Box("pad row", Ui.Avatar(J.Str(b, "playerName"), J.Str(b, "playerPhoto")), Ui.Box("grow set-words", Ui.Title(J.Str(b, "playerName"), "title-20"), Ui.Text(sub, "body-2 wrap", Ui.Regular)), J.Int(b, "status") == 5 ? AdminLabels.Pill("Completed", Ui.Faint) : null));
            if (special) _body.Add(Page.Note(J.Flag(b, "isCertification") ? "Your certification lesson is with Puttr for grading." : "This was a practice delivery. Nothing reached a real player."));
            else
            {
                VisualElement card = Ui.Box("coach-list",
                    Ui.Box("set-row", Ui.Text("Watched", "sub grow", Ui.Regular), Ui.Text(J.Str(b, "watchedAt").Length > 0 ? "Watched" : "Not watched yet", "body", Ui.Bold)));
                if (!studio) card.Add(Ui.Box("set-row", Ui.Text("Rating", "sub grow", Ui.Regular), J.Get(b, "rating") != null ? (VisualElement)LessonInfo.StarText(J.Num(b, "rating")) : Ui.Text("No rating yet", "body-2", Ui.Regular)));
                if (J.Str(b, "review").Length > 0) card.Add(Ui.Text(first + "'s review: “" + J.Str(b, "review") + "”", "sub wrap", Ui.Regular).Margin(8));
                _body.Add(Ui.Box("pad", card));
            }
            if (J.Str(b, "planId").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("card row", Ui.Icon("ListChecks", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Practice plan", "body", Ui.Bold), Ui.Text("Tap for the full monitor", "body-2", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                .OnTap(() => _app.Push(new PlayerMonitorScreen(_app, J.Str(b, "planId"))))));
            else if (!special) _body.Add(Page.Note("No practice plan was attached to this lesson."));
            _body.Add(Ui.Section("Your lesson video"));
            if (J.Str(b, "videoUrl").Length > 0) _body.Add(Ui.Box("pad", VideoBox.Make(J.Str(b, "videoUrl"), 220, J.Str(b, "videoThumbnail"), J.Num(b, "videoLength")),
                J.Str(b, "videoTranscodeStatus") == "pending" ? Ui.Text("Optimizing for streaming…", "body-2", Ui.Regular).Margin(8) : null));
            else _body.Add(Page.Note("The lesson video is still processing. Check back in a moment."));
            if (J.Str(b, "coachSummary").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("card", Ui.Text("Your notes", "body", Ui.Bold), Ui.Text(J.Str(b, "coachSummary"), "sub wrap", Ui.Regular).Margin(8))));
            List<object> findings = J.List(J.Parse(J.Str(b, "findings")));
            if (findings.Count > 0)
            {
                VisualElement card = Ui.Box("coach-list");
                foreach (object f in findings) card.Add(Ui.Box("set-row", Ui.Text(J.Str(f, "label"), "sub grow", Ui.Regular), Ui.Text(J.Str(f, "value"), "body", Ui.Bold)));
                _body.Add(Ui.Section("Key findings"));
                _body.Add(Ui.Box("pad", card));
            }
            if (J.Str(b, "goals").Length > 0) _body.Add(Ui.Box("pad", Ui.Box("card", Ui.Text(first + "'s goals", "body", Ui.Bold), Ui.Text(J.Str(b, "goals"), "sub wrap", Ui.Regular).Margin(8))));
            _body.Add(Ui.Box("pad", Ui.Box("card row", Ui.Box("grow set-words", Ui.Text("Revisit the session", "body", Ui.Bold), Ui.Text(first + "'s submitted session and putting analysis", "body-2 wrap", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                .OnTap(() => _app.Push(new LessonWorkspaceScreen(_app, _id)))));
        }
    }

    // ── The coaching profile and payouts ─────────────────────────────────────

    public sealed class CoachLessonSetupScreen : AppScreen
    {
        static readonly int[] Turnarounds = { 24, 48, 72, 168 };
        static readonly string[] Keys = { "STROKE", "SPEED", "GREEN_READING", "PRESSURE" };
        readonly App _app;
        readonly VisualElement _body, _payouts;
        string _headline = "", _bio = "", _price = "", _intro = "";
        int _turnaround = 72;
        bool _accepting;
        readonly HashSet<string> _specialties = new HashSet<string>();
        bool _saving;

        public CoachLessonSetupScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Coaching Profile", out _body);
            _payouts = new VisualElement();
            _body.Add(Page.Loading());
            App.Run(Api.Get("/lesson/coaching/profile", d =>
            {
                _headline = J.Str(d, "headline"); _bio = J.Str(d, "bio"); _intro = J.Str(d, "introVideoUrl");
                _price = J.Num(d, "lessonPrice") > 0 ? J.Num(d, "lessonPrice").ToString("0", CultureInfo.InvariantCulture) : "";
                _turnaround = J.Int(d, "turnaroundHours", 72);
                _accepting = J.Int(d, "acceptingLessons") == 1;
                foreach (string s in J.Str(d, "specialties").Split(',')) if (s.Trim().Length > 0) _specialties.Add(s.Trim());
                Draw();
            }, _ => Draw()));
        }

        public override void Shown() => App.Run(Api.Get("/lesson/coaching/stripe/status", Payouts, _ => { }));

        void Draw()
        {
            _body.Clear();
            VisualElement p = Ui.Box("pad", Ui.Text("How players find and book you for online lessons", "sub wrap", Ui.Regular));
            p.Add(Form.Label("Photo"));
            VisualElement photo = Ui.Avatar(J.Str(_app.Profile, "displayName"), J.Str(_app.Profile, "photoURL"), true);
            p.Add(Ui.Box("row", photo, Ui.Text("Change photo", "sec-link", Ui.Medium).OnTap(() => PhotoPicker.ChangeProfilePhoto(_app, ok => { if (ok) Draw(); }))));
            p.Add(Form.Label("Headline"));
            TextField headline = Form.Field("Short game specialist, 20 yrs teaching", _headline, 120);
            headline.RegisterValueChangedCallback(e => _headline = e.newValue);
            p.Add(headline);
            p.Add(Form.Label("About you"));
            TextField bio = Form.Field("Who you coach and how", _bio, 2000, true);
            bio.RegisterValueChangedCallback(e => _bio = e.newValue);
            p.Add(bio);
            if (_intro.Length > 0)
            {
                // The intro video comes from the coach application (step 4) and is copied to the profile on approval;
                // POST /lesson/coaching/profile does not take it, so it is shown here, not edited.
                p.Add(Form.Label("Intro video"));
                p.Add(VideoBox.Make(_intro, 200));
                p.Add(Ui.Text("From your coach application. Players see it on your coach page.", "body-2 wrap", Ui.Regular).Margin(8));
            }
            p.Add(Form.Label("Lesson price"));
            TextField price = Form.Field("50", _price, 4);
            price.keyboardType = TouchScreenKeyboardType.NumberPad;
            Label priceNote = Ui.Text("", "body-2 wrap", Ui.Regular);
            void Note() { double x = double.TryParse(_price, out double v) ? v : 50; priceNote.text = "You receive the full $" + x.ToString("0") + " per lesson. Players pay " + LessonInfo.Price(Math.Round(x * 1.10, 2)) + " (members) or " + LessonInfo.Price(Math.Round(x * 1.20, 2)) + " (non-members). Minimum price $50."; }
            price.RegisterValueChangedCallback(e => { _price = System.Text.RegularExpressions.Regex.Replace(e.newValue, "[^0-9]", ""); Note(); });
            Note();
            p.Add(price); p.Add(priceNote);
            p.Add(Form.Label("Turnaround promise"));
            p.Add(Form.Pills(new[] { "24 hrs", "48 hrs", "72 hrs", "1 week" }, Array.IndexOf(Turnarounds, _turnaround), i => _turnaround = Turnarounds[i]));
            p.Add(Ui.Text("The clock starts when a player submits their session. Miss it by 4 days and they get an automatic refund.", "body-2 wrap", Ui.Regular));
            p.Add(Form.Label("Specialties"));
            VisualElement pills = Ui.Box("pills");
            foreach (string k in Keys)
            {
                string key = k;
                VisualElement pill = Ui.Box(_specialties.Contains(k) ? "scope scope-on" : "scope", Ui.Text(LessonInfo.Specialties[k], "scope-text", Ui.Bold));
                pills.Add(pill.OnTap(() => { if (!_specialties.Remove(key)) _specialties.Add(key); pill.EnableInClassList("scope-on", _specialties.Contains(key)); }));
            }
            p.Add(pills);
            p.Add(_payouts.Margin(12));
            p.Add(Ui.Box("card row", Ui.Box("grow set-words", Ui.Text("Accepting new lessons", "body", Ui.Bold), Ui.Text("Pause anytime. Lessons already booked are unaffected.", "body-2 wrap", Ui.Regular)), Rows.Switch(_accepting, on => _accepting = on)).Margin(12));
            _body.Add(p);
            _body.Add(Form.Bottom(Ui.Button(_saving ? "Saving..." : "Save Profile", Save)));
        }

        void Payouts(object s)
        {
            _payouts.Clear();
            bool enabled = J.Flag(s, "payoutsEnabled"), onboarded = J.Flag(s, "onboarded");
            VisualElement card = Ui.Box("card", Ui.Box("row", Ui.Icon(enabled ? "CheckCircle" : "Warning", 22, enabled ? Ui.Bright : Ui.Amber), Ui.Text(enabled ? "Payouts enabled" : onboarded ? "Finish payout setup" : "Set up payouts", "body grow", Ui.Bold).Margin(0)),
                Ui.Text(enabled ? "You get paid through Stripe when each lesson is delivered." : "You can accept lessons now. Connect a bank account with Stripe to receive your earnings, we hold them safely until then.", "body-2 wrap", Ui.Regular).Margin(8));
            object bal = J.Get(s, "balance");
            if (enabled && bal != null)
            {
                string next = Form.When(J.Get(s, "nextPayout"), "arrivalDate") is DateTime t ? "Next payout " + t.ToString("MMM d", CultureInfo.InvariantCulture) : "Paid to your bank automatically";
                card.Add(Ui.Box("set-row", Ui.Box("grow set-words", Ui.Text("ON THE WAY", "caps", Ui.Bold), Ui.Text(LessonInfo.Money(J.Num(bal, "availableCents") + J.Num(bal, "pendingCents")) + " " + J.Str(bal, "currency").ToUpperInvariant(), "num-20 ital", Ui.Bold), Ui.Text(next, "body-2", Ui.Regular))));
            }
            if (!enabled && J.Num(s, "pendingCents") > 0) card.Add(AdminLabels.Pill(LessonInfo.Money(J.Num(s, "pendingCents")) + " waiting for payout", Ui.Amber).Margin(8));
            card.Add(Ui.Button(enabled ? "Open Stripe dashboard" : "Set up with Stripe", () => App.Run(Api.Post(enabled ? "/lesson/coaching/stripe/dashboard" : "/lesson/coaching/stripe/onboard", new Dictionary<string, object>(),
                d => { if (J.Str(d, "url").Length > 0) Application.OpenURL(J.Str(d, "url")); }, e => Dialog.Ask(Root, "Stripe", Form.Why(e, "Could not open Stripe. Try again."), "OK", () => { }))), "btn btn-ghost").Margin(12));
            _payouts.Add(card);
        }

        void Save()
        {
            if (_saving) return;
            int.TryParse(_price, out int price);
            if (_accepting && price <= 0) { Dialog.Ask(Root, "Set a price", "Add your lesson price to accept lessons.", "OK", () => { }); return; }
            if (price > 0 && price < 50) { Dialog.Ask(Root, "Price too low", "The minimum lesson price is $50.", "OK", () => { }); return; }
            _saving = true; Draw();
            App.Run(Api.Post("/lesson/coaching/profile", new Dictionary<string, object> { { "headline", _headline }, { "bio", _bio }, { "lessonPrice", price }, { "turnaroundHours", _turnaround }, { "specialties", string.Join(",", _specialties) }, { "acceptingLessons", _accepting ? 1 : 0 } },
                _ => { _saving = false; _app.Toast("Profile saved."); Draw(); }, e => { _saving = false; Draw(); Dialog.Ask(Root, "Could not save", Form.Why(e, "Try again in a moment."), "OK", () => { }); }));
        }
    }

    // ── Practice plans ───────────────────────────────────────────────────────

    public sealed class AssignPlanScreen : AppScreen
    {
        static readonly string[] Skills = { "All", "Distance", "Speed", "Entry angle", "Pressure", "Custom" };
        readonly App _app;
        readonly string _bookingId, _userId, _planId, _player;
        readonly VisualElement _body, _list;
        readonly List<Dictionary<string, object>> _items = new List<Dictionary<string, object>>();
        List<object> _catalog = new List<object>(), _custom = new List<object>();
        int _weeks = 2, _skill;
        string _search = "", _start;
        bool _saving;

        public AssignPlanScreen(App app, string bookingId, string userId, string playerName, string planId)
        {
            _app = app;
            _bookingId = bookingId; _userId = userId; _planId = planId; _player = playerName;
            Root = Page.Make(app, planId != null ? "Edit Plan" : "Practice Plan", out _body);
            _list = new VisualElement();
            _body.Add(Page.Loading());
            int waiting = planId != null ? 3 : 2;
            void Done() { if (--waiting == 0) Draw(); }
            App.Run(Api.Get("/games/ALL", d => { _catalog = J.List(d).FindAll(g => { string t = J.Str(g, "gameType"); return t != "ASSESSMENT" && t != "OPEN" && !GameBridge.Retired(g); }); Done(); }, _ => Done()));
            App.Run(Api.Get("/creator/games/", d => { _custom = J.List(d); Done(); }, _ => Done()));
            if (planId != null) App.Run(Api.Get("/lesson/plan/" + planId, d =>
            {
                object plan = J.Get(d, "plan");
                _start = J.Str(plan, "startDate");
                if (Form.When(plan, "startDate") is DateTime s && Form.When(plan, "endDate") is DateTime e) { double w = Math.Round((e - s).TotalDays / 7); _weeks = w <= 1 ? 1 : w <= 3 ? 2 : 4; }
                foreach (object it in J.List(J.Get(d, "items"))) _items.Add(new Dictionary<string, object> { { "gameId", J.Get(it, "gameId") }, { "title", J.Str(it, "gameTitle") }, { "gameType", J.Str(it, "gameType") }, { "targetCount", J.Int(it, "targetCount", 3) }, { "note", J.Str(it, "note") } });
                if (_player == null) _app.Toast("Editing " + J.Str(plan, "playerName") + "'s plan");
                Done();
            }, _ => Done()));
        }

        void Draw()
        {
            _body.Clear();
            string first = LessonInfo.First(_player);
            VisualElement p = Ui.Box("pad", Ui.Text(_planId != null ? "for " + first + " · their progress so far is kept" : _userId != null ? "for " + first + " · they get notified right away" : "for " + first + " · attaches to this lesson", "sub wrap", Ui.Regular));
            p.Add(Form.Label("Duration"));
            if (_planId != null && Form.When(new Dictionary<string, object> { { "s", _start } }, "s") is DateTime st) p.Add(Ui.Text("Counted from the plan start, " + st.ToString("MMM d", CultureInfo.InvariantCulture) + ".", "body-2", Ui.Regular));
            p.Add(FreePuttScreen.Choice(new[] { "1 week", "2 weeks", "4 weeks" }, _weeks == 1 ? 0 : _weeks == 2 ? 1 : 2, i => _weeks = i == 0 ? 1 : i == 1 ? 2 : 4));
            if (_items.Count > 0)
            {
                p.Add(Form.Label("Selected · " + _items.Count));
                foreach (Dictionary<string, object> it in _items)
                {
                    Dictionary<string, object> item = it;
                    p.Add(Ui.Box("card", Ui.Box("row", Form.Glyph(PlanDetailScreen.GameIconsFor(J.Str(it, "gameType"))), Ui.Text(J.Str(it, "title"), "body grow", Ui.Bold).Margin(0).OnTap(() => Configure(item, null)),
                        AdminLabels.Pill(J.Int(it, "targetCount") + "x", Ui.Bright), Ui.Icon("Trash", 18, Ui.Faint).OnTap(() => { _items.Remove(item); Draw(); })),
                        J.Str(it, "note").Length > 0 ? Ui.Text("“" + J.Str(it, "note") + "”", "body-2 wrap", Ui.Regular).Margin(8) : null).Margin(8));
                }
            }
            p.Add(Form.Label("Games & drills"));
            TextField search = Form.Field("Search all games and drills...", _search);
            search.RegisterValueChangedCallback(e => { _search = e.newValue; List(); });
            p.Add(search);
            p.Add(Form.Pills(Skills, _skill, i => { _skill = i; List(); }));
            _body.Add(p);
            _body.Add(_list);
            List();
            string verb = _planId != null ? "Save Changes" : _userId != null ? "Assign Plan" : "Attach Plan";
            _body.Add(Form.Bottom(Ui.Button(_saving ? "Saving..." : verb + (_items.Count > 0 ? " · " + _items.Count + " item" + (_items.Count == 1 ? "" : "s") : ""), () => App.Run(Save()))));
        }

        void List()
        {
            _list.Clear();
            bool custom = _skill == Skills.Length - 1;
            string[] skillKeys = { null, "distance", "speed", "angle", "pressure" };
            List<object> games = (custom ? _custom : _catalog).FindAll(g =>
                (_search.Length == 0 || J.Str(g, "title").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (custom || _skill == 0 || J.Str(g, "skills").ToLowerInvariant().Contains(skillKeys[_skill])));
            if (games.Count == 0) { _list.Add(Page.Note(custom ? "You have no custom games yet." : "No games match your search.")); return; }
            VisualElement cols = Ui.Box("cols");
            foreach (object g in games)
            {
                object game = g;
                bool on = _items.Exists(i => J.Str(i, "gameId") == J.Str(g, "id"));
                string kind = J.Str(g, "gameType") == "PRACTICE" || J.Num(g, "multiPlayer") == 0 ? "Practice" : "Game";
                VisualElement tile = Ui.Box(on ? "card row coach-on" : "card row", Form.Glyph(PlanDetailScreen.GameIconsFor(J.Str(g, "gameType"))), Ui.Box("grow set-words", Ui.Text(J.Str(g, "title"), "body", Ui.Bold), Ui.Text(kind + (J.Str(g, "skills").Length > 0 ? " · " + J.Str(g, "skills").Replace(",", ", ") : ""), "body-2", Ui.Regular)), on ? Ui.Icon("CheckCircle", 20, Ui.Bright) : null);
                cols.Add(Ui.Box("col pad", tile.OnTap(() => Configure(_items.Find(i => J.Str(i, "gameId") == J.Str(game, "id")), game))).Margin(8));
            }
            _list.Add(cols);
        }

        void Configure(Dictionary<string, object> item, object game)
        {
            bool existing = item != null;
            int count = existing ? J.Int(item, "targetCount", 3) : 3;
            string note = existing ? J.Str(item, "note") : "";
            VisualElement dim = null;
            TextField cue = Form.Field("Cue for the player (optional), e.g. eyes over the ball", note, 255, true);
            VisualElement card = Ui.Box("modal", Ui.Title(existing ? J.Str(item, "title") : J.Str(game, "title"), "title-20"),
                Ui.Text("Times per week", "body", Ui.Bold).Margin(12), Ui.Text("How often to play it each week", "body-2", Ui.Regular),
                FreePuttScreen.Stepper(() => count.ToString(), d => count = Mathf.Clamp(count + d, 1, 20)).Margin(8), cue.Margin(12),
                Ui.Button(existing ? "Update" : "Add", () =>
                {
                    dim.RemoveFromHierarchy();
                    if (!existing) { item = new Dictionary<string, object> { { "gameId", J.Get(game, "id") }, { "title", J.Str(game, "title") }, { "gameType", J.Str(game, "gameType") } }; _items.Add(item); }
                    item["targetCount"] = count;
                    item["note"] = cue.value.Trim();
                    Draw();
                }),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        IEnumerator Save()
        {
            if (_saving) yield break;
            if (_items.Count == 0) { Dialog.Ask(Root, "Add a game", "Pick at least one game or drill for the plan.", "OK", () => { }); yield break; }
            _saving = true; Draw();
            var items = new List<object>();
            foreach (Dictionary<string, object> i in _items) items.Add(new Dictionary<string, object> { { "gameId", J.Get(i, "gameId") }, { "targetCount", J.Int(i, "targetCount", 3) }, { "note", J.Str(i, "note").Length > 0 ? J.Str(i, "note") : null } });
            var body = new Dictionary<string, object> { { "durationWeeks", _weeks }, { "items", items } };
            if (_planId == null) { if (_bookingId != null) body["bookingId"] = _bookingId; else body["userId"] = _userId; }
            string error = null; bool ok = false;
            yield return Api.Post(_planId != null ? "/lesson/coaching/plan/" + _planId : "/lesson/coaching/plan", body, _ => ok = true, e => error = e ?? "failed");
            _saving = false;
            if (!ok) { Draw(); Dialog.Ask(Root, "Could not save plan", Form.Why(error, "Try again in a moment."), "OK", () => { }); yield break; }
            _app.Pop();
        }
    }

    public sealed class PlayerRosterScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;

        public PlayerRosterScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "My Players", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/lesson/coaching/players", Draw, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load your players.")); }));

        void Draw(object d)
        {
            _body.Clear();
            List<object> players = J.List(J.Get(d, "players")), ended = J.List(J.Get(d, "recentlyEnded"));
            _body.Add(Ui.Box("pad", Ui.Text("Active plans · " + players.Count, "body-2", Ui.Medium)));
            if (players.Count == 0) _body.Add(Ui.Box("pad", Ui.Box("card center-box", Ui.Icon("UsersThree", 30, Ui.Faint), Ui.Text("No active practice plans. Assign a plan when you deliver a lesson.", "sub center wrap", Ui.Regular).Margin(8))));
            VisualElement cols = Ui.Box("cols");
            foreach (object p in players)
            {
                object row = p;
                int done = J.Int(p, "weekDone"), target = J.Int(p, "weekTarget");
                bool idle = J.Get(p, "idleDays") != null && J.Int(p, "idleDays") >= 3;
                Label pill = idle ? AdminLabels.Pill("Idle " + J.Int(p, "idleDays") + "d", Ui.Amber) : done >= target ? AdminLabels.Pill("On track", Ui.Bright) : AdminLabels.Pill("In progress", Ui.Faint);
                VisualElement bar = Ui.Progress(target > 0 ? done / (float)target : 0);
                VisualElement card = Ui.Box("card", Ui.Box("row", Ui.Avatar(J.Str(p, "playerName"), J.Str(p, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(J.Str(p, "playerName"), "body", Ui.Bold),
                    Ui.Text("Week " + J.Int(p, "weekNo") + " of " + J.Int(p, "totalWeeks") + " · " + done + " of " + target + " this week", "body-2", Ui.Regular)), pill), bar.Margin(12));
                cols.Add(Ui.Box("col pad", card.OnTap(() => _app.Push(new PlayerMonitorScreen(_app, J.Str(row, "id"))))).Margin(8));
            }
            _body.Add(cols);
            if (ended.Count > 0)
            {
                _body.Add(Ui.Section("Recently ended"));
                VisualElement card = Ui.Box("coach-list");
                foreach (object e in ended)
                {
                    object row = e;
                    VisualElement r = Ui.Box("set-row", Ui.Avatar(J.Str(e, "playerName"), J.Str(e, "playerPhoto")), Ui.Box("grow set-words", Ui.Text(J.Str(e, "playerName"), "body", Ui.Bold), Ui.Text("Finished " + Form.Date(e, "completedAt"), "body-2", Ui.Regular)),
                        J.Get(e, "rating") != null ? (VisualElement)LessonInfo.StarText(J.Num(e, "rating"), "body-2") : Ui.Icon("CaretRight", 16, Ui.Faint));
                    r.style.opacity = 0.75f;
                    card.Add(r.OnTap(() => _app.Push(new PlayerMonitorScreen(_app, J.Str(row, "id")))));
                }
                _body.Add(Ui.Box("pad", card));
            }
        }
    }

    public sealed class PlayerMonitorScreen : AppScreen
    {
        readonly App _app;
        readonly string _id;
        readonly VisualElement _body;

        public PlayerMonitorScreen(App app, string planId)
        {
            _app = app;
            _id = planId;
            Root = Page.Make(app, "Practice Plan", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/lesson/plan/" + _id, Draw, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load this plan.")); }));

        void Draw(object d)
        {
            _body.Clear();
            object plan = J.Get(d, "plan");
            int status = J.Int(plan, "status");
            string name = J.Str(plan, "playerName"), first = LessonInfo.First(name);
            _body.Add(Ui.Box("pad row", Ui.Avatar(name, J.Str(plan, "playerPhoto")), Ui.Box("grow set-words", Ui.Title(name, "title-20"), Ui.Text(status == 2 ? "Plan finished " + Form.Date(plan, "completedAt") : "Plan ends " + Form.Date(plan, "endDate"), "body-2", Ui.Regular)),
                AdminLabels.Pill(status == 2 ? "Ended" : "Active", status == 2 ? Ui.Faint : Ui.Bright)));
            object before = J.Get(J.Get(d, "progress"), "before"), after = J.Get(J.Get(d, "progress"), "after");
            // One bordered strip, a cell a stat: the number, its caption, and the change under it.
            VisualElement strip = Ui.Box("strip");
            VisualElement tiles = Ui.Box("pad", strip);
            void Tile(string label, double? was, double? now, string value, bool lowerBetter = false)
            {
                if (was == null || now == null) return;
                double change = lowerBetter ? Math.Abs(was.Value) - Math.Abs(now.Value) : now.Value - was.Value;
                Label delta = Ui.Text(Math.Abs(change) < 0.5 ? "" : (change > 0 ? "▲ " : "▼ ") + Math.Abs(change).ToString("0", CultureInfo.InvariantCulture), "body-2", Ui.Bold);
                delta.style.color = change > 0 ? Ui.Bright : Ui.Red;
                strip.Add(Ui.Box(strip.childCount == 0 ? "strip-cell strip-cell-first" : "strip-cell", Ui.Num(value, "strip-num"), Ui.Caps(label, "strip-cap"), delta));
            }
            double? N(object o, string k) => J.Get(o, k) != null ? J.Num(o, k) : (double?)null;
            object Band(object o) => J.List(J.Get(o, "bands")).Find(b => J.Str(b, "label") == "6-8 ft");
            Tile("MAKE %", N(before, "makePct"), N(after, "makePct"), J.Int(after, "makePct") + "%");
            Tile("MAKE 6-8", N(Band(before), "pct"), N(Band(after), "pct"), J.Int(Band(after), "pct") + "%");
            double ro = J.Num(after, "avgRollout");
            Tile("PACE", N(before, "avgRollout"), N(after, "avgRollout"), (ro > 0 ? "+" : "") + ro.ToString("0", CultureInfo.InvariantCulture) + "\"", true);
            if (strip.childCount > 0) _body.Add(tiles);
            VisualElement card = Ui.Box("card", Ui.Text("PLAN PROGRESS", "caps", Ui.Bold));
            foreach (object it in J.List(J.Get(d, "items")))
            {
                int target = J.Int(it, "targetCount"), week = Mathf.Min(J.Int(it, "weekDone"), target);
                Label line = Ui.Text(week + " of " + target + " this week" + (week >= target ? " ✓" : ""), "body-2", Ui.Regular);
                if (week >= target) line.style.color = Ui.Bright;
                card.Add(Ui.Box("", Ui.Box("row", Form.Glyph(PlanDetailScreen.GameIconsFor(J.Str(it, "gameType"))), Ui.Box("grow set-words", Ui.Text(J.Str(it, "gameTitle"), "body", Ui.Bold), line)), Ui.Progress(target > 0 ? week / (float)target : 0).Margin(8)).Margin(12));
            }
            _body.Add(Ui.Box("pad", card));
            if (status != 2)
            {
                VisualElement buttons = Ui.Box("pad");
                if (status == 1) buttons.Add(Ui.Button("Edit plan", () => _app.Push(new AssignPlanScreen(_app, null, null, name, _id)), "btn btn-ghost"));
                buttons.Add(Ui.Button("Send a nudge", () => App.Run(Api.Post("/lesson/coaching/nudge", new Dictionary<string, object> { { "planId", _id } }, r =>
                {
                    if (J.Flag(r, "sent")) Dialog.Ask(Root, "Nudge sent", name + " will get a note from you.", "OK", () => { });
                    else Dialog.Ask(Root, "Already nudged today", "You can send one nudge per player per day.", "OK", () => { });
                }, e => _app.Toast(Form.Why(e, "Could not send the nudge."))))).Margin(8));
                _body.Add(buttons);
            }
            string until = Form.When(plan, "endDate") is DateTime end ? end.AddDays(7).ToString("MMM d", CultureInfo.InvariantCulture) : "";
            _body.Add(Ui.Box("pad", Ui.Text("You can see " + first + "'s plan progress until " + until + ".", "body-2 center wrap", Ui.Regular)));
        }
    }

    // ── Player Review ────────────────────────────────────────────────────────

    public sealed class CoachingDashboardScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body, _list;
        readonly List<object> _players = new List<object>();
        string _search = "";

        public CoachingDashboardScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Player Review", out _body);
            _list = new VisualElement();
            _body.Add(Page.Loading());
            int waiting = 2;
            var seen = new HashSet<string>();
            void Add(List<object> rows) { foreach (object r in rows) if (seen.Add(J.Str(r, "userId"))) _players.Add(r); }
            void Done()
            {
                if (--waiting > 0) return;
                _players.Sort((a, b) => string.Compare(J.Str(a, "displayName"), J.Str(b, "displayName"), StringComparison.OrdinalIgnoreCase));
                if (app.Profile != null && seen.Add(J.Str(app.Profile, "userId"))) _players.Insert(0, app.Profile);
                Draw();
            }
            App.Run(Api.Get("/profile/followers/", d => { Add(J.List(d)); Done(); }, _ => Done()));
            App.Run(Api.Get("/profile/following/", d => { Add(J.List(d)); Done(); }, _ => Done()));
        }

        void Draw()
        {
            _body.Clear();
            _body.Add(Ui.Box("pad row", Ui.Box("grow", Ui.Title("Select Player", "title-20"), Ui.Text(_players.Count + " player" + (_players.Count == 1 ? "" : "s"), "body-2", Ui.Regular)),
                Form.Mini("Find player", () => _app.Push(new FindPlayersScreen(_app)))));
            if (_players.Count > 5)
            {
                TextField search = Form.Field("Search players...", _search);
                search.RegisterValueChangedCallback(e => { _search = e.newValue; List(); });
                _body.Add(Ui.Box("pad", search));
            }
            _body.Add(_list);
            List();
            VisualElement invite = Ui.Box("card", Ui.Text("Player not on Puttr?", "body", Ui.Bold), Ui.Text("Have them scan this to get the app and connect with you. They will show up in this list.", "body-2 wrap", Ui.Regular).Margin(8));
            _body.Add(Ui.Box("pad", invite).Margin(12));
            App.Run(Api.Get("/coach/invite", d =>
            {
                string link = J.Str(d, "link");
                if (link.Length == 0) return;
                VisualElement qr = Ui.Box("thumb");
                qr.style.width = qr.style.height = 144;
                Images.Load("https://api.qrserver.com/v1/create-qr-code/?size=280x280&data=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(link), t => { if (t != null) qr.style.backgroundImage = new StyleBackground(t); });
                invite.Add(qr.Margin(12));
                invite.Add(Ui.Button("Copy invite link", () => { GUIUtility.systemCopyBuffer = link; _app.Toast("Copied to clipboard"); }, "btn btn-ghost").Margin(12));
            }, _ => { }));
        }

        void List()
        {
            _list.Clear();
            string me = J.Str(_app.Profile, "userId");
            VisualElement card = Ui.Box("coach-list");
            int n = 0;
            foreach (object p in _players)
            {
                if (_search.Length > 0 && J.Str(p, "displayName").IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0 && J.Str(p, "homeTown").IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                object player = p;
                n++;
                string hcp = J.Str(p, "realHandicap", J.Str(p, "handicap"));
                if (hcp.StartsWith("-")) hcp = "+" + hcp.Substring(1);
                card.Add(Ui.Box("set-row", Ui.Avatar(J.Str(p, "displayName"), J.Str(p, "photoURL")), Ui.Box("grow set-words", Ui.Box("row", Ui.Text(J.Str(p, "displayName"), "body", Ui.Bold), J.Str(p, "userId") == me ? AdminLabels.Pill("YOU", Ui.Bright) : null), Ui.Text(J.Str(p, "homeTown"), "body-2", Ui.Regular)),
                    hcp.Length > 0 ? Ui.Box("", Ui.Text(hcp, "body center", Ui.Bold), Ui.Text("HCP", "caps center", Ui.Bold)) : null).OnTap(() => _app.Push(new CoachPlayerScreen(_app, player))));
            }
            _list.Add(n == 0 ? Page.Note(_search.Length > 0 ? "No players match your search" : "No players found") : Ui.Box("pad", card));
        }
    }

    /// <summary>UserView: a Player's goals and notes, their plan, their sessions.</summary>
    public sealed class CoachPlayerScreen : AppScreen
    {
        readonly App _app;
        readonly object _player;
        readonly VisualElement _body;
        object _meta, _plan;
        object _current;   // the session the coach last opened: a take from REC here becomes its studio lesson
        List<object> _sessions;

        public CoachPlayerScreen(App app, object player)
        {
            _app = app;
            _player = player;
            Root = Page.Make(app, J.Str(player, "displayName"), out _body);
        }

        bool Self => J.Str(_player, "userId") == J.Str(_app.Profile, "userId");

        public override void Shown()
        {
            string id = J.Str(_player, "userId");
            int waiting = Self ? 1 : 3;
            void Done() { if (--waiting == 0) Draw(); }
            if (!Self)
            {
                App.Run(Api.Get("/coaching/playerMeta/" + id, d => { _meta = d; Done(); }, _ => Done()));
                App.Run(Api.Get("/lesson/coaching/players", d => { _plan = J.List(J.Get(d, "players")).Find(p => J.Str(p, "userId") == id); Done(); }, _ => Done()));
            }
            App.Run(Api.Get("/coaching/user/sessions/" + id, d => { _sessions = J.List(d); Done(); }, _ => { _sessions = new List<object>(); Done(); }));
        }

        void Draw()
        {
            _body.Clear();
            object p = _player;
            string first = LessonInfo.First(J.Str(p, "displayName"));
            _body.Add(Ui.Box("pad row", Ui.Avatar(J.Str(p, "displayName"), J.Str(p, "photoURL"), true), Ui.Box("grow set-words", Ui.Title(J.Str(p, "displayName"), "title-20"), Ui.Text(J.Str(p, "homeTown"), "body-2", Ui.Regular)),
                Recorder.Pill(Root, new Recorder.Context { Player = p, Session = () => _current ?? (_sessions != null && _sessions.Count > 0 ? _sessions[0] : null) })));
            if (!Self)
            {
                VisualElement tools = Ui.Box("pad row");
                if (_plan != null) tools.Add(Ui.Button("Practice Plan", () => _app.Push(new PlayerMonitorScreen(_app, J.Str(_plan, "id"))), "btn btn-ghost grow"));
                else tools.Add(Ui.Button("Assign Plan", () => _app.Push(new AssignPlanScreen(_app, null, J.Str(p, "userId"), J.Str(p, "displayName"), null)), "btn btn-ghost grow"));
                bool empty = J.Str(_meta, "goals").Length == 0 && J.Str(_meta, "notes").Length == 0;
                VisualElement notes = Ui.Button(empty ? "Add Notes" : "Goals & Notes", Meta, "btn btn-ghost grow");
                notes.style.marginLeft = 8;
                tools.Add(notes);
                _body.Add(tools);
                if (_plan != null) _body.Add(Ui.Box("pad", Ui.Text("Week " + J.Int(_plan, "weekNo") + " of " + J.Int(_plan, "totalWeeks") + " · " + J.Int(_plan, "weekDone") + " of " + J.Int(_plan, "weekTarget") + " sessions this week", "body-2", Ui.Regular)));
            }
            _body.Add(Ui.Section("Sessions"));
            if (_sessions.Count == 0) _body.Add(Page.Note("No recorded sessions yet"));
            VisualElement cols = Ui.Box("cols");
            foreach (object s in _sessions)
            {
                object session = s;
                object check = J.Parse(J.Str(s, "captureCheck"));
                object halves = J.Get(check, "halves");
                VisualElement views = halves != null ? Ui.Box("row", AdminLabels.Pill("DTL", J.Str(halves, "first") == "downTheLine" ? Ui.Bright : Ui.Faint), AdminLabels.Pill("FACE ON", J.Str(halves, "second") == "faceOn" ? Ui.Bright : Ui.Faint)) : null;
                cols.Add(Ui.Box("col pad", Ui.Box("card", LessonInfo.SessionRow(s, AdminLabels.Pill(Coach.Grade(J.Num(s, "score")) + " · " + J.Int(s, "score"), Ui.Bright)), views)
                    .OnTap(() => { _current = session; object who = _player; CoachSessionScreen.HeaderExtra = () => Recorder.Pill(_app.Toasts, new Recorder.Context { Player = who, Session = () => session }); _app.Push(LabSessionScreen.ForCoach(_app, J.Str(session, "id"), J.Str(_player, "displayName"))); })).Margin(8));
            }
            _body.Add(cols);
            if (!Self) _body.Add(Ui.Box("pad", Ui.Button("Deliver a session review as a lesson", () => StudioLesson(), "btn btn-ghost")).Margin(12));
        }

        // A studio lesson: POST /lesson/coaching/studio/start with the newest session, then Deliver.
        void StudioLesson()
        {
            if (_sessions.Count == 0) { _app.Toast("They need a recorded session first."); return; }
            App.Run(Api.Post("/lesson/coaching/studio/start", new Dictionary<string, object> { { "userId", J.Str(_player, "userId") }, { "sessionType", "LAB" }, { "sessionId", J.Get(_sessions[0], "id") } },
                d => { object b = J.Get(d, "booking"); App.Run(Api.Get("/lesson/booking/" + J.Str(b, "id"), full => _app.Push(new LessonDeliverScreen(_app, full)), _ => _app.Push(new LessonDeliverScreen(_app, b)))); },
                e => _app.Toast(Form.Why(e, "Could not start the lesson."))));
        }

        void Meta()
        {
            VisualElement dim = null;
            TextField goals = Form.Field("What they're working toward, e.g. stop leaving putts short", J.Str(_meta, "goals"), 2000, true);
            TextField notes = Form.Field("Lesson history, tendencies, what to check next time...", J.Str(_meta, "notes"), 4000, true);
            VisualElement card = Ui.Box("modal", Ui.Title(LessonInfo.First(J.Str(_player, "displayName")), "title-20"),
                Form.Label("Goals · shown on their review"), goals, Form.Label("Private notes · only you see these"), notes,
                Ui.Button("Save", () =>
                {
                    dim.RemoveFromHierarchy();
                    string g = goals.value.Trim(), n = notes.value.Trim();
                    App.Run(Api.Post("/coaching/playerMeta", new Dictionary<string, object> { { "userId", J.Str(_player, "userId") }, { "goals", g.Length > 0 ? g : null }, { "notes", n.Length > 0 ? n : null } }, d => { _meta = d; Draw(); }, e => _app.Toast(Form.Why(e, "Could not save."))));
                }),
                Ui.Button("Cancel", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card);
            App.Layer(Root).Add(dim);
        }
    }

    // ── The coach's videos ───────────────────────────────────────────────────

    public sealed class CoachingVideosScreen : AppScreen
    {
        readonly App _app;
        readonly VisualElement _body;
        List<object> _videos = new List<object>();
        string _search = "";
        float _progress = -1;

        public CoachingVideosScreen(App app)
        {
            _app = app;
            Root = Page.Make(app, "Videos", out _body);
            _body.Add(Page.Loading());
        }

        public override void Shown() => App.Run(Api.Get("/coaching/videos/", d => { _videos = J.List(d); Draw(); }, _ => { _body.Clear(); _body.Add(Page.Note("Couldn't load your videos.")); }));

        void Draw()
        {
            _body.Clear();
            _body.Add(Ui.Box("pad row", Ui.Box("grow"), Form.Mini(_progress >= 0 ? Mathf.RoundToInt(_progress * 100) + "%" : "Upload", Upload)));
            if (_videos.Count >= 5)
            {
                TextField search = Form.Field("Search videos...", _search);
                search.RegisterValueChangedCallback(e => { _search = e.newValue; Draw(); search.Focus(); });
                _body.Add(Ui.Box("pad", search));
            }
            List<object> shown = _videos.FindAll(v => _search.Length == 0 || J.Str(v, "videoTitle").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 || J.Str(v, "displayName").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            List<object> library = shown.FindAll(v => J.Int(v, "library") == 1), players = shown.FindAll(v => J.Int(v, "library") != 1);
            _body.Add(Ui.Section("Video Library · " + library.Count));
            if (library.Count == 0) _body.Add(Page.Note("No library videos yet"));
            else _body.Add(List(library));
            _body.Add(Ui.Section("Player Reviews · " + players.Count));
            if (players.Count == 0) { _body.Add(Page.Note("No player review videos yet")); return; }
            var groups = new Dictionary<string, List<object>>();
            var order = new List<string>();
            foreach (object v in players) { string k = J.Str(v, "userId"); if (!groups.ContainsKey(k)) { groups[k] = new List<object>(); order.Add(k); } groups[k].Add(v); }
            foreach (string k in order)
            {
                object first = groups[k][0];
                _body.Add(Ui.Box("pad row", Ui.Avatar(J.Str(first, "displayName"), J.Str(first, "photoURL")), Ui.Text(J.Str(first, "displayName"), "body grow", Ui.Bold).Margin(0), AdminLabels.Pill(groups[k].Count.ToString(), Ui.Faint)));
                _body.Add(List(groups[k]));
            }
        }

        VisualElement List(List<object> videos)
        {
            VisualElement cols = Ui.Box("cols");
            foreach (object v in videos)
            {
                object video = v;
                VisualElement thumb = Ui.Box("thumb");
                if (J.Str(v, "thumbnail").Length > 0) Images.Load(J.Str(v, "thumbnail"), t => { if (t != null) { thumb.style.backgroundImage = new StyleBackground(t); thumb.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop; } });
                cols.Add(Ui.Box("col pad", Ui.Box("card row", thumb, Ui.Box("grow set-words", Ui.Text(J.Str(v, "videoTitle").Length > 0 ? J.Str(v, "videoTitle") : "Untitled", "body", Ui.Bold), Ui.Text(Form.Duration(J.Num(v, "videoLength")) + " · " + Form.Ago(v, "created_at"), "body-2", Ui.Regular)), Ui.Icon("CaretRight", 16, Ui.Faint))
                    .OnTap(() => Preview(video))).Margin(8));
            }
            return cols;
        }

        void Preview(object v)
        {
            VisualElement dim = null;
            VisualElement card = Ui.Box("modal", VideoBox.Make(J.Str(v, "publicUrl"), 260), Ui.Title(J.Str(v, "videoTitle").Length > 0 ? J.Str(v, "videoTitle") : "Untitled", "title-20").Margin(12),
                Ui.Text(Form.Duration(J.Num(v, "videoLength")) + " · " + Form.Ago(v, "created_at"), "body-2", Ui.Regular),
                Ui.Button("Delete", () => Dialog.Ask(Root, "Delete Video", "Are you sure you want to delete this video?", "Delete", () =>
                {
                    dim.RemoveFromHierarchy();
                    App.Run(Api.Post("/coaching/deleteVideo", new Dictionary<string, object> { { "videoId", J.Get(v, "id") } }, _ => Shown(), e => _app.Toast(Form.Why(e, "Could not delete."))));
                }, true), "btn btn-red").Margin(12),
                Ui.Button("Close", () => dim.RemoveFromHierarchy(), "btn btn-ghost"));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(() => dim.RemoveFromHierarchy());
            App.Layer(Root).Add(dim);
        }

        // The upload wizard: Player Feedback (pick a Player) or Video Library, then a video from the camera roll, then a title.
        void Upload()
        {
            if (_progress >= 0) return;
            VisualElement dim = null;
            void Close() => dim?.RemoveFromHierarchy();
            VisualElement card = Ui.Box("modal", Ui.Title("Upload Video", "title-20"), Ui.Text("What type of video are you uploading?", "sub", Ui.Regular).Margin(8));
            void Pick(object player)
            {
                Close();
                string date = DateTime.Now.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);
                string title = player != null ? "Player feedback - " + date : "Coach video - " + date;
                PhotoPicker.PickVideo(path =>
                {
                    if (path == null) return;
                    App.Run(Uploads.Length(path, seconds =>
                    {
                        _progress = 0; Draw();
                        string file = "coaching_vid_" + UnityEngine.Random.Range(0, 100000000) + ".mp4";
                        App.Run(Uploads.Video(path, file, f => { _progress = f; Draw(); }, url =>
                        {
                            if (url == null) { _progress = -1; Draw(); Dialog.Ask(Root, "Upload Failed", "Could not upload your video. Try again.", "OK", () => { }); return; }
                            var save = new Dictionary<string, object> { { "fileName", file }, { "coachId", J.Str(_app.Profile, "userId", Auth.Uid) }, { "videoLength", Math.Round(Math.Max(0, seconds)) }, { "videoTitle", title }, { "videoWidth", 1080 }, { "videoHeight", 1920 } };
                            if (player != null) save["userId"] = J.Str(player, "userId"); else save["isLibrary"] = 1;
                            App.Run(Api.Post("/coaching/saveVideo", save, _ => { _progress = -1; _app.Toast(player != null ? "Sent to " + LessonInfo.First(J.Str(player, "displayName")) + "." : "Saved to your library."); Shown(); }, e => { _progress = -1; Draw(); _app.Toast(Form.Why(e, "Could not save the video.")); }));
                        }));
                    }));
                });
            }
            card.Add(Ui.Box("card row", Ui.Icon("User", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Player Feedback", "body", Ui.Bold), Ui.Text("Review video for a specific player", "body-2", Ui.Regular))).Margin(12).OnTap(() =>
            {
                Close();
                App.Run(Api.Get("/profile/followers/", d =>
                {
                    VisualElement pick = null;
                    VisualElement list = Ui.Box("modal", Ui.Title("Which player?", "title-20"));
                    ScrollView scroll = Ui.Scroll();
                    scroll.style.maxHeight = 420;
                    foreach (object p in J.List(d)) { object player = p; scroll.Add(Ui.Box("set-row", Ui.Avatar(J.Str(p, "displayName"), J.Str(p, "photoURL")), Ui.Text(J.Str(p, "displayName"), "body grow", Ui.Bold)).OnTap(() => { pick.RemoveFromHierarchy(); Pick(player); })); }
                    list.Add(scroll);
                    list.Add(Ui.Button("Cancel", () => pick.RemoveFromHierarchy(), "btn btn-ghost"));
                    list.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                    pick = Ui.Box("modal-dim", list);
                    App.Layer(Root).Add(pick);
                }, _ => _app.Toast("Couldn't load your players.")));
            }));
            card.Add(Ui.Box("card row", Ui.Icon("FilmReel", 22, Ui.Bright), Ui.Box("grow set-words", Ui.Text("Video Library", "body", Ui.Bold), Ui.Text("Drills, techniques, or instructional content", "body-2", Ui.Regular))).Margin(12).OnTap(() => Pick(null)));
            card.Add(Ui.Button("Cancel", Close, "btn btn-ghost").Margin(12));
            card.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            dim = Ui.Box("modal-dim", card).OnBackdropTap(Close);
            App.Layer(Root).Add(dim);
        }
    }
}
