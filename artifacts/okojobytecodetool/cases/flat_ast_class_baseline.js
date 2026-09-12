class Counter {
    constructor(value) {
        this.value = value;
    }

    increment(step = 1) {
        return this.value += step;
    }

    static create(value) {
        return new Counter(value);
    }
}

Counter.create(2).increment();
