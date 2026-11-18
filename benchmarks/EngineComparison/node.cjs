const fs = require("node:fs");
const vm = require("node:vm");

const path = process.argv[2];
console.log(vm.runInNewContext(fs.readFileSync(path, "utf8"), vm.constants.DONT_CONTEXTIFY, {
    filename: path,
}));
