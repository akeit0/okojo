function optional(object, key, argument) {
    return [
        object?.value,
        object?.[key()],
        object?.method(argument()),
        object.method?.(argument()),
        delete object?.value
    ];
}

optional(null, () => 'value', () => 1);
