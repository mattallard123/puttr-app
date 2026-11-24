// Profile photo and home town (puttr-app ProfilePhotoPicker, SetUserLocation).
//
// Photo: the library (iOS PHPicker in Plugins/iOS/PuttrPhoto.m, Android's photo picker in
// PuttrPhotoActivity.java), turned upright (EXIF) and cut to its middle square, 300 x 300 as a JPEG, then POST /profile/savePhoto
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
            src = Upright(src, ExifOrientation(image));
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
            src = Upright(src, ExifOrientation(image));
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

        /// <summary>The JPEG's EXIF orientation (1 to 8), or 1. Android hands over the camera's bytes as they are,
        /// sideways with a rotation tag; Texture2D.LoadImage ignores the tag, so it is applied here.</summary>
        public static int ExifOrientation(byte[] jpg)
        {
            try
            {
                if (jpg == null || jpg.Length < 4 || jpg[0] != 0xFF || jpg[1] != 0xD8) return 1;
                int i = 2;
                while (i + 4 < jpg.Length && jpg[i] == 0xFF)
                {
                    int marker = jpg[i + 1], length = (jpg[i + 2] << 8) | jpg[i + 3];
                    if (marker == 0xDA || length < 2) return 1;   // the image data: no EXIF before it
                    int at = i + 4;
                    if (marker == 0xE1 && at + 14 < jpg.Length && jpg[at] == 'E' && jpg[at + 1] == 'x' && jpg[at + 2] == 'i' && jpg[at + 3] == 'f')
                    {
                        int tiff = at + 6;
                        bool little = jpg[tiff] == 'I';
                        int U16(int o) => little ? jpg[o] | (jpg[o + 1] << 8) : (jpg[o] << 8) | jpg[o + 1];
                        int U32(int o) => little ? jpg[o] | (jpg[o + 1] << 8) | (jpg[o + 2] << 16) | (jpg[o + 3] << 24) : (jpg[o] << 24) | (jpg[o + 1] << 16) | (jpg[o + 2] << 8) | jpg[o + 3];
                        int ifd = tiff + U32(tiff + 4);
                        int count = U16(ifd);
                        for (int e = 0; e < count; e++)
                        {
                            int entry = ifd + 2 + e * 12;
                            if (entry + 12 > jpg.Length) break;
                            if (U16(entry) == 0x0112) { int v = U16(entry + 8); return v >= 1 && v <= 8 ? v : 1; }
                        }
                        return 1;
                    }
                    i += 2 + length;
                }
            }
            catch (Exception) { /* a broken tag: shown as stored */ }
            return 1;
        }

        /// <summary>The picture turned (and mirrored) the way its EXIF orientation says, so it shows upright.</summary>
        public static Texture2D Upright(Texture2D src, int orientation)
        {
            if (orientation <= 1 || orientation > 8) return src;
            int w = src.width, h = src.height;
            bool turned = orientation >= 5;
            int dw = turned ? h : w, dh = turned ? w : h;
            Color32[] from = src.GetPixels32(), to = new Color32[dw * dh];
            for (int dy = 0; dy < dh; dy++)
                for (int dx = 0; dx < dw; dx++)
                {
                    // Both counted from the top left; Unity's rows start at the bottom.
                    int sx, sy;
                    switch (orientation)
                    {
                        case 2: sx = w - 1 - dx; sy = dy; break;
                        case 3: sx = w - 1 - dx; sy = h - 1 - dy; break;
                        case 4: sx = dx; sy = h - 1 - dy; break;
                        case 5: sx = dy; sy = dx; break;
                        case 6: sx = dy; sy = h - 1 - dx; break;
                        case 7: sx = w - 1 - dy; sy = h - 1 - dx; break;
                        default: sx = w - 1 - dy; sy = dx; break;   // 8
                    }
                    to[(dh - 1 - dy) * dw + dx] = from[(h - 1 - sy) * w + sx];
                }
            var dst = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
            dst.SetPixels32(to);
            dst.Apply();
            Destroy(src);
            return dst;
        }

        /// <summary>A Kid profile's photo: on this phone only (puttr-app utils/family.js family-photos/).</summary>
        public static string KidPhotoPath(string userId) => Path.Combine(Application.persistentDataPath, "family-photos", System.Text.RegularExpressions.Regex.Replace(userId ?? "", "[^A-Za-z0-9_-]", "") + ".jpg");

        /// <summary>Chooses, shrinks and saves the Player's photo; done(true) when the profile has the new one.</summary>
        /// <param name="picked">Called once a photo is chosen, before it is saved (for a loading state).</param>
        public static void ChangeProfilePhoto(App app, Action<bool> done, Action picked = null)
        {
            Pick(path =>
            {
                if (path == null) { done(false); return; }
                picked?.Invoke();
                byte[] jpg;
                // The middle square, upright: an avatar is round, so a tall or wide photo is never squashed into it.
                try { jpg = Square(File.ReadAllBytes(path), 300, 92); } catch (Exception) { jpg = null; }
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

    /// <summary>SetUserLocation: "Search city or town...", Google's city suggestions as you type; a pick sets the home town and its lat/lng.</summary>
    public sealed class CityScreen : AppScreen
    {
        // puttr-app SetUserLocation's key (react-native-google-places-autocomplete: the legacy Places API, the
        // key in the query, no bundle id header). Keys.cs is local only.
        const string Key = Keys.Places;
        const string Root_ = "https://maps.googleapis.com/maps/api/place/";
        const int PauseMs = 300;
        readonly App _app;
        readonly Action<string, double?, double?> _picked;
        readonly VisualElement _list;
        readonly TextField _query;
        readonly string _session = Guid.NewGuid().ToString("N");
        int _asked;
        bool _picking;
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
            // The phone's keyboard types straight into the field: with the system text bar above the keyboard,
            // iOS hands the text over only on Done, so nothing was searched while typing.
            _query.textEdition.hideMobileInput = true;
            _query.AddToClassList("field");
            _list = new VisualElement();
            current = current == "null" ? "" : current ?? "";
            body.Add(Ui.Box("pad", Ui.Text("Search for your city or town", "body-2", Ui.Regular), current.Length > 0 ? Ui.Text(current, "chip chip-green", Ui.Bold).Margin(8) : null, _query.Face(Ui.Medium).Margin(12)));
            body.Add(_list);
            _query.RegisterValueChangedCallback(e => Typed(e.newValue));
            // Ready to type as soon as the page is up.
            _query.schedule.Execute(() => _query.Focus()).StartingIn(350);
            // -app2CityQuery "Austin": types that into the search (for checking it from a script).
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-app2CityQuery") { string q = args[i + 1]; _query.schedule.Execute(() => _query.value = q).StartingIn(600); }
        }

        void Typed(string text)
        {
            int me = ++_asked;
            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 2) { _list.Clear(); return; }
            // A short pause after typing, so each key is not a request.
            _query.schedule.Execute(() => { if (me == _asked) App.Run(Search(text.Trim(), me)); }).StartingIn(PauseMs);
        }

        IEnumerator Search(string text, int me)
        {
            if (_list.childCount == 0) _list.Add(Ui.Box("pad center-box", Spinner.Small()));
            Perf.Wake(1f);
            string url = Root_ + "autocomplete/json?input=" + UnityWebRequest.EscapeURL(text) + "&key=" + Key + "&language=en&types=" + UnityWebRequest.EscapeURL("(cities)") + "&sessiontoken=" + _session;
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (me != _asked || _picking) yield break;
                _list.Clear();
                Perf.Wake(0.5f);
                if (req.result != UnityWebRequest.Result.Success) { Perf.Note("City search failed: " + req.error); _list.Add(Page.Note("Couldn't search. Check your connection.")); yield break; }
                object data = J.Parse(req.downloadHandler.text);
                string status = J.Str(data, "status");
                List<object> places = J.List(J.Get(data, "predictions"));
                if (places.Count == 0)
                {
                    if (status != "ZERO_RESULTS") Perf.Note("City search: " + status + " " + J.Str(data, "error_message"));
                    _list.Add(Page.Note(status == "ZERO_RESULTS" || status == "OK" ? "No cities match." : "Search is not available right now."));
                    yield break;
                }
                VisualElement card = Ui.Box("glass set-card");
                foreach (object p in places)
                {
                    object place = p;
                    object fmt = J.Get(p, "structured_formatting");
                    string main = J.Str(fmt, "main_text"), rest = J.Str(fmt, "secondary_text");
                    if (main.Length == 0) main = J.Str(p, "description");
                    VisualElement words = Ui.Box("grow set-words", Ui.Text(main, "set-title", Ui.Bold), rest.Length > 0 ? Ui.Text(rest, "set-sub", Ui.Regular) : null);
                    card.Add(Ui.Box("set-row", Ui.Icon("MapPin", 24, Ui.Faint), words).OnTap(() => { if (!_picking) App.Run(Pick(place)); }));
                }
                if (card.childCount > 0) card[card.childCount - 1].AddToClassList("set-row-last");
                _list.Add(Ui.Box("pad", card));
                // -app2CityPick 0: picks that suggestion once they are up (for checking it from a script).
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-app2CityPick");
                if (!_devPicked && at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int n) && n < places.Count) { _devPicked = true; App.Run(Pick(places[n])); }
            }
        }
        static bool _devPicked;

        IEnumerator Pick(object place)
        {
            _picking = true;
            _query.Blur();
            _list.Clear();
            _list.Add(Ui.Box("pad center-box", Spinner.Small()));
            Perf.Wake(1f);
            // "Austin, TX" from the first two terms; else the whole description.
            List<object> terms = J.List(J.Get(place, "terms"));
            string name = terms.Count >= 2 ? J.Str(terms[0], "value") + ", " + J.Str(terms[1], "value") : J.Str(place, "description");
            double? lat = null, lng = null;
            LastCountry = LastRegion = null;
            using (UnityWebRequest req = UnityWebRequest.Get(Root_ + "details/json?key=" + Key + "&placeid=" + UnityWebRequest.EscapeURL(J.Str(place, "place_id")) + "&language=en&fields=geometry,address_component&sessiontoken=" + _session))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    object result = J.Get(J.Parse(req.downloadHandler.text), "result");
                    object loc = J.Get(J.Get(result, "geometry"), "location");
                    foreach (object part in J.List(J.Get(result, "address_components")))
                    {
                        List<object> types = J.List(J.Get(part, "types"));
                        if (types.Contains("country")) LastCountry = J.Str(part, "short_name");
                        if (types.Contains("administrative_area_level_1")) LastRegion = J.Str(part, "short_name");
                    }
                    if (J.Get(loc, "lat") != null) { lat = J.Num(loc, "lat"); lng = J.Num(loc, "lng"); }
                }
                else Perf.Note("City details failed: " + req.error);
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

        /// <summary>
        /// A message and a link through the system share sheet (react-native-share Share.open with message and url).
        /// Off the phone there is no sheet, so the words and the link go to the clipboard and false comes back
        /// (the caller says it was copied).
        /// </summary>
        public static bool Text(string text, string url, string subject = null)
        {
#if UNITY_IOS && !UNITY_EDITOR
            PuttrShare_Text(text ?? "", url ?? "");
            return true;
#elif UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var share = new AndroidJavaClass("co.puttr.app2.PuttrShare"))
                share.CallStatic("text", activity, text ?? "", url ?? "", subject ?? "");
            return true;
#else
            GUIUtility.systemCopyBuffer = string.IsNullOrEmpty(text) ? url : string.IsNullOrEmpty(url) ? text : text + " " + url;
            return false;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void PuttrShare_Text(string text, string url);
#endif
    }

}
