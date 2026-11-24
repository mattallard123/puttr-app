// IAPHUB on iOS (iaphub-ios-sdk 4.6.7, the version react-native-iaphub pulls in, vendored in Plugins/iOS/Iaphub): the Puttr Club
// subscriptions. Same app id and key as puttr-app; the IAPHUB user is the Firebase uid (the account
// owner's for a managed profile), so the API's webhook finds the right profile.
//
// Answers go to Unity: UnitySendMessage(target, "OnIap", "<id>|ok|<json>") or "<id>|error|<code>|<message>",
// and "update" when IAPHUB says the user changed.
// sdk and sdkVersion stay empty: IAPHUB only knows its own wrappers (react_native, flutter, cordova); "ios/unity"
// came back {"error":"library_name_invalid"} on every request, which the SDK reports as network_error.
import Foundation

@_silgen_name("UnitySendMessage")
func PuttrIapUnitySendMessage(_ object: UnsafePointer<CChar>, _ method: UnsafePointer<CChar>, _ message: UnsafePointer<CChar>)

final class PuttrIaphub: NSObject, IaphubDelegate {
    static let shared = PuttrIaphub()
    var target = ""
    var started = false

    func send(_ message: String) {
        let to = target
        DispatchQueue.main.async { PuttrIapUnitySendMessage(to, "OnIap", message) }
    }

    static func json(_ value: Any?) -> String {
        guard let value = value, JSONSerialization.isValidJSONObject(value), let data = try? JSONSerialization.data(withJSONObject: value) else { return "" }
        return String(data: data, encoding: .utf8) ?? ""
    }

    func answer(_ request: String, _ err: IHError?, _ value: Any?) {
        if let err = err { send("\(request)|error|\(err.code)|\(err.message.replacingOccurrences(of: "|", with: " "))"); return }
        send("\(request)|ok|\(PuttrIaphub.json(value))")
    }

    func didReceiveUserUpdate() { send("update") }
    func didReceiveError(err: IHError) { }
}

// JSON-safe dictionaries: dates become milliseconds, as on Android.
private func clean(_ v: Any) -> Any {
    if let d = v as? [String: Any] { var o: [String: Any] = [:]; for (k, x) in d { o[k] = clean(x) }; return o }
    if let a = v as? [Any] { return a.map(clean) }
    if let date = v as? Date { return Int64(date.timeIntervalSince1970 * 1000) }
    return v
}

@_cdecl("PuttrIap_Start")
public func PuttrIap_Start(_ appId: UnsafePointer<CChar>, _ apiKey: UnsafePointer<CChar>, _ target: UnsafePointer<CChar>) {
    let app = String(cString: appId), key = String(cString: apiKey), to = String(cString: target)
    DispatchQueue.main.async {
        let me = PuttrIaphub.shared
        me.target = to
        if me.started { return }
        me.started = true
        Iaphub.delegate = me
        Iaphub.start(appId: app, apiKey: key, userId: nil, allowAnonymousPurchase: false, enableDeferredPurchaseListener: true, enableStorekitV2: false, environment: "production", lang: "", sdk: "", sdkVersion: "")
    }
}

@_cdecl("PuttrIap_Login")
public func PuttrIap_Login(_ userId: UnsafePointer<CChar>, _ request: UnsafePointer<CChar>) {
    let user = String(cString: userId), req = String(cString: request)
    DispatchQueue.main.async { Iaphub.login(userId: user, { err in PuttrIaphub.shared.answer(req, err, nil) }) }
}

@_cdecl("PuttrIap_Logout")
public func PuttrIap_Logout() { DispatchQueue.main.async { Iaphub.logout() } }

@_cdecl("PuttrIap_Products")
public func PuttrIap_Products(_ request: UnsafePointer<CChar>) {
    let req = String(cString: request)
    DispatchQueue.main.async {
        Iaphub.getProducts(includeSubscriptionStates: [], { err, products in
            guard let products = products else { PuttrIaphub.shared.answer(req, err, nil); return }
            PuttrIaphub.shared.answer(req, err, clean(products.getDictionary()))
        })
    }
}

@_cdecl("PuttrIap_Buy")
public func PuttrIap_Buy(_ sku: UnsafePointer<CChar>, _ request: UnsafePointer<CChar>) {
    let s = String(cString: sku), req = String(cString: request)
    DispatchQueue.main.async {
        Iaphub.buy(sku: s, crossPlatformConflict: true, { err, transaction in PuttrIaphub.shared.answer(req, err, transaction.map { clean($0.getDictionary()) }) })
    }
}

@_cdecl("PuttrIap_Restore")
public func PuttrIap_Restore(_ request: UnsafePointer<CChar>) {
    let req = String(cString: request)
    DispatchQueue.main.async { Iaphub.restore({ err, _ in PuttrIaphub.shared.answer(req, err, nil) }) }
}
