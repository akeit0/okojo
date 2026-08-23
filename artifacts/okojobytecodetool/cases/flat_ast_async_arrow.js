const simple = async value => await value;
const advanced = async (value = 1, ...rest) => value + await rest[0];

simple(Promise.resolve(1));
advanced(undefined, Promise.resolve(2));
