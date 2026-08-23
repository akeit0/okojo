let keys = 0;

class Base {
    [(keys++, "value")] = 1;

    read() {
        return this.value;
    }
}

class Derived extends Base {
    result = super.read() + 1;

    constructor() {
        super();
    }
}

new Derived().result + "|" + keys;
