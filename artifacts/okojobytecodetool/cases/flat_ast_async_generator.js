async function* sequence(value) {
    yield await value;
    return Promise.resolve(2);
}

sequence(Promise.resolve(1));
