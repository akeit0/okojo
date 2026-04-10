globalThis.workerTrace = [];
globalThis.worker = new Worker("/browser/worker.js");
worker.onmessage = function (e) {
    workerTrace.push(e.data);
};
worker.postMessage("manual:a");
worker.postMessage("manual:b");
