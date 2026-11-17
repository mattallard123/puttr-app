// Profile photo and home town (puttr-app ProfilePhotoPicker, SetUserLocation).
//
// Photo: the library (iOS PHPicker in Plugins/iOS/PuttrPhoto.m, Android's photo picker in
// PuttrPhotoActivity.java), made at most 200 x 200 as a JPEG, then POST /profile/savePhoto
// (multipart: file "<uid>_<rand>_profile.jpg", userId), as the app. A Kid profile's photo never leaves
// the phone (the API refuses it too), so it is kept on the device only.
//
// Home town: Google Places (legacy) autocomplete for cities, then the place's details for lat/lng;
// saved with the rest of the profile (POST /profile/save), as the app.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class PhotoPicker : MonoBehaviour
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PuttrPhoto_Pick(string target);
        [DllImport("__Internal")] static extern void PuttrPhoto_PickKind(string target, int video);
        [DllImport("__Internal")] static extern void PuttrPhoto_Camera(string target);
        static void CameraNative(string target) => PuttrPhoto_Camera(target);
        public static bool Supported => true;
        static void PickVideoNative(string target) => PuttrPhoto_PickKind(target, 1);
#elif UNITY_ANDROID && !UNITY_EDITOR
        static void PickVideoNative(string target)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var picker = new AndroidJavaClass("co.puttr.app2.PuttrPhotoActivity"))
                picker.CallStatic("pickKind", activity, target, true);
        }
        static void CameraNative(string target)
        {
            Puttr.AndroidPermissions.Ask(new[] { "android.permission.CAMERA" }, () =>
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var picker = new AndroidJavaClass("co.puttr.app2.PuttrPhotoActivity"))
                    picker.CallStatic("takePhoto", activity, target);
            });
        }
        static void PuttrPhoto_Pick(string target)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var picker = new AndroidJavaClass("co.puttr.app2.PuttrPhotoActivity"))
                picker.CallStatic("pick", activity, target);
        }
        public static bool Supported => true;
#else
        static void PuttrPhoto_Pick(string target) { }
        static void PickVideoNative(string target) { }
        static void CameraNative(string target) { }
        public static bool Supported => false;
