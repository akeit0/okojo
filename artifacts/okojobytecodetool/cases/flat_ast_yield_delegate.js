function* flatYieldDelegate(iterable) {
    return yield* iterable;
}

const iterator = flatYieldDelegate([1, 2]);
iterator.next();
iterator.next();
iterator.next();
