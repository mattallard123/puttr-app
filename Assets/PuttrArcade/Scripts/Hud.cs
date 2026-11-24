// The interface, built in code on one canvas in the Puttr style: black glass
// cards, white Montserrat, green for what matters.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PuttrArcade
{
    public sealed class Hud
    {
        static readonly Color Glass = new Color(0.02f, 0.022f, 0.024f, 0.74f);
        static readonly Color Border = new Color(1f, 1f, 1f, 0.1f);
        static readonly Color Cell = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color Fg1 = Color.white;
        static readonly Color Fg2 = new Color(1f, 1f, 1f, 0.7f);
        static readonly Color Fg3 = new Color(1f, 1f, 1f, 0.5f);

        readonly Font _bold, _medium, _regular, _light;
        readonly Sprite _round, _outline;
        readonly Texture2D _logo;
        readonly RectTransform _root;

        GameObject _status;
        Image _statusDot;
        Text _statusText;
        GameObject _promptCard;
        Text _prompt;
        Image _promptDot;
        GameObject _puttCard;
        readonly Text[] _puttValues = new Text[3];
        Text _banner, _bannerSub;
        Image _bannerBand; // a dark band across the screen, so the banner reads over any game
        float _bannerSize = 1f;
        /// <summary>Draw banners small and low, for a game that plays out in the middle of the screen.</summary>
        public bool SmallBanner;
        float _bannerAge = 99f, _bannerLife = 1f;
        GameObject _overlay;
        RawImage _overlayLogo;
        Text _overlayTitle, _overlayBody;
        RectTransform _scoreRoot;
        float _time;

        sealed class Row
        {
            public Image Bar;
            public Text Name, Total;
            public Image[] Rings;
            public Text[] Tops, Mains;
            public Image[] Pips, PipDots;
            public Text[] PipTexts;
        }
        Text _gameName;
        GameObject _home;
        Text _homeFoot;
        readonly List<Image> _homeRings = new List<Image>();
        readonly List<CanvasGroup> _homeCards = new List<CanvasGroup>();
        readonly List<Row> _rows = new List<Row>();

        public Hud(Transform parent)
        {
            Font fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _bold = LoadFont("Montserrat-Bold", fallback);
            _medium = LoadFont("Montserrat-Medium", fallback);
            _regular = LoadFont("Montserrat-Regular", fallback);
            _light = LoadFont("Montserrat-Light", fallback);
            _round = Make.RoundedSprite(14);
            _outline = Make.RoundedSprite(14, true);
            _logo = Resources.Load<Texture2D>("PuttrArcade/LogoHeader");

            var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true; // type lands on whole pixels, so it stays crisp
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler = scaler;
            _canvas = canvas;
            // Everything hangs from Safe, which keeps clear of the iPhone's Dynamic Island, rounded
            // corners and home indicator; full-screen backdrops bleed back out to the edges (Bleed).
            _root = Node("Safe", go.transform);
            Stretch(_root);
            FitScreen();

            BuildBrand();
            BuildStatus();
            BuildPutt();
            BuildPrompt();
            BuildBanner();
            BuildOverlay();
            BuildMenuButton();
            _uprightKnown = false; // puttr-app-2: arrange for the way the device is held, now that everything exists
            FitScreen();
        }

        static Font LoadFont(string name, Font fallback)
        {
            var font = Resources.Load<Font>("PuttrArcade/Fonts/" + name);
            return font != null ? font : fallback;
        }

        // ── Which kind of device ────────────────────────────────────────────

        /// <summary>How this device is worked: a keyboard and mouse, fingers, or a remote.</summary>
        public enum Device { Desktop, Touch, Tv }

        public static Device Kind =>
            Application.platform == RuntimePlatform.tvOS || AndroidTv ? Device.Tv :
            Application.isMobilePlatform ? Device.Touch : Device.Desktop;

        // One Android build runs on tablets and on TVs (Android TV, Google TV, Fire TV): the
        // device says which it is. Asked once.
        static int _androidTv = -1;
        static bool AndroidTv
        {
            get
            {
                if (_androidTv < 0)
                {
                    _androidTv = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
                    try
                    {
                        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        using (AndroidJavaObject packages = activity.Call<AndroidJavaObject>("getPackageManager"))
                        {
                            bool tv = packages.Call<bool>("hasSystemFeature", "android.software.leanback")
                                || packages.Call<bool>("hasSystemFeature", "android.hardware.type.television")
                                || packages.Call<bool>("hasSystemFeature", "amazon.hardware.fire_tv");
                            _androidTv = tv ? 1 : 0;
                        }
                    }
                    catch (System.Exception) { }
#endif
                }
                return _androidTv == 1;
            }
        }

        // Say only what this device can do.
        public static string Choose(string desktop, string touch, string tv) => For(desktop, touch, tv);

        static string For(string desktop, string touch, string tv) => Kind == Device.Tv ? tv : Kind == Device.Touch ? touch : desktop;

        // Fingers need more room than a mouse: nothing they press is under 44 points.
        static bool Fingers => Kind == Device.Touch;
        static float Sized(float mouse, float finger) => Fingers ? finger : mouse;

        // ── Taps and clicks ─────────────────────────────────────────────────
        //
        // Nothing here is a Button. Each thing that can be pressed is noted
        // with a name, and the arcade asks what is under the finger.

        readonly List<KeyValuePair<RectTransform, string>> _taps = new List<KeyValuePair<RectTransform, string>>();

        void Tappable(Component thing, string name) => _taps.Add(new KeyValuePair<RectTransform, string>((RectTransform)thing.transform, name));

        /// <summary>The name of what is showing at this point on screen, or null.</summary>
        public string TapAt(Vector2 screen) => Under(screen, out _);

        string Under(Vector2 screen, out RectTransform what)
        {
            // A card over the whole screen takes every press: nothing under it can be reached.
            Transform over = _overlay != null && _overlay.activeInHierarchy ? _overlay.transform : null;
            for (int i = _taps.Count - 1; i >= 0; i--)
            {
                RectTransform rt = _taps[i].Key;
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                if (over != null && !rt.IsChildOf(over)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null)) { what = rt; return _taps[i].Value; }
            }
            what = null;
            return null;
        }

        RectTransform _pressed;
        Vector3 _pressedScale;

        /// <summary>A finger or the mouse has come down here: what is under it gives a little.</summary>
        public string Press(Vector2 screen)
        {
            LetGo();
            string name = Under(screen, out RectTransform what);
            if (what == null || name == "overlay" || name == "brand" || name == "status") return name;
            _pressed = what;
            _pressedScale = what.localScale;
            what.localScale = _pressedScale * 0.96f;
            return name;
        }

        public void LetGo()
        {
            if (_pressed != null) _pressed.localScale = _pressedScale;
            _pressed = null;
        }

        // ── The row of games: dragged, flicked, and kept in view ────────────

        Canvas _canvas;

        /// <summary>puttr-app-2: the whole HUD off while a game's set is only a backdrop behind the app's page.</summary>
        public void SetShown(bool on) { if (_canvas != null) _canvas.enabled = on; }
        RectTransform _railView, _rail;
        float _railX, _railMax, _railSpeed, _railGoal = -1f, _railWidth, _tileW, _railGrab;
        int _revealed = -1;
        bool _railHeld;
        const float RailPad = 64f, RailGap = 14f, RailLift = 24f;

        public bool OnRail(Vector2 screen) => _railView != null && _railView.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(_railView, screen, null);

        public void HoldRail() { _railHeld = true; _railSpeed = 0f; _railGoal = -1f; _railGrab = Mathf.Clamp(_railX, 0f, _railMax); }

        /// <summary>Put the row where the finger has taken it. pixels is how far the finger is from
        /// where the drag began, to the right. Placing it outright, not adding up small steps,
        /// keeps the row under the finger whatever the frames do.</summary>
        public void DragRail(float pixels)
        {
            float x = _railGrab - pixels / Mathf.Max(0.01f, _canvas.scaleFactor);
            if (x < 0f) x *= 0.3f;                                        // past the end it resists
            else if (x > _railMax) x = _railMax + (x - _railMax) * 0.3f;
            _railX = x;
        }

        /// <summary>Let the row go, travelling at this many pixels a second to the right.</summary>
        public void FlingRail(float pixelsPerSecond)
        {
            _railHeld = false;
            _railSpeed = Mathf.Clamp(-pixelsPerSecond / Mathf.Max(0.01f, _canvas.scaleFactor), -5200f, 5200f);
            if (Mathf.Abs(_railSpeed) < 60f) _railSpeed = 0f; // set down, not thrown
        }

        void TickRail(float dt)
        {
            if (_rail == null || !_home.activeSelf) return;
            _railMax = Mathf.Max(0f, _railWidth - _root.rect.width);
            if (!_railHeld)
            {
                if (_railGoal >= 0f)
                {
                    _railGoal = Mathf.Min(_railGoal, _railMax);
                    _railX = Mathf.Lerp(_railX, _railGoal, 1f - Mathf.Exp(-12f * dt));
                    if (Mathf.Abs(_railX - _railGoal) < 0.5f) { _railX = _railGoal; _railGoal = -1f; }
                }
                else if (Mathf.Abs(_railSpeed) > 4f)
                {
                    _railX += _railSpeed * dt;
                    _railSpeed *= Mathf.Exp(-2.4f * dt);
                }
                float inside = Mathf.Clamp(_railX, 0f, _railMax);
                if (_railX != inside)
                {
                    _railSpeed *= Mathf.Exp(-30f * dt); // it runs a little past the end, then eases back
                    _railX = Mathf.Lerp(_railX, inside, 1f - Mathf.Exp(-9f * dt));
                    if (Mathf.Abs(_railX - inside) < 0.5f) _railX = inside;
                }
            }
            _rail.anchoredPosition = new Vector2(-_railX, RailLift);
        }

        // Bring a tile wholly into view.
        void Reveal(int i)
        {
            float left = RailPad + i * (_tileW + RailGap), width = _root.rect.width;
            float goal = _railGoal >= 0f ? _railGoal : _railX;
            if (left - RailPad < goal) goal = left - RailPad;
            else if (left + _tileW + RailPad > goal + width) goal = left + _tileW + RailPad - width;
            else return;
            _railGoal = Mathf.Clamp(goal, 0f, Mathf.Max(0f, _railWidth - width));
            _railSpeed = 0f;
        }

        // In a game, top right: a way to stop, start over, or pick another game.
        GameObject _menuButton;

        void BuildMenuButton()
        {
            if (Kind == Device.Tv) return; // the remote's Menu button does this
            float size = _menuBuilt = Sized(56f, 76f);
            Image button = Panel("Menu", _root, new Color(0.02f, 0.022f, 0.024f, 0.74f));
            Place(button.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -32), new Vector2(size, size));
            Image edge = Panel("Edge", button.transform, new Color(1f, 1f, 1f, 0.12f));
            edge.sprite = _outline;
            Stretch(edge.rectTransform);
            for (int i = -1; i <= 1; i += 2)
            {
                Image bar = Solid("Bar", button.transform, Fg1);
                Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(i * size * 0.11f, 0), new Vector2(size * 0.1f, size * 0.36f));
            }
            Tappable(button, "menu");
            _menuRect = button.rectTransform;
            _menuButton = button.gameObject;
            _menuButton.SetActive(false);
        }

        // puttr-app-2 (walk, 2026-11-23): upright, in a game, a soft dark shade lies behind the logo row and the top of the
        // scoreboard, so the game's name can be read over a bright set (Balloon Pop's awning, Putt Star's lights, Putt-ball's
        // marquee). It is on whenever the pause button is.
        RawImage _topShade;
        bool _inGame;
        void ShowTopShade()
        {
            if (_topShade == null)
            {
                var tex = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 32; y++) tex.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(y / 31f, 0.6f)));
                tex.Apply();
                _topShade = Raw("Top shade", _root, tex);
                _topShade.color = new Color(0f, 0f, 0f, 0.8f);
                RectTransform rt = _topShade.rectTransform;
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0, 140f);            // up over the island and the status bar
                rt.sizeDelta = new Vector2(400f, 140f + 230f);         // wider than the safe area, down past the logo row
                _topShade.transform.SetAsFirstSibling();
            }
            _topShade.gameObject.SetActive(_inGame && Upright);
        }

        public void SetMenuButton(bool visible)
        {
            _inGame = visible;
            ShowTopShade();
            if (_menuButton == null) return;
            _menuButton.SetActive(visible);
            if (visible) _menuButton.transform.SetAsLastSibling();
        }

        // A way back, top left under the logo, on the screens that have one.
        void BackButton(Transform parent)
        {
            if (Kind == Device.Tv) return; // the remote's Menu button goes back
            Image back = Panel("Back", parent, new Color(1f, 1f, 1f, 0.05f));
            Place(back.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -112), new Vector2(Sized(132, 168), Sized(52, 74)));
            Text label = Label("Text", back.transform, "Back", _medium, (int)Sized(21, 24), Fg1, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);
            Tappable(back, "back");
        }

        // ── Pieces ──────────────────────────────────────────────────────────

        // Top left: the Puttr logo and the name of the game.
        void BuildBrand()
        {
            RectTransform brand = _brand = Node("Brand", _root);
            Tappable(brand, "brand");
            Place(brand, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -36), new Vector2(420, 56));
            float x = 0;
            if (_logo != null)
            {
                float h = 40f, w = h * _logo.width / _logo.height;
                RawImage logo = Raw("Logo", brand, _logo);
                Place(logo.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(w, h));
                x = w + 18f;
            }
            Image rule = Solid("Rule", brand, new Color(1, 1, 1, 0.25f));
            Place(rule.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(2, 34));
            _gameName = Label("Game", brand, "", _light, 26, Fg1, TextAnchor.MiddleLeft);
            _gameName.horizontalOverflow = HorizontalWrapMode.Overflow;
            Text name = _gameName;
            Place(name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 20, -1), new Vector2(220, 44));
        }

        // Top right, and only when something needs saying about the console.
        void BuildStatus()
        {
            Image card = Card("Status", _root);
            _status = card.gameObject;
            Tappable(card, "status");
            Place(card.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(Sized(-108, -130), Sized(-36, -46)), new Vector2(480, 48));
            _statusDot = Panel("Dot", card.transform, Make.Amber);
            Place(_statusDot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(12, 12));
            _statusText = Label("Text", card.transform, "", _medium, 19, Fg2, TextAnchor.MiddleLeft);
            Place(_statusText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 0), new Vector2(430, 40));
            _statusText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _status.SetActive(false);
        }

        // Bottom left: the last putt, in three numbers.
        void BuildPutt()
        {
            Image card = Card("Last putt", _root);
            _puttCard = card.gameObject;
            Place(card.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 40), new Vector2(560, 104));
            string[] names = { "PACE", "LINE", "RESULT" };
            float[] xs = { 28, 176, 344 };
            float[] widths = { 140, 160, 200 };
            for (int i = 0; i < 3; i++)
            {
                Text name = Label("Name", card.transform, names[i], _medium, 14, Fg3, TextAnchor.UpperLeft);
                _puttNames[i] = name;
                Place(name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(xs[i], -22), new Vector2(widths[i], 18));
                _puttValues[i] = Label("Value", card.transform, "", _bold, 26, Fg1, TextAnchor.UpperLeft);
                Place(_puttValues[i].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(xs[i], -46), new Vector2(widths[i], 34));
                _puttValues[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            _puttCard.SetActive(false);
        }

        void BuildPrompt()
        {
            Image card = Card("Prompt", _root);
            _promptCard = card.gameObject;
            Place(card.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(640, 64));
            _promptDot = Panel("Dot", card.transform, Make.Bright);
            Place(_promptDot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(12, 12));
            _prompt = Label("Text", card.transform, "", _medium, 23, Fg1, TextAnchor.MiddleCenter);
            Stretch(_prompt.rectTransform);
            _promptCard.SetActive(false);

            // Above it: where to putt from.
            Image tee = Card("Tee", _root);
            _teeCard = tee.gameObject;
            Place(tee.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 112), new Vector2(360, 40));
            _tee = Label("Text", tee.transform, "", _bold, 16, Make.Bright, TextAnchor.MiddleCenter);
            Stretch(_tee.rectTransform);
            _teeCard.SetActive(false);
        }

        GameObject _teeCard;
        Text _tee;

        // A big sign for games that move the Player round the mat: the distance large enough to
        // read across the room, and the three tees as the Player sees the mat with a golf ball on
        // the one to putt from.
        // puttr-app-2: the dots were 24 unit squares beside the words "LEFT TEE". They are now the app's tee marker
        // (Frame.TeeMarker): three 64 unit circles, the ball on the active one and the other two empty rings, and
        // no words, so the picture is the instruction.
        GameObject _teeSign;
        Text _teeSignFeet;
        readonly Image[] _teeSignRing = new Image[3], _teeSignFill = new Image[3], _teeSignBall = new Image[3];
        Sprite _teeDisc, _teeRing;
        static readonly Color TeeEmpty = new Color(0.43f, 0.45f, 0.46f, 1f), TeeFill = new Color(0.18f, 0.196f, 0.208f, 1f), TeeOn = Color.white; // the tee to putt from: white, as the app's TeeMarker (Matt, 2026-11-22)

        /// <summary>Show the big where-to-putt-from sign: feet, and tee 1 left, 2 center, 3 right.
        /// feet 0 or less hides it. low puts it bottom left (puttr-app-2: Major League, across from its hit wheel).</summary>
        public void SetTeeSign(int feet, int tee, bool low = false)
        {
            _teeFeet = feet; _teeTee = tee;
            if (feet <= 0) { if (_teeSign != null) _teeSign.SetActive(false); if (_teeChip != null) _teeChip.SetActive(false); if (_aimCard != null) _aimCard.SetActive(false); return; }
            ShowTeeChip();
            if (_teeSign == null)
            {
                const float spot = 64f, gap = 26f, left = 30f, top = -170f;
                // Circles of their own: the panels' sprite has 14 unit corners, which is a rounded square at this size.
                _teeDisc = Make.RoundedSprite(64);
                _teeRing = Make.RoundedSprite(64, true, 9f);
                Image card = Card("Tee sign", _root);
                _teeSign = card.gameObject;
                var tl = new Vector2(0, 1);
                Place(card.rectTransform, tl, tl, new Vector2(48, -128), new Vector2(330, 260));
                Text caption = Label("Caption", card.transform, "PUTT FROM", _bold, 16, Make.Bright, TextAnchor.MiddleLeft);
                Place(caption.rectTransform, tl, tl, new Vector2(28, -18), new Vector2(250, 24));
                _teeSignFeet = Label("Feet", card.transform, "", _bold, 112, Fg1, TextAnchor.LowerLeft);
                _teeSignFeet.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(_teeSignFeet.rectTransform, tl, tl, new Vector2(24, -36), new Vector2(160, 118));
                Text unit = Label("Unit", card.transform, "FEET", _bold, 30, Fg2, TextAnchor.LowerLeft);
                Place(unit.rectTransform, tl, tl, new Vector2(150, -93), new Vector2(120, 38)); // baseline with the number: lower-aligned boxes sit each size's descender (about a quarter of it) above their bottoms
                for (int i = 0; i < 3; i++)
                {
                    var at = new Vector2(left + i * (spot + gap), top);
                    // Solid colours only (the project is linear colour): a filled disc for the active tee, a ring round every tee, the ball on top.
                    _teeSignFill[i] = Solid("Tee " + (i + 1) + " fill", card.transform, TeeFill);
                    _teeSignFill[i].sprite = _teeDisc;
                    _teeSignFill[i].type = Image.Type.Sliced;
                    Place(_teeSignFill[i].rectTransform, tl, tl, at, new Vector2(spot, spot));
                    _teeSignRing[i] = Solid("Tee " + (i + 1), card.transform, TeeEmpty);
                    _teeSignRing[i].sprite = _teeRing;
                    _teeSignRing[i].type = Image.Type.Sliced;
                    Place(_teeSignRing[i].rectTransform, tl, tl, at, new Vector2(spot, spot));
                    _teeSignBall[i] = Solid("Tee " + (i + 1) + " ball", card.transform, Color.white);
                    _teeSignBall[i].sprite = _teeDisc;
                    _teeSignBall[i].type = Image.Type.Sliced;
                    Place(_teeSignBall[i].rectTransform, tl, tl, at + new Vector2(spot * 0.2f, -spot * 0.2f), new Vector2(spot * 0.6f, spot * 0.6f));
                }
            }
            // puttr-app-2: top left, or bottom left for a game whose own panel is bottom right (Matt, 2026-11-21).
            var corner = low ? Vector2.zero : new Vector2(0, 1);
            Place((RectTransform)_teeSign.transform, corner, corner, low ? new Vector2(48, 48) : new Vector2(48, -128), new Vector2(330, 260));
            _teeSign.SetActive(!Upright);
            _teeSign.transform.SetAsLastSibling();
            _teeSignFeet.text = feet.ToString();
            // Two digits are wider: the word moves along with them.
            ((RectTransform)_teeSignFeet.transform.parent.Find("Unit")).anchoredPosition = new Vector2(feet >= 10 ? 172 : 104, -93);
            int t = Mathf.Clamp(tee, 1, 3);
            for (int i = 0; i < 3; i++)
            {
                bool on = i == t - 1;
                _teeSignFill[i].enabled = on;
                _teeSignBall[i].enabled = on;
                _teeSignRing[i].color = on ? TeeOn : TeeEmpty;
            }
        }

        // puttr-app-2: a phone held upright. The sign above is a third of the screen there and sat over the scoreboard,
        // the scene and the putt's replay. Upright it is a band centered low on the screen, above the last putt's
        // card (where the "PUTT FROM" line was): the three tees as circles, the one to putt from solid, and the
        // distance large.
        int _teeFeet, _teeTee;
        GameObject _teeChip;
        Text _teeChipFeet;
        readonly Image[] _teeChipDot = new Image[3], _teeChipRing = new Image[3];

        void ShowTeeChip()
        {
            if (_teeChip == null)
            {
                const float dot = 38f, gap = 14f;
                Sprite disc = Make.RoundedSprite(38), ring = Make.RoundedSprite(38, true, 6f);
                Image card = Card("Tee chip", _root);
                _teeChip = card.gameObject;
                var tl = new Vector2(0, 1);
                Place(card.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 232), new Vector2(320, 76));
                for (int i = 0; i < 3; i++)
                {
                    var at = new Vector2(24 + i * (dot + gap), -19);
                    _teeChipRing[i] = Solid("Tee " + (i + 1), card.transform, TeeEmpty);
                    _teeChipRing[i].sprite = ring;
                    _teeChipRing[i].type = Image.Type.Sliced;
                    Place(_teeChipRing[i].rectTransform, tl, tl, at, new Vector2(dot, dot));
                    _teeChipDot[i] = Solid("Tee " + (i + 1) + " on", card.transform, TeeOn);
                    _teeChipDot[i].sprite = disc;
                    _teeChipDot[i].type = Image.Type.Sliced;
                    Place(_teeChipDot[i].rectTransform, tl, tl, at, new Vector2(dot, dot));
                }
                _teeChipFeet = Label("Feet", card.transform, "", _bold, 46, Fg1, TextAnchor.MiddleLeft);
                _teeChipFeet.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(_teeChipFeet.rectTransform, tl, tl, new Vector2(24 + 3 * (dot + gap) + 10, -10), new Vector2(140, 56));
            }
            _teeChip.SetActive(Upright && _teeFeet > 0);
            if (_teeFeet <= 0) return;
            _teeChip.transform.SetAsLastSibling();
            _teeChipFeet.text = _teeFeet + " ft";
            ((RectTransform)_teeChip.transform).sizeDelta = new Vector2(24 + 3 * 52f + 10 + _teeChipFeet.preferredWidth + 26, 76);
            int t = Mathf.Clamp(_teeTee, 1, 3);
            for (int i = 0; i < 3; i++) { _teeChipDot[i].enabled = i == t - 1; _teeChipRing[i].enabled = i != t - 1; }
        }

        /// <summary>puttr-app-2, upright: how far down the top stack ends (the logo row, then the scoreboard), from the top of the safe area.</summary>
        float UnderScore()
        {
            float y = -84f;
            if (_scoreRoot != null && _scoreRoot.gameObject.activeInHierarchy)
            {
                var corners = new Vector3[4];
                _scoreRoot.GetWorldCorners(corners);
                y = Mathf.Min(y, _root.InverseTransformPoint(corners[0]).y - _root.rect.yMax - 10f);
            }
            if (StatusBelowPx > 0f)
            {
                float k = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
                y = Mathf.Min(y, -StatusBelowPx / k - 10f);
            }
            return y;
        }

        // Upright, every frame (a game builds its scoreboard whenever it likes): the scoreboard under the logo row and
        // no wider than the screen, and the putt's replay smaller under it on the right.
        void StackUpright()
        {
            if (_scoreRoot != null)
            {
                float fit = Upright ? Mathf.Min(1f, (UprightWide - 24f) / Mathf.Max(1f, _scoreRoot.sizeDelta.x)) : 1f;
                _scoreRoot.localScale = Vector3.one * fit;
                _scoreRoot.anchoredPosition = new Vector2(_scoreRoot.anchoredPosition.x, Upright ? -84f : -28f);
            }
            if (_teeSign != null && _teeSign.activeSelf && Upright) _teeSign.SetActive(false);
            if (_teeChip != null && !Upright && _teeChip.activeSelf) { _teeChip.SetActive(false); if (_teeSign != null && _teeFeet > 0) _teeSign.SetActive(true); }
            if (_teeChip != null && Upright && !_teeChip.activeSelf && _teeFeet > 0 && _teeSign != null) ShowTeeChip();
            float under = Upright ? UnderScore() : 0f;
            if (_aimCard != null)
            {
                var aim = (RectTransform)_aimCard.transform;
                aim.localScale = Vector3.one * (Upright ? 0.62f : 1f);
                aim.anchoredPosition = Upright ? new Vector2(-20f, under) : new Vector2(-48f, -128f);
            }
        }

        // ── The ideal putt (AimView) ────────────────────────────────────────
        // Top right, across from the tee sign: the real ramp and hole, seen by AimView's own
        // camera, with the perfect putt's path on the carpet, and one line saying it in words.

        GameObject _aimCard;
        RawImage _aimPicture;
        Text _aimWords, _aimPaceWords, _aimCaption;
        GameObject _aimPace;
        RectTransform _aimPaceZone, _aimPaceNeedle;

        /// <summary>puttr-app-2: where the ideal putt card is on the screen, in pixels from the bottom left; empty when
        /// it is not up. The app lays its own cards out under it.</summary>
        public Rect AimCardPx
        {
            get
            {
                if (_aimCard == null || !_aimCard.activeInHierarchy) return Rect.zero;
                var corners = new Vector3[4];
                ((RectTransform)_aimCard.transform).GetWorldCorners(corners);
                return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
            }
        }

        /// <summary>The ramp picture and its line of words. paceLow/paceHigh (0 to 1 along the bar):
        /// the pace that counts, for games where the pace is the point (Send It); paceNow, where the
        /// last putt's pace landed (below 0 for none).</summary>
        public void SetAimCard(Texture picture, string words, float paceLow = -1f, float paceHigh = -1f, float paceNow = -1f, string paceWords = null, string caption = "IDEAL PUTT")
        {
            const float W = 330f, pic = W - 24f, picH = pic * 0.625f;
            if (_aimCard == null)
            {
                Image card = Card("Ideal putt", _root);
                _aimCard = card.gameObject;
                var tr = new Vector2(1, 1);
                Place(card.rectTransform, tr, tr, new Vector2(-48, -128), new Vector2(W, 34 + picH + 44));
                var tl = new Vector2(0, 1);
                _aimCaption = Label("Caption", card.transform, "IDEAL PUTT", _bold, 16, Make.Bright, TextAnchor.MiddleLeft);
                Place(_aimCaption.rectTransform, tl, tl, new Vector2(16, -8), new Vector2(260, 24));
                _aimPicture = Raw("Ramp", card.transform, null);
                Place(_aimPicture.rectTransform, tl, tl, new Vector2(12, -34), new Vector2(pic, picH));
                _aimWords = Label("Words", card.transform, "", _bold, 18, Fg1, TextAnchor.MiddleCenter);
                _aimWords.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(_aimWords.rectTransform, tl, tl, new Vector2(10, -(38 + picH)), new Vector2(W - 20, 32));

                // Pace: a bar from soft to firm, the window that counts lit green, a needle for the putt.
                var pace = Node("Pace", card.transform);
                _aimPace = pace.gameObject;
                Place(pace, tl, tl, new Vector2(16, -(74 + picH)), new Vector2(W - 32, 52));
                Image track = Panel("Track", pace, new Color(1f, 1f, 1f, 0.12f));
                Place(track.rectTransform, tl, tl, new Vector2(0, -6), new Vector2(W - 32, 14));
                Image zone = Panel("Counts", pace, Make.Bright);
                _aimPaceZone = zone.rectTransform;
                Image needle = Solid("Putt", pace, Color.white);
                _aimPaceNeedle = needle.rectTransform;
                Text soft = Label("Soft", pace, "SOFT", _bold, 12, Fg3, TextAnchor.MiddleLeft);
                Place(soft.rectTransform, tl, tl, new Vector2(0, -24), new Vector2(80, 16));
                Text firm = Label("Firm", pace, "FIRM", _bold, 12, Fg3, TextAnchor.MiddleRight);
                Place(firm.rectTransform, tl, tl, new Vector2(W - 32 - 80, -24), new Vector2(80, 16));
                _aimPaceWords = Label("Target", pace, "", _bold, 14, Make.Bright, TextAnchor.MiddleCenter);
                _aimPaceWords.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(_aimPaceWords.rectTransform, tl, tl, new Vector2(60, -24), new Vector2(W - 32 - 120, 16));
            }
            if (picture == null) { _aimCard.SetActive(false); return; }
            _aimPicture.texture = picture;
            _aimWords.text = words;
            _aimCaption.text = caption;
            bool paced = paceLow >= 0f && paceHigh > paceLow;
            _aimPace.SetActive(paced);
            if (paced)
            {
                float barW = W - 32f;
                var tl2 = new Vector2(0, 1);
                Place(_aimPaceZone, tl2, tl2, new Vector2(barW * Mathf.Clamp01(paceLow), -6), new Vector2(barW * Mathf.Clamp01(paceHigh - paceLow), 14));
                _aimPaceNeedle.gameObject.SetActive(paceNow >= 0f);
                if (paceNow >= 0f) Place(_aimPaceNeedle, tl2, tl2, new Vector2(barW * Mathf.Clamp01(paceNow) - 2f, -1), new Vector2(4, 24));
                _aimPaceWords.text = paceWords ?? "";
            }
            ((RectTransform)_aimCard.transform).sizeDelta = new Vector2(W, 34 + picH + 44 + (paced ? 58 : 0));
            _aimCard.SetActive(true);
            _aimCard.transform.SetAsLastSibling();
        }

        /// <summary>Say where to putt from, above the prompt. Null or empty says nothing.</summary>
        public void SetTee(string text)
        {
            bool show = !string.IsNullOrEmpty(text) && _promptCard.activeSelf;
            // puttr-app-2: with the big tee sign up, this line would say the same distance and tee a second time, in words.
            if ((_teeSign != null && _teeSign.activeSelf) || (_teeChip != null && _teeChip.activeSelf)) show = false;
            _teeCard.SetActive(show);
            if (!show) return;
            _tee.text = "PUTT FROM   " + text.ToUpperInvariant();
            _tee.horizontalOverflow = HorizontalWrapMode.Overflow;
            var card = (RectTransform)_teeCard.transform;
            card.sizeDelta = new Vector2(_tee.preferredWidth + 56f, card.sizeDelta.y);
            card.anchoredPosition = new Vector2(((RectTransform)_promptCard.transform).anchoredPosition.x, card.anchoredPosition.y);
        }

        void BuildBanner()
        {
            _bannerBand = Solid("Banner band", _root, Color.clear);
            RectTransform band = _bannerBand.rectTransform;
            band.anchorMin = new Vector2(0f, 0.5f);
            band.anchorMax = new Vector2(1f, 0.5f);
            band.pivot = new Vector2(0.5f, 0.5f);
            band.anchoredPosition = new Vector2(0, -256);
            band.sizeDelta = new Vector2(0, 250);
            _bannerBand.raycastTarget = false;
            _banner = Label("Banner", _root, "", _bold, 150, Fg1, TextAnchor.MiddleCenter);
            Place(_banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -230), new Vector2(1600, 180));
            Shade(_banner, 5);
            _bannerSub = Label("Banner sub", _root, "", _medium, 32, Fg1, TextAnchor.MiddleCenter);
            Place(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -330), new Vector2(1600, 50));
            Shade(_bannerSub, 2);
            _banner.color = _bannerSub.color = Color.clear;
        }

        void BuildOverlay()
        {
            Image dim = Solid("Overlay", _root, new Color(0, 0, 0, 0.94f)); // blended in linear light, so this still leaves the game faintly in view
            Stretch(dim.rectTransform);
            Bleed(dim);
            _overlay = dim.gameObject;
            Tappable(dim, "overlay");

            Image card = Card("Card", dim.transform);
            _overlayCard = card.rectTransform;
            card.color = new Color(0.015f, 0.016f, 0.018f, 1f); // solid: big figures in the scene must not read through it
            Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 560));
            if (_logo != null)
            {
                float w = 380f;
                _overlayLogo = Raw("Logo", card.transform, _logo);
                Place(_overlayLogo.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(w, w * _logo.height / _logo.width));
            }
            _overlayTitle = Label("Title", card.transform, "", _light, 54, Fg1, TextAnchor.UpperCenter);
            Place(_overlayTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -176), new Vector2(820, 70));
            _overlayBody = Label("Body", card.transform, "", _regular, 26, Fg2, TextAnchor.UpperCenter);
            _overlayBody.lineSpacing = 1.3f;
            Place(_overlayBody.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -270), new Vector2(800, 260));
            for (int i = 0; i < _overlayButton.Length; i++)
            {
                _overlayButton[i] = Panel("Button " + i, card.transform, i == 0 ? Make.Green : new Color(1f, 1f, 1f, 0.07f));
                Tappable(_overlayButton[i], "button:" + i);
                _overlayButtonText[i] = Label("Text", _overlayButton[i].transform, "", _bold, 24, Fg1, TextAnchor.MiddleCenter);
                Stretch(_overlayButtonText[i].rectTransform);
            }
            _overlay.SetActive(false);
        }

        // ── The card at the end of a game, and the one for a pause ──────────

        /// <summary>One line of a result: a Player and what they scored.</summary>
        public struct Line
        {
            public string Name, Value, Tag;
            public Color Dot;
            public bool Lit;
        }

        /// <summary>What a summary card says, top to bottom. Anything left empty is left out.</summary>
        public struct Summary
        {
            public string Icon;          // the game's icon
            public Color Accent;
            public string Eyebrow;       // "GAME OVER"
            public string Figure, Unit;  // a big number and what it counts: "24", "FEET"
            public string Headline;      // or words in its place: "Matt wins"
            public string[] Notes;       // a line or two under it
            public Line[] Players;       // every Player's result, when there are several
            public string BoardCaption;
            public Line[] Board;         // the best scores on this device
            public string Hint;
            public string[] Buttons;
        }

        RectTransform _summaryCard, _summaryBody;
        readonly Image[] _summaryButton = new Image[3];
        readonly Text[] _summaryButtonText = new Text[3];

        void BuildSummary()
        {
            Image card = Card("Summary", _overlay.transform);
            card.color = new Color(0.015f, 0.016f, 0.018f, 1f);
            _summaryCard = card.rectTransform;
            Place(_summaryCard, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860, 600));
            for (int i = 0; i < _summaryButton.Length; i++)
            {
                _summaryButton[i] = Panel("Button " + i, card.transform, i == 0 ? Make.Green : new Color(1f, 1f, 1f, 0.035f)); // linear blending: this reads as a mid grey
                Tappable(_summaryButton[i], "button:" + i);
                _summaryButtonText[i] = Label("Text", _summaryButton[i].transform, "", _bold, (int)Sized(24, 27), Fg1, TextAnchor.MiddleCenter);
                Stretch(_summaryButtonText[i].rectTransform);
            }
            _summaryCard.gameObject.SetActive(false);
        }

        /// <summary>Show a summary card over the game: laid out from what it has to say, so nothing floats.</summary>
        public void ShowSummary(Summary card)
        {
            if (_summaryCard == null) BuildSummary();
            if (_summaryBody != null) Make.Kill(_summaryBody.gameObject);
            _summaryBody = Node("Body", _summaryCard);
            Stretch(_summaryBody);
            _summaryBody.SetAsFirstSibling();
            Transform t = _summaryBody;

            const float wide = 860f, pad = 48f, inner = wide - pad * 2;
            var top = new Vector2(0.5f, 1);
            var left = new Vector2(0, 1);
            float y = 44f;

            Texture2D icon = string.IsNullOrEmpty(card.Icon) ? null : IconTexture(card.Icon, false);
            if (icon != null)
            {
                Image bubble = Panel("Bubble", t, Faded(card.Accent, 0.22f));
                Place(bubble.rectTransform, top, top, new Vector2(0, -y), new Vector2(64, 64));
                RawImage image = Raw("Icon", bubble.transform, icon);
                image.color = card.Accent;
                Place(image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
                y += 64 + 20;
            }
            if (!string.IsNullOrEmpty(card.Eyebrow))
            {
                Text eyebrow = Label("Eyebrow", t, Spaced(card.Eyebrow), _bold, 15, Make.Bright, TextAnchor.MiddleCenter);
                eyebrow.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(eyebrow.rectTransform, top, top, new Vector2(0, -y), new Vector2(inner, 22));
                y += 22 + 10;
            }
            if (!string.IsNullOrEmpty(card.Figure))
            {
                Text figure = Label("Figure", t, card.Figure, _bold, 128, Fg1, TextAnchor.MiddleCenter);
                figure.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(figure.rectTransform, top, top, new Vector2(0, -y), new Vector2(inner, 132));
                y += 132;
                if (!string.IsNullOrEmpty(card.Unit))
                {
                    Text unit = Label("Unit", t, Spaced(card.Unit), _bold, 20, Fg2, TextAnchor.MiddleCenter);
                    unit.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(unit.rectTransform, top, top, new Vector2(0, -y), new Vector2(inner, 30));
                    y += 30;
                }
                y += 10;
            }
            else if (!string.IsNullOrEmpty(card.Headline))
            {
                Text headline = Label("Headline", t, card.Headline, _bold, 60, Fg1, TextAnchor.MiddleCenter);
                headline.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(headline.rectTransform, top, top, new Vector2(0, -y), new Vector2(inner, 76));
                y += 76 + 8;
            }
            if (card.Notes != null)
                foreach (string note in card.Notes)
                {
                    if (string.IsNullOrEmpty(note)) continue;
                    Text line = Label("Note", t, note, _medium, 24, new Color(1f, 1f, 1f, 0.78f), TextAnchor.MiddleCenter);
                    line.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(line.rectTransform, top, top, new Vector2(0, -y), new Vector2(inner, 34));
                    y += 34;
                }

            // Every Player's result, one to a row.
            if (card.Players != null && card.Players.Length > 0)
            {
                y += 22;
                foreach (Line who in card.Players)
                {
                    Image row = Panel("Player", t, who.Lit ? new Color(0.176f, 0.549f, 0.235f, 0.16f) : new Color(1f, 1f, 1f, 0.022f));
                    Place(row.rectTransform, left, left, new Vector2(pad, -y), new Vector2(inner, 60));
                    Image dot = Solid("Dot", row.transform, who.Dot);
                    dot.sprite = _circle;
                    Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(14, 14));
                    Text name = Label("Name", row.transform, who.Name, _medium, 25, Fg1, TextAnchor.MiddleLeft);
                    name.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(52, 0), new Vector2(360, 40));
                    Text value = Label("Value", row.transform, who.Value, _bold, 27, Fg1, TextAnchor.MiddleRight);
                    value.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(value.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(240, 40));
                    if (!string.IsNullOrEmpty(who.Tag))
                    {
                        Image tag = Panel("Tag", row.transform, who.Lit ? Make.Green : new Color(1f, 1f, 1f, 0.08f));
                        Place(tag.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-280, 0), new Vector2(118, 32));
                        Text word = Label("Text", tag.transform, who.Tag, _bold, 14, Fg1, TextAnchor.MiddleCenter);
                        Stretch(word.rectTransform);
                    }
                    y += 60 + 8;
                }
                y -= 8;
            }

            // The best on this device.
            if (card.Board != null && card.Board.Length > 0)
            {
                y += 28;
                Image rule = Solid("Rule", t, new Color(1f, 1f, 1f, 0.1f));
                Place(rule.rectTransform, left, left, new Vector2(pad, -y), new Vector2(inner, 1));
                y += 22;
                Text caption = Label("Caption", t, card.BoardCaption ?? "", _bold, 14, Fg3, TextAnchor.MiddleLeft);
                caption.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(caption.rectTransform, left, left, new Vector2(pad, -y), new Vector2(inner, 20));
                y += 20 + 10;
                int place = 1;
                foreach (Line best in card.Board)
                {
                    Color ink = best.Lit ? Make.Bright : Fg1;
                    Text rank = Label("Rank", t, place.ToString(), _bold, 20, Fg3, TextAnchor.MiddleLeft);
                    Place(rank.rectTransform, left, left, new Vector2(pad + 4, -y), new Vector2(40, 38));
                    Text name = Label("Name", t, best.Name + (best.Lit ? "     NEW" : ""), _medium, 22, ink, TextAnchor.MiddleLeft);
                    name.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(name.rectTransform, left, left, new Vector2(pad + 52, -y), new Vector2(460, 38));
                    Text score = Label("Score", t, best.Value, _bold, 22, ink, TextAnchor.MiddleRight);
                    score.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(score.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-pad - 4, -y), new Vector2(240, 38));
                    y += 38;
                    place++;
                }
            }

            if (!string.IsNullOrEmpty(card.Hint))
            {
                y += 22;
                Text hint = Label("Hint", t, card.Hint, _medium, 18, Fg3, TextAnchor.MiddleCenter);
                hint.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(hint.rectTransform, top, top, new Vector2(0, -y), new Vector2(inner, 26));
                y += 26;
            }

            // Buttons: one row, the width of everything above, the same gap all round.
            int n = Kind == Device.Tv || card.Buttons == null ? 0 : Mathf.Min(card.Buttons.Length, _summaryButton.Length);
            float high = Sized(72, 86), gap = 16f;
            if (n > 0) y += 28;
            float each = n > 0 ? (inner - gap * (n - 1)) / n : 0f;
            for (int i = 0; i < _summaryButton.Length; i++)
            {
                _summaryButton[i].gameObject.SetActive(i < n);
                if (i >= n) continue;
                _summaryButton[i].rectTransform.localScale = Vector3.one;
                Place(_summaryButton[i].rectTransform, left, left, new Vector2(pad + i * (each + gap), -y), new Vector2(each, high));
                _summaryButtonText[i].text = card.Buttons[i];
            }
            if (n > 0) y += high;
            y += pad;

            // Whatever the game last shouted ("STREAK OVER") is done with: the card has the floor.
            _bannerAge = 99f;
            _banner.color = _bannerSub.color = _bannerBand.color = Color.clear;
            _summaryCard.sizeDelta = new Vector2(wide, y);
            Arrange(); // puttr-app-2: fitted to an upright screen
            _summaryCard.gameObject.SetActive(true);
            _overlayCard.gameObject.SetActive(false);
            _pressed = null;
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
        }

        RectTransform _overlayCard;
        readonly Image[] _overlayButton = new Image[3];
        readonly Text[] _overlayButtonText = new Text[3];

        /// <summary>
        /// Lay out a score table: a row per Player, `columns` cells, and a
        /// total. Each cell has a small line on top and a bold one below.
        /// </summary>
        /// <summary>headed: the column numbers once, as headings over every row (innings), with
        /// totalHead over the totals and each cell's figure big in its middle. Otherwise each cell
        /// carries its own small number (bowling frames).</summary>
        public void BuildTable(IReadOnlyList<string> names, int columns, bool lastWide = false, float cellWidth = 78f, bool headed = false, string columnHead = null, string totalHead = null)
        {
            if (_scoreRoot != null) Make.Kill(_scoreRoot.gameObject);
            _rows.Clear();

            const float nameW = 150, totalW = 110, pad = 14, rowH = 66, gap = 6;
            float headH = headed ? 26f : 0f;
            float tenthW = lastWide ? cellWidth + 34f : cellWidth;
            float width = pad * 2 + nameW + cellWidth * (columns - 1) + tenthW + totalW;
            float height = pad * 2 + headH + names.Count * rowH + (names.Count - 1) * gap;

            Image card = Card("Scores", _root);
            _scoreRoot = card.rectTransform;
            // In the middle: clear of the game's name on the left and of the console status,
            // which is only as wide as what it says, on the right.
            Place(_scoreRoot, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0f, -28), new Vector2(width, height));
            _scoreRoot.SetSiblingIndex(0);

            if (headed)
            {
                // One line of headings: what the columns are, their numbers, and the total.
                var tlh = new Vector2(0, 1);
                if (columnHead != null)
                {
                    Text what = Label("Head", card.transform, columnHead, _bold, 13, Make.Bright, TextAnchor.MiddleLeft);
                    Place(what.rectTransform, tlh, tlh, new Vector2(pad + 18, -pad + 2), new Vector2(nameW - 18, headH));
                }
                float hx = pad + nameW;
                for (int f = 0; f < columns; f++)
                {
                    float w = f == columns - 1 ? tenthW : cellWidth;
                    Text head = Label("Head " + (f + 1), card.transform, (f + 1).ToString(), _bold, 16, Fg2, TextAnchor.MiddleCenter);
                    Place(head.rectTransform, tlh, tlh, new Vector2(hx, -pad + 2), new Vector2(w, headH));
                    hx += w;
                }
                if (totalHead != null)
                {
                    Text sum = Label("Head total", card.transform, totalHead, _bold, 13, Make.Bright, TextAnchor.MiddleCenter);
                    Place(sum.rectTransform, tlh, tlh, new Vector2(hx, -pad + 2), new Vector2(totalW, headH));
                }
            }

            for (int r = 0; r < names.Count; r++)
            {
                var row = new Row { Rings = new Image[columns], Tops = new Text[columns], Mains = new Text[columns] };
                RectTransform t = Node("Row", card.transform);
                Place(t, new Vector2(0, 1), new Vector2(0, 1), new Vector2(pad, -pad - headH - r * (rowH + gap)), new Vector2(width - pad * 2, rowH));

                row.Bar = Panel("Turn", t, Make.Bright);
                Place(row.Bar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(4, rowH - 22));
                row.Name = Label("Name", t, names[r], _medium, 22, Fg2, TextAnchor.MiddleLeft);
                Place(row.Name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(nameW - 18, rowH));

                float x = nameW;
                for (int f = 0; f < columns; f++)
                {
                    float w = f == columns - 1 ? tenthW : cellWidth;
                    Image cell = Panel("Cell " + (f + 1), t, Cell);
                    Place(cell.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 2, 0), new Vector2(w - 4, rowH - 2));
                    Transform c = cell.transform;
                    row.Rings[f] = Panel("Ring", c, Make.Bright);
                    row.Rings[f].sprite = _outline;
                    row.Rings[f].enabled = false;
                    Stretch(row.Rings[f].rectTransform);
                    if (!headed)
                    {
                        Text number = Label("Number", c, (f + 1).ToString(), _medium, 12, Fg3, TextAnchor.UpperLeft);
                        Place(number.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -6), new Vector2(30, 16));
                    }
                    row.Tops[f] = Label("Top", c, "", _medium, 20, Fg1, TextAnchor.UpperRight);
                    Place(row.Tops[f].rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-9, -5), new Vector2(w - 24, 26));
                    if (headed)
                    {
                        // The figure alone, big, in the middle of its cell.
                        row.Mains[f] = Label("Main", c, "", _bold, 38, Fg1, TextAnchor.MiddleCenter);
                        Stretch(row.Mains[f].rectTransform);
                    }
                    else
                    {
                        row.Mains[f] = Label("Main", c, "", _bold, 24, Fg1, TextAnchor.LowerCenter);
                        Place(row.Mains[f].rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 5), new Vector2(w - 8, 30));
                    }
                    x += w;
                }
                row.Total = Label("Sum", t, "0", _bold, headed ? 44 : 38, Fg1, TextAnchor.MiddleCenter);
                Place(row.Total.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(totalW, rowH));
                _rows.Add(row);
            }
        }

        /// <summary>What one mark on a board shows.</summary>
        public enum Pip { Empty, Good, Bad, Over }

        Text _boardTitle;

        /// <summary>
        /// A small board for games that do not need a table: an optional line
        /// on top, then for each Player a name, a row of round marks, and one
        /// number. A mark can be empty, good (solid green), bad (a red dot) or
        /// over (an outline), and can carry a figure.
        /// </summary>
        public void BuildBoard(IReadOnlyList<string> names, int pips, string totalLabel = null, bool titled = false, string pipLabel = null, float pip = 28f)
        {
            if (_scoreRoot != null) Make.Kill(_scoreRoot.gameObject);
            _rows.Clear();
            _boardTitle = null;

            const float pad = 18, totalW = 96;
            float pipGap = pips > 6 ? 8 : 14, rowH = pip + 18;
            bool many = names.Count > 1;
            float nameW = many ? 132 : 0;
            float titleH = titled ? 32 : 0, labelH = totalLabel != null || pipLabel != null ? 24 : 0;
            float head = titleH + labelH;
            float pipsW = pips * pip + (pips - 1) * pipGap;
            float width = Mathf.Max(pad * 2 + nameW + pipsW + 24 + totalW, titled ? 470f : 0f);
            float height = pad * 2 + head + names.Count * rowH - 6;

            Image card = Card("Scores", _root);
            _scoreRoot = card.rectTransform;
            Place(_scoreRoot, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(width, height));
            _scoreRoot.SetSiblingIndex(0);

            if (titled)
            {
                _boardTitle = Label("Title", card.transform, "", _medium, 16, Fg1, TextAnchor.MiddleCenter);
                _boardTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(_boardTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -pad + 3), new Vector2(width - pad * 2, 24));
                Image rule = Solid("Rule", card.transform, new Color(1f, 1f, 1f, 0.1f));
                Place(rule.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -pad - titleH + 6), new Vector2(width - pad * 2, 1));
            }
            // The marks and the number sit together in the middle of the card.
            float group = nameW + pipsW + 24 + totalW;
            float left = (width - group) * 0.5f;
            float pipsX = left + nameW;
            if (totalLabel != null)
            {
                Text what = Label("Total label", card.transform, totalLabel, _medium, 12, Fg3, TextAnchor.MiddleCenter);
                Place(what.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(pipsX + pipsW + 24 + totalW * 0.5f, -pad - titleH + 2), new Vector2(totalW, 20));
            }
            if (pipLabel != null)
            {
                Text what = Label("Mark label", card.transform, pipLabel, _medium, 12, Fg3, TextAnchor.MiddleCenter);
                what.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(what.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(pipsX + pipsW * 0.5f, -pad - titleH + 2), new Vector2(pipsW, 20));
            }

            for (int r = 0; r < names.Count; r++)
            {
                var row = new Row { Rings = new Image[pips], Tops = new Text[0], Mains = new Text[0], Pips = new Image[pips], PipDots = new Image[pips], PipTexts = new Text[pips] };
                RectTransform t = Node("Row", card.transform);
                Place(t, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -pad - head - r * rowH + 3), new Vector2(width, rowH - 6));

                row.Bar = Panel("Turn", t, Make.Bright);
                Place(row.Bar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(left - 10, 0), new Vector2(4, 24));
                row.Name = Label("Name", t, many ? names[r] : "", _medium, 20, Fg2, TextAnchor.MiddleLeft);
                Place(row.Name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(left + 4, 0), new Vector2(Mathf.Max(nameW, 10), rowH));

                for (int i = 0; i < pips; i++)
                {
                    var at = new Vector2(pipsX + i * (pip + pipGap) + pip * 0.5f, 0);
                    row.Rings[i] = Panel("Turn ring", t, new Color(1f, 1f, 1f, 0.8f));
                    row.Rings[i].sprite = _outline;
                    Place(row.Rings[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), at, new Vector2(pip + 10, pip + 10));
                    row.Rings[i].enabled = false;
                    row.Pips[i] = Panel("Mark", t, new Color(1f, 1f, 1f, 0.3f));
                    row.Pips[i].sprite = _outline;
                    Place(row.Pips[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), at, new Vector2(pip, pip));
                    row.PipDots[i] = Panel("Dot", t, Make.Red);
                    Place(row.PipDots[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), at, new Vector2(12, 12));
                    row.PipDots[i].enabled = false;
                    row.PipTexts[i] = Label("Figure", t, "", _bold, pip > 30 ? 14 : 16, Fg1, TextAnchor.MiddleCenter);
                    row.PipTexts[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(row.PipTexts[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), at + new Vector2(0, 1), new Vector2(pip, pip));
                }
                row.Total = Label("Sum", t, "0", _bold, 32, Fg1, TextAnchor.MiddleCenter);
                Place(row.Total.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(pipsX + pipsW + 24 + totalW * 0.5f, 0), new Vector2(totalW, rowH));
                _rows.Add(row);
            }
        }

        /// <summary>For a game that keeps its score in the scene: no board at all.</summary>
        public void ClearBoard()
        {
            if (_scoreRoot != null) Make.Kill(_scoreRoot.gameObject);
            _scoreRoot = null;
            _rows.Clear();
            _boardTitle = null;
        }

        /// <summary>The line across the top of a board.</summary>
        public void SetBoardTitle(string text)
        {
            if (_boardTitle != null) _boardTitle.text = text;
        }

        public void SetPip(int row, int index, Pip state, string figure = "")
        {
            if (row >= _rows.Count || _rows[row].Pips == null || index >= _rows[row].Pips.Length) return;
            Image mark = _rows[row].Pips[index];
            bool solid = state == Pip.Good;
            mark.sprite = solid ? _round : _outline;
            mark.color = solid ? Make.Bright : state == Pip.Over ? new Color(1f, 1f, 1f, 0.75f) : state == Pip.Bad ? new Color(1f, 0.32f, 0.32f, 0.55f) : new Color(1f, 1f, 1f, 0.3f);
            _rows[row].PipDots[index].enabled = state == Pip.Bad;
            Text text = _rows[row].PipTexts[index];
            text.text = state == Pip.Bad ? "" : figure ?? "";
            text.color = solid ? new Color(0.02f, 0.08f, 0.03f) : Fg1;
        }

        // ── Updates ─────────────────────────────────────────────────────────

        public void SetCell(int row, int column, string top, string main)
        {
            if (row >= _rows.Count || column >= _rows[row].Mains.Length) return;
            _rows[row].Tops[column].text = top ?? "";
            _rows[row].Mains[column].text = main ?? "";
        }

        public void SetTotal(int row, int total)
        {
            if (row < _rows.Count) _rows[row].Total.text = total.ToString();
        }

        /// <summary>Mark whose turn it is and which cell is in play (column -1 for none).</summary>
        public void SetTurn(int row, int column)
        {
            for (int r = 0; r < _rows.Count; r++)
            {
                bool on = r == row;
                _rows[r].Bar.enabled = on && _rows.Count > 1;
                _rows[r].Name.color = on ? Fg1 : Fg3;
                for (int c = 0; c < _rows[r].Rings.Length; c++) _rows[r].Rings[c].enabled = on && c == column;
            }
        }

        public void SetScoreVisible(bool visible)
        {
            if (_scoreRoot != null) _scoreRoot.gameObject.SetActive(visible);
            PlaceStatus();
        }

        /// <summary>The name beside the logo, top left.</summary>
        public void SetGameName(string spaced) { _gameName.text = spaced; FitGameName(); }

        // puttr-app-2 (2026-11-23): a long name ("FULL WORKOUT", "AROUND THE CLOCK") ran on under the pause button. The
        // name is drawn smaller until it ends short of the button; never under 13.
        string _nameFitFor;
        void FitGameName()
        {
            if (_gameName == null || _brand == null || _root == null) return;
            // Only when the name or the room for it has changed (this is asked every frame: the screen's width is not known
            // when the name is first set).
            string key = _gameName.text + "|" + _root.rect.width.ToString("0") + "|" + _brand.localScale.x.ToString("0.00") + "|" + (_menuRect != null ? _menuRect.localScale.x.ToString("0.00") : "");
            if (key == _nameFitFor) return;
            _nameFitFor = key;
            _gameName.fontSize = 26;
            float scale = Mathf.Max(0.01f, _brand.localScale.x);
            float menu = _menuRect != null && _menuButton != null ? _menuBuilt * _menuRect.localScale.x - _menuRect.anchoredPosition.x + 14f : 20f;
            float room = (_root.rect.width - _brand.anchoredPosition.x - menu) / scale - _gameName.rectTransform.anchoredPosition.x;
            float wide = _gameName.preferredWidth;
            if (room > 40f && wide > room) _gameName.fontSize = Mathf.Max(13, Mathf.FloorToInt(26f * room / wide));
        }

        // ── Home ────────────────────────────────────────────────────────────

        // ── Home ────────────────────────────────────────────────────────────

        // The app's 6% white, in this project's linear color.
        static readonly Color CardFill = new Color(1f, 1f, 1f, 0.022f);
        public static readonly Color[] PlayerColors = { Make.Hex(0x5687FF), Make.Hex(0xFFB300), Make.Hex(0xFF5252), Make.Hex(0x2D8C3C) };
        Sprite _circle;

        static Texture2D IconTexture(string name, bool fill) => Resources.Load<Texture2D>("PuttrArcade/Icons/" + (fill ? "IconFill" : "Icon") + name);

        static Color Faded(Color c, float alpha) { c.a = alpha; return c; }

        // A chip: small muted words on a faint rounded ground. Returns its width.
        float Chip(Transform parent, string text, Vector2 at, Color ground, Color ink, Font font, int size = 15)
        {
            float w = 22f + text.Length * size * 0.6f;
            Image chip = Panel("Chip", parent, ground);
            Place(chip.rectTransform, new Vector2(0, 0), new Vector2(0, 0), at, new Vector2(w, size + 14));
            Text label = Label("Text", chip.transform, text, font, size, ink, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(label.rectTransform);
            return w;
        }

        IReadOnlyList<GameMode> _homeGames;
        readonly List<Image> _homeTiles = new List<Image>();
        readonly List<Text> _homeTileNames = new List<Text>();
        readonly List<RawImage> _homeTilePictures = new List<RawImage>();
        int _homeSelected = -1;

        /// <summary>Give a game's card its picture.</summary>
        public void SetCard(int index, Texture picture)
        {
            if (index < 0 || index >= _homeTilePictures.Count) return;
            _homeTilePictures[index].texture = picture;
            _homeTilePictures[index].color = index == _homeSelected ? Color.white : CardDim;
        }

        static readonly Color CardDim = new Color(0.82f, 0.82f, 0.82f, 1f); // a card that is not chosen sits back a little

        // Clear at the top, solid at the bottom: laid over a picture so words on it can be read.
        static Texture2D _fadeUp;
        static Texture2D FadeUp()
        {
            if (_fadeUp != null) return _fadeUp;
            _fadeUp = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
            {
                float t = 1f - y / 63f;
                // Blended in linear light, a half-clear black hardly darkens: the fade is kept
                // nearly solid under the words and lets go quickly above them.
                _fadeUp.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(t * 1.35f), 0.35f) * Mathf.SmoothStep(0f, 1f, t * 3f)));
            }
            _fadeUp.Apply();
            return _fadeUp;
        }
        Text _homeCount, _homeTitle, _homeBlurb, _homePlay, _homeBoard;
        Image _homeBubble;
        RawImage _homeIcon;
        RectTransform _homeChips, _brand;

        // A wash of black that fades out across the screen, so words on the left
        // stay sharp over whatever game is showing behind them.
        static Sprite Fade(bool across)
        {
            const int n = 256;
            var tex = new Texture2D(across ? n : 1, across ? 1 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float a = across ? Mathf.SmoothStep(0.97f, 0f, Mathf.InverseLerp(0.18f, 0.72f, t)) : Mathf.SmoothStep(0.96f, 0f, Mathf.InverseLerp(0.1f, 0.42f, t));
                tex.SetPixel(across ? i : 0, across ? 0 : i, new Color(0, 0, 0, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// The home screen. The highlighted game is live behind it; its name,
        /// what it is and how many can play are on the left, in the Puttr app's
        /// type and colors; every game is a tile along the bottom.
        /// </summary>
        public void BuildHome(IReadOnlyList<GameMode> games)
        {
            _homeGames = games;
            _circle = Make.RoundedSprite(64);
            RectTransform home = Node("Home", _root);
            Stretch(home);
            _home = home.gameObject;

            Image left = Solid("Shade, left", home, Color.white);
            left.sprite = Fade(true);
            Stretch(left.rectTransform);
            Bleed(left);
            Image low = Solid("Shade, bottom", home, Color.white);
            low.sprite = Fade(false);
            Stretch(low.rectTransform);
            Bleed(low);

            var topLeft = new Vector2(0, 1);
            const float x = 64f;
            const float lift = 56f; // the hero sits this much higher, so Play keeps clear of the row of games

            _homeBubble = Panel("Bubble", home, CardFill);
            Place(_homeBubble.rectTransform, topLeft, topLeft, new Vector2(x, -212 + lift), new Vector2(76, 76));
            _homeIcon = Raw("Icon", _homeBubble.transform, null);
            Place(_homeIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42, 42));
            _homeCount = Label("Count", home, "", _bold, 18, Make.Bright, TextAnchor.MiddleLeft);
            _homeCount.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(_homeCount.rectTransform, topLeft, topLeft, new Vector2(x + 96, -236 + lift), new Vector2(600, 28));

            _homeTitle = Label("Title", home, "", _bold, 104, Fg1, TextAnchor.UpperLeft);
            _homeTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(_homeTitle.rectTransform, topLeft, topLeft, new Vector2(x - 4, -304 + lift), new Vector2(1200, 130));
            _homeBlurb = Label("Blurb", home, "", _regular, 30, new Color(1f, 1f, 1f, 0.86f), TextAnchor.UpperLeft);
            _homeBlurb.lineSpacing = 1.28f;
            _homeBlurb.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(_homeBlurb.rectTransform, topLeft, topLeft, new Vector2(x, -452 + lift), new Vector2(900, 140));
            _homeChips = Node("Chips", home);
            Place(_homeChips, topLeft, new Vector2(0, 0), new Vector2(x, -640 + lift), new Vector2(900, 40));

            Image play = Panel("Play", home, Make.Green);
            float playW = Sized(380, 300), playH = Sized(70, 84);
            Place(play.rectTransform, topLeft, topLeft, new Vector2(x, -676 + lift), new Vector2(playW, playH));
            Tappable(play, "play");
            _homePlay = Label("Text", play.transform, For("Putt center to play", "Play", "Putt center to play"), _bold, (int)Sized(25, 30), Fg1, TextAnchor.MiddleCenter);
            Stretch(_homePlay.rectTransform);
            Text browse = Label("Browse", home, For("Putt left or right of the hole to browse, or click a game",
                "or putt center to play\nSwipe to see the other games",
                "Putt left or right of the hole to browse, or use the remote"), _medium, 21, Fg2, TextAnchor.MiddleLeft);
            browse.lineSpacing = 1.2f;
            browse.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(browse.rectTransform, topLeft, topLeft, new Vector2(x + playW + 28, -676 + lift - (playH - 60) * 0.5f), new Vector2(700, 60));

            _homeBoard = Label("Board", home, "", _medium, 20, Fg2, TextAnchor.UpperLeft);
            _homeBoard.lineSpacing = 1.25f;
            _homeBoard.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(_homeBoard.rectTransform, topLeft, topLeft, new Vector2(x, Sized(-776, -796) + lift), new Vector2(900, 120));

            // Settings: a cog, top right.
            Image cog = Panel("Settings", home, new Color(1f, 1f, 1f, 0.06f));
            float cogSize = Sized(56f, 76f);
            Place(cog.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -32), new Vector2(cogSize, cogSize));
            Image cogEdge = Panel("Edge", cog.transform, new Color(1f, 1f, 1f, 0.12f));
            cogEdge.sprite = _outline;
            Stretch(cogEdge.rectTransform);
            Texture2D gear = IconTexture("Gear", false);
            if (gear != null)
            {
                RawImage image = Raw("Icon", cog.transform, gear);
                Place(image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cogSize * 0.54f, cogSize * 0.54f));
            }
            Tappable(cog, "settings");
            if (Kind == Device.Tv)
            {
                Text up = Label("Hint", home, "Up for settings", _medium, 17, Fg3, TextAnchor.MiddleRight);
                Place(up.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -94), new Vector2(300, 26));
            }

            // Every game, along the bottom.
            // The row runs the full width of the screen and slides under the
            // finger; where every game fits (a mouse, a remote) it stays put.
            int n = games.Count;
            const float gap = RailGap;
            float tileH = Sized(156f, 180f);
            float tileW = Fingers ? 300f : 250f; // more games than fit: the row slides to the one chosen
            _tileW = tileW;
            _railWidth = RailPad * 2 + n * tileW + (n - 1) * gap;
            _railView = Node("Rail", home);
            _railView.anchorMin = new Vector2(0, 0);
            _railView.anchorMax = new Vector2(1, 0);
            _railView.pivot = new Vector2(0, 0);
            _railView.anchoredPosition = new Vector2(0, 56 - RailLift);
            _railView.sizeDelta = new Vector2(0, tileH + RailLift * 2);
            _railView.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(40, 0);
            _rail = Node("Tiles", _railView);
            Place(_rail, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, RailLift), new Vector2(_railWidth, tileH));
            for (int i = 0; i < n; i++)
            {
                GameMode game = games[i];
                Image tile = Panel("Tile " + game.Name, _rail, new Color(0.02f, 0.023f, 0.025f, 0.94f));
                Place(tile.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(RailPad + i * (tileW + gap) + tileW * 0.5f, 0), new Vector2(tileW, tileH));
                Tappable(tile, "tile:" + i);
                // The card is a picture of the game, cut to the card's rounded corners, with
                // its name on a dark fade along the bottom.
                tile.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                RawImage picture = Raw("Picture", tile.transform, null);
                Stretch(picture.rectTransform);
                picture.color = Color.clear; // until its picture is taken
                _homeTilePictures.Add(picture);
                RawImage fade = Raw("Fade", tile.transform, FadeUp());
                fade.color = new Color(0f, 0f, 0f, 0.96f);
                fade.rectTransform.anchorMin = new Vector2(0, 0);
                fade.rectTransform.anchorMax = new Vector2(1, 0.52f);
                fade.rectTransform.offsetMin = fade.rectTransform.offsetMax = Vector2.zero;
                Image edge = Panel("Edge", tile.transform, new Color(1f, 1f, 1f, 0.1f));
                edge.sprite = _outline;
                Stretch(edge.rectTransform);
                _homeTiles.Add(edge);
                Text name = Label("Name", tile.transform, game.Name, _bold, (int)Sized(22, 25), Fg1, TextAnchor.LowerLeft);
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(name.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, Sized(30, 34)), new Vector2(tileW - 24, 32));
                Shade(name, 2);
                Text who = Label("Players", tile.transform, game.MaxPlayers == 1 ? "Solo" : "1 to " + game.MaxPlayers + " players", _medium, (int)Sized(15, 17), new Color(1f, 1f, 1f, 0.78f), TextAnchor.LowerLeft);
                who.horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(who.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, Sized(10, 12)), new Vector2(tileW - 24, 22));
                _homeTileNames.Add(name);
            }
            _home.SetActive(false);
        }

        public void ShowHome(int selected, string board)
        {
            _homeSelected = selected;
            // The cards are tall: on a wide screen there is room above them for the best score only.
            if (!string.IsNullOrEmpty(board) && _root.rect.height < 1150f) board = board.Split('\n')[0];
            _homeBoard.text = string.IsNullOrEmpty(board) ? "" : "TOP SCORES ON THIS DEVICE\n" + board;
            _home.SetActive(true);
            _home.transform.SetAsLastSibling();
            if (_brand != null) _brand.SetAsLastSibling();
            GameMode game = _homeGames[selected];
            Color accent = game.Accent;
            _homeBubble.color = Faded(accent, 0.26f);
            _homeIcon.texture = IconTexture(game.Icon, false);
            _homeIcon.color = accent;
            _homeIcon.enabled = _homeIcon.texture != null;
            // No "game n of 14": the line only marks the featured game, so it never goes stale as games are added.
            _homeCount.text = selected == 0 ? "FEATURED" : "";
            _homeTitle.text = game.Name;
            _homeBlurb.text = game.Blurb;

            for (int i = _homeChips.childCount - 1; i >= 0; i--) Make.Kill(_homeChips.GetChild(i).gameObject);
            float tx = 0f;
            var ground = new Color(1f, 1f, 1f, 0.07f);
            var ink = new Color(1f, 1f, 1f, 0.8f);
            foreach (string tag in game.Tags) tx += Chip(_homeChips, tag, new Vector2(tx, 0), ground, ink, _medium, 18) + 8f;
            tx += Chip(_homeChips, game.MaxPlayers == 1 ? "Solo" : "1 to " + game.MaxPlayers + " players", new Vector2(tx, 0), ground, ink, _medium, 18) + 8f;

            for (int i = 0; i < _homeTiles.Count; i++)
            {
                bool on = i == selected;
                _homeTiles[i].color = on ? Make.Bright : new Color(1f, 1f, 1f, 0.1f);
                _homeTiles[i].transform.parent.GetComponent<Image>().color = on ? new Color(0.03f, 0.1f, 0.05f, 0.97f) : new Color(0.02f, 0.023f, 0.025f, 0.94f);
                _homeTiles[i].transform.parent.localScale = Vector3.one * (on ? 1.04f : 1f);
                _homeTileNames[i].color = on ? Fg1 : Fg2;
                if (_homeTilePictures[i].texture != null) _homeTilePictures[i].color = on ? Color.white : CardDim;
            }
            _pressed = null; // the scales above are now the true ones
            // Only a change of game moves the row: redrawing the same one must not pull it
            // back from where a finger has just left it.
            if (selected != _revealed) { _revealed = selected; Reveal(selected); }
        }

        public void HideHome() => _home.SetActive(false);

        // ── Settings: which console ─────────────────────────────────────────

        /// <summary>One console on the settings screen.</summary>
        public struct ConsoleRow
        {
            public string Title, Detail, Badge;
            public bool Connected, Faint;
        }

        const int SettingsRows = 5;
        GameObject _settings;
        readonly Text[] _settingsHead = new Text[2];
        readonly Image[,] _settingsRow = new Image[2, SettingsRows], _settingsEdge = new Image[2, SettingsRows], _settingsBadge = new Image[2, SettingsRows];
        readonly Text[,] _settingsTitle = new Text[2, SettingsRows], _settingsDetail = new Text[2, SettingsRows], _settingsBadgeText = new Text[2, SettingsRows];
        readonly Text[] _settingsEmpty = new Text[2];

        /// <summary>The settings screen: every Puttr in range over Bluetooth, and every one answering on WiFi.</summary>
        public void BuildSettings()
        {
            Image dim = Solid("Settings", _root, new Color(0, 0, 0, 0.997f));
            Stretch(dim.rectTransform);
            Bleed(dim);
            _settings = dim.gameObject;
            Transform t = dim.transform;
            var top = new Vector2(0.5f, 1);

            Text title = Label("Title", t, "Your Puttr", _bold, 48, Fg1, TextAnchor.MiddleCenter);
            Place(title.rectTransform, top, top, new Vector2(0, -118), new Vector2(1200, 60));
            Text sub = Label("Sub", t, "Putts can come over Bluetooth or WiFi. Choose the console to play on.", _regular, 22, Fg2, TextAnchor.MiddleCenter);
            Place(sub.rectTransform, top, top, new Vector2(0, -184), new Vector2(1400, 32));

            string[] names = { "Bluetooth", "WiFi" }, icons = { "Bluetooth", "WifiHigh" };
            const float w = 800f, h = 600f, rowH = 84f;
            for (int c = 0; c < 2; c++)
            {
                Image card = Panel(names[c], t, CardFill);
                Place(card.rectTransform, top, top, new Vector2((c == 0 ? -1 : 1) * (w * 0.5f + 16f), -250), new Vector2(w, h));
                Image bubble = Panel("Bubble", card.transform, new Color(0.176f, 0.549f, 0.235f, 0.3f));
                Place(bubble.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -24), new Vector2(60, 60));
                Texture2D icon = IconTexture(icons[c], false);
                if (icon != null)
                {
                    RawImage image = Raw("Icon", bubble.transform, icon);
                    image.color = Make.Bright;
                    Place(image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
                }
                Text name = Label("Name", card.transform, names[c], _bold, 28, Fg1, TextAnchor.MiddleLeft);
                Place(name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, -22), new Vector2(400, 36));
                _settingsHead[c] = Label("State", card.transform, "", _medium, 18, Fg3, TextAnchor.MiddleLeft);
                _settingsHead[c].horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(_settingsHead[c].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, -56), new Vector2(660, 26));
                _settingsEmpty[c] = Label("Empty", card.transform, "", _regular, 21, Fg3, TextAnchor.UpperLeft);
                _settingsEmpty[c].lineSpacing = 1.25f;
                Place(_settingsEmpty[c].rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -124), new Vector2(w - 56, 300));

                for (int r = 0; r < SettingsRows; r++)
                {
                    Image row = Panel("Row", card.transform, new Color(1f, 1f, 1f, 0.03f));
                    Place(row.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -112 - r * (rowH + 10)), new Vector2(w - 44, rowH));
                    _settingsRow[c, r] = row;
                    Tappable(row, "console:" + c + ":" + r);
                    Image edge = Panel("Edge", row.transform, new Color(1f, 1f, 1f, 0.07f));
                    edge.sprite = _outline;
                    Stretch(edge.rectTransform);
                    _settingsEdge[c, r] = edge;
                    _settingsTitle[c, r] = Label("Title", row.transform, "", _bold, 24, Fg1, TextAnchor.MiddleLeft);
                    _settingsTitle[c, r].horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(_settingsTitle[c, r].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(24, 0), new Vector2(480, 34));
                    _settingsDetail[c, r] = Label("Detail", row.transform, "", _medium, 17, Fg3, TextAnchor.MiddleLeft);
                    _settingsDetail[c, r].horizontalOverflow = HorizontalWrapMode.Overflow;
                    Place(_settingsDetail[c, r].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 1), new Vector2(24, -2), new Vector2(480, 26));
                    Image badge = Panel("Badge", row.transform, new Color(0.176f, 0.549f, 0.235f, 0.3f));
                    Place(badge.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(170, 36));
                    _settingsBadge[c, r] = badge;
                    _settingsBadgeText[c, r] = Label("Text", badge.transform, "", _bold, 14, Make.Bright, TextAnchor.MiddleCenter);
                    Stretch(_settingsBadgeText[c, r].rectTransform);
                }
            }
            BackButton(t);
            Text hint = Label("Hint", t, For("Click a console, or choose with the arrows and Enter      ·      Esc to go back",
                "Tap a console to use it",
                "Choose a console with the remote      ·      Menu to go back"), _medium, 19, Fg3, TextAnchor.MiddleCenter);
            Place(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 96), new Vector2(1700, 30));
            _settings.SetActive(false);
        }

        /// <summary>
        /// Show the consoles. column and row are the highlight; empty is what
        /// to say in a column that has no rows.
        /// </summary>
        public void ShowSettings(string[] heads, IReadOnlyList<ConsoleRow>[] rows, string[] empty, int column, int row)
        {
            _settings.SetActive(true);
            _settings.transform.SetAsLastSibling();
            for (int c = 0; c < 2; c++)
            {
                _settingsHead[c].text = heads[c];
                _settingsEmpty[c].text = rows[c].Count == 0 ? empty[c] : "";
                for (int r = 0; r < SettingsRows; r++)
                {
                    bool used = r < rows[c].Count;
                    _settingsRow[c, r].gameObject.SetActive(used);
                    if (!used) continue;
                    ConsoleRow item = rows[c][r];
                    bool on = c == column && r == row;
                    _settingsEdge[c, r].color = on ? Make.Bright : new Color(1f, 1f, 1f, 0.07f);
                    _settingsRow[c, r].color = on ? new Color(0.176f, 0.549f, 0.235f, 0.12f) : new Color(1f, 1f, 1f, 0.03f);
                    _settingsTitle[c, r].text = item.Title;
                    _settingsTitle[c, r].color = item.Faint ? Fg3 : Fg1;
                    _settingsDetail[c, r].text = item.Detail;
                    bool badge = !string.IsNullOrEmpty(item.Badge);
                    _settingsBadge[c, r].gameObject.SetActive(badge);
                    _settingsBadge[c, r].color = item.Connected ? new Color(0.176f, 0.549f, 0.235f, 0.35f) : new Color(1f, 1f, 1f, 0.05f);
                    _settingsBadgeText[c, r].text = item.Badge ?? "";
                    _settingsBadgeText[c, r].color = item.Connected ? Make.Bright : Fg2;
                }
            }
        }

        public void HideSettings()
        {
            if (_settings != null) _settings.SetActive(false);
        }

        // ── Setup ───────────────────────────────────────────────────────────

        GameObject _setup;
        RawImage _setupIcon;
        Image _setupBubble;
        Text _setupTitle, _setupRule, _setupPlayersCaption, _setupHint;
        readonly Text[] _setupHow = new Text[3];
        readonly GameObject[] _setupHowStep = new GameObject[3];
        readonly Image[] _setupField = new Image[4], _setupFieldEdge = new Image[4], _setupRemove = new Image[4];
        readonly Text[] _setupName = new Text[4];
        Image _setupAdd;
        Text _setupAddText, _setupLevelNote, _setupBoardCaption, _setupVsCaption, _setupVs;
        readonly Text[] _setupBoardName = new Text[5], _setupBoardScore = new Text[5];
        Image _setupGoEdge, _setupGo;
        Text _setupGoText;
        readonly Dictionary<Image, Image> _focusRings = new Dictionary<Image, Image>();
        Sprite _focusSprite;

        // With a remote or keys, what is highlighted has to read from across the room: a thick
        // white ring outside it, a little bigger than its neighbours. (A thin edge was all there
        // was, and Let's play is green either way, so a move off it was easy to miss.)
        void Focus(Image item, bool on)
        {
            if (item == null) return;
            if (!_focusRings.TryGetValue(item, out Image ring))
            {
                // A crisp 3 px white line drawn at its own size (stretching the thin outline
                // made it thick and soft), a few pixels outside the item.
                if (_focusSprite == null) _focusSprite = Make.RoundedSprite(18, true, 3f);
                ring = Panel("Focus", item.transform, Color.white);
                ring.sprite = _focusSprite;
                RectTransform r = ring.rectTransform;
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(-5f, -5f); r.offsetMax = new Vector2(5f, 5f);
                _focusRings[item] = ring;
            }
            ring.gameObject.SetActive(on);
            item.rectTransform.localScale = on ? new Vector3(1.04f, 1.04f, 1f) : Vector3.one;
        }
        readonly Image[] _setupChip = new Image[4], _setupChipEdge = new Image[4], _setupRemoveEdge = new Image[4];
        readonly Text[] _setupChipText = new Text[4];

        /// <summary>
        /// The screen before a game, as in the Puttr app: what the game is, how
        /// to play it, how many Players, their names, and "Let's play".
        /// </summary>
        public void BuildSetup()
        {
            Image dim = Solid("Setup", _root, new Color(0, 0, 0, 0.997f)); // blended in linear light: 0.3% left over still reads as a faint scene
            Stretch(dim.rectTransform);
            Bleed(dim);
            _setup = dim.gameObject;
            Transform t = dim.transform;

            // Every screen is landscape, so the two halves sit side by side around the middle:
            // what the game is and how it is played on the left, who is playing on the right.
            var block = Node("Block", t);
            Place(block, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(SetupWide, SetupTall));
            var tl = new Vector2(0, 1);

            // Left: the game.
            _setupBubble = Panel("Bubble", block, CardFill);
            Place(_setupBubble.rectTransform, tl, tl, new Vector2(0, 0), new Vector2(76, 76));
            _setupIcon = Raw("Icon", _setupBubble.transform, null);
            Place(_setupIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
            _setupTitle = Label("Title", block, "", _bold, 50, Fg1, TextAnchor.MiddleLeft);
            Place(_setupTitle.rectTransform, tl, tl, new Vector2(98, -6), new Vector2(SetupLeft - 98, 64));
            _setupRule = Label("Rule", block, "", _regular, 23, Fg2, TextAnchor.UpperLeft);
            _setupRule.lineSpacing = 1.12f;
            Place(_setupRule.rectTransform, tl, tl, new Vector2(0, -98), new Vector2(SetupLeft, 64));

            // How to play: three steps, each a box of its own, black, with its number in a
            // green badge, so each reads on its own from across the room.
            var how = Node("How to play", block);
            Place(how, tl, tl, new Vector2(0, -180), new Vector2(SetupLeft, SetupTall - 180));
            Text caption = Label("Caption", how, "HOW TO PLAY", _bold, 14, Make.Bright, TextAnchor.MiddleLeft);
            Place(caption.rectTransform, tl, tl, new Vector2(2, 0), new Vector2(300, 22));
            const float stepTop = 34f, stepGap = 14f;
            float each = (SetupTall - 180 - stepTop - 2 * stepGap) / 3f;
            for (int i = 0; i < 3; i++)
            {
                Image step = Panel("Step " + (i + 1), how, new Color(0f, 0f, 0f, 0.86f));
                Image edge = Panel("Border", step.transform, Border);
                edge.sprite = _outline;
                Stretch(edge.rectTransform);
                Place(step.rectTransform, tl, tl, new Vector2(0, -stepTop - i * (each + stepGap)), new Vector2(SetupLeft, each));
                _setupHowStep[i] = step.gameObject;
                Image number = Solid("Number", step.transform, Make.Green);
                number.sprite = _circle;
                Place(number.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(46, 0), new Vector2(48, 48));
                Text n = Label("N", number.transform, (i + 1).ToString(), _bold, 22, Fg1, TextAnchor.MiddleCenter);
                Stretch(n.rectTransform);
                _setupHow[i] = Label("Line", step.transform, "", _medium, 23, Fg1, TextAnchor.MiddleLeft);
                _setupHow[i].lineSpacing = 1.12f;
                RectTransform r = _setupHow[i].rectTransform;
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(112, 10); r.offsetMax = new Vector2(-28, -10);
            }

            // Right: the Players, one row each, and a row that adds the next.
            float x = SetupWide - SetupRight;
            _setupPlayersCaption = Label("Players", block, "PLAYERS", _bold, 14, Make.Bright, TextAnchor.MiddleLeft);
            Place(_setupPlayersCaption.rectTransform, tl, tl, new Vector2(x, -18), new Vector2(SetupRight, 22));
            for (int i = 0; i < 4; i++)
            {
                _setupField[i] = Panel("Name " + (i + 1), block, new Color(1f, 1f, 1f, 0.03f));
                Tappable(_setupField[i], "name:" + i);
                Place(_setupField[i].rectTransform, tl, tl, new Vector2(x, SetupRowY(i)), new Vector2(SetupRight, SetupRow));
                _setupFieldEdge[i] = Panel("Edge", _setupField[i].transform, new Color(1f, 1f, 1f, 0.07f));
                _setupFieldEdge[i].sprite = _outline;
                Stretch(_setupFieldEdge[i].rectTransform);
                Image dot = Solid("Dot", _setupField[i].transform, PlayerColors[i]);
                dot.sprite = _circle;
                Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(16, 16));
                _setupName[i] = Label("Text", _setupField[i].transform, "", _medium, 26, Fg1, TextAnchor.MiddleLeft);
                _setupName[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(_setupName[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(62, 0), new Vector2(400, 44));

                // This Player's difficulty: tap, or press on it, to go round Easy, Medium, Hard.
                _setupChip[i] = Panel("Level " + (i + 1), block, new Color(1f, 1f, 1f, 0.05f));
                Place(_setupChip[i].rectTransform, tl, tl, new Vector2(x, SetupRowY(i)), new Vector2(SetupChip, SetupRow));
                _setupChipEdge[i] = Panel("Edge", _setupChip[i].transform, new Color(1f, 1f, 1f, 0.07f));
                _setupChipEdge[i].sprite = _outline;
                Stretch(_setupChipEdge[i].rectTransform);
                _setupChipText[i] = Label("Text", _setupChip[i].transform, "", _bold, (int)Sized(20, 22), Fg1, TextAnchor.MiddleCenter);
                Stretch(_setupChipText[i].rectTransform);
                Tappable(_setupChip[i], "level:" + i);

                // Take this Player out.
                _setupRemove[i] = Panel("Remove " + (i + 1), block, new Color(1f, 1f, 1f, 0.05f));
                Place(_setupRemove[i].rectTransform, tl, tl, new Vector2(SetupWide - SetupRow, SetupRowY(i)), new Vector2(SetupRow, SetupRow));
                _setupRemoveEdge[i] = Panel("Edge", _setupRemove[i].transform, new Color(1f, 1f, 1f, 0.07f));
                _setupRemoveEdge[i].sprite = _outline;
                Stretch(_setupRemoveEdge[i].rectTransform);
                Text cross = Label("X", _setupRemove[i].transform, "\u00D7", _regular, 38, Fg2, TextAnchor.MiddleCenter);
                Stretch(cross.rectTransform);
                Tappable(_setupRemove[i], "remove:" + i);
            }
            _setupAdd = Panel("Add Player", block, new Color(1f, 1f, 1f, 0.02f));
            _setupAdd.sprite = _outline;
            _setupAdd.color = new Color(1f, 1f, 1f, 0.22f);
            Place(_setupAdd.rectTransform, tl, tl, new Vector2(x, SetupRowY(1)), new Vector2(SetupRight, SetupRow));
            _setupAddText = Label("Text", _setupAdd.transform, "+   Add Player", _bold, 23, Fg2, TextAnchor.MiddleCenter);
            Stretch(_setupAddText.rectTransform);
            Tappable(_setupAdd, "add");

            // What the three levels mean, in one line above Let's play.
            float goTall = Sized(76, 90);
            _setupLevelNote = Label("Levels", block, "Easy: the whole mat counts     ·     Medium: within 6 in     ·     Hard: the hole only", _medium, 15, Fg3, TextAnchor.MiddleLeft);
            Place(_setupLevelNote.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, goTall + 14), new Vector2(SetupRight, 22));

            // The leaderboard for this game on this device, in the room left under the Players.
            // Who you play against, in games where a Player alone plays Puttr.
            _setupVsCaption = Label("Opponent caption", block, "YOU PLAY AGAINST", _bold, 14, Make.Bright, TextAnchor.MiddleLeft);
            _setupVs = Label("Opponent", block, "", _medium, 21, Fg1, TextAnchor.MiddleLeft);
            _setupVs.supportRichText = true;
            _setupBoardCaption = Label("Top scores", block, "", _bold, 14, Make.Bright, TextAnchor.MiddleLeft);
            for (int i = 0; i < _setupBoardName.Length; i++)
            {
                _setupBoardName[i] = Label("Best " + (i + 1), block, "", _medium, 21, Fg1, TextAnchor.MiddleLeft);
                _setupBoardScore[i] = Label("Score " + (i + 1), block, "", _bold, 21, Fg1, TextAnchor.MiddleRight);
            }

            BackButton(t);
            Image go = Panel("Let's play", block, Make.Green);
            _setupGo = go;
            Tappable(go, "start");
            Place(go.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 0), new Vector2(SetupRight, goTall));
            _setupGoEdge = Panel("Edge", go.transform, Color.clear);
            _setupGoEdge.sprite = _outline;
            Stretch(_setupGoEdge.rectTransform);
            Text goText = Label("Text", go.transform, "Let's play", _bold, (int)Sized(26, 30), Fg1, TextAnchor.MiddleCenter);
            _setupGoText = goText;
            Stretch(goText.rectTransform);
            _setupHint = Label("Hint", t, "", _medium, 18, Fg3, TextAnchor.MiddleCenter);
            Place(_setupHint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, Sized(64, 60)), new Vector2(1700, 30));
            _setup.SetActive(false);
        }

        const float SetupWide = 1560f, SetupTall = 660f, SetupLeft = 830f, SetupRight = 660f;
        static float SetupRow => Sized(72, 82);
        static float SetupChip => Sized(140, 156);
        static float SetupRowY(int row) => -52 - row * (SetupRow + 12);
        static readonly Color[] LevelInk = { new Color(0.48f, 0.83f, 0.27f), new Color(1f, 0.65f, 0.15f), new Color(1f, 0.42f, 0.42f) };

        /// <summary>The setup screen. zone is what is highlighted (0 a Player's row, 1 Add Player,
        /// 2 Let's play), focus is which Player's row and col which part of it (0 the name, 1 the
        /// difficulty, 2 the cross). levels is each Player's difficulty, or null where the game
        /// has none.</summary>
        public void ShowSetup(GameMode game, int players, IReadOnlyList<string> names, int zone, int focus, int col, int[] levels, string boardTitle = null, Line[] board = null, string opponent = null)
        {
            ShowSetupBoard(game, players, boardTitle, board, opponent);
            _setup.SetActive(true);
            _setup.transform.SetAsLastSibling();
            _setupBubble.color = Faded(game.Accent, 0.2f);
            _setupIcon.texture = IconTexture(game.Icon, false);
            _setupIcon.color = game.Accent;
            _setupIcon.enabled = _setupIcon.texture != null;
            _setupTitle.text = game.Name;
            _setupRule.text = game.Rule;
            string[] how = game.HowTo;
            for (int i = 0; i < 3; i++) { _setupHow[i].text = i < how.Length ? how[i] : ""; _setupHowStep[i].SetActive(i < how.Length); }

            bool solo = game.MaxPlayers == 1, keys = Kind != Device.Touch;
            _setupPlayersCaption.text = solo ? "ONE PLAYER" : "PLAYERS      " + players + " OF " + game.MaxPlayers;
            _setupLevelNote.gameObject.SetActive(levels != null);
            _setupHint.text = For(
                "Putt to start      ·      Arrows move, Enter chooses      ·      Type to name the highlighted Player" + (solo ? "" : "      ·      Leave a putt short to add a Player"),
                "Putt, or tap Let's play, to start" + (solo ? "" : "      ·      Leave a putt short of the hole to add a Player"),
                "Putt to start      ·      Move with the remote and press to choose" + (solo ? "" : "      ·      Leave a putt short to add a Player"));
            bool removable = !solo && players > 1;
            var lit = Color.white;
            var quiet = new Color(1f, 1f, 1f, 0.07f);
            float x = SetupWide - SetupRight;
            float fieldWide = SetupRight - (levels != null ? SetupChip + 12 : 0) - (removable ? SetupRow + 12 : 0);
            for (int i = 0; i < 4; i++)
            {
                bool shown = i < players, here = zone == 0 && i == focus;
                _setupField[i].gameObject.SetActive(shown);
                _setupField[i].rectTransform.sizeDelta = new Vector2(fieldWide, SetupRow);
                bool typing = here && col == 0;
                _setupFieldEdge[i].color = typing ? Make.Bright : quiet;
                bool empty = string.IsNullOrEmpty(names[i]);
                _setupName[i].text = empty ? (typing ? "|" : "Player " + (i + 1)) : names[i] + (typing ? "|" : "");
                _setupName[i].color = empty && !typing ? new Color(1f, 1f, 1f, 0.3f) : Fg1;

                _setupChip[i].gameObject.SetActive(shown && levels != null);
                if (levels != null)
                {
                    int level = Mathf.Clamp(levels[i], 0, 2);
                    _setupChip[i].rectTransform.anchoredPosition = new Vector2(x + fieldWide + 12, SetupRowY(i));
                    _setupChip[i].color = Faded(LevelInk[level], 0.16f);
                    _setupChipText[i].text = Arcade.LevelNames[level];
                    _setupChipText[i].color = LevelInk[level];
                    _setupChipEdge[i].color = here && col == 1 && keys ? lit : Faded(LevelInk[level], 0.45f);
                }
                _setupRemove[i].gameObject.SetActive(shown && removable);
                _setupRemoveEdge[i].color = here && col == 2 && keys ? lit : quiet;
            }
            // The row after the last Player adds the next one.
            bool room = !solo && players < game.MaxPlayers;
            _setupAdd.gameObject.SetActive(room);
            _setupAdd.rectTransform.anchoredPosition = new Vector2(x, SetupRowY(players));
            _setupAdd.color = zone == 1 && keys ? lit : new Color(1f, 1f, 1f, 0.22f);
            _setupAddText.color = zone == 1 && keys ? Fg1 : Fg2;
            _setupGoEdge.color = zone == 2 && keys ? lit : Color.clear;

            // Make the highlight unmistakable with a remote or keys; fingers just tap.
            bool goHere = zone == 2 && keys;
            _setupGo.color = !keys || goHere ? Make.Green : Faded(Make.Green, 0.4f);
            _setupGoText.color = !keys || goHere ? Fg1 : new Color(1f, 1f, 1f, 0.6f);
            Focus(_setupGo, goHere);
            for (int i = 0; i < 4; i++)
            {
                bool here = keys && zone == 0 && i == focus;
                Focus(_setupField[i], here && col == 0);
                Focus(_setupChip[i], here && col == 1);
                Focus(_setupRemove[i], here && col == 2);
            }
            Focus(_setupAdd, keys && zone == 1);
        }

        // As many of the best scores as fit between the last Player row and the level note:
        // with one Player there is room for five, with four there may be none.
        void ShowSetupBoard(GameMode game, int players, string title, Line[] board, string opponent)
        {
            bool solo = game.MaxPlayers == 1;
            int rowsUsed = players + (!solo && players < game.MaxPlayers ? 1 : 0);
            float top = -SetupRowY(rowsUsed) + 22f;                  // below the last row
            float bottom = SetupTall - (Sized(76, 90) + 14 + 22 + 16); // above the level note
            const float caption = 34f, line = 36f;
            float x = SetupWide - SetupRight;
            var tl = new Vector2(0, 1);

            // Who you play against comes first, then the scores in what room is left.
            bool vs = opponent != null && bottom - top >= caption + line;
            _setupVsCaption.gameObject.SetActive(vs);
            _setupVs.gameObject.SetActive(vs);
            if (vs)
            {
                _setupVs.text = opponent;
                Place(_setupVsCaption.rectTransform, tl, tl, new Vector2(x, -top), new Vector2(SetupRight, 22));
                Place(_setupVs.rectTransform, tl, tl, new Vector2(x, -(top + caption)), new Vector2(SetupRight, 30));
                top += caption + line + 18f;
            }

            int fit = Mathf.Clamp(Mathf.FloorToInt((bottom - top - caption) / line), 0, _setupBoardName.Length);
            bool any = board != null && board.Length > 0;
            bool show = title != null && fit > 0;
            _setupBoardCaption.gameObject.SetActive(show);
            if (show)
            {
                _setupBoardCaption.text = title;
                Place(_setupBoardCaption.rectTransform, tl, tl, new Vector2(x, -top), new Vector2(SetupRight, 22));
            }
            for (int i = 0; i < _setupBoardName.Length; i++)
            {
                bool on = show && i < fit && (any ? i < board.Length : i == 0);
                _setupBoardName[i].gameObject.SetActive(on);
                _setupBoardScore[i].gameObject.SetActive(on && any);
                if (!on) continue;
                float y = -(top + caption + i * line);
                Place(_setupBoardName[i].rectTransform, tl, tl, new Vector2(x, y), new Vector2(SetupRight - 180, 30));
                Place(_setupBoardScore[i].rectTransform, tl, tl, new Vector2(x + SetupRight - 180, y), new Vector2(180, 30));
                if (!any)
                {
                    _setupBoardName[i].text = "No scores yet. Finish a game to set the first.";
                    _setupBoardName[i].color = Fg3;
                    _setupBoardName[i].rectTransform.sizeDelta = new Vector2(SetupRight, 30);
                    continue;
                }
                Color ink = board[i].Lit ? Make.Bright : Fg1;
                _setupBoardName[i].text = (i + 1) + "    " + board[i].Name;
                _setupBoardName[i].color = ink;
                _setupBoardScore[i].text = board[i].Value;
                _setupBoardScore[i].color = ink;
            }
        }

        public void HideSetup()
        {
            if (_setup != null) _setup.SetActive(false);
        }

        /// <summary>"Dead Weight" to "D E A D   W E I G H T".</summary>
        public static string Spaced(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in text.ToUpperInvariant())
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(c == ' ' ? ' ' : c);
            }
            return sb.ToString();
        }

        /// <summary>Say something about the console connection, or nothing (null) when all is well.</summary>
        public void SetStatus(string text, Color dot)
        {
            _status.SetActive(!string.IsNullOrEmpty(text));
            StatusUp = !string.IsNullOrEmpty(text);
            _statusText.text = text ?? "";
            _statusDot.color = dot;
            // As wide as what it says, so it keeps clear of a scoreboard across the top.
            if (!string.IsNullOrEmpty(text))
            {
                var card = (RectTransform)_status.transform;
                card.sizeDelta = new Vector2(Mathf.Clamp(_statusText.preferredWidth + 66f, 200f, Upright ? UprightWide - 40f : 640f), card.sizeDelta.y);
            }
            if (_status.activeSelf) { _status.transform.SetAsLastSibling(); PlaceStatus(); }
        }

        public void SetPrompt(string text)
        {
            _prompt.text = text;
            _promptCard.SetActive(!string.IsNullOrEmpty(text));
            if (string.IsNullOrEmpty(text) && _teeCard != null) _teeCard.SetActive(false);
            // The card is as wide as what it says, so a long prompt stays on one line inside it.
            if (!string.IsNullOrEmpty(text))
            {
                _prompt.horizontalOverflow = HorizontalWrapMode.Overflow;
                var card = (RectTransform)_promptCard.transform;
                float wide = Mathf.Clamp(_prompt.preferredWidth + 110f, 480f, 1240f);
                if (Upright)
                {
                    // puttr-app-2: upright the last-putt card is above, not beside: centered, and as wide as the screen allows.
                    bool fits = wide <= UprightWide - 40f;
                    _prompt.horizontalOverflow = fits ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
                    card.sizeDelta = new Vector2(Mathf.Min(wide, UprightWide - 40f), fits ? 64f : 92f);
                    card.anchoredPosition = new Vector2(0f, card.anchoredPosition.y);
                    return;
                }
                card.sizeDelta = new Vector2(wide, 64f);
                // Centered while it fits; a wide one moves right, clear of the last-putt card.
                float half = _root.rect.width * 0.5f;
                card.anchoredPosition = new Vector2(Mathf.Max(0f, 624f + wide * 0.5f - half), card.anchoredPosition.y);
            }
        }

        public void SetPutt(string speed, string line, string result, Color resultColor)
        {
            _puttCard.SetActive(true);
            _puttValues[0].text = speed;
            _puttValues[1].text = line;
            _puttValues[2].text = result;
            _puttValues[2].color = resultColor;
        }

        public void HidePutt() => _puttCard.SetActive(false);

        /// <summary>Big words across the middle that fade on their own.</summary>
        public void Banner(string text, string sub, Color color, float seconds = 1.8f)
        {
            _banner.text = text;
            _bannerSub.text = sub ?? "";
            _banner.color = color;
            _bannerSub.color = Fg1;
            _bannerAge = 0f;
            _bannerLife = seconds;
            _bannerBand.color = new Color(0f, 0f, 0f, 0.9f);
            // Small: half the size, along the bottom of the screen, clear of the last-putt card.
            float y = SmallBanner ? -438f : -230f, subY = SmallBanner ? -492f : -330f;
            _bannerSize = SmallBanner ? 0.5f : 1f;
            _banner.rectTransform.anchoredPosition = new Vector2(SmallBanner ? 120f : 0f, y);
            _bannerSub.rectTransform.anchoredPosition = new Vector2(SmallBanner ? 120f : 0f, subY);
            _bannerSub.rectTransform.localScale = Vector3.one * (SmallBanner ? 0.8f : 1f);
            _bannerBand.rectTransform.anchoredPosition = new Vector2(0, SmallBanner ? -455f : -256f);
            _bannerBand.rectTransform.sizeDelta = new Vector2(0, SmallBanner ? 150f : 250f);
            if (Upright)
            {
                // puttr-app-2
                _bannerSize = 1f;
                _banner.rectTransform.anchoredPosition = new Vector2(0f, -150f);
                _bannerSub.rectTransform.anchoredPosition = new Vector2(0f, -240f);
                _bannerSub.rectTransform.localScale = Vector3.one;
                _bannerBand.rectTransform.anchoredPosition = new Vector2(0f, -186f);
                _bannerBand.rectTransform.sizeDelta = new Vector2(0f, 220f);
            }
        }

        public void ShowOverlay(string title, string body, params string[] buttons)
        {
            _overlayTitle.text = title;
            _overlayBody.text = body;
            // Buttons along the bottom of the card, which grows to hold them.
            int n = Kind == Device.Tv ? 0 : Mathf.Min(buttons.Length, _overlayButton.Length);
            const float gap = 16f, across = 820f, high = 78f;
            // As tall as what it says: a short message does not leave a hole above the buttons.
            int lines = body.Split('\n').Length;
            float tall = n > 0 ? Mathf.Clamp(300 + lines * 36, 380, 560) : 560;
            _overlayCard.sizeDelta = new Vector2(900, n > 0 ? tall + high + 26 : tall);
            float each = n > 0 ? (across - gap * (n - 1)) / n : 0f;
            for (int i = 0; i < _overlayButton.Length; i++)
            {
                _overlayButton[i].gameObject.SetActive(i < n);
                if (i >= n) continue;
                _overlayButton[i].rectTransform.localScale = Vector3.one;
                Place(_overlayButton[i].rectTransform, new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(-across * 0.5f + i * (each + gap), 36), new Vector2(each, high));
                _overlayButtonText[i].text = buttons[i];
            }
            _overlayCard.gameObject.SetActive(true);
            if (_summaryCard != null) _summaryCard.gameObject.SetActive(false);
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
        }

        public void HideOverlay() => _overlay.SetActive(false);

        CanvasScaler _scaler;

        // The interface is laid out 1920 wide by 1080 high. A squarer screen
        // keeps the width and gains height; a wider one keeps the height.
        // ── puttr-app-2: a phone held upright ───────────────────────────────
        // Wide, the interface is 1920 across and a phone shows it at about a third of a point to
        // the unit: fine for a TV, unreadable upright. Upright it is 600 across instead, which
        // makes a unit about two thirds of a point on a phone, and the pieces that sat side by
        // side are stacked. Nothing in the game frame is then smaller than 17 units, 11 points.
        // puttr-app-2 (walk, 2026-11-23): a tablet upright is squarer than a phone, so 600 across left it only 860 units
        // tall and the pieces stacked for a phone's 1300 ran into each other (the banner over the tee and the putt card,
        // the scoreboard over the game). A tablet is laid out as wide as makes it 1300 tall (about 900 on an iPad), which
        // also brings everything down from twice a phone's size to about a third larger.
        const float UprightTall = 1300f;
        static float UprightWide
        {
            get
            {
                if (!Tablet || Screen.height <= Screen.width) return 600f;
                return Mathf.Clamp(UprightTall * Screen.width / Mathf.Max(1f, Screen.height), 600f, 1000f);
            }
        }
        public bool Upright { get; private set; }
        bool _uprightKnown;
        readonly Text[] _puttNames = new Text[3];
        RectTransform _menuRect;

        // Wide: top right, beside the menu. Upright: a row of its own under the logo, and under the scoreboard when
        // one is up (puttr-app-2: it sat on the rule games' score bar).
        /// <summary>puttr-app-2: the app's score bug ends this far down the screen (pixels); upright, the chip goes below it.</summary>
        public static float StatusBelowPx;
        /// <summary>puttr-app-2: the chip is showing. Upright, with the app's score bug on screen, it takes a row of its own
        /// at the top under the logo and the app moves its bug down a row (it used to sit under the bug, over the
        /// hole in Center Cut: Matt, 2026-11-22).</summary>
        public static bool StatusUp;
        public const float StatusRowPx = 44f;

        void PlaceStatus()
        {
            if (_status == null) return;
            bool up = Upright;
            var card = (RectTransform)_status.transform;
            float wide = Mathf.Min(card.sizeDelta.x, up ? UprightWide - 40f : 640f);
            if (up)
            {
                float y = -92f;
                if (_scoreRoot != null && _scoreRoot.gameObject.activeInHierarchy)
                {
                    var score = (RectTransform)_scoreRoot.transform;
                    Vector3[] corners = new Vector3[4];
                    score.GetWorldCorners(corners);
                    var mine = (RectTransform)_root;
                    float bottom = mine.InverseTransformPoint(corners[0]).y, top = mine.rect.yMax;
                    y = Mathf.Min(y, bottom - top - 12f);
                }
                // With the app's score bug up: the top row under the logo, and the bug steps down under it.
                if (StatusBelowPx > 0f) y = -74f;
                Place(card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(wide, 48));
            }
            else Place(card, new Vector2(1, 1), new Vector2(1, 1), new Vector2(Sized(-108, -130), Sized(-36, -46)), new Vector2(wide, 48));
        }

        // ── puttr-app-2: the top row's size on a tablet, and the pause button's on anything touched ──
        // Upright the interface is 600 units across whatever the screen, so on a tablet (820 points across, not 400) the
        // logo, the game's name and the pause button came out twice the size they are with the tablet on its side
        // (Matt, 2026-11-23). Upright on a tablet the logo row is drawn at its sideways size. The pause button is
        // 44 points, iOS's own size for a navigation button, on every phone and tablet, either way up.

        /// <summary>A tablet: the short side is 3.3 inches or more (the app's own test, App.Tablet), or -app2Tablet on a desktop window.</summary>
        public static bool Tablet
        {
            get
            {
                if (_forceTablet < 0) _forceTablet = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-app2Tablet") >= 0 ? 1 : 0;
                return _forceTablet == 1 || (Application.isMobilePlatform && Screen.dpi > 0f && Mathf.Min(Screen.width, Screen.height) / Screen.dpi >= 3.3f);
            }
        }
        static int _forceTablet = -1;

        /// <summary>Screen pixels to a point (as App's own scale: an iPhone's 2x or 3x, an Android's dpi / 160, 1 on a desktop).</summary>
        static float PixelsPerPoint =>
            !Application.isMobilePlatform || Screen.dpi <= 0f ? 1f :
            Application.platform == RuntimePlatform.IPhonePlayer ? Mathf.Max(1f, Mathf.Round(Screen.dpi / 150f)) : Mathf.Max(1f, Screen.dpi / 160f);

        /// <summary>Screen pixels to one unit of the interface, for a screen this size (the scaler's own sum, known before it has run).</summary>
        static float UnitPixels(float width, float height)
        {
            if (height > width) return width / UprightWide;
            return width / Mathf.Max(1f, height) < 16f / 9f ? width / 1920f : height / 1080f;
        }

        /// <summary>How much smaller the logo row is drawn upright on a tablet, so it is the size it is sideways.</summary>
        float TopRowScale => Upright && Tablet ? Mathf.Clamp(UnitPixels(Screen.height, Screen.width) / Mathf.Max(0.001f, UnitPixels(Screen.width, Screen.height)), 0.3f, 1f) : 1f;

        const float MenuPoints = 44f;
        float _menuBuilt = 76f;

        void Arrange()
        {
            bool up = Upright;
            if (_brand != null)
            {
                Place(_brand, new Vector2(0, 1), new Vector2(0, 1), up ? new Vector2(20, -14) : new Vector2(40, -36), new Vector2(420, 56));
                _brand.localScale = Vector3.one * TopRowScale;
            }
            if (_menuRect != null)
            {
                _menuRect.anchoredPosition = up ? new Vector2(-20, -10) : new Vector2(-40, -32);
                // 44 points on a phone or tablet (it was 50 upright on a phone and over 100 on a tablet); a mouse keeps its own.
                float want = Application.isMobilePlatform || Tablet ? MenuPoints * PixelsPerPoint / Mathf.Max(0.001f, UnitPixels(Screen.width, Screen.height)) : _menuBuilt;
                _menuRect.localScale = Vector3.one * (want / _menuBuilt);
            }
            FitGameName();
            if (_root != null) ShowTopShade();
            PlaceStatus();
            if (_promptCard != null)
            {
                var card = (RectTransform)_promptCard.transform;
                card.anchoredPosition = new Vector2(0, up ? 28 : 40);
                if (up) card.sizeDelta = new Vector2(Mathf.Min(card.sizeDelta.x, UprightWide - 40f), card.sizeDelta.y);
            }
            if (_puttCard != null)
            {
                // Wide: bottom left, beside the prompt. Upright: above it.
                var card = (RectTransform)_puttCard.transform;
                if (up) Place(card, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 108), new Vector2(560, 112));
                else Place(card, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 40), new Vector2(560, 104));
                foreach (Text name in _puttNames) if (name != null) { name.fontSize = up ? 17 : 14; name.rectTransform.sizeDelta = new Vector2(name.rectTransform.sizeDelta.x, up ? 22 : 18); }
            }
            if (_teeCard != null)
            {
                ((RectTransform)_teeCard.transform).anchoredPosition = new Vector2(0, up ? 236 : 112);
                _tee.fontSize = up ? 18 : 16;
            }
            if (_banner != null)
            {
                _banner.fontSize = up ? 84 : 150;
                _banner.rectTransform.sizeDelta = up ? new Vector2(UprightWide - 20f, 110) : new Vector2(1600, 180);
                _bannerSub.fontSize = up ? 26 : 32;
                _bannerSub.rectTransform.sizeDelta = up ? new Vector2(UprightWide - 40f, 80) : new Vector2(1600, 50);
            }
            // The pause card is wider than an upright screen: shown smaller, its text stays above 10 points.
            if (_overlayCard != null) _overlayCard.localScale = Vector3.one * (up ? (UprightWide - 40f) / 900f : 1f);
            if (_summaryCard != null) _summaryCard.localScale = Vector3.one * (up ? Mathf.Min(1f, (UprightWide - 40f) / Mathf.Max(1f, _summaryCard.sizeDelta.x)) : 1f);
        }

        void FitScreen()
        {
            bool up = Screen.height > Screen.width;
            if (!_uprightKnown || up != Upright)
            {
                Upright = up;
                _uprightKnown = true;
                if (_scaler != null) _scaler.referenceResolution = up ? new Vector2(UprightWide, UprightTall) : new Vector2(1920, 1080);
                Arrange();
            }
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            float match = up ? 0f : aspect < 16f / 9f ? 0f : 1f;
            if (_scaler != null && !Mathf.Approximately(_scaler.matchWidthOrHeight, match)) _scaler.matchWidthOrHeight = match;
            FitSafeArea();
            FitGameName();
        }

        // ── Safe area ───────────────────────────────────────────────────────
        // An iPhone held sideways has the Dynamic Island (or notch) at one end, rounded corners and
        // the home indicator: Screen.safeArea is the part clear of them. Everywhere else it is the
        // whole screen. -puttrSafeArea left,bottom,right,top (pixels) fakes one for testing.

        readonly List<RectTransform> _bleeds = new List<RectTransform>();
        Rect _safeFor = new Rect(-1, -1, -1, -1);
        Vector2 _safeScreen;
        float _safeScale = -1f;
        static Rect? _fakeSafe;
        static bool _fakeRead;

        static Rect SafeArea()
        {
            if (!_fakeRead)
            {
                _fakeRead = true;
                string[] args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-puttrSafeArea");
                if (i >= 0 && i + 1 < args.Length)
                {
                    string[] v = args[i + 1].Split(',');
                    if (v.Length == 4 && float.TryParse(v[0], out float l) && float.TryParse(v[1], out float b) && float.TryParse(v[2], out float r) && float.TryParse(v[3], out float t))
                        _fakeSafe = new Rect(l, b, -r - l, -t - b); // insets; made absolute below
                }
            }
            if (_fakeSafe is Rect f) return new Rect(f.x, f.y, Screen.width + f.width, Screen.height + f.height);
            // A TV's "safe area" is an overscan margin. The TV layout keeps its own margins already,
            // and squeezing into it pushed the home screen's Play behind the row of games.
            if (Kind == Device.Tv) return new Rect(0, 0, Screen.width, Screen.height);
            return Screen.safeArea;
        }

        /// <summary>A full-screen backdrop that should reach the screen's edges, not stop at the safe area.</summary>
        void Bleed(Graphic g)
        {
            _bleeds.Add(g.rectTransform);
            _safeScale = -1f; // lay it out on the next fit
        }

        void FitSafeArea()
        {
            if (_root == null || _canvas == null || Screen.width <= 0 || Screen.height <= 0) return;
            Rect safe = SafeArea();
            var screen = new Vector2(Screen.width, Screen.height);
            float scale = Mathf.Max(0.0001f, _canvas.scaleFactor);
            if (safe == _safeFor && screen == _safeScreen && Mathf.Approximately(scale, _safeScale)) return;
            _safeFor = safe; _safeScreen = screen; _safeScale = scale;
            var min = new Vector2(safe.xMin, safe.yMin) / scale;
            var max = new Vector2(Screen.width - safe.xMax, Screen.height - safe.yMax) / scale;
            _root.offsetMin = min;
            _root.offsetMax = -max;
            foreach (RectTransform b in _bleeds)
            {
                if (b == null) continue;
                b.offsetMin = -min;
                b.offsetMax = max;
            }
        }

        public void Tick(float dt)
        {
            FitScreen();
            StackUpright();
            TickRail(dt);
            _time += dt;
            // The dot beside the prompt breathes while the game waits for a putt.
            if (_promptCard.activeSelf)
            {
                Color c = Make.Bright;
                c.a = 0.55f + 0.45f * Mathf.Sin(_time * 3.2f);
                _promptDot.color = c;
            }
            if (_bannerAge > _bannerLife) return;
            _bannerAge += dt;
            float t = Mathf.Clamp01(_bannerAge / _bannerLife);
            float pop = 1f + 0.35f * Mathf.Pow(1f - Mathf.Clamp01(_bannerAge / 0.25f), 2f);
            float alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
            _banner.rectTransform.localScale = Vector3.one * pop * _bannerSize;
            Color b = _banner.color; b.a = alpha; _banner.color = b;
            Color s = _bannerSub.color; s.a = alpha; _bannerSub.color = s;
            _bannerBand.color = new Color(0f, 0f, 0f, 0.9f * alpha);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Shade(Text text, float distance)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.6f);
            shadow.effectDistance = new Vector2(0, -distance);
        }

        static Image Solid(string name, Transform parent, Color color)
        {
            var image = Node(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static RawImage Raw(string name, Transform parent, Texture texture)
        {
            var raw = Node(name, parent).gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;
            return raw;
        }

        Image Panel(string name, Transform parent, Color color)
        {
            Image image = Solid(name, parent, color);
            image.sprite = _round;
            image.type = Image.Type.Sliced;
            return image;
        }

        // A glass card: dark, see-through, with a hairline border.
        Image Card(string name, Transform parent)
        {
            Image card = Panel(name, parent, Glass);
            Image border = Panel("Border", card.transform, Border);
            border.sprite = _outline;
            Stretch(border.rectTransform);
            return card;
        }

        static Text Label(string name, Transform parent, string text, Font font, int size, Color color, TextAnchor anchor)
        {
            var label = Node(name, parent).gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }
    }
}
