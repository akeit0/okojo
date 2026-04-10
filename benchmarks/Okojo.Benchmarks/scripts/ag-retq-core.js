(() => {
    const N = 1000;
    let iter;
    let promises;

    async function* g() {
        promises.push(iter.return(42));
        yield 1;
    }

    return function () {
        promises = [];

        for (let i = 0; i < N; i++) {
            iter = g();
            promises.push(iter.next());
            promises.push(iter.next());
        }

        out = promises;
        return 0;
    };
})()
