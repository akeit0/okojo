function add1_hoisted(x) {
    return x + 1;
}

function add2_hoisted(x) {
    return add1_hoisted(add1_hoisted(x));
}

function add3_hoisted(x) {
    return add2_hoisted(add1_hoisted(x));
}

function functionCallHoisted() {
    var s = 0;
    for (var i = 0; i < 100; i++) {
        s = add3_hoisted(s);
    }

    return s;
}

functionCallHoisted;
