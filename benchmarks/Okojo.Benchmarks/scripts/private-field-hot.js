function privateFieldHot() {
    class Counter {
        #x = 1;

        inc() {
            this.#x = this.#x + 1;
        }

        read() {
            return this.#x;
        }
    }

    var c = new Counter();
    var sum = 0;
    for (var i = 0; i < 200000; i++) {
        c.inc();
        sum = sum + c.read();
    }

    return sum;
}

privateFieldHot;

