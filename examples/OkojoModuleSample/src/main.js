import * as shop from "./index.js";

export function runDemo() {
  const before = shop.runCount;
  shop.bumpRuns();
  shop.addItem("notebook", 2);
  shop.addItem("pen", 3);
  return {
    shopName: shop.shopName,
    runCountBefore: before,
    runCountAfter: shop.runCount,
    roundedSample: shop["round#2"](12.34),
    catalogHasNotebook: !!shop.catalogNS.getProduct("notebook"),
    summary: shop.getSummary(),
    totalText: shop.formatMoney(shop.cartTotal),
    totalValue: shop.cartTotal,
    runsViaDefault: shop.getRunsDefault()
  };
}
