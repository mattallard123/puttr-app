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
        const string AppId = "60abc90ec85aaa314c8b288d", ApiKey = "Woi17nZkTW0PxEsm7U9JmkzqWFIaDL9";
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

        /// <summary>{productsForSale, activeProducts}; err is IAPHUB's code ("network_error", ...).</summary>
        public static void Products(Action<object, string> done) { if (!Supported) { done(null, "unavailable"); return; } Ensure(); PuttrIap_Products(Ask(done)); }
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
                case "billing_unavailable": return "Purchases are not available on this device.";
                case "cross_platform_conflict": return "This membership was bought on another platform. Manage it there.";
                case "user_conflict": return "This purchase belongs to another Puttr account.";
                default: return "The purchase did not go through. Please try again.";
            }
        }
    }

    /// <summary>MembershipProducts (and, pro: true, ProMembershipProducts for coaches): Monthly and Annual, the selected one bought with a tap.</summary>
    public sealed class ClubPlans
    {
        public readonly VisualElement Root = Ui.Box("glass");
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
            _monthly = pro ? Iap.ProMonthly : Iap.Monthly;
            _annual = pro ? Iap.ProAnnual : Iap.Annual;
            _sku = _annual;
            Root.Add(Spinner.Small());
            Load();
            Iap.Updated += Load;
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Iap.Updated -= Load);
        }

        void Load()
        {
            if (!Iap.Supported) { Root.Clear(); Root.Add(Ui.Text("Join in the Puttr app on your phone. This account is a member everywhere.", "sub", Ui.Regular)); return; }
            Iap.Products((data, err) => { _products = data; Draw(err); });
        }

        static object Find(List<object> list, string sku) => list.Find(p => J.Str(p, "sku") == sku && (J.Str(p, "type") == "renewable_subscription" || J.Str(p, "type").Length == 0));

        void Draw(string err)
        {
            Root.Clear();
            List<object> active = J.List(J.Get(_products, "activeProducts")).FindAll(p => _pro ? J.Str(p, "sku").Contains("pro_membership") : J.Str(p, "sku").StartsWith("membership"));
            if (active.Count > 0)
            {
                object a = active[0];
                double ms = J.Num(a, "expirationDate");
                string renews = ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToString("MMM dd, yyyy", CultureInfo.InvariantCulture) : "";
                Root.Add(Ui.Text(_pro ? "Pro membership active" : "Your membership is active", "h2", Ui.Bold));
                Root.Add(Ui.Text(J.Str(a, "localizedTitle") + (renews.Length > 0 ? "   ·   Renews " + renews : ""), "sub", Ui.Regular).Margin(4));
                return;
            }
            List<object> sale = J.List(J.Get(_products, "productsForSale"));
            object monthly = Find(sale, _monthly), annual = Find(sale, _annual);
            Root.Add(Ui.Text(_pro ? "Puttr Pro for Coaches" : "Unlock your putting potential", "h2", Ui.Bold));
            Root.Add(Ui.Text(_pro ? "3D sessions, coaching tools, custom games & drills, ambassador program, merch discounts and more" : "AI coach, online play, tournaments, battle mode, sessions and advanced putting analysis", "sub", Ui.Regular).Margin(4));
            if (monthly == null && annual == null) { Root.Add(Ui.Text(err == "network_error" ? "No connection. Plans will show when you are online." : "Plans are not available right now.", "muted", Ui.Regular).Margin(10)); return; }
            if (monthly != null) Root.Add(Plan(monthly, "Monthly", null, J.Str(monthly, "localizedPrice") + " / mo"));
            if (annual != null)
            {
                string local = J.Str(annual, "localizedPrice");
                double perMonth = J.Num(annual, "price") / 12.0;
                // The local currency's own symbol (the app took only the first character).
                string symbol = new string(Array.FindAll(local.ToCharArray(), c => !char.IsDigit(c) && c != '.' && c != ',' && !char.IsWhiteSpace(c)));
                Root.Add(Plan(annual, "Annual", local + "/year", symbol + perMonth.ToString("0.00", CultureInfo.InvariantCulture) + " / mo", "BEST VALUE"));
            }
            VisualElement buy = Ui.Button(_busy ? "Confirming purchase..." : _sku == _annual ? "Subscribe" : "Start Free Trial", Buy);
            buy.SetEnabled(!_busy);
            Root.Add(buy.Margin(12));
        }

        VisualElement Plan(object product, string label, string sub, string price, string badge = null)
        {
            string sku = J.Str(product, "sku");
            bool on = sku == _sku;
            VisualElement words = Ui.Box("grow set-words", Ui.Box("row", Ui.Text(label, "feed-name", Ui.Bold), badge != null ? Ui.Text(badge, "chip chip-green", Ui.Bold) : null));
            if (sub != null) words.Add(Ui.Text(sub, "muted", Ui.Regular));
            return Ui.Box(on ? "plan plan-on" : "plan", Ui.Box(on ? "plan-dot plan-dot-on" : "plan-dot"), words, Ui.Text(price, "feed-name", Ui.Bold)).OnTap(() => { _sku = sku; Draw(null); });
        }

        void Buy()
        {
            if (_busy) return;
            _busy = true;
            Draw(null);
            Iap.Buy(_sku, (data, err) =>
            {
                _busy = false;
                if (err != null) { string words = Iap.Words(err); if (words != null) _app.Toast(words); Draw(null); return; }
                _app.Toast(_pro ? "Welcome to Puttr Pro." : "Welcome to Puttr Club.");
                // The webhook turns membership on; read the profile again (and again when IAPHUB says the user changed).
                App.Run(Api.Get("/profile/", d => { _app.Profile = J.First(d); if (Club.Member(_app.Profile) && _app.Top is ClubScreen) _app.Pop(); }));
                Load();
            });
        }
    }
}
