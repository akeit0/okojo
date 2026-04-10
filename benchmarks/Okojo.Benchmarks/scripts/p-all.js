function run() {
    let sum = 0;
    for (let i = 0; i < 200; i++) {
        Promise.all([1, 2, 3, 4]).then(values => {
            sum += values[0] + values[1] + values[2] + values[3];
        });
    }

    globalThis.out = 0;
    Promise.resolve().then(() => {
        globalThis.out = sum;
    });
}

run;
