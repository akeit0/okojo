function makePrototypeGet() {
    const prototype = { value: 1 };
    const object = Object.create(prototype);

    return function prototypeGet() {
        let sum = 0;
        for (let i = 0; i < 100000; i++)
            sum += object.value;
        return sum;
    };
}

makePrototypeGet();
