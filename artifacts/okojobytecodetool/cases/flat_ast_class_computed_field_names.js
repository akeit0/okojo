let instanceKey = "instance";
let staticKey = "staticValue";

class Names {
    [instanceKey] = function () {};
    static [staticKey] = class {
        static observed = this.name;
    };
}

let value = new Names();
value.instance.name + "|" + Names.staticValue.name + "|" + Names.staticValue.observed;
