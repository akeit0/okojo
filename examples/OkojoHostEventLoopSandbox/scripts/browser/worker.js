onmessage = function (e) {
    postMessage("echo:" + e.data);
};
