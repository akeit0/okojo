function awaitBench() {
    globalThis.out = 0;
    (async function () {
        let s = 0;
        for (let i = 0; i < 100; i++) {
            s += await new Promise(function (resolve) {
                Promise.resolve(1).then(function (v) {
                    resolve(v);
                });
            });
        }
        globalThis.out = s;
    })();
    return globalThis.out;
}

awaitBench;
