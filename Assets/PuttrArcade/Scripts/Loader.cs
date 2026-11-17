// What shows while the arcade builds: the Puttr mark, still, in Puttr green on the arcade's ink,
// with a thin bar under it that fills as each part is built, then a fade into the home screen.
//
// The mark does not turn. The build runs on the main thread a piece a frame, and some pieces take
// a good part of a second, so a turning mark stuttered; a bar that steps forward reads as progress.
//
// The native launch screens (iOS/tvOS storyboard, Android splash) show the same mark, same size, on
// the same colour, so the hand-over to Unity cannot be seen: the bar simply appears under it.
// Unity's own splash is off (PlayerSettings > Splash Image).
using UnityEngine;
using UnityEngine.UI;

namespace PuttrArcade
{
    public static class Loader
    {
        // Shared with the launch screens: keep Editor/LaunchScreens.cs in step if these change.
        public static readonly Color Background = Make.Ink;
        public static readonly Color Mark = Make.Green;
        public const float SizeOfShortSide = 0.16f; // the mark's width, as a share of the screen's short side

        // puttr-app-2: the app's splash is the whole logo (mark and name), not the mark alone, half the
        // short side wide. The native launch screen is the same picture: Editor/IosSplash.cs.
        public static bool AppSplash;

        /// <summary>puttr-app-2: building quietly behind the app's game page (a backdrop): the loader is not drawn.</summary>
        public static bool Quiet
        {
            get => _quiet;
            set { _quiet = value; if (_root != null) { Canvas c = _root.GetComponent<Canvas>(); if (c != null) c.enabled = !value; } }
        }
        static bool _quiet;
        public const float SplashShare = 0.5f, SplashAspect = 1943f / 479f;

        static GameObject _root;
        static Host _host;

        /// <summary>Puts the loader up; returns its component, which can host the build's coroutine.</summary>
        public static MonoBehaviour Show()
        {
            if (_root != null) return _host;
            _root = new GameObject("Loader");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // over the HUD while it builds underneath
            canvas.enabled = !_quiet;
            var group = _root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            var bg = new GameObject("Background").AddComponent<RawImage>();
            bg.transform.SetParent(_root.transform, false);
            bg.color = Background;
            bg.raycastTarget = false;
            Stretch(bg.rectTransform);

            var mark = new GameObject("Mark").AddComponent<RawImage>();
            mark.transform.SetParent(_root.transform, false);
            mark.texture = Resources.Load<Texture2D>(AppSplash ? "PuttrArcade/LogoHeader" : "PuttrArcade/Spinner");
            mark.color = AppSplash ? Color.white : Mark; // the logo carries its own green and white
            mark.raycastTarget = false;
            mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            var track = new GameObject("Track").AddComponent<RawImage>();
            track.transform.SetParent(_root.transform, false);
            track.color = new Color(1f, 1f, 1f, 0.1f);
            track.raycastTarget = false;
            var fill = new GameObject("Fill").AddComponent<RawImage>();
            fill.transform.SetParent(track.transform, false);
            fill.color = Mark;
            fill.raycastTarget = false;

            _host = _root.AddComponent<Host>();
            _host.Mark = mark.rectTransform;
            _host.Track = track.rectTransform;
            _host.Fill = fill.rectTransform;
            _host.Group = group;
            _host.Layout();
            return _host;
        }

        /// <summary>How much of the arcade is built, 0 to 1.</summary>
        public static void Progress(float done)
        {
            if (_host != null) _host.Done = Mathf.Max(_host.Done, Mathf.Clamp01(done));
        }

        /// <summary>Fills the bar, fades the loader out and removes it.</summary>
        public static void Hide()
        {
            if (_host != null) { _host.Done = 1f; _host.FadeOut(); }
            _root = null;
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        class Host : MonoBehaviour
        {
            public RectTransform Mark, Track, Fill;
            public CanvasGroup Group;
            public float Done;
            float _shown, _fadeFrom = -1f;
            Vector2Int _size;
            const float Fade = 0.35f;

            public void FadeOut() => _fadeFrom = Time.unscaledTime;

            public void Layout()
            {
                _size = new Vector2Int(Screen.width, Screen.height);
                float side = Mathf.Min(Screen.width, Screen.height) * SizeOfShortSide;
                if (AppSplash)
                {
                    // The logo where the launch screen put it, and the bar a little way under it.
                    float wide = Mathf.Min(Screen.width, Screen.height) * SplashShare, high = wide / SplashAspect;
                    Mark.sizeDelta = new Vector2(wide, high);
                    Track.anchorMin = Track.anchorMax = new Vector2(0.5f, 0.5f);
                    Track.sizeDelta = new Vector2(wide * 0.62f, Mathf.Max(3f, high * 0.035f));
                    Track.anchoredPosition = new Vector2(0f, -high * 1.25f);
                    Fill.anchorMin = Vector2.zero;
                    Fill.pivot = new Vector2(0f, 0.5f);
                    Fill.offsetMin = Fill.offsetMax = Vector2.zero;
                    return;
                }
                Mark.sizeDelta = new Vector2(side, side);
                // A hairline bar, as wide as the mark, a little way under it.
                Track.anchorMin = Track.anchorMax = new Vector2(0.5f, 0.5f);
                Track.sizeDelta = new Vector2(side * 1.1f, Mathf.Max(2f, side * 0.025f));
                Track.anchoredPosition = new Vector2(0f, -side * 0.85f);
                Fill.anchorMin = Vector2.zero;
                Fill.pivot = new Vector2(0f, 0.5f);
                Fill.offsetMin = Fill.offsetMax = Vector2.zero;
            }

            void Update()
            {
                if (_size.x != Screen.width || _size.y != Screen.height) Layout();
                // The bar eases toward what is built, so a step forward glides rather than jumps.
                _shown = Mathf.MoveTowards(_shown, Done, Time.unscaledDeltaTime * 2.5f);
                Fill.anchorMax = new Vector2(_shown, 1f);
                if (_fadeFrom < 0f) return;
                float t = (Time.unscaledTime - _fadeFrom) / Fade;
                Group.alpha = 1f - Mathf.SmoothStep(0f, 1f, t);
                if (t >= 1f) Destroy(gameObject);
            }
        }
    }
}
