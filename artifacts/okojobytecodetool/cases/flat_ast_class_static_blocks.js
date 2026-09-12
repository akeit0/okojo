let order = [];

class Base {
    static value = 2;
}

class Derived extends Base {
    static before = (order.push("before"), 1);

    static {
        order.push("block");
        this.result = super.value + this.before;
    }

    static after = (order.push("after"), this.result + 1);
}

Derived.after + "|" + order.join(",");
