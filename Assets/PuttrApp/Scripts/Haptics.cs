// A short tap from the phone's haptic engine, where puttr-app used react-native-haptic-feedback.
//
// The React Native app taps in two places only: adding a putt in the game creator (impactHeavy, EditGame.js)
// and choosing a putter as your gamer (impactMedium, GamerPutterCard.js). Buttons in general do not tap, so
// Ui.Button and OnTap do not either.
//
//   iOS      Plugins/iOS/PuttrHaptics.m: UIImpactFeedbackGenerator / UINotificationFeedbackGenerator
//   Android  PuttrHaptics.java: View.performHapticFeedback, which needs no permission and follows the Player's
//            own touch feedback setting (react-native-haptic-feedback's ignoreAndroidSystemSettings: false)
//   elsewhere (the editor, desktop, TVs)  nothing
using UnityEngine;

namespace PuttrApp
{
    public static class Haptics
    {
        // The numbers the native side reads: keep the three in step (PuttrHaptics.m, PuttrHaptics.java).
        const int KindLight = 0, KindMedium = 1, KindHeavy = 2, KindSuccess = 3, KindWarning = 4, KindError = 5;

        /// <summary>A light tap (impactLight).</summary>
        public static void Light() => Play(KindLight);
        /// <summary>A medium tap (impactMedium): choosing your gamer.</summary>
        public static void Medium() => Play(KindMedium);
        /// <summary>A heavy tap (impactHeavy): adding a putt in the game creator.</summary>
        public static void Heavy() => Play(KindHeavy);
        /// <summary>It worked (notificationSuccess).</summary>
        public static void Success() => Play(KindSuccess);
        /// <summary>Take care (notificationWarning).</summary>
        public static void Warning() => Play(KindWarning);
        /// <summary>It did not work (notificationError).</summary>
        public static void Error() => Play(KindError);

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrHaptics_Play(int kind);

        static void Play(int kind)
        {
            if (Fixtures.On) return;
            PuttrHaptics_Play(kind);
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaClass _java;

        static void Play(int kind)
        {
            if (Fixtures.On || Tv.On) return;   // a TV has nothing to shake
            // A tap is a nicety: a phone that cannot make one must never break the screen that asked for it.
            try
            {
                if (_java == null) _java = new AndroidJavaClass("co.puttr.app2.PuttrHaptics");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _java.CallStatic("play", activity, kind);
            }
            catch (System.Exception) { }
        }
#else
        static void Play(int kind) { }
#endif
    }
}
