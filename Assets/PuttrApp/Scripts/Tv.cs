// The app on a TV (Apple TV, Google TV / Android TV, Samsung via the web build): QR sign-in as puttr-tv, then the Play
// tab, driven by the remote. Tournaments and League show their boards with no play buttons (a TV has no camera to
// record a round). The TV looks for the Puttr green over Bluetooth first, then on the wifi.
//
// Remote: every tappable element (Ui.OnTap / Ui.Button) is focusable on a TV; the d-pad moves focus to the nearest
// element that way (spatial, not tab order), OK taps it, Back goes back. -app2Tv lays a desktop window out as a TV.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Tv
    {
        /// <summary>This is a TV.</summary>
        public static bool On { get; private set; }
        /// <summary>"tvos", "androidtv" or "web" on a TV, else "".</summary>
        public static string Platform { get; private set; } = "";

        /// <summary>Log what the remote sends and what is made of it ("[remote]" lines): debug builds, or -app2TvTrace.</summary>
        public static bool Trace { get; private set; }
        /// <summary>
        /// True while the platform's own remote reader has a swipe in hand (Apple TV: TvPlatform). The EventSystem reads
        /// the same swipe its own way, early and sometimes to the side, so its moves are not taken meanwhile.
        /// </summary>
        public static Func<bool> RemoteBusy;

        static readonly ConditionalWeakTable<VisualElement, Action> Taps = new ConditionalWeakTable<VisualElement, Action>();
        static VisualElement _stage;
        static readonly Queue<(float at, string key)> Script = new Queue<(float, string)>();

        /// <summary>Called first thing in App.Awake.</summary>
        public static void Detect(IList<string> args)
        {
#if UNITY_TVOS
            On = true; Platform = "tvos";
#elif UNITY_WEBGL && !UNITY_EDITOR
            On = true; Platform = "web";   // the web build is the Samsung (Tizen) TV app
#elif UNITY_ANDROID && !UNITY_EDITOR
            if (AndroidTv()) { On = true; Platform = "androidtv"; }
#endif
            Trace = Debug.isDebugBuild;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "-app2TvTrace") Trace = true;
                if (args[i] == "-app2Tv") { On = true; if (Platform == "") Platform = "tvos"; }
                // -app2TvKeys "7:down,down,ok": remote presses from 7 s in, one a second (for checking from a script).
                if (args[i] == "-app2TvKeys" && i + 1 < args.Count)
                {
                    string spec = args[i + 1];
                    float at = 7f;
                    int colon = spec.IndexOf(':');
                    if (colon > 0) { float.TryParse(spec.Substring(0, colon), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out at); spec = spec.Substring(colon + 1); }
                    foreach (string key in spec.Split(',')) { Script.Enqueue((at, key.Trim())); at += 1f; }
                    _scripted = true;
                }
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool AndroidTv()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    if (pm.Call<bool>("hasSystemFeature", "android.software.leanback")) return true;
                    using (AndroidJavaObject ui = activity.Call<AndroidJavaObject>("getSystemService", "uimode"))
                        return ui != null && ui.Call<int>("getCurrentModeType") == 4;   // UI_MODE_TYPE_TELEVISION
                }
            }
            catch (Exception) { return false; }
        }
