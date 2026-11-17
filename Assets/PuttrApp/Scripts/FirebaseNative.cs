// The native Firebase Auth SDK on iPhone (Assets/Plugins/iOS/PuttrFirebase.m) and Android
// (Plugins/Android/PuttrApp.androidlib PuttrFirebase.java). Elsewhere (the editor, the Mac build)
// Supported is false and Auth falls back to Firebase's REST API, which signs in to the same
// accounts but keeps its own session.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PuttrApp
{
    public sealed class FirebaseNative : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int PuttrFirebase_Configure();
        [DllImport("__Internal")] static extern string PuttrFirebase_CurrentUid();
        [DllImport("__Internal")] static extern void PuttrFirebase_GetIdToken(int force, string target, string request);
        [DllImport("__Internal")] static extern void PuttrFirebase_SignInEmail(string email, string password, string target, string request);
        [DllImport("__Internal")] static extern void PuttrFirebase_SignInCredential(string provider, string idToken, string secret, string target, string request);
        [DllImport("__Internal")] static extern void PuttrFirebase_SignInCustomToken(string token, string target, string request);
        [DllImport("__Internal")] static extern void PuttrFirebase_CreateUser(string email, string password, string target, string request);
        [DllImport("__Internal")] static extern int PuttrFirebase_SignOut();
        const bool Built = true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        // Android: Plugins/Android/PuttrApp.androidlib PuttrFirebase.java, the same answers as the iPhone plugin.
        static AndroidJavaClass _java;
        static AndroidJavaClass Java => _java ?? (_java = new AndroidJavaClass("co.puttr.app2.PuttrFirebase"));
        static int PuttrFirebase_Configure()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                return Java.CallStatic<int>("configure", activity);
        }
        static string PuttrFirebase_CurrentUid() => Java.CallStatic<string>("currentUid");
        static void PuttrFirebase_GetIdToken(int force, string target, string request) => Java.CallStatic("getIdToken", force != 0, target, request);
        static void PuttrFirebase_SignInEmail(string email, string password, string target, string request) => Java.CallStatic("signInEmail", email, password, target, request);
        static void PuttrFirebase_SignInCredential(string provider, string idToken, string secret, string target, string request) => Java.CallStatic("signInCredential", provider, idToken, secret, target, request);
        static void PuttrFirebase_SignInCustomToken(string token, string target, string request) => Java.CallStatic("signInCustomToken", token, target, request);
        static void PuttrFirebase_CreateUser(string email, string password, string target, string request) => Java.CallStatic("createUser", email, password, target, request);
        static int PuttrFirebase_SignOut() => Java.CallStatic<int>("signOut");
        const bool Built = true;
#else
        static int PuttrFirebase_Configure() => 0;
        static string PuttrFirebase_CurrentUid() => null;
        static void PuttrFirebase_GetIdToken(int force, string target, string request) { }
        static void PuttrFirebase_SignInEmail(string email, string password, string target, string request) { }
        static void PuttrFirebase_SignInCredential(string provider, string idToken, string secret, string target, string request) { }
        static void PuttrFirebase_SignInCustomToken(string token, string target, string request) { }
        static void PuttrFirebase_CreateUser(string email, string password, string target, string request) { }
        static int PuttrFirebase_SignOut() => 0;
        const bool Built = false;
#endif

        const string ObjectName = "PuttrFirebase";
        static FirebaseNative _one;
        static bool _running;
        static int _next;
        // What each request in flight will be answered with: (value, error code).
        static readonly Dictionary<string, Action<string, string>> _waiting = new Dictionary<string, Action<string, string>>();

        /// <summary>True once the SDK is running on this device.</summary>
        public static bool Supported => _running;

        /// <summary>Starts the SDK. Call once, before anything else.</summary>
        public static void Start_()
        {
            if (!Built || _running) return;
            if (_one == null)
            {
                var go = new GameObject(ObjectName);
                DontDestroyOnLoad(go);
                _one = go.AddComponent<FirebaseNative>();
            }
            _running = PuttrFirebase_Configure() == 1;
            Perf.Note("firebase sdk " + (_running ? "running" : "did not start: falling back to the web API"));
        }

        public static string CurrentUid => _running ? PuttrFirebase_CurrentUid() : null;

        static string Ask(Action<string, string> answer)
        {
            string id = (++_next).ToString();
            _waiting[id] = answer;
            return id;
        }

        public static void GetIdToken(bool force, Action<string, string> answer) => PuttrFirebase_GetIdToken(force ? 1 : 0, ObjectName, Ask(answer));
        public static void SignInEmail(string email, string password, Action<string, string> answer) => PuttrFirebase_SignInEmail(email, password, ObjectName, Ask(answer));
        public static void SignInCredential(string provider, string idToken, string secret, Action<string, string> answer) => PuttrFirebase_SignInCredential(provider, idToken, secret ?? "", ObjectName, Ask(answer));
        public static void CreateUser(string email, string password, Action<string, string> answer) => PuttrFirebase_CreateUser(email, password, ObjectName, Ask(answer));
        public static void SignInCustomToken(string token, Action<string, string> answer) => PuttrFirebase_SignInCustomToken(token, ObjectName, Ask(answer));
        public static void SignOut() { if (_running) PuttrFirebase_SignOut(); }

        // From the plugin: "<id>|ok|<value>" or "<id>|error|<code>|<message>".
        void OnFirebase(string message)
        {
            if (!Parse(message, out string id, out string value, out string error)) return;
            if (!_waiting.TryGetValue(id, out Action<string, string> answer)) return;
            _waiting.Remove(id);
            answer(value, error);
        }

        /// <summary>Splits an answer from the plugin. value is set on success, error (a code) on failure.</summary>
        public static bool Parse(string message, out string id, out string value, out string error)
        {
            id = value = error = null;
            if (string.IsNullOrEmpty(message)) return false;
            string[] parts = message.Split(new[] { '|' }, 3);
            if (parts.Length < 2) return false;
            id = parts[0];
            string rest = parts.Length > 2 ? parts[2] : "";
            if (parts[1] == "ok") { value = rest; return true; }
            int bar = rest.IndexOf('|');
            error = bar >= 0 ? rest.Substring(0, bar) : rest;
            if (error.Length == 0) error = "ERROR_UNKNOWN";
            return true;
        }
    }
}
