console.log('math: module loaded');

export function multiply(left: number, right: number): number {
  const product: number = left * right;
  console.log(`math: multiply(${left}, ${right}) = ${product}`);
  debugger;
  return product;
}
