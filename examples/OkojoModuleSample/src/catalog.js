export const products = {
  notebook: { name: "Notebook", price: 4.25 },
  pen: { name: "Pen", price: 1.75 },
  bag: { name: "Bag", price: 18.0 }
};

export function getProduct(sku) {
  return products[sku];
}
