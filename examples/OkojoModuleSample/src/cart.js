import * as catalog from "./catalog.js";
import formatMoney, { round2 } from "./money.js";

export let cartTotal = 0;
const lines = [];

export function addItem(sku, qty) {
  const item = catalog.getProduct(sku);
  if (!item) {
    throw new Error("Unknown sku: " + sku);
  }

  const lineTotal = round2(item.price * qty);
  cartTotal = round2(cartTotal + lineTotal);
  lines.push(item.name + " x " + qty + " = " + formatMoney(lineTotal));
}

export function getSummary() {
  return lines.join(", ");
}
