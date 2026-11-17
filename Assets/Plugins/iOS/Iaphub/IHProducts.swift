//
//  IHProducts.swift
//  Iaphub
//
//  Created by iaphub on 12/12/22.
//  Copyright © 2022 iaphub. All rights reserved.
//

import Foundation

@objc(IHProducts) public class IHProducts: NSObject {

   // Active products
   @objc public var activeProducts: [IHActiveProduct]
   // Products for sale
   @objc public var productsForSale: [IHProduct]

   init(activeProducts: [IHActiveProduct], productsForSale: [IHProduct]) {
      self.activeProducts = activeProducts
      self.productsForSale = productsForSale
   }
   
   public func getDictionary() -> [String: Any] {
      return [
         "activeProducts": self.activeProducts.map({(item) in item.getDictionary()}) as Any,
         "productsForSale": self.productsForSale.map({(item) in item.getDictionary()}) as Any
      ]
   }

}
