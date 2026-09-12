function denseKeyed() {
    const array = [];
    for (let i = 0; i < 1024; i++)
        array[i] = i + 1;

    let sum = 0;
    for (let i = 0; i < 200000; i++)
        sum = sum + array[i % 1023];

    return sum;
}

denseKeyed;
