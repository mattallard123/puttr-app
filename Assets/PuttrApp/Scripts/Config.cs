// Where the app talks to. The same values puttr-app ships with (constants/Strings.js and
// tv/src/config.js): the public Firebase web key and the TV service token are client
// identifiers, not secrets, but keep them in this one file.
namespace PuttrApp
{
    public static class Config
    {
        public const string ApiUrl = "https://api.puttr.co";
        public const string FirebaseWebApiKey = "AIzaSyA2ZHh0-w8Luk6J8dZzqTaFt6WBW57Ycj4";
        /// <summary>Service token for the /tv/session sign-in (no user until the Puttr app claims the code).</summary>
        public const string TvToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJuYW1lIjoiVFYiLCJpYXQiOjE2MjY5Nzk4MjEsImV4cCI6ODUzODk3OTgyMX0.HWFzPHFPBSXpvZgTwogmXi8L1gvAppFuI3oHB42HFsQ";
        /// <summary>Google sign-in on iOS: the OAuth client for co.puttr.ios, from puttr-app's GoogleService-Info.plist.</summary>
        public const string GoogleIosClientId = "636312820806-a5dg6h420tc1qdr77mriu07rdpg6dnic.apps.googleusercontent.com";
        /// <summary>Google sign-in on Android: the Firebase project's web client (google-services.json, client_type 3), which Credential Manager issues ID tokens for.</summary>
        public const string GoogleWebClientId = "636312820806-o0og6mk4l5vbmig7coab3pckd8ek8n2v.apps.googleusercontent.com";
        public const string GoogleIosRedirectScheme = "com.googleusercontent.apps.636312820806-a5dg6h420tc1qdr77mriu07rdpg6dnic";
        /// <summary>Agora project the app's in-game recording uses (puttr-app's AGORA_APP_ID: a client identifier).</summary>
        public const string AgoraAppId = "0c95af442f6a4537a527abfa7b84a249";
        public const string Version = "0.3.0";
    }
}
