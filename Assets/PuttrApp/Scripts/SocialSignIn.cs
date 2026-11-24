// Sign in with Apple and with Google, on iOS and the Mac, without the Firebase or Google SDKs.
//
// Apple: the system sheet (Assets/Plugins/iOS/PuttrSignIn.m) gives an identity token, which
// Firebase takes with the nonce it was asked for.
// Google: the system's web sign-in sheet runs Google's OAuth page for the app's own iOS client
// (authorization code with PKCE, no secret), the code is swapped for an id token, and Firebase
// takes that.
//
// Either way the token goes to Firebase (Auth.SignInWithIdp): on iPhone to the native SDK,
// which then holds the session; on the Mac to Firebase's REST API (Auth.cs).
//
// The Mac (Assets/PuttrApp/Native~/PuttrSignInMac.m, built into Plugins/macOS/PuttrSignIn.bundle) has the same two
// sheets. A Mac plug-in cannot call back into Unity, so its answers are polled for (Update). Sign in with Apple is
// only in the Mac App Store build: it needs that build's entitlement, and a token made for co.puttr.ios.
//
// SPIKE: this signs existing Players in. A first-time sign-up also needs the profile row
// puttr-app's IntroScreen makes (initSocialProfile); that is not here yet.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PuttrApp
{
    public sealed class SocialSignIn : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PuttrSignIn_Apple(string hashedNonce, string target);
        [DllImport("__Internal")] static extern void PuttrSignIn_WebAuth(string url, string scheme, string target);
        public static bool Supported => true;
        public static bool AppleSupported => true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        // Android: Google through Credential Manager (PuttrSignIn.java); no Sign in with Apple.
        static void PuttrSignIn_Apple(string hashedNonce, string target) { }
        static void PuttrSignIn_WebAuth(string url, string scheme, string target) { }
        public static bool Supported => true;
        public static bool AppleSupported => false;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        [DllImport("PuttrSignIn")] static extern void PuttrSignInMac_Apple(string hashedNonce);
        [DllImport("PuttrSignIn")] static extern void PuttrSignInMac_WebAuth(string url, string scheme);
        [DllImport("PuttrSignIn")] static extern int PuttrSignInMac_Poll(byte[] buffer, int capacity);
        static void PuttrSignIn_Apple(string hashedNonce, string target) => PuttrSignInMac_Apple(hashedNonce);
        static void PuttrSignIn_WebAuth(string url, string scheme, string target) => PuttrSignInMac_WebAuth(url, scheme);
        public static bool Supported => true;
        public static bool AppleSupported => Application.identifier == MacStoreId;
        static readonly byte[] Buffer = new byte[16384];

        // The plug-in's answers: "apple \t token", "web \t url", "error \t why".
        void Update()
        {
            if (_done == null) return;
            int length;
            try { length = PuttrSignInMac_Poll(Buffer, Buffer.Length); }
            catch (Exception) { Finish("Sign-in is not available in this build."); return; }   // the bundle is missing
            if (length <= 0) return;
            string line = Encoding.UTF8.GetString(Buffer, 0, length);
            int tab = line.IndexOf('\t');
            string kind = tab < 0 ? line : line.Substring(0, tab), text = tab < 0 ? "" : line.Substring(tab + 1);
            if (kind == "apple") OnAppleToken(text);
            else if (kind == "web") OnWebAuth(text);
            else OnSignInError(text);
        }
#else
        static void PuttrSignIn_Apple(string hashedNonce, string target) { }
        static void PuttrSignIn_WebAuth(string url, string scheme, string target) { }
        public static bool Supported => false;
        public static bool AppleSupported => false;
#endif
        /// <summary>The Mac App Store build's bundle id: the iPhone app's (MacBuilds.StoreId).</summary>
        public const string MacStoreId = "co.puttr.ios";

        const string ObjectName = "PuttrSignIn";
        static SocialSignIn _one;

        Action<string> _done;   // null when signed in, "cancelled", or a sentence to show
        string _nonce, _verifier;

        static SocialSignIn One
        {
            get
            {
                if (_one != null) return _one;
                var go = new GameObject(ObjectName);
                DontDestroyOnLoad(go);
                return _one = go.AddComponent<SocialSignIn>();
            }
        }

        public static void Apple(Action<string> done)
        {
            SocialSignIn me = One;
            me._done = done;
            me._nonce = Random(32);
            PuttrSignIn_Apple(Sha256Hex(me._nonce), ObjectName);
        }

        public static void Google(Action<string> done)
        {
            SocialSignIn me = One;
            me._done = done;
            me._verifier = Random(64);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var java = new AndroidJavaClass("co.puttr.app2.PuttrSignIn"))
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                java.CallStatic("google", activity, Config.GoogleWebClientId, ObjectName);
            return;
#endif
            PuttrSignIn_WebAuth(GoogleUrl(Config.GoogleIosClientId, Config.GoogleIosRedirectScheme, me._verifier), Config.GoogleIosRedirectScheme, ObjectName);
        }

        // ── Called from the plugin ───────────────────────────────────────────

        void OnAppleToken(string identityToken) => StartCoroutine(Auth.SignInWithIdp("apple.com", identityToken, _nonce, Finish));

        void OnWebAuth(string callbackUrl)
        {
            string code = Query(callbackUrl, "code");
            if (code.Length == 0) { Finish(Query(callbackUrl, "error") == "access_denied" ? "cancelled" : "Google did not finish signing in."); return; }
            StartCoroutine(GoogleExchange(code));
        }

        void OnSignInError(string message) => Finish(message);

        // Android's Credential Manager hands over the ID token itself (for the web client): straight to Firebase.
        void OnGoogleToken(string idToken) => StartCoroutine(Auth.SignInWithIdp("google.com", idToken, null, Finish));

        void Finish(string problem)
        {
            Action<string> done = _done;
            _done = null;
            done?.Invoke(problem);
        }

        IEnumerator GoogleExchange(string code)
        {
            string form = "grant_type=authorization_code&client_id=" + UnityWebRequest.EscapeURL(Config.GoogleIosClientId) +
                          "&code=" + UnityWebRequest.EscapeURL(code) + "&code_verifier=" + _verifier +
                          "&redirect_uri=" + UnityWebRequest.EscapeURL(Config.GoogleIosRedirectScheme + ":/oauth2redirect");
            long status = 0;
            string text = null;
            yield return Api.Send("POST", "https://oauth2.googleapis.com/token", form, null, (s, t) => { status = s; text = t; }, "application/x-www-form-urlencoded");
            object tokens = J.Parse(text);
            string idToken = J.Str(tokens, "id_token");
            if (status != 200 || idToken.Length == 0) { Finish(status == 0 ? "No connection. Check your WiFi and try again." : "Google did not finish signing in."); yield break; }
            yield return Auth.SignInWithIdp("google.com", idToken, null, Finish, J.Str(tokens, "access_token"));
        }

        // ── The parts that can be tested without a phone ─────────────────────

        public static string GoogleUrl(string clientId, string redirectScheme, string verifier) =>
            "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&scope=openid%20email%20profile" +
            "&client_id=" + UnityWebRequest.EscapeURL(clientId) +
            "&redirect_uri=" + UnityWebRequest.EscapeURL(redirectScheme + ":/oauth2redirect") +
            "&code_challenge_method=S256&code_challenge=" + Challenge(verifier);

        /// <summary>PKCE: the base64url SHA-256 of the verifier, no padding.</summary>
        public static string Challenge(string verifier)
        {
            using (SHA256 sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>What Apple is given as the nonce: the hex SHA-256 of the one Firebase is later shown.</summary>
        public static string Sha256Hex(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text))) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string Random(int length)
        {
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var bytes = new byte[length];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(length);
            foreach (byte b in bytes) sb.Append(letters[b % letters.Length]);
            return sb.ToString();
        }

        /// <summary>One value from a URL's query, decoded; "" if it is not there.</summary>
        public static string Query(string url, string key)
        {
            int q = url.IndexOf('?');
            if (q < 0) return "";
            string query = url.Substring(q + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);
            foreach (string pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0 && pair.Substring(0, eq) == key) return UnityWebRequest.UnEscapeURL(pair.Substring(eq + 1));
            }
            return "";
        }
    }
}
