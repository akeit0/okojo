const stream = require("node:stream");

const source = new stream.PassThrough();
const sink = new stream.PassThrough();

let chunks = "";
sink.on("data", chunk => {
  chunks += chunk.toString();
});

stream.pipeline(source, sink);
source.write("hi");
source.end("!");

module.exports = [
  typeof stream.PassThrough,
  typeof stream.pipeline,
  chunks,
  sink.writableEnded,
  sink.destroyed
].join("|");
