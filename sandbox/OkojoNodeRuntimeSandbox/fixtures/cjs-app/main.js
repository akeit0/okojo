const path = require("node:path");
const pkg = require("pkg");
const bytes = Buffer.from("ABC");

module.exports = [
  "cjs",
  pkg.label,
  path.basename(__filename),
  bytes.length,
  bytes[0],
  process.cwd()
].join("|");
