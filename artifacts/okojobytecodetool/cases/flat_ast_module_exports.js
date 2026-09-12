export const value = 1;
export function read() { return value; }
export { value as renamed };
export { source as forwarded } from "dependency" with { type: "json" };
export * as namespaceValue from "namespace";
export * from "star";
export default class { static observed = this.name; }
