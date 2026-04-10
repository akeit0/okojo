(() => {
    const N = 1000;

    return function () {
        out = 0;

        for (let i = 0; i < N; i++) {
            let iter;

            async function* g() {
                iter.return(42).then(function (result) {
                    out += result.value;
                });

                yield 1;
            }

            iter = g();
            iter.next().then(function (result) {
                out += result.value;
                iter.next().then(function (result2) {
                    if (result2.done) {
                        out += 1;
                    }
                });
            });
        }

        return 0;
    };
})()
