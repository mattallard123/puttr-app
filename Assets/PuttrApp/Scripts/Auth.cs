// Firebase Auth without the Firebase SDK, the way puttr-app's Apple TV target does it
// (tv/src/auth/firebaseRest.js): the Identity Toolkit REST API gives an id token and a refresh
// token, this keeps them and hands out fresh id tokens for Authorization: Bearer.
//
// Two ways in:
//   - email and password (accounts made with email)
//   - a code the Puttr app claims (/tv/session), for accounts made with Apple or Google
//
// On iPhone all of this goes through the native Firebase SDK instead (FirebaseNative.cs): the
// same SDK and version the React Native app uses, so a Player who updates from that app is found
// in the Keychain and is still signed in. The REST path below is what the editor, the Mac build
// and Android (for now) use; it keeps its session in PlayerPrefs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PuttrApp
{
    public static class Auth
    {
        const string Pref = "app2.auth";
        const long RefreshAheadSeconds = 120;

        [Serializable]
        class Session { public string uid, idToken, refreshToken; public long expiresAt; }

        static Session _session;

        /// <summary>True when the native Firebase SDK holds the session (iPhone).</summary>
        public static bool Native => FirebaseNative.Supported;
        static string _nativeUid;

        public static bool SignedIn => Native ? !string.IsNullOrEmpty(_nativeUid) : _session != null && !string.IsNullOrEmpty(_session.refreshToken);
        public static string Uid => Native ? _nativeUid : _session != null ? _session.uid : null;

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static void Restore()
        {
            FirebaseNative.Start_();
            if (Native)
            {
                // Whoever the Keychain remembers: this app's own sign-in, or the one made by the app it replaced.
                _nativeUid = FirebaseNative.CurrentUid;
                Perf.Note(string.IsNullOrEmpty(_nativeUid) ? "firebase: nobody signed in" : "firebase: found a signed-in Player in the Keychain");
                PlayerPrefs.DeleteKey(Pref); // a session from the earlier, SDK-less builds is of no use now
                return;
            }
            _session = null;
            string raw = PlayerPrefs.GetString(Pref, "");
            if (raw.Length == 0) return;
            try { _session = JsonUtility.FromJson<Session>(raw); } catch (Exception) { _session = null; }
            if (_session != null && string.IsNullOrEmpty(_session.refreshToken)) _session = null;
        }

        static void Save()
        {
            if (_session == null) PlayerPrefs.DeleteKey(Pref);
            else PlayerPrefs.SetString(Pref, JsonUtility.ToJson(_session));
            PlayerPrefs.Save();
        }

        public static void SignOut()
        {
            if (Native) { FirebaseNative.SignOut(); _nativeUid = null; return; }
            _session = null;
            Save();
        }

        /// <summary>done(null) when signed in, otherwise a sentence to show.</summary>
        // One native sign-in: wait for the SDK's answer ("uid|new" or "uid|known"), then say how it went.
        static IEnumerator NativeSignIn(Action<Action<string, string>> start, Action<string> done)
        {
            bool answered = false;
            string value = null, error = null;
            start((v, e) => { value = v; error = e; answered = true; });
            while (!answered) yield return null;
            if (error != null) { done?.Invoke(Friendly(400, error)); yield break; }
            int bar = value.IndexOf('|');
            _nativeUid = bar >= 0 ? value.Substring(0, bar) : value;
            LastSignInWasNew = value.EndsWith("|new");
            done?.Invoke(null);
        }

        /// <summary>True when the last sign-in made a new Firebase account (a first Apple or Google sign-in).</summary>
        public static bool LastSignInWasNew { get; private set; }

        public static IEnumerator SignInWithPassword(string email, string password, Action<string> done)
        {
            if (Native) { yield return NativeSignIn(answer => FirebaseNative.SignInEmail(email, password, answer), done); yield break; }
            string url = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" + Config.FirebaseWebApiKey;
            var body = new Dictionary<string, object> { { "email", email }, { "password", password }, { "returnSecureToken", true } };
            yield return Exchange(url, J.Write(body), done);
        }

        /// <summary>A new email and password account (EmailAuth "Create account").</summary>
        public static IEnumerator SignUpWithPassword(string email, string password, Action<string> done)
        {
            if (Native) { yield return NativeSignIn(answer => FirebaseNative.CreateUser(email, password, answer), done); yield break; }
            string url = "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + Config.FirebaseWebApiKey;
            var body = new Dictionary<string, object> { { "email", email }, { "password", password }, { "returnSecureToken", true } };
            yield return Exchange(url, J.Write(body), done);
        }

        /// <summary>sendPasswordResetEmail: Firebase emails a reset link. done(null) when sent.</summary>
        public static IEnumerator SendPasswordReset(string email, Action<string> done)
        {
            long status = 0;
            yield return Api.Send("POST", "https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key=" + Config.FirebaseWebApiKey,
                J.Write(new Dictionary<string, object> { { "requestType", "PASSWORD_RESET" }, { "email", email } }), null, (s, _) => status = s);
            done(status == 200 ? null : "Password reset failed. Check your email.");
        }

        /// <summary>What the signed-in Firebase account says about itself (from its ID token): email, name, picture, provider.</summary>
        public static IEnumerator Claims(Action<object> got)
        {
            string token = null;
            yield return Token(t => token = t);
            if (string.IsNullOrEmpty(token)) { got(null); yield break; }
            string[] parts = token.Split('.');
            if (parts.Length < 2) { got(null); yield break; }
            string b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            try { got(J.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)))); }
            catch (Exception) { got(null); }
        }

        /// <summary>Turns the custom token from /tv/session into a session.</summary>
        public static IEnumerator SignInWithCustomToken(string token, Action<string> done)
        {
            if (Native) { yield return NativeSignIn(answer => FirebaseNative.SignInCustomToken(token, answer), done); yield break; }
            string url = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key=" + Config.FirebaseWebApiKey;
            var body = new Dictionary<string, object> { { "token", token }, { "returnSecureToken", true } };
            yield return Exchange(url, J.Write(body), done);
        }

        /// <summary>Signs in with an id token from Apple ("apple.com") or Google ("google.com").
        /// rawNonce is the nonce whose SHA-256 Apple was given; null for Google.</summary>
        public static IEnumerator SignInWithIdp(string providerId, string idToken, string rawNonce, Action<string> done, string accessToken = null)
        {
            // The SDK wants Apple's raw nonce, and Google's access token beside the id token.
            if (Native) { yield return NativeSignIn(answer => FirebaseNative.SignInCredential(providerId, idToken, providerId == "google.com" ? accessToken : rawNonce, answer), done); yield break; }
            string url = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=" + Config.FirebaseWebApiKey;
            var body = new Dictionary<string, object>
            {
                { "postBody", IdpPostBody(providerId, idToken, rawNonce) },
                { "requestUri", "http://localhost" }, { "returnSecureToken", true }, { "returnIdpCredential", true },
            };
            yield return Exchange(url, J.Write(body), done);
        }

        public static string IdpPostBody(string providerId, string idToken, string rawNonce) =>
            "id_token=" + UnityWebRequest.EscapeURL(idToken) + "&providerId=" + providerId + (string.IsNullOrEmpty(rawNonce) ? "" : "&nonce=" + rawNonce);

        static IEnumerator Exchange(string url, string json, Action<string> done)
        {
            long status = 0;
            string text = null;
            yield return Api.Send("POST", url, json, null, (s, t) => { status = s; text = t; });
            object data = J.Parse(text);
            if (status != 200 || string.IsNullOrEmpty(J.Str(data, "idToken")))
            {
                done?.Invoke(Friendly(status, J.Str(J.Get(data, "error"), "message")));
                yield break;
            }
            string idToken = J.Str(data, "idToken");
            _session = new Session
            {
                idToken = idToken,
                refreshToken = J.Str(data, "refreshToken"),
                uid = J.Str(data, "localId", UidFromIdToken(idToken)),
                expiresAt = Now + (long)J.Num(data, "expiresIn", 3600),
            };
            if (string.IsNullOrEmpty(_session.uid)) _session.uid = UidFromIdToken(idToken);
            Save();
            done?.Invoke(null);
        }

        public static string Friendly(long status, string code)
        {
            if (status == 0) return "No connection. Check your WiFi and try again.";
            code = code ?? "";
            // The native SDK names its errors ERROR_...; the REST API does not. Same meanings.
            if (code == "ERROR_NETWORK_REQUEST_FAILED") return "No connection. Check your WiFi and try again.";
            if (code == "ERROR_WRONG_PASSWORD" || code == "ERROR_INVALID_CREDENTIAL" || code == "ERROR_USER_NOT_FOUND" || code == "ERROR_INVALID_LOGIN_CREDENTIALS") code = "INVALID_LOGIN_CREDENTIALS";
            else if (code == "ERROR_ACCOUNT_EXISTS_WITH_DIFFERENT_CREDENTIAL" || code == "ERROR_CREDENTIAL_ALREADY_IN_USE" || code == "ERROR_EMAIL_ALREADY_IN_USE") code = "EMAIL_EXISTS";
            else if (code.StartsWith("ERROR_")) code = code.Substring(6);
            if (code.StartsWith("INVALID_LOGIN_CREDENTIALS") || code.StartsWith("INVALID_PASSWORD") || code.StartsWith("EMAIL_NOT_FOUND"))
                return "That email and password do not match. If you joined with Apple or Google, sign in with the Puttr app below.";
            if (code.StartsWith("INVALID_EMAIL")) return "That does not look like an email address.";
            if (code.StartsWith("MISSING_PASSWORD")) return "Enter your password.";
            if (code.StartsWith("USER_DISABLED")) return "This account has been disabled.";
            if (code.StartsWith("FEDERATED_USER_ID_ALREADY_LINKED") || code.StartsWith("EMAIL_EXISTS")) return "That account is already linked to a different sign-in. Use the way you first joined.";
            if (code.StartsWith("TOO_MANY")) return "Too many tries. Wait a few minutes and try again.";
            return "Could not sign in (" + (code.Length > 0 ? code : "error " + status) + ").";
        }

        /// <summary>The id token for the API. Refreshes ahead of expiry; null when signed out.</summary>
        public static IEnumerator Token(Action<string> got)
        {
            if (Native)
            {
                // The SDK keeps its own token fresh and hands back the cached one when it is still good.
                if (string.IsNullOrEmpty(_nativeUid)) { got(null); yield break; }
                bool answered = false;
                string token = null, error = null;
                FirebaseNative.GetIdToken(false, (v, e) => { token = v; error = e; answered = true; });
                while (!answered) yield return null;
                // The account is gone for good: back to the sign-in screen. Anything else (no network) is not a sign-out.
                if (error == "ERROR_NO_USER" || error == "ERROR_USER_NOT_FOUND" || error == "ERROR_USER_DISABLED" || error == "ERROR_USER_TOKEN_EXPIRED" || error == "ERROR_INVALID_USER_TOKEN")
                {
                    Perf.Note("firebase: session ended (" + error + ")");
                    SignOut();
                    got(null);
                    yield break;
                }
                if (error != null) Perf.Note("firebase: no id token (" + error + ")");
                got(string.IsNullOrEmpty(token) ? null : token);
                yield break;
            }
            if (!SignedIn) { got(null); yield break; }
            if (Now < _session.expiresAt - RefreshAheadSeconds) { got(_session.idToken); yield break; }

            string url = "https://securetoken.googleapis.com/v1/token?key=" + Config.FirebaseWebApiKey;
            string form = "grant_type=refresh_token&refresh_token=" + UnityWebRequest.EscapeURL(_session.refreshToken);
            long status = 0;
            string text = null;
            yield return Api.Send("POST", url, form, null, (s, t) => { status = s; text = t; }, "application/x-www-form-urlencoded");
            if (_session == null) { got(null); yield break; } // signed out while this was in flight
            object data = J.Parse(text);
            string fresh = J.Str(data, "id_token");
            if (status == 200 && fresh.Length > 0)
            {
                _session.idToken = fresh;
                _session.refreshToken = J.Str(data, "refresh_token", _session.refreshToken);
                _session.expiresAt = Now + (long)J.Num(data, "expires_in", 3600);
                Save();
                got(fresh);
                yield break;
            }
            string code = J.Str(J.Get(data, "error"), "message");
            // A revoked or invalid refresh token means the sign-in is gone for good.
            if (code == "TOKEN_EXPIRED" || code == "USER_DISABLED" || code == "USER_NOT_FOUND" || code == "INVALID_REFRESH_TOKEN")
            {
                SignOut();
                got(null);
                yield break;
            }
            // A network failure: the stale token rather than nothing.
            got(_session.idToken);
        }

        /// <summary>The user id inside a Firebase id token (a JWT), or null.</summary>
        public static string UidFromIdToken(string idToken)
        {
            try
            {
                string[] parts = idToken.Split('.');
                if (parts.Length < 2) return null;
                string b64 = parts[1].Replace('-', '+').Replace('_', '/');
                while (b64.Length % 4 != 0) b64 += "=";
                object payload = J.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                string uid = J.Str(payload, "user_id");
                return uid.Length > 0 ? uid : J.Str(payload, "sub", null);
            }
            catch (Exception) { return null; }
        }
    }
}
