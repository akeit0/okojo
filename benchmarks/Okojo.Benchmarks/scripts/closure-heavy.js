function closureHeavy() {
    function makeAdder(base) {
        return function add(v) {
            return base + v;
        };
    }

    var add1 = makeAdder(1);
    var add2 = makeAdder(2);
    var add3 = makeAdder(3);
    var s = 0;

    for (var i = 0; i < 100; i++) {
        s = add3(add2(add1(s)));
    }

    return s;
}

closureHeavy;
