function run() {
    let sum = 0;
    for (let i = 0; i < 200; i++) {
        Promise.any([1, 2, 3, 4]).then(value => {
            sum += value;
        });
    }

    globalThis.out = 0;
    Promise.resolve().then(() => {
        globalThis.out = sum;
    });
}

run;
