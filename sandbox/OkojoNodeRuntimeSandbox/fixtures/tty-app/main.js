const tty = require("node:tty");
const util = require("node:util");

process.stdout.write("stdout-write-ok\n");
process.stderr.write("stderr-write-ok\n");
process.stdout.cursorTo(2);
process.stdout.moveCursor(1, 0);
process.stdout.clearLine(0);
process.stdout.clearScreenDown();

module.exports = [
  typeof process.stdout.write,
  typeof process.stdout.cursorTo,
  util.format("%s:%d", "tty", process.stdout.columns),
  process.stdout.fd,
  process.stderr.fd,
  tty.isatty(1),
  process.stdout.isTTY,
  process.stdout.columns,
  process.stdout.rows
].join("|");
