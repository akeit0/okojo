function awaitBench() {
    globalThis.out = 0;

    async function identityAsync(x) {
        return x;
    }

    (async function () {
        let s = 0;
        let f = identityAsync;
        for (let i = 0; i < 100; i++) {
            s += await f(i);
        }
        globalThis.out = s;
    })();
    return globalThis.out;
}

awaitBench;
