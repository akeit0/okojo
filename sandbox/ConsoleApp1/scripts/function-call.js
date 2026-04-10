function functionCall() {
    function add1(x) {
        return x + 1;
    }

    function add2(x) {
        return add1(add1(x));
    }

    function add3(x) {
        return add2(add1(x));
    }

    var s = 0;
    for (var i = 0; i < 100; ++i) {
        s = add3(s);
    }

    return s;
}

functionCall;
