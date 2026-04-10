function privateAccessorHot() {
    class Counter {
        #x = 0;

        get #v() {
            return this.#x;
        }

        set #v(value) {
            this.#x = value;
        }

        step() {
            this.#v = this.#v + 1;
            return this.#v;
        }
    }

    var c = new Counter();
    var sum = 0;
    for (var i = 0; i < 200000; i++) {
        sum = sum + c.step();
    }

    return sum;
}

privateAccessorHot;

