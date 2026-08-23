function outer(value) {
    let expression = add => this.base + value + add + arguments[0];
    let block = (left, right) => { return left * right; };
    let patterns = ({ item: [first = 1, ...rest] }, ...tail) =>
        first + rest.length + tail.length;
    return expression.call({ base: 100 }, 2) + block(3, 4)
        + patterns({ item: [3, 4, 5] }, 6, 7);
}
outer.call({ base: 10 }, 20);

function captureNewTarget() {
    const read = () => new.target;
    return read();
}

captureNewTarget();
new captureNewTarget();
