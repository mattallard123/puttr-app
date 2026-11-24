// Puttr Club in-app subscriptions through IAPHUB (puttr-app Navigator.js + members/MembershipProducts.js).
// Native: Plugins/iOS/PuttrIaphub.swift (iaphub-ios-sdk 4.6.7), Plugins/Android PuttrIaphub.java
// (iaphub-android-sdk 5.2.0). Same app id and key as puttr-app.
//
// The IAPHUB user is the Firebase uid, or the account owner's for a managed profile (the managedBy claim),
// so purchases always belong to the owner. A purchase changes nothing here directly: IAPHUB's webhook
// sets the profile (puttr-api profile.iapWebhook), and "user update" re-reads it, as the app does.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public sealed class Iap : MonoBehaviour
    {
        const string AppId = "60abc90ec85aaa314c8b288d";
        static string ApiKey => Keys.IaphubApiKey;
        public const string Monthly = "membership_1month", Annual = "membership_1year";
        public const string ProMonthly = "pro_membership_1month", ProAnnual = "pro_membership_1year";

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PuttrIap_Start(string appId, string apiKey, string target);
        [DllImport("__Internal")] static extern void PuttrIap_Login(string userId, string request);
        [DllImport("__Internal")] static extern void PuttrIap_Logout();
        [DllImport("__Internal")] static extern void PuttrIap_Products(string request);
        [DllImport("__Internal")] static extern void PuttrIap_Buy(string sku, string request);
        [DllImport("__Internal")] static extern void PuttrIap_Restore(string request);
        public static bool Supported => true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaClass _java;
        static AndroidJavaClass Java => _java ?? (_java = new AndroidJavaClass("co.puttr.app2.PuttrIaphub"));
        static AndroidJavaObject Activity() { using (var p = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) return p.GetStatic<AndroidJavaObject>("currentActivity"); }
        static void PuttrIap_Start(string appId, string apiKey, string target) => Java.CallStatic("start", Activity(), appId, apiKey, target);
        static void PuttrIap_Login(string userId, string request) => Java.CallStatic("login", userId, request);
        static void PuttrIap_Logout() => Java.CallStatic("logout");
        static void PuttrIap_Products(string request) => Java.CallStatic("products", request);
        static void PuttrIap_Buy(string sku, string request) => Java.CallStatic("buy", Activity(), sku, request);
        static void PuttrIap_Restore(string request) => Java.CallStatic("restore", request);
        public static bool Supported => true;
#else
        static void PuttrIap_Start(string appId, string apiKey, string target) { }
        static void PuttrIap_Login(string userId, string request) { }
        static void PuttrIap_Logout() { }
        static void PuttrIap_Products(string request) { }
        static void PuttrIap_Buy(string sku, string request) { }
        static void PuttrIap_Restore(string request) { }
        public static bool Supported => false;
#endif
        const string ObjectName = "PuttrIap";
        static Iap _one;
        static int _next;
        static readonly Dictionary<string, Action<object, string>> _waiting = new Dictionary<string, Action<object, string>>();
        static bool _loggedIn;
        /// <summary>IAPHUB says the user changed: a purchase, renewal or restore landed. Re-read the profile.</summary>
        public static event Action Updated;

        static string Ask(Action<object, string> answer) { string id = (++_next).ToString(); _waiting[id] = answer; return id; }

        static void Ensure()
        {
            if (_one != null || !Supported) return;
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            _one = go.AddComponent<Iap>();
            PuttrIap_Start(AppId, ApiKey, ObjectName);
        }

        /// <summary>After sign-in: IAPHUB's user is the account owner (managedBy), else this Player.</summary>
        public static void Login(object profile)
        {
            if (!Supported) return;
            Ensure();
            string owner = J.Str(profile, "managedBy");
            string user = owner.Length > 0 ? owner : Auth.Uid;
            if (string.IsNullOrEmpty(user)) return;
            PuttrIap_Login(user, Ask((_, err) => { _loggedIn = err == null; if (err != null) Perf.Note("iaphub: login " + err); }));
        }

        public static void Logout() { if (Supported && _one != null) { PuttrIap_Logout(); _loggedIn = false; } }

        /// <summary>
        /// {productsForSale, activeProducts}; err is IAPHUB's code ("network_error", ...). Where the store has no plans to
        /// sell (the Mac, a simulator, a device with no store account) the plans come from IAPHUB's REST API instead, with
        /// "storeless": true so the page shows them and says purchasing is not available here.
        /// </summary>
        public static void Products(Action<object, string> done)
        {
            if (!Supported) { Catalog(done); return; }
            Ensure();
            PuttrIap_Products(Ask((data, err) =>
            {
                if (J.List(J.Get(data, "productsForSale")).Count > 0 || J.List(J.Get(data, "activeProducts")).Count > 0) { done(data, null); return; }
                if (err != null) Perf.Note("iaphub: products " + err);
                Catalog(done);
            }));
        }

        // The App Store's US prices (apps.apple.com Puttr listing, 2026-11-17), shown only where no store gives its own.
        static readonly Dictionary<string, double> ListPrice = new Dictionary<string, double>
        {
            { Monthly, 19.99 }, { Annual, 199.99 }, { ProMonthly, 39.99 }, { ProAnnual, 399.99 },
        };

        static string _anonymous;

        /// <summary>The IAPHUB user as the REST API sees it: the account owner, this Player, or an anonymous id.</summary>
        static string RestUser(object profile)
        {
            string owner = J.Str(profile, "managedBy");
            if (owner.Length > 0) return owner;
            if (!string.IsNullOrEmpty(Auth.Uid) && !Fixtures.On) return Auth.Uid;
            if (_anonymous == null) _anonymous = "a:" + Guid.NewGuid().ToString();
            return _anonymous;
        }

        /// <summary>The plans IAPHUB sells (GET /app/{app}/user/{user}), with list prices, for a device without a store.</summary>
        static void Catalog(Action<object, string> done)
        {
            if (string.IsNullOrEmpty(ApiKey)) { done(null, "unavailable"); return; }
            string platform = Application.platform == RuntimePlatform.Android ? "android" : "ios";
            string url = "https://api.iaphub.com/v1/app/" + AppId + "/user/" + Uri.EscapeDataString(RestUser(App.I != null ? App.I.Profile : null))
                + "?platform=" + platform + "&environment=production&context=products";
            App.Run(Fetch(url, (status, body) =>
            {
                object data = status == 200 ? J.Parse(body) : null;
                if (data == null || J.Get(data, "productsForSale") == null) { Perf.Note("iaphub: rest " + status); done(null, status == 0 ? "network_error" : "unavailable"); return; }
                var sale = new List<object>();
                foreach (object p in J.List(J.Get(data, "productsForSale")))
                {
                    string sku = J.Str(p, "sku");
                    if (!ListPrice.TryGetValue(sku, out double price)) continue;
                    sale.Add(new Dictionary<string, object>
                    {
                        { "sku", sku }, { "type", J.Str(p, "type") }, { "price", price },
                        { "localizedPrice", "$" + price.ToString("0.00", CultureInfo.InvariantCulture) }, { "currency", "USD" },
                        { "subscriptionPeriodType", J.Str(p, "subscriptionPeriodType") },
                    });
                }
                var active = new List<object>();
                foreach (object p in J.List(J.Get(data, "activeProducts")))
                {
                    // REST dates are ISO strings; the SDKs give milliseconds.
                    double ms = J.Num(p, "expirationDate");
                    if (ms <= 0 && DateTimeOffset.TryParse(J.Str(p, "expirationDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset when)) ms = when.ToUnixTimeMilliseconds();
                    active.Add(new Dictionary<string, object> { { "sku", J.Str(p, "sku") }, { "localizedTitle", J.Str(p, "localizedTitle", J.Str(p, "alias")) }, { "expirationDate", ms } });
                }
                done(new Dictionary<string, object> { { "productsForSale", sale }, { "activeProducts", active }, { "storeless", true } }, null);
            }));
        }

        static System.Collections.IEnumerator Fetch(string url, Action<long, string> done)
        {
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", "ApiKey " + ApiKey);
                req.timeout = Api.TimeoutSeconds;
                yield return req.SendWebRequest();
                long status = req.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode;
                done(status, req.downloadHandler != null ? req.downloadHandler.text : "");
            }
        }

        public static void Buy(string sku, Action<object, string> done) { if (!Supported) { done(null, "unavailable"); return; } Ensure(); PuttrIap_Buy(sku, Ask(done)); }
        public static void Restore(Action<object, string> done) { if (!Supported) { done(null, "unavailable"); return; } Ensure(); PuttrIap_Restore(Ask(done)); }

        // "<id>|ok|<json>", "<id>|error|<code>|<message>", or "update".
        void OnIap(string message)
        {
            if (message == "update") { Updated?.Invoke(); return; }
            string[] parts = message.Split(new[] { '|' }, 3);
            if (parts.Length < 2 || !_waiting.TryGetValue(parts[0], out Action<object, string> answer)) return;
            _waiting.Remove(parts[0]);
            string rest = parts.Length > 2 ? parts[2] : "";
            if (parts[1] == "ok") answer(rest.Length > 0 ? J.Parse(rest) : null, null);
            else answer(null, rest.Split('|')[0]);
        }

        /// <summary>What to tell the Player for a failed purchase. Cancelling says nothing.</summary>
        public static string Words(string code)
        {
            switch (code)
            {
                case "user_cancelled": return null;
                case "deferred_payment": return "Your purchase is waiting for approval. It turns on when it goes through.";
                case "product_already_purchased": return "You already have this plan. Try Restore Purchase.";
                case "network_error": return "No connection. Check your WiFi and try again.";
                case "billing_unavailable": case "unavailable": return "Purchases are not available on this device.";
                case "cross_platform_conflict": return "This membership was bought on another platform. Manage it there.";
                case "user_conflict": return "This purchase belongs to another Puttr account.";
                default: return "The purchase did not go through. Please try again.";
            }
        }
    }

    /// <summary>
    /// MembershipProducts (and, pro: true, ProMembershipProducts for coaches): the heading, Monthly and Annual as radio
    /// cards (Annual picked, BEST VALUE, its price per month), and Subscribe / Start Free Trial for the picked one.
    /// Styles in Resources/PuttrApp/Club.uss.
    /// </summary>
    public sealed class ClubPlans
    {
        public readonly VisualElement Root = Ui.Box("club-plans");
        readonly App _app;
        readonly bool _pro;
        readonly string _monthly, _annual;
        object _products;
        string _sku;
        bool _busy;

        public ClubPlans(App app, bool pro = false)
        {
            _app = app;
            _pro = pro;
            StyleSheet sheet = Resources.Load<StyleSheet>("PuttrApp/Club");
            if (sheet != null) Root.styleSheets.Add(sheet);
            _monthly = pro ? Iap.ProMonthly : Iap.Monthly;
            _annual = pro ? Iap.ProAnnual : Iap.Annual;
            _sku = _annual;
            Root.Add(Ui.Box("club-loading", Spinner.Small()));
            Load();
            Iap.Updated += Load;
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Iap.Updated -= Load);
        }

        void Load() => Iap.Products((data, err) => { _products = data; Draw(err); });

        static object Find(List<object> list, string sku) => list.Find(p => J.Str(p, "sku") == sku && (J.Str(p, "type") == "renewable_subscription" || J.Str(p, "type").Length == 0));

        void Draw(string err)
        {
            Root.Clear();
            if (_busy) { Root.Add(Ui.Box("club-loading", Spinner.Small(), Ui.Text("Confirming purchase", "club-sub", Ui.Medium).Margin(10))); return; }
            List<object> active = J.List(J.Get(_products, "activeProducts")).FindAll(p => _pro ? J.Str(p, "sku").Contains("pro_membership") : J.Str(p, "sku").StartsWith("membership"));
            if (active.Count > 0)
            {
                object a = active[0];
                double ms = J.Num(a, "expirationDate");
                string renews = ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToString("MMM dd, yyyy", CultureInfo.InvariantCulture) : "";
                Root.Add(Ui.Title(_pro ? "Pro membership active" : "Your membership is active", "title-20 club-title"));
                Root.Add(Ui.Text(J.Str(a, "localizedTitle") + (renews.Length > 0 ? "  \u00b7  Renews " + renews : ""), "club-sub", Ui.Regular));
                return;
            }
            List<object> sale = J.List(J.Get(_products, "productsForSale"));
            object monthly = Find(sale, _monthly), annual = Find(sale, _annual);
            Root.Add(Ui.Title(_pro ? "Puttr Pro for Coaches" : "Unlock your putting potential", "title-20 club-title"));
            Root.Add(Ui.Text(_pro ? "3D sessions, coaching tools, custom games & drills, ambassador program, merch discounts and more" : "AI coach, online play, tournaments, battle mode, sessions and advanced putting analysis", "club-sub club-lead", Ui.Regular));
            if (monthly == null && annual == null)
            {
                Root.Add(Ui.Text(err == "network_error" ? "No connection. Plans will show when you are online." : "Plans are not available right now. Please try again later.", "club-note", Ui.Regular));
                Root.Add(Ui.Button("Try again", () => { Root.Clear(); Root.Add(Ui.Box("club-loading", Spinner.Small())); Load(); }, "btn btn-ghost").Margin(12));
                return;
            }
            if (Find(sale, _sku) == null) _sku = annual != null ? _annual : _monthly;
            if (monthly != null) Root.Add(Plan(monthly, "Monthly", null, J.Str(monthly, "localizedPrice") + " / mo"));
            if (annual != null)
            {
                string local = J.Str(annual, "localizedPrice");
                double perMonth = J.Num(annual, "price") / 12.0;
                // The local currency's own symbol (the app took only the first character).
                string symbol = new string(Array.FindAll(local.ToCharArray(), c => !char.IsDigit(c) && c != '.' && c != ',' && !char.IsWhiteSpace(c)));
                Root.Add(Plan(annual, "Annual", local + "/year", symbol + perMonth.ToString("0.00", CultureInfo.InvariantCulture) + " / mo", "BEST VALUE"));
            }
            bool storeless = J.Flag(_products, "storeless");
            VisualElement buy = Ui.Button(_sku == _annual ? "Subscribe" : "Start Free Trial", Buy, "btn club-buy");
            buy.SetEnabled(!storeless);
            Root.Add(buy);
            if (storeless)
                Root.Add(Ui.Text(Iap.Supported
                    ? "Purchases are not available on this device. Sign in to the " + (Application.platform == RuntimePlatform.Android ? "Google Play Store" : "App Store") + " to subscribe."
                    : "Subscribe in the Puttr app on your iPhone, iPad or Android phone. Your membership works everywhere you sign in.", "club-note", Ui.Regular));
        }

        VisualElement Plan(object product, string label, string sub, string price, string badge = null)
        {
            string sku = J.Str(product, "sku");
            bool on = sku == _sku;
            VisualElement name = Ui.Box("club-plan-name", Ui.Text(label, "club-plan-label", Ui.Bold));
            if (badge != null) name.Add(Ui.Text(badge, "club-badge", Ui.Bold));
            VisualElement words = Ui.Box("club-plan-words", name);
            if (sub != null) words.Add(Ui.Text(sub, "club-plan-sub", Ui.Regular));
            return Ui.Box(on ? "club-plan club-plan-on" : "club-plan", Ui.Box("club-radio", on ? Ui.Box("club-radio-dot") : null), words, Ui.Num(price, "num-20 club-plan-price"))
                .OnTap(() => { if (_sku == sku) return; _sku = sku; Draw(null); });
        }

        void Buy()
        {
            if (_busy || J.Flag(_products, "storeless")) return;
            _busy = true;
            Draw(null);
            Analytics.Purchase("initiated", _sku);
            Iap.Buy(_sku, (data, err) =>
            {
                _busy = false;
                Analytics.Purchase(err != null ? "failed" : "succeeded", _sku, err);
                if (err != null) { string words = Iap.Words(err); if (words != null) _app.Toast(words); Draw(null); return; }
                _app.Toast(_pro ? "Welcome to Puttr Pro." : "Welcome to Puttr Club.");
                // IAPHUB posts the receipt and its webhook turns membership on; read the profile again (and again when IAPHUB says the user changed).
                App.Run(Api.Get("/profile/", d => { _app.Profile = J.First(d); if (Club.Member(_app.Profile) && _app.Top is ClubScreen) _app.Pop(); }));
                Load();
            });
        }
    }
}
