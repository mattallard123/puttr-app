// Paying for a lesson on iOS: Stripe's PaymentSheet, as puttr-app's BookLesson.js (stripe-react-native,
// stripe-ios 26.3.0, added as a Swift package by Editor/IosFirebase.cs). Apple Pay through merchant.co.puttr.puttrapp.
// The answer goes to Unity as "<id>|ok", "<id>|cancelled" or "<id>|error|<message>" (UnitySendMessage(target, "OnPay")).
import Foundation
import UIKit
import StripePaymentSheet

@_silgen_name("UnitySendMessage")
func PuttrPay_UnitySend(_ object: UnsafePointer<CChar>, _ method: UnsafePointer<CChar>, _ message: UnsafePointer<CChar>)

@_silgen_name("UnityGetGLViewController")
func PuttrPay_RootController() -> UIViewController?

private var puttrPaySheet: PaymentSheet?

private func puttrPayAnswer(_ target: String, _ message: String) {
    DispatchQueue.main.async { PuttrPay_UnitySend(target, "OnPay", message) }
}

@_cdecl("PuttrPay_Present")
public func PuttrPay_Present(_ publishableKey: UnsafePointer<CChar>, _ clientSecret: UnsafePointer<CChar>, _ customer: UnsafePointer<CChar>,
                             _ ephemeralKey: UnsafePointer<CChar>, _ target: UnsafePointer<CChar>, _ request: UnsafePointer<CChar>) {
    let key = String(cString: publishableKey), secret = String(cString: clientSecret)
    let cust = String(cString: customer), eph = String(cString: ephemeralKey)
    let to = String(cString: target), id = String(cString: request)
    DispatchQueue.main.async {
        STPAPIClient.shared.publishableKey = key
        var config = PaymentSheet.Configuration()
        config.merchantDisplayName = "Puttr, Inc."
        if !cust.isEmpty && !eph.isEmpty { config.customer = .init(id: cust, ephemeralKeySecret: eph) }
        config.applePay = .init(merchantId: "merchant.co.puttr.puttrapp", merchantCountryCode: "US")
        config.style = .alwaysDark
        let sheet = PaymentSheet(paymentIntentClientSecret: secret, configuration: config)
        puttrPaySheet = sheet
        guard var top = PuttrPay_RootController() else { puttrPayAnswer(to, id + "|error|No screen to show the payment on"); return }
        while let shown = top.presentedViewController { top = shown }
        sheet.present(from: top) { result in
            puttrPaySheet = nil
            switch result {
            case .completed: puttrPayAnswer(to, id + "|ok")
            case .canceled: puttrPayAnswer(to, id + "|cancelled")
            case .failed(let error): puttrPayAnswer(to, id + "|error|" + error.localizedDescription.replacingOccurrences(of: "|", with: " "))
            }
        }
    }
}
