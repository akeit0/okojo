let key = 'computed';
let object = {
    base: 2,
    method(value) { return this.base + value; },
    [key](value) { return this.base * value; },
    get value() { return this.base; },
    set value(value) { this.base = value; }
};
object.value = 4;
object.method(1) + object.computed(2) + object.value;
