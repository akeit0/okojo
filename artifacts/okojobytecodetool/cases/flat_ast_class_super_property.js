class Base {
    get value() {
        return this._value;
    }

    set value(next) {
        this._value = next;
    }

    read() {
        return this._value;
    }

    static identify() {
        return this.name;
    }
}

class Derived extends Base {
    readSuper(next) {
        super.value = next;
        return super.read() + super.value;
    }

    static identifySuper() {
        return super.identify();
    }
}

new Derived().readSuper(3);
