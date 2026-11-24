// What each TV platform needs at run time, below the TV mode itself (Tv.cs). App.Awake calls Init() when Tv.On.
//
//   Apple TV  the Siri Remote: Menu stays in the app below its first screen (AtRoot says when it may leave),
//             swipes and clicks are read as single d-pad steps (a swipe is one step, never two, never the
//             wrong way: RemoteSwipe) and handed to Tv.Press, and the touch
//             surface never lands as a tap. The EventSystem's own navigate / submit / cancel stay on too (a remote
//             this reader does not see still moves focus); Tv takes a press that arrives both ways once, and takes no
//             move from the EventSystem while this reader has the swipe (Tv.RemoteBusy).
//   Android TV  landscape, always. The remote's d-pad, OK and Back arrive as arrow keys, Enter and Escape,
//             which the EventSystem turns into navigation events as on a desktop: nothing to do here.
//   Samsung   the web build. The page (Assets/WebGLTemplates/PuttrTV) sends the remote's Back key to
//             App.TvBackKey; Exit() closes the Tizen app (Back on the first screen).
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PuttrApp
{
    public static class TvPlatform
    {
        /// <summary>True when the app is on its first screen, where the remote's Menu / Back may leave it. Set by the app.</summary>
        public static Func<bool> AtRoot;

        /// <summary>Apple TV: the remote drives the app's focus (Tv.Press). Turn off while a game reads the remote itself.</summary>
        public static bool RemoteToUi = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PuttrTv_Exit();
#endif

        static bool _started;

        public static void Init()
        {
            if (_started) return;
            _started = true;
#if UNITY_TVOS
            UnityEngine.tvOS.Remote.allowExitToHome = false;
            UnityEngine.tvOS.Remote.reportAbsoluteDpadValues = false;   // a swipe, not where the thumb rests
            UnityEngine.tvOS.Remote.allowRemoteRotation = false;
            UnityEngine.tvOS.Remote.touchesEnabled = false;             // the touch surface is a d-pad, never a pointer
            var go = new GameObject("Puttr TV remote");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SiriRemote>();
#elif UNITY_ANDROID && !UNITY_EDITOR
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
#endif
        }

        /// <summary>Leave the app (Back on the first screen). Apple TV apps may not quit themselves: Menu does it.</summary>
        public static void Exit()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            PuttrTv_Exit();
#elif UNITY_ANDROID && !UNITY_EDITOR
            Application.Quit();
#endif
        }

#if UNITY_TVOS
        sealed class SiriRemote : MonoBehaviour
        {
            readonly RemoteSwipe _swipe = new RemoteSwipe();
            float _keysAt = -9f, _padAt = -9f;
            // Tv.Trace: the frames of the swipe under way and what it stepped, logged as one line when it ends.
            readonly System.Text.StringBuilder _trace = new System.Text.StringBuilder();
            string _traceSteps = "";
            int _traceFrames;

            void Awake() { Tv.RemoteBusy = Busy; }

            // The surface is being touched, or a swipe has only just ended: the EventSystem's reading of it is not wanted.
            bool Busy()
            {
                if (!RemoteToUi) return false;
                Gamepad pad = Gamepad.current;
                if (pad == null) return false;
                return _swipe.Busy(Time.unscaledTime) || pad.leftStick.ReadValue() != Vector2.zero || pad.dpad.ReadValue() != Vector2.zero;
            }

            void Update()
            {
                bool root = AtRoot != null && AtRoot();
                UnityEngine.tvOS.Remote.allowExitToHome = root;
                if (!RemoteToUi || !Tv.On) return;
                ReadPad();
                ReadKeys();
            }

            void ReadPad()
            {
                Gamepad pad = Gamepad.current;
                if (pad == null) return;
                // The touch surface arrives as both a stick that follows the thumb and a d-pad that snaps
                // to eight ways (and lands on diagonals): RemoteSwipe makes one step of the two.
                Vector2 d = pad.dpad.ReadValue(), stick = pad.leftStick.ReadValue();
                // A click on the d-pad ring can go down and up inside one frame.
                if (pad.dpad.left.wasPressedThisFrame) d.x = -1f; else if (pad.dpad.right.wasPressedThisFrame) d.x = 1f;
                if (pad.dpad.down.wasPressedThisFrame) d.y = -1f; else if (pad.dpad.up.wasPressedThisFrame) d.y = 1f;
                float now = Time.unscaledTime;
                string step = _swipe.Read(stick, d, now);
                bool keysJustActed = now - _keysAt < 0.2f;
                if (step != null && keysJustActed) step = null;
                if (Tv.Trace) Trace(now, stick, d, step);
                if (step != null) { _padAt = now; Tv.Press(step); }
                if (keysJustActed) return;
                if (pad.buttonSouth.wasPressedThisFrame) { _padAt = now; Tv.Press("ok"); }
                else if (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame) { _padAt = now; Tv.Press("back"); }
            }

            // "[remote] down | time,stickX,stickY,dpadX,dpadY;...": one line a swipe, which RemoteSwipe.Replay reads back.
            void Trace(float now, Vector2 stick, Vector2 d, string step)
            {
                bool touched = stick != Vector2.zero || d != Vector2.zero;
                if (touched || _swipe.Busy(now))
                {
                    if (_traceFrames++ < 400) { if (_trace.Length > 0) _trace.Append(';'); _trace.Append(RemoteSwipe.Frame(now, stick, d)); }
                    if (step != null) _traceSteps += (_traceSteps == "" ? "" : ",") + step;
                    return;
                }
                if (_trace.Length == 0) return;
                Debug.Log("[remote] " + (_traceSteps == "" ? "none" : _traceSteps) + " | " + _trace);
                _trace.Clear(); _traceSteps = ""; _traceFrames = 0;
            }

            // The simulator's keyboard, and a remote that also arrives as arrow keys (taken once, not twice).
            void ReadKeys()
            {
                Keyboard kb = Keyboard.current;
                if (kb == null || Time.unscaledTime - _padAt < 0.2f) return;
                string key = kb.leftArrowKey.wasPressedThisFrame ? "left" : kb.rightArrowKey.wasPressedThisFrame ? "right"
                    : kb.upArrowKey.wasPressedThisFrame ? "up" : kb.downArrowKey.wasPressedThisFrame ? "down"
                    : kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame ? "ok"
                    : kb.escapeKey.wasPressedThisFrame ? "back" : null;
                if (key == null) return;
                _keysAt = Time.unscaledTime;
                Tv.Press(key);
            }
        }
#endif
    }
}
