const boxes = require("./boxes.json");
module.exports = [
  typeof boxes,
  Object.keys(boxes).join(","),
  JSON.stringify(boxes),
  boxes.foo,
  boxes.answer
].join("|");
