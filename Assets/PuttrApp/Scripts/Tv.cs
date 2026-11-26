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
        static readonly ConditionalWeakTable<VisualElement, Action> Arrived = new ConditionalWeakTable<VisualElement, Action>();
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
                // -app2TvAudit file [-app2TvAuditAt 8]: what the remote can reach on the screen showing then, written to a file.
                if (args[i] == "-app2TvAudit" && i + 1 < args.Count) _auditPath = args[i + 1];
                if (args[i] == "-app2TvAuditAt" && i + 1 < args.Count) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _auditAt);
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

        /// <summary>What happens when the remote moves the focus onto this (a tab shows its page, as an Apple TV tab bar does).</summary>
        public static void OnArrive(VisualElement element, Action arrived) { if (On) Arrived.AddOrUpdate(element, arrived); }

        // A path the remote follows by its own order, not by where things sit (a map's trail of stages winds about:
        // the stage "to the right" is the next one along it). As Apple's focus guides: the layout says where a press
        // goes when geometry would not.
        sealed class Link { public object Path; public VisualElement Prev, Next, Up, Down; }
        static readonly ConditionalWeakTable<VisualElement, Link> Links = new ConditionalWeakTable<VisualElement, Link>();

        /// <summary>These are one path: Right is the next along it and Left the one before. Up and Down leave the path.</summary>
        public static void Path(IList<VisualElement> ordered)
        {
            if (!On || ordered == null) return;
            var path = new object();
            for (int i = 0; i < ordered.Count; i++)
            {
                Links.Remove(ordered[i]);
                Links.Add(ordered[i], new Link { Path = path, Prev = i > 0 ? ordered[i - 1] : null, Next = i + 1 < ordered.Count ? ordered[i + 1] : null });
            }
        }

        /// <summary>A side branch off a path: `branch` is reached from `from` by Up (or Down, if it sits lower), and goes back the other way.</summary>
        public static void Branch(VisualElement from, VisualElement branch, bool above)
        {
            if (!On || from == null || branch == null || !Links.TryGetValue(from, out Link at)) return;
            if (above) at.Up = branch; else at.Down = branch;
            Links.Remove(branch);
            Links.Add(branch, new Link { Path = at.Path, Up = above ? null : from, Down = above ? from : null });
        }

        /// <summary>The element the remote's focus is on, or null.</summary>
        public static VisualElement Focused => On ? _stage?.focusController?.focusedElement as VisualElement : null;

        /// <summary>A sheet or dialog is open over the screen.</summary>
        public static bool ModalOpen => TopModal() != null;

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
                _return = null; _auto = null;
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
        static VisualElement _auto;
        static int _autoCount;
        static float _autoUntil;

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
            Glide();
            if (_auditPath != null && Time.realtimeSinceStartup >= _auditAt) { string path = _auditPath; _auditPath = null; System.IO.File.WriteAllText(path, Audit()); }
            if (Time.realtimeSinceStartup - _lastFix < 0.25f) return;
            _lastFix = Time.realtimeSinceStartup;
            var current = _stage.focusController.focusedElement as VisualElement;
            List<VisualElement> all = Candidates();
            if (all.Count == 0) return;
            if (current == null || !all.Contains(current))
            {
                bool starting = _fresh;
                VisualElement to = Recover(all);
                Focus(to);
                if (starting) { _auto = to; _autoCount = all.Count; _autoUntil = Time.realtimeSinceStartup + 6f; }
            }
            else
            {
                // The focus was put where the screen starts and the remote has not been touched: when the screen fills
                // in (its rows and its button arrive after a load), it starts again from what is there now.
                if (_auto != null && (current != _auto || Time.realtimeSinceStartup > _autoUntil)) _auto = null;
                if (_auto != null && all.Count != _autoCount)
                {
                    _autoCount = all.Count;
                    VisualElement to = Start(all);
                    if (to != current) { Focus(to); _auto = to; return; }
                }
                Note(current);
            }
        }

        /// <summary>A remote press by name: up, down, left, right, ok, back.</summary>
        public static void Press(string key)
        {
            if (Taken(key, fromNav: false)) return;
            _return = null; _auto = null;
            switch (key)
            {
                case "up": Move(NavigationMoveEvent.Direction.Up); break;
                case "down": Move(NavigationMoveEvent.Direction.Down); break;
                case "left": Move(NavigationMoveEvent.Direction.Left); break;
                case "right": Move(NavigationMoveEvent.Direction.Right); break;
                case "ok": Submit(); break;
                case "back": Back(); break;
                case "putt": TvPutt.Test(); break;
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

        // The last move: where it came from, where it went, and which way. The opposite press from there goes back
        // (down from a wide card to the row under it, then up, returns to the card it left, not the one nearest).
        static VisualElement _trailFrom, _trailTo;
        static NavigationMoveEvent.Direction _trailWay;

        static NavigationMoveEvent.Direction Opposite(NavigationMoveEvent.Direction d) => d switch
        {
            NavigationMoveEvent.Direction.Up => NavigationMoveEvent.Direction.Down,
            NavigationMoveEvent.Direction.Down => NavigationMoveEvent.Direction.Up,
            NavigationMoveEvent.Direction.Left => NavigationMoveEvent.Direction.Right,
            _ => NavigationMoveEvent.Direction.Left,
        };

        static bool Scrolls(ScrollView sv, bool vertical) => sv.mode == ScrollViewMode.VerticalAndHorizontal || (sv.mode == ScrollViewMode.Vertical) == vertical;

        // Only what can be seen can take the focus (Apple's rule), but a list scrolls: what a scroller hides is
        // within reach when the focus is in that scroller and the move is along the way it scrolls.
        static bool InReach(VisualElement c, VisualElement current, bool vertical)
        {
            Rect r = c.worldBound;
            for (VisualElement p = c.parent; p != null; p = p.parent)
            {
                if (!(p is ScrollView sv)) continue;
                Rect view = sv.contentViewport.worldBound;
                if (view.width <= 0f || view.height <= 0f || float.IsNaN(view.x)) continue;
                if (view.Overlaps(r)) continue;
                if (!sv.Contains(current)) return false;
                // Along the way it scrolls, or beside a focus that is itself out of sight there (the same hidden row).
                if (!Scrolls(sv, vertical) && view.Overlaps(current.worldBound)) return false;
            }
            return true;
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
            VisualElement best = Next(current, all, direction);
            // A warning, not a log: release builds print warnings to the console, which is what a scripted check reads.
            if (Logging) Debug.LogWarning("[tv] move " + direction + " from " + Name(current) + " " + current.worldBound + " to " + (best != null ? Name(best) + " " + best.worldBound : "nothing") + " of " + all.Count);
            if (best == null) return true;   // nothing that way: the focus stays where it is
            _trailFrom = current; _trailTo = best; _trailWay = direction;
            Focus(best);
            if (Arrived.TryGetValue(best, out Action arrived)) arrived?.Invoke();
            return true;
        }

        // Where a press that way takes the focus from `current`, or null.
        static VisualElement Next(VisualElement current, List<VisualElement> all, NavigationMoveEvent.Direction direction, bool trail = true)
        {
            bool vertical = direction == NavigationMoveEvent.Direction.Up || direction == NavigationMoveEvent.Direction.Down;
            // The scrollers the focus sits in that scroll this way, innermost first: a list is walked to its end
            // before the move leaves it (Up through the cards reaches the menu only from the top one).
            var scopes = new List<VisualElement>();
            for (VisualElement p = current.parent; p != null; p = p.parent)
                if (p is ScrollView sv && Scrolls(sv, vertical)) scopes.Add(sv);
            Rect from = current.worldBound;
            // On a path: along it by its order; any other way leaves the path (never to another stop on it).
            Links.TryGetValue(current, out Link link);
            if (link != null)
            {
                VisualElement linked = direction == NavigationMoveEvent.Direction.Right ? link.Next : direction == NavigationMoveEvent.Direction.Left ? link.Prev
                    : direction == NavigationMoveEvent.Direction.Up ? link.Up : link.Down;
                if (linked != null && all.Contains(linked)) return linked;
            }
            var rects = new List<Rect>(all.Count);
            var ranks = new List<int>(all.Count);
            foreach (VisualElement c in all)
            {
                bool samePath = link != null && Links.TryGetValue(c, out Link other) && other.Path == link.Path;
                rects.Add(c == current || samePath || !InReach(c, current, vertical) ? Rect.zero : c.worldBound);
                int rank = 0;
                while (rank < scopes.Count && !scopes[rank].Contains(c)) rank++;
                ranks.Add(rank);
            }
            // Straight back the way the focus came, if that is still there and still that way.
            if (trail && current == _trailTo && direction == Opposite(_trailWay) && _trailFrom != null)
            {
                int back = all.IndexOf(_trailFrom);
                if (back >= 0 && Pick(from, new List<Rect> { rects[back] }, null, direction) == 0) return _trailFrom;
            }
            int pick = Pick(from, rects, ranks, direction);
            // Off a path there is often nothing straight that way (its stops are scattered over a map): the nearest
            // thing in that half of the screen, so every stop has a way off the path.
            if (pick < 0 && link != null && vertical) pick = Pick(from, rects, ranks, direction, true);
            VisualElement best = pick < 0 || all[pick] == current ? null : all[pick];
            if (best == null) return null;
            // Onto a path from outside it: its stop that is marked as the way in (a map's picked stage).
            if (link == null && Links.TryGetValue(best, out Link onto))
                foreach (VisualElement c in all)
                    if (c.ClassListContains("tv-path-entry") && Links.TryGetValue(c, out Link same) && same.Path == onto.Path) { best = c; break; }
            // Into a row of tabs from outside it: the tab that is showing, not the one nearest (Apple's tab bar).
            if (vertical && best.ClassListContains("play-tab") && best.parent != null && best.parent != current.parent)
                foreach (VisualElement tab in best.parent.Children())
                    if (tab.ClassListContains("play-tab-on") && all.Contains(tab)) { best = tab; break; }
            return best;
        }

        /// <summary>
        /// Which of these the focus moves to from `from`, or -1. As Apple's focus engine: the search is a beam the size of
        /// the focused element, thrown the way of the press, and the nearest thing in it wins. Left and right take
        /// nothing else: with nothing in the beam the focus stays (a row ends where it ends). Up and down go from
        /// row to row, so with nothing in the beam they take what is beside it, else the nearest row that way.
        /// Among those, the lowest rank that has any wins (rank 0 shares the innermost scroller with the focus,
        /// higher ranks are further out).
        /// </summary>
        public static int Pick(Rect from, IList<Rect> rects, IList<int> ranks, NavigationMoveEvent.Direction direction) => Pick(from, rects, ranks, direction, false);

        // anyAhead: also what is off to the side, as long as it is in that half of the screen (a path's way off it).
        static int Pick(Rect from, IList<Rect> rects, IList<int> ranks, NavigationMoveEvent.Direction direction, bool anyAhead)
        {
            // Kept for the lowest rank seen: the best in the beam (and the clear space before it), the best beside it
            // (and how far its far edge is), and the best of what is merely in that half of the screen.
            int bestRank = int.MaxValue, inBeam = -1, beside = -1, loose = -1;
            float inScore = 0f, inAhead = 0f, besideScore = 0f, besideFar = 0f, looseScore = 0f;
            for (int i = 0; i < rects.Count; i++)
            {
                Rect to = rects[i];
                if (to.width <= 0f || to.height <= 0f) continue;
                // primary: centre to centre along the move. ahead: the clear space between the facing edges.
                // across: how far out of the beam. own: the beam's width. slide: how far out of line inside the beam.
                float primary, ahead, across, own, slide, far;
                switch (direction)
                {
                    case NavigationMoveEvent.Direction.Right: far = to.xMax - from.xMax; primary = to.center.x - from.center.x; ahead = to.xMin - from.xMax; across = Gap(from.yMin, from.yMax, to.yMin, to.yMax); own = from.height; slide = Mathf.Min(Mathf.Abs(to.center.y - from.center.y), Mathf.Abs(to.yMin - from.yMin)); if (to.xMin < from.xMax - from.width * 0.5f) continue; break;
                    case NavigationMoveEvent.Direction.Left: far = from.xMin - to.xMin; primary = from.center.x - to.center.x; ahead = from.xMin - to.xMax; across = Gap(from.yMin, from.yMax, to.yMin, to.yMax); own = from.height; slide = Mathf.Min(Mathf.Abs(to.center.y - from.center.y), Mathf.Abs(to.yMin - from.yMin)); if (to.xMax > from.xMin + from.width * 0.5f) continue; break;
                    case NavigationMoveEvent.Direction.Down: far = to.yMax - from.yMax; primary = to.center.y - from.center.y; ahead = to.yMin - from.yMax; across = Gap(from.xMin, from.xMax, to.xMin, to.xMax); own = from.width; slide = Mathf.Min(Mathf.Abs(to.center.x - from.center.x), Mathf.Abs(to.xMin - from.xMin)); if (to.yMin < from.yMax - from.height * 0.5f) continue; break;
                    default: far = from.yMin - to.yMin; primary = from.center.y - to.center.y; ahead = from.yMin - to.yMax; across = Gap(from.xMin, from.xMax, to.xMin, to.xMax); own = from.width; slide = Mathf.Min(Mathf.Abs(to.center.x - from.center.x), Mathf.Abs(to.xMin - from.xMin)); if (to.yMax > from.yMin + from.height * 0.5f) continue; break;
                }
                if (primary <= 0f) continue;
                ahead = Mathf.Max(ahead, 0f);
                // Sideways stays in its row: only what is in the beam. Up and down go row to row: beside the beam
                // counts, and failing that the nearest row that way, at the place nearest across.
                bool vertical = direction == NavigationMoveEvent.Direction.Up || direction == NavigationMoveEvent.Direction.Down;
                bool cone = (vertical || anyAhead) && across <= ahead * 0.5f + own * 0.5f;
                if (across > 0f && !vertical && !anyAhead) continue;             // off to the side: not that way
                int rank = ranks != null ? ranks[i] : 0;
                if (rank > bestRank) continue;
                if (rank < bestRank) { bestRank = rank; inBeam = beside = loose = -1; }
                if (across <= 0f)
                {
                    float score = ahead + slide * 0.3f;                          // in the beam: the nearest, then the best lined up
                    if (inBeam < 0 || score < inScore) { inBeam = i; inScore = score; inAhead = ahead; }
                }
                else if (cone)
                {
                    float score = ahead + across * 2f;                           // beside the beam, still that way
                    if (beside < 0 || score < besideScore) { beside = i; besideScore = score; besideFar = across < own * 0.25f ? far : float.MaxValue; }
                }
                else
                {
                    float score = ahead * 4f + across;                           // the nearest row, then the nearest in it
                    if (loose < 0 || score < looseScore) { loose = i; looseScore = score; }
                }
            }
            // The beam wins, unless something right beside it (within a quarter of the beam's width) would be passed over on the way (a switch at the end of
            // a row between two buttons): nothing on the way is skipped.
            if (inBeam >= 0) return beside >= 0 && besideFar <= inAhead ? beside : inBeam;
            return beside >= 0 ? beside : loose;
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
                if (p is ScrollView sv) Reveal(sv, e);
        }

        // ── Scrolling to the focus ───────────────────────────────────────────
        // The focused element is kept clear of the edge (the next one along shows, so there is somewhere to go), the
        // first row brings the top of the page back with it, and the scroll is eased, not a jump.

        const float RevealPad = 44f;
        static readonly Dictionary<ScrollView, Vector2> Glides = new Dictionary<ScrollView, Vector2>();
        static readonly List<ScrollView> GlideKeys = new List<ScrollView>();

        /// <summary>
        /// Where a scroller should sit so that [start, end] (in its content) shows with `pad` to spare: `at` if it
        /// already does, never past either end, and all the way to an end that is within reach of the padding.
        /// </summary>
        public static float RevealAt(float at, float view, float content, float start, float end, float pad)
        {
            float max = Mathf.Max(0f, content - view);
            float to = at;
            if (end - start + pad * 2f >= view) to = start - Mathf.Max(0f, (view - (end - start)) * 0.5f);   // too big to pad: centred
            else if (start - pad < at) to = start - pad;
            else if (end + pad > at + view) to = end + pad - view;
            if (to < pad * 1.5f) to = 0f;
            if (to > max - pad * 1.5f) to = max;
            return Mathf.Clamp(to, 0f, max);
        }

        static void Reveal(ScrollView sv, VisualElement e)
        {
            Rect view = sv.contentViewport.worldBound, content = sv.contentContainer.worldBound, r = e.worldBound;
            if (float.IsNaN(view.x) || float.IsNaN(r.x) || view.width <= 0f || view.height <= 0f) { sv.ScrollTo(e); return; }
            Vector2 now = sv.scrollOffset;
            Vector2 to = Glides.TryGetValue(sv, out Vector2 going) ? going : now;
            // The element in the content's own space: its place on the panel is where the scroll is this frame.
            if (Scrolls(sv, true)) to.y = RevealAt(to.y, view.height, content.height, r.yMin - content.yMin, r.yMax - content.yMin, RevealPad);
            if (Scrolls(sv, false)) to.x = RevealAt(to.x, view.width, content.width, r.xMin - content.xMin, r.xMax - content.xMin, RevealPad);
            if ((to - now).sqrMagnitude < 0.25f) { Glides.Remove(sv); return; }
            Glides[sv] = to;
        }

        static void Glide()
        {
            if (Glides.Count == 0) return;
            Perf.Wake(0.3f);
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 16f);
            GlideKeys.Clear();
            GlideKeys.AddRange(Glides.Keys);
            foreach (ScrollView sv in GlideKeys)
            {
                if (sv.panel == null) { Glides.Remove(sv); continue; }
                Vector2 to = Glides[sv], at = sv.scrollOffset, next = Vector2.Lerp(at, to, k);
                if ((to - next).sqrMagnitude < 1f) { next = to; Glides.Remove(sv); }
                sv.scrollOffset = next;
                // The scroller would not go there (its content changed size): stop asking.
                if ((sv.scrollOffset - next).sqrMagnitude > 4f) Glides.Remove(sv);
            }
        }

        // Where focus starts on a screen that has none (just shown, or a sheet just opened). As Apple's preferred
        // focus: what the screen says (class "tv-first"), else its one main button if that is in view (Play, Start,
        // Play again: OK does the obvious thing), else the tab that is showing, else the first target that is not the
        // way out (the Back arrow is reached with Back, and is the last place the focus should start).
        static VisualElement Start(List<VisualElement> all)
        {
            Rect screen = _stage.worldBound;
            foreach (VisualElement e in all) if (e.ClassListContains("tv-first")) return e;
            foreach (VisualElement e in all)
                if (e.ClassListContains("btn") && !e.ClassListContains("btn-ghost") && !e.ClassListContains("btn-white") && Inside(screen, e.worldBound) && Unclipped(e)) return e;
            foreach (VisualElement e in all) if (e.ClassListContains("play-tab-on")) return e;
            foreach (VisualElement e in all) if (!e.ClassListContains("back-chevron") && Unclipped(e)) return e;
            return all[0];
        }

        static bool Inside(Rect outer, Rect r) => r.xMin >= outer.xMin - 1f && r.xMax <= outer.xMax + 1f && r.yMin >= outer.yMin - 1f && r.yMax <= outer.yMax + 1f;

        // Not scrolled out of sight in any scroller it sits in.
        static bool Unclipped(VisualElement e)
        {
            Rect r = e.worldBound;
            for (VisualElement p = e.parent; p != null; p = p.parent)
                if (p is ScrollView sv && sv.contentViewport.worldBound is Rect view && view.width > 0f && !view.Overlaps(r)) return false;
            return true;
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

        // ── Checking a screen ────────────────────────────────────────────────
        static string _auditPath;
        static float _auditAt = 8f;

        /// <summary>
        /// What the remote can do on the screen showing now: every target with where each press takes it, then what is
        /// wrong (ISSUE lines): a target no presses reach, one inside another, one too small to see, a press that does
        /// not come back the way it went.
        /// </summary>
        public static string Audit()
        {
            var sb = new System.Text.StringBuilder();
            List<VisualElement> all = Candidates();
            var focus = _stage.focusController.focusedElement as VisualElement;
            Rect screen = _stage.worldBound;
            sb.AppendLine("screen " + screen.width.ToString("0") + "x" + screen.height.ToString("0") + " targets " + all.Count + " modal " + (TopModal() != null) + " focus " + Tag(focus));
            var ways = new[] { NavigationMoveEvent.Direction.Up, NavigationMoveEvent.Direction.Down, NavigationMoveEvent.Direction.Left, NavigationMoveEvent.Direction.Right };
            var next = new Dictionary<VisualElement, VisualElement[]>();
            foreach (VisualElement e in all)
            {
                var to = new VisualElement[4];
                for (int w = 0; w < 4; w++) to[w] = Next(e, all, ways[w], false);
                next[e] = to;
                Rect r = e.worldBound;
                sb.AppendLine("T " + Tag(e) + " | " + r.x.ToString("0") + "," + r.y.ToString("0") + " " + r.width.ToString("0") + "x" + r.height.ToString("0")
                    + " | up:" + Tag(to[0]) + " down:" + Tag(to[1]) + " left:" + Tag(to[2]) + " right:" + Tag(to[3]));
            }
            // Reach: from where the focus is (or would start), by presses alone.
            VisualElement start = focus != null && all.Contains(focus) ? focus : all.Count > 0 ? Start(all) : null;
            var seen = new HashSet<VisualElement>();
            var queue = new Queue<VisualElement>();
            if (start != null) { seen.Add(start); queue.Enqueue(start); }
            while (queue.Count > 0)
                foreach (VisualElement n in next[queue.Dequeue()]) if (n != null && seen.Add(n)) queue.Enqueue(n);
            foreach (VisualElement e in all)
            {
                Rect r = e.worldBound;
                if (!seen.Contains(e)) sb.AppendLine("ISSUE unreachable " + Tag(e));
                if (r.width < 28f || r.height < 28f) sb.AppendLine("ISSUE small " + Tag(e) + " " + r.width.ToString("0") + "x" + r.height.ToString("0"));
                for (VisualElement p = e.parent; p != null && p != _stage; p = p.parent)
                    if (all.Contains(p)) { sb.AppendLine("ISSUE nested " + Tag(e) + " in " + Tag(p)); break; }
                for (int w = 0; w < 4; w++)
                {
                    VisualElement n = next[e][w];
                    if (n == null) continue;
                    VisualElement back = next[n][w ^ 1];
                    if (back != e && back != null && !n.ClassListContains("play-tab")) sb.AppendLine("NOTE one-way " + Tag(e) + " " + ways[w] + " " + Tag(n) + " then back to " + Tag(back));
                    if (back == null) sb.AppendLine("ISSUE no-way-back " + Tag(e) + " " + ways[w] + " " + Tag(n));
                }
            }
            if (start != null && start.ClassListContains("back-chevron") && all.Count > 1) sb.AppendLine("ISSUE starts-on-back");
            return sb.ToString();
        }

        static string Tag(VisualElement e)
        {
            if (e == null) return "-";
            string name = Name(e);
            if (name != null && name.Length > 28) name = name.Substring(0, 28);
            string cls = "";
            foreach (string c in e.GetClasses()) { if (c != "tv-focus") { cls = c; break; } }
            return "[" + (name ?? "").Replace('\n', ' ') + "/" + cls + "]";
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
            // One target is never inside another (the focus could not show which of the two it is on). A card that
            // holds a button gives way to the button; any other target inside a target gives way to the outer one.
            var holdsButton = new HashSet<VisualElement>();
            var inner = new HashSet<VisualElement>();
            foreach (VisualElement e in list)
                for (VisualElement p = e.parent; p != null && p != root; p = p.parent)
                    if (p.ClassListContains("tv-focus")) { if (e.ClassListContains("btn")) holdsButton.Add(p); else inner.Add(e); break; }
            if (holdsButton.Count > 0 || inner.Count > 0) list.RemoveAll(e => holdsButton.Contains(e) || (inner.Contains(e) && !(e.parent != null && Within(e, holdsButton))));
            return list;
        }

        static bool Within(VisualElement e, HashSet<VisualElement> outers)
        {
            for (VisualElement p = e.parent; p != null; p = p.parent) if (outers.Contains(p)) return true;
            return false;
        }

        static VisualElement TopModal()
        {
            if (_stage == null) return null;
            VisualElement top = null;
            _stage.Query<VisualElement>(className: "modal-dim").ForEach(e => { if (Shown(e)) top = e; });
            return top;
        }

        internal static bool Shown(VisualElement e)
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
