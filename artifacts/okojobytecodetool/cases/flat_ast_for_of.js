function values(iterable) {
    let result = '';
    for (const [value] of iterable) {
        if (value === 2) continue;
        result += value;
        if (value === 3) break;
    }
    return result;
}

values([[1], [2], [3], [4]]);
