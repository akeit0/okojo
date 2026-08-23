function labels(values) {
    let result = '';
    outer: for (const value of values) {
        inner: {
            if (value === 2) continue outer;
            if (value === 3) break outer;
            break inner;
        }
        result += value;
    }
    return result;
}

labels([1, 2, 3, 4]);