#endif

        /// <summary>Ui.OnTap / Ui.Button: on a TV the element can take focus, and OK runs its tap.</summary>
        public static void Tappable(VisualElement element, Action tap)
        {
            if (!On) return;
            element.focusable = true;
            element.AddToClassList("tv-focus");
            Taps.AddOrUpdate(element, tap);
        }

        // ── Moving focus with the remote ─────────────────────────────────────

        public static void Attach(VisualElement stage)
        {
            if (!On) return;
            _stage = stage;
            stage.AddToClassList("tv");
            // The EventSystem's own navigation stays on (whatever its Navigate action reads: a gamepad, a joystick, arrow
            // keys, the Siri Remote) next to the remote reader in TvPlatform; one gesture can arrive both ways, so a
            // press the other way just took is not taken again (Taken), and no move is taken from here while the reader has
            // a swipe in hand (RemoteBusy): this route calls a down swipe that drifts "right" as well.
            stage.RegisterCallback<NavigationMoveEvent>(e =>
            {
                string key = KeyOf(e.direction);
                if (key == null) return;
                e.StopImmediatePropagation(); stage.focusController?.IgnoreEvent(e);
                if (RemoteBusy != null && RemoteBusy())
                {
                    if (Trace) Debug.Log("[remote] nav " + key + " not taken (swipe in hand)");
                    return;
                }
                if (Taken(key, fromNav: true)) return;
                _return = null;
                Move(e.direction);
            }, TrickleDown.TrickleDown);
            stage.RegisterCallback<NavigationSubmitEvent>(e =>
            {
                e.StopImmediatePropagation(); stage.focusController?.IgnoreEvent(e);
                if (!Taken("ok", fromNav: true)) { _return = null; Submit(); }
            }, TrickleDown.TrickleDown);
            stage.RegisterCallback<NavigationCancelEvent>(e =>
            {
                e.StopImmediatePropagation(); stage.focusController?.IgnoreEvent(e);
                if (!Taken("back", fromNav: true)) { _return = null; Back(); }
            }, TrickleDown.TrickleDown);
        }

        static string KeyOf(NavigationMoveEvent.Direction d) => d switch
        {
            NavigationMoveEvent.Direction.Up => "up",
            NavigationMoveEvent.Direction.Down => "down",
            NavigationMoveEvent.Direction.Left => "left",
            NavigationMoveEvent.Direction.Right => "right",
            _ => null,
        };

        // The last press from each route: the remote reader (Press) and the EventSystem's navigation events.
        static string _pressKey, _navKey;
        static float _pressAt = -9f, _navAt = -9f;

        /// <summary>
        /// True when the same press just came the other way (within a fifth of a second), so it is not acted on twice.
        /// Otherwise records it for this route.
        /// </summary>
        static bool Taken(string key, bool fromNav)
        {
            if (_keyboard != null) return true;   // the TV's keyboard has the remote
            float now = Time.unscaledTime;
            if (fromNav)
            {
                if (key == _pressKey && now - _pressAt < 0.2f) return true;
                _navKey = key; _navAt = now;
            }
            else
            {
                if (key == _navKey && now - _navAt < 0.2f) return true;
                _pressKey = key; _pressAt = now;
            }
            return false;
        }

        static float _lastFix;

        // ── Back to where the Player was ─────────────────────────────────────
        // A screen left for one on top of it remembers what had the focus; coming back, the focus returns there. The
        // screen may redraw as it comes back (its rows are new elements, some after a load), so the place is also kept
        // as what the target said and where it was, and looked for again for a few seconds, until the remote is used.

        sealed class Place { public VisualElement Element; public string Name; public Vector2 At; }
        static readonly ConditionalWeakTable<AppScreen, Place> Left = new ConditionalWeakTable<AppScreen, Place>();
        static Place _return;
        static VisualElement _returnRoot;
        static float _returnUntil;

        /// <summary>App.Swap: `from` is going off the screen and `to` is coming on.</summary>
        public static void Swapped(AppScreen from, AppScreen to)
        {
            if (!On || _stage == null || from == to) return;
            _return = null;
            _fresh = true;
            if (from?.Root != null && _stage.focusController?.focusedElement is VisualElement f && from.Root.Contains(f))
            {
                Left.Remove(from);
                Left.Add(from, new Place { Element = f, Name = Name(f), At = f.worldBound.center });
            }
            if (to == null || !Left.TryGetValue(to, out Place place)) return;
            Left.Remove(to);
            _return = place;
            _returnRoot = to.Root;
            _returnUntil = Time.realtimeSinceStartup + 4f;
        }

        static void Return()
        {
            if (_return == null) return;
            if (Time.realtimeSinceStartup > _returnUntil || _returnRoot?.panel == null) { _return = null; return; }
            VisualElement target = _return.Element;
            if (target.panel == null || !_returnRoot.Contains(target) || !Shown(target))
            {
                // Redrawn: the target that says the same, nearest to where it was.
                target = null;
                float best = float.MaxValue;
                foreach (VisualElement c in Candidates())
                {
                    if (!_returnRoot.Contains(c) || Name(c) != _return.Name) continue;
                    float d = (c.worldBound.center - _return.At).sqrMagnitude;
                    if (d < best) { best = d; target = c; }
                }
                if (target == null) return;
                _return.Element = target;
            }
            if (_stage.focusController.focusedElement != target) Focus(target);
        }

        /// <summary>Every frame (App.Update): keep something focused, and play scripted presses.</summary>
        public static void Tick()
        {
            if (!On || _stage?.panel == null) return;
            while (Script.Count > 0 && Time.realtimeSinceStartup >= Script.Peek().at) Press(Script.Dequeue().key);
            Typing();
            Return();
            if (Time.realtimeSinceStartup - _lastFix < 0.25f) return;
            _lastFix = Time.realtimeSinceStartup;
            var current = _stage.focusController.focusedElement as VisualElement;
            List<VisualElement> all = Candidates();
            if (all.Count == 0) return;
            if (current == null || !all.Contains(current)) Focus(Recover(all));
            else Note(current);
        }

        /// <summary>A remote press by name: up, down, left, right, ok, back.</summary>
        public static void Press(string key)
        {
            if (Taken(key, fromNav: false)) return;
            _return = null;
            switch (key)
            {
                case "up": Move(NavigationMoveEvent.Direction.Up); break;
                case "down": Move(NavigationMoveEvent.Direction.Down); break;
                case "left": Move(NavigationMoveEvent.Direction.Left); break;
                case "right": Move(NavigationMoveEvent.Direction.Right); break;
                case "ok": Submit(); break;
                case "back": Back(); break;
            }
        }

        /// <summary>Back on the remote: close the top sheet, else the screen on top, else the Play tab's first tab.</summary>
        public static void Back()
        {
            Perf.Wake();
            VisualElement modal = TopModal();
            if (modal != null) { modal.RemoveFromHierarchy(); return; }
            App.I?.TvBack();
        }

        static bool Submit()
        {
            Perf.Wake();
            var current = _stage?.focusController.focusedElement as VisualElement;
            for (VisualElement e = current; e != null; e = e.parent)
                if (Taps.TryGetValue(e, out Action tap)) { tap?.Invoke(); return true; }
            return false;
        }

        static bool Move(NavigationMoveEvent.Direction direction)
        {
            Perf.Wake();
            if (direction == NavigationMoveEvent.Direction.None || direction == NavigationMoveEvent.Direction.Next || direction == NavigationMoveEvent.Direction.Previous) return false;
            List<VisualElement> all = Candidates();
            if (all.Count == 0) return true;
            var current = _stage.focusController.focusedElement as VisualElement;
            if (current == null || !all.Contains(current))
            {
                if (Logging) Debug.LogWarning("[tv] move " + direction + ": focus is on " + Name(current) + " (" + current?.GetType().Name + "), not a remote target; back to " + Name(Recover(all)));
                Focus(Recover(all));
                return true;
            }
            // The scrollers the focus sits in that scroll this way, innermost first: a list is walked to its end
            // before the move leaves it (Up through the cards reaches the menu only from the top one).
            bool vertical = direction == NavigationMoveEvent.Direction.Up || direction == NavigationMoveEvent.Direction.Down;
            var scopes = new List<VisualElement>();
            for (VisualElement p = current.parent; p != null; p = p.parent)
                if (p is ScrollView sv && (sv.mode == ScrollViewMode.VerticalAndHorizontal || (sv.mode == ScrollViewMode.Vertical) == vertical)) scopes.Add(sv);
            Rect from = current.worldBound;
            var rects = new List<Rect>(all.Count);
            var ranks = new List<int>(all.Count);
            foreach (VisualElement c in all)
            {
                rects.Add(c == current ? Rect.zero : c.worldBound);
                int rank = 0;
                while (rank < scopes.Count && !scopes[rank].Contains(c)) rank++;
                ranks.Add(rank);
            }
            int pick = Pick(from, rects, ranks, direction);
            VisualElement best = pick < 0 || all[pick] == current ? null : all[pick];
            // A warning, not a log: release builds print warnings to the console, which is what a scripted check reads.
            if (Logging) Debug.LogWarning("[tv] move " + direction + " from " + Name(current) + " " + from + " to " + (best != null ? Name(best) + " " + best.worldBound : "nothing") + " of " + all.Count);
            if (best != null) Focus(best);
            return true;
        }

        /// <summary>
        /// Which of these the focus moves to from `from`, or -1: the nearest that way, among those of the lowest rank
        /// that has any (rank 0 shares the innermost scroller with the focus, higher ranks are further out).
        /// </summary>
        public static int Pick(Rect from, IList<Rect> rects, IList<int> ranks, NavigationMoveEvent.Direction direction)
        {
            int best = -1, bestRank = int.MaxValue;
            float bestScore = float.MaxValue;
            for (int i = 0; i < rects.Count; i++)
            {
                Rect to = rects[i];
                if (to.width <= 0f || to.height <= 0f) continue;
                float primary, across;
                switch (direction)
                {
                    case NavigationMoveEvent.Direction.Right: primary = to.center.x - from.center.x; across = Gap(from.yMin, from.yMax, to.yMin, to.yMax); if (to.xMin < from.xMax - from.width * 0.5f) continue; break;
                    case NavigationMoveEvent.Direction.Left: primary = from.center.x - to.center.x; across = Gap(from.yMin, from.yMax, to.yMin, to.yMax); if (to.xMax > from.xMin + from.width * 0.5f) continue; break;
                    case NavigationMoveEvent.Direction.Down: primary = to.center.y - from.center.y; across = Gap(from.xMin, from.xMax, to.xMin, to.xMax); if (to.yMin < from.yMax - from.height * 0.5f) continue; break;
                    default: primary = from.center.y - to.center.y; across = Gap(from.xMin, from.xMax, to.xMin, to.xMax); if (to.yMax > from.yMin + from.height * 0.5f) continue; break;
                }
                if (primary <= 0f) continue;
                // Straight along the line beats nearer but off to the side.
                // Anything in line (overlapping across) beats anything off to the side, however near.
                float score = primary + across * 3f + (across > 0f ? 100000f : 0f);
                int rank = ranks != null ? ranks[i] : 0;
                if (rank < bestRank || (rank == bestRank && score < bestScore)) { bestRank = rank; bestScore = score; best = i; }
            }
            return best;
        }

        // How far apart two ranges are (0 when they overlap).
        static float Gap(float a0, float a1, float b0, float b1) => b1 < a0 ? a0 - b1 : b0 > a1 ? b0 - a1 : 0f;

        static bool Logging => Script.Count > 0 || _scripted;
        static bool _scripted;
        static string Name(VisualElement e) => e == null ? "null" : (e.Q<Label>()?.text ?? e.name ?? e.GetType().Name);

        /// <summary>Puts the remote's focus here (a screen that redraws keeps the Player where they were).</summary>
        public static void FocusOn(VisualElement e) { if (On && e != null) Focus(e); }

        static void Focus(VisualElement e)
        {
            e.Focus();
            Note(e);
            // Bring it into view in every scroller it sits in (a rail across, the page down).
            for (VisualElement p = e.parent; p != null; p = p.parent)
                if (p is ScrollView sv) sv.ScrollTo(e);
        }

        // Where focus starts on a screen that has none (just shown, or back from a screen on top): the tab that is
        // showing, so the lit tab and the focused tab are the same one; else the first target.
        static VisualElement Start(List<VisualElement> all)
        {
            foreach (VisualElement e in all) if (e.ClassListContains("play-tab-on")) return e;
            return all[0];
        }

        // Where the focus last was, on the screen and in the sheet over it. A screen that redraws under the focus (Go
        // first, a level chip, a row added) puts new elements where the old ones were: the focus goes to the nearest
        // of them, not back to the start. A new screen, or a sheet just opened, starts at Start.
        static Vector2 _stageAt, _modalAt;
        static bool _hasStageAt, _hasModalAt, _fresh = true;

        static void Note(VisualElement e)
        {
            Vector2 at = e.worldBound.center;
            if (float.IsNaN(at.x) || float.IsNaN(at.y)) return;
            if (TopModal() != null) { _modalAt = at; _hasModalAt = true; }
            else { _stageAt = at; _hasStageAt = true; _hasModalAt = false; }
            _fresh = false;
        }

        static VisualElement Recover(List<VisualElement> all)
        {
            bool modal = TopModal() != null;
            if (_fresh || !(modal ? _hasModalAt : _hasStageAt)) return Start(all);
            return Nearest(modal ? _modalAt : _stageAt, all.ConvertAll(e => e.worldBound)) is int i && i >= 0 ? all[i] : Start(all);
        }

        /// <summary>Which of these is nearest to a point (the one it is in, if any), or -1.</summary>
        public static int Nearest(Vector2 at, IList<Rect> rects)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < rects.Count; i++)
            {
                Rect r = rects[i];
                if (float.IsNaN(r.x) || r.width <= 0f || r.height <= 0f) continue;
                float dx = Mathf.Max(r.xMin - at.x, 0f, at.x - r.xMax), dy = Mathf.Max(r.yMin - at.y, 0f, at.y - r.yMax);
                float d = dx * dx + dy * dy + (r.center - at).sqrMagnitude * 0.0001f;   // inside two: the one centred nearer
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // ── Typing ───────────────────────────────────────────────────────────
        // A text field is no remote target (its focus is the caret's, and a TV has no keys). Where a TV needs a few
        // typed words (a guest's name), TextEntry shows them in a box the remote can reach; OK opens the TV's own keyboard.

        static TouchScreenKeyboard _keyboard;
        static Action<string> _typed;

        /// <summary>This TV has a keyboard to type with (Apple TV, Android TV): use TextEntry in place of a text field.</summary>
        public static bool CanType => On && TouchScreenKeyboard.isSupported;

        /// <summary>A text field for the remote: shows the words (or the placeholder), and OK types them on the TV's keyboard.</summary>
        public static VisualElement TextEntry(string value, string placeholder, Action<string> changed)
        {
            string now = value ?? "";
            Label text = Ui.Text("", "tv-entry-t", Ui.Medium);
            void Show() { text.text = now.Length > 0 ? now : placeholder; text.EnableInClassList("tv-entry-empty", now.Length == 0); }
            Show();
            return Ui.Box("tv-entry grow", text).OnTap(() =>
            {
                if (_keyboard != null) return;
                _keyboard = TouchScreenKeyboard.Open(now, TouchScreenKeyboardType.Default, false, false, false, false, placeholder);
                _typed = typedText => { now = (typedText ?? "").Trim(); Show(); changed?.Invoke(now); };
            });
        }

        static void Typing()
        {
            if (_keyboard == null) return;
            TouchScreenKeyboard.Status status = _keyboard.status;
            if (status == TouchScreenKeyboard.Status.Visible) return;
            if (status == TouchScreenKeyboard.Status.Done) _typed?.Invoke(_keyboard.text);
            _keyboard = null; _typed = null;
        }

        // Everything that can take focus now: in the top sheet if one is open, else on the screen; shown, not hidden.
        static List<VisualElement> Candidates()
        {
            var list = new List<VisualElement>();
            VisualElement root = TopModal() ?? _stage;
            if (root == null) return list;
            root.Query<VisualElement>(className: "tv-focus").ForEach(e => { if (Shown(e)) list.Add(e); });
            return list;
        }

        static VisualElement TopModal()
        {
            if (_stage == null) return null;
            VisualElement top = null;
            _stage.Query<VisualElement>(className: "modal-dim").ForEach(e => { if (Shown(e)) top = e; });
            return top;
        }

        static bool Shown(VisualElement e)
        {
            if (e.panel == null || !e.enabledInHierarchy) return false;
            for (VisualElement x = e; x != null; x = x.parent)
                if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden || x.resolvedStyle.opacity <= 0.01f || x.ClassListContains("hidden")) return false;
            Rect r = e.worldBound;
            return r.width > 1f && r.height > 1f && !float.IsNaN(r.x);
        }

        // ── The green ────────────────────────────────────────────────────────

        const string PrefGreen = "app2.tv.green";

        /// <summary>The green the phone that signed this TV in is paired with ("puttr" by default): its Bluetooth name, and its name on the wifi.</summary>
        public static string GreenName
        {
            get => PlayerPrefs.GetString(PrefGreen, "puttr");
            set
            {
                string name = string.IsNullOrWhiteSpace(value) ? "puttr" : value.Trim();
                PlayerPrefs.SetString(PrefGreen, name);
                if (Green.Peek != null) { Green.Peek.consoleName = name; Green.Peek.SetHost(""); }
            }
        }
    }
}
