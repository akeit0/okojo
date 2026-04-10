function awaitBench() {
    async function identityAsync(x) {
        return x;
    }

    (async function () {
        let f = identityAsync;
        for (let i = 0; i < 100; i++) {
            await f(i);
        }
    })();
}

awaitBench;
