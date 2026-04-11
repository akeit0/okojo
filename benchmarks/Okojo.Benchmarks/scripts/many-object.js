function manyObject() {
    var s = 0;
    for (var i = 0; i < 3000; i++) {
        var o = {a: i, b: i + 1, c: i + 2, d: i + 3};
        s = s + o.a + o.b + o.c + o.d;
    }

    return s;
}

manyObject;
