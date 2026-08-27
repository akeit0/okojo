function dateSubtract() {
    const left = new Date(123456789);
    const right = new Date(123456);
    let sum = 0;
    for (let i = 0; i < 200000; i++)
        sum = sum + (left - right);

    return sum;
}

dateSubtract;
