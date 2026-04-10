const { EventEmitter } = require("node:events");

globalThis.status = "start";

const emitter = new EventEmitter();
emitter.once("ready", value => {
  globalThis.status = value;
});

process.nextTick(() => {
  emitter.emit("ready", "tick-ready");
});

module.exports = "queued";
