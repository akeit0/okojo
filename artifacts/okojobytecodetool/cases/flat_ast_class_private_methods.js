class Box {
    #value = 1;

    #method() {
        return this.#value;
    }

    get #accessor() {
        return this.#value;
    }

    set #accessor(value) {
        this.#value = value;
    }

    static #staticMethod() {
        return this;
    }

    read() {
        return this.#method() + this.#accessor;
    }

    write(value) {
        this.#accessor = value;
    }

    static read() {
        return this.#staticMethod() === this;
    }
}

let box = new Box();
box.write(2);
box.read() + "|" + Box.read();
