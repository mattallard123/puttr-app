// A played game's replay, putt by putt (puttr-app profile/GameReplay.js).
//
// The recording is one video per segment (a round resumed across app sessions makes several), each row of
// /video/recordings/:gameId with its videoUrl and created_at, the moment it started recording. A putt's place in
// the recording is where GameReplay.js puts it: the latest segment that started at or before the putt row's
// created_at, at (putt created_at - segment created_at) rounded to the second, plus one. A putt row is written
// once the ball has stopped, so its clip runs from 6 s before that (the stroke and the roll) to 2 s after.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace PuttrApp
{
    public static class GameReplay
    {
        /// <summary>Seconds of video shown before a putt's time (GameReplay.js timeOffset) and after it.</summary>
        public const float Lead = 6f, Tail = 2f;

        /// <summary>Where one putt is in the recording. Timed is false when the putt or its segment has no time.</summary>
        public struct Clip
        {
            public int Seg;
            public float At, Start, End;
            public bool Timed;
        }

        /// <summary>The recordings that have a video, in recording order.</summary>
        public static List<object> Segments(List<object> recordings)
        {
            if (recordings == null) return new List<object>();
            return recordings.Where(r => J.Str(r, "videoUrl").StartsWith("http"))
                .OrderBy(r => ProfileMath.Date(r, "created_at") ?? DateTime.MinValue).ToList();
        }

        /// <summary>Each putt's clip: its segment and the seconds into that segment's video (buildPuttMap).</summary>
        public static List<Clip> Clips(List<object> segments, List<object> putts)
        {
            var clips = new List<Clip>();
            if (putts == null) return clips;
            foreach (object putt in putts)
            {
                DateTime? at = ProfileMath.Date(putt, "created_at");
                int seg = 0;
                DateTime? from = segments.Count > 0 ? ProfileMath.Date(segments[0], "created_at") : null;
                if (at.HasValue)
                    for (int s = 0; s < segments.Count; s++)
                    {
                        DateTime? started = ProfileMath.Date(segments[s], "created_at");
                        if (started.HasValue && started.Value <= at.Value) { seg = s; from = started; }
                        else break;
                    }
                if (!at.HasValue || !from.HasValue) { clips.Add(new Clip { Seg = 0, Timed = false }); continue; }
                float t = (float)Math.Round((at.Value - from.Value).TotalSeconds, MidpointRounding.AwayFromZero) + 1f;
                clips.Add(new Clip { Seg = seg, At = t, Start = Mathf.Max(0f, t - Lead), End = Mathf.Max(0f, t + Tail), Timed = true });
            }
            return clips;
        }

        /// <summary>The replay, full screen, starting on putt <paramref name="start"/> (or the start of the video when the putts have no times).</summary>
        public static void Open(VisualElement over, List<object> recordings, List<object> putts, int start = 0)
        {
            List<object> segments = Segments(recordings);
            if (segments.Count == 0) return;
            new Player(over, segments, putts ?? new List<object>(), start);
        }

        sealed class Player
        {
            readonly List<object> _segments;
            readonly List<Clip> _clips;
            readonly bool _timed;
            readonly GameObject _go;
            readonly VideoPlayer _video;
            readonly Image _picture;
            readonly VisualElement _dim, _stage, _load, _paused;
            readonly ScrollView _strip;
            readonly List<VisualElement> _chips = new List<VisualElement>();
            RenderTexture _texture;
            VisualElement _spinner;
            int _seg = -1, _current;
            float? _pending;
            bool _closed, _seeking, _ended, _shown;
            long _seekFrame;
            float _seekSince;

            public Player(VisualElement over, List<object> segments, List<object> putts, int start)
            {
                _segments = segments;
                _clips = Clips(segments, putts);
                _timed = _clips.Any(c => c.Timed);

                _go = new GameObject("Game replay");
                _video = _go.AddComponent<VideoPlayer>();
                var audio = _go.AddComponent<AudioSource>();
                _video.source = VideoSource.Url;
                _video.playOnAwake = false;
                _video.isLooping = false;
                _video.waitForFirstFrame = true;
                _video.renderMode = VideoRenderMode.RenderTexture;
                _video.audioOutputMode = VideoAudioOutputMode.AudioSource;
                _video.SetTargetAudioSource(0, audio);
                _video.prepareCompleted += Prepared;
                _video.seekCompleted += _ => { _seeking = false; };
                _video.loopPointReached += _ => SegmentEnded();
                _video.errorReceived += (_, __) => Failed();

                // The picture keeps the video's own shape (fitted, never stretched) on black.
                _picture = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                _picture.AddToClassList("rp-fill");
                _picture.visible = false;
                _load = Ui.Box("rp-fill rp-center");
                _load.pickingMode = PickingMode.Ignore;
                _paused = Ui.Box("rp-fill rp-center", Ui.Box("rp-play", Ui.Icon("Play", 30, Color.white)));
                _paused.pickingMode = PickingMode.Ignore;
                _paused.style.display = DisplayStyle.None;
                _stage = Ui.Box("rp-stage", _picture, _load, _paused);
                _stage.OnTap(TogglePause);
                VisualElement close = Ui.Box("rp-close", Ui.Text("‹", "rp-close-t", Ui.Medium)).OnTap(Close);

                _strip = Ui.Scroll(true);
                _strip.AddToClassList("rp-strip");
                for (int i = 0; i < putts.Count; i++)
                {
                    int index = i;
                    VisualElement chip = Chip(putts[i], i).OnTap(() => Start(index));
                    _chips.Add(chip);
                    _strip.Add(chip);
                }
                if (putts.Count == 0) _strip.style.display = DisplayStyle.None;

                _dim = Ui.Box("rp-root", _stage, _strip, close);
                StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Replay");
                if (sheet != null) _dim.styleSheets.Add(sheet);
                Vector4 safe = App.I != null ? App.I.SafeInsets() : Vector4.zero;
                _dim.style.paddingTop = safe.w;
                _dim.style.paddingBottom = safe.y;
                _dim.style.paddingLeft = safe.x;
                _dim.style.paddingRight = safe.z;
                close.style.top = safe.w + 10;
                close.style.left = safe.x + 12;
                App.Layer(over).Add(_dim);
                _dim.schedule.Execute(Tick).Every(50);
                Perf.Wake(2f);

                Start(Mathf.Clamp(start, 0, Mathf.Max(0, putts.Count - 1)));

                // Fixture runs only: PUTTR_FIXTURE_REPLAY_TAP=putt,seconds does what a tap on that putt's card does, that
                // long after the replay opens (a scripted click is not delivered while another app has the focus).
                string[] tap = (Environment.GetEnvironmentVariable("PUTTR_FIXTURE_REPLAY_TAP") ?? "").Split(',');
                if (Fixtures.On && tap.Length == 2 && int.TryParse(tap[0], out int putt) && float.TryParse(tap[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float after) && putt >= 1 && putt <= putts.Count)
                    _dim.schedule.Execute(() => Start(putt - 1)).ExecuteLater((long)(after * 1000));
            }

            /// <summary>One putt in the strip: its number, distance and tee, the cup with where it went in, and its read.</summary>
            VisualElement Chip(object putt, int i)
            {
                GameViewScreen.PuttRead r = GameViewScreen.Read(putt);
                VisualElement number = Ui.Box("rp-num", Ui.Text((i + 1).ToString(), "rp-num-t", Ui.Bold));
                number.style.backgroundColor = r.Made ? Ui.Green : Ui.Red;
                Label result = Ui.Text(r.Result, "rp-result", Ui.Bold);
                result.style.color = r.Made ? Ui.Bright : Ui.Red;
                VisualElement hole = r.Tracked ? new GvHole(r.Made, r.Entry) : Ui.Box("gv-hole");
                hole.AddToClassList("rp-hole");
                VisualElement chip = Ui.Box("rp-chip",
                    Ui.Box("rp-chip-top", number, Ui.Text(J.Int(putt, "distance") + " ft", "rp-dist", Ui.Bold), Ui.Text(PracticeSession.TeeName(J.Int(putt, "tee", 2)) + " tee", "rp-tee", Ui.Medium)),
                    Ui.Box("rp-chip-mid", hole, result));
                if (r.Rollout != null) chip.Add(Ui.Text(r.Rollout, "rp-pace", Ui.Bold));
                if (r.Entry.HasValue) chip.Add(Ui.Text(GameViewScreen.EntryWords(r.Entry.Value), "rp-entry", Ui.Regular));
                return chip;
            }

            /// <summary>Plays putt <paramref name="index"/>'s clip; the following putts' clips follow it.</summary>
            void Start(int index)
            {
                if (_closed) return;
                _ended = false;
                _current = index;
                Highlight();
                if (_timed && index < _clips.Count && _clips[index].Timed) PlayAt(_clips[index].Seg, _clips[index].Start);
                else if (_seg < 0) PlayAt(0, 0f);
                else { if (!_video.isPlaying) _video.Play(); ShowPaused(false); }
            }

            void PlayAt(int seg, float seconds)
            {
                ShowPaused(false);
                Busy(true);
                if (seg != _seg)
                {
                    _seg = seg;
                    _pending = seconds;
                    _shown = false;
                    _video.Stop();
                    _video.url = J.Str(_segments[seg], "videoUrl");
                    _video.Prepare();
                    return;
                }
                if (!_video.isPrepared) { _pending = seconds; return; }
                Seek(seconds);
            }

            void Seek(float seconds)
            {
                if (_video.canSetTime && Mathf.Abs((float)_video.time - seconds) > 0.05f)
                {
                    _seeking = true;
                    _seekFrame = _video.frame;
                    _seekSince = Time.realtimeSinceStartup;
                    _video.time = seconds;
                }
                _video.Play();
            }

            void Prepared(VideoPlayer p)
            {
                if (_closed) return;
                int w = (int)p.width, h = (int)p.height;
                if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
                if (p.pixelAspectRatioDenominator > 0 && p.pixelAspectRatioNumerator > 0 && p.pixelAspectRatioNumerator != p.pixelAspectRatioDenominator)
                    w = Mathf.RoundToInt(w * (float)p.pixelAspectRatioNumerator / p.pixelAspectRatioDenominator);
                if (_texture == null || _texture.width != w || _texture.height != h)
                {
                    if (_texture != null) _texture.Release();
                    _texture = new RenderTexture(w, h, 0);
                    _texture.Create();
                    RenderTexture was = RenderTexture.active;
                    RenderTexture.active = _texture;
                    GL.Clear(true, true, Color.black);
                    RenderTexture.active = was;
                    _picture.image = _texture;
                }
                p.targetTexture = _texture;
                float at = _pending ?? 0f;
                _pending = null;
                Seek(at);
            }

            void Tick()
            {
                if (_closed) return;
                if (_video.isPlaying) Perf.Wake(1f);
                // A seek that never reports back (some streams) stops counting as one after 4 s.
                if (_seeking && Time.realtimeSinceStartup - _seekSince > 4f) _seeking = false;
                // The loader stays until a frame from the new place is drawn, so nothing stale or grey shows.
                if (_spinner != null && _video.isPlaying && !_seeking && _video.frame >= 1 && _video.frame != _seekFrame)
                {
                    Busy(false);
                    if (!_shown) { _shown = true; _picture.visible = true; }
                }
                if (!_timed || _seeking || !_video.isPlaying || _current >= _clips.Count) return;
                Clip clip = _clips[_current];
                if (clip.Timed && clip.Seg == _seg && _video.time >= clip.End) Next();
            }

            /// <summary>The next putt's clip: straight on when it starts where this one ends, else a seek to it. After the last, a pause.</summary>
            void Next()
            {
                int next = _current + 1;
                while (next < _clips.Count && !_clips[next].Timed) next++;
                if (next >= _clips.Count) { _video.Pause(); _ended = true; ShowPaused(true); return; }
                Clip c = _clips[next];
                _current = next;
                Highlight();
                if (c.Seg == _seg && c.Start <= _video.time + 0.3f) return;
                PlayAt(c.Seg, c.Start);
            }

            void SegmentEnded()
            {
                if (_closed) return;
                if (_timed) { Next(); return; }
                if (_seg + 1 < _segments.Count) PlayAt(_seg + 1, 0f);
                else { _ended = true; ShowPaused(true); }
            }

            void TogglePause()
            {
                if (_closed || _seg < 0 || !_video.isPrepared) return;
                if (_ended) { Start(_timed ? _current : 0); return; }
                if (_video.isPlaying) { _video.Pause(); ShowPaused(true); }
                else { _video.Play(); ShowPaused(false); }
            }

            void Highlight()
            {
                for (int i = 0; i < _chips.Count; i++) _chips[i].EnableInClassList("rp-chip-on", i == _current);
                if (_current < _chips.Count)
                {
                    VisualElement chip = _chips[_current];
                    // The current putt in the middle of the strip (as far as the ends allow).
                    _strip.schedule.Execute(() =>
                    {
                        if (chip.panel == null) return;
                        float x = chip.layout.x - (_strip.contentViewport.layout.width - chip.layout.width) * 0.5f;
                        float max = Mathf.Max(0f, _strip.contentContainer.layout.width - _strip.contentViewport.layout.width);
                        _strip.scrollOffset = new Vector2(Mathf.Clamp(x, 0f, max), 0f);
                    });
                }
            }

            void ShowPaused(bool on) => _paused.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

            void Busy(bool on)
            {
                if (on && _spinner == null) { _spinner = Spinner.Big(); _load.Add(_spinner); }
                else if (!on && _spinner != null) { _spinner.RemoveFromHierarchy(); _spinner = null; }
            }

            void Failed()
            {
                if (_closed) return;
                Busy(false);
                _load.Clear();
                _load.Add(Ui.Text("Could not load the video.", "muted", Ui.Regular));
            }

            void Close()
            {
                if (_closed) return;
                _closed = true;
                _dim.RemoveFromHierarchy();
                UnityEngine.Object.Destroy(_go);
                if (_texture != null) _texture.Release();
            }
        }
    }
}
