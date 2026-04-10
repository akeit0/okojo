function awaitBench() {
    globalThis.out = 0;
    (async function () {
        let s = 0;
        for (let i = 0; i < 100; i++) {
            s += await 1;
        }
        globalThis.out = s;
        return s;
    })();
    return globalThis.out;
}

awaitBench;
