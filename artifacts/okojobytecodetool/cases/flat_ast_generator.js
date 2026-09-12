function* flatGenerator(value) {
    const sent = yield value;
    return sent + 1;
}

const iterator = flatGenerator(3);
iterator.next();
iterator.next(4);
