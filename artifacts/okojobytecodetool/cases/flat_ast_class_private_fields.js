class Box {
    #value = 1;
    static #count = 2;

    read() {
        return this.#value;
    }

    static count() {
        return this.#count;
    }

    has(value) {
        return #value in value;
    }
}

let box = new Box();
box.read() + "|" + Box.count() + "|" + box.has(box);
