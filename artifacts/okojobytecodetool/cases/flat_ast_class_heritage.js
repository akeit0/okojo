class Base {
    constructor(value) {
        this.value = value;
    }
}

class Derived extends Base {
    constructor(value) {
        super(value);
        this.ready = true;
    }
}

new Derived(3).value;
