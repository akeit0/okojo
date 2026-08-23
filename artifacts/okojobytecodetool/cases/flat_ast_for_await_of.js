async function collect(iterable) {
    let total = 0;
    for await (const value of iterable) {
        total += value;
    }
    return total;
}

collect([Promise.resolve(1), 2]);
