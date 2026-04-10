function indexDescriptor() {
    var o = {};
    Object.defineProperty(o, "100000", {
        value: 3,
        writable: true,
        enumerable: true,
        configurable: true
    });

    var sum = 0;
    for (var i = 0; i < 200000; i++) {
        sum = sum + o[100000];
        o[100000] = (sum % 17) + 1;
    }

    return sum;
}

indexDescriptor;

