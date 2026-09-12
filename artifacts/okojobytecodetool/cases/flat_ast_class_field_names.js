class Names {
    field = function () {};
    #privateField = class {};
    static staticField = function () {};
    static #staticPrivateField = class {};

    names() {
        return this.field.name + "|" + this.#privateField.name;
    }

    static names() {
        return this.staticField.name + "|" + this.#staticPrivateField.name;
    }
}

new Names().names() + "|" + Names.names();
