let order = [];

class Base {
    static inherited = 4;
}

class Derived extends Base {
    static first = (order.push("first"), 1);
    static [(order.push("key"), "computed")] =
        (order.push("value"), super.inherited + this.first);
    static empty;
}

Derived.computed + "|" + order.join(",");
