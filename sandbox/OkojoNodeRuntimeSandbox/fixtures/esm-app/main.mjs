import path from "node:path";
import process from "node:process";
import value from "pkg";

export default [
  "esm",
  value,
  path.basename("/sandbox/esm-app/main.mjs"),
  process.platform,
  typeof process.versions.node
].join("|");
