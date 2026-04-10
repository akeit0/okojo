function objectScenario() {
    for (var i = 0; i < 200; i++) {
        var o = {a: 3, b: 4};
        o.c = 5;
        var v = o.a + o["b"] + o.c;
    }
    var o = {a: 3, b: 4};
    o.c = 5;
    return o.a + o["b"] + o.c;
}

objectScenario;
