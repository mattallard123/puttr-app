//
//  IHBillingStatus.swift
//  Iaphub
//
//  Created by iaphub on 1/7/23.
//  Copyright © 2023 iaphub. All rights reserved.
//

import Foundation

@objc(IHBillingStatus) public class IHBillingStatus: NSObject {

   // Error
   @objc public var error: IHError?
   // Filtered product ids
   @objc public var filteredProductIds: [String]

   init(error: IHError? = nil, filteredProductIds: [String] = []) {
      self.error = error
      self.filteredProductIds = filteredProductIds
   }
   
   public func getDictionary() -> [String: Any] {
      return [
         "error": self.error?.getDictionary() as Any,
         "filteredProductIds": self.filteredProductIds as Any
      ]
   }

}