#endif
        const string ObjectName = "PuttrPhoto";
        static PhotoPicker _one;
        Action<string> _done;

        /// <summary>Opens the photo library; done gets a file path, or null if nothing was chosen.</summary>
        public static void Pick(Action<string> done)
        {
            if (!Supported) { done(null); return; }
            if (_one == null) { var go = new GameObject(ObjectName); DontDestroyOnLoad(go); _one = go.AddComponent<PhotoPicker>(); }
            _one._done = done;
            PuttrPhoto_Pick(ObjectName);
        }

        /// <summary>A video from the library; done gets its file path, or null.</summary>
        public static void PickVideo(Action<string> done)
        {
            if (!Supported) { done(null); return; }
            if (_one == null) { var go = new GameObject(ObjectName); DontDestroyOnLoad(go); _one = go.AddComponent<PhotoPicker>(); }
            _one._done = done;
            PickVideoNative(ObjectName);
        }

        /// <summary>The system camera, front facing (CrewPicker takePhoto); done gets a file path, "nocamera", or null.</summary>
        public static void TakePhoto(Action<string> done)
        {
            if (!Supported) { done("nocamera"); return; }
            if (_one == null) { var go = new GameObject(ObjectName); DontDestroyOnLoad(go); _one = go.AddComponent<PhotoPicker>(); }
            _one._done = done;
            CameraNative(ObjectName);
        }

        /// <summary>The middle square at size x size, as a JPEG (ImageResizer 400 x 400, quality 90).</summary>
        public static byte[] Square(byte[] image, int size, int quality)
        {
            var src = new Texture2D(2, 2);
            if (!src.LoadImage(image)) { Destroy(src); return null; }
            int side = Mathf.Min(src.width, src.height);
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt, new Vector2(side / (float)src.width, side / (float)src.height), new Vector2((1 - side / (float)src.width) / 2, (1 - side / (float)src.height) / 2));
            RenderTexture was = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(size, size, TextureFormat.RGB24, false);
            dst.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            dst.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            byte[] jpg = dst.EncodeToJPG(quality);
            Destroy(src); Destroy(dst);
            return jpg;
        }

        void OnPhoto(string path) { Action<string> d = _done; _done = null; d?.Invoke(string.IsNullOrEmpty(path) ? null : path); }

        /// <summary>At most size x size, the shape kept, as a JPEG (ImageResizer.createResizedImage(uri, 200, 200, 'JPEG', 100)).</summary>
        public static byte[] Shrink(byte[] image, int size, int quality)
        {
            var src = new Texture2D(2, 2);
            if (!src.LoadImage(image)) { Destroy(src); return null; }
            float k = Mathf.Min(1f, size / (float)Mathf.Max(src.width, src.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(src.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(src.height * k));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture was = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            byte[] jpg = dst.EncodeToJPG(quality);
            Destroy(src); Destroy(dst);
            return jpg;
        }

        /// <summary>A Kid profile's photo: on this phone only (puttr-app utils/family.js family-photos/).</summary>
        public static string KidPhotoPath(string userId) => Path.Combine(Application.persistentDataPath, "family-photos", System.Text.RegularExpressions.Regex.Replace(userId ?? "", "[^A-Za-z0-9_-]", "") + ".jpg");

        /// <summary>Chooses, shrinks and saves the Player's photo; done(true) when the profile has the new one.</summary>
        public static void ChangeProfilePhoto(App app, Action<bool> done)
        {
            Pick(path =>
            {
                if (path == null) { done(false); return; }
                byte[] jpg;
                try { jpg = Shrink(File.ReadAllBytes(path), 200, 100); } catch (Exception) { jpg = null; }
                if (jpg == null) { app.Toast("Couldn't read that photo."); done(false); return; }
                string uid = J.Str(app.Profile, "userId", Auth.Uid);
                if (Kid.IsKid(app.Profile))
                {
                    string kid = KidPhotoPath(uid);
                    Directory.CreateDirectory(Path.GetDirectoryName(kid));
                    File.WriteAllBytes(kid, jpg);
                    app.Toast("Saved on this phone only.");
                    done(true);
                    return;
                }
                App.Run(Upload(uid, jpg, ok =>
                {
                    if (!ok) { app.Toast("Couldn't save the photo. Please try again."); done(false); return; }
                    App.Run(Api.Get("/profile/", d => { app.Profile = J.First(d); done(true); }, _ => done(true)));
                }));
            });
        }

        public static IEnumerator UploadFor(string uid, byte[] jpg, Action<bool> done) => Upload(uid, jpg, done);

        static IEnumerator Upload(string uid, byte[] jpg, Action<bool> done)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", jpg, uid + "_" + UnityEngine.Random.Range(0, 100000000) + "_profile.jpg", "image/jpeg"),
                new MultipartFormDataSection("userId", uid),
            };
            using (UnityWebRequest req = UnityWebRequest.Post(Config.ApiUrl + "/profile/savePhoto", form))
            {
                req.timeout = 60;
                yield return req.SendWebRequest();
                done(req.result == UnityWebRequest.Result.Success);
            }
        }
    }

    /// <summary>SetUserLocation: "Search city or town...", Google's city suggestions; a pick sets the home town and its lat/lng.</summary>
    public sealed class CityScreen : AppScreen
    {
        // puttr-app SetUserLocation's key (a client key, restricted on Google's side).
        const string Key = "AIzaSyA2ZHh0-w8Luk6J8dZzqTaFt6WBW57Ycj4";
        const string Root_ = "https://maps.googleapis.com/maps/api/place/";
        readonly App _app;
        readonly Action<string, double?, double?> _picked;
        readonly VisualElement _list;
        readonly TextField _query;
        int _asked;
        /// <summary>The last pick's country and state codes (address_components short_name), for the coach and Junior applications.</summary>
        public static string LastCountry, LastRegion;

        public CityScreen(App app, string current, Action<string, double?, double?> picked)
        {
            _app = app;
            _picked = picked;
            Root = Page.Make(app, "Set your location", out VisualElement body);
            _query = new TextField();
            _query.textEdition.placeholder = "Search city or town...";
            _query.textEdition.hidePlaceholderOnFocus = false;
            _query.AddToClassList("field");
            _list = new VisualElement();
            body.Add(Ui.Box("pad", Ui.Text("Search for your city or town", "sub", Ui.Regular), current.Length > 0 ? Ui.Text(current, "chip chip-green", Ui.Bold).Margin(8) : null, _query.Face(Ui.Medium).Margin(10)));
            body.Add(_list);
            _query.RegisterValueChangedCallback(e =>
            {
                int me = ++_asked;
                // A short pause after typing, so each key is not a request.
                _query.schedule.Execute(() => { if (me == _asked) App.Run(Search(e.newValue, me)); }).StartingIn(250);
            });
        }

        IEnumerator Search(string text, int me)
        {
            if (string.IsNullOrWhiteSpace(text)) { _list.Clear(); yield break; }
            string url = Root_ + "autocomplete/json?input=" + UnityWebRequest.EscapeURL(text) + "&key=" + Key + "&language=en&types=(cities)";
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (me != _asked) yield break;
                _list.Clear();
                if (req.result != UnityWebRequest.Result.Success) { _list.Add(Page.Note("Couldn't search. Check your connection.")); yield break; }
                object data = J.Parse(req.downloadHandler.text);
                List<object> places = J.List(J.Get(data, "predictions"));
                if (places.Count == 0) { _list.Add(Page.Note(J.Str(data, "status") == "ZERO_RESULTS" ? "No cities match." : "Search is not available right now (" + J.Str(data, "status") + ").")); yield break; }
                VisualElement card = Ui.Box("glass set-card");
                foreach (object p in places)
                {
                    object place = p;
                    card.Add(Rows.Row("Target", J.Str(p, "description"), () => App.Run(Pick(place))));
                }
                _list.Add(Ui.Box("pad", card));
            }
        }

        IEnumerator Pick(object place)
        {
            // "Austin, TX" from the first two terms; else the whole description.
            List<object> terms = J.List(J.Get(place, "terms"));
            string name = terms.Count >= 2 ? J.Str(terms[0], "value") + ", " + J.Str(terms[1], "value") : J.Str(place, "description");
            double? lat = null, lng = null;
            using (UnityWebRequest req = UnityWebRequest.Get(Root_ + "details/json?key=" + Key + "&placeid=" + UnityWebRequest.EscapeURL(J.Str(place, "place_id")) + "&language=en&fields=geometry,address_component"))
            {
                yield return req.SendWebRequest();
                object result = J.Get(J.Parse(req.downloadHandler.text), "result");
                object loc = J.Get(J.Get(result, "geometry"), "location");
                LastCountry = LastRegion = null;
                foreach (object part in J.List(J.Get(result, "address_components")))
                {
                    List<object> types = J.List(J.Get(part, "types"));
                    if (types.Contains("country")) LastCountry = J.Str(part, "short_name");
                    if (types.Contains("administrative_area_level_1")) LastRegion = J.Str(part, "short_name");
                }
                if (J.Get(loc, "lat") != null) { lat = J.Num(loc, "lat"); lng = J.Num(loc, "lng"); }
            }
            _picked(name, lat, lng);
            _app.Pop();
        }
    }
}

namespace PuttrApp
{
    /// <summary>The system share sheet for a file (Plugins/iOS/PuttrPhoto.m PuttrShare_File, Android PuttrShare.java).</summary>
    public static class Share
    {
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrShare_File(string path);
        public static void File(string path, string type) => PuttrShare_File(path);
#elif UNITY_ANDROID && !UNITY_EDITOR
        public static void File(string path, string type)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var share = new AndroidJavaClass("co.puttr.app2.PuttrShare"))
                share.CallStatic("file", activity, path, type);
        }
#else
        public static void File(string path, string type) => Application.OpenURL("file://" + path);
#endif
    }

}
