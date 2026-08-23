function outer(value) {
    let expression = add => this.base + value + add + arguments[0];
    let block = (left, right) => { return left * right; };
    return expression.call({ base: 100 }, 2) + block(3, 4);
}
outer.call({ base: 10 }, 20);
