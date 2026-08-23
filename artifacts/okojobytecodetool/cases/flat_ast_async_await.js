async function flatAsync(value) {
    const resolved = await value;
    return resolved + 1;
}

flatAsync(Promise.resolve(1));
